using Avalonia;
using LibVLCSharp.Shared;
using Rezui.Services;

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

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
