using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// A circuit put on the ground: each run of its roads as a <see cref="RoadRibbon"/> and each place
/// three or more meet as a <see cref="RoadJunction"/>, which together are the surface a wheel is
/// over, and each run as a line of points along that surface for whatever still wants one.
///
/// <para>A road's height is the smoothed ground under it, the lift every road stands proud by and the
/// climb between the circuit's points. A run stops at a junction's mouth, at the height and the slope
/// of the junction's plane there.</para>
///
/// <para>A junction whose roads do not part within reach of its point is not made, and says why: the
/// road that goes through it is one run as it would be with no junction, and any other runs to the
/// point and ends there, lying over it.</para>
/// </summary>
internal static class RoadLaying
{
    /// <param name="Line">The road's centre line on its surface, from the body's centre.</param>
    /// <param name="HalfWidth">Half the road's width where it is widest.</param>
    /// <param name="AboveGroundM">How far the surface is above the ground at each point of the line.</param>
    /// <param name="Ribbon">The surface the line was taken from, or none for a line made by hand.</param>
    public sealed record Strip(double3[] Line, double HalfWidth, double LengthM, bool Closed, double[] AboveGroundM, RoadRibbon? Ribbon = null);

    /// <summary>A circuit as it was laid, all on one chart.</summary>
    /// <param name="Refused">The points where three or more roads meet and no junction could be made, each with the reason.</param>
    public sealed record Network(List<RoadRibbon> Ribbons, List<RoadJunction> Junctions, List<(int Node, string Why)> Refused);

    /// <summary>What is joined into one piece: runs, and the junctions they stop at. The physics is given one solid for each.</summary>
    public sealed record Component(List<RoadRibbon> Ribbons, List<RoadJunction> Junctions);

    /// <summary>Every run of <paramref name="circuit"/> as a surface. See <see cref="Laid"/>.</summary>
    public static List<RoadRibbon> Ribbons(Circuit circuit, Func<double, double, double3> dirOf, double radiusM,
                                           Func<double3, double> groundAt, double liftM, double spacingM) =>
        Laid(circuit, dirOf, radiusM, groundAt, liftM, spacingM).Ribbons;

    private sealed record Pending(RoadLine Line, RoadProfile Profile, RoadRibbon.Span[] Spans, RoadRibbon.Survey Survey,
                                  RoadJunction? Start, RoadJunction.Arm? StartArm, RoadJunction? End, RoadJunction.Arm? EndArm);

    /// <summary>
    /// Every run and every junction of <paramref name="circuit"/>, on one chart, the ground read every
    /// <paramref name="spacingM"/> along each run.
    /// </summary>
    /// <param name="groundAt">The ground's height over the body's mean radius, in a direction from its centre.</param>
    public static Network Laid(Circuit circuit, Func<double, double, double3> dirOf, double radiusM,
                               Func<double3, double> groundAt, double liftM, double spacingM)
    {
        Network network = new([], [], []);
        if (circuit.Nodes.Count == 0) return network;

        RoadChart chart = RoadLayout.Chart(circuit, dirOf, radiusM);
        Dictionary<int, double3> at = RoadLayout.Places(circuit, dirOf, radiusM);
        double Terrain(Plan place) => groundAt(chart.Dir(place));

        Dictionary<Circuit.Road, RoadArc> arcs = new(ReferenceEqualityComparer.Instance);
        foreach (Circuit.Road road in circuit.Roads)
        {
            if (at.ContainsKey(road.From) && at.ContainsKey(road.To)) arcs[road] = RoadLayout.Arc(chart, RoadLayout.Controls(circuit, at, road, dirOf, radiusM));
        }

        Dictionary<int, RoadJunction> junctions = [];
        foreach (Circuit.Node node in circuit.Nodes)
        {
            List<RoadJunction.Arm> arms = [];
            foreach (Circuit.Road road in circuit.Roads)
            {
                if (!road.Touches(node.Id) || !arcs.TryGetValue(road, out RoadArc? arc)) continue;
                bool leaves = road.From == node.Id;
                int far = leaves ? road.To : road.From;
                arms.Add(new RoadJunction.Arm
                {
                    Road = road, Far = far, Out = leaves ? arc : arc.Reversed(),
                    HalfWidth = 0.5 * (Circuit.EndWidth(road, node.Id) ?? circuit.WidthOf(road)),
                    HeightM = Math.Max(node.HeightM, 0.0), FarHeightM = Math.Max(circuit.Find(far)?.HeightM ?? 0.0, 0.0),
                    BankOutDeg = Circuit.EndBankDeg(road, node.Id) is { } bank ? (leaves ? bank : -bank) : null,
                    PartnerFar = RoadLayout.Through(circuit, at, node.Id, far) is { } onward && RoadLayout.Through(circuit, at, node.Id, onward) == far ? onward : null,
                });
            }
            if (arms.Count < 3) continue;

            // Whatever goes wrong in making one, the roads are still laid: as with no junction there.
            RoadJunction? junction = null;
            string why;
            try
            {
                junction = RoadJunction.Of(node.Id, chart, chart.Of(at[node.Id]), arms, node.JunctionRadiusM ?? RoadJunction.DefaultRadiusM, out why);
                junction?.Tilt(Terrain);
                if (junction is not null && !(double.IsFinite(junction.Gradient.E) && double.IsFinite(junction.Gradient.N))) (junction, why) = (null, "its plane could not be worked out");
            }
            catch (Exception e)
            {
                (junction, why) = (null, $"making it threw {e.GetType().Name}: {e.Message}");
            }

            if (junction is null)
            {
                network.Refused.Add((node.Id, why));
                continue;
            }
            junctions[node.Id] = junction;
            network.Junctions.Add(junction);
        }

        List<Pending> pending = [];
        foreach (RoadLayout.Run run in RoadLayout.Runs(circuit, dirOf, radiusM, junctions.Keys.ToHashSet()))
        {
            int legs = run.Legs.Count;
            RoadJunction? start = run.Closed ? null : junctions.GetValueOrDefault(run.Legs[0].From), end = run.Closed ? null : junctions.GetValueOrDefault(run.Legs[^1].To);
            RoadJunction.Arm? startArm = start?.Arms.First(a => ReferenceEquals(a.Road, run.Legs[0].Road)), endArm = end?.Arms.First(a => ReferenceEquals(a.Road, run.Legs[^1].Road));

            List<RoadArc> curves = [];
            for (int k = 0; k < legs; k++)
            {
                RoadLayout.Leg leg = run.Legs[k];
                RoadArc curve = leg.Forward ? arcs[leg.Road] : arcs[leg.Road].Reversed();
                double from = k == 0 && startArm is not null ? curve.TimeAt(startArm.MouthS) : 0.0;
                double to = k == legs - 1 && endArm is not null ? curve.TimeAt(curve.LengthM - endArm.MouthS) : 1.0;
                curves.Add(from > 0.0 || to < 1.0 ? curve.Part(from, to) : curve);
            }
            RoadLine line = new(curves, run.Closed);
            if (!(line.LengthM > 1e-3)) continue;

            // One value at each point of the run. Where two roads meet at one, what either sets there
            // holds, the mean of the two if both do, and otherwise the road is level and as wide as
            // the mean of the two roads. An end at a junction is at its mouth, and leans as the plane does.
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

            double? startRate = null, endRate = null;
            if (start is not null) (bank[0], startRate) = Lean(start, line.At(0, 0.0));
            if (end is not null) (bank[^1], endRate) = Lean(end, line.At(line.Count - 1, line.LengthM));

            RoadProfile profile = new(knot, height, bank, width, run.Closed, liftM, null, startRate, endRate);
            pending.Add(new Pending(line, profile, spans, RoadRibbon.Look(line, profile, Terrain, spacingM, circuit.GroundSmoothM), start, startArm, end, endArm));
        }

        foreach (RoadJunction junction in network.Junctions)
        {
            double[] under = [.. junction.Arms.Select(_ => double.NegativeInfinity)];
            foreach (Pending run in pending)
            {
                for (int i = 0; i < under.Length; i++)
                {
                    if (ReferenceEquals(junction.Arms[i], run.StartArm)) under[i] = run.Survey.Ground.At(0.0);
                    if (ReferenceEquals(junction.Arms[i], run.EndArm)) under[i] = run.Survey.Ground.At(run.Line.LengthM);
                }
            }
            junction.Raise(Terrain, liftM, under);
        }

        foreach (Pending run in pending)
        {
            RoadProfile.Pin? Pin(RoadJunction? junction, RoadLine.Point on) =>
                junction is null ? null : new RoadProfile.Pin(junction.HeightAt(on.At), Plan.Dot(junction.Gradient, on.Heading));

            RoadProfile profile = run.Profile.Over(run.Survey.Ground, Pin(run.Start, run.Line.At(0, 0.0)), Pin(run.End, run.Line.At(run.Line.Count - 1, run.Line.LengthM)));
            RoadRibbon ribbon = RoadRibbon.Lay(chart, run.Line, profile, run.Spans, Terrain, run.Survey, run.Start, run.End);
            if (run.StartArm is not null) (run.StartArm.Ribbon, run.StartArm.AtStart) = (ribbon, true);
            if (run.EndArm is not null) (run.EndArm.Ribbon, run.EndArm.AtStart) = (ribbon, false);
            network.Ribbons.Add(ribbon);
        }
        foreach (RoadJunction junction in network.Junctions) junction.Edge(Terrain);
        return network;
    }

    // The lean a run has where it stops at a junction, in degrees, and how fast its tangent changes
    // there. On the plane a place a distance to the left of a bend climbs along it less than the
    // centre line does by the share of the bend's radius that distance is, and so the lean changes.
    private static (double BankDeg, double TanRate) Lean(RoadJunction junction, RoadLine.Point on) =>
        (Math.Atan(Plan.Dot(junction.Gradient, on.Heading.Left())) * 180.0 / Math.PI, -on.Curvature * Plan.Dot(junction.Gradient, on.Heading));

    /// <summary>The runs and junctions that are joined to one another, a piece at a time.</summary>
    public static List<Component> Components(IReadOnlyList<RoadRibbon> ribbons)
    {
        List<Component> components = [];
        HashSet<RoadRibbon> done = new(ReferenceEqualityComparer.Instance);
        foreach (RoadRibbon first in ribbons)
        {
            if (!done.Add(first)) continue;
            Component component = new([], []);
            Queue<RoadRibbon> reached = new([first]);
            while (reached.TryDequeue(out RoadRibbon? ribbon))
            {
                component.Ribbons.Add(ribbon);
                foreach (RoadJunction? junction in new[] { ribbon.StartJunction, ribbon.EndJunction })
                {
                    if (junction is null || component.Junctions.Contains(junction)) continue;
                    component.Junctions.Add(junction);
                    foreach (RoadJunction.Arm arm in junction.Arms)
                    {
                        if (arm.Ribbon is { } other && done.Add(other)) reached.Enqueue(other);
                    }
                }
            }
            components.Add(component);
        }
        return components;
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
                                  Func<double3, double> groundAt, double liftM, double spacingM) =>
        Strips(Laid(circuit, dirOf, radiusM, groundAt, liftM, spacingM).Ribbons, groundAt, spacingM);

    /// <summary>Runs that are laid, each as a line of points along its surface.</summary>
    public static List<Strip> Strips(IReadOnlyList<RoadRibbon> ribbons, Func<double3, double> groundAt, double spacingM)
    {
        List<Strip> strips = [];
        foreach (RoadRibbon ribbon in ribbons)
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
