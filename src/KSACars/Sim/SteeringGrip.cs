using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// Where a seated driver's hands hold the steering wheel, and the elbow that puts a wrist there.
///
/// <para>The kitten is posed in its own model space, which is anchored to its seat: centimetres, +Y
/// up, +Z the seat's forward and +X its left, from an origin 0.63 m below the seat's eye point. The
/// buggy's seat faces the part's +Y with +X up, so that model space is the part frame re-axed and
/// scaled, and nothing about where the car is in the world enters it.</para>
/// </summary>
public static class SteeringGrip
{
    /// <summary>How far below a seat's eye point the engine puts the seated kitten's origin.</summary>
    public const double SeatedDrop = 0.63;

    /// <summary>Palm to wrist, so the wrist stops short of the rim and the hand closes on it.</summary>
    public const double PalmMetres = 0.045;

    /// <summary>
    /// How far the hands turn with the wheel before the rim slides through them. Further than this
    /// carries a grip over the top of the rim, out of a seated kitten's reach.
    /// </summary>
    public const double HandsFollowRad = 20.0 * Math.PI / 180.0;

    /// <summary>
    /// The driver's grips on the rim, from nine and three o'clock turned with the wheel, in the part
    /// frame. Past <see cref="HandsFollowRad"/> the wheel turns on and the hands stay, still on the rim.
    /// </summary>
    public static (double3 Left, double3 Right) GripsPart(BuggyProfile p, double steerAngle)
    {
        double wheel = Math.Clamp(steerAngle * p.SteeringRatio, -HandsFollowRad, HandsFollowRad);
        doubleQuat turn = doubleQuat.CreateFromAxisAngle(p.SteeringAxis, wheel);
        double3 across = Vec.Unit(Vec.RejectFrom(new double3(0, 0, 1), p.SteeringAxis)) * p.SteeringRimRadius;
        return (p.SteeringPivot + (turn * across), p.SteeringPivot - (turn * across));
    }

    /// <summary>A part-frame point in the driver kitten's model space, centimetres.</summary>
    public static double3 ToKittenModel(BuggyProfile p, double3 part)
    {
        double3 d = part - (p.DriverEye - new double3(SeatedDrop, 0, 0));
        return new double3(100.0 * d.Z, 100.0 * d.X, 100.0 * d.Y);
    }

    /// <summary>
    /// The elbow for a two-bone arm from <paramref name="shoulder"/> reaching toward
    /// <paramref name="target"/>, bent toward <paramref name="bend"/>. The target is brought inside the
    /// arm's reach first, and the one actually reached is returned beside the elbow.
    /// </summary>
    public static (double3 Elbow, double3 Reached) SolveElbow(double3 shoulder, double3 target,
                                                             double upper, double fore, double3 bend)
    {
        double3 toTarget = target - shoulder;
        double reach = Math.Max((upper + fore) * 0.98, 1e-6);
        double shortest = Math.Abs(upper - fore) * 1.02;
        double length = Math.Clamp(Vec.Len(toTarget), shortest, reach);
        double3 dir = Vec.Len(toTarget) > 1e-9 ? Vec.Unit(toTarget) : Vec.Unit(bend);
        double3 reached = shoulder + (dir * length);

        // Law of cosines along the line, then out toward the bend by what is left of the upper arm.
        double along = ((upper * upper) - (fore * fore) + (length * length)) / (2.0 * length);
        double out_ = Math.Sqrt(Math.Max((upper * upper) - (along * along), 0.0));
        double3 side = Vec.RejectFrom(bend, dir);
        side = Vec.Len(side) > 1e-9 ? Vec.Unit(side) : Vec.AnyPerpendicular(dir);
        return (shoulder + (dir * along) + (side * out_), reached);
    }
}
