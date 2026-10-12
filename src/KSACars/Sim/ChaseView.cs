namespace KSACars;

/// <summary>
/// A view that stays behind a car: the orbit camera's heading eased towards the car's own, looking into a
/// corner by how fast the car is turning and tilted as the car is on a slope, with a stick to look round
/// from there and to raise or lower it. Angles are KSA's, where a negative elevation is
/// a camera above what it follows.
/// </summary>
public static class ChaseView
{
    /// <summary>How long the view takes to come most of the way round behind a car that has turned.</summary>
    public const double FollowSeconds = 0.3;

    /// <summary>How far round the car a stick held over looks.</summary>
    public const double LookRoundRad = 150.0 * Math.PI / 180.0;

    /// <summary>How fast a stick held over raises or lowers the view, and turns one that is not chasing.</summary>
    public const double StickRadPerSec = 1.5;

    /// <summary>The elevation a chase starts at, if the view was lower.</summary>
    public const double StartElevationRad = -0.26;

    public const double HighestRad = -1.4;
    public const double LowestRad = 0.05;

    /// <summary>The angle that is <paramref name="angle"/> a whole number of turns round and nearest <paramref name="to"/>.</summary>
    public static double Nearest(double angle, double to) =>
        angle + (Math.Round((to - angle) / (2.0 * Math.PI)) * 2.0 * Math.PI);

    /// <summary>
    /// The view's heading a frame on. The share of the way it goes depends on the frame's length alone, so
    /// it neither overshoots on a long frame nor crawls on a short one, and it goes the short way round.
    /// </summary>
    public static double Follow(double azimuth, double heading, double look, double dt)
    {
        double wanted = Nearest(heading + (Math.Clamp(look, -1.0, 1.0) * LookRoundRad), azimuth);
        return azimuth + ((wanted - azimuth) * (1.0 - Math.Exp(-Math.Max(dt, 0.0) / FollowSeconds)));
    }

    /// <summary>How far ahead of a turning car the view looks, as the time the car takes to turn there.</summary>
    public const double LeadSeconds = 0.5;

    /// <summary>The most the view looks into a corner.</summary>
    public const double MostLeadRad = 0.6;

    /// <summary>How far into a corner the view looks for a car turning at a rate, in radians a second.</summary>
    public static double Lead(double turnRate) => Math.Clamp(turnRate * LeadSeconds, -MostLeadRad, MostLeadRad);

    /// <summary>A value brought towards another by the share a frame of that length covers.</summary>
    public static double Ease(double value, double towards, double seconds, double dt) =>
        value + ((towards - value) * (1.0 - Math.Exp(-Math.Max(dt, 0.0) / seconds)));

    /// <summary>The view's elevation a frame on with a stick pushed away, which lowers it, or pulled back.</summary>
    public static double Tilt(double elevation, double stickY, double dt) =>
        Math.Clamp(elevation - (stickY * StickRadPerSec * dt), HighestRad, LowestRad);
}
