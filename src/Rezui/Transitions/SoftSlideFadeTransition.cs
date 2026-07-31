using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;

namespace Rezui.Transitions;

public sealed class SoftSlideFadeTransition : IPageTransition
{
    private static readonly Easing MotionEasing = new SplineEasing
    {
        X1 = 0.22,
        Y1 = 1,
        X2 = 0.36,
        Y2 = 1
    };

    private static readonly Easing FadeEasing = new CubicEaseOut();

    public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(280);

    public TimeSpan FadeDuration { get; set; } = TimeSpan.FromMilliseconds(150);

    public double Offset { get; set; } = 42;

    public async Task Start(
        Visual? from,
        Visual? to,
        bool forward,
        CancellationToken cancellationToken)
    {
        var direction = forward ? 1d : -1d;
        var animations = new List<Task>(4);
        var fromTransform = from?.RenderTransform;
        var toTransform = to?.RenderTransform;
        var fromOpacity = from?.Opacity ?? 1;
        var toOpacity = to?.Opacity ?? 1;

        try
        {
            if (from is not null)
            {
                from.RenderTransform = new TranslateTransform();
                animations.Add(AnimateAsync(
                    from,
                    TranslateTransform.XProperty,
                    0,
                    -direction * Offset,
                    Duration,
                    MotionEasing,
                    cancellationToken));
                animations.Add(AnimateAsync(
                    from,
                    Visual.OpacityProperty,
                    1,
                    0,
                    FadeDuration,
                    FadeEasing,
                    cancellationToken));
            }

            if (to is not null)
            {
                to.IsVisible = true;
                to.RenderTransform = new TranslateTransform(direction * Offset, 0);
                animations.Add(AnimateAsync(
                    to,
                    TranslateTransform.XProperty,
                    direction * Offset,
                    0,
                    Duration,
                    MotionEasing,
                    cancellationToken));
                animations.Add(AnimateAsync(
                    to,
                    Visual.OpacityProperty,
                    0,
                    1,
                    FadeDuration,
                    FadeEasing,
                    cancellationToken));
            }

            await Task.WhenAll(animations);
        }
        finally
        {
            if (from is not null)
            {
                from.RenderTransform = fromTransform;
                from.Opacity = fromOpacity;
            }

            if (to is not null)
            {
                to.RenderTransform = toTransform;
                to.Opacity = toOpacity;
            }
        }
    }

    private static Task AnimateAsync(
        Animatable target,
        AvaloniaProperty property,
        double from,
        double to,
        TimeSpan duration,
        Easing easing,
        CancellationToken cancellationToken)
    {
        var animation = new Animation
        {
            Duration = duration,
            Easing = easing,
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters = { new Setter(property, from) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters = { new Setter(property, to) }
                }
            }
        };

        return animation.RunAsync(target, cancellationToken);
    }
}
