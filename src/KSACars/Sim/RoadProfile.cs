namespace KSACars;

/// <summary>
/// A curve through points that never goes where they do not: between two of them it stays between
/// their values, through points in a line it is that line, and its slope has no step anywhere.
///
/// <para>A cubic between each two points, with the slope at each chosen as Fritsch and Carlson's
/// method does: nothing where the points either side turn back, and otherwise a mean of the two
/// slopes that leans to the lesser. A spline that minimises bending overshoots, and an overshoot in
/// a road's height is a hump nobody asked for.</para>
/// </summary>
internal sealed class MonotoneCurve
{
    private readonly double[] _x, _y, _slope;
    private readonly double _period;
    private readonly bool _straight;

    /// <summary>Straight lines from each point to the next, for heights that are given and not to be made anything of.</summary>
    public static MonotoneCurve Straight(IReadOnlyList<double> x, IReadOnlyList<double> y, double period = 0.0) => new(x, y, period, true);

    /// <param name="period">More than nothing for a curve that comes round to its first point again that far on.</param>
    /// <param name="startSlope">The slope an open curve is to have at its first point, where that is given and not the curve's to choose; and so at its last.</param>
    public MonotoneCurve(IReadOnlyList<double> x, IReadOnlyList<double> y, double period = 0.0, double? startSlope = null, double? endSlope = null)
        : this(x, y, period, false, startSlope, endSlope)
    {
    }

    private MonotoneCurve(IReadOnlyList<double> x, IReadOnlyList<double> y, double period, bool straight,
                          double? startSlope = null, double? endSlope = null)
    {
        _straight = straight;
        List<double> xs = [], ys = [];
        for (int i = 0; i < x.Count; i++)
        {
            if (xs.Count > 0 && !(x[i] > xs[^1])) continue;
            xs.Add(x[i]);
            ys.Add(y[i]);
        }
        if (xs.Count == 0)
        {
            xs.Add(0.0);
            ys.Add(0.0);
        }

        bool round = period > 0.0 && xs.Count > 1 && xs[^1] < xs[0] + period;
        if (round)
        {
            xs.Add(xs[0] + period);
            ys.Add(ys[0]);
        }
        _period = round ? period : 0.0;
        (_x, _y) = ([.. xs], [.. ys]);
        _slope = Slopes(_x, _y, round);
        if (!round && _x.Length > 1)
        {
            _slope[0] = startSlope ?? _slope[0];
            _slope[^1] = endSlope ?? _slope[^1];
        }
    }

    public static MonotoneCurve Level(double y) => new([0.0], [y]);

    private static double[] Slopes(double[] x, double[] y, bool round)
    {
        int n = x.Length;
        double[] slope = new double[n];
        if (n < 2) return slope;

        double[] h = new double[n - 1], rise = new double[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            h[i] = x[i + 1] - x[i];
            rise[i] = (y[i + 1] - y[i]) / h[i];
        }
        if (n == 2)
        {
            slope[0] = slope[1] = round ? 0.0 : rise[0];
            return slope;
        }

        for (int i = 1; i < n - 1; i++) slope[i] = Between(h[i - 1], rise[i - 1], h[i], rise[i]);
        if (round)
        {
            slope[0] = slope[n - 1] = Between(h[n - 2], rise[n - 2], h[0], rise[0]);
        }
        else
        {
            slope[0] = AtEnd(h[0], rise[0], h[1], rise[1]);
            slope[n - 1] = AtEnd(h[n - 2], rise[n - 2], h[n - 3], rise[n - 3]);
        }
        return slope;
    }

    internal static double Between(double hBefore, double before, double hAfter, double after)
    {
        if (!(before * after > 0.0)) return 0.0;
        double w1 = (2.0 * hAfter) + hBefore, w2 = hAfter + (2.0 * hBefore);
        return (w1 + w2) / ((w1 / before) + (w2 / after));
    }

    // The slope a parabola through the end and the two points after it has there, held to what keeps
    // the curve from turning back before the next point.
    private static double AtEnd(double h, double rise, double hNext, double riseNext)
    {
        double slope = ((((2.0 * h) + hNext) * rise) - (h * riseNext)) / (h + hNext);
        if (!(slope * rise > 0.0)) return 0.0;
        return !(rise * riseNext > 0.0) && Math.Abs(slope) > 3.0 * Math.Abs(rise) ? 3.0 * rise : slope;
    }

    public double At(double x)
    {
        At(x, out double y, out _, out _);
        return y;
    }

    /// <summary>The curve at <paramref name="x"/>, its slope there and how fast that slope is changing. Past an open end, as at the end.</summary>
    public void At(double x, out double y, out double slope, out double bend)
    {
        int last = _x.Length - 1;
        if (last == 0)
        {
            (y, slope, bend) = (_y[0], 0.0, 0.0);
            return;
        }

        if (_period > 0.0)
        {
            x = _x[0] + ((x - _x[0]) % _period);
            if (x < _x[0]) x += _period;
        }
        x = Math.Clamp(x, _x[0], _x[last]);

        // Evenly spaced points are found at once; any others by halving.
        int i = Math.Clamp((int)((x - _x[0]) / (_x[last] - _x[0]) * last), 0, last - 1);
        if (x < _x[i] || x > _x[i + 1])
        {
            i = Array.BinarySearch(_x, x);
            if (i < 0) i = ~i - 1;
            i = Math.Clamp(i, 0, last - 1);
        }

        double h = _x[i + 1] - _x[i], t = (x - _x[i]) / h;
        if (_straight)
        {
            (y, slope, bend) = (_y[i] + ((_y[i + 1] - _y[i]) * t), (_y[i + 1] - _y[i]) / h, 0.0);
            return;
        }
        double y0 = _y[i], y1 = _y[i + 1], m0 = _slope[i] * h, m1 = _slope[i + 1] * h;
        double t2 = t * t, t3 = t2 * t;
        y = (((2.0 * t3) - (3.0 * t2) + 1.0) * y0) + ((t3 - (2.0 * t2) + t) * m0) + (((3.0 * t2) - (2.0 * t3)) * y1) + ((t3 - t2) * m1);
        slope = ((((6.0 * t2) - (6.0 * t)) * (y0 - y1)) + (((3.0 * t2) - (4.0 * t) + 1.0) * m0) + (((3.0 * t2) - (2.0 * t)) * m1)) / h;
        bend = ((((12.0 * t) - 6.0) * (y0 - y1)) + (((6.0 * t) - 4.0) * m0) + (((6.0 * t) - 2.0) * m1)) / (h * h);
    }
}

/// <summary>
/// What a road is along its length, apart from where it goes: how high, how far it leans and how wide,
/// each a distance along the run of roads it belongs to.
///
/// <para>The height is the ground under the road, smoothed, the lift every road stands proud by, and a
/// curve through the heights of the run's points that climbs steadily where they do. The lean is a
/// curve of the same kind through the banks set at the points; the width eases from one point's to
/// the next's.</para>
///
/// <para>An end at a junction is the junction's to say: the run's first or last point is then its
/// mouth, where its height, its climb, its lean and how fast that changes are the junction's plane's.</para>
/// </summary>
internal sealed class RoadProfile
{
    /// <summary>What an end of a run is held to at a junction's mouth: its height over the body's mean radius, and its climb for each metre along the run.</summary>
    public readonly record struct Pin(double HeightM, double Slope);

    private readonly double[] _knot, _halfWidth, _heightM;
    private readonly MonotoneCurve _above, _bank;
    private readonly RoadGround _ground;
    private readonly double _liftM;

    public double LengthM => _knot[^1];
    public bool Closed { get; }

    /// <summary>How many roads the run is of: one fewer than its points, or as many on a closed one.</summary>
    public int Roads => _knot.Length - 1;

    /// <param name="knotS">How far along the run each of its points is, the first at nothing and the last at its length; on a closed run the last is the first again.</param>
    /// <param name="heightM">Each point's height above the ground. One below it is laid on it.</param>
    /// <param name="bankDeg">The lean at each point, raising the left edge of one travelling along the run.</param>
    /// <param name="widthM">The road's width at each point.</param>
    /// <param name="bankRateStart">How fast the tangent of the lean changes at an open run's first point (1/m), where that is given; and so at its last.</param>
    public RoadProfile(double[] knotS, double[] heightM, double[] bankDeg, double[] widthM, bool closed,
                       double liftM, RoadGround? ground = null, double? bankRateStart = null, double? bankRateEnd = null)
    {
        _knot = knotS;
        Closed = closed;
        _halfWidth = [.. widthM.Select(w => 0.5 * w)];
        _liftM = liftM;
        _ground = ground ?? RoadGround.Level(0.0);

        int points = closed ? knotS.Length - 1 : knotS.Length;
        double period = closed ? knotS[^1] : 0.0;
        _heightM = [.. heightM[..points].Select(h => Math.Max(h, 0.0))];
        double[] bank = [.. bankDeg[..points].Select(b => b * Math.PI / 180.0)];
        _above = new MonotoneCurve(knotS[..points], _heightM, period);
        _bank = new MonotoneCurve(knotS[..points], bank, period, Turning(bankRateStart, bank[0]), Turning(bankRateEnd, bank[^1]));
    }

    // How fast the lean itself turns where its tangent changes at a rate.
    private static double? Turning(double? tanRate, double angle) =>
        tanRate is { } rate ? rate * Math.Cos(angle) * Math.Cos(angle) : null;

    private RoadProfile(RoadProfile from, RoadGround ground, Pin? start, Pin? end)
    {
        (_knot, _halfWidth, _heightM, _bank, Closed) = (from._knot, from._halfWidth, from._heightM, from._bank, from.Closed);
        (_liftM, _ground, _above) = (from._liftM, ground, from._above);
        if (Closed || (start is null && end is null)) return;

        double[] above = [.. _heightM];
        double? startSlope = null, endSlope = null;
        if (start is { } first)
        {
            ground.At(0.0, out double under, out double slope, out _);
            (above[0], startSlope) = (first.HeightM - under - _liftM, first.Slope - slope);
        }
        if (end is { } last)
        {
            ground.At(LengthM, out double under, out double slope, out _);
            (above[^1], endSlope) = (last.HeightM - under - _liftM, last.Slope - slope);
        }
        _above = new MonotoneCurve(_knot, above, 0.0, startSlope, endSlope);
    }

    /// <summary>
    /// The same road over <paramref name="ground"/>, the smoothed ground's height a distance along,
    /// with either end held to a junction's mouth.
    /// </summary>
    public RoadProfile Over(RoadGround ground, Pin? start = null, Pin? end = null) => new(this, ground, start, end);

    /// <summary>How far along the run its point <paramref name="index"/> is.</summary>
    public double KnotS(int index) => _knot[index];

    /// <summary>The road's height over the body's mean radius (m), its slope and how fast the slope changes (1/m).</summary>
    public void Height(double s, out double height, out double slope, out double bend)
    {
        _ground.At(s, out double ground, out double groundSlope, out double groundBend);
        Above(s, out double above, out double aboveSlope, out double aboveBend);
        (height, slope, bend) = (ground + above, groundSlope + aboveSlope, groundBend + aboveBend);
    }

    /// <summary>How far the road is above the smoothed ground under it: the lift and the curve through the points' heights.</summary>
    public void Above(double s, out double above, out double slope, out double bend)
    {
        _above.At(s, out above, out slope, out bend);
        above += _liftM;
    }

    /// <summary>The tangent of the lean at <paramref name="s"/>, the left edge up positive, and how fast it changes (1/m).</summary>
    public void Bank(double s, out double tan, out double rate)
    {
        _bank.At(s, out double angle, out double turning, out _);
        tan = Math.Tan(angle);
        rate = turning * (1.0 + (tan * tan));
    }

    /// <summary>Half the road's width at <paramref name="s"/> and how fast it changes.</summary>
    public void HalfWidth(double s, out double half, out double rate)
    {
        int i = Road(s);
        double span = _knot[i + 1] - _knot[i];
        double t = span > 0.0 ? Math.Clamp((s - _knot[i]) / span, 0.0, 1.0) : 0.0;
        double change = _halfWidth[i + 1] - _halfWidth[i];
        half = _halfWidth[i] + (change * t * t * (3.0 - (2.0 * t)));
        rate = span > 0.0 ? change * 6.0 * t * (1.0 - t) / span : 0.0;
    }

    /// <summary>Which of the run's roads <paramref name="s"/> is on, counted from its start.</summary>
    public int Road(double s)
    {
        int i = Array.BinarySearch(_knot, s);
        if (i < 0) i = ~i - 1;
        return Math.Clamp(i, 0, _knot.Length - 2);
    }

    /// <summary>
    /// The steepest one of the run's roads gets, as climb for each metre along, and the tightest its
    /// climb bends, as a radius (m): what a car is thrown off the top of, or pressed into the foot of.
    /// </summary>
    public (double SteepestGrade, double LeastVerticalRadiusM) Steepness(int road, double stepM = 1.0)
    {
        double from = _knot[road], to = _knot[road + 1];
        int steps = Math.Max(1, (int)Math.Ceiling((to - from) / stepM));
        double steepest = 0.0, mostBend = 0.0;
        for (int i = 0; i <= steps; i++)
        {
            // Just inside each end: the bend steps at a point, and each road answers for its own side of it.
            double s = Math.Clamp(from + ((to - from) * i / steps), from + 1e-9, to - 1e-9);
            Height(s, out _, out double slope, out double bend);
            steepest = Math.Max(steepest, Math.Abs(slope));
            mostBend = Math.Max(mostBend, Math.Abs(bend) / Math.Pow(1.0 + (slope * slope), 1.5));
        }
        return (steepest, mostBend > 0.0 ? 1.0 / mostBend : double.PositiveInfinity);
    }
}
