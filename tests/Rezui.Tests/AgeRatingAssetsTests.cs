using Avalonia.Headless.XUnit;
using Rezui.Services;
using Xunit;

namespace Rezui.Tests;

public sealed class AgeRatingAssetsTests
{
    [AvaloniaFact]
    public void GetLoadsEveryBundledAgeGeometry()
    {
        foreach (var age in new[] { "0+", "6+", "12+", "16+", "18+" })
        {
            Assert.NotNull(AgeRatingAssets.Get(age));
        }
    }

    [Theory]
    [InlineData("0+", "0")]
    [InlineData("6+ для зрителей старше шести лет", "6")]
    [InlineData("12+", "12")]
    [InlineData("16+ для более зрелых", "16")]
    [InlineData("18+ только для взрослых", "18")]
    public void ExtractAgeCodeRecognizesSupportedRezkaValues(
        string source,
        string expected)
    {
        Assert.Equal(expected, AgeRatingAssets.ExtractAgeCode(source));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("21+")]
    public void ExtractAgeCodeRejectsUnsupportedValues(string? source)
    {
        Assert.Null(AgeRatingAssets.ExtractAgeCode(source));
    }
}
