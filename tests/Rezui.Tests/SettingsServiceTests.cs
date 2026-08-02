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
    public async Task AuthenticationCookiesUseDedicatedAuthFile()
    {
        using var directory = new TemporaryDirectory();
        var service = new SettingsService(directory.Path);
        var settings = new AppSettings
        {
            Origin = "https://example.com"
        };
        var auth = new AuthState
        {
            Cookies =
            {
                ["dle_user_id"] = "user-id",
                ["dle_password"] = "password-hash"
            }
        };

        var cancellationToken = TestContext.Current.CancellationToken;
        await service.SaveAsync(settings, cancellationToken);
        await service.SaveAuthAsync(auth, cancellationToken);
        var restored = await service.LoadAuthAsync(cancellationToken);
        var settingsJson = await File.ReadAllTextAsync(
            service.SettingsPath,
            cancellationToken);

        Assert.Equal("user-id", restored.Cookies["dle_user_id"]);
        Assert.Equal("password-hash", restored.Cookies["dle_password"]);
        Assert.DoesNotContain("dle_user_id", settingsJson, StringComparison.Ordinal);
        Assert.Equal("Settings.json", Path.GetFileName(service.SettingsPath));
        Assert.Equal("Auth.json", Path.GetFileName(service.AuthPath));
    }

    [Fact]
    public async Task LegacySettingsAreSplitWithoutLosingSessionOrHistory()
    {
        using var directory = new TemporaryDirectory();
        using var cache = new LocalCacheStore(directory.Path);
        var service = new SettingsService(directory.Path, cache);
        var legacyPath = Path.Combine(directory.Path, "settings.json");
        await File.WriteAllTextAsync(
            legacyPath,
            """
            {
              "origin": "https://example.com",
              "theme": 2,
              "rememberSession": true,
              "customMirrors": ["https://mirror.example"],
              "authenticationCookies": {
                "dle_user_id": "42",
                "dle_password": "secret"
              },
              "recent": [{
                "title": "Тестовый сериал",
                "url": "https://example.com/series/test.html",
                "imageUrl": "https://example.com/poster.jpg",
                "category": "Сериал",
                "openedAt": "2026-08-02T12:00:00+03:00"
              }]
            }
            """,
            TestContext.Current.CancellationToken);

        var settings = await service.LoadAsync(TestContext.Current.CancellationToken);
        var auth = await service.LoadAuthAsync(TestContext.Current.CancellationToken);
        var recent = await cache.GetRecentAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        var cleanSettingsJson = await File.ReadAllTextAsync(
            service.SettingsPath,
            TestContext.Current.CancellationToken);

        Assert.Equal(ThemePreference.Dark, settings.Theme);
        Assert.Equal("42", auth.Cookies["dle_user_id"]);
        Assert.Single(recent);
        Assert.Equal("Тестовый сериал", recent[0].Title);
        Assert.DoesNotContain(
            "authenticationCookies",
            cleanSettingsJson,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recent", cleanSettingsJson, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(legacyPath));
    }
}
