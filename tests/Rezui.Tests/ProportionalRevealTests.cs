using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Rezui.Controls;
using Xunit;

namespace Rezui.Tests;

public sealed class ProportionalRevealTests
{
    [AvaloniaFact]
    public void ProgressRevealsTheSameFractionForShortAndTallContent()
    {
        var shortReveal = CreateReveal(48, 0.5);
        var tallReveal = CreateReveal(144, 0.5);

        shortReveal.Measure(new Size(200, 500));
        tallReveal.Measure(new Size(200, 500));

        Assert.Equal(24, shortReveal.DesiredSize.Height);
        Assert.Equal(72, tallReveal.DesiredSize.Height);
        Assert.Equal(
            shortReveal.DesiredSize.Height / 48,
            tallReveal.DesiredSize.Height / 144);
    }

    private static ProportionalReveal CreateReveal(double contentHeight, double progress) =>
        new()
        {
            Width = 200,
            RevealProgress = progress,
            Child = new Border { Height = contentHeight }
        };
}
