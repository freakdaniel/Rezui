using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Rezui.Services;
using Rezui.ViewModels;

namespace Rezui.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Activated += OnActivated;
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape
            && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.IsCategoryMenuOpen = false;
        }

        if (eventArgs.Key == Key.Escape && FocusManager?.GetFocusedElement() is TextBox)
        {
            FocusManager.Focus(null);
        }
    }

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        var source = eventArgs.Source as Visual;
        if (source is not null
            && DataContext is MainWindowViewModel viewModel)
        {
            if (viewModel.IsCategoryMenuOpen
                && !IsWithin(source, CategoryMenuPopup)
                && !IsWithin(source, FilmsNavButton)
                && !IsWithin(source, SeriesNavButton)
                && !IsWithin(source, CartoonsNavButton)
                && !IsWithin(source, AnimeNavButton))
            {
                viewModel.IsCategoryMenuOpen = false;
            }

            if (viewModel.IsProfilePopupOpen
                && !IsWithin(source, ProfilePopup)
                && !IsWithin(source, ProfileAvatarButton))
            {
                viewModel.IsProfilePopupOpen = false;
            }
        }

        if (FocusManager?.GetFocusedElement() is not TextBox)
        {
            return;
        }

        if (source?.FindAncestorOfType<TextBox>(includeSelf: true) is null)
        {
            FocusManager.Focus(null);
        }
    }

    private static bool IsWithin(Visual source, Visual container) =>
        ReferenceEquals(source, container) || container.IsVisualAncestorOf(source);

    private void OnActivated(object? sender, EventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.RequestLibraryRefresh(LibrarySyncReason.WindowActivated);
        }
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
