using System.Reflection;
using System.Runtime.InteropServices;
using System.Diagnostics;
using LibVLCSharp.Shared;

namespace Rezui.Services;

public static class LibVlcRuntime
{
    private static readonly object Sync = new();
    private static bool _initialized;
    private static bool _resolverInstalled;
    private static nint _linuxLibVlcHandle;
    private static nint _linuxLibVlcCoreHandle;

    public static void Initialize()
    {
        lock (Sync)
        {
            if (_initialized)
            {
                return;
            }

            if (OperatingSystem.IsLinux())
            {
                InitializeLinux();
            }
            else
            {
                Core.Initialize();
            }

            _initialized = true;
        }
    }

    public static LibVlcRuntimeStatus Inspect()
    {
        if (!OperatingSystem.IsLinux())
        {
            return new LibVlcRuntimeStatus(
                true,
                "Native LibVLC поставляется платформенным пакетом.",
                null,
                null);
        }

        var library = FindLinuxLibVlc();
        var plugins = FindLinuxPluginDirectory();
        if (library is null)
        {
            return new LibVlcRuntimeStatus(
                false,
                "Не найден libvlc.so.5.",
                null,
                plugins);
        }

        if (plugins is null)
        {
            return new LibVlcRuntimeStatus(
                false,
                "LibVLC найден, но не найдены VLC-плагины.",
                library,
                null);
        }

        return new LibVlcRuntimeStatus(
            true,
            "LibVLC и плагины найдены.",
            library,
            plugins);
    }

    private static void InitializeLinux()
    {
        var status = Inspect();
        if (!status.IsUsable)
        {
            throw new PlayerRuntimeException(CreateLinuxError(status));
        }

        Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", status.PluginDirectory);
        InstallLinuxResolver();
        Core.Initialize();
    }

    private static void InstallLinuxResolver()
    {
        if (_resolverInstalled)
        {
            return;
        }

        try
        {
            NativeLibrary.SetDllImportResolver(
                typeof(LibVLC).Assembly,
                ResolveLinuxLibrary);
        }
        catch (InvalidOperationException exception)
        {
            Debug.WriteLine(
                $"Резолвер LibVLC уже установлен; используется существующий: {exception.Message}");
        }

        _resolverInstalled = true;
    }

    private static nint ResolveLinuxLibrary(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, "libvlc", StringComparison.Ordinal))
        {
            return nint.Zero;
        }

        if (_linuxLibVlcHandle != nint.Zero)
        {
            return _linuxLibVlcHandle;
        }

        var libVlcPath = FindLinuxLibVlc();
        if (libVlcPath is null)
        {
            return nint.Zero;
        }

        var corePath = FindLinuxLibVlcCore(libVlcPath);
        if (corePath is not null)
        {
            _linuxLibVlcCoreHandle = Dlopen(
                corePath,
                RtldNow | RtldGlobal);
        }

        _linuxLibVlcHandle = Dlopen(libVlcPath, RtldNow | RtldGlobal);
        return _linuxLibVlcHandle;
    }

    private static string? FindLinuxLibVlc()
    {
        foreach (var candidate in GetLinuxLibraryCandidates())
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static string? FindLinuxLibVlcCore(string libVlcPath)
    {
        var directory = Path.GetDirectoryName(libVlcPath);
        if (directory is not null)
        {
            var local = Directory
                .EnumerateFiles(directory, "libvlccore.so.*", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path.Length)
                .FirstOrDefault();
            if (local is not null)
            {
                return local;
            }
        }

        return GetSystemLibraryDirectories()
            .SelectMany(directory =>
                Directory.Exists(directory)
                    ? Directory.EnumerateFiles(
                        directory,
                        "libvlccore.so.*",
                        SearchOption.TopDirectoryOnly)
                    : [])
            .OrderBy(path => path.Length)
            .FirstOrDefault();
    }

    private static string? FindLinuxPluginDirectory()
    {
        var configured = Environment.GetEnvironmentVariable("VLC_PLUGIN_PATH");
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            candidates.AddRange(
                configured.Split(
                    Path.PathSeparator,
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries));
        }

        var runtimeDirectory = GetBundledLinuxRuntimeDirectory();
        candidates.Add(Path.Combine(runtimeDirectory, "plugins"));
        candidates.Add(Path.Combine(runtimeDirectory, "vlc", "plugins"));
        candidates.AddRange(
            GetSystemLibraryDirectories()
                .Select(directory => Path.Combine(directory, "vlc", "plugins")));

        return candidates
            .Distinct(StringComparer.Ordinal)
            .FirstOrDefault(ContainsVlcPlugins);
    }

    private static bool ContainsVlcPlugins(string directory)
    {
        try
        {
            return Directory.Exists(directory) &&
                   Directory.EnumerateFiles(
                       directory,
                       "*_plugin.so",
                       SearchOption.AllDirectories)
                       .Any();
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IEnumerable<string> GetLinuxLibraryCandidates()
    {
        var runtimeDirectory = GetBundledLinuxRuntimeDirectory();
        yield return Path.Combine(runtimeDirectory, "libvlc.so.5");
        yield return Path.Combine(runtimeDirectory, "libvlc.so");

        foreach (var directory in GetSystemLibraryDirectories())
        {
            yield return Path.Combine(directory, "libvlc.so.5");
            yield return Path.Combine(directory, "libvlc.so");
        }
    }

    private static string GetBundledLinuxRuntimeDirectory()
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "linux-x64",
            Architecture.Arm64 => "linux-arm64",
            Architecture.Arm => "linux-arm",
            _ => $"linux-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}"
        };
        return Path.Combine(AppContext.BaseDirectory, "libvlc", architecture);
    }

    private static IEnumerable<string> GetSystemLibraryDirectories()
    {
        yield return "/usr/lib/x86_64-linux-gnu";
        yield return "/usr/lib/aarch64-linux-gnu";
        yield return "/usr/lib/arm-linux-gnueabihf";
        yield return "/usr/lib64";
        yield return "/usr/lib";
        yield return "/usr/local/lib";
    }

    private static string CreateLinuxError(LibVlcRuntimeStatus status)
    {
        if (status.LibraryPath is null)
        {
            return
                "Не найден движок LibVLC. Используйте сборку Rezui со встроенным " +
                "runtime или установите пакет libvlc.";
        }

        return
            $"LibVLC найден ({status.LibraryPath}), но каталог VLC-плагинов пуст. " +
            "Для Debian/Ubuntu нужен пакет vlc-plugin-base; " +
            "release-сборка Rezui должна содержать плагины внутри приложения.";
    }

    private const int RtldNow = 2;
    private const int RtldGlobal = 0x100;

    [DllImport("libdl.so.2", EntryPoint = "dlopen")]
    private static extern nint Dlopen(string fileName, int flags);
}

public sealed record LibVlcRuntimeStatus(
    bool IsUsable,
    string Message,
    string? LibraryPath,
    string? PluginDirectory);

public sealed class PlayerRuntimeException : Exception
{
    public PlayerRuntimeException(string message)
        : base(message)
    {
    }
}
