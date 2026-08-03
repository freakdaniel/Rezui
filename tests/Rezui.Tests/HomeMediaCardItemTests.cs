using Rezui.Models;
using Xunit;

namespace Rezui.Tests;

public sealed class HomeMediaCardItemTests
{
    [Fact]
    public void EveryFifthCardCreatesAnEditorialSizeBreak()
    {
        var regular = CreateCard(position: 3);
        var editorial = CreateCard(position: 4);

        Assert.False(regular.IsLarge);
        Assert.True(editorial.IsLarge);
        Assert.Equal(190, regular.CardWidth);
        Assert.Equal(238, editorial.CardWidth);
        Assert.Equal(editorial.CardWidth * 1.5, editorial.PosterHeight);
    }

    [Fact]
    public void MasonryHeightsRepeatTheEditorialRhythmInsteadOfThePosition()
    {
        var heights = Enumerable.Range(0, 10)
            .Select(position => CreateCard(position).MasonryHeight)
            .ToArray();

        Assert.Equal(
            [330, 356, 310, 342, 390, 318, 366, 336, 330, 356],
            heights);
    }

    [Fact]
    public async Task HoverMetadataLoadsOnlyOnce()
    {
        var loadCount = 0;
        var card = CreateCard(
            loadMetadata: _ =>
            {
                loadCount++;
                return Task.CompletedTask;
            });

        await card.LoadMetadataCommand.ExecuteAsync(null);
        await card.LoadMetadataCommand.ExecuteAsync(null);

        Assert.Equal(1, loadCount);
    }

    [Fact]
    public void CachedMetadataBecomesCompactHoverFacts()
    {
        var card = CreateCard();
        card.ApplyMetadata(new CachedMediaMetadata(
            Url: card.Url.AbsoluteUri,
            Id: 42,
            Title: card.Title,
            Names: [],
            OriginalNames: [],
            Description: string.Empty,
            ImageUrl: null,
            ReleaseYear: 2026,
            Format: "series",
            Category: "Сериал",
            IsPlaybackAvailable: true,
            Rating: 8.47,
            RatingVotes: 100,
            Tagline: null,
            ReleaseDate: null,
            Countries: [],
            Genres: [],
            Directors: [],
            Cast: [],
            Quality: "4K",
            AgeRating: "18+",
            DurationSeconds: 5_400,
            Collections: [],
            Rankings: [],
            ExternalRatings: [],
            Recommendations: [],
            Schedule:
            [
                new CachedScheduleEntry(1, 1, 1, null, null, null, true, false),
                new CachedScheduleEntry(2, 1, 2, null, null, null, true, false)
            ],
            Translators: [],
            OtherParts: []));

        Assert.Equal("8.5", card.RatingLabel);
        Assert.Equal("2026", card.YearLabel);
        Assert.Equal("1 ч 30 мин", card.DurationLabel);
        Assert.Equal("4K", card.QualityLabel);
        Assert.Equal("18+", card.AgeRatingLabel);
        Assert.Equal("2 серий", card.EpisodeCountLabel);
    }

    private static HomeMediaCardItem CreateCard(
        int position = 0,
        Func<HomeMediaCardItem, Task>? loadMetadata = null)
    {
        var media = new MediaCardItem(
            "Тестовый тайтл",
            new Uri("https://example.com/media/test.html"),
            new DeferredImageSource(() => Task.FromResult<Avalonia.Media.Imaging.Bitmap?>(null)),
            "Сериал",
            () => Task.CompletedTask);
        return new HomeMediaCardItem(
            media,
            position,
            false,
            loadMetadata ?? (_ => Task.CompletedTask),
            card =>
            {
                card.IsSaved = !card.IsSaved;
                return Task.CompletedTask;
            });
    }
}
