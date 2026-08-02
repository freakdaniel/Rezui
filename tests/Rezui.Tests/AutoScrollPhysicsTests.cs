using Rezui.Services;
using Xunit;

namespace Rezui.Tests;

public sealed class AutoScrollPhysicsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(AutoScrollPhysics.DeadZone)]
    [InlineData(-AutoScrollPhysics.DeadZone)]
    public void DeadZoneDoesNotScroll(double displacement) =>
        Assert.Equal(0, AutoScrollPhysics.CalculateVelocity(displacement));

    [Fact]
    public void DirectionFollowsPointerAndSpeedIsSymmetric()
    {
        var downward = AutoScrollPhysics.CalculateVelocity(80);
        var upward = AutoScrollPhysics.CalculateVelocity(-80);

        Assert.True(downward > 0);
        Assert.Equal(-downward, upward, precision: 8);
    }

    [Theory]
    [InlineData(10000)]
    [InlineData(-10000)]
    public void SpeedIsCapped(double displacement) =>
        Assert.Equal(
            AutoScrollPhysics.MaximumSpeed,
            Math.Abs(AutoScrollPhysics.CalculateVelocity(displacement)));
}
