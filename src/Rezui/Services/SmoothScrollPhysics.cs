namespace Rezui.Services;

internal static class SmoothScrollPhysics
{
    internal const double MaximumWheelDelta = 1;
    internal const double MaximumTargetLead = 124;

    internal static double NormalizeWheelDelta(double delta) =>
        Math.Clamp(delta, -MaximumWheelDelta, MaximumWheelDelta);

    internal static double CalculateTarget(
        double currentOffset,
        double currentTarget,
        double delta,
        double wheelStep,
        double maximumOffset)
    {
        var movement = -NormalizeWheelDelta(delta) * wheelStep;
        var outstandingMovement = currentTarget - currentOffset;
        var baseTarget = Math.Sign(movement) != Math.Sign(outstandingMovement)
            ? currentOffset
            : currentTarget;
        var desiredTarget = baseTarget + movement;
        var minimumTarget = Math.Max(0, currentOffset - MaximumTargetLead);
        var maximumTarget = Math.Min(maximumOffset, currentOffset + MaximumTargetLead);
        return Math.Clamp(desiredTarget, minimumTarget, maximumTarget);
    }
}
