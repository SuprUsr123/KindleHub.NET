using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace KindleHub.Client.Converters;

/// <summary>
/// Turns a "data:image/...;base64,..." URI (as carried by the official KHIMG1 chat marker)
/// into an Avalonia Bitmap so a chat row can render the photo. Decode failures (or non-data
/// input) silently yield null, which hides the image control. Decoded bitmaps are cached
/// against the source URI (bounded) to survive list virtualization and re-polling.
/// </summary>
public sealed class DataImageToBitmapConverter : IValueConverter
{
    private const int MaxCached = 120;
    private static readonly Dictionary<string, WeakReference<Bitmap>> Cache = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string { Length: > 11 } uri) return null;
        if (!uri.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return null;

        lock (Gate)
        {
            if (Cache.TryGetValue(uri, out var cached) && cached.TryGetTarget(out var live))
                return live;
        }

        Bitmap? bmp = null;
        try
        {
            int comma = uri.IndexOf(',');
            if (comma < 0 || comma == uri.Length - 1) return null;
            var bytes = System.Convert.FromBase64String(uri.Substring(comma + 1));
            if (bytes is { Length: > 0 and <= 20_000_000 })
            {
                using var ms = new MemoryStream(bytes);
                bmp = new Bitmap(ms);
            }
        }
        catch
        {
            return null; // malformed base64 / unsupported image format
        }

        if (bmp == null) return null;
        lock (Gate)
        {
            Cache[uri] = new WeakReference<Bitmap>(bmp);
            if (Cache.Count > MaxCached) TrimLocked();
        }
        return bmp;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static void TrimLocked()
    {
        HashSet<string>? dead = null;
        foreach (var kv in Cache)
        {
            if (!kv.Value.TryGetTarget(out _))
            {
                dead ??= new HashSet<string>(StringComparer.Ordinal);
                dead.Add(kv.Key);
            }
        }
        if (dead != null) foreach (var k in dead) Cache.Remove(k);

        if (Cache.Count <= MaxCached) return;
        foreach (var k in new List<string>(Cache.Keys))
        {
            if (Cache.Count <= MaxCached) break;
            Cache.Remove(k);
        }
    }
}
