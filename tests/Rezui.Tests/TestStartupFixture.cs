using Rezui.Models;
using Rezui.Services;
using Rezui.ViewModels;

namespace Rezui.Tests;

/// <summary>
/// Shared wiring for tests that need a real MainWindowViewModel backed by an
/// in-process mirror discovery stub. Extracted so both StartupFlowTests and
/// MainWindowSmokeTests can build the layered window with a working DataContext.
/// </summary>
internal sealed class TestStartupFixture : IDisposable
{
    private readonly TemporaryDirectory _directory = new();
    private readonly ImageCacheService _images = new();
    private readonly PlayerViewModel _player = new();
    private RezkaClientService? _rezka;
    private LibrarySyncWorker? _librarySync;
    private MainWindowViewModel? _viewModel;

    public TestStartupFixture()
    {
        Settings = new SettingsService(_directory.Path);
        Mirrors = new FakeMirrorDiscoveryService();
    }

    public SettingsService Settings { get; }

    public FakeMirrorDiscoveryService Mirrors { get; }

    public MainWindowViewModel CreateViewModel()
    {
        _rezka = new RezkaClientService(Settings);
        _librarySync = new LibrarySyncWorker(_rezka);
        _viewModel = new MainWindowViewModel(
            Settings,
            _rezka,
            _images,
            _player,
            new ThemeService(),
            _librarySync,
            Mirrors);
        return _viewModel;
    }

    public void Dispose()
    {
        _viewModel?.Dispose();
        _librarySync?.Dispose();
        _player.Dispose();
        _images.Dispose();
        _rezka?.Dispose();
        _directory.Dispose();
    }
}

internal sealed class FakeMirrorDiscoveryService : IMirrorDiscoveryService
{
    public Dictionary<string, long> Latencies { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        [RezkaMirrors.Primary] = 12,
        ["https://rezka.fi"] = 24,
        ["https://hdrezka.cm"] = 36
    };

    public bool AllUnavailable { get; set; }

    public bool RejectRezkaValidation { get; set; }

    public Task<bool> IsRezkaMirrorAsync(
        string origin,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(!RejectRezkaValidation);
    }

    public Task<IReadOnlyList<MirrorProbeResult>> ProbeAsync(
        IEnumerable<string> origins,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<MirrorProbeResult> results = origins
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(origin =>
            {
                var normalized = RezkaClientService.NormalizeOrigin(origin);
                var normalizedOrigin = normalized.AbsoluteUri.TrimEnd('/');
                var latency = Latencies.GetValueOrDefault(normalizedOrigin, 80);
                return new MirrorProbeResult(
                    normalizedOrigin,
                    normalized.Host,
                    AllUnavailable ? null : latency,
                    !AllUnavailable);
            })
            .ToArray();
        return Task.FromResult(results);
    }
}
