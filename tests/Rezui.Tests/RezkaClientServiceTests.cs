using Rezui.Services;
using Xunit;

namespace Rezui.Tests;

public sealed class RezkaClientServiceTests
{
    [Theory]
    [InlineData("example.com", "https://example.com/")]
    [InlineData("https://example.com/path", "https://example.com/")]
    [InlineData("http://example.com:8080/a", "http://example.com:8080/")]
    public void NormalizeOriginKeepsOnlyHttpOrigin(string input, string expected)
    {
        var result = RezkaClientService.NormalizeOrigin(input);

        Assert.Equal(expected, result.AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("file:///tmp/page.html")]
    [InlineData("not a host")]
    public void NormalizeOriginRejectsInvalidInput(string input)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            RezkaClientService.NormalizeOrigin(input));
    }
}
