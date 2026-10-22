using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// The still air behind a car, which a car following in it is pushed through with less drag: what
/// lets one of two cars alike draw up to the other on a straight and go by. Every car on its wheels
/// has it, the one being flown among them.
/// </summary>
internal static class Slipstream
{
    /// <summary>The most of a car's drag the air behind another takes away, right behind it.</summary>
    public const double MostShare = 0.4;

    /// <summary>Nearer than this a car is alongside or touching, and further than <see cref="ReachM"/> the air behind it has come back.</summary>
    public const double CloseM = 2.0, ReachM = 50.0;

    /// <summary>How far to the side of a car's middle its wake reaches, m.</summary>
    public const double WideM = 1.6;

    /// <summary>The least a car's way may agree with the one ahead's for it to be in its wake: a car crossing or coming the other way leaves none.</summary>
    public const double SameWay = 0.8;

    /// <summary>
    /// The share of its drag a car at <paramref name="place"/> facing <paramref name="facing"/> is
    /// spared by one ahead of it, at so far along its way and so far to its side: most close behind
    /// and dead astern, and nothing past <see cref="ReachM"/> or <see cref="WideM"/>.
    /// </summary>
    public static double Tow(double3 place, double3 facing, double3 theirPlace, double3 theirFacing)
    {
        double3 up = Vec.Unit(place), ahead = Vec.Unit(Vec.RejectFrom(facing, up));
        if (Vec.Dot(ahead, Vec.Unit(Vec.RejectFrom(theirFacing, up))) < SameWay) return 0.0;
        double3 to = Vec.RejectFrom(theirPlace - place, up);
        double along = Vec.Dot(to, ahead), side = Vec.Dot(to, Vec.Cross(up, ahead));
        if (along < CloseM || along > ReachM || Math.Abs(side) > WideM) return 0.0;
        double across = 1.0 - ((side / WideM) * (side / WideM));
        return MostShare * across * (1.0 - ((along - CloseM) / (ReachM - CloseM)));
    }

    /// <summary>The most any of the cars ahead spares a car, the nearest wake being the one it is in.</summary>
    public static double Tow(double3 place, double3 facing, ReadOnlySpan<(double3 Place, double3 Facing)> others)
    {
        double most = 0.0;
        foreach ((double3 at, double3 way) in others) most = Math.Max(most, Tow(place, facing, at, way));
        return most;
    }
}
