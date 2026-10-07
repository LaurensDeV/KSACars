namespace KSACars;

/// <summary>
/// One cubic curve on a chart, measured by its own length: where it is, which way it runs and how
/// hard it turns a given distance along, whatever the speed its parameter goes at.
/// </summary>
internal sealed class RoadArc
{
    // The curve is cut into this many pieces of its parameter and each measured by a five-point
    // Gauss-Legendre rule, which is exact for a straight one and within a part in a thousand million
    // of a road's.
    private const int Pieces = 16;

    private static readonly double[] Node = [-0.9061798459386640, -0.5384693101056831, 0.0, 0.5384693101056831, 0.9061798459386640];
    private static readonly double[] Weight = [0.2369268850561891, 0.4786286704993665, 0.5688888888888889, 0.4786286704993665, 0.2369268850561891];

    private readonly Plan _a, _b, _c, _d;
    private readonly double[] _lengthTo = new double[Pieces + 1];
    private readonly Plan _chord;

    public double LengthM => _lengthTo[Pieces];

    public RoadArc(Plan a, Plan b, Plan c, Plan d)
    {
        (_a, _b, _c, _d) = (a, b, c, d);
        _chord = (d - a).Unit();
        for (int k = 0; k < Pieces; k++) _lengthTo[k + 1] = _lengthTo[k] + Measure((double)k / Pieces, (double)(k + 1) / Pieces);
    }

    /// <summary>A straight line as a curve, its parameter going at a steady speed.</summary>
    public static RoadArc Straight(Plan a, Plan d) => new(a, a + ((d - a) * (1.0 / 3.0)), a + ((d - a) * (2.0 / 3.0)), d);

    /// <summary>Where the curve is at <paramref name="t"/> of its parameter, from 0 to 1.</summary>
    public Plan At(double t)
    {
        double u = 1.0 - t;
        return (_a * (u * u * u)) + (_b * (3.0 * u * u * t)) + (_c * (3.0 * u * t * t)) + (_d * (t * t * t));
    }

    private Plan Rate(double t)
    {
        double u = 1.0 - t;
        return ((_b - _a) * (3.0 * u * u)) + ((_c - _b) * (6.0 * u * t)) + ((_d - _c) * (3.0 * t * t));
    }

    private Plan Bend(double t) => ((_c - (_b * 2.0) + _a) * (6.0 * (1.0 - t))) + ((_d - (_c * 2.0) + _b) * (6.0 * t));

    private double Measure(double from, double to)
    {
        double half = 0.5 * (to - from), middle = 0.5 * (to + from), sum = 0.0;
        for (int i = 0; i < Node.Length; i++) sum += Weight[i] * Rate(middle + (half * Node[i])).Len;
        return sum * half;
    }

    /// <summary>How far along the curve its parameter <paramref name="t"/> is (m).</summary>
    public double LengthAt(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        int k = Math.Min((int)(t * Pieces), Pieces - 1);
        return _lengthTo[k] + Measure((double)k / Pieces, t);
    }

    /// <summary>The parameter <paramref name="s"/> metres along the curve.</summary>
    public double TimeAt(double s)
    {
        if (s <= 0.0) return 0.0;
        if (s >= LengthM) return 1.0;

        int k = Array.BinarySearch(_lengthTo, s);
        if (k < 0) k = ~k - 1;
        k = Math.Clamp(k, 0, Pieces - 1);
        double low = (double)k / Pieces, high = (double)(k + 1) / Pieces;
        double span = _lengthTo[k + 1] - _lengthTo[k];
        double t = span > 0.0 ? low + ((s - _lengthTo[k]) / span / Pieces) : low;

        // Newton, kept between the two ends it is known to lie between: where a handle has no length
        // the parameter stands still at that end, and a plain Newton step from there goes anywhere.
        double from = (double)k / Pieces;
        for (int i = 0; i < 40; i++)
        {
            double off = _lengthTo[k] + Measure(from, t) - s;
            if (Math.Abs(off) < 1e-12 * Math.Max(1.0, LengthM)) break;
            if (off > 0.0) high = t;
            else low = t;
            double speed = Rate(t).Len;
            double next = speed > 0.0 ? t - (off / speed) : double.NaN;
            t = next > low && next < high ? next : 0.5 * (low + high);
        }
        return t;
    }

    /// <summary>The way the curve runs at <paramref name="t"/>, as a unit vector.</summary>
    public Plan Heading(double t)
    {
        Plan rate = Rate(t);
        return rate.Len > 1e-9 * Math.Max(1.0, LengthM) ? rate.Unit() : _chord;
    }

    /// <summary>How hard the curve turns at <paramref name="t"/>, 1/m, a turn to the left positive.</summary>
    public double Curvature(double t)
    {
        Plan rate = Rate(t), bend = Bend(t);
        double speed = rate.Len, cross = Plan.Cross(rate, bend);

        // Where the parameter stands still, and where the three are in line, nothing is left of the
        // cross product but its rounding, over a speed cubed that is next to nothing.
        if (!(speed > 1e-6 * Math.Max(1.0, LengthM)) || Math.Abs(cross) < 1e-12 * speed * bend.Len) return 0.0;
        return cross / (speed * speed * speed);
    }
}

/// <summary>
/// A road's centre line on a chart: cubic curves end to end, as one line measured by its own length.
/// Two curves may meet at a kink, and the line then has two headings there.
/// </summary>
internal sealed class RoadLine
{
    /// <param name="Heading">Along the line, a unit vector.</param>
    /// <param name="Curvature">1/m, a turn to the left positive.</param>
    public readonly record struct Point(Plan At, Plan Heading, double Curvature);

    /// <summary>
    /// A stretch of the line that turns too tightly for the road on it. One of no length is a kink,
    /// whose radius is nothing.
    /// </summary>
    public readonly record struct Tight(double FromS, double ToS, double LeastRadiusM, double NeededM);

    /// <summary>A turn of more than this where two curves meet is a kink and not rounding.</summary>
    public const double KinkRad = 1e-3;

    private readonly RoadArc[] _arcs;
    private readonly double[] _start;

    public int Count => _arcs.Length;
    public double LengthM { get; }
    public bool Closed { get; }

    public RoadLine(IEnumerable<RoadArc> arcs, bool closed)
    {
        _arcs = [.. arcs];
        _start = new double[_arcs.Length + 1];
        for (int i = 0; i < _arcs.Length; i++) _start[i + 1] = _start[i] + _arcs[i].LengthM;
        LengthM = _start[^1];
        Closed = closed;
    }

    /// <summary>Straight lines through <paramref name="points"/>, back to the first if <paramref name="closed"/>.</summary>
    public static RoadLine Through(IReadOnlyList<Plan> points, bool closed)
    {
        List<RoadArc> arcs = [];
        int count = closed ? points.Count : points.Count - 1;
        for (int i = 0; i < count; i++) arcs.Add(RoadArc.Straight(points[i], points[(i + 1) % points.Count]));
        return new RoadLine(arcs, closed);
    }

    /// <summary>How far along the line curve <paramref name="arc"/> starts; with the number of curves, the line's length.</summary>
    public double StartOf(int arc) => _start[arc];

    /// <summary>A distance along brought onto the line: round again on a closed one, held to the ends of an open one.</summary>
    public double Wrap(double s)
    {
        if (!Closed || !(LengthM > 0.0)) return Math.Clamp(s, 0.0, LengthM);
        s %= LengthM;
        return s < 0.0 ? s + LengthM : s;
    }

    /// <summary>The curve <paramref name="s"/> metres along is on. Where two meet, the one after.</summary>
    public int ArcAt(double s)
    {
        int i = Array.BinarySearch(_start, s);
        if (i < 0) i = ~i - 1;
        return Math.Clamp(i, 0, _arcs.Length - 1);
    }

    /// <summary>The line <paramref name="s"/> metres along it. At a kink, as it leaves.</summary>
    public Point At(double s)
    {
        s = Wrap(s);
        return At(ArcAt(s), s);
    }

    /// <summary>The same on a curve that is named, which at a kink says which side of it is meant.</summary>
    public Point At(int arc, double s)
    {
        RoadArc a = _arcs[arc];
        double t = a.TimeAt(s - _start[arc]);
        return new Point(a.At(t), a.Heading(t), a.Curvature(t));
    }

    /// <summary>How far the line turns where curve <paramref name="arc"/> starts, radians, to the left positive; nothing at an open end.</summary>
    public double TurnAt(int arc)
    {
        if (_arcs.Length == 0 || (!Closed && (arc <= 0 || arc >= _arcs.Length))) return 0.0;
        Plan before = _arcs[(arc + _arcs.Length - 1) % _arcs.Length].Heading(1.0), after = _arcs[arc % _arcs.Length].Heading(0.0);
        return Math.Atan2(Plan.Cross(before, after), Plan.Dot(before, after));
    }

    /// <summary>
    /// Where the line's radius is less than <paramref name="neededAt"/> says a road there needs,
    /// looked for every <paramref name="stepM"/>. Inside that radius the inner edge of the road folds
    /// over itself.
    /// </summary>
    public List<Tight> TooTight(Func<double, double> neededAt, double stepM = 0.5)
    {
        List<Tight> found = [];
        for (int arc = 0; arc < _arcs.Length; arc++)
        {
            if (Math.Abs(TurnAt(arc)) > KinkRad) found.Add(new Tight(_start[arc], _start[arc], 0.0, neededAt(_start[arc])));

            double length = _arcs[arc].LengthM;
            int steps = Math.Max(1, (int)Math.Ceiling(length / Math.Max(stepM, 0.01)));
            Tight? open = null;
            for (int i = 0; i <= steps; i++)
            {
                double s = _start[arc] + (length * i / steps);
                double bend = Math.Abs(At(arc, s).Curvature), needed = neededAt(s);
                if (bend * needed > 1.0)
                {
                    double radius = 1.0 / bend;
                    open = open is { } o
                        ? o with { ToS = s, LeastRadiusM = Math.Min(o.LeastRadiusM, radius), NeededM = Math.Max(o.NeededM, needed) }
                        : new Tight(s, s, radius, needed);
                    continue;
                }
                if (open is { } done) found.Add(done);
                open = null;
            }
            if (open is { } last) found.Add(last);
        }
        return found;
    }
}
