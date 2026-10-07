using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// A circuit put on the ground: each run of its roads as a line of points on the road's surface, from
/// the body's centre, which is what is drawn and what a wheel is over.
///
/// <para>A point's height is the ground under it, the lift every road stands proud by, the height
/// eased between the circuit's points, less the sink of an end that meets other roads, which is taken
/// up over two widths.</para>
/// </summary>
internal static class RoadLaying
{
    /// <param name="Line">The road's centre line on its surface, from the body's centre.</param>
    /// <param name="AboveGroundM">How far the surface is above the ground at each point of the line.</param>
    public sealed record Strip(double3[] Line, double HalfWidth, double LengthM, bool Closed, double[] AboveGroundM);

    /// <param name="groundAt">The ground's height over the body's mean radius, in a direction from its centre.</param>
    public static List<Strip> Lay(Circuit circuit, Func<double, double, double3> dirOf, double radiusM,
                                  Func<double3, double> groundAt, double liftM, double spacingM)
    {
        List<Strip> strips = [];
        foreach (RoadLayout.Run run in RoadLayout.Runs(circuit, dirOf, radiusM, spacingM))
        {
            double3[] line = run.Line;
            if (line.Length < 2) continue;

            double[] along = new double[line.Length];
            for (int i = 1; i < line.Length; i++) along[i] = along[i - 1] + (Vec.Len(line[i] - line[i - 1]) * radiusM);
            double length = along[^1];

            double dip = 2.0 * run.WidthM;
            double[] above = new double[line.Length];
            for (int i = 0; i < line.Length; i++)
            {
                double sunk = (run.SinkStartM * Math.Max(0.0, 1.0 - (along[i] / dip)))
                            + (run.SinkEndM * Math.Max(0.0, 1.0 - ((length - along[i]) / dip)));
                above[i] = liftM + run.HeightM[i] - sunk;
                line[i] *= radiusM + groundAt(line[i]) + above[i];
            }
            strips.Add(new Strip(line, 0.5 * run.WidthM, length, run.Closed, above));
        }
        return strips;
    }

    /// <summary>The surface a wheel is asked about, over what was laid.</summary>
    public static RoadSurface Surface(IEnumerable<Strip> strips) => new(strips.Select(s => (s.Line, s.HalfWidth, s.Closed, (double[]?)s.AboveGroundM)));
}
