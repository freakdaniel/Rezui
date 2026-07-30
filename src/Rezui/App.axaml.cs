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

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = new SettingsService();
            _rezka = new RezkaClientService(settings);
            _images = new ImageCacheService();
            _player = new PlayerViewModel();
            var viewModel = new MainWindowViewModel(settings, _rezka, _images, _player);

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel
            };
            desktop.Exit += (_, _) =>
            {
                viewModel.Dispose();
                _player.Dispose();
                _images.Dispose();
                _rezka.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
