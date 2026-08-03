using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Rezui.Services;
using Rezui.ViewModels;
using Rezui.Views;
using Serilog;

namespace Rezui;

public sealed partial class App : Application
{
    private RezkaClientService? _rezka;
    private LocalCacheStore? _cache;
    private ImageCacheService? _images;
    private PlayerViewModel? _player;
    private LibrarySyncWorker? _librarySync;
    private MirrorDiscoveryService? _mirrorDiscovery;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var logger = Log.ForContext<App>();
            logger.Information("Initializing desktop application services");
            _cache = new LocalCacheStore();
            var settings = new SettingsService(cache: _cache);
            var themes = new ThemeService();
            _rezka = new RezkaClientService(
                settings,
                _cache,
                Log.ForContext<RezkaClientService>());
            _images = new ImageCacheService(_cache);
            _player = new PlayerViewModel();
            _librarySync = new LibrarySyncWorker(
                _rezka,
                Log.ForContext<LibrarySyncWorker>());
            _mirrorDiscovery = new MirrorDiscoveryService(
                logger: Log.ForContext<MirrorDiscoveryService>());
            var viewModel = new MainWindowViewModel(
                settings,
                _rezka,
                _images,
                _player,
                themes,
                _librarySync,
                _mirrorDiscovery,
                Log.ForContext<MainWindowViewModel>());

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel
            };
            desktop.Exit += (_, _) =>
            {
                logger.Information("Disposing desktop application services");
                viewModel.Dispose();
                _librarySync.Dispose();
                _player.Dispose();
                _images.Dispose();
                _mirrorDiscovery.Dispose();
                _rezka.Dispose();
                _cache.Dispose();
                logger.Information("Desktop application services disposed");
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
