using Rezui.Services;
using Xunit;

namespace Rezui.Tests;

public sealed class SmoothScrollPhysicsTests
{
    [Theory]
    [InlineData(0.25, 0.25)]
    [InlineData(-0.25, -0.25)]
    [InlineData(1, 1)]
    [InlineData(-1, -1)]
    [InlineData(12, 1)]
    [InlineData(-12, -1)]
    public void WheelDeltaIsLimitedToOneNotch(double input, double expected) =>
        Assert.Equal(expected, SmoothScrollPhysics.NormalizeWheelDelta(input));

    [Fact]
    public void RepeatedWheelEventsCannotBuildAnUnboundedBacklog()
    {
        const double currentOffset = 100;
        var target = currentOffset;
        for (var index = 0; index < 20; index++)
        {
            target = SmoothScrollPhysics.CalculateTarget(
                currentOffset,
                target,
                delta: -1,
                wheelStep: 62,
                maximumOffset: 2000);
        }

        Assert.Equal(
            SmoothScrollPhysics.MaximumTargetLead,
            target - currentOffset);
    }

    [Fact]
    public void DirectionChangeDropsTheOldTargetImmediately()
    {
        var target = SmoothScrollPhysics.CalculateTarget(
            currentOffset: 500,
            currentTarget: 624,
            delta: 1,
            wheelStep: 62,
            maximumOffset: 2000);

        Assert.Equal(438, target);
    }
}
