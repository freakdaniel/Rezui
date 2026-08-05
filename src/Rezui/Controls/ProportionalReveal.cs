using Avalonia;
using Avalonia.Controls;

namespace Rezui.Controls;

/// <summary>
/// Reveals its child by a normalized progress value while measuring the child
/// at its full natural height. The animation duration therefore stays the same
/// regardless of how many metadata rows the child contains.
/// </summary>
public sealed class ProportionalReveal : Decorator
{
    public static readonly StyledProperty<double> RevealProgressProperty =
        AvaloniaProperty.Register<ProportionalReveal, double>(
            nameof(RevealProgress),
            validate: value => value is >= 0 and <= 1);

    private Size _contentSize;

    static ProportionalReveal()
    {
        AffectsMeasure<ProportionalReveal>(RevealProgressProperty);
    }

    public double RevealProgress
    {
        get => GetValue(RevealProgressProperty);
        set => SetValue(RevealProgressProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Child is null)
        {
            _contentSize = default;
            return default;
        }

        Child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        _contentSize = Child.DesiredSize;
        return new Size(
            _contentSize.Width,
            _contentSize.Height * Math.Clamp(RevealProgress, 0, 1));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Child?.Arrange(new Rect(0, 0, finalSize.Width, _contentSize.Height));
        return finalSize;
    }
}
