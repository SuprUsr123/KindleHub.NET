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
    // Same spoofed web identity as KindleHubApiClient: frame PNGs are served from
    // kindlehub.pro itself, so a browser tab on the site would be same-origin here
    // and send image-mode Sec-Fetch metadata instead of the API's cors variant.
    private static readonly HttpClient Http = CreateFrameHttp();

    private static HttpClient CreateFrameHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        var h = client.DefaultRequestHeaders;
        h.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/134.0.0.0 Safari/537.36");
        h.TryAddWithoutValidation("Origin", "https://kindlehub.pro");
        h.TryAddWithoutValidation("Referer", "https://kindlehub.pro/");
        h.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
        h.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
        h.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
        h.TryAddWithoutValidation("Sec-Fetch-Mode", "no-cors");
        h.TryAddWithoutValidation("Sec-Fetch-Dest", "image");
        return client;
    }
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
