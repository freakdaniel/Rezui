using System.Diagnostics;
using System.Text.Json;
using Rezui.Models;

namespace Rezui.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly LocalCacheStore? _cache;
    private readonly string _legacySettingsPath;

    public SettingsService(string? directory = null, LocalCacheStore? cache = null)
    {
        DirectoryPath = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rezui");
        SettingsPath = Path.Combine(DirectoryPath, "Settings.json");
        AuthPath = Path.Combine(DirectoryPath, "Auth.json");
        _legacySettingsPath = Path.Combine(DirectoryPath, "settings.json");
        _cache = cache;
    }

    public string DirectoryPath { get; }

    public string SettingsPath { get; }

    public string AuthPath { get; }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var sourcePath = File.Exists(SettingsPath)
            ? SettingsPath
            : File.Exists(_legacySettingsPath)
                ? _legacySettingsPath
                : null;
        if (sourcePath is null)
        {
            return new AppSettings();
        }

        try
        {
            // Read and close the source file before any write. On
            // case-insensitive filesystems (Windows, default macOS) the legacy
            // "settings.json" and the canonical "Settings.json" resolve to the
            // same file, so leaving the read stream open across SaveAsync's
            // File.Move(overwrite:true) throws UnauthorizedAccessException.
            LegacyAppState legacy;
            await using (var stream = File.OpenRead(sourcePath))
            {
                legacy = await JsonSerializer.DeserializeAsync<LegacyAppState>(
                             stream,
                             JsonOptions,
                             cancellationToken)
                         ?? new LegacyAppState();
            }

            var settings = Normalize(new AppSettings
            {
                Origin = legacy.Origin,
                Theme = legacy.Theme,
                RememberSession = legacy.RememberSession,
                CustomMirrors = legacy.CustomMirrors ?? [],
                PinnedMediaUrls = legacy.PinnedMediaUrls ?? []
            });

            var hasLegacyPayload = legacy.AuthenticationCookies?.Count > 0 ||
                                   legacy.Recent?.Count > 0 ||
                                   !Path.GetFullPath(sourcePath).Equals(
                                       Path.GetFullPath(SettingsPath),
                                       StringComparison.Ordinal);
            if (hasLegacyPayload)
            {
                if (legacy.AuthenticationCookies?.Count > 0 && !File.Exists(AuthPath))
                {
                    await SaveAuthAsync(
                        new AuthState { Cookies = legacy.AuthenticationCookies },
                        cancellationToken);
                }

                if (_cache is not null && legacy.Recent?.Count > 0)
                {
                    var scope = RezkaClientService.BuildAccountScope(
                        NormalizeAuthority(legacy.Origin),
                        legacy.AuthenticationCookies ?? []);
                    await _cache.ImportRecentAsync(scope, legacy.Recent, cancellationToken);
                }

                await SaveAsync(settings, cancellationToken);
                // On case-insensitive filesystems sourcePath (settings.json) and
                // SettingsPath (Settings.json) are the same file, which the
                // canonical SaveAsync has just rewritten — so there is nothing
                // extra to delete. On case-sensitive systems, remove the old
                // lowercase file so it stops shadowing the canonical one.
                if (!string.Equals(sourcePath, SettingsPath, StringComparison.Ordinal) &&
                    File.Exists(sourcePath))
                {
                    File.Delete(sourcePath);
                }
            }

            return settings;
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            Debug.WriteLine($"Не удалось прочитать настройки: {exception.Message}");
            return new AppSettings();
        }
    }

    public async Task<AuthState> LoadAuthAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(AuthPath))
        {
            return new AuthState();
        }

        try
        {
            await using var stream = File.OpenRead(AuthPath);
            var auth = await JsonSerializer.DeserializeAsync<AuthState>(
                           stream,
                           JsonOptions,
                           cancellationToken)
                       ?? new AuthState();
            auth.Cookies = new Dictionary<string, string>(
                auth.Cookies ?? [],
                StringComparer.Ordinal);
            return auth;
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            Debug.WriteLine($"Не удалось прочитать авторизацию: {exception.Message}");
            return new AuthState();
        }
    }

    public Task SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default) =>
        SaveFileAsync(SettingsPath, Normalize(settings), cancellationToken);

    public Task SaveAuthAsync(
        AuthState auth,
        CancellationToken cancellationToken = default) =>
        SaveFileAsync(AuthPath, auth, cancellationToken);

    public async Task ClearAuthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (File.Exists(AuthPath))
        {
            File.Delete(AuthPath);
        }

        await Task.CompletedTask;
    }

    private async Task SaveFileAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temporaryPath = path + ".tmp";

        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                value,
                JsonOptions,
                cancellationToken);
        }

        ApplyOwnerOnlyPermissions(temporaryPath);
        File.Move(temporaryPath, path, overwrite: true);
        ApplyOwnerOnlyPermissions(path);
    }

    private static string NormalizeAuthority(string origin)
    {
        try
        {
            return RezkaClientService.NormalizeOrigin(origin)
                .GetLeftPart(UriPartial.Authority);
        }
        catch (ArgumentException)
        {
            return origin.Trim();
        }
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Origin))
        {
            settings.Origin = RezkaMirrors.Primary;
        }

        settings.CustomMirrors = (settings.CustomMirrors ?? [])
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        settings.PinnedMediaUrls = (settings.PinnedMediaUrls ?? [])
            .Where(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return settings;
    }

    private static void ApplyOwnerOnlyPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (PlatformNotSupportedException exception)
        {
            Debug.WriteLine(
                $"Не удалось ограничить права доступа к {path}: {exception.Message}");
        }
    }

    private sealed class LegacyAppState
    {
        public string Origin { get; set; } = RezkaMirrors.Primary;

        public ThemePreference Theme { get; set; } = ThemePreference.System;

        public bool RememberSession { get; set; } = true;

        public List<string>? CustomMirrors { get; set; }

        public List<string>? PinnedMediaUrls { get; set; }

        public Dictionary<string, string>? AuthenticationCookies { get; set; }

        public List<RecentMedia>? Recent { get; set; }
    }
}
