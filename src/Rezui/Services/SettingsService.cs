using System.Text.Json;
using System.Diagnostics;
using Rezui.Models;

namespace Rezui.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _directory;

    public SettingsService(string? directory = null)
    {
        _directory = directory ?? Path.Combine(
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
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(
                               stream,
                               JsonOptions,
                               cancellationToken)
                           ?? new AppSettings();
            if (string.IsNullOrWhiteSpace(settings.Origin))
            {
                settings.Origin = RezkaMirrors.Primary;
            }

            settings.CustomMirrors = (settings.CustomMirrors ?? [])
                .Where(origin => !string.IsNullOrWhiteSpace(origin))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return settings;
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
        catch (PlatformNotSupportedException exception)
        {
            Debug.WriteLine(
                $"Не удалось ограничить права доступа к {path}: {exception.Message}");
        }
    }
}

