using Rezui.Services;
using Xunit;

namespace Rezui.Tests;

public sealed class CountryFlagAssetsTests
{
    [Theory]
    [InlineData("США", "https://example.test/country/USA/", "US")]
    [InlineData("Великобритания", "https://example.test/country/UK/", "GB")]
    [InlineData("Япония", "https://example.test/country/JPN/", "JP")]
    [InlineData("ОАЭ", null, "AE")]
    public void ResolveCountryCodeSupportsRezkaLinksAndAliases(
        string name,
        string? url,
        string expected)
    {
        Assert.Equal(expected, CountryFlagAssets.ResolveCountryCode(name, url));
    }

    [Fact]
    public void ResolveCountryCodeReturnsNullForUnknownCountry()
    {
        Assert.Null(CountryFlagAssets.ResolveCountryCode(
            "Неизвестная страна",
            "https://example.test/country/UNKNOWN/"));
    }
}
