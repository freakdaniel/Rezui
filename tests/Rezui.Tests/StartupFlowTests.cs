using Rezui.Models;
using Rezui.Services;
using Rezui.ViewModels;
using Xunit;

namespace Rezui.Tests;

public sealed class StartupFlowTests
{
    [Fact]
    public async Task StartupUsesDefaultMirrorAndRequestsLoginWhenSettingsAreMissing()
    {
        using var fixture = new StartupFixture();
        var viewModel = fixture.CreateViewModel();

        await viewModel.Initialization;

        Assert.True(viewModel.IsStartupVisible);
        Assert.True(viewModel.IsStartupAuthenticationRequired);
        Assert.Equal(RezkaMirrors.Primary, viewModel.Origin);
        Assert.Equal(3, viewModel.MirrorStatuses.Count);
        Assert.True(viewModel.CanLogin);
        Assert.Equal("Зеркало: автовыбор", viewModel.MirrorSelectorLabel);
    }

    [Fact]
    public async Task StartupSelectsAvailableMirrorWithLowestLatency()
    {
        using var fixture = new StartupFixture();
        fixture.Mirrors.Latencies[RezkaMirrors.Primary] = 45;
        fixture.Mirrors.Latencies["https://rezka.fi"] = 9;
        fixture.Mirrors.Latencies["https://hdrezka.cm"] = 28;
        var viewModel = fixture.CreateViewModel();

        await viewModel.Initialization;

        Assert.Equal("https://rezka.fi", viewModel.Origin);
        Assert.True(viewModel.IsAutoMirrorSelection);
        Assert.DoesNotContain(viewModel.MirrorStatuses, item => item.IsSelected);
    }

    [Fact]
    public async Task StartupBlocksLoginWhenEveryMirrorIsUnavailable()
    {
        using var fixture = new StartupFixture();
        fixture.Mirrors.AllUnavailable = true;
        var viewModel = fixture.CreateViewModel();

        await viewModel.Initialization;

        Assert.False(viewModel.CanLogin);
        Assert.True(viewModel.IsStartupAuthenticationRequired);
        Assert.All(viewModel.MirrorStatuses, item => Assert.False(item.IsAvailable));
    }

    [Fact]
    public async Task MirrorWizardMovesForwardAndBackFromTheLoginStep()
    {
        using var fixture = new StartupFixture();
        var viewModel = fixture.CreateViewModel();
        await viewModel.Initialization;

        Assert.True(viewModel.IsLoginWizardStep);
        Assert.False(viewModel.IsMirrorWizardStep);

        viewModel.OpenMirrorWizardCommand.Execute(null);

        Assert.Equal(1, viewModel.StartupWizardStep);
        Assert.True(viewModel.IsMirrorWizardStep);

        viewModel.CloseMirrorWizardCommand.Execute(null);

        Assert.Equal(0, viewModel.StartupWizardStep);
        Assert.True(viewModel.IsLoginWizardStep);
    }

    [Fact]
    public async Task MirrorWizardCannotOpenWhileConnectionCheckIsRunning()
    {
        using var fixture = new StartupFixture();
        var viewModel = fixture.CreateViewModel();
        await viewModel.Initialization;
        viewModel.IsMirrorCheckRunning = true;

        viewModel.OpenMirrorWizardCommand.Execute(null);

        Assert.Equal(0, viewModel.StartupWizardStep);
    }

    [Fact]
    public async Task AvailableCustomMirrorIsUsedAndPersisted()
    {
        using var fixture = new StartupFixture();
        fixture.Mirrors.AllUnavailable = true;
        var viewModel = fixture.CreateViewModel();
        await viewModel.Initialization;
        fixture.Mirrors.AllUnavailable = false;
        viewModel.OpenMirrorWizardCommand.Execute(null);
        viewModel.CustomMirror = "custom.example.com";

        await viewModel.UseCustomMirrorCommand.ExecuteAsync(null);
        var settings = await fixture.Settings.LoadAsync(
            TestContext.Current.CancellationToken);

        Assert.True(viewModel.CanLogin);
        Assert.Equal("https://custom.example.com", viewModel.Origin);
        Assert.Equal("Зеркало: custom.example.com", viewModel.MirrorSelectorLabel);
        Assert.True(viewModel.IsLoginWizardStep);
        Assert.Contains("https://custom.example.com", settings.CustomMirrors);
    }

    [Fact]
    public async Task UnavailableCustomMirrorIsNotAddedOrSelected()
    {
        using var fixture = new StartupFixture();
        fixture.Mirrors.AllUnavailable = true;
        var viewModel = fixture.CreateViewModel();
        await viewModel.Initialization;
        viewModel.OpenMirrorWizardCommand.Execute(null);
        viewModel.CustomMirror = "offline.example.com";

        await viewModel.UseCustomMirrorCommand.ExecuteAsync(null);
        var settings = await fixture.Settings.LoadAsync(
            TestContext.Current.CancellationToken);

        Assert.False(viewModel.CanLogin);
        Assert.Equal(string.Empty, viewModel.Origin);
        Assert.DoesNotContain("https://offline.example.com", settings.CustomMirrors);
        Assert.DoesNotContain(
            viewModel.MirrorStatuses,
            item => item.Origin == "https://offline.example.com");
    }

    [Fact]
    public async Task StartupRequiresLoginWhenSessionCookiesAreMissing()
    {
        using var fixture = new StartupFixture();
        await fixture.Settings.SaveAsync(
            new AppSettings
            {
                Origin = "https://example.com"
            },
            TestContext.Current.CancellationToken);
        var viewModel = fixture.CreateViewModel();

        await viewModel.Initialization;

        Assert.True(viewModel.IsStartupVisible);
        Assert.True(viewModel.IsStartupAuthenticationRequired);
        Assert.False(viewModel.IsShellVisible);
    }

    private sealed class StartupFixture : IDisposable
    {
        private readonly TemporaryDirectory _directory = new();
        private readonly ImageCacheService _images = new();
        private readonly PlayerViewModel _player = new();
        private RezkaClientService? _rezka;
        private LibrarySyncWorker? _librarySync;
        private MainWindowViewModel? _viewModel;

        public StartupFixture()
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

    public sealed class FakeMirrorDiscoveryService : IMirrorDiscoveryService
    {
        public Dictionary<string, long> Latencies { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            [RezkaMirrors.Primary] = 12,
            ["https://rezka.fi"] = 24,
            ["https://hdrezka.cm"] = 36
        };

        public bool AllUnavailable { get; set; }

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
}
