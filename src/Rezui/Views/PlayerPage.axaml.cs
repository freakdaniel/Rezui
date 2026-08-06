using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Rezui.ViewModels;

namespace Rezui.Views;

/// <summary>
/// Player page. Owns the two interactions that previously lived on the
/// window: committing a seek when the scrub slider is released, and toggling
/// the host window between normal and fullscreen.
/// </summary>
public partial class PlayerPage : UserControl
{
    public PlayerPage() => InitializeComponent();

    private void SeekSlider_OnPointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        if (sender is Slider slider && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Player.Seek((long)slider.Value);
        }
    }

    private void FullscreenButton_OnClick(object? sender, RoutedEventArgs eventArgs)
    {
        if (this.GetSelfAndVisualAncestors().OfType<Window>().FirstOrDefault() is not { } window)
        {
            return;
        }

        window.WindowState = window.WindowState == WindowState.FullScreen
            ? WindowState.Normal
            : WindowState.FullScreen;
    }
}
