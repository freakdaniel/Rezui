using Rezui.Models;
using Rezui.Services;
using Xunit;

namespace Rezui.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public async Task MissingSettingsUsePrimaryMirror()
    {
        using var directory = new TemporaryDirectory();
        var service = new SettingsService(directory.Path);

        var settings = await service.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(RezkaMirrors.Primary, settings.Origin);
    }

    [Fact]
    public async Task EmptySavedMirrorFallsBackToPrimaryMirror()
    {
        using var directory = new TemporaryDirectory();
        var service = new SettingsService(directory.Path);
        var cancellationToken = TestContext.Current.CancellationToken;
        await service.SaveAsync(
            new AppSettings { Origin = string.Empty },
            cancellationToken);

        var settings = await service.LoadAsync(cancellationToken);

        Assert.Equal(RezkaMirrors.Primary, settings.Origin);
    }

    [Fact]
    public async Task SaveAndLoadPreservesThemePreference()
    {
        using var directory = new TemporaryDirectory();
        var service = new SettingsService(directory.Path);
        var settings = new AppSettings
        {
            Origin = "https://example.com",
            Theme = ThemePreference.Light
        };

        var cancellationToken = TestContext.Current.CancellationToken;
        await service.SaveAsync(settings, cancellationToken);
        var restored = await service.LoadAsync(cancellationToken);

        Assert.Equal(ThemePreference.Light, restored.Theme);
        Assert.Equal("https://example.com", restored.Origin);
    }

    [Fact]
    public async Task SaveAndLoadPreservesAuthenticationCookies()
    {
        using var directory = new TemporaryDirectory();
        var service = new SettingsService(directory.Path);
        var settings = new AppSettings
        {
            Origin = "https://example.com",
            AuthenticationCookies =
            {
                ["dle_user_id"] = "user-id",
                ["dle_password"] = "password-hash"
            }
        };

        var cancellationToken = TestContext.Current.CancellationToken;
        await service.SaveAsync(settings, cancellationToken);
        var restored = await service.LoadAsync(cancellationToken);

        Assert.Equal("user-id", restored.AuthenticationCookies["dle_user_id"]);
        Assert.Equal("password-hash", restored.AuthenticationCookies["dle_password"]);
    }
}
