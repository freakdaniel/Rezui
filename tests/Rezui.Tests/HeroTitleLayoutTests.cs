using Rezui.ViewModels;
using Xunit;

namespace Rezui.Tests;

public sealed class HeroTitleLayoutTests
{
    [Fact]
    public void ShortTitleKeepsMaximumHeroFontSize()
    {
        var fontSize = MainWindowViewModel.CalculateDetailsTitleFontSize(
            "Очень странные дела");

        Assert.Equal(38, fontSize);
    }

    [Fact]
    public void LongTitleUsesSmallerHeroFontSize()
    {
        var fontSize = MainWindowViewModel.CalculateDetailsTitleFontSize(
            "Клеватесс: Король демонических зверей, младенец и герой-нежить [ТВ-2]");

        Assert.InRange(fontSize, 28, 33);
    }

    [Fact]
    public void FontSizeNeverDropsBelowReadableMinimum()
    {
        var fontSize = MainWindowViewModel.CalculateDetailsTitleFontSize(
            new string('Ж', 300));

        Assert.Equal(28, fontSize);
    }
}
