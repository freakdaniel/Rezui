using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Rezui.Models;

namespace Rezui.Controls;

public enum MasonryEntranceDirection
{
    Top,
    Bottom,
    Left,
    Right,
    Center,
    Alternating
}

/// <summary>
/// Avalonia adaptation of React Bits Masonry. Children are arranged at the
/// origin and positioned with animated transforms, mirroring absolutely
/// positioned HTML items animated by GSAP. Reflows therefore animate between
/// columns instead of snapping or participating in uniform rows.
/// </summary>
public sealed class ReactBitsMasonryPanel : Panel
{
    public static readonly StyledProperty<double> ColumnSpacingProperty =
        AvaloniaProperty.Register<ReactBitsMasonryPanel, double>(
            nameof(ColumnSpacing),
            18,
            validate: value => value >= 0);

    public static readonly StyledProperty<double> RowSpacingProperty =
        AvaloniaProperty.Register<ReactBitsMasonryPanel, double>(
            nameof(RowSpacing),
            18,
            validate: value => value >= 0);

    public static readonly StyledProperty<int> MaxColumnCountProperty =
        AvaloniaProperty.Register<ReactBitsMasonryPanel, int>(
            nameof(MaxColumnCount),
            5,
            validate: value => value > 0);

    public static readonly StyledProperty<TimeSpan> AnimationDurationProperty =
        AvaloniaProperty.Register<ReactBitsMasonryPanel, TimeSpan>(
            nameof(AnimationDuration),
            TimeSpan.FromMilliseconds(620));

    public static readonly StyledProperty<TimeSpan> StaggerProperty =
        AvaloniaProperty.Register<ReactBitsMasonryPanel, TimeSpan>(
            nameof(Stagger),
            TimeSpan.FromMilliseconds(42));

    public static readonly StyledProperty<double> EntranceDistanceProperty =
        AvaloniaProperty.Register<ReactBitsMasonryPanel, double>(
            nameof(EntranceDistance),
            86,
            validate: value => value >= 0);

    public static readonly StyledProperty<MasonryEntranceDirection> AnimateFromProperty =
        AvaloniaProperty.Register<ReactBitsMasonryPanel, MasonryEntranceDirection>(
            nameof(AnimateFrom),
            MasonryEntranceDirection.Bottom);

    public static readonly StyledProperty<bool> BlurToFocusProperty =
        AvaloniaProperty.Register<ReactBitsMasonryPanel, bool>(
            nameof(BlurToFocus),
            true);

    private readonly Dictionary<Control, ItemMotionState> _motionStates = [];

    static ReactBitsMasonryPanel()
    {
        AffectsMeasure<ReactBitsMasonryPanel>(
            ColumnSpacingProperty,
            RowSpacingProperty,
            MaxColumnCountProperty);
    }

    public double ColumnSpacing
    {
        get => GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public double RowSpacing
    {
        get => GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    public int MaxColumnCount
    {
        get => GetValue(MaxColumnCountProperty);
        set => SetValue(MaxColumnCountProperty, value);
    }

    public TimeSpan AnimationDuration
    {
        get => GetValue(AnimationDurationProperty);
        set => SetValue(AnimationDurationProperty, value);
    }

    public TimeSpan Stagger
    {
        get => GetValue(StaggerProperty);
        set => SetValue(StaggerProperty, value);
    }

    public double EntranceDistance
    {
        get => GetValue(EntranceDistanceProperty);
        set => SetValue(EntranceDistanceProperty, value);
    }

    public MasonryEntranceDirection AnimateFrom
    {
        get => GetValue(AnimateFromProperty);
        set => SetValue(AnimateFromProperty, value);
    }

    public bool BlurToFocus
    {
        get => GetValue(BlurToFocusProperty);
        set => SetValue(BlurToFocusProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = ResolveWidth(availableSize.Width);
        var placements = CalculatePlacements(width, measureChildren: true);
        return new Size(width, CalculateExtentHeight(placements));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var placements = CalculatePlacements(finalSize.Width, measureChildren: false);
        for (var index = 0; index < placements.Count; index++)
        {
            var placement = placements[index];
            placement.Child.Arrange(new Rect(0, 0, placement.Width, placement.Height));
            MoveToPlacement(placement, index, finalSize);
        }

        RemoveDetachedStates();
        return new Size(finalSize.Width, CalculateExtentHeight(placements));
    }

    private IReadOnlyList<ItemPlacement> CalculatePlacements(
        double width,
        bool measureChildren)
    {
        var columnCount = CalculateColumnCount(width);
        var columnWidth = Math.Max(
            0,
            (width - ColumnSpacing * (columnCount - 1)) / columnCount);
        var columnHeights = new double[columnCount];
        var placements = new List<ItemPlacement>(Children.Count);

        foreach (var child in Children)
        {
            var modelHeight = GetModelHeight(child);
            if (measureChildren)
            {
                child.Measure(new Size(
                    columnWidth,
                    modelHeight ?? double.PositiveInfinity));
            }

            var height = modelHeight ?? child.DesiredSize.Height;
            var column = FindShortestColumn(columnHeights);
            var x = column * (columnWidth + ColumnSpacing);
            var y = columnHeights[column];
            placements.Add(new ItemPlacement(child, x, y, columnWidth, height));
            columnHeights[column] += height + RowSpacing;
        }

        return placements;
    }

    private void MoveToPlacement(ItemPlacement placement, int index, Size panelSize)
    {
        if (!_motionStates.TryGetValue(placement.Child, out var state))
        {
            state = CreateMotionState(placement, index, panelSize);
            _motionStates.Add(placement.Child, state);
            placement.Child.Opacity = 1;
            state.Position.X = placement.X;
            state.Position.Y = placement.Y;
            if (state.Blur is not null)
            {
                state.Blur.Radius = 0;
            }
            return;
        }

        state.Position.X = placement.X;
        state.Position.Y = placement.Y;
    }

    private ItemMotionState CreateMotionState(
        ItemPlacement placement,
        int index,
        Size panelSize)
    {
        var initial = GetInitialPosition(placement, index, panelSize);
        var position = new TranslateTransform(initial.X, initial.Y);
        var blur = BlurToFocus ? new BlurEffect { Radius = 9 } : null;
        placement.Child.Opacity = 0;
        placement.Child.RenderTransform = position;
        placement.Child.RenderTransformOrigin = RelativePoint.Center;
        placement.Child.Effect = blur;

        var easing = new CubicEaseOut();
        var cascadeDuration = AnimationDuration +
                              TimeSpan.FromTicks(Stagger.Ticks * Math.Min(index, 7));
        position.Transitions =
        [
            new DoubleTransition
            {
                Property = TranslateTransform.XProperty,
                Duration = cascadeDuration,
                Easing = easing
            },
            new DoubleTransition
            {
                Property = TranslateTransform.YProperty,
                Duration = cascadeDuration,
                Easing = easing
            }
        ];
        placement.Child.Transitions =
        [
            new DoubleTransition
            {
                Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(420),
                Easing = easing
            }
        ];

        if (blur is not null)
        {
            blur.Transitions =
            [
                new DoubleTransition
                {
                    Property = BlurEffect.RadiusProperty,
                    Duration = TimeSpan.FromMilliseconds(520),
                    Easing = easing
                }
            ];
        }

        return new ItemMotionState(position, blur);
    }

    private Point GetInitialPosition(ItemPlacement placement, int index, Size panelSize)
    {
        var direction = AnimateFrom == MasonryEntranceDirection.Alternating
            ? (index % 4) switch
            {
                0 => MasonryEntranceDirection.Bottom,
                1 => MasonryEntranceDirection.Left,
                2 => MasonryEntranceDirection.Right,
                _ => MasonryEntranceDirection.Top
            }
            : AnimateFrom;
        return direction switch
        {
            MasonryEntranceDirection.Top =>
                new Point(placement.X, placement.Y - EntranceDistance),
            MasonryEntranceDirection.Left =>
                new Point(placement.X - EntranceDistance, placement.Y),
            MasonryEntranceDirection.Right =>
                new Point(placement.X + EntranceDistance, placement.Y),
            MasonryEntranceDirection.Center =>
                new Point(
                    panelSize.Width / 2 - placement.Width / 2,
                    panelSize.Height / 2 - placement.Height / 2),
            _ => new Point(placement.X, placement.Y + EntranceDistance)
        };
    }

    private int CalculateColumnCount(double width)
    {
        var responsiveCount = width switch
        {
            >= 1120 => 5,
            >= 880 => 4,
            >= 640 => 3,
            >= 420 => 2,
            _ => 1
        };
        return Math.Clamp(
            responsiveCount,
            1,
            Math.Min(MaxColumnCount, Math.Max(Children.Count, 1)));
    }

    private double ResolveWidth(double availableWidth)
    {
        if (!double.IsInfinity(availableWidth) && !double.IsNaN(availableWidth))
        {
            return Math.Max(0, availableWidth);
        }

        return Math.Max(1, Math.Min(Children.Count, MaxColumnCount)) * 220 +
               Math.Max(0, Math.Min(Children.Count, MaxColumnCount) - 1) * ColumnSpacing;
    }

    private static double? GetModelHeight(Control child)
    {
        // ItemsControl gives an items panel generated ContentPresenters. Their
        // DataContext is not guaranteed to be the item during the first measure,
        // while Content is already populated. Reading both prevents the initial
        // template desired height (often just a few pixels) from becoming the
        // permanent masonry height.
        var item = child.DataContext as IMasonryItem ??
                   (child as ContentPresenter)?.Content as IMasonryItem ??
                   (child as ContentControl)?.Content as IMasonryItem;
        return item is { MasonryHeight: > 0 } ? item.MasonryHeight : null;
    }

    private static int FindShortestColumn(IReadOnlyList<double> heights)
    {
        var result = 0;
        for (var index = 1; index < heights.Count; index++)
        {
            if (heights[index] < heights[result])
            {
                result = index;
            }
        }

        return result;
    }

    private double CalculateExtentHeight(IReadOnlyList<ItemPlacement> placements) =>
        placements.Count == 0
            ? 0
            : placements.Max(item => item.Y + item.Height);

    private void RemoveDetachedStates()
    {
        if (_motionStates.Count == Children.Count)
        {
            return;
        }

        var children = Children.ToHashSet();
        foreach (var child in _motionStates.Keys.Where(child => !children.Contains(child)).ToArray())
        {
            _motionStates.Remove(child);
        }
    }

    private sealed record ItemPlacement(
        Control Child,
        double X,
        double Y,
        double Width,
        double Height);

    private sealed class ItemMotionState(
        TranslateTransform position,
        BlurEffect? blur)
    {
        public TranslateTransform Position { get; } = position;

        public BlurEffect? Blur { get; } = blur;

    }
}
