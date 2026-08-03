using Rezui.Services;
using Xunit;

namespace Rezui.Tests;

public sealed class AppLoggingTests
{
    [Fact]
    public void SessionFileNameContainsDateAndTime()
    {
        var timestamp = new DateTimeOffset(
            2026,
            8,
            3,
            12,
            46,
            51,
            561,
            TimeSpan.FromHours(3));

        Assert.Equal(
            "rezui-2026-08-03-12-46-51-561.log",
            AppLogging.CreateSessionFileName(timestamp));
    }

    [Fact]
    public void SessionHeaderHasOneBlankLineAroundLogoAndDynamicVersions()
    {
        var header = AppLogging.BuildSessionHeader(
            "0.1.0",
            "0.0.11",
            "Microsoft Windows NT 10.0.19045.0",
            "\n");

        Assert.StartsWith("\n  ,ggggggggggg,", header);
        Assert.EndsWith("`Y8P\"                   \n\n", header);
        Assert.Contains("app version: 0.1.0", header);
        Assert.Contains("lib version: 0.0.11", header);
        Assert.Contains("system: Microsoft Windows NT 10.0.19045.0", header);
    }
}
