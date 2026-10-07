namespace KSACars;

/// <summary>
/// The ground along a road, smoothed and never buried: a height a distance along that is at or above
/// the ground at every sample and all the way between two, with no step in its slope.
///
/// <para>A road laid on the ground sample by sample has every bump of the ground in it. A plain
/// average of the ground is smooth and is under half of it, and KSA's terrain cannot be cut away from
/// over a road. So the samples are averaged under a triangle as wide as the smoothing, which follows
/// a steady slope exactly; what any sample or its neighbours stand above that average by is taken as
/// its greatest over the same width, averaged under the same triangle, and added back. The sum is no
/// lower than a sample or its neighbours anywhere within reach of them, and a curve through it that
/// never leaves the span of two of its points is therefore above the straight line between two
/// samples.</para>
///
/// <para>On a steady slope it stands above the ground by the climb between two samples, which is the
/// cost of answering for the ground between them; on level ground it is the ground.</para>
/// </summary>
internal static class RoadGround
{
    /// <param name="ground">The highest ground across the road at each sample, <paramref name="spacingM"/> apart: on an open road from its start to its end, on a closed one once round without the start again.</param>
    /// <param name="windowM">How far along the road a bump is smoothed over.</param>
    public static MonotoneCurve Smooth(double[] ground, double spacingM, double windowM, bool closed)
    {
        int n = ground.Length;
        if (n == 0) return MonotoneCurve.Level(0.0);
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

        double[] mean = new double[n], proud = new double[n], most = new double[n], height = new double[n];
        for (int i = 0; i < n; i++)
        {
            for (int j = -reach; j <= reach; j++) mean[i] += weight[j + reach] * Past(i + j);
        }
        for (int i = 0; i < n; i++) proud[i] = Math.Max(ground[i], Math.Max(ground[In(i - 1)], ground[In(i + 1)])) - mean[i];
        for (int i = 0; i < n; i++)
        {
            most[i] = double.NegativeInfinity;
            for (int j = -reach; j <= reach; j++) most[i] = Math.Max(most[i], proud[In(i + j)]);
        }
        for (int i = 0; i < n; i++)
        {
            height[i] = mean[i];
            for (int j = -reach; j <= reach; j++) height[i] += weight[j + reach] * most[In(i + j)];
        }

        double[] along = new double[n];
        for (int i = 0; i < n; i++) along[i] = i * spacingM;
        return new MonotoneCurve(along, height, closed ? n * spacingM : 0.0);
    }
}
