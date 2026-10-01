using Brutal.Numerics;

namespace KSACars;

/// <summary>Which rockets a hatch belongs to: the ones that lift the car, push it forward or press it down.</summary>
public enum ThrusterGroup
{
    Lift,
    Boost,
    Down,
}

/// <summary>
/// One thruster's hatch: the subpart that is the cup behind its hole, the centre of the hole in the
/// bodywork, and the way out of the bodywork there.
/// </summary>
public sealed record HatchProfile(string Suffix, double3 At, double3 Out, ThrusterGroup Group);

/// <summary>
/// A thruster behind an iris. The bodywork has a round hole with a cup under it, and six blades in the
/// car's paint close the hole flush with the skin. Each blade is half the disc, and they open as a
/// camera's do: every straight edge draws back towards its own side of the rim, leaving a hexagon that
/// grows from the centre. Cup and blade are modelled with the way out of the car along +X, and a blade
/// about the middle of its rim, which is towards +Y.
/// </summary>
public static class Hatch
{
    /// <summary>The blades to a hatch.</summary>
    public const int Blades = 6;

    /// <summary>How long an iris takes to open, or close.</summary>
    public const double OpenSeconds = 0.1;

    /// <summary>A blade's radius: the hole's, and a little under the skin round it.</summary>
    public const double Radius = 0.1225;

    /// <summary>How far under the skin the top blade lies, which is over the cup's rim.</summary>
    public const double BladeDepth = 0.0015;

    /// <summary>How much lower each blade lies than the one before, so no two share a plane.</summary>
    public const double BladeStep = 0.00015;

    /// <summary>How far open a hatch is after a step, from 0 closed to 1 open.</summary>
    public static double Advance(double open, bool wanted, double dt) =>
        Math.Clamp(open + ((wanted ? 1.0 : -1.0) * Math.Max(dt, 0.0) / OpenSeconds), 0.0, 1.0);

    /// <summary>Whether a hatch is open far enough for its rocket to fire.</summary>
    public static bool Ready(double open) => open >= 0.999;

    /// <summary>How the cup is turned, in the part's frame; it sits at the hatch's own point.</summary>
    public static doubleQuat Facing(HatchProfile hatch) =>
        Vec.RotationFromTo(new double3(1, 0, 0), Vec.Unit(hatch.Out));

    /// <summary>How far a blade's straight edge has drawn back from the centre at <paramref name="open"/>.</summary>
    public static double Aperture(double open) => Radius * Math.Clamp(open, 0.0, 1.0);

    /// <summary>
    /// Where blade <paramref name="blade"/> is, how it is turned and how it is squashed, in the part's
    /// frame, at <paramref name="open"/>. A blade is squashed towards its rim rather than slid under the
    /// skin, which is thin and curved; narrowed by as much as keeps its corners on the hole's edge, it
    /// covers what a sliding blade would and stays within 7 mm of the hole.
    /// </summary>
    public static (double3 Position, doubleQuat Rotation, double3 Scale) Blade(HatchProfile hatch, int blade, double open)
    {
        double deep = Math.Max(1.0 - (Aperture(open) / Radius), 0.001);
        double wide = Math.Sqrt(deep * (2.0 - deep));
        var axis = new double3(1, 0, 0);
        doubleQuat facing = Facing(hatch);
        doubleQuat place = doubleQuat.CreateFromAxisAngle(axis, blade * 2.0 * Math.PI / Blades);
        var rim = new double3(-(BladeDepth + (blade * BladeStep)), Radius, 0.0);
        return (hatch.At + (facing * (place * rim)), facing * place, new double3(1.0, deep, wide));
    }
}
