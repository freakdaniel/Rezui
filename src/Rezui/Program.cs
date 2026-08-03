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
                MaxGpuResourceSizeBytes = 128L * 1024 * 1024
            })
            .UsePlatformDetect()
            .LogToDelegate(
                AppLogging.WriteAvaloniaEvent,
                Avalonia.Logging.LogEventLevel.Warning);
}
