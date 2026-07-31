using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Rezui.Services;
using Rezui.ViewModels;

namespace Rezui.Views;

public sealed partial class MainWindow : Window
{
    private const double TopMaskCaptureHeight = 166;
    private RenderTargetBitmap? _topMaskSnapshot;
    private INotifyPropertyChanged? _observedViewModel;
    private bool _isTopMaskRefreshQueued;

    public MainWindow()
    {
        InitializeComponent();
        Activated += OnActivated;
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        DataContextChanged += OnDataContextChanged;
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
        Closed += OnClosed;
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape && FocusManager?.GetFocusedElement() is TextBox)
        {
            FocusManager.Focus(null);
        }
    }

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (FocusManager?.GetFocusedElement() is not TextBox)
        {
            return;
        }

        var source = eventArgs.Source as Visual;
        if (source?.FindAncestorOfType<TextBox>(includeSelf: true) is null)
        {
            FocusManager.Focus(null);
        }
    }

    private void OnActivated(object? sender, EventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.RequestLibraryRefresh(LibrarySyncReason.WindowActivated);
        }

        QueueTopMaskRefresh();
    }

    private void OnLoaded(object? sender, EventArgs eventArgs) =>
        QueueTopMaskRefresh();

    private void OnSizeChanged(object? sender, SizeChangedEventArgs eventArgs) =>
        QueueTopMaskRefresh();

    private void OnActualThemeVariantChanged(object? sender, EventArgs eventArgs) =>
        QueueTopMaskRefresh();

    private void OnDataContextChanged(object? sender, EventArgs eventArgs)
    {
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _observedViewModel = DataContext as INotifyPropertyChanged;
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        QueueTopMaskRefresh();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(MainWindowViewModel.IsShellVisible)
            or nameof(MainWindowViewModel.IsHomeVisible)
            or nameof(MainWindowViewModel.IsLibraryVisible)
            or nameof(MainWindowViewModel.IsSettingsVisible)
            or nameof(MainWindowViewModel.IsDetailsVisible)
            or nameof(MainWindowViewModel.SelectedTheme))
        {
            QueueTopMaskRefresh();
        }
    }

    private void ShellScrollViewer_OnScrollChanged(
        object? sender,
        ScrollChangedEventArgs eventArgs) =>
        QueueTopMaskRefresh();

    private void QueueTopMaskRefresh()
    {
        if (_isTopMaskRefreshQueued)
        {
            return;
        }

        _isTopMaskRefreshQueued = true;
        Dispatcher.UIThread.Post(RenderTopMaskSnapshot, DispatcherPriority.Render);
    }

    private void RenderTopMaskSnapshot()
    {
        _isTopMaskRefreshQueued = false;

        var logicalWidth = ShellPages.Bounds.Width;
        if (logicalWidth <= 0 || !ShellPages.IsEffectivelyVisible)
        {
            return;
        }

        var scale = RenderScaling;
        var pixelSize = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(logicalWidth * scale)),
            Math.Max(1, (int)Math.Ceiling(TopMaskCaptureHeight * scale)));

        if (_topMaskSnapshot?.PixelSize != pixelSize)
        {
            _topMaskSnapshot?.Dispose();
            _topMaskSnapshot = new RenderTargetBitmap(
                pixelSize,
                new Vector(96 * scale, 96 * scale));
            TopMaskImage.Source = _topMaskSnapshot;
        }

        _topMaskSnapshot.Render(ShellPages);
        TopMaskImage.InvalidateVisual();
    }

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _topMaskSnapshot?.Dispose();
        _topMaskSnapshot = null;
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
