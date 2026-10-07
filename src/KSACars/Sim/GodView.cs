using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// A view from above that is not tied to a craft: a place on the ground looked at, from how far, from
/// which compass heading and how steeply. Everything is in the body's own frame, so the view stays
/// over its place as the body turns.
/// </summary>
internal readonly record struct GodView(double3 Target, double YawRad, double PitchRad, double DistanceM)
{
    public const double MinDistanceM = 15.0, MaxDistanceM = 30_000.0;

    // Short of straight down, where KSA's fixed camera has no up to roll about, and short of level.
    public static readonly double MinPitchRad = 8.0 * Math.PI / 180.0, MaxPitchRad = 86.0 * Math.PI / 180.0;

    /// <summary>East and north on the ground at a place, from the body's pole.</summary>
    public static (double3 East, double3 North) Compass(double3 up, double3 pole)
    {
        double3 east = Vec.Cross(pole, up);
        east = Vec.Len(east) > 1e-9 ? Vec.Unit(east) : Vec.AnyPerpendicular(up);
        return (east, Vec.Cross(up, east));
    }

    /// <summary>The way the view faces along the ground, and what is to its right.</summary>
    public (double3 Ahead, double3 Right) Facing(double3 pole)
    {
        (double3 east, double3 north) = Compass(Target, pole);
        double3 ahead = (north * Math.Cos(YawRad)) + (east * Math.Sin(YawRad));
        return (ahead, Vec.Cross(ahead, Target));
    }

    /// <summary>Where the eye is and which way it looks, for a target standing <paramref name="targetRadiusM"/> from the centre.</summary>
    public (double3 Eye, double3 Forward) Pose(double3 pole, double targetRadiusM)
    {
        (double3 ahead, _) = Facing(pole);
        double3 back = (Target * Math.Sin(PitchRad)) - (ahead * Math.Cos(PitchRad));
        return ((Target * targetRadiusM) + (back * DistanceM), -back);
    }

    /// <summary>The target slid over the ground, so many metres to the view's right and ahead of it.</summary>
    public GodView Pan(double3 pole, double radiusM, double rightM, double aheadM)
    {
        (double3 ahead, double3 right) = Facing(pole);
        return this with { Target = Vec.Unit((Target * radiusM) + (right * rightM) + (ahead * aheadM)) };
    }

    public GodView Turn(double yawRad, double pitchRad) => this with
    {
        YawRad = Math.IEEERemainder(YawRad + yawRad, 2.0 * Math.PI),
        PitchRad = Math.Clamp(PitchRad + pitchRad, MinPitchRad, MaxPitchRad),
    };

    /// <summary>Nearer by a notch of the wheel, or further by a negative one.</summary>
    public GodView Zoom(double notches) => this with
    {
        DistanceM = Math.Clamp(DistanceM * Math.Pow(0.85, notches), MinDistanceM, MaxDistanceM),
    };
}
