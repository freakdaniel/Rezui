using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Avalonia.Media.Imaging;
using HdRezka;

namespace Rezui.Services;

public sealed class ImageCacheService : IDisposable
{
    private const int MaximumImageSize = 15 * 1024 * 1024;
    private static readonly Task<Bitmap?> EmptyImage = Task.FromResult<Bitmap?>(null);

    private readonly HttpClient _httpClient;
    private readonly LocalCacheStore? _cache;
    private readonly ConcurrentDictionary<string, Lazy<Task<Bitmap?>>> _images =
        new(StringComparer.Ordinal);

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

    public Task<Bitmap?> LoadAsync(Uri? imageUrl, Uri? referer = null)
    {
        if (imageUrl is null ||
            imageUrl.Scheme is not ("http" or "https"))
        {
            return EmptyImage;
        }

        return _images.GetOrAdd(
                imageUrl.AbsoluteUri,
                _ => new Lazy<Task<Bitmap?>>(
                    () => LoadOrDownloadAsync(imageUrl, referer),
                    LazyThreadSafetyMode.ExecutionAndPublication))
            .Value;
    }

    private async Task<Bitmap?> LoadOrDownloadAsync(Uri imageUrl, Uri? referer)
    {
        if (_cache is not null)
        {
            var cached = await _cache.GetBytesAsync(CacheArea.Covers, imageUrl.AbsoluteUri);
            if (cached is { Length: > 0 })
            {
                try
                {
                    using var stream = new MemoryStream(cached, writable: false);
                    return new Bitmap(stream);
                }
                catch (ArgumentException)
                {
                    // A corrupt cache entry falls through to a fresh network copy.
                }
            }
        }

        return await DownloadAsync(imageUrl, referer);
    }

    private async Task<Bitmap?> DownloadAsync(Uri imageUrl, Uri? referer)
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
                HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode ||
                response.Content.Headers.ContentLength > MaximumImageSize)
            {
                TraceFailure(imageUrl, $"HTTP {(int)response.StatusCode}");
                return null;
            }

            await using var source = await response.Content.ReadAsStreamAsync();
            using var bytes = new MemoryStream();
            var buffer = ArrayPool<byte>.Shared.Rent(81920);
            try
            {
                while (true)
                {
                    var read = await source.ReadAsync(buffer);
                    if (read == 0)
                    {
                        break;
                    }

                    if (bytes.Length + read > MaximumImageSize)
                    {
                        TraceFailure(imageUrl, "файл больше 15 МБ");
                        return null;
                    }

                    await bytes.WriteAsync(buffer.AsMemory(0, read));
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (bytes.Length == 0)
            {
                TraceFailure(imageUrl, "пустой ответ");
                return null;
            }

            var imageBytes = bytes.ToArray();
            if (_cache is not null)
            {
                await _cache.SetBytesAsync(
                    CacheArea.Covers,
                    imageUrl.AbsoluteUri,
                    imageBytes,
                    TimeSpan.FromDays(45));
            }

            using var bitmapStream = new MemoryStream(imageBytes, writable: false);
            return new Bitmap(bitmapStream);
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

    private static void TraceFailure(Uri imageUrl, string reason) =>
        Debug.WriteLine($"Не удалось загрузить обложку {imageUrl}: {reason}");

    public void Dispose()
    {
        _httpClient.Dispose();
        foreach (var image in _images.Values)
        {
            if (image.IsValueCreated &&
                image.Value.IsCompletedSuccessfully)
            {
                image.Value.Result?.Dispose();
            }
        }

        _images.Clear();
    }
}
