namespace KSACars;

/// <summary>
/// A guest's world kept to the host's universe time, by how fast it is run and nothing else: a
/// world a little behind runs a little faster until it has caught up, and one ahead a little
/// slower. Nobody warps; a step of 33 ms at 1.02 is 33.7.
/// </summary>
internal static class ClockLock
{
    /// <summary>How long a gap between the two worlds is taken to close, s.</summary>
    public const double CloseOverSeconds = 5.0;

    /// <summary>The most a guest's world runs faster than the host's, as a share: a tenth, and well under any warp.</summary>
    public const double MostFaster = 1.1;

    /// <summary>The host's universe time now, from its last word: what it said, carried on at the speed it said for as long as has passed on its clock since.</summary>
    public static double HostNow(double said, double saidAt, double speed, double hostClockNow) =>
        said + (Math.Max(0.0, hostClockNow - saidAt) * speed);

    /// <summary>
    /// How fast to run a world that is <paramref name="behind"/> seconds behind the host's, which runs
    /// at <paramref name="hostSpeed"/>: stopped with the host's, and from stopped to a tenth faster.
    /// </summary>
    public static double Speed(double behind, double hostSpeed)
    {
        if (!(hostSpeed > 0.0) || !double.IsFinite(behind)) return 0.0;
        return Math.Clamp(hostSpeed * (1.0 + (behind / CloseOverSeconds)), 0.0, hostSpeed * MostFaster);
    }
}
