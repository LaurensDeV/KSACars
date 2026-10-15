using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// A car's place, attitude and motion measured against a laid course rather than the planet, so a
/// car on one player's copy of a course can be put in the same place on another's. Each player lays
/// a course where their own car is, on their own ground: what two copies share is the route, a
/// distance along it and the road's own frame there.
///
/// <para>The frame at a distance along is the road's: ahead along it, left across it and its own up.
/// A distance on the other copy is scaled by the two routes' lengths, which differ by the chart and
/// the ground under each by a few metres in a lap.</para>
/// </summary>
internal readonly record struct RacePose(
    double S, double LengthM, double3 Offset, doubleQuat Attitude, double3 Velocity, double3 SpinBody)
{
    /// <summary>
    /// A car where it is: <paramref name="index"/> is the stretch of the route it was last on, which
    /// is brought up to date, and less than zero for a car not yet placed.
    /// </summary>
    public static RacePose Of(Route route, ref int index, double3 positionCcf, doubleQuat body2Ccf, double3 velocityCcf,
                              double3 spinBody, double aheadM)
    {
        if (index < 0) index = route.Nearest(positionCcf);
        route.Locate(positionCcf, ref index, aheadM, out double s, out _);
        doubleQuat frame = Frame(route, s, out double3 at);
        doubleQuat toFrame = doubleQuat.Inverse(frame);
        return new RacePose(s, route.LengthM, toFrame * (positionCcf - at), toFrame * body2Ccf,
                            toFrame * velocityCcf, spinBody);
    }

    /// <summary>The same car on another copy of the course: where it is, how it is turned, and how it moves, in that body's own frame.</summary>
    public (double3 PositionCcf, doubleQuat Body2Ccf, double3 VelocityCcf) On(Route route)
    {
        double s = LengthM > 0.0 ? S * route.LengthM / LengthM : S;
        s = route.Closed ? Wrap(s, route.LengthM) : Math.Clamp(s, 0.0, route.LengthM);
        doubleQuat frame = Frame(route, s, out double3 at);
        return (at + (frame * Offset), doubleQuat.Normalize(frame * Attitude), frame * Velocity);
    }

    /// <summary>
    /// The pose a share of the way from one to the next. Distance along is taken the short way round
    /// a closed route, so a car over the line is not drawn going the whole lap back.
    /// </summary>
    public static RacePose Between(RacePose a, RacePose b, double t)
    {
        double ds = b.S - a.S;
        if (a.LengthM > 0.0 && Math.Abs(ds) > 0.5 * a.LengthM) ds -= Math.Sign(ds) * a.LengthM;
        double s = a.S + (ds * t);
        if (a.LengthM > 0.0 && (s < 0.0 || s >= a.LengthM)) s = Wrap(s, a.LengthM);
        return new RacePose(s, a.LengthM, Lerp(a.Offset, b.Offset, t), doubleQuat.Slerp(a.Attitude, b.Attitude, t),
                            Lerp(a.Velocity, b.Velocity, t), Lerp(a.SpinBody, b.SpinBody, t));
    }

    /// <summary>The pose carried on for so many seconds at its own velocity, along the road's frame where it is.</summary>
    public RacePose Ahead(double seconds)
    {
        double along = Velocity.X * seconds;
        double s = S + along;
        if (LengthM > 0.0 && (s < 0.0 || s >= LengthM)) s = Wrap(s, LengthM);
        return this with { S = s, Offset = Offset + new double3(0.0, Velocity.Y * seconds, Velocity.Z * seconds) };
    }

    private static double3 Lerp(double3 a, double3 b, double t) => a + ((b - a) * t);

    private static double Wrap(double s, double length) => ((s % length) + length) % length;

    // The road's frame a distance along: ahead, left and up as the columns, and the place on the route there.
    private static doubleQuat Frame(Route route, double s, out double3 at)
    {
        (at, double3 up, double3 ahead) = route.Standing(s);
        double3 left = Vec.Unit(Vec.Cross(up, ahead));
        up = Vec.Unit(Vec.Cross(ahead, left));
        return Vec.FromColumns(ahead, left, up);
    }
}
