using Avalonia;

namespace Rezui.Services;

public readonly record struct HomeCardMotionState(
    double Scale,
    double AngleX,
    double AngleY,
    double AngleZ,
    double LiftX,
    double LiftY,
    double PosterX,
    double PosterY);

public static class HomeCardMotion
{
    public static HomeCardMotionState Calculate(Point pointerPosition, Size cardSize)
    {
        if (cardSize.Width <= 0 || cardSize.Height <= 0)
        {
            return default;
        }

        var normalizedX = Math.Clamp(pointerPosition.X / cardSize.Width * 2 - 1, -1, 1);
        var normalizedY = Math.Clamp(pointerPosition.Y / cardSize.Height * 2 - 1, -1, 1);
        var curvedX = Math.Sin(normalizedX * Math.PI / 2);
        var curvedY = Math.Sin(normalizedY * Math.PI / 2);
        var distance = Math.Min(1, Math.Sqrt(curvedX * curvedX + curvedY * curvedY));
        var scale = 1.025 + distance * 0.008;

        return new HomeCardMotionState(
            scale,
            -curvedY * 5.2,
            curvedX * 6.4,
            -curvedX * curvedY * 1.05,
            curvedX * 0.8,
            -5.5 + curvedY * 0.7,
            curvedX * 3.8 + curvedY * 0.9,
            curvedY * 3.1 - Math.Abs(curvedX) * 0.55);
    }
}
