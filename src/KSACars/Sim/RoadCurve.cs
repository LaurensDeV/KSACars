using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// A road's centre line between two points: a cubic Bezier through the body's own frame, which the
/// ground is then put under.
/// </summary>
public static class RoadCurve
{
    public static double3 Bezier(double3 a, double3 b, double3 c, double3 d, double t)
    {
        double u = 1.0 - t;
        return (a * (u * u * u)) + (b * (3.0 * u * u * t)) + (c * (3.0 * u * t * t)) + (d * (t * t * t));
    }

    /// <summary>The curve as points no further apart than <paramref name="spacing"/>, both ends included.</summary>
    public static double3[] Sample(double3 a, double3 b, double3 c, double3 d, double spacing)
    {
        double length = 0.0;
        double3 last = a;
        for (int i = 1; i <= 32; i++)
        {
            double3 at = Bezier(a, b, c, d, i / 32.0);
            length += Vec.Len(at - last);
            last = at;
        }

        int steps = Math.Clamp((int)Math.Ceiling(length / Math.Max(spacing, 0.01)), 1, 100_000);
        double3[] points = new double3[steps + 1];
        for (int i = 0; i <= steps; i++) points[i] = Bezier(a, b, c, d, (double)i / steps);
        return points;
    }
}
