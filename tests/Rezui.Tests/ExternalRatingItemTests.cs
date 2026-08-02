using Rezui.Models;
using Xunit;

namespace Rezui.Tests;

public sealed class ExternalRatingItemTests
{
    [Theory]
    [InlineData("IMDb", true, false, false, false)]
    [InlineData("Кинопоиск", false, true, false, false)]
    [InlineData("Kinopoisk", false, true, false, false)]
    [InlineData("World Art", false, false, true, false)]
    [InlineData("World-Art", false, false, true, false)]
    [InlineData("Metacritic", false, false, false, true)]
    public void SelectsMatchingVisualStyle(
        string source,
        bool isImdb,
        bool isKinopoisk,
        bool isWorldArt,
        bool isOther)
    {
        var item = new ExternalRatingItem(
            source,
            "8.5",
            "100 оценок",
            new Uri("https://example.com/rating"));

        Assert.Equal(isImdb, item.IsImdb);
        Assert.Equal(isKinopoisk, item.IsKinopoisk);
        Assert.Equal(isWorldArt, item.IsWorldArt);
        Assert.Equal(isOther, item.IsOther);
        Assert.True(item.HasUrl);
    }
}
