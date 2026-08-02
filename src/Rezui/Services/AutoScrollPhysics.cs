namespace Rezui.Services;

internal static class AutoScrollPhysics
{
    internal const double DeadZone = 14;
    internal const double MaximumSpeed = 1800;

    internal static double CalculateVelocity(double displacement)
    {
        var direction = Math.Sign(displacement);
        var distance = Math.Abs(displacement) - DeadZone;
        if (distance <= 0)
        {
            return 0;
        }

        var speed = Math.Pow(distance, 1.22) * 4.5;
        return direction * Math.Min(speed, MaximumSpeed);
    }
}
