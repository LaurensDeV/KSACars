using Brutal.Numerics;

namespace KSACars;

/// <summary>How a car is put back on its wheels: a turn in its own frame, and how far to lift it.</summary>
public readonly record struct RightingMove(doubleQuat Turn, double Lift);

/// <summary>
/// Sets a car that has ended up on its roof or its side back on its wheels, where it lies.
///
/// <para>The turn is the shortest one that stands the car up, so it keeps the way it was pointing; a
/// car flat on its roof has no shortest one, and is rolled over its side rather than end over end.</para>
/// </summary>
public static class Righting
{
    /// <summary>The gap left under the lowest tyre, for the springs to take up as the car comes down.</summary>
    public const double Clearance = 0.15;

    /// <summary>
    /// Everything in the car's own frame: <paramref name="hubs"/> from the centre of mass, and
    /// <paramref name="comHeight"/> the centre of mass above the ground under it.
    /// </summary>
    public static RightingMove Solve(ReadOnlySpan<BuggyCorner> corners, ReadOnlySpan<double3> hubs,
                                     double3 up, double3 forward, double3 groundUp, double comHeight)
    {
        doubleQuat turn = Vec.Dot(Vec.Unit(up), Vec.Unit(groundUp)) < -0.999
            ? doubleQuat.CreateFromAxisAngle(Vec.Unit(forward), Math.PI)
            : Vec.RotationFromTo(up, groundUp);

        // Once it stands, a hub is as far above the centre of mass as it sits along the car's own up.
        double lift = double.NegativeInfinity;
        for (int i = 0; i < corners.Length && i < hubs.Length; i++)
        {
            double standing = comHeight + Vec.Dot(hubs[i], Vec.Unit(up));
            lift = Math.Max(lift, corners[i].Radius + Clearance - standing);
        }

        return new RightingMove(turn, double.IsFinite(lift) ? lift : 0.0);
    }
}
