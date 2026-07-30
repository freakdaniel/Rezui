using Avalonia.Controls;
using Avalonia.Input;
using Rezui.ViewModels;

namespace Rezui.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void SeekSlider_OnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        if (sender is Slider slider && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Player.Seek((long)slider.Value);
        }
    }

    private void FullscreenButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs eventArgs)
    {
        WindowState = WindowState == WindowState.FullScreen
            ? WindowState.Normal
            : WindowState.FullScreen;
    }
}
