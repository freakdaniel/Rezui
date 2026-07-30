using System.Text.Json;
using Rezui.Models;

namespace Rezui.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _directory;

    public SettingsService()
    {
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rezui");
        SettingsPath = Path.Combine(_directory, "settings.json");
    }

    public string SettingsPath { get; }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            await using var stream = File.OpenRead(SettingsPath);
            return await JsonSerializer.DeserializeAsync<AppSettings>(
                       stream,
                       JsonOptions,
                       cancellationToken)
                   ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directory);
        var temporaryPath = SettingsPath + ".tmp";

        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                settings,
                JsonOptions,
                cancellationToken);
        }

        ApplyOwnerOnlyPermissions(temporaryPath);
        File.Move(temporaryPath, SettingsPath, overwrite: true);
        ApplyOwnerOnlyPermissions(SettingsPath);
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
        catch (PlatformNotSupportedException)
        {
            // Permissions are best-effort on unusual Unix filesystems.
        }
    }
}

