using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text;
using HdRezka;
using Serilog;
using Serilog.Events;

namespace Rezui.Services;

internal static class AppLogging
{
    private static ActivityListener? _hdRezkaActivityListener;
    private static MeterListener? _hdRezkaMeterListener;
    private static bool _initialized;

    public static string LogDirectory { get; private set; } = string.Empty;

    public static string CurrentLogPath { get; private set; } = string.Empty;

    internal static string AppVersion { get; } = GetVersion(typeof(AppLogging).Assembly);

    internal static string LibraryVersion { get; } = GetVersion(typeof(Client).Assembly);

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        try
        {
            LogDirectory = ResolveLogDirectory();
            CurrentLogPath = Path.Combine(
                LogDirectory,
                CreateSessionFileName(DateTimeOffset.Now));
            File.WriteAllText(
                CurrentLogPath,
                BuildSessionHeader(
                    AppVersion,
                    LibraryVersion,
                    Environment.OSVersion.VersionString),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Log.Logger = CreateLoggerConfiguration()
                .WriteTo.File(
                    CurrentLogPath,
                    rollingInterval: RollingInterval.Infinite,
                    shared: true,
                    flushToDiskInterval: TimeSpan.FromSeconds(1),
                    outputTemplate:
                        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] " +
                        "[{SourceContext}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"File logging initialization failed: {exception}");
            LogDirectory = string.Empty;
            CurrentLogPath = string.Empty;
            Log.Logger = CreateLoggerConfiguration().CreateLogger();
        }

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        StartHdRezkaDiagnostics();
        _initialized = true;
        Logger.Information(
            "Logging initialized in {LogPath}; HDRezka diagnostics source is {DiagnosticsSource}",
            CurrentLogPath,
            Diagnostics.ActivitySourceName);
    }

    public static void WriteAvaloniaEvent(string message) =>
        Log.ForContext("SourceContext", "Avalonia")
            .Warning("{AvaloniaEvent}", message);

    public static void Shutdown()
    {
        if (!_initialized)
        {
            return;
        }

        Logger.Information("Logging is shutting down");
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        _hdRezkaActivityListener?.Dispose();
        _hdRezkaActivityListener = null;
        _hdRezkaMeterListener?.Dispose();
        _hdRezkaMeterListener = null;
        Log.CloseAndFlush();
        _initialized = false;
    }

    private static string ResolveLogDirectory()
    {
        var preferred = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Rezui",
            "Logs");
        try
        {
            Directory.CreateDirectory(preferred);
            return preferred;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            var fallback = Path.Combine(Path.GetTempPath(), "Rezui", "Logs");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }

    private static LoggerConfiguration CreateLoggerConfiguration() =>
        new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .Enrich.WithProperty("SourceContext", "Rezui")
            .Enrich.WithProperty("Application", "Rezui")
            .Enrich.WithProperty("ProcessId", Environment.ProcessId)
            .WriteTo.Debug(restrictedToMinimumLevel: LogEventLevel.Debug);

    internal static string CreateSessionFileName(DateTimeOffset startedAt) =>
        $"rezui-{startedAt:yyyy-MM-dd-HH-mm-ss-fff}.log";

    internal static string BuildSessionHeader(
        string appVersion,
        string libraryVersion,
        string systemDescription,
        string? newLine = null)
    {
        var separator = newLine ?? Environment.NewLine;
        string[] lines =
        [
            "  ,ggggggggggg,                                         ",
            " dP\"\"\"88\"\"\"\"\"\"Y8,                                       ",
            " Yb,  88      `8b                                       ",
            "  `\"  88      ,8P                                   gg  ",
            "      88aaaad8P\"                                    \"\"  ",
            "      88\"\"\"\"Yb,     ,ggg,      ,gggg,  gg      gg   gg  ",
            "      88     \"8b   i8\" \"8i    d8\"  Yb  I8      8I   88  ",
            "      88      `8i  I8, ,8I   dP    dP  I8,    ,8I   88  ",
            "      88       Yb, `YbadP' ,dP  ,adP' ,d8b,  ,d8b,*,88,*",
            "      88        Y8888P\"Y8888\"   \"\"Y8d88P'\"Y88P`Y88P\"\"Y8",
            "                                 ,d8I'                  ",
            $"                               ,dP'8I    app version: {appVersion}",
            $"                              ,8\"  8I    lib version: {libraryVersion}",
            $"                              I8   8I    system: {systemDescription}",
            "                              `8, ,8I                   ",
            "                               `Y8P\"                   "
        ];

        return separator +
            string.Join(separator, lines) +
            separator +
            separator;
    }

    private static string GetVersion(Assembly assembly)
    {
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion.Split('+', 2)[0];
        }

        return assembly.GetName().Version?.ToString(3) ?? "unknown";
    }

    private static void StartHdRezkaDiagnostics()
    {
        _hdRezkaActivityListener = new ActivityListener
        {
            ShouldListenTo = static source =>
                source.Name == Diagnostics.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = LogHdRezkaActivity
        };
        ActivitySource.AddActivityListener(_hdRezkaActivityListener);

        _hdRezkaMeterListener = new MeterListener
        {
            InstrumentPublished = static (instrument, listener) =>
            {
                if (instrument.Meter.Name == Diagnostics.MeterName &&
                    instrument.Name == "hdrezka.cache.request.count")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        _hdRezkaMeterListener.SetMeasurementEventCallback<long>(LogHdRezkaCacheMeasurement);
        _hdRezkaMeterListener.Start();
    }

    private static void LogHdRezkaCacheMeasurement(
        Instrument instrument,
        long measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
    {
        try
        {
            string? outcome = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "outcome")
                {
                    outcome = tag.Value?.ToString();
                    break;
                }
            }

            Log.ForContext("SourceContext", Diagnostics.MeterName)
                .Debug(
                    "HDRezka response cache {CacheOutcome}; count={RequestCount}",
                    outcome,
                    measurement);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"HDRezka metrics logging failed: {exception}");
        }
    }

    private static void LogHdRezkaActivity(Activity activity)
    {
        try
        {
            var logger = Log.ForContext("SourceContext", Diagnostics.ActivitySourceName);
            var method = activity.GetTagItem("http.request.method")?.ToString();
            var server = activity.GetTagItem("server.address")?.ToString();
            var path = activity.GetTagItem("url.path")?.ToString();
            if (activity.Status == ActivityStatusCode.Error)
            {
                logger.Warning(
                    "HDRezka request {Method} {Server}{Path} failed after {DurationMs:0.0} ms with {ErrorType}",
                    method,
                    server,
                    path,
                    activity.Duration.TotalMilliseconds,
                    activity.GetTagItem("error.type"));
            }
            else
            {
                logger.Debug(
                    "HDRezka request {Method} {Server}{Path} completed in {DurationMs:0.0} ms",
                    method,
                    server,
                    path,
                    activity.Duration.TotalMilliseconds);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"HDRezka diagnostics logging failed: {exception}");
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args) =>
        Logger.Fatal(
            args.ExceptionObject as Exception,
            "Unhandled application exception; terminating={IsTerminating}",
            args.IsTerminating);

    private static void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs args)
    {
        Logger.Error(args.Exception, "Unobserved task exception");
        args.SetObserved();
    }

    private static ILogger Logger =>
        Log.ForContext("SourceContext", "Rezui.Logging");
}
