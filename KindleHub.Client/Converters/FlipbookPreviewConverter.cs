using System;
using System.Collections.Concurrent;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using KindleHub.Core;

namespace KindleHub.Client.Converters;

/// <summary>Renders the first frame of a KHFLIP1 flipbook message as a bitmap.
///
/// Flipbook frames are packed hex (4 bits per cell), not PNG data, so the chat
/// row cannot hand them to <see cref="DataImageToBitmapConverter"/>. This
/// converter unpacks the cells and paints them onto a 1-bit bitmap instead,
/// cached per message so scrolling and re-polling stay cheap.
/// </summary>
public sealed class FlipbookPreviewConverter : IValueConverter
{
    private const int MaxCached = 80;
    private const int RenderSide = 160;

    private static readonly ConcurrentDictionary<string, WeakReference<Bitmap>> Cache = new(StringComparer.Ordinal);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Message msg ? Render(msg) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Bitmap? Render(Message msg)
    {
        var key = msg.Id ?? msg.Text;
        if (string.IsNullOrEmpty(key)) return null;

        if (Cache.TryGetValue(key, out var cached) && cached.TryGetTarget(out var live)) return live;

        var fb = ChatMedia.TryParseFlipbook(msg.Text);
        if (fb is null || fb.Frames is null || fb.Frames.Length == 0) return null;
        var w = Math.Clamp(fb.Width, 1, 64);
        var h = Math.Clamp(fb.Height, 1, 64);
        var cells = ChatMedia.FlipUnpack(fb.Frames[0], w * h);

        var scale = Math.Max(1, RenderSide / Math.Max(w, h));
        var side = Math.Max(1, Math.Max(w, h) * scale);
        var bmp = new WriteableBitmap(new PixelSize(side, side), new Vector(96, 96));
        var buf = new byte[side * side * 4];
        Fill(buf, side, 0xFF, 0xFF, 0xFF); // white ground

        for (var row = 0; row < h; row++)
        {
            for (var col = 0; col < w; col++)
            {
                if (cells[row * w + col] == 0) continue;
                for (var dy = 0; dy < scale; dy++)
                {
                    var y = row * scale + dy;
                    if (y >= side) break;
                    for (var dx = 0; dx < scale; dx++)
                    {
                        var x = col * scale + dx;
                        if (x >= side) break;
                        var o = (y * side + x) * 4;
                        buf[o + 0] = 0x11; buf[o + 1] = 0x11; buf[o + 2] = 0x11; buf[o + 3] = 0xFF;
                    }
                }
            }
        }

        try
        {
            using var locked = bmp.Lock();
            var rowBytes = locked.RowBytes;
            for (var y = 0; y < side; y++)
                System.Runtime.InteropServices.Marshal.Copy(buf, y * side * 4, locked.Address + (nint)(y * rowBytes), side * 4);
        }
        catch
        {
            return null;
        }

        var weak = new WeakReference<Bitmap>(bmp);
        Cache[key] = weak;
        if (Cache.Count > MaxCached) Trim();
        return bmp;
    }

    private static void Fill(byte[] buf, int side, byte r, byte g, byte b)
    {
        for (var i = 0; i < side * side; i++)
        {
            buf[i * 4 + 0] = b; buf[i * 4 + 1] = g; buf[i * 4 + 2] = r; buf[i * 4 + 3] = 0xFF;
        }
    }

    private static void Trim()
    {
        foreach (var k in Cache.Keys)
        {
            if (Cache.TryGetValue(k, out var w) && !w.TryGetTarget(out _)) Cache.TryRemove(k, out _);
        }
        if (Cache.Count <= MaxCached) return;
        foreach (var k in Cache.Keys)
        {
            if (Cache.Count <= MaxCached) break;
            Cache.TryRemove(k, out _);
        }
    }
}
