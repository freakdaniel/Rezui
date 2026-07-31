using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Rezui.Services;
using Rezui.ViewModels;
using Rezui.Views;

namespace Rezui;

public sealed partial class App : Application
{
    private RezkaClientService? _rezka;
    private ImageCacheService? _images;
    private PlayerViewModel? _player;
    private LibrarySyncWorker? _librarySync;
    private MirrorDiscoveryService? _mirrorDiscovery;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = new SettingsService();
            var themes = new ThemeService();
            _rezka = new RezkaClientService(settings);
            _images = new ImageCacheService();
            _player = new PlayerViewModel();
            _librarySync = new LibrarySyncWorker(_rezka);
            _mirrorDiscovery = new MirrorDiscoveryService();
            var viewModel = new MainWindowViewModel(
                settings,
                _rezka,
                _images,
                _player,
                themes,
                _librarySync,
                _mirrorDiscovery);

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel
            };
            desktop.Exit += (_, _) =>
            {
                viewModel.Dispose();
                _librarySync.Dispose();
                _player.Dispose();
                _images.Dispose();
                _mirrorDiscovery.Dispose();
                _rezka.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
