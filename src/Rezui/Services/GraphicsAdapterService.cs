using System.Runtime.Versioning;
using Microsoft.Win32;
using Rezui.Models;
using Serilog;

namespace Rezui.Services;

/// <summary>
/// Persists the per-application GPU preference and, where the platform allows
/// it, steers the process onto the chosen adapter
/// </summary>
public static class GraphicsAdapterService
{
    public static bool IsSupported =>
        OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    /// <summary>
    /// Persists the preference for the current executable. Returns <c>true</c>
    /// when the change will take effect on the next launch (Windows registry),
    /// <c>false</c> when it could only be applied to the running process or the
    /// platform does not support switching
    /// </summary>
    public static bool Apply(
        GraphicsAdapterPreference preference,
        ILogger? logger = null) =>
        OperatingSystem.IsWindows() && ApplyOnWindows(preference, logger);

    /// <summary>
    /// Persists the preference (where supported) and returns a user-facing
    /// message describing how and when it takes effect on the current platform.
    /// Used by the settings view-model so the toggle always gives feedback
    /// instead of staying silent on Linux/macOS
    /// </summary>
    public static string DescribeApplyOutcome(
        GraphicsAdapterPreference preference,
        ILogger? logger = null)
    {
        if (OperatingSystem.IsWindows())
        {
            return ApplyOnWindows(preference, logger)
                ? "Настройка графики применится после перезапуска приложения"
                : "Не удалось сохранить выбор адаптера";
        }

        if (OperatingSystem.IsLinux())
        {
            return preference == GraphicsAdapterPreference.Auto
                ? "Настройка графики применится после перезапуска приложения"
                : "Дискретный адаптер включится после перезапуска приложения";
        }

        return "macOS выбирает видеокарту автоматически — настройка сохранена";
    }

    /// <summary>
    /// Applies the preference to the running process before the render surface
    /// is created. Called from <c>Program.Main</c>, ahead of
    /// <c>BuildAvaloniaApp</c>, so that the Linux GL context picks up the
    /// PRIME/Mesa variables. No-op on Windows (the driver reads the registry)
    /// and on macOS (no per-process selector)
    /// </summary>
    public static void ApplyAtStartup(
        GraphicsAdapterPreference preference,
        ILogger? logger = null)
    {
        if (OperatingSystem.IsLinux())
        {
            ApplyOnLinux(preference, logger);
        }
        else if (OperatingSystem.IsMacOS() && preference != GraphicsAdapterPreference.Auto)
        {
            (logger ?? Log.ForContext(typeof(GraphicsAdapterService)))
                .Information(
                    "Graphics adapter preference {Preference} recorded; macOS selects the GPU automatically",
                    preference);
        }
    }

    /// <summary>
    /// Reads the persisted GPU preference from the settings file synchronously,
    /// for use during process startup (before the async settings service is
    /// available). Returns <see cref="GraphicsAdapterPreference.Auto"/> when the
    /// file is missing or unreadable, matching the default settings value
    /// </summary>
    public static GraphicsAdapterPreference ReadPersistedPreference(ILogger? logger = null)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Rezui",
                "Settings.json");
            if (!File.Exists(path))
            {
                return GraphicsAdapterPreference.Auto;
            }

            var json = File.ReadAllText(path);
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty(
                    "GraphicsAdapter",
                    out var element) &&
                element.TryGetInt32(out var value) &&
                Enum.IsDefined(typeof(GraphicsAdapterPreference), value))
            {
                return (GraphicsAdapterPreference)value;
            }

            if (document.RootElement.TryGetProperty(
                    "GraphicsAdapter",
                    out var namedElement) &&
                namedElement.ValueKind == System.Text.Json.JsonValueKind.String &&
                Enum.TryParse<GraphicsAdapterPreference>(namedElement.GetString(), ignoreCase: true, out var named))
            {
                return named;
            }
        }
        catch (Exception exception)
        {
            (logger ?? Log.ForContext(typeof(GraphicsAdapterService)))
                .Warning(exception, "Could not read the persisted GPU preference at startup");
        }

        return GraphicsAdapterPreference.Auto;
    }

    [SupportedOSPlatform("windows")]
    private static bool ApplyOnWindows(
        GraphicsAdapterPreference preference,
        ILogger? logger)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                $@"Software\Microsoft\DirectX\UserGpuPreferences\{executable}");
            if (key is null)
            {
                return false;
            }

            key.SetValue(
                "GpuPreference",
                ToWindowsGpuPreference(preference),
                RegistryValueKind.DWord);
            (logger ?? Log.ForContext(typeof(GraphicsAdapterService)))
                .Information(
                    "Graphics adapter preference {Preference} persisted for {Executable}",
                    preference,
                    executable);
            return true;
        }
        catch (Exception exception)
        {
            (logger ?? Log.ForContext(typeof(GraphicsAdapterService)))
                .Warning(
                    exception,
                    "Could not persist the graphics adapter preference");
            return false;
        }
    }

    [SupportedOSPlatform("linux")]
    private static void ApplyOnLinux(
        GraphicsAdapterPreference preference,
        ILogger? logger)
    {
        // PRIME render offload is NVIDIA-specific; DRI_PRIME covers the Mesa
        // (AMD/Intel) stack. Setting both is harmless: each is ignored by the
        // driver family it does not apply to, and together they cover the two
        // common discrete-GPU configurations without requiring the user to know
        // which one they have
        var log = logger ?? Log.ForContext(typeof(GraphicsAdapterService));
        try
        {
            switch (preference)
            {
                case GraphicsAdapterPreference.Discrete:
                    Environment.SetEnvironmentVariable("__NV_PRIME_RENDER_OFFLOAD", "1");
                    Environment.SetEnvironmentVariable(
                        "__GLX_VENDOR_LIBRARY_NAME",
                        "nvidia");
                    Environment.SetEnvironmentVariable("DRI_PRIME", "1");
                    log.Information(
                        "Linux GPU preference {Preference} applied (PRIME + DRI_PRIME=1)",
                        preference);
                    break;
                case GraphicsAdapterPreference.Integrated:
                    Environment.SetEnvironmentVariable("__NV_PRIME_RENDER_OFFLOAD", null);
                    Environment.SetEnvironmentVariable("__GLX_VENDOR_LIBRARY_NAME", null);
                    Environment.SetEnvironmentVariable("DRI_PRIME", "0");
                    log.Information(
                        "Linux GPU preference {Preference} applied (offload cleared, DRI_PRIME=0)",
                        preference);
                    break;
                default:
                    Environment.SetEnvironmentVariable("__NV_PRIME_RENDER_OFFLOAD", null);
                    Environment.SetEnvironmentVariable("__GLX_VENDOR_LIBRARY_NAME", null);
                    Environment.SetEnvironmentVariable("DRI_PRIME", null);
                    log.Information(
                        "Linux GPU preference {Preference} applied (driver-default selection)",
                        preference);
                    break;
            }
        }
        catch (Exception exception)
        {
            log.Warning(exception, "Could not apply the Linux GPU preference");
        }
    }

    private static int ToWindowsGpuPreference(GraphicsAdapterPreference preference) =>
        preference switch
        {
            GraphicsAdapterPreference.Discrete => 2,
            GraphicsAdapterPreference.Integrated => 1,
            _ => 0,
        };
}
