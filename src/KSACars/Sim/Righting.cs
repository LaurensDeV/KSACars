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

    /// <summary>
    /// The turn, in the car's own frame, that stands it on a surface facing a given way: its
    /// <paramref name="up"/> onto <paramref name="surfaceUp"/> and its <paramref name="forward"/> onto
    /// <paramref name="ahead"/>, all four in that frame.
    /// </summary>
    public static doubleQuat Facing(double3 up, double3 forward, double3 surfaceUp, double3 ahead)
    {
        double3 normal = Vec.Unit(surfaceUp);
        doubleQuat stand = Vec.Dot(Vec.Unit(up), normal) < -0.999
            ? doubleQuat.CreateFromAxisAngle(Vec.Unit(forward), Math.PI)
            : Vec.RotationFromTo(up, normal);
        double3 now = Vec.RejectFrom(stand * forward, normal), wanted = Vec.RejectFrom(ahead, normal);
        double swing = Math.Atan2(Vec.Dot(Vec.Cross(now, wanted), normal), Vec.Dot(now, wanted));
        return doubleQuat.CreateFromAxisAngle(normal, swing) * stand;
    }

    /// <summary>How far above a surface the centre of mass is with every tyre just clear of it, the car standing square on it.</summary>
    public static double StandingHeight(ReadOnlySpan<BuggyCorner> corners, ReadOnlySpan<double3> hubs, double3 up)
    {
        double height = 0.0;
        for (int i = 0; i < corners.Length && i < hubs.Length; i++)
        {
            height = Math.Max(height, corners[i].Radius + Clearance - Vec.Dot(hubs[i], Vec.Unit(up)));
        }
        return height;
    }
}
