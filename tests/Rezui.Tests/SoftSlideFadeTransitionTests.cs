using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Rezui.Transitions;
using Xunit;

namespace Rezui.Tests;

public sealed class SoftSlideFadeTransitionTests
{
    [AvaloniaFact]
    public async Task TransitionRunsAndRestoresExistingVisualState()
    {
        var fromTransform = new ScaleTransform(0.9, 0.9);
        var toTransform = new RotateTransform(4);
        var from = new Border
        {
            Width = 100,
            Height = 100,
            Opacity = 0.8,
            RenderTransform = fromTransform
        };
        var to = new Border
        {
            Width = 100,
            Height = 100,
            Opacity = 0.9,
            RenderTransform = toTransform,
            IsVisible = false
        };
        var window = new Window
        {
            Content = new Grid
            {
                Children = { from, to }
            }
        };
        var transition = new SoftSlideFadeTransition
        {
            Duration = TimeSpan.FromMilliseconds(1),
            FadeDuration = TimeSpan.FromMilliseconds(1),
            Offset = 24
        };

        window.Show();
        try
        {
            await transition.Start(from, to, true, CancellationToken.None);

            Assert.Same(fromTransform, from.RenderTransform);
            Assert.Same(toTransform, to.RenderTransform);
            Assert.Equal(0.8, from.Opacity);
            Assert.Equal(0.9, to.Opacity);
            Assert.True(to.IsVisible);

            await transition.Start(to, from, false, CancellationToken.None);

            Assert.Same(fromTransform, from.RenderTransform);
            Assert.Same(toTransform, to.RenderTransform);
            Assert.Equal(0.8, from.Opacity);
            Assert.Equal(0.9, to.Opacity);
            Assert.True(from.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task InterruptedTransitionCannotLeavePartialVisualState()
    {
        var fromTransform = new ScaleTransform(0.9, 0.9);
        var toTransform = new RotateTransform(4);
        var from = new Border
        {
            Width = 100,
            Height = 100,
            Opacity = 0.8,
            RenderTransform = fromTransform
        };
        var to = new Border
        {
            Width = 100,
            Height = 100,
            Opacity = 0.9,
            RenderTransform = toTransform,
            IsVisible = false
        };
        var window = new Window
        {
            Content = new Grid
            {
                Children = { from, to }
            }
        };
        var transition = new SoftSlideFadeTransition
        {
            Duration = TimeSpan.FromMilliseconds(120),
            FadeDuration = TimeSpan.FromMilliseconds(100),
            Offset = 24
        };
        using var interrupted = new CancellationTokenSource();

        window.Show();
        try
        {
            var first = transition.Start(from, to, true, interrupted.Token);
            await Task.Delay(20);
            var second = transition.Start(to, from, false, CancellationToken.None);
            interrupted.Cancel();

            await first;
            await second;

            Assert.Same(fromTransform, from.RenderTransform);
            Assert.Same(toTransform, to.RenderTransform);
            Assert.Equal(0.8, from.Opacity);
            Assert.Equal(0.9, to.Opacity);
        }
        finally
        {
            window.Close();
        }
    }
}
