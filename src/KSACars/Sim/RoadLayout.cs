using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// A circuit's roads as centre lines over the body: one cubic curve a road, leaving each end along a
/// handle.
///
/// <para>A handle nobody set is worked out from what meets at the point. Two roads there are one road
/// going through, so each leaves along the line between the points either side. At a junction a road
/// goes through with the one most nearly opposite it, if that is within 45 degrees of straight on and
/// each is the other's choice; any other road leaves straight for its far end, which is a side road
/// meeting a through road.</para>
/// </summary>
internal static class RoadLayout
{
    /// <summary>
    /// One road's centre line, as directions from the body's centre, and how far above the ground it
    /// is at each: eased from one end's height to the other's, level at both, so a road through a
    /// point has no step in its slope there.
    /// </summary>
    public sealed record Stretch(int From, int To, double WidthM, double3[] Line, double[] HeightM);

    private static readonly double ThroughCos = Math.Cos(135.0 * Math.PI / 180.0);

    /// <summary>
    /// Every road of <paramref name="circuit"/>, sampled no coarser than <paramref name="spacingM"/> on a
    /// body of <paramref name="radiusM"/>. <paramref name="dirOf"/> is the body's own latitude and
    /// longitude, in degrees, as a direction from its centre.
    /// </summary>
    public static List<Stretch> Of(Circuit circuit, Func<double, double, double3> dirOf, double radiusM, double spacingM)
    {
        Dictionary<int, double3> at = Places(circuit, dirOf, radiusM);

        List<Stretch> stretches = [];
        foreach (Circuit.Road road in circuit.Roads)
        {
            if (!at.TryGetValue(road.From, out double3 a) || !at.TryGetValue(road.To, out double3 b)) continue;
            (_, double3 leave, double3 arrive, _) = Controls(circuit, at, road, dirOf, radiusM);
            double3[] line = RoadCurve.Sample(a, leave, arrive, b, spacingM);
            double from = circuit.Find(road.From)?.HeightM ?? 0.0, to = circuit.Find(road.To)?.HeightM ?? 0.0;
            double[] height = new double[line.Length];
            for (int i = 0; i < line.Length; i++)
            {
                line[i] = Vec.Unit(line[i]);
                double t = line.Length > 1 ? (double)i / (line.Length - 1) : 0.0;
                height[i] = from + ((to - from) * t * t * (3.0 - (2.0 * t)));
            }
            stretches.Add(new Stretch(road.From, road.To, circuit.WidthOf(road), line, height));
        }
        return stretches;
    }

    /// <summary>Every point of <paramref name="circuit"/> on the body's mean sphere, from its centre, by its id.</summary>
    public static Dictionary<int, double3> Places(Circuit circuit, Func<double, double, double3> dirOf, double radiusM)
    {
        Dictionary<int, double3> at = [];
        foreach (Circuit.Node n in circuit.Nodes) at[n.Id] = Vec.Unit(dirOf(n.LatDeg, n.LonDeg)) * radiusM;
        return at;
    }

    /// <summary>The four points a road's curve is drawn from: its two ends and, between them, where each handle reaches to.</summary>
    public static (double3 From, double3 Leave, double3 Arrive, double3 To) Controls(
        Circuit circuit, Dictionary<int, double3> at, Circuit.Road road, Func<double, double, double3> dirOf, double radiusM)
    {
        double3 a = at[road.From], b = at[road.To];
        return (a, a + Handle(circuit, at, road.From, road.To, road.FromHandle, dirOf, radiusM),
                b + Handle(circuit, at, road.To, road.From, road.ToHandle, dirOf, radiusM), b);
    }

    /// <summary>A chart flat at the middle of a circuit's points.</summary>
    public static RoadChart Chart(Circuit circuit, Func<double, double, double3> dirOf, double radiusM) =>
        RoadChart.About(circuit.Nodes.Select(n => dirOf(n.LatDeg, n.LonDeg)), radiusM, dirOf(90.0, 0.0));

    /// <summary>
    /// A road's curve on a chart, from its <c>From</c> end to its <c>To</c>: the same four points,
    /// each put on the chart where it is seen from the body's centre.
    /// </summary>
    public static RoadArc Arc(RoadChart chart, (double3 From, double3 Leave, double3 Arrive, double3 To) controls) =>
        new(chart.Of(controls.From), chart.Of(controls.Leave), chart.Of(controls.Arrive), chart.Of(controls.To));

    /// <summary>The handle of the road from <paramref name="node"/> to <paramref name="far"/> at the node, as an offset from it in metres.</summary>
    public static double3 Handle(Circuit circuit, Dictionary<int, double3> at, int node, int far, Circuit.Place? set,
                                 Func<double, double, double3> dirOf, double radiusM)
    {
        double3 here = at[node];
        if (set is not null) return (Vec.Unit(dirOf(set.LatDeg, set.LonDeg)) * radiusM) - here;

        double3 up = Vec.Unit(here);
        double3 chord = at[far] - here;
        double3 leave = Arm(here, at[far], up);
        double corner = circuit.Find(node)?.Corner ?? 1.0;

        if (Through(circuit, at, node, far) is { } partner)
        {
            double3 through = leave - Arm(here, at[partner], up);
            if (Vec.Len(through) > 1e-9) leave = Vec.Unit(through);
        }
        return leave * (corner * Vec.Len(chord) / 3.0);
    }

    /// <summary>The point the road from <paramref name="far"/> carries on to through <paramref name="node"/>, or none if it ends or joins there.</summary>
    public static int? Through(Circuit circuit, Dictionary<int, double3> at, int node, int far)
    {
        if (!at.TryGetValue(node, out double3 here) || !at.ContainsKey(far)) return null;
        double3 up = Vec.Unit(here);
        double3 leave = Arm(here, at[far], up);

        List<int> others = [];
        foreach (Circuit.Road r in circuit.Roads)
        {
            if (!r.Touches(node)) continue;
            int other = r.From == node ? r.To : r.From;
            if (other != far && at.ContainsKey(other)) others.Add(other);
        }

        return Opposite(here, up, leave, others, at) is { } partner
               && (others.Count == 1 || IsThrough(here, up, leave, far, partner, circuit, at, node)) ? partner : null;
    }

    /// <summary>
    /// A run of roads that go through one another, as one line: what is drawn as a single strip, so
    /// there is no seam where a road passes a point. An end that meets other roads is sunk by
    /// <see cref="SinkStartM"/> or <see cref="SinkEndM"/>, each road at a junction by a different
    /// amount, so no two surfaces lie in one plane there. A closed run ends on its own first point.
    /// </summary>
    public sealed record Run(double WidthM, double3[] Line, double[] HeightM, bool Closed, double SinkStartM, double SinkEndM);

    private const double SinkM = 0.012, SinkStepM = 0.008;

    /// <summary>The circuit's roads joined into runs. Roads of different widths are not joined.</summary>
    public static List<Run> Runs(Circuit circuit, Func<double, double, double3> dirOf, double radiusM, double spacingM)
    {
        Dictionary<int, double3> at = Places(circuit, dirOf, radiusM);
        List<Stretch> stretches = Of(circuit, dirOf, radiusM, spacingM);

        Stretch? Between(int a, int b) => stretches.FirstOrDefault(s => (s.From == a && s.To == b) || (s.From == b && s.To == a));

        // The road a road carries on as past its end at a point: its partner there, if that is mutual and as wide.
        (int Node, int Far)? Next(int from, int node)
        {
            if (Through(circuit, at, node, from) is not { } onward || Through(circuit, at, node, onward) != from) return null;
            return Between(from, node)?.WidthM == Between(node, onward)?.WidthM ? (node, onward) : null;
        }

        List<Run> runs = [];
        HashSet<Stretch> used = [];
        foreach (Stretch first in stretches)
        {
            if (used.Contains(first)) continue;

            // Back to where the run starts, or all the way round if it is a ring.
            (int a, int b) = (first.From, first.To);
            bool closed = false;
            for (int guard = 0; guard <= stretches.Count; guard++)
            {
                if (Next(b, a) is not { } before) break;
                (a, b) = (before.Far, a);
                if (Between(a, b) == first)
                {
                    closed = true;
                    break;
                }
            }

            List<double3> line = [];
            List<double> height = [];
            int start = a, end = b;
            while (Between(a, b) is { } stretch && used.Add(stretch))
            {
                bool forward = stretch.From == a;
                for (int i = line.Count == 0 ? 0 : 1; i < stretch.Line.Length; i++)
                {
                    int k = forward ? i : stretch.Line.Length - 1 - i;
                    line.Add(stretch.Line[k]);
                    height.Add(stretch.HeightM[k]);
                }
                end = b;
                if (Next(a, b) is not { } onward) break;
                (a, b) = (b, onward.Far);
            }

            runs.Add(new Run(first.WidthM, [.. line], [.. height], closed,
                closed ? 0.0 : Sink(circuit, stretches, used, start), closed ? 0.0 : Sink(circuit, stretches, used, end)));
        }
        return runs;
    }

    // Deeper for each run that already ends at the point, so the ones meeting there are all at different depths.
    private static double Sink(Circuit circuit, List<Stretch> stretches, HashSet<Stretch> used, int node)
    {
        int roads = circuit.Roads.Count(r => r.Touches(node));
        if (roads < 2) return 0.0;
        int before = stretches.Count(s => used.Contains(s) && (s.From == node || s.To == node)) - 1;
        return SinkM + (SinkStepM * Math.Max(before, 0));
    }

    private static double3 Arm(double3 here, double3 far, double3 up)
    {
        double3 flat = Vec.RejectFrom(far - here, up);
        return Vec.Len(flat) > 1e-9 ? Vec.Unit(flat) : Vec.AnyPerpendicular(up);
    }

    private static int? Opposite(double3 here, double3 up, double3 leave, List<int> others, Dictionary<int, double3> at)
    {
        int? best = null;
        double least = double.PositiveInfinity;
        foreach (int other in others)
        {
            double cos = Vec.Dot(leave, Arm(here, at[other], up));
            if (cos < least) (best, least) = (other, cos);
        }
        return best;
    }

    private static bool IsThrough(double3 here, double3 up, double3 leave, int far, int partner, Circuit circuit,
                                  Dictionary<int, double3> at, int node)
    {
        double3 back = Arm(here, at[partner], up);
        if (Vec.Dot(leave, back) > ThroughCos) return false;

        List<int> partnersOthers = [];
        foreach (Circuit.Road r in circuit.Roads)
        {
            if (!r.Touches(node)) continue;
            int other = r.From == node ? r.To : r.From;
            if (other != partner && at.ContainsKey(other)) partnersOthers.Add(other);
        }
        return Opposite(here, up, back, partnersOthers, at) == far;
    }
}
