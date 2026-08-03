using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Rendering;
using Avalonia.VisualTree;
using Rezui.Services;
using Rezui.ViewModels;

namespace Rezui.Views;

public sealed partial class MainWindow : Window
{
    private const RendererDebugOverlays PerformanceOverlays =
        RendererDebugOverlays.Fps |
        RendererDebugOverlays.DirtyRects |
        RendererDebugOverlays.LayoutTimeGraph |
        RendererDebugOverlays.RenderTimeGraph;

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
        Opened += OnOpened;
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
#if DEBUG
        if (eventArgs.Key == Key.F10)
        {
            TogglePerformanceOverlays();
            eventArgs.Handled = true;
            return;
        }
#endif

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

    private void OnActivated(object? sender, EventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.RequestLibraryRefresh(LibrarySyncReason.WindowActivated);
        }
    }

    private void OnOpened(object? sender, EventArgs eventArgs)
    {
#if DEBUG
        if (string.Equals(
                Environment.GetEnvironmentVariable("REZUI_PERF_OVERLAY"),
                "1",
                StringComparison.Ordinal))
        {
            RendererDiagnostics.DebugOverlays = PerformanceOverlays;
        }
#endif
    }

#if DEBUG
    private void TogglePerformanceOverlays()
    {
        RendererDiagnostics.DebugOverlays =
            RendererDiagnostics.DebugOverlays == RendererDebugOverlays.None
                ? PerformanceOverlays
                : RendererDebugOverlays.None;
    }
#endif

    private void OnDeactivated(object? sender, EventArgs eventArgs) => StopAutoScroll();

    private void OnClosed(object? sender, EventArgs eventArgs)
    {
        DetailsScrollViewer.PropertyChanged -= OnDetailsScrollViewerPropertyChanged;
        Opened -= OnOpened;
        StopAutoScroll();
        foreach (var state in _smoothScrollStates.Values)
        {
            state.Dispose();
        }

        _smoothScrollStates.Clear();
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
        private bool _isApplyingOffset;
        private bool _disposed;

        public SmoothScrollState(ScrollViewer scrollViewer)
        {
            _scrollViewer = scrollViewer;
            _topLevel = TopLevel.GetTopLevel(scrollViewer);
            _targetY = scrollViewer.Offset.Y;
            _scrollViewer.PropertyChanged += OnScrollViewerPropertyChanged;
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

            // Bound both a single wheel impulse and the accumulated distance
            // between the animated offset and its target. Without the latter,
            // a long gesture can build a large backlog and visibly jump when
            // the animation catches up.
            _targetY = SmoothScrollPhysics.CalculateTarget(
                _scrollViewer.Offset.Y,
                _targetY,
                deltaY,
                WheelStep,
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
            SetOffset(new Vector(0, 0));
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
                SetOffset(new Vector(
                    _scrollViewer.Offset.X,
                    _targetY));
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
                SetOffset(new Vector(
                    _scrollViewer.Offset.X,
                    _targetY));
                _lastFrameTime = default;
                return;
            }

            var elapsedSeconds = _lastFrameTime == default
                ? 1d / 60
                : Math.Clamp((timestamp - _lastFrameTime).TotalSeconds, 1d / 240, 1d / 30);
            _lastFrameTime = timestamp;
            var blend = 1 - Math.Exp(-Response * elapsedSeconds);
            SetOffset(new Vector(
                _scrollViewer.Offset.X,
                _scrollViewer.Offset.Y + distance * blend));
            RequestNextFrame();
        }

        private void OnScrollViewerPropertyChanged(
            object? sender,
            AvaloniaPropertyChangedEventArgs eventArgs)
        {
            if (_disposed ||
                _isApplyingOffset ||
                eventArgs.Property != ScrollViewer.OffsetProperty)
            {
                return;
            }

            // Native gestures, scrollbars and layout anchoring can all update
            // Offset independently. Continuing toward the old target after
            // that update is perceived as a sudden jump back or forward.
            _targetY = eventArgs.GetNewValue<Vector>().Y;
            _lastFrameTime = default;
        }

        private void SetOffset(Vector offset)
        {
            _isApplyingOffset = true;
            try
            {
                _scrollViewer.Offset = offset;
            }
            finally
            {
                _isApplyingOffset = false;
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _scrollViewer.PropertyChanged -= OnScrollViewerPropertyChanged;
        }
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
