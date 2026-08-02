using Rezui.Services;
using Xunit;

namespace Rezui.Tests;

public sealed class TitleFormatterTests
{
    [Theory]
    [InlineData("Любовная сцена / Сцена любви", "Любовная сцена")]
    [InlineData("  Основное название / Альтернативное  ", "Основное название")]
    [InlineData("AC/DC", "AC/DC")]
    [InlineData(".хак//Корни", ".хак//Корни")]
    [InlineData("Обычное название", "Обычное название")]
    public void NormalizeKeepsOnlyRezkaAlternativeTitlePrefix(
        string source,
        string expected)
    {
        Assert.Equal(expected, TitleFormatter.Normalize(source));
    }

    [Theory]
    [InlineData(".хак", ".хак//Корни", ".хак//Корни")]
    [InlineData("AC", "AC/DC", "AC/DC")]
    [InlineData("Любовная сцена", "Любовная сцена / Сцена любви", "Любовная сцена")]
    [InlineData("Полное название", "Другое название", "Полное название")]
    public void ReconcileRestoresTitlesSplitByTheLibrary(
        string parsedTitle,
        string previewTitle,
        string expected)
    {
        Assert.Equal(expected, TitleFormatter.Reconcile(parsedTitle, previewTitle));
    }
}
