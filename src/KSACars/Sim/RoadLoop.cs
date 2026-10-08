using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// A loop a car drives up, over upside down and down out of: road that a height over the ground
/// cannot say, so a surface of its own, answered to a wheel along its own facing.
///
/// <para>It stands in one upright plane, along the way it is entered. Its bend is nothing where it
/// leaves the ground, the most over the top and nothing again where it comes back, a tear and not a
/// ring: a ring's bend is all there at its foot, and a car is pressed into its springs by the whole
/// of it in one step. As it goes round it is carried to the left by a little more than its width, so
/// the way out clears the way in.</para>
/// </summary>
internal sealed class RoadLoop
{
    private const double StepM = 0.25;

    /// <summary>How far under the asphalt and how far over it a wheel's hub is still on the loop.</summary>
    public const double UnderM = 0.6, OverM = 1.5;

    private readonly double3 _base, _ahead, _up, _left;
    private readonly double[] _x, _z, _pitch;

    /// <param name="baseAt">Where it leaves the ground, on the road's middle, from the body's centre.</param>
    /// <param name="ahead">The way it is entered, level.</param>
    /// <param name="lengthM">How long the road of it is, round from the ground to the ground.</param>
    /// <param name="shiftM">How far to the left the way out is of the way in.</param>
    public RoadLoop(double3 baseAt, double3 ahead, double lengthM, double shiftM, double halfWidthM)
    {
        _base = baseAt;
        _up = Vec.Unit(baseAt);
        _ahead = Vec.Unit(Vec.RejectFrom(ahead, _up));
        _left = Vec.Cross(_up, _ahead);
        (LengthM, ShiftM, HalfWidthM) = (lengthM, shiftM, halfWidthM);

        int count = (int)Math.Ceiling(lengthM / StepM) + 1;
        (_x, _z, _pitch) = (new double[count], new double[count], new double[count]);
        double step = lengthM / (count - 1);
        for (int i = 1; i < count; i++)
        {
            // The bend is a sine squared along it, which is this for how far it has turned; by the middle of each step.
            double mid = (i - 0.5) * step, turned = Turned(mid);
            _pitch[i] = Turned(i * step);
            _x[i] = _x[i - 1] + (Math.Cos(turned) * step);
            _z[i] = _z[i - 1] + (Math.Sin(turned) * step);
        }
    }

    public double LengthM { get; }
    public double ShiftM { get; }
    public double HalfWidthM { get; }

    /// <summary>How high its top is over where it leaves the ground.</summary>
    public double HeightM => _z.Max();

    /// <summary>How hard it bends over its top, 1/m: twice what a ring of its length would all the way round.</summary>
    public double MostBend => 4.0 * Math.PI / LengthM;

    // How far the road has turned upward a distance along: the whole way round by its end.
    private double Turned(double s)
    {
        double t = Math.Clamp(s / LengthM, 0.0, 1.0);
        return (2.0 * Math.PI * t) - Math.Sin(2.0 * Math.PI * t);
    }

    /// <summary>How hard it bends a distance along, 1/m.</summary>
    public double Bend(double s) => s <= 0.0 || s >= LengthM ? 0.0 : MostBend * Math.Pow(Math.Sin(Math.PI * s / LengthM), 2.0);

    // How far to the left its middle is a distance along: eased at both ends, so the road meets it straight.
    private double Aside(double s)
    {
        double t = Math.Clamp(s / LengthM, 0.0, 1.0);
        return ShiftM * t * t * (3.0 - (2.0 * t));
    }

    private (double X, double Z, double Pitch) In(double s)
    {
        if (s <= 0.0) return (s, 0.0, 0.0);
        if (s >= LengthM) return (_x[^1] + (s - LengthM), _z[^1], 2.0 * Math.PI);
        double at = s / LengthM * (_x.Length - 1);
        int i = Math.Min((int)at, _x.Length - 2);
        double f = at - i;
        return (_x[i] + ((_x[i + 1] - _x[i]) * f), _z[i] + ((_z[i + 1] - _z[i]) * f), _pitch[i] + ((_pitch[i + 1] - _pitch[i]) * f));
    }

    /// <summary>
    /// The place a distance along it and to the left of its middle, with the way the road runs there
    /// and the way its asphalt faces. Before it and after it the level road it stands on, carried on.
    /// </summary>
    public double3 Point(double s, double leftM, out double3 along, out double3 facing)
    {
        (double x, double z, double pitch) = In(s);
        along = (_ahead * Math.Cos(pitch)) + (_up * Math.Sin(pitch));
        facing = (_up * Math.Cos(pitch)) - (_ahead * Math.Sin(pitch));
        return _base + (_ahead * x) + (_up * z) + (_left * (Aside(s) + leftM));
    }

    /// <summary>The way to its left, which is the same all the way round.</summary>
    public double3 Left => _left;

    /// <summary>
    /// What a place is over: how far along the loop and to the left of its middle, and how far off the
    /// asphalt the way that faces. False where it is not within the loop's width, its length and a
    /// wheel's reach of its asphalt.
    /// </summary>
    public bool TryLocate(double3 at, out double s, out double leftM, out double over, out double3 facing)
    {
        double3 from = at - _base;
        double x = Vec.Dot(from, _ahead), z = Vec.Dot(from, _up), y = Vec.Dot(from, _left);
        (s, leftM, over, facing) = (0.0, 0.0, 0.0, _up);

        // The loop passes over itself, so the nearest place is looked for among those the point is beside.
        int best = -1;
        double least = double.PositiveInfinity;
        double step = LengthM / (_x.Length - 1);
        for (int i = 0; i < _x.Length; i++)
        {
            if (Math.Abs(y - Aside(i * step)) > HalfWidthM) continue;
            double dx = x - _x[i], dz = z - _z[i], d = (dx * dx) + (dz * dz);
            if (d < least) (best, least) = (i, d);
        }
        if (best < 0 || least > (OverM + step) * (OverM + step)) return false;

        // And from there along the road's own run to the foot of the point.
        double along = best * step;
        for (int pass = 0; pass < 4; pass++)
        {
            (double px, double pz, double pitch) = In(along);
            along += ((x - px) * Math.Cos(pitch)) + ((z - pz) * Math.Sin(pitch));
        }
        if (along < 0.0 || along > LengthM) return false;

        (double fx, double fz, double turned) = In(along);
        over = ((z - fz) * Math.Cos(turned)) - ((x - fx) * Math.Sin(turned));
        leftM = y - Aside(along);
        if (over < -UnderM || over > OverM || Math.Abs(leftM) > HalfWidthM) return false;

        s = along;
        facing = (_up * Math.Cos(turned)) - (_ahead * Math.Sin(turned));
        return true;
    }
}
