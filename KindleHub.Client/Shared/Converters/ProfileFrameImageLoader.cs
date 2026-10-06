using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace KindleHub.Client.Converters;

/// <summary>Loads the same transparent frame PNGs referenced by the official web client.</summary>
public static class ProfileFrameImageLoader
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly ConcurrentDictionary<string, Task<Bitmap?>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlyDictionary<string, string> Files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["plus_book"] = "plus-book", ["plus_laurel"] = "plus-laurel", ["plus_star"] = "plus-star",
        ["plus_wings"] = "plus-wings", ["plus_ring"] = "plus-ring", ["pro_darklaurel"] = "pro-darklaurel",
        ["pro_gold"] = "pro-gold", ["pro_obsidian"] = "pro-obsidian", ["pro_crown"] = "pro-crown",
        ["pro_sigils"] = "pro-sigils", ["max_phoenix"] = "max-phoenix", ["max_dragons"] = "max-dragons",
        ["max_sun"] = "max-sun", ["max_void"] = "max-void", ["max_gate"] = "max-gate"
    };

    public static async Task<Bitmap?> LoadAsync(string? frameId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(frameId) || !Files.TryGetValue(frameId, out var file))
            return null;
        var task = Cache.GetOrAdd(frameId, _ => LoadOrDownloadAsync(file, cancellationToken));
        var image = await task.WaitAsync(cancellationToken);
        // A transient network failure must not be cached for the rest of the session.
        if (image is null) Cache.TryRemove(frameId, out _);
        return image;
    }

    private static async Task<Bitmap?> LoadOrDownloadAsync(string file, CancellationToken cancellationToken)
    {
        var cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KindleHubPro", "profile-frames");
        var cachedFile = Path.Combine(cacheDirectory, file + ".png");
        try
        {
            if (File.Exists(cachedFile))
            {
                try
                {
                    using var cachedStream = File.OpenRead(cachedFile);
                    return new Bitmap(cachedStream);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    try { File.Delete(cachedFile); } catch { }
                }
            }

            var bytes = await Http.GetByteArrayAsync($"https://kindlehub.pro/frames/{file}.png", cancellationToken);
            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new Bitmap(stream);

            try
            {
                Directory.CreateDirectory(cacheDirectory);
                var temporaryFile = Path.Combine(cacheDirectory, file + "." + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    await File.WriteAllBytesAsync(temporaryFile, bytes, cancellationToken);
                    File.Move(temporaryFile, cachedFile, overwrite: true);
                }
                finally
                {
                    try { if (File.Exists(temporaryFile)) File.Delete(temporaryFile); } catch { }
                }
            }
            catch { /* The in-memory image remains usable if disk caching is unavailable. */ }

            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
