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

    /// <summary>How far ahead of where it leaves the ground a loop of this length comes back to it.</summary>
    public static double Reach(double lengthM)
    {
        const int Steps = 2000;
        double x = 0.0, step = lengthM / Steps;
        for (int i = 0; i < Steps; i++)
        {
            double t = (i + 0.5) / Steps;
            x += Math.Cos((2.0 * Math.PI * t) - Math.Sin(2.0 * Math.PI * t)) * step;
        }
        return x;
    }

    /// <summary>
    /// A circuit's loop where its roads are laid: from the end of the road at its first point, the
    /// way that road arrives, to one side by as far as the road at its second point starts to the
    /// side of it. Null with the reason where either point has no road ending at it.
    /// </summary>
    public static RoadLoop? Of(Circuit.Loop loop, IReadOnlyList<RoadRibbon> ribbons, out string why)
    {
        if (End(ribbons, loop.From, arriving: true) is not { } from || End(ribbons, loop.To, arriving: false) is not { } to)
        {
            why = $"the loop from point {loop.From} to point {loop.To} has no road ending at one of them";
            return null;
        }
        double3 up = Vec.Unit(from.At), ahead = Vec.Unit(Vec.RejectFrom(from.Ahead, up));
        why = "";
        return new RoadLoop(from.At, ahead, loop.LengthM, Vec.Dot(to.At - from.At, Vec.Cross(up, ahead)), Math.Min(from.HalfWidth, to.HalfWidth));
    }

    // Where a road ends at a point, on its own asphalt, and the way a car arriving there by it, or leaving there by it, is going.
    private static (double3 At, double3 Ahead, double HalfWidth)? End(IReadOnlyList<RoadRibbon> ribbons, int node, bool arriving)
    {
        foreach (RoadRibbon ribbon in ribbons)
        {
            if (ribbon.Closed || ribbon.Spans.Count == 0) continue;
            bool last = ribbon.Spans[^1].To == node, first = ribbon.Spans[0].From == node;
            if (!last && !first) continue;

            RoadRibbon.Section at = ribbon.At(last ? ribbon.LengthM : 0.0);
            ribbon.Chart.Compass(at.At, out _, out double3 east, out double3 north);
            double3 along = (east * at.Heading.E) + (north * at.Heading.N);
            return (ribbon.Point(at, 0.0, at.Height), last == arriving ? along : -along, at.HalfWidth);
        }
        return null;
    }

    /// <summary>
    /// The angle to steer a car on the loop by, to the left, towards a place on its middle a little
    /// ahead: a loop is carried aside as it goes round, and upside down is no place to learn that. Null
    /// for a car that is not on it.
    /// </summary>
    /// <param name="at">The car's centre of mass, from the body's centre.</param>
    /// <param name="body2Ccf">The car's turn: its up is X, ahead Y and left Z.</param>
    public double? Steer(double3 at, doubleQuat body2Ccf, double speedMs, double wheelbaseM)
    {
        if (!TryLocate(at, out double s, out _, out _, out _)) return null;
        double reach = Math.Max(6.0, 0.3 * speedMs);
        double3 to = doubleQuat.Conjugate(body2Ccf) * (Point(s + reach, 0.0, out _, out _) - at);
        return Math.Atan2(2.0 * wheelbaseM * Math.Sin(Math.Atan2(to.Z, to.Y)), reach);
    }

    /// <summary>How thick the road of it is, and how far apart its rows of vertices are at the least.</summary>
    public const double ThickM = 0.5, RowsEveryM = 1.0;

    /// <summary>How high its barriers stand at its foot, where they start, and how far round they come to a deck's height.</summary>
    public const double BarrierFootM = 0.05, BarrierRiseOverM = 8.0;

    private const int Across = 17;

    /// <summary>How high the barrier either side stands a distance along: low where it leaves the ground and comes back, so its end is no wall to meet.</summary>
    public double BarrierHigh(double s)
    {
        double t = Math.Clamp(Math.Min(s, LengthM - s) / BarrierRiseOverM, 0.0, 1.0);
        return BarrierFootM + ((RoadTessellation.BarrierHighM - BarrierFootM) * t * t * (3.0 - (2.0 * t)));
    }

    /// <summary>
    /// The loop as triangles: its asphalt, drawn as a road's is with its lines, its underside, and a
    /// barrier outside each edge as a deck has. A row every metre, or as few more apart as keep it to
    /// <paramref name="mostVertices"/>.
    /// </summary>
    public RoadMeshData Mesh(int mostVertices) => Built(mostVertices, solid: false);

    /// <summary>
    /// What the physics is given of it: a trough, its floor <see cref="ThickM"/> under the asphalt and
    /// a wall either side to the barriers' tops, each solid from both sides. Under the asphalt and not
    /// at it because a loop bends up under a hull's ends by more than a car stands clear on its
    /// wheels: at the asphalt, a car going round on its wheels would be scraping it.
    /// </summary>
    public RoadMeshData Solid(int mostVertices) => Built(mostVertices, solid: true);

    private RoadMeshData Built(int mostVertices, bool solid)
    {
        int rows = Math.Max(Math.Min((int)Math.Ceiling(LengthM / RowsEveryM), (mostVertices / Across) - 1), 8) + 1;
        double3 origin = Point(0.5 * LengthM, 0.0, out _, out _);
        double3[] places = new double3[rows * Across];
        float3[] positions = new float3[rows * Across], normals = new float3[rows * Across];
        float2[] uvs = new float2[rows * Across];
        double radius = 0.0, thick = RoadTessellation.BarrierThickM;
        for (int r = 0; r < rows; r++)
        {
            double s = LengthM * r / (rows - 1), high = BarrierHigh(s);
            double3 middle = Point(s, 0.0, out _, out double3 facing);

            void Put(int i, double aside, double over, double3 normal, float2 uv)
            {
                double3 at = middle + (_left * aside) + (facing * over), from = at - origin;
                places[(r * Across) + i] = at;
                radius = Math.Max(radius, Vec.Len(from));
                positions[(r * Across) + i] = new float3((float)from.X, (float)from.Y, (float)from.Z);
                normals[(r * Across) + i] = new float3((float)normal.X, (float)normal.Y, (float)normal.Z);
                uvs[(r * Across) + i] = uv;
            }

            // The asphalt as a road's: across the picture from its left edge, and along it by its length.
            for (int i = 0; i < 3; i++) Put(i, HalfWidthM * (1 - i), 0.0, facing, new float2((float)(0.5 * i), (float)(s / RoadTessellation.MarkingsM)));

            // A barrier a side: up its face from the asphalt's edge, across its top and down its back to the underside.
            double span = (2.0 * high) + thick + ThickM;
            float along = (float)(s / RoadTessellation.TrimRepeatM);
            float2 Trim(double gone) => new((float)(RoadTessellation.BarrierFrom + ((RoadTessellation.BarrierTo - RoadTessellation.BarrierFrom) * gone / span)), along);
            for (int side = 1, first = 3; side >= -1; side -= 2, first += 6)
            {
                double edge = side * HalfWidthM, back = side * (HalfWidthM + thick);
                Put(first, edge, 0.0, _left * -side, Trim(0.0));
                Put(first + 1, edge, high, _left * -side, Trim(high));
                Put(first + 2, edge, high, facing, Trim(high));
                Put(first + 3, back, high, facing, Trim(high + thick));
                Put(first + 4, back, high, _left * side, Trim(high + thick));
                Put(first + 5, back, -ThickM, _left * side, Trim(span));
            }

            // The underside, from one barrier's back to the other's.
            for (int i = 0; i < 2; i++)
            {
                double aside = (HalfWidthM + thick) * (1 - (2 * i));
                Put(15 + i, aside, -ThickM, -facing, new float2((float)(aside / RoadTessellation.TileM), (float)(s / RoadTessellation.TileM)));
            }
        }

        List<int> asphalt = [], under = [], trim = [];
        void Quad(List<int> to, int r, int a, int b)
        {
            // Anticlockwise seen from outside: a is to the left of b for whoever stands on the face looking along the loop.
            int a0 = (r * Across) + a, b0 = (r * Across) + b, a1 = a0 + Across, b1 = b0 + Across;
            to.AddRange([a0, b0, b1, a0, b1, a1]);
        }
        for (int r = 0; r < rows - 1; r++)
        {
            if (solid)
            {
                // The floor and each wall, from the barrier's back, each way round.
                foreach ((int a, int b) in new[] { (15, 16), (7, 8), (14, 13) })
                {
                    Quad(under, r, a, b);
                    Quad(under, r, b, a);
                }
                continue;
            }
            Quad(asphalt, r, 0, 1);
            Quad(asphalt, r, 1, 2);
            Quad(under, r, 16, 15);
            Quad(trim, r, 4, 3);
            Quad(trim, r, 6, 5);
            Quad(trim, r, 8, 7);
            Quad(trim, r, 9, 10);
            Quad(trim, r, 11, 12);
            Quad(trim, r, 13, 14);
        }
        return new RoadMeshData(origin, positions, normals, uvs, [.. asphalt, .. under, .. trim], asphalt.Count, 0, under.Count, radius, 0.0, LengthM,
                                [], [], 0, places, trim.Count);
    }

    /// <summary>The way to its left, which is the same all the way round.</summary>
    public double3 Left => _left;

    /// <summary>
    /// What a place is over: how far along the loop and to the left of its middle, and how far off the
    /// asphalt the way that faces. False where it is not within the loop's width, its length and a
    /// wheel's reach of its asphalt.
    /// </summary>
    public bool TryLocate(double3 at, out double s, out double leftM, out double over, out double3 facing) =>
        Locate(at, 0.0, out s, out leftM, out over, out facing);

    /// <summary>
    /// Whether a place is at one of the loop's barriers: within its length and a barrier's height of
    /// its asphalt, whichever side of the edge it is on.
    /// </summary>
    /// <param name="pastM">How far past the asphalt's edge the place is: under nothing, and it is short of the wall.</param>
    /// <param name="outward">The way out of the loop through the wall there.</param>
    public bool TryBarrier(double3 at, out double pastM, out double3 outward)
    {
        (pastM, outward) = (double.NegativeInfinity, default);
        if (!Locate(at, BesideM, out _, out double leftM, out _, out _)) return false;
        (pastM, outward) = (Math.Abs(leftM) - HalfWidthM, leftM < 0.0 ? -_left : _left);
        return true;
    }

    // How far past the asphalt's edge a place is still asked about for the barrier there.
    private const double BesideM = 1.5;

    private bool Locate(double3 at, double besideM, out double s, out double leftM, out double over, out double3 facing)
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
            if (Math.Abs(y - Aside(i * step)) > HalfWidthM + besideM) continue;
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
        if (over < -UnderM || over > OverM || Math.Abs(leftM) > HalfWidthM + besideM) return false;

        s = along;
        facing = (_up * Math.Cos(turned)) - (_ahead * Math.Sin(turned));
        return true;
    }
}
