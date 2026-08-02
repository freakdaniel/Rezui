using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Rezui.Services;
using Rezui.ViewModels;

namespace Rezui.Views;

public sealed partial class MainWindow : Window
{
    private readonly Dictionary<Image, DispatcherTimer> _continueBackgroundTimers = [];
    private readonly Dictionary<ScrollViewer, SmoothScrollState> _smoothScrollStates = [];
    private readonly Cursor _autoScrollCursor = new(StandardCursorType.SizeNorthSouth);
    private AutoScrollState? _autoScrollState;
    private Cursor? _cursorBeforeAutoScroll;

    public MainWindow()
    {
        InitializeComponent();
        DetailsScrollViewer.PropertyChanged += OnDetailsScrollViewerPropertyChanged;
        Activated += OnActivated;
        Deactivated += OnDeactivated;
        Closed += OnClosed;
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnPreviewPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(
            PointerWheelChangedEvent,
            OnPreviewPointerWheelChanged,
            RoutingStrategies.Tunnel);
    }

    private void OnPreviewPointerWheelChanged(
        object? sender,
        PointerWheelEventArgs eventArgs)
    {
        StopAutoScroll();

        if (eventArgs.Source is not Visual source ||
            Math.Abs(eventArgs.Delta.Y) < double.Epsilon)
        {
            return;
        }

        var scrollViewer = source.FindAncestorOfType<ScrollViewer>(includeSelf: true);
        if (scrollViewer is null ||
            !scrollViewer.Classes.Contains("smooth-scroll") ||
            scrollViewer.Extent.Height <= scrollViewer.Viewport.Height)
        {
            return;
        }

        var state = GetSmoothScrollState(scrollViewer);
        state.AddWheelDelta(eventArgs.Delta.Y);
        eventArgs.Handled = true;
    }

    private void OnDetailsScrollViewerPropertyChanged(
        object? sender,
        AvaloniaPropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.Property != Visual.IsVisibleProperty ||
            !eventArgs.GetNewValue<bool>() ||
            sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        if (_smoothScrollStates.TryGetValue(scrollViewer, out var state))
        {
            state.ResetToTop();
        }
        else
        {
            scrollViewer.Offset = new Vector(0, 0);
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape && _autoScrollState is not null)
        {
            StopAutoScroll();
            eventArgs.Handled = true;
        }

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
        var pointerPoint = eventArgs.GetCurrentPoint(this);
        if (pointerPoint.Properties.PointerUpdateKind == PointerUpdateKind.MiddleButtonPressed)
        {
            if (_autoScrollState is not null)
            {
                StopAutoScroll();
            }
            else if (source?.FindAncestorOfType<ScrollViewer>(includeSelf: true)
                     is { } scrollViewer
                     && scrollViewer.Classes.Contains("smooth-scroll")
                     && scrollViewer.Extent.Height > scrollViewer.Viewport.Height)
            {
                StartAutoScroll(scrollViewer, eventArgs.GetPosition(this));
            }

            eventArgs.Handled = true;
            return;
        }

        StopAutoScroll();

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

    private void OnPreviewPointerMoved(object? sender, PointerEventArgs eventArgs) =>
        _autoScrollState?.UpdatePointer(eventArgs.GetPosition(this).Y);

    private SmoothScrollState GetSmoothScrollState(ScrollViewer scrollViewer)
    {
        if (_smoothScrollStates.TryGetValue(scrollViewer, out var state))
        {
            return state;
        }

        state = new SmoothScrollState(scrollViewer);
        _smoothScrollStates.Add(scrollViewer, state);
        return state;
    }

    private void StartAutoScroll(ScrollViewer scrollViewer, Point anchor)
    {
        var smoothScrollState = GetSmoothScrollState(scrollViewer);
        smoothScrollState.SyncToCurrentOffset();
        _autoScrollState = new AutoScrollState(scrollViewer, smoothScrollState, anchor.Y);
        _cursorBeforeAutoScroll = Cursor;
        Cursor = _autoScrollCursor;
    }

    private void StopAutoScroll()
    {
        if (_autoScrollState is null)
        {
            return;
        }

        _autoScrollState.Dispose();
        _autoScrollState = null;
        Cursor = _cursorBeforeAutoScroll;
        _cursorBeforeAutoScroll = null;
    }

    private static bool IsWithin(Visual source, Visual container) =>
        ReferenceEquals(source, container) || container.IsVisualAncestorOf(source);

    private void ContinueTitle_OnSizeChanged(object? sender, SizeChangedEventArgs eventArgs)
    {
        if (sender is not TextBlock title ||
            string.IsNullOrWhiteSpace(title.Text) ||
            eventArgs.NewSize.Width <= 0)
        {
            return;
        }

        const double largeFontSize = 54;
        const double regularFontSize = 34;
        var singleLineProbe = new TextBlock
        {
            Text = title.Text,
            FontFamily = title.FontFamily,
            FontWeight = title.FontWeight,
            FontStyle = title.FontStyle,
            FontStretch = title.FontStretch,
            FontSize = largeFontSize,
            TextWrapping = TextWrapping.NoWrap
        };
        singleLineProbe.Measure(
            new Size(double.PositiveInfinity, double.PositiveInfinity));

        var fitsOnOneLine =
            singleLineProbe.DesiredSize.Width <= eventArgs.NewSize.Width;
        var targetFontSize = fitsOnOneLine ? largeFontSize : regularFontSize;
        if (Math.Abs(title.FontSize - targetFontSize) < 0.01)
        {
            return;
        }

        title.FontSize = targetFontSize;
        title.LineHeight = fitsOnOneLine ? 60 : 40;
        title.MaxHeight = fitsOnOneLine ? 64 : 82;
        title.TextWrapping = fitsOnOneLine
            ? TextWrapping.NoWrap
            : TextWrapping.Wrap;
    }

    private void ContinueBackground_OnAttachedToVisualTree(
        object? sender,
        VisualTreeAttachmentEventArgs eventArgs)
    {
        if (sender is not Image image ||
            _continueBackgroundTimers.ContainsKey(image) ||
            image.RenderTransform is not TransformGroup transformGroup)
        {
            return;
        }

        var translation = transformGroup.Children
            .OfType<TranslateTransform>()
            .FirstOrDefault();
        if (translation is null)
        {
            return;
        }

        var startedAt = Stopwatch.GetTimestamp();
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        timer.Tick += (_, _) =>
        {
            var elapsed = Stopwatch.GetElapsedTime(startedAt).TotalSeconds;
            var angle = elapsed / 24 * Math.Tau;
            translation.X = Math.Cos(angle) * 7;
            translation.Y = Math.Sin(angle) * 5;
        };
        _continueBackgroundTimers.Add(image, timer);
        timer.Start();
    }

    private void ContinueBackground_OnDetachedFromVisualTree(
        object? sender,
        VisualTreeAttachmentEventArgs eventArgs)
    {
        if (sender is Image image &&
            _continueBackgroundTimers.Remove(image, out var timer))
        {
            timer.Stop();
        }
    }

    private void OnActivated(object? sender, EventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.RequestLibraryRefresh(LibrarySyncReason.WindowActivated);
        }
    }

    private void OnDeactivated(object? sender, EventArgs eventArgs) => StopAutoScroll();

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        DetailsScrollViewer.PropertyChanged -= OnDetailsScrollViewerPropertyChanged;
        StopAutoScroll();
        foreach (var state in _smoothScrollStates.Values)
        {
            state.Dispose();
        }

        _smoothScrollStates.Clear();
        foreach (var timer in _continueBackgroundTimers.Values)
        {
            timer.Stop();
        }

        _continueBackgroundTimers.Clear();
        _autoScrollCursor.Dispose();
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

    private sealed class SmoothScrollState : IDisposable
    {
        private const double WheelStep = 62;
        private const double Response = 22;

        private readonly ScrollViewer _scrollViewer;
        private readonly TopLevel? _topLevel;
        private double _targetY;
        private TimeSpan _lastFrameTime;
        private bool _framePending;
        private bool _disposed;

        public SmoothScrollState(ScrollViewer scrollViewer)
        {
            _scrollViewer = scrollViewer;
            _topLevel = TopLevel.GetTopLevel(scrollViewer);
            _targetY = scrollViewer.Offset.Y;
        }

        public void AddWheelDelta(double deltaY)
        {
            var maximum = Math.Max(
                0,
                _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);
            if (!_framePending)
            {
                _targetY = _scrollViewer.Offset.Y;
            }

            _targetY = Math.Clamp(
                _targetY - deltaY * WheelStep,
                0,
                maximum);
            if (Math.Abs(_targetY - _scrollViewer.Offset.Y) < 0.1)
            {
                return;
            }

            RequestNextFrame();
        }

        public void ResetToTop()
        {
            _targetY = 0;
            _lastFrameTime = default;
            _scrollViewer.Offset = new Vector(0, 0);
        }

        public void SyncToCurrentOffset()
        {
            _targetY = _scrollViewer.Offset.Y;
            _lastFrameTime = default;
        }

        private void RequestNextFrame()
        {
            if (_disposed || _framePending)
            {
                return;
            }

            if (_topLevel is null)
            {
                _scrollViewer.Offset = new Vector(
                    _scrollViewer.Offset.X,
                    _targetY);
                return;
            }

            _framePending = true;
            _topLevel.RequestAnimationFrame(OnAnimationFrame);
        }

        private void OnAnimationFrame(TimeSpan timestamp)
        {
            _framePending = false;
            if (_disposed)
            {
                return;
            }

            var maximum = Math.Max(
                0,
                _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);
            _targetY = Math.Clamp(_targetY, 0, maximum);
            var distance = _targetY - _scrollViewer.Offset.Y;
            if (Math.Abs(distance) <= 0.35)
            {
                _scrollViewer.Offset = new Vector(
                    _scrollViewer.Offset.X,
                    _targetY);
                _lastFrameTime = default;
                return;
            }

            var elapsedSeconds = _lastFrameTime == default
                ? 1d / 60
                : Math.Clamp((timestamp - _lastFrameTime).TotalSeconds, 1d / 240, 1d / 30);
            _lastFrameTime = timestamp;
            var blend = 1 - Math.Exp(-Response * elapsedSeconds);
            _scrollViewer.Offset = new Vector(
                _scrollViewer.Offset.X,
                _scrollViewer.Offset.Y + distance * blend);
            RequestNextFrame();
        }

        public void Dispose() => _disposed = true;
    }

    private sealed class AutoScrollState : IDisposable
    {
        private readonly ScrollViewer _scrollViewer;
        private readonly SmoothScrollState _smoothScrollState;
        private readonly TopLevel? _topLevel;
        private readonly double _anchorY;
        private double _pointerY;
        private TimeSpan _lastFrameTime;
        private bool _framePending;
        private bool _disposed;

        public AutoScrollState(
            ScrollViewer scrollViewer,
            SmoothScrollState smoothScrollState,
            double anchorY)
        {
            _scrollViewer = scrollViewer;
            _smoothScrollState = smoothScrollState;
            _topLevel = TopLevel.GetTopLevel(scrollViewer);
            _anchorY = anchorY;
            _pointerY = anchorY;
            RequestNextFrame();
        }

        public void UpdatePointer(double pointerY) => _pointerY = pointerY;

        private void RequestNextFrame()
        {
            if (_disposed || _framePending || _topLevel is null)
            {
                return;
            }

            _framePending = true;
            _topLevel.RequestAnimationFrame(OnAnimationFrame);
        }

        private void OnAnimationFrame(TimeSpan timestamp)
        {
            _framePending = false;
            if (_disposed)
            {
                return;
            }

            var elapsedSeconds = _lastFrameTime == default
                ? 1d / 60
                : Math.Clamp((timestamp - _lastFrameTime).TotalSeconds, 1d / 240, 1d / 20);
            _lastFrameTime = timestamp;

            var velocity = AutoScrollPhysics.CalculateVelocity(_pointerY - _anchorY);
            if (Math.Abs(velocity) > double.Epsilon)
            {
                var maximum = Math.Max(
                    0,
                    _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);
                var targetY = Math.Clamp(
                    _scrollViewer.Offset.Y + velocity * elapsedSeconds,
                    0,
                    maximum);
                _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, targetY);
            }

            RequestNextFrame();
        }

        public void Dispose()
        {
            _disposed = true;
            _smoothScrollState.SyncToCurrentOffset();
        }
    }
}
