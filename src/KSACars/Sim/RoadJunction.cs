using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// Where three or more roads meet, as one flat piece of asphalt: a polygon on the circuit's chart
/// with a tilted plane over it, which every road there stops at and is brought to.
///
/// <para>Each road is an arm. It stops at its mouth, a section square to its own centre line, and
/// from mouth to mouth the polygon's edge follows one arm's edge in towards the point, rounds the
/// corner between the two on an arc that touches both, and follows the next arm's edge out. Two
/// arms at less than <see cref="BevelRad"/> to one another are joined straight across from mouth to
/// mouth. Two whose edges do not cross, either side of a road going through or a narrow road against
/// the end of a wide one, are each followed in to beside the point and joined there.</para>
///
/// <para>The plane is the road's that goes through: its climb along it and its lean across it. A
/// road arriving at another slope is brought to the plane's at its mouth, so it has a landing. With
/// nothing going through, the plane is the one nearest every arm's own climb, no steeper than
/// <see cref="MostGrade"/>. It is never under the ground, so on a hillside it stands on fill.</para>
///
/// <para>Past the asphalt between two mouths there is what a road has past its edge: a verge and an
/// embankment, no further out than the nearest other stretch of edge is; or, with any of the edge
/// more than <see cref="RoadRibbon.DeckOverM"/> up, a deck with nothing beside it.</para>
///
/// <para>Roads too nearly alongside one another to part within reach of the point make no junction:
/// <see cref="Of"/> answers none and says why.</para>
/// </summary>
internal sealed class RoadJunction
{
    public const double DefaultRadiusM = 6.0;

    /// <summary>How far past the end of a corner's rounding an arm's mouth is.</summary>
    public const double MouthPastM = 0.5;

    /// <summary>The furthest from the point a mouth may be: this share of the road's length, and this many of its widths.</summary>
    public const double MostMouthShare = 0.45, MostMouthWidths = 6.0;

    /// <summary>The steepest a plane fitted to arms that do not go through one another may be.</summary>
    public const double MostGrade = 0.15;

    public static readonly double BevelRad = 20.0 * Math.PI / 180.0;

    // Two arms this far round from one another with edges that do not cross are either side of a road going through.
    private static readonly double JoinRad = 150.0 * Math.PI / 180.0;

    // No stretch of the edge is longer than this, so that what is past it can be cut short near a corner and not all along a stretch.
    private const double MostEdgeM = 2.0;

    // Two points of the polygon's edge nearer than this are one.
    private const double SameM = 0.002;

    private const int ArmSamples = 64;

    /// <summary>One road at a junction, from the point outwards.</summary>
    public sealed class Arm
    {
        public required Circuit.Road Road { get; init; }

        /// <summary>The point at the road's other end.</summary>
        public required int Far { get; init; }

        /// <summary>The road's centre line, from the junction's point to its other end.</summary>
        public required RoadArc Out { get; init; }

        public required double HalfWidth { get; init; }

        /// <summary>How far above the ground the junction's point and the road's other end are set.</summary>
        public double HeightM { get; init; }

        public double FarHeightM { get; init; }

        /// <summary>The bank set at this end, raising the left edge of one leaving the junction, or none.</summary>
        public double? BankOutDeg { get; init; }

        /// <summary>The point at the far end of the road this one goes through the junction with, or none.</summary>
        public int? PartnerFar { get; init; }

        /// <summary>Which of the junction's arms that is, or -1.</summary>
        public int Partner { get; internal set; } = -1;

        /// <summary>How far along the road from the point its mouth is.</summary>
        public double MouthS { get; internal set; }

        public Plan MouthAt { get; internal set; }

        /// <summary>The way the road runs at its mouth, away from the junction.</summary>
        public Plan MouthHeading { get; internal set; }

        /// <summary>The run that ends at the mouth, and whether it starts there.</summary>
        public RoadRibbon? Ribbon { get; internal set; }

        public bool AtStart { get; internal set; }

        /// <summary>Where in the polygon the mouth's right corner, centre and left corner are, seen leaving the junction.</summary>
        public (int Right, int Centre, int Left) Mouth { get; internal set; }

        internal double CapS;
        internal Plan[] At = [], Heading = [];
        internal double StepS;

        public Plan Centre(double s) => Out.At(Out.TimeAt(s));

        public Plan HeadingAt(double s) => Out.Heading(Out.TimeAt(s));

        /// <summary>A place <paramref name="offset"/> to the left of the centre line, <paramref name="s"/> along it.</summary>
        public Plan Beside(double s, double offset)
        {
            double t = Out.TimeAt(s);
            return Out.At(t) + (Out.Heading(t).Left() * offset);
        }
    }

    /// <summary>A stretch of the polygon's edge between two mouths: the places of it in <see cref="Boundary"/>, first to last.</summary>
    public readonly record struct Chain(int From, int To);

    public int Node { get; }
    public RoadChart Chart { get; }
    public Plan At { get; }
    public IReadOnlyList<Arm> Arms { get; }

    /// <summary>The asphalt's edge, anticlockwise seen from above. A mouth is three of these: its two corners and its centre.</summary>
    public Plan[] Boundary { get; }

    /// <summary>The way out of the asphalt at each place of the edge, level; nothing at a mouth's centre.</summary>
    public Plan[] Outward { get; }

    /// <summary>One for each arm: the edge from its left corner round to the next arm's right.</summary>
    public Chain[] Chains { get; }

    /// <summary>The asphalt as triangles, three places of <see cref="Boundary"/> each; one past its last is <see cref="Hub"/>.</summary>
    public int[] Faces { get; }

    /// <summary>The place the asphalt's triangles fan about, where they do: the junction's point, unless a stretch of the edge is in line with it.</summary>
    public Plan Hub { get; }

    /// <summary>The least any corner was rounded by, which is less than was asked where a corner had no room for it.</summary>
    public double RadiusM { get; }

    /// <summary>How many corners were joined straight across, their arms being too nearly alongside to round.</summary>
    public int Bevels { get; }

    /// <summary>The climb of the plane for each metre east and each metre north.</summary>
    public Plan Gradient { get; private set; }

    /// <summary>The plane's height at the junction's point, over the body's mean radius.</summary>
    public double HeightM { get; private set; }

    public bool Deck { get; private set; }

    /// <summary>The furthest any of the junction is from its point: its asphalt, and whatever is past it.</summary>
    public double ReachM { get; private set; }

    /// <summary>
    /// One line out from the asphalt's edge that what is past it is drawn along: from a place of
    /// <see cref="Boundary"/>, a level way out, and how far the verge and the embankment go that way
    /// before they are buried. At a mouth's corner it is the run's own row, <paramref name="Corner"/>.
    /// </summary>
    public readonly record struct Spoke(int At, Plan Outward, double ToeM, bool Corner);

    /// <summary>The lines out from the edge, in order round it, a mouth's left corner to the next mouth's right.</summary>
    public Spoke[] Spokes { get; private set; } = [];

    /// <summary>Each two of <see cref="Spokes"/> with a surface between them: along a stretch of the edge, or round one place of it where the edge turns a corner.</summary>
    public (int From, int To)[] Strips { get; private set; } = [];

    // The two spokes either end of the stretch of edge that starts at each place, or none; and the furthest any spoke at a place goes.
    private (int From, int To)?[] _stretch = [];
    private Lazy<double[]> _out = new(() => []);
    private Lazy<double[]> _most = new(() => []);

    // A way out of the edge at a place that is further than this from square to a stretch next to it is
    // not that stretch's: the edge turns a corner there, and the stretch has its own.
    private static readonly double CornerRad = 20.0 * Math.PI / 180.0, FanRad = 15.0 * Math.PI / 180.0;

    private RoadJunction(int node, RoadChart chart, Plan at, Arm[] arms, Plan[] boundary, Plan[] outward, Chain[] chains, int[] faces,
                         Plan hub, double radiusM, int bevels)
    {
        (Node, Chart, At, Arms, Boundary, Outward, Chains, Faces, Hub, RadiusM, Bevels) = (node, chart, at, arms, boundary, outward, chains, faces, hub, radiusM, bevels);
        foreach (Plan p in boundary) ReachM = Math.Max(ReachM, (p - at).Len);
    }

    // ---- the polygon ------------------------------------------------------------------------------

    private enum Kind
    {
        Fillet,
        Bevel,
        Join,
    }

    // The corner between one arm's left edge and the next arm's right: how far along each its rounding starts.
    private readonly record struct Corner(Kind Kind, double LeftS, double RightS, double RadiusM, double TurnRad);

    /// <summary>
    /// The junction of <paramref name="arms"/> at a point, its corners rounded by
    /// <paramref name="radiusM"/> where there is room. Null, with the reason, where the roads do not
    /// part within reach of the point and no polygon can be made of them.
    /// </summary>
    public static RoadJunction? Of(int node, RoadChart chart, Plan at, IReadOnlyList<Arm> arms, double radiusM, out string why)
    {
        why = "";
        if (arms.Count < 3)
        {
            why = "fewer than three roads meet";
            return null;
        }

        Arm[] sorted = [.. arms.OrderBy(a => Math.Atan2(a.Out.Heading(0.0).N, a.Out.Heading(0.0).E))];
        Dictionary<int, int> place = [];
        for (int i = 0; i < sorted.Length; i++) place[sorted[i].Far] = i;
        foreach (Arm arm in sorted)
        {
            arm.CapS = Math.Min(MostMouthShare * arm.Out.LengthM, MostMouthWidths * 2.0 * arm.HalfWidth);
            if (arm.CapS <= MouthPastM)
            {
                why = $"the road to {arm.Far} is too short to stop at a mouth";
                return null;
            }
            arm.StepS = arm.CapS / ArmSamples;
            arm.At = new Plan[ArmSamples + 1];
            arm.Heading = new Plan[ArmSamples + 1];
            for (int k = 0; k <= ArmSamples; k++)
            {
                double t = arm.Out.TimeAt(k * arm.StepS);
                (arm.At[k], arm.Heading[k]) = (arm.Out.At(t), arm.Out.Heading(t));
            }
        }

        int n = sorted.Length, bevels = 0;
        Corner[] corners = new Corner[n];
        double least = radiusM;
        for (int i = 0; i < n; i++)
        {
            Arm a = sorted[i], b = sorted[(i + 1) % n];
            if (Round(a, b, Math.Max(radiusM, 0.0)) is not { } corner)
            {
                why = $"the roads to {a.Far} and {b.Far} are too nearly alongside one another to part within reach of the point";
                return null;
            }
            corners[i] = corner;
            if (corner.Kind == Kind.Fillet) least = Math.Min(least, corner.RadiusM);
            if (corner.Kind == Kind.Bevel) bevels++;
        }

        for (int i = 0; i < n; i++)
        {
            Arm arm = sorted[i];
            arm.MouthS = Math.Min(Math.Max(corners[i].LeftS, corners[(i + n - 1) % n].RightS) + MouthPastM, arm.CapS);
            double t = arm.Out.TimeAt(arm.MouthS);
            (arm.MouthAt, arm.MouthHeading) = (arm.Out.At(t), arm.Out.Heading(t));
        }

        List<Plan> edge = [], outward = [];
        Chain[] chains = new Chain[n];
        void Add(Plan p, Plan o, bool always = false)
        {
            if (!always && edge.Count > 0 && (edge[^1] - p).Len < SameM)
            {
                // A corner that is not rounded is one place with two ways out of it: the way between them.
                if (o.Len > 0.0 && outward[^1].Len > 0.0 && (outward[^1] + o).Len > 1e-6) outward[^1] = (outward[^1] + o).Unit();
                return;
            }
            edge.Add(p);
            outward.Add(o);
        }

        for (int i = 0; i < n; i++)
        {
            Arm a = sorted[i], b = sorted[(i + 1) % n];
            Plan across = a.MouthHeading.Left();
            Add(a.MouthAt - (across * a.HalfWidth), -across, always: true);
            Add(a.MouthAt, default, always: true);
            Add(a.MouthAt + (across * a.HalfWidth), across, always: true);
            a.Mouth = (edge.Count - 3, edge.Count - 2, edge.Count - 1);

            int from = edge.Count - 1;
            Corner corner = corners[i];
            if (corner.Kind != Kind.Bevel)
            {
                Along(a, a.MouthS, corner.LeftS, a.HalfWidth, Add);
                if (corner.Kind == Kind.Fillet && corner.RadiusM > 0.0) Arc(a, b, corner, Add);
                if (corner.Kind == Kind.Join)
                {
                    if (corner.LeftS <= 0.0 && corner.RightS <= 0.0) Fan(at, a, b, corner.TurnRad, Add);

                    // Straight across from where one edge stops to where the other starts: the step from a narrow road's edge to a wide one's.
                    Plan square = b.HeadingAt(corner.RightS).Left();
                    Plan stop = edge[^1], start = b.Centre(corner.RightS) - (square * b.HalfWidth);
                    int pieces = Math.Max(1, (int)Math.Ceiling((start - stop).Len / MostEdgeM));
                    for (int k = 1; k < pieces; k++) Add(stop + ((start - stop) * ((double)k / pieces)), -(start - stop).Left().Unit());
                    Add(start, -square);
                }
                Along(b, corner.RightS, b.MouthS, -b.HalfWidth, Add);
            }

            // The chain's last place is the next arm's right corner, which that arm puts in itself.
            Plan next = b.MouthAt - (b.MouthHeading.Left() * b.HalfWidth);
            while (edge.Count - 1 > from && (edge[^1] - next).Len < SameM)
            {
                edge.RemoveAt(edge.Count - 1);
                outward.RemoveAt(outward.Count - 1);
            }
            chains[i] = new Chain(from, edge.Count);
        }
        chains[n - 1] = chains[n - 1] with { To = 0 };

        Plan[] boundary = [.. edge];
        if (Area(boundary) <= 0.0 || Crosses(boundary))
        {
            why = "the roads' edges cross one another round the point";
            return null;
        }
        if (Triangles(boundary, at, sorted.Select(a => a.MouthAt), out Plan hub) is not { } faces)
        {
            why = "the asphalt between the roads could not be cut into triangles";
            return null;
        }

        for (int i = 0; i < n; i++)
        {
            Arm arm = sorted[i];
            arm.Partner = arm.PartnerFar is { } far && place.TryGetValue(far, out int partner) ? partner : -1;
            (arm.At, arm.Heading) = ([], []);
        }
        return new RoadJunction(node, chart, at, sorted, boundary, [.. outward], chains, faces, hub, Math.Max(least, 0.0), bevels);
    }

    // An arm's edge from one distance along it to another, without the place it starts from.
    private static void Along(Arm arm, double fromS, double toS, double offset, Action<Plan, Plan, bool> add)
    {
        double length = Math.Abs(toS - fromS);
        if (!(length > 0.0)) return;

        double bend = 0.0;
        for (int k = 0; k <= 4; k++) bend = Math.Max(bend, Math.Abs(arm.Out.Curvature(arm.Out.TimeAt(fromS + ((toS - fromS) * k / 4.0)))));
        double squeezed = bend * Math.Abs(offset);
        double edge = squeezed < 0.95 ? bend / (1.0 - squeezed) : bend * 20.0;
        double step = edge > 1e-9 ? Math.Clamp(Math.Sqrt(8.0 * RoadTessellation.PlanToleranceM / edge), RoadTessellation.LeastStepM, MostEdgeM) : MostEdgeM;
        int steps = Math.Max(1, (int)Math.Ceiling(length / step));
        for (int k = 1; k <= steps; k++)
        {
            double t = arm.Out.TimeAt(fromS + ((toS - fromS) * k / steps));
            Plan left = arm.Out.Heading(t).Left();
            add(arm.Out.At(t) + (left * offset), offset < 0.0 ? -left : left, false);
        }
    }

    // The rounding of a corner, between the places it touches the two edges at and without either.
    private static void Arc(Arm a, Arm b, Corner corner, Action<Plan, Plan, bool> add)
    {
        Plan centre = a.Beside(corner.LeftS, a.HalfWidth + corner.RadiusM);
        Plan from = a.Beside(corner.LeftS, a.HalfWidth) - centre, to = b.Beside(corner.RightS, -b.HalfWidth) - centre;
        double turn = Math.Atan2(Plan.Cross(from, to), Plan.Dot(from, to));
        double each = Math.Min(Math.Sqrt(8.0 * RoadTessellation.PlanToleranceM / corner.RadiusM), 10.0 * Math.PI / 180.0);
        int steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(turn) / each));
        for (int k = 1; k < steps; k++)
        {
            Plan spoke = Turned(from, turn * k / steps);
            add(centre + spoke, -spoke.Unit(), false);
        }
    }

    // Round the point from the foot of one arm's edge to the foot of the next arm's, where they are more than half a turn apart.
    private static void Fan(Plan at, Arm a, Arm b, double turnRad, Action<Plan, Plan, bool> add)
    {
        double turn = turnRad - Math.PI;
        if (!(turn > 1e-6)) return;
        Plan from = a.Out.Heading(0.0).Left();
        double widest = Math.Max(a.HalfWidth, b.HalfWidth);
        double each = Math.Min(Math.Sqrt(8.0 * RoadTessellation.PlanToleranceM / widest), 10.0 * Math.PI / 180.0);
        int steps = Math.Max(1, (int)Math.Ceiling(turn / each));
        for (int k = 1; k < steps; k++)
        {
            double share = (double)k / steps;
            Plan spoke = Turned(from, turn * share);
            add(at + (spoke * (a.HalfWidth + ((b.HalfWidth - a.HalfWidth) * share))), spoke, false);
        }
    }

    private static Plan Turned(Plan v, double angle)
    {
        double cos = Math.Cos(angle), sin = Math.Sin(angle);
        return new Plan((v.E * cos) - (v.N * sin), (v.E * sin) + (v.N * cos));
    }

    // The corner between a's left edge and b's right, rounded by as much of the radius as there is room for.
    private static Corner? Round(Arm a, Arm b, double radiusM)
    {
        Plan ta = a.Out.Heading(0.0), tb = b.Out.Heading(0.0);
        double turn = Math.Atan2(Plan.Cross(ta, tb), Plan.Dot(ta, tb));
        if (turn <= 1e-9) turn += 2.0 * Math.PI;

        if (Meet(a, b, 0.0) is not { } sharp)
        {
            // The edges do not cross where one runs into the other road's end beside the point instead of
            // its edge: a narrow road against a wide one. It is followed that far, and the wide road's
            // edge from its foot.
            if (turn < Math.PI - 1e-9)
            {
                if (Butts(a, a.HalfWidth, b) is { } left) return new Corner(Kind.Join, left, 0.0, 0.0, turn);
                if (Butts(b, -b.HalfWidth, a) is { } right) return new Corner(Kind.Join, 0.0, right, 0.0, turn);
                if (turn < JoinRad) return null;
            }
            return new Corner(Kind.Join, 0.0, 0.0, 0.0, turn);
        }

        Plan ha = a.HeadingAt(sharp.LeftS), hb = b.HeadingAt(sharp.RightS);
        double between = Math.Atan2(Plan.Cross(ha, hb), Plan.Dot(ha, hb));
        if (between < BevelRad) return new Corner(Kind.Bevel, sharp.LeftS, sharp.RightS, 0.0, turn);

        (double LeftS, double RightS) found = sharp;
        double fits = 0.0;
        if (radiusM > 0.0 && Meet(a, b, radiusM) is { } whole)
        {
            (found, fits) = (whole, radiusM);
        }
        else if (radiusM > 0.0)
        {
            double tooMuch = radiusM;
            for (int i = 0; i < 12; i++)
            {
                double middle = 0.5 * (fits + tooMuch);
                if (Meet(a, b, middle) is { } some) (found, fits) = (some, middle);
                else tooMuch = middle;
            }
        }
        return fits < 0.05 ? new Corner(Kind.Fillet, sharp.LeftS, sharp.RightS, 0.0, turn) : new Corner(Kind.Fillet, found.LeftS, found.RightS, fits, turn);
    }

    // How far along an arm its edge, a distance to the left of its centre line, meets the line square
    // across another arm at the point, within that arm's width; none if it does not, or not with room
    // left for a mouth.
    private static double? Butts(Arm arm, double offset, Arm other)
    {
        Plan along = other.Out.Heading(0.0), start = other.Out.At(0.0);
        double closing = Plan.Dot(arm.Out.Heading(0.0), along);
        if (closing > -0.05) return null;

        double s = Math.Max(-Plan.Dot(arm.Beside(0.0, offset) - start, along) / closing, 0.0);
        for (int i = 0; i < 6 && s <= arm.CapS; i++)
        {
            double t = arm.Out.TimeAt(s);
            Plan heading = arm.Out.Heading(t);
            double ahead = Plan.Dot(arm.Out.At(t) + (heading.Left() * offset) - start, along);
            double rate = Plan.Dot(heading, along) * (1.0 - (offset * arm.Out.Curvature(t)));
            if (Math.Abs(ahead) < 1e-10 || rate > -0.05) break;
            s = Math.Max(s - (ahead / rate), 0.0);
        }
        if (s > arm.CapS - MouthPastM) return null;
        return Math.Abs(Plan.Cross(along, arm.Beside(s, offset) - start)) <= other.HalfWidth + 1e-9 ? s : null;
    }

    // Where a's left edge and b's right edge, each moved out by a distance, first cross: how far along
    // each. That is where an arc of that radius touching both has its centre; with no distance, the
    // corner itself. None where they do not cross with room left for a mouth past it.
    private static (double LeftS, double RightS)? Meet(Arm a, Arm b, double outM)
    {
        double offA = a.HalfWidth + outM, offB = -(b.HalfWidth + outM);
        (double s, double u) best = (double.PositiveInfinity, 0.0);
        Plan a0 = a.At[0] + (a.Heading[0].Left() * offA);
        for (int i = 0; i < ArmSamples && i < best.s; i++)
        {
            Plan a1 = a.At[i + 1] + (a.Heading[i + 1].Left() * offA);
            Plan b0 = b.At[0] + (b.Heading[0].Left() * offB);
            for (int j = 0; j < ArmSamples; j++)
            {
                Plan b1 = b.At[j + 1] + (b.Heading[j + 1].Left() * offB);
                Plan da = a1 - a0, db = b1 - b0;
                double cross = Plan.Cross(da, db);
                if (Math.Abs(cross) > 1e-12)
                {
                    double p = Plan.Cross(b0 - a0, db) / cross, q = Plan.Cross(b0 - a0, da) / cross;
                    if (p >= 0.0 && p <= 1.0 && q >= 0.0 && q <= 1.0 && i + p < best.s) best = (i + p, j + q);
                }
                b0 = b1;
            }
            a0 = a1;
        }
        if (double.IsPositiveInfinity(best.s)) return null;

        // The edges are curves and what crossed were chords of them: Newton on the curves themselves from there.
        double s = best.s * a.StepS, v = best.u * b.StepS;
        for (int i = 0; i < 6; i++)
        {
            double ta = a.Out.TimeAt(s), tb = b.Out.TimeAt(v);
            Plan ha = a.Out.Heading(ta), hb = b.Out.Heading(tb);
            Plan off = (a.Out.At(ta) + (ha.Left() * offA)) - (b.Out.At(tb) + (hb.Left() * offB));
            if (off.Len < 1e-10) break;
            Plan ra = ha * (1.0 - (offA * a.Out.Curvature(ta))), rb = hb * (1.0 - (offB * b.Out.Curvature(tb)));
            double det = Plan.Cross(ra, rb);
            if (Math.Abs(det) < 1e-9) break;
            double ds = -Plan.Cross(off, rb) / det, dv = -Plan.Cross(off, ra) / det;
            if (Math.Abs(ds) > 2.0 * a.StepS || Math.Abs(dv) > 2.0 * b.StepS) break;
            (s, v) = (s + ds, v + dv);
        }
        s = Math.Max(s, 0.0);
        v = Math.Max(v, 0.0);
        return s <= a.CapS - MouthPastM && v <= b.CapS - MouthPastM ? (s, v) : null;
    }

    private static double Area(Plan[] polygon)
    {
        double twice = 0.0;
        for (int i = 0; i < polygon.Length; i++) twice += Plan.Cross(polygon[i], polygon[(i + 1) % polygon.Length]);
        return 0.5 * twice;
    }

    private static bool Crosses(Plan[] polygon)
    {
        int n = polygon.Length;
        for (int i = 0; i < n; i++)
        {
            Plan a = polygon[i], b = polygon[(i + 1) % n];
            for (int j = i + 2; j < n; j++)
            {
                if (i == 0 && j == n - 1) continue;
                Plan c = polygon[j], d = polygon[(j + 1) % n];
                double d1 = Plan.Cross(b - a, c - a), d2 = Plan.Cross(b - a, d - a), d3 = Plan.Cross(d - c, a - c), d4 = Plan.Cross(d - c, b - c);
                if (((d1 > 1e-9 && d2 < -1e-9) || (d1 < -1e-9 && d2 > 1e-9)) && ((d3 > 1e-9 && d4 < -1e-9) || (d3 < -1e-9 && d4 > 1e-9))) return true;
            }
        }
        return false;
    }

    // A fan about a place every stretch of the edge is seen from going the same way round: the point,
    // or where a stretch of edge is in line with the point, the middle of the mouths or of the whole
    // edge. Ears cut off one at a time, the best shaped first, where there is no such place.
    private static int[]? Triangles(Plan[] polygon, Plan at, IEnumerable<Plan> mouths, out Plan hub)
    {
        int n = polygon.Length;
        Plan mouth = default, all = default;
        int count = 0;
        foreach (Plan m in mouths) (mouth, count) = (mouth + m, count + 1);
        foreach (Plan p in polygon) all += p;

        List<int> faces = [];
        foreach (Plan tried in new[] { at, mouth * (1.0 / Math.Max(count, 1)), all * (1.0 / n) })
        {
            bool star = true;
            for (int i = 0; i < n && star; i++) star = 0.5 * Plan.Cross(polygon[i] - tried, polygon[(i + 1) % n] - tried) > 4.0 * RoadTessellation.LeastAreaM2;
            if (!star) continue;

            for (int i = 0; i < n; i++) faces.AddRange([n, i, (i + 1) % n]);
            hub = tried;
            return [.. faces];
        }

        hub = at;
        List<int> left = [.. Enumerable.Range(0, n)];
        while (left.Count > 3)
        {
            (int best, double shape) = (-1, 0.0);
            for (int k = 0; k < left.Count; k++)
            {
                int before = left[(k + left.Count - 1) % left.Count], here = left[k], after = left[(k + 1) % left.Count];
                Plan a = polygon[before], b = polygon[here], c = polygon[after];
                double twice = Plan.Cross(b - a, c - b), longest = Math.Max((b - a).Len, Math.Max((c - b).Len, (a - c).Len));
                if (twice <= 8.0 * RoadTessellation.LeastAreaM2 || twice / (longest * longest) <= shape) continue;

                bool empty = true;
                foreach (int other in left)
                {
                    if (other == before || other == here || other == after) continue;
                    Plan p = polygon[other];
                    if (Plan.Cross(b - a, p - a) >= -1e-12 && Plan.Cross(c - b, p - b) >= -1e-12 && Plan.Cross(a - c, p - c) >= -1e-12)
                    {
                        empty = false;
                        break;
                    }
                }
                if (empty) (best, shape) = (k, twice / (longest * longest));
            }
            if (best < 0) return null;
            faces.AddRange([left[(best + left.Count - 1) % left.Count], left[best], left[(best + 1) % left.Count]]);
            left.RemoveAt(best);
        }
        if (Plan.Cross(polygon[left[1]] - polygon[left[0]], polygon[left[2]] - polygon[left[1]]) <= 8.0 * RoadTessellation.LeastAreaM2) return null;
        faces.AddRange(left);
        return [.. faces];
    }

    // ---- the plane --------------------------------------------------------------------------------

    /// <summary>
    /// Tilts the plane: as the road that goes through climbs and leans, or with none, as nearly as
    /// one plane can be to every arm's own climb away from the point.
    /// </summary>
    /// <param name="terrainAt">The ground's height over the body's mean radius at a place on the chart.</param>
    public void Tilt(Func<Plan, double> terrainAt)
    {
        // Each row asks for a climb along a direction, with a weight; the plane is the least squares of
        // them. Where a road goes through, only it is asked, and a side road is brought to it.
        List<(Plan Along, double Climb, double Weight)> through = [], own = [];
        double here = terrainAt(At);
        for (int i = 0; i < Arms.Count; i++)
        {
            Arm arm = Arms[i];
            double weight = arm.HalfWidth * arm.HalfWidth;
            if (arm.Partner < 0)
            {
                double ground = (terrainAt(arm.MouthAt) - here) / Math.Max(arm.MouthS, MouthPastM);
                own.Add((arm.Out.Heading(0.0), ground + ((arm.FarHeightM - arm.HeightM) / arm.Out.LengthM), weight));
                continue;
            }
            if (arm.Partner < i) continue;

            Arm other = Arms[arm.Partner];
            Plan along = (arm.Out.Heading(0.0) - other.Out.Heading(0.0)).Unit();
            // The ground's climb from one mouth to the other is the line nearest it at five places
            // between them, so a bump under either mouth does not tilt the whole junction.
            double apart = Plan.Dot(arm.MouthAt - other.MouthAt, along), climb = 0.0, spread = 0.0;
            for (int k = 0; k <= 4 && apart > 1e-6; k++)
            {
                double off = apart * ((k / 4.0) - 0.5);
                climb += off * terrainAt(other.MouthAt + ((arm.MouthAt - other.MouthAt) * (k / 4.0)));
                spread += off * off;
            }
            climb = spread > 0.0 ? climb / spread : 0.0;
            climb += MonotoneCurve.Between(other.Out.LengthM, (arm.HeightM - other.FarHeightM) / other.Out.LengthM,
                                           arm.Out.LengthM, (arm.FarHeightM - arm.HeightM) / arm.Out.LengthM);
            through.Add((along, climb, weight));

            // Each arm's bank is as one leaving the junction sees it, so the partner's is the other way
            // up. It weighs little: at a crossroads the other road's climb is this one's lean.
            double? bank = arm.BankOutDeg is { } mine && other.BankOutDeg is { } theirs ? 0.5 * (mine - theirs) : arm.BankOutDeg ?? -other.BankOutDeg;
            through.Add((along.Left(), Math.Tan((bank ?? 0.0) * Math.PI / 180.0), 0.01 * weight));
        }

        double ee = 1e-9, en = 0.0, nn = 1e-9, ec = 0.0, nc = 0.0;
        foreach ((Plan along, double climb, double weight) in through.Count > 0 ? through : own)
        {
            ee += weight * along.E * along.E;
            en += weight * along.E * along.N;
            nn += weight * along.N * along.N;
            ec += weight * along.E * climb;
            nc += weight * along.N * climb;
        }
        double det = (ee * nn) - (en * en);
        Plan gradient = Math.Abs(det) > 1e-12 ? new Plan(((ec * nn) - (nc * en)) / det, ((nc * ee) - (ec * en)) / det) : default;
        if (through.Count == 0 && gradient.Len > MostGrade) gradient = gradient.Unit() * MostGrade;
        Gradient = gradient;
    }

    /// <summary>
    /// Sets the plane's height: the point's own above the ground there, and no lower than keeps every
    /// part of the asphalt above the ground by the lift and every arm above its own smoothed ground.
    /// </summary>
    /// <param name="armGroundM">For each arm, the smoothed ground under its run at its mouth.</param>
    public void Raise(Func<Plan, double> terrainAt, double liftM, IReadOnlyList<double> armGroundM)
    {
        double heightM = Arms.Count > 0 ? Arms[0].HeightM : 0.0;
        double least = terrainAt(At) + Math.Max(heightM, 0.0);
        double[] under = new double[Boundary.Length];
        for (int i = 0; i < Boundary.Length; i++)
        {
            Plan p = Boundary[i], half = At + ((p - At) * 0.5);
            under[i] = terrainAt(p);
            least = Math.Max(least, under[i] - Plan.Dot(Gradient, p - At));
            least = Math.Max(least, terrainAt(half) - Plan.Dot(Gradient, half - At));
        }
        for (int i = 0; i < Arms.Count; i++) least = Math.Max(least, armGroundM[i] - Plan.Dot(Gradient, Arms[i].MouthAt - At));
        HeightM = least + liftM;

        Deck = false;
        for (int i = 0; i < Boundary.Length; i++) Deck |= HeightAt(Boundary[i]) - under[i] > RoadRibbon.DeckOverM;
    }

    /// <summary>
    /// Works out <see cref="Spokes"/>: what is past the asphalt between the mouths. Asked once every
    /// arm's run is laid, since at a mouth's corner the way out and how far it goes are the run's own.
    ///
    /// <para>Along a smooth stretch a place has one way out. Where the edge turns a corner each stretch
    /// has its own there: turning outwards, the earth fans round from one to the other, as it does
    /// round the end of a road; turning inwards the two cross, and each is cut short where the other
    /// stretch is as near.</para>
    /// </summary>
    public void Edge(Func<Plan, double> terrainAt)
    {
        int n = Boundary.Length;
        List<Spoke> spokes = [];
        List<(int, int)> strips = [];
        _stretch = new (int, int)?[n];

        double Toe(Plan p, Plan o) => Deck ? 0.0 : RoadRibbon.Toe(HeightAt(p), terrainAt(p), out_ => terrainAt(p + (o * out_)));

        double Corner(Arm arm, bool left)
        {
            if (Deck) return 0.0;
            if (arm.Ribbon is not { } ribbon) return Toe(Boundary[left ? arm.Mouth.Left : arm.Mouth.Right], Outward[left ? arm.Mouth.Left : arm.Mouth.Right]);
            RoadRibbon.Section end = ribbon.At(arm.AtStart ? 0 : ribbon.Line.Count - 1, arm.AtStart ? 0.0 : ribbon.LengthM);
            return left == arm.AtStart ? end.ToeLeft : end.ToeRight;
        }

        // From the last spoke at a place to one that goes out another way from it.
        int Turn(int last, int at, Plan wanted, double? corner)
        {
            Plan had = spokes[last].Outward;
            double angle = Math.Atan2(Plan.Cross(had, wanted), Plan.Dot(had, wanted));
            if (Math.Abs(angle) < 1e-6)
            {
                if (corner is { } own) spokes[last] = spokes[last] with { ToeM = own, Corner = true };
                return last;
            }

            int steps = angle > 0.0 && !Deck ? Math.Max(1, (int)Math.Ceiling(angle / FanRad)) : 1;
            for (int k = 1; k <= steps; k++)
            {
                Plan o = k == steps ? wanted : Turned(had, angle * k / steps);
                spokes.Add(new Spoke(at, o, k == steps && corner is { } own ? own : Toe(Boundary[at], o), k == steps && corner is not null));
                if (angle > 0.0 && !Deck) strips.Add((last, spokes.Count - 1));
                last = spokes.Count - 1;
            }
            return last;
        }

        for (int c = 0; c < Chains.Length; c++)
        {
            Chain chain = Chains[c];
            spokes.Add(new Spoke(chain.From, Outward[chain.From], Corner(Arms[c], true), true));
            int last = spokes.Count - 1;
            for (int i = chain.From; i != chain.To; i = (i + 1) % n)
            {
                int j = (i + 1) % n;
                Plan square = -(Boundary[j] - Boundary[i]).Left().Unit();
                Plan Own(Plan o) => Plan.Dot(o, square) >= Math.Cos(CornerRad) ? o : square;

                last = Turn(last, i, Own(Outward[i]), null);
                Plan to = Own(Outward[j]);
                spokes.Add(new Spoke(j, to, Toe(Boundary[j], to), false));
                strips.Add((last, spokes.Count - 1));
                _stretch[i] = (last, spokes.Count - 1);
                last = spokes.Count - 1;
            }
            Turn(last, chain.To, Outward[chain.To], Corner(Arms[(c + 1) % Arms.Count], false));
        }

        (Spokes, Strips) = ([.. spokes], [.. strips]);
        ReachM = Boundary.Max(p => (p - At).Len) + (Spokes.Length > 0 ? Spokes.Max(s => s.ToeM) : 0.0);
        _out = new Lazy<double[]>(Room);
        _most = new Lazy<double[]>(() =>
        {
            double[] most = new double[n];
            for (int k = 0; k < Spokes.Length; k++) most[Spokes[k].At] = Math.Max(most[Spokes[k].At], _out.Value[k]);
            return most;
        });
    }

    // No further out along a spoke than where another stretch of the edge is as near: the banks of two
    // edges that face one another meet there. A mouth's corner keeps what its run has.
    private double[] Room()
    {
        double[] room = [.. Spokes.Select(s => s.ToeM)];
        for (int i = 0; i < room.Length; i++)
        {
            if (!(room[i] > 0.0) || Spokes[i].Corner) continue;
            Plan p = Boundary[Spokes[i].At], o = Spokes[i].Outward;
            bool Clear(double out_) => Nearest(p + (o * out_), out _, out _, out _) >= (out_ * 0.99) - 1e-4;
            if (Clear(room[i])) continue;

            double clear = 0.0, blocked = room[i];
            for (int k = 0; k < 20; k++)
            {
                double middle = 0.5 * (clear + blocked);
                if (Clear(middle)) clear = middle;
                else blocked = middle;
            }
            room[i] = clear;
        }
        return room;
    }

    /// <summary>How far out the earth goes along one of <see cref="Spokes"/>.</summary>
    public double OutOf(int spoke) => _out.Value[spoke];

    /// <summary>The furthest out the earth goes from a place of the edge.</summary>
    public double OutAt(int boundary) => _most.Value.Length > boundary ? _most.Value[boundary] : 0.0;

    /// <summary>The plane's height over the body's mean radius at a place on the chart.</summary>
    public double HeightAt(Plan place) => HeightM + Plan.Dot(Gradient, place - At);

    /// <summary>A place on the plane, from the body's centre, <paramref name="belowM"/> under it.</summary>
    public double3 Point(Plan place, double belowM = 0.0) => Chart.Dir(place) * (Chart.RadiusM + HeightAt(place) - belowM);

    /// <summary>The way a surface over a place on the chart faces that climbs by <paramref name="gradient"/> each metre east and north.</summary>
    public double3 Facing(Plan place, Plan gradient)
    {
        Chart.Compass(place, out double3 up, out double3 east, out double3 north);
        double metres = (Chart.RadiusM + HeightAt(place)) / (Chart.RadiusM * Chart.Scale(place));
        return Vec.Unit((up * metres) - (east * gradient.E) - (north * gradient.N));
    }

    /// <summary>The way the asphalt faces at a place on it.</summary>
    public double3 Normal(Plan place) => Facing(place, Gradient);

    // ---- what a wheel is over ---------------------------------------------------------------------

    /// <summary>Whether a place on the chart is on the junction's asphalt.</summary>
    public bool Contains(Plan place)
    {
        bool inside = false;
        for (int i = 0, j = Boundary.Length - 1; i < Boundary.Length; j = i++)
        {
            Plan a = Boundary[i], b = Boundary[j];
            if ((a.N > place.N) != (b.N > place.N) && place.E < a.E + ((b.E - a.E) * (place.N - a.N) / (b.N - a.N))) inside = !inside;
        }
        return inside;
    }

    /// <summary>
    /// The junction's surface at a place on the chart: its height over the body's mean radius and how
    /// far out past the asphalt that is, nothing on it. False where the junction has no surface there.
    /// </summary>
    /// <param name="edgeM">How far out past the asphalt's edge still counts as on it.</param>
    public bool Surface(Plan place, double edgeM, out double height, out double outM)
    {
        (height, outM) = (HeightAt(place), 0.0);
        if ((place - At).Len > ReachM + edgeM) return false;
        if (Contains(place)) return true;

        // On the edge itself is on the asphalt, a mouth's line too: a run answers up to its mouth and
        // not a hair past it, and a wheel on the line is not to fall between the two.
        if (Nearest(place, out _, out _, out _) <= edgeM) return true;
        double off = Nearest(place, out Plan on, out double toe, out bool past, chains: true);
        if (Deck || past || off > toe) return false;
        (height, outM) = (HeightAt(on) - RoadRibbon.Drop(off), off);
        return true;
    }

    // How far a place is from the edge, the place of the edge nearest it and how far out the earth
    // goes from there. With `chains`, only the edge between mouths, and `past` says the nearest of it
    // is a mouth's corner with the place on along the road from it, where it is the road's own.
    private double Nearest(Plan place, out Plan on, out double toe, out bool past, bool chains = false)
    {
        double least = double.PositiveInfinity;
        (on, toe, past) = (default, 0.0, false);
        int n = Boundary.Length;
        for (int c = 0; c < Chains.Length; c++)
        {
            int from = chains ? Chains[c].From : 0, count = chains ? ((Chains[c].To - from + n) % n) : n;
            for (int k = 0; k < count; k++)
            {
                int i = (from + k) % n, j = (i + 1) % n;
                Plan a = Boundary[i], run = Boundary[j] - a;
                double length2 = Plan.Dot(run, run);
                double t = length2 > 0.0 ? Math.Clamp(Plan.Dot(place - a, run) / length2, 0.0, 1.0) : 0.0;
                Plan foot = a + (run * t);
                double off = (place - foot).Len;
                if (!(off < least)) continue;
                least = off;
                on = foot;
                if (!chains) continue;
                toe = t <= 0.0 || _stretch[i] is not { } ends ? OutAt(i) : t >= 1.0 ? OutAt(j) : OutOf(ends.From) + ((OutOf(ends.To) - OutOf(ends.From)) * t);
                past = (k == 0 && t <= 0.0 && Plan.Dot(place - a, run) < -1e-9) || (k == count - 1 && t >= 1.0 && Plan.Dot(place - Boundary[j], run) > 1e-9);
            }
            if (!chains) break;
        }
        return least;
    }

    // ---- a line to drive ---------------------------------------------------------------------------

    /// <summary>
    /// A line across the junction from one arm's mouth to another's, leaving each along its road: places
    /// on the plane from the body's centre no more than <paramref name="spacingM"/> apart, the first at
    /// the mouth come in by and the last at the one left by.
    /// </summary>
    public List<double3> Across(Arm from, Arm to, double spacingM)
    {
        // The curve reaches in from each mouth no further than keeps it clear of the corners: a short
        // reach is a line straight across, which between two arms side by side is over the corner between them.
        Plan a = from.MouthAt, d = to.MouthAt;
        double chord = (d - a).Len, wanted = Math.Min(AcrossClearM, 0.5 * Math.Min(from.HalfWidth, to.HalfWidth)), most = double.NegativeInfinity;
        RoadArc curve = RoadArc.Straight(a, d);
        foreach (double share in AcrossReach)
        {
            double reach = share * Math.Max(chord, from.HalfWidth + to.HalfWidth);
            RoadArc tried = new(a, a - (from.MouthHeading * reach), d - (to.MouthHeading * reach), d);
            double clear = double.PositiveInfinity;
            for (int k = 1; k < 16; k++)
            {
                Plan on = tried.At(k / 16.0);
                double off = Nearest(on, out _, out _, out _, chains: true);
                clear = Math.Min(clear, Contains(on) ? off : -off);
            }
            if (clear > most) (curve, most) = (tried, clear);
            if (clear >= wanted) break;
        }

        int steps = Math.Max(2, (int)Math.Ceiling(curve.LengthM / Math.Max(spacingM, 0.01)));
        List<double3> line = [];
        for (int i = 0; i <= steps; i++) line.Add(Point(curve.At(curve.TimeAt(curve.LengthM * i / steps))));
        return line;
    }

    // How far from the edge between mouths a line across is to keep, where the junction has the room;
    // and how far in from each mouth its curve may reach, as shares of the distance between them.
    private const double AcrossClearM = 2.5;
    private static readonly double[] AcrossReach = [0.4, 0.6, 0.8, 1.1, 1.5, 2.0, 2.7];

    /// <summary>The arm that is the road to <paramref name="far"/>, or none.</summary>
    public Arm? ArmTo(int far) => Arms.FirstOrDefault(a => a.Far == far);
}
