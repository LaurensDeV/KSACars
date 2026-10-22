namespace KSACars;

/// <summary>
/// The ground along a road, smoothed and never buried: a height a distance along that is at or above
/// the ground at every sample and all the way along the straight line between two, with no step in
/// its slope.
///
/// <para>A road laid on the ground sample by sample has every bump of the ground in it. A plain
/// average of the ground is smooth and is under half of it, and KSA's terrain cannot be cut away from
/// over a road. So the samples are averaged under a triangle as wide as the smoothing, which follows
/// a steady slope exactly; how far the ground stands above the curve through those averages is taken
/// between each two samples, then as its greatest over the same width, averaged under the same
/// triangle, and added back as a second curve. Neither curve leaves the span of two of its points,
/// so the second is at least what the ground stands proud by wherever it does, and the sum is at or
/// above the ground.</para>
///
/// <para>Level ground and a steady slope come back as they are; a bump is filled over, the road
/// rising to it and falling from it over the smoothing's width either side.</para>
/// </summary>
internal sealed class RoadGround
{
    private readonly MonotoneCurve _mean;
    private readonly MonotoneCurve? _proud;

    private RoadGround(MonotoneCurve mean, MonotoneCurve? proud) => (_mean, _proud) = (mean, proud);

    /// <summary>A height that is already the one wanted, with nothing smoothed.</summary>
    public static RoadGround Along(MonotoneCurve height) => new(height, null);

    public static RoadGround Level(double height) => new(MonotoneCurve.Level(height), null);

    /// <summary>The height <paramref name="s"/> metres along, its slope and how fast that changes.</summary>
    public void At(double s, out double height, out double slope, out double bend)
    {
        _mean.At(s, out height, out slope, out bend);
        if (_proud is null) return;
        _proud.At(s, out double more, out double moreSlope, out double moreBend);
        (height, slope, bend) = (height + more, slope + moreSlope, bend + moreBend);
    }

    public double At(double s)
    {
        At(s, out double height, out _, out _);
        return height;
    }

    /// <param name="ground">The highest ground across the road at each sample, <paramref name="spacingM"/> apart: on an open road from its start to its end, on a closed one once round without the start again.</param>
    /// <param name="windowM">How far along the road a bump is smoothed over.</param>
    public static RoadGround Smooth(double[] ground, double spacingM, double windowM, bool closed)
    {
        int n = ground.Length;
        if (n == 0) return Level(0.0);
        int reach = Math.Max(0, (int)Math.Ceiling(0.5 * windowM / spacingM));
        if (closed) reach = Math.Min(reach, (n - 1) / 2);

        int In(int i) => closed ? ((i % n) + n) % n : Math.Clamp(i, 0, n - 1);

        // Past an open end the ground is taken to carry on as it was going, so a slope is still a slope there.
        double Past(int i) => closed || (i >= 0 && i < n) ? ground[In(i)]
                            : i < 0 ? (2.0 * ground[0]) - ground[Math.Min(-i, n - 1)]
                            : (2.0 * ground[n - 1]) - ground[Math.Max((2 * (n - 1)) - i, 0)];

        double[] weight = new double[(2 * reach) + 1];
        double total = 0.0;
        for (int j = -reach; j <= reach; j++) total += weight[j + reach] = reach + 1 - Math.Abs(j);
        for (int j = 0; j < weight.Length; j++) weight[j] /= total;

        double[] along = new double[n], mean = new double[n];
        for (int i = 0; i < n; i++)
        {
            along[i] = i * spacingM;
            for (int j = -reach; j <= reach; j++) mean[i] += weight[j + reach] * Past(i + j);
        }
        double period = closed ? n * spacingM : 0.0;
        MonotoneCurve smooth = new(along, mean, period);

        // How far the straight line between a sample and the next stands above the curve, at its most.
        int spans = closed ? n : n - 1;
        double[] over = new double[n];
        for (int i = 0; i < spans; i++)
        {
            double a = ground[i], b = ground[In(i + 1)], most = double.NegativeInfinity;
            for (int k = 0; k <= Checks; k++)
            {
                double t = (double)k / Checks;
                most = Math.Max(most, a + ((b - a) * t) - smooth.At(along[i] + (spacingM * t)));
            }
            over[i] = most + Slack(spacingM, smooth, along[i]);
        }

        double[] proud = new double[n], most_ = new double[n], lift = new double[n];
        for (int i = 0; i < n; i++)
        {
            bool before = closed || i > 0, after = i < spans;
            proud[i] = Math.Max(before ? over[In(i - 1)] : double.NegativeInfinity, after ? over[i] : double.NegativeInfinity);
            if (double.IsNegativeInfinity(proud[i])) proud[i] = ground[i] - mean[i];
        }
        for (int i = 0; i < n; i++)
        {
            most_[i] = double.NegativeInfinity;
            for (int j = -reach; j <= reach; j++) most_[i] = Math.Max(most_[i], proud[In(i + j)]);
        }
        for (int i = 0; i < n; i++)
        {
            for (int j = -reach; j <= reach; j++) lift[i] += weight[j + reach] * most_[In(i + j)];
        }
        return new RoadGround(smooth, new MonotoneCurve(along, lift, period));
    }

    // The line less the curve is a cubic between two samples, looked at this many times along; it
    // can stand above the highest of those by no more than its bend allows between two looks.
    private const int Checks = 8;

    private static double Slack(double spacingM, MonotoneCurve smooth, double from)
    {
        smooth.At(from + 1e-9, out _, out _, out double bendStart);
        smooth.At(from + spacingM - 1e-9, out _, out _, out double bendEnd);
        double step = spacingM / Checks;
        return Math.Max(Math.Abs(bendStart), Math.Abs(bendEnd)) * step * step / 8.0;
    }
}
