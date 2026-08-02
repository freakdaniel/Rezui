using Rezui.Models;
using Rezui.Services;
using Xunit;

namespace Rezui.Tests;

public sealed class LocalCacheStoreTests
{
    [Fact]
    public async Task CacheSeparatesAreasAndRoundTripsCompressedJson()
    {
        using var directory = new TemporaryDirectory();
        using var cache = new LocalCacheStore(directory.Path);
        var cancellationToken = TestContext.Current.CancellationToken;
        var value = new CacheProbe("Повторяемые данные ".PadRight(1_048_576, 'x'), 17);

        await cache.SetJsonAsync(
            CacheArea.MediaMetadata,
            "same-key",
            value,
            TimeSpan.FromHours(1),
            cancellationToken);

        var restored = await cache.GetJsonAsync<CacheProbe>(
            CacheArea.MediaMetadata,
            "same-key",
            cancellationToken);
        var otherArea = await cache.GetJsonAsync<CacheProbe>(
            CacheArea.Account,
            "same-key",
            cancellationToken);

        Assert.Equal(value, restored);
        Assert.Null(otherArea);

        var storedBytes = new[]
            {
                cache.DatabasePath,
                cache.DatabasePath + "-wal",
                cache.DatabasePath + "-shm"
            }
            .Where(File.Exists)
            .Sum(path => new FileInfo(path).Length);
        Assert.True(storedBytes < value.Text.Length / 4);
    }

    [Fact]
    public async Task ExpiredEntriesAreNotReturned()
    {
        using var directory = new TemporaryDirectory();
        using var cache = new LocalCacheStore(directory.Path);
        var cancellationToken = TestContext.Current.CancellationToken;

        await cache.SetJsonAsync(
            CacheArea.Comments,
            "expired",
            new CacheProbe("old", 1),
            TimeSpan.FromSeconds(-1),
            cancellationToken);

        var restored = await cache.GetJsonAsync<CacheProbe>(
            CacheArea.Comments,
            "expired",
            cancellationToken);

        Assert.Null(restored);
    }

    [Fact]
    public async Task RecentHistoryIsDeduplicatedAndNewestFirst()
    {
        using var directory = new TemporaryDirectory();
        using var cache = new LocalCacheStore(directory.Path);
        var cancellationToken = TestContext.Current.CancellationToken;
        var older = new RecentMedia(
            "Старое название",
            "https://example.com/title.html",
            "https://example.com/old.jpg",
            "Фильм",
            DateTimeOffset.UtcNow.AddHours(-2));
        var newer = older with
        {
            Title = "Новое название",
            ImageUrl = "https://example.com/new.jpg",
            OpenedAt = DateTimeOffset.UtcNow
        };

        await cache.SaveRecentAsync(older, cancellationToken);
        await cache.SaveRecentAsync(newer, cancellationToken);
        var restored = await cache.GetRecentAsync(cancellationToken: cancellationToken);

        var item = Assert.Single(restored);
        Assert.Equal("Новое название", item.Title);
        Assert.Equal(newer.ImageUrl, item.ImageUrl);
    }

    private sealed record CacheProbe(string Text, int Number);
}
