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

    /// <summary>The speed at which the view is as wide, as far back and as low as it goes, m/s.</summary>
    public const double FullSpeedMs = 65.0;

    /// <summary>What a car at full speed adds to the view: degrees of field of view, a share of its distance, and radians it drops by.</summary>
    public const double MostFovDeg = 22.0, MostPull = 0.25, MostDropRad = 0.07;

    /// <summary>The share of full speed past which the view shakes, and by how much at all of it, rad.</summary>
    public const double ShakeFrom = 0.7, MostShakeRad = 0.005;

    /// <summary>
    /// How much of what speed does to the view a car going at one has, 0 to 1: nothing standing, all of it at
    /// <see cref="FullSpeedMs"/>, and eased at both ends so neither pulling away nor the last of the speed jerks it.
    /// </summary>
    public static double SpeedShare(double speedMs)
    {
        double t = Math.Clamp(Math.Abs(speedMs) / FullSpeedMs, 0.0, 1.0);
        return t * t * (3.0 - (2.0 * t));
    }

    /// <summary>
    /// How far the view is thrown off its heading and its elevation at a time, rad: two beats that never
    /// fall in step, slow enough to come through the spring KSA turns its camera by.
    /// </summary>
    public static (double Azimuth, double Elevation) Shake(double share, double seconds)
    {
        double over = Math.Clamp((share - ShakeFrom) / (1.0 - ShakeFrom), 0.0, 1.0), size = MostShakeRad * over * over;
        return (size * Math.Sin(seconds * 2.0 * Math.PI * 5.3), size * 0.6 * Math.Sin((seconds * 2.0 * Math.PI * 7.9) + 1.0));
    }

    /// <summary>The view's elevation a frame on with a stick pushed away, which lowers it, or pulled back.</summary>
    public static double Tilt(double elevation, double stickY, double dt) =>
        Math.Clamp(elevation - (stickY * StickRadPerSec * dt), HighestRad, LowestRad);

    /// <summary>How far behind the car the chase stands, in KSA's orbit distance power: the craft's radius times this, before speed pulls it back.</summary>
    public static double DistancePower(ChaseMode mode) => mode switch
    {
        ChaseMode.Close => 1.5,
        ChaseMode.Medium => 2.2,
        ChaseMode.Far => 3.2,
        _ => double.NaN,
    };

    /// <summary>The mode after this one, as a button steps through them: close, medium, far, free, and round again.</summary>
    public static ChaseMode Next(ChaseMode mode) => (ChaseMode)(((int)mode + 1) % 4);
}

/// <summary>Where the camera is while a car is flown: behind it close, medium or far, or free, KSA's own orbit view.</summary>
public enum ChaseMode { Close, Medium, Far, Free }
