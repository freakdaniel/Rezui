using Avalonia;
using Rezui.Services;
using Xunit;

namespace Rezui.Tests;

public sealed class HomeCardMotionTests
{
    [Fact]
    public void CenterKeepsCardFacingForwardWhileStillLiftingIt()
    {
        var motion = HomeCardMotion.Calculate(
            new Point(100, 150),
            new Size(200, 300));

        Assert.Equal(0, motion.AngleX, precision: 6);
        Assert.Equal(0, motion.AngleY, precision: 6);
        Assert.Equal(0, motion.AngleZ, precision: 6);
        Assert.Equal(-5.5, motion.LiftY, precision: 6);
    }

    [Fact]
    public void ResponseUsesCurvedMotionAndCrossAxisDepth()
    {
        var half = HomeCardMotion.Calculate(
            new Point(150, 75),
            new Size(200, 300));
        var edge = HomeCardMotion.Calculate(
            new Point(200, 0),
            new Size(200, 300));

        Assert.True(half.AngleX > 0);
        Assert.True(half.AngleY > 0);
        Assert.NotEqual(0, half.AngleZ);
        Assert.True(edge.AngleY / half.AngleY < 2);
        Assert.NotEqual(Math.Sign(edge.PosterX), Math.Sign(edge.PosterY));
    }
}
