using Avalonia;
using LibVLCSharp.Shared;
using Rezui.Services;
using Serilog;

namespace Rezui;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--check-libvlc", StringComparer.OrdinalIgnoreCase))
        {
            var status = LibVlcRuntime.Inspect();
            Console.WriteLine(status.Message);
            Console.WriteLine($"Library: {status.LibraryPath ?? "not found"}");
            Console.WriteLine($"Plugins: {status.PluginDirectory ?? "not found"}");
            if (!status.IsUsable)
            {
                Environment.ExitCode = 1;
                return;
            }

            try
            {
                LibVlcRuntime.Initialize();
                using var engine = new LibVLC("--quiet");
                Console.WriteLine("LibVLC initialized successfully.");
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Initialization failed: {exception.Message}");
                Environment.ExitCode = 2;
            }

            return;
        }

        AppLogging.Initialize();
        try
        {
            var logger = Log.ForContext(typeof(Program));
            logger.Information(
                "Rezui {Version} starting on {OperatingSystem}",
                AppLogging.AppVersion,
                Environment.OSVersion.VersionString);
            // Apply the persisted GPU preference before any render surface is
            // created: on Linux the PRIME/Mesa env variables are only consulted
            // when the GL context is built, so this must precede the Avalonia
            // AppBuilder. Windows reads the registry at device creation time and
            // macOS has no per-process selector, so this is a safe no-op there.
            GraphicsAdapterService.ApplyAtStartup(
                GraphicsAdapterService.ReadPersistedPreference(logger),
                logger);
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            logger.Information("Rezui stopped normally");
        }
        catch (Exception exception)
        {
            Log.ForContext(typeof(Program))
                .Fatal(exception, "Rezui terminated during startup or application lifetime");
            throw;
        }
        finally
        {
            AppLogging.Shutdown();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .With(new SkiaOptions
            {
                // Keep the GPU resource budget generous so large hero backdrops,
                // cached blurred layers and the video surface can coexist without
                // the compositor evicting textures mid-frame on weaker GPUs.
                MaxGpuResourceSizeBytes = 256L * 1024 * 1024
            })
            .UsePlatformDetect()
            .With(new X11PlatformOptions
            {
                // RenderingMode lists the GPU-backed compositor first and falls
                // back to software only when no GL context is available, which
                // keeps the app usable on headless/llvmpipe X11 sessions.
                RenderingMode = [X11RenderingMode.Glx, X11RenderingMode.Software]
            })
            .With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software]
            })
            // macOS defaults to Metal and only falls back to OpenGL when Metal is
            // unavailable, so no explicit RenderingMode is needed there. Setting it
            // would require the macOS-only platform options type, which is not
            // referenced in this cross-platform build.
            .LogToDelegate(
                AppLogging.WriteAvaloniaEvent,
                Avalonia.Logging.LogEventLevel.Warning);
}
