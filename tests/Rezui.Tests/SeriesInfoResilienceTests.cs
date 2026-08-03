using HdRezka;
using Rezui.ViewModels;
using Xunit;

namespace Rezui.Tests;

public sealed class SeriesInfoResilienceTests
{
    [Fact]
    public void EmptySourcesProduceNoSeasons()
    {
        var seasons = MainWindowViewModel.MergeSeriesInfo(
            Array.Empty<IReadOnlyDictionary<int, SeriesInfo>>());

        Assert.Empty(seasons);
    }

    [Fact]
    public void SingleTranslatorSeasonsAndEpisodesArePreserved()
    {
        var source = new Dictionary<int, SeriesInfo>
        {
            [42] = new SeriesInfo(
                TranslatorId: 42,
                TranslatorName: "LostFilm",
                IsPremium: false,
                Seasons: new Dictionary<int, string>
                {
                    [1] = "Сезон 1",
                },
                Episodes: new Dictionary<int, IReadOnlyDictionary<int, string>>
                {
                    [1] = new Dictionary<int, string>
                    {
                        [1] = "Серия 1",
                        [2] = "Серия 2",
                    },
                }),
        };

        var seasons = MainWindowViewModel.MergeSeriesInfo(
            new[] { source });

        var season = Assert.Single(seasons);
        Assert.Equal(1, season.Number);
        Assert.Equal("Сезон 1", season.Title);
        Assert.Equal(new[] { 1, 2 }, season.Episodes.Select(episode => episode.Number));
        var translation = Assert.Single(season.Episodes.First().Translations);
        Assert.Equal(42, translation.TranslatorId);
        Assert.Equal("LostFilm", translation.TranslatorName);
        Assert.False(translation.IsPremium);
    }

    [Fact]
    public void MultipleTranslatorsMergeSeasonsAndAggregateTranslations()
    {
        var first = new Dictionary<int, SeriesInfo>
        {
            [1] = MakeSeries(
                translatorId: 1,
                name: "Дубляж",
                isPremium: false,
                seasons: new[] { (1, "Сезон 1") },
                episodes: new Dictionary<int, IReadOnlyDictionary<int, string>>
                {
                    [1] = new Dictionary<int, string> { [1] = "Серия 1" },
                }),
        };
        var second = new Dictionary<int, SeriesInfo>
        {
            [2] = MakeSeries(
                translatorId: 2,
                name: "Subtitles",
                isPremium: false,
                seasons: new[] { (1, "Сезон 1"), (2, "Сезон 2") },
                episodes: new Dictionary<int, IReadOnlyDictionary<int, string>>
                {
                    [1] = new Dictionary<int, string> { [1] = "Episode 1", [2] = "Episode 2" },
                    [2] = new Dictionary<int, string> { [3] = "Episode 3" },
                }),
        };

        var seasons = MainWindowViewModel.MergeSeriesInfo(new[] { first, second });

        Assert.Equal(new[] { 1, 2 }, seasons.Select(season => season.Number));
        var seasonOne = seasons.Single(season => season.Number == 1);
        Assert.Equal(new[] { 1, 2 }, seasonOne.Episodes.Select(episode => episode.Number));
        Assert.Equal(2, seasonOne.Episodes.First().Translations.Count);
        var seasonTwo = seasons.Single(season => season.Number == 2);
        Assert.Equal("Сезон 2", seasonTwo.Title);
        var seasonTwoEpisode = Assert.Single(seasonTwo.Episodes);
        Assert.Equal(3, seasonTwoEpisode.Number);
        Assert.Single(seasonTwoEpisode.Translations);
    }

    [Fact]
    public void EpisodesKeepTheirOriginalTitleAcrossTranslators()
    {
        var first = new Dictionary<int, SeriesInfo>
        {
            [1] = MakeSeries(
                translatorId: 1,
                name: "A",
                isPremium: false,
                seasons: new[] { (1, "Сезон 1") },
                episodes: new Dictionary<int, IReadOnlyDictionary<int, string>>
                {
                    [1] = new Dictionary<int, string> { [1] = "Первый заголовок" },
                }),
        };
        var second = new Dictionary<int, SeriesInfo>
        {
            [2] = MakeSeries(
                translatorId: 2,
                name: "B",
                isPremium: false,
                seasons: new[] { (1, "Сезон 1") },
                episodes: new Dictionary<int, IReadOnlyDictionary<int, string>>
                {
                    [1] = new Dictionary<int, string> { [1] = "Второй заголовок" },
                }),
        };

        var seasons = MainWindowViewModel.MergeSeriesInfo(new[] { first, second });

        var episode = Assert.Single(Assert.Single(seasons).Episodes);
        Assert.Equal("Первый заголовок", episode.Title);
        Assert.Equal(2, episode.Translations.Count);
    }

    private static SeriesInfo MakeSeries(
        int translatorId,
        string name,
        bool isPremium,
        IEnumerable<(int Number, string Title)> seasons,
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, string>> episodes) =>
        new(
            translatorId,
            name,
            isPremium,
            seasons.ToDictionary(season => season.Number, season => season.Title),
            episodes);
}
