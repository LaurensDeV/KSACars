using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// A circuit put on the ground: each run of its roads as a <see cref="RoadRibbon"/>, which is the
/// surface a wheel is over, and as a line of points along that surface for whatever still wants one.
///
/// <para>A road's height is the smoothed ground under it, the lift every road stands proud by and the
/// climb between the circuit's points, less the sink of an end that meets other roads.</para>
/// </summary>
internal static class RoadLaying
{
    /// <param name="Line">The road's centre line on its surface, from the body's centre.</param>
    /// <param name="HalfWidth">Half the road's width where it is widest.</param>
    /// <param name="AboveGroundM">How far the surface is above the ground at each point of the line.</param>
    /// <param name="Ribbon">The surface the line was taken from, or none for a line made by hand.</param>
    public sealed record Strip(double3[] Line, double HalfWidth, double LengthM, bool Closed, double[] AboveGroundM, RoadRibbon? Ribbon = null);

    /// <summary>
    /// Every run of <paramref name="circuit"/> as a surface, on one chart, the ground read every
    /// <paramref name="spacingM"/> along each.
    /// </summary>
    /// <param name="groundAt">The ground's height over the body's mean radius, in a direction from its centre.</param>
    public static List<RoadRibbon> Ribbons(Circuit circuit, Func<double, double, double3> dirOf, double radiusM,
                                           Func<double3, double> groundAt, double liftM, double spacingM)
    {
        List<RoadRibbon> ribbons = [];
        if (circuit.Nodes.Count == 0) return ribbons;

        RoadChart chart = RoadLayout.Chart(circuit, dirOf, radiusM);
        Dictionary<int, double3> at = RoadLayout.Places(circuit, dirOf, radiusM);
        foreach (RoadLayout.Run run in RoadLayout.Runs(circuit, dirOf, radiusM))
        {
            int legs = run.Legs.Count;
            List<RoadArc> arcs = [];
            foreach (RoadLayout.Leg leg in run.Legs)
            {
                (double3 a, double3 b, double3 c, double3 d) = RoadLayout.Controls(circuit, at, leg.Road, dirOf, radiusM);
                arcs.Add(leg.Forward ? RoadLayout.Arc(chart, (a, b, c, d)) : RoadLayout.Arc(chart, (d, c, b, a)));
            }
            RoadLine line = new(arcs, run.Closed);
            if (!(line.LengthM > 1e-3)) continue;

            // One value at each point of the run. Where two roads meet at one, what either sets there
            // holds, the mean of the two if both do, and otherwise the road is level and as wide as
            // the mean of the two roads.
            double[] knot = new double[legs + 1], height = new double[legs + 1], bank = new double[legs + 1], width = new double[legs + 1];
            RoadRibbon.Span[] spans = new RoadRibbon.Span[legs];
            for (int k = 0; k <= legs; k++)
            {
                knot[k] = line.StartOf(k);
                RoadLayout.Leg? before = k > 0 ? run.Legs[k - 1] : run.Closed ? run.Legs[^1] : null;
                RoadLayout.Leg? after = k < legs ? run.Legs[k] : run.Closed ? run.Legs[0] : null;
                int node = after?.From ?? before!.To;
                height[k] = circuit.Find(node)?.HeightM ?? 0.0;
                bank[k] = Mean(Bank(before, node), Bank(after, node)) ?? 0.0;
                width[k] = Mean(before is null ? null : Circuit.EndWidth(before.Road, node), after is null ? null : Circuit.EndWidth(after.Road, node))
                           ?? Mean(before is null ? null : circuit.WidthOf(before.Road), after is null ? null : circuit.WidthOf(after.Road))
                           ?? circuit.WidthM;
                if (k < legs) spans[k] = new RoadRibbon.Span(run.Legs[k].From, run.Legs[k].To, line.StartOf(k), line.StartOf(k + 1));
            }

            RoadProfile profile = new(knot, height, bank, width, run.Closed, liftM, run.SinkStartM, run.SinkEndM);
            ribbons.Add(RoadRibbon.Lay(chart, line, profile, spans, place => groundAt(chart.Dir(place)), spacingM, circuit.GroundSmoothM));
        }
        return ribbons;
    }

    // A road's bank is saved as its left edge up travelling from its From to its To; a run that takes it the other way sees the other edge up.
    private static double? Bank(RoadLayout.Leg? leg, int node) =>
        leg is not null && Circuit.EndBankDeg(leg.Road, node) is { } bank ? (leg.Forward ? bank : -bank) : null;

    private static double? Mean(double? a, double? b) => a is { } x && b is { } y ? 0.5 * (x + y) : a ?? b;

    /// <summary>
    /// The same runs as lines of points no more than <paramref name="spacingM"/> apart along each
    /// surface, every point of the circuit among them, so a line turns where its road does.
    /// </summary>
    public static List<Strip> Lay(Circuit circuit, Func<double, double, double3> dirOf, double radiusM,
                                  Func<double3, double> groundAt, double liftM, double spacingM)
    {
        List<Strip> strips = [];
        foreach (RoadRibbon ribbon in Ribbons(circuit, dirOf, radiusM, groundAt, liftM, spacingM))
        {
            List<double3> line = [];
            List<double> above = [];
            double widest = 0.0;
            for (int arc = 0; arc < ribbon.Line.Count; arc++)
            {
                double from = ribbon.Line.StartOf(arc), length = ribbon.Line.StartOf(arc + 1) - from;
                int steps = Math.Max(1, (int)Math.Ceiling((length / Math.Max(spacingM, 0.01)) - 1e-9));
                for (int i = line.Count == 0 ? 0 : 1; i <= steps; i++)
                {
                    RoadRibbon.Section section = ribbon.At(Math.Min(arc + (i == steps ? 1 : 0), ribbon.Line.Count - 1),
                                                           i == steps ? from + length : from + (length * i / steps));
                    line.Add(ribbon.Point(section, 0.0, section.Height));
                    above.Add(section.Height - groundAt(Vec.Unit(line[^1])));
                    widest = Math.Max(widest, section.HalfWidth);
                }
            }
            strips.Add(new Strip([.. line], widest, ribbon.LengthM, ribbon.Closed, [.. above], ribbon));
        }
        return strips;
    }

    /// <summary>The surface a wheel is asked about, over what was laid.</summary>
    public static RoadSurface Surface(IEnumerable<Strip> strips)
    {
        List<Strip> all = [.. strips];
        return all.TrueForAll(s => s.Ribbon is not null)
            ? new RoadSurface([.. all.Select(s => s.Ribbon!)])
            : new RoadSurface(all.Select(s => (s.Line, s.HalfWidth, s.Closed, (double[]?)s.AboveGroundM)));
    }
}
