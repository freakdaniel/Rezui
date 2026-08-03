using System.Buffers;
using System.Diagnostics;
using System.Net;
using Avalonia.Media.Imaging;
using HdRezka;
using Rezui.Models;

namespace Rezui.Services;

public sealed class ImageCacheService : IDisposable
{
    private const int MaximumImageSize = 15 * 1024 * 1024;
    private const int MaximumDecodedEntryCount = 96;
    private const long MaximumDecodedCacheSize = 96L * 1024 * 1024;
    private static readonly Task<Bitmap?> EmptyImage = Task.FromResult<Bitmap?>(null);

    private readonly HttpClient _httpClient;
    private readonly LocalCacheStore? _cache;
    private readonly Lock _cacheLock = new();
    private readonly Dictionary<ImageCacheKey, CacheEntry> _images = [];
    private readonly LinkedList<ImageCacheKey> _leastRecentlyUsed = [];
    private readonly SemaphoreSlim _decodeGate = new(2, 2);
    private long _estimatedDecodedCacheSize;
    private bool _disposed;

    public ImageCacheService(LocalCacheStore? cache = null)
    {
        _cache = cache;
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(25)
        };
    }

    public DeferredImageSource Defer(
        Uri? imageUrl,
        Uri? referer,
        ImageDecodeSize decodeSize) =>
        new(() => LoadAsync(imageUrl, referer, decodeSize));

    public Task<Bitmap?> LoadAsync(
        Uri? imageUrl,
        Uri? referer = null,
        ImageDecodeSize decodeSize = default)
    {
        if (imageUrl is null || imageUrl.Scheme is not ("http" or "https"))
        {
            return EmptyImage;
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        decodeSize = decodeSize.Normalize();
        var key = new ImageCacheKey(imageUrl.AbsoluteUri, decodeSize);
        CacheEntry entry;
        lock (_cacheLock)
        {
            if (_images.TryGetValue(key, out entry!))
            {
                Touch(entry);
                return entry.Image.Value;
            }

            var node = _leastRecentlyUsed.AddFirst(key);
            entry = new CacheEntry(
                new Lazy<Task<Bitmap?>>(
                    () => LoadOrDownloadAsync(imageUrl, referer, decodeSize),
                    LazyThreadSafetyMode.ExecutionAndPublication),
                node,
                decodeSize.EstimatedByteSize);
            _images.Add(key, entry);
            _estimatedDecodedCacheSize += entry.EstimatedByteSize;
            TrimDecodedCache();
        }

        return entry.Image.Value;
    }

    private async Task<Bitmap?> LoadOrDownloadAsync(
        Uri imageUrl,
        Uri? referer,
        ImageDecodeSize decodeSize)
    {
        if (_cache is not null)
        {
            var cached = await _cache.GetBytesAsync(CacheArea.Covers, imageUrl.AbsoluteUri)
                .ConfigureAwait(false);
            if (cached is { Length: > 0 })
            {
                try
                {
                    return await DecodeAsync(cached, decodeSize).ConfigureAwait(false);
                }
                catch (ArgumentException exception)
                {
                    TraceFailure(imageUrl, $"cached copy is invalid: {exception.Message}");
                }
            }
        }

        return await DownloadAsync(imageUrl, referer, decodeSize).ConfigureAwait(false);
    }

    private async Task<Bitmap?> DownloadAsync(
        Uri imageUrl,
        Uri? referer,
        ImageDecodeSize decodeSize)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
            request.Headers.TryAddWithoutValidation("User-Agent", ClientOptions.DefaultUserAgent);
            request.Headers.TryAddWithoutValidation(
                "Accept",
                "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
            request.Headers.TryAddWithoutValidation("Accept-Language", "ru-RU,ru;q=0.9,en;q=0.7");

            if (referer is { Scheme: "http" or "https" })
            {
                request.Headers.Referrer = referer;
            }

            using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode ||
                response.Content.Headers.ContentLength > MaximumImageSize)
            {
                TraceFailure(imageUrl, $"HTTP {(int)response.StatusCode}");
                return null;
            }

            await using var source = await response.Content.ReadAsStreamAsync()
                .ConfigureAwait(false);
            using var bytes = new MemoryStream();
            var buffer = ArrayPool<byte>.Shared.Rent(81920);
            try
            {
                while (true)
                {
                    var read = await source.ReadAsync(buffer).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    if (bytes.Length + read > MaximumImageSize)
                    {
                        TraceFailure(imageUrl, "file is larger than 15 MB");
                        return null;
                    }

                    await bytes.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (bytes.Length == 0)
            {
                TraceFailure(imageUrl, "empty response");
                return null;
            }

            var imageBytes = bytes.ToArray();
            if (_cache is not null)
            {
                await _cache.SetBytesAsync(
                        CacheArea.Covers,
                        imageUrl.AbsoluteUri,
                        imageBytes,
                        TimeSpan.FromDays(45))
                    .ConfigureAwait(false);
            }

            return await DecodeAsync(imageBytes, decodeSize).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or
                IOException or
                OperationCanceledException or
                ArgumentException)
        {
            TraceFailure(imageUrl, exception.Message);
            return null;
        }
    }

    private async Task<Bitmap> DecodeAsync(byte[] bytes, ImageDecodeSize decodeSize)
    {
        await _decodeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(
                    () =>
                    {
                        using var stream = new MemoryStream(bytes, writable: false);
                        return Bitmap.DecodeToWidth(
                            stream,
                            decodeSize.PixelWidth,
                            BitmapInterpolationMode.HighQuality);
                    })
                .ConfigureAwait(false);
        }
        finally
        {
            _decodeGate.Release();
        }
    }

    private void Touch(CacheEntry entry)
    {
        _leastRecentlyUsed.Remove(entry.Node);
        _leastRecentlyUsed.AddFirst(entry.Node);
    }

    private void TrimDecodedCache()
    {
        while (_images.Count > MaximumDecodedEntryCount ||
               _estimatedDecodedCacheSize > MaximumDecodedCacheSize)
        {
            var node = _leastRecentlyUsed.Last;
            if (node is null || !_images.Remove(node.Value, out var removed))
            {
                return;
            }

            _leastRecentlyUsed.Remove(node);
            _estimatedDecodedCacheSize -= removed.EstimatedByteSize;
        }
    }

    private static void TraceFailure(Uri imageUrl, string reason) =>
        Debug.WriteLine($"Could not load image {imageUrl}: {reason}");

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
        CacheEntry[] entries;
        lock (_cacheLock)
        {
            entries = [.. _images.Values];
            _images.Clear();
            _leastRecentlyUsed.Clear();
            _estimatedDecodedCacheSize = 0;
        }

        foreach (var image in entries
                     .Where(entry => entry.Image.IsValueCreated &&
                                     entry.Image.Value.IsCompletedSuccessfully)
                     .Select(entry => entry.Image.Value.Result)
                     .OfType<Bitmap>()
                     .Distinct<Bitmap>(ReferenceEqualityComparer.Instance))
        {
            image.Dispose();
        }
    }

    private readonly record struct ImageCacheKey(string Url, ImageDecodeSize DecodeSize);

    private sealed record CacheEntry(
        Lazy<Task<Bitmap?>> Image,
        LinkedListNode<ImageCacheKey> Node,
        long EstimatedByteSize);
}

public readonly record struct ImageDecodeSize(int PixelWidth, int PixelHeight)
{
    public static ImageDecodeSize Avatar => new(96, 96);

    public static ImageDecodeSize Card => new(384, 544);

    public static ImageDecodeSize Details => new(640, 960);

    public static ImageDecodeSize Hero => new(1600, 900);

    internal long EstimatedByteSize => (long)PixelWidth * PixelHeight * 4;

    internal ImageDecodeSize Normalize() => PixelWidth > 0 && PixelHeight > 0
        ? this
        : Card;
}
