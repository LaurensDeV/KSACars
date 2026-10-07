using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// The tops of the roads laid on a body, as something a wheel can be over: each road a strip of its
/// width either side of a line of points, level across.
///
/// <para>The points are in the body's own frame, where the ground does not move. Each stretch
/// between two points answers for the ground between the planes that halve the turn at its two
/// ends, so round a bend every place has one stretch to answer for it: none on the outside is left
/// without, and none on the inside has two that disagree. That holds while the bend's radius is more
/// than the road's half width; tighter than that the inside edge folds over itself, and the highest
/// answer is given.</para>
///
/// <para>A road up to <see cref="StepM"/> above a point counts as under it; one higher than that is
/// a bridge overhead, and the highest road that is not is the one answered.</para>
///
/// <para>Past its edge and past its ends a road falls away at <see cref="ShoulderSlope"/>, which is
/// not drawn: a road stands a hand above the ground, and a wheel meeting that as a step is thrown.</para>
/// </summary>
public sealed class RoadSurface
{
    /// <summary>
    /// As deep as a road's collider is. A hub nearer the top than that is inside the box, where no
    /// car can be driven and one is only on its way back out; a hub further down is below the box, and
    /// under a deck. A hull on the box keeps a hub well above its top, so no more is needed for a car
    /// that has come down hard.
    /// </summary>
    public const double StepM = RoadSlabs.ThicknessM;

    /// <summary>How far the shoulder drops for each metre out from the road, and how far it drops before it ends.</summary>
    public const double ShoulderSlope = 1.0 / 15.0, ShoulderDropM = 0.5;

    // A road's asphalt this far under another's shoulder is what a wheel there is over.
    private const double AsphaltUnderM = 0.1;

    private const double CellM = 32.0;

    // Two points nearer than this, seen from above, are one: a stretch that short has no direction.
    private const double LeastPieceM = 1e-6;

    // Along: the stretch's direction seen from above. Across: level, square to it.
    // PlaneA and PlaneB: the normals, pointing on along the road, of the planes it answers between,
    // and CosA and CosB the cosine of each to Along.
    // OpenA and OpenB: the road ends there, with nothing carrying on from it.
    // Shouldered: near enough the ground that the fall past its edge reaches it.
    private readonly record struct Piece(double3 A, double3 B, double3 Along, double3 Across, double LengthM,
                                         double3 PlaneA, double3 PlaneB, double CosA, double CosB,
                                         double HalfWidth, bool OpenA, bool OpenB, bool Shouldered);

    private readonly Dictionary<(int, int, int), List<Piece>> _cells = [];
    private readonly int _reach;

    /// <param name="roads">Each road's centre line on its surface, from the body's centre, half its width, and whether it is a ring.</param>
    public RoadSurface(IEnumerable<(double3[] Line, double HalfWidth, bool Closed)> roads)
        : this(roads.Select(r => (r.Line, r.HalfWidth, r.Closed, (double[]?)null)))
    {
    }

    /// <param name="roads">
    /// As above, with how far each point of the line is above the ground. A stretch more than
    /// <see cref="ShoulderDropM"/> up has no shoulder: one that could not reach the ground would be a
    /// ledge in the air beside a raised road, lifting whatever drove under it.
    /// </param>
    public RoadSurface(IEnumerable<(double3[] Line, double HalfWidth, bool Closed, double[]? AboveGroundM)> roads)
    {
        double furthest = 0.0;
        foreach ((double3[] line, double halfWidth, bool closed, double[]? above) in roads)
        {
            List<double3> points = [], along = [];
            List<bool> low = [];
            for (int i = 0; i < line.Length; i++)
            {
                bool isLow = above is null || above[i] <= ShoulderDropM;
                if (points.Count > 0)
                {
                    double3 flat = Vec.RejectFrom(line[i] - points[^1], points[^1] + line[i]);
                    if (!(Vec.Len(flat) >= LeastPieceM))
                    {
                        low[^1] &= isLow;
                        continue;
                    }
                    along.Add(Vec.Unit(flat));
                }
                points.Add(line[i]);
                low.Add(isLow);
            }

            int count = along.Count;
            bool ring = closed && count > 1;
            for (int i = 0; i < count; i++)
            {
                bool openA = !ring && i == 0, openB = !ring && i == count - 1;
                double3 a = points[i], b = points[i + 1];
                double3 planeA = openA ? along[i] : Halving(along[(i + count - 1) % count], along[i], a, along[i]);
                double3 planeB = openB ? along[i] : Halving(along[i], along[(i + 1) % count], b, along[i]);
                Piece piece = new(a, b, along[i], Vec.Unit(Vec.Cross(a + b, along[i])), Vec.Len(Vec.RejectFrom(b - a, a + b)),
                                  planeA, planeB, Vec.Dot(planeA, along[i]), Vec.Dot(planeB, along[i]),
                                  halfWidth, openA, openB, low[i] && low[i + 1]);

                (int, int, int) cell = Cell(a);
                if (!_cells.TryGetValue(cell, out List<Piece>? pieces)) _cells[cell] = pieces = [];
                pieces.Add(piece);

                // The furthest from its start a stretch answers for: its own length on, and past each
                // end and each edge the half width and the shoulder, the corner of that half as far again.
                furthest = Math.Max(furthest, Vec.Len(b - a) + (1.5 * (halfWidth + (ShoulderDropM / ShoulderSlope))));
            }
        }
        // A stretch is filed under where it starts, and a cell more for a wheel in the air over it.
        _reach = 1 + (int)Math.Ceiling(furthest / CellM);
    }

    // The plane between two stretches that meet at a point: upright there, and halving the turn seen
    // from above. Where the road doubles straight back there is no such plane, and a stretch ends square.
    private static double3 Halving(double3 before, double3 after, double3 at, double3 square)
    {
        double3 both = Vec.RejectFrom(before + after, at);
        return Vec.Len(both) > 1e-6 ? Vec.Unit(both) : square;
    }

    private static (int, int, int) Cell(double3 p) => ((int)Math.Floor(p.X / CellM), (int)Math.Floor(p.Y / CellM), (int)Math.Floor(p.Z / CellM));

    /// <summary>How far a point is above the road under it (m), negative when it is in it; false with no road under it.</summary>
    public bool TryHeightOver(double3 at, out double metres) => TryHeightOver(at, null, out metres);

    /// <summary>
    /// The same for a point that was <paramref name="last"/> above a road a moment ago. One that was
    /// in a road is still over that road however far into it it has got: the roads it may be on are
    /// those no more than <see cref="StepM"/> above the surface it was on, which is what tells a car
    /// that has run into a ramp from one under a bridge.
    /// </summary>
    public bool TryHeightOver(double3 at, double? last, out double metres) => TryLocate(at, last, out metres, out _);

    /// <summary>
    /// The same, with how far out past the asphalt the point is (m): nothing on the road itself, and
    /// on the shoulder how far from the road's edge or its end. Where a road joins another, the
    /// shoulder of one lies a centimetre over the sunk end of the other, and that is asphalt.
    /// </summary>
    public bool TryLocate(double3 at, double? last, out double metres, out double outM)
    {
        metres = double.PositiveInfinity;
        outM = 0.0;
        if (_cells.Count == 0) return false;
        double asphalt = double.PositiveInfinity;

        // A wheel above its road is asked as one new to it, or a deck it flew in over would not be under it.
        double deepest = Math.Min(last ?? 0.0, 0.0) - StepM;
        double3 up = Vec.Unit(at);

        (int cx, int cy, int cz) = Cell(at);
        for (int x = cx - _reach; x <= cx + _reach; x++)
        {
            for (int y = cy - _reach; y <= cy + _reach; y++)
            {
                for (int z = cz - _reach; z <= cz + _reach; z++)
                {
                    if (!_cells.TryGetValue((x, y, z), out List<Piece>? pieces)) continue;
                    foreach (Piece piece in pieces)
                    {
                        // Past an end's plane is the next stretch's to answer, unless the road ends there.
                        double3 fromA = at - piece.A;
                        double pastA = -Vec.Dot(fromA, piece.PlaneA), pastB = -Vec.Dot(piece.B - at, piece.PlaneB);
                        if ((pastA > 0.0 && !piece.OpenA) || (pastB > 0.0 && !piece.OpenB)) continue;

                        // Square to the stretch and not to the line that halves the turn, which on the
                        // outside of a bend is further from the point than the road is wide.
                        double along = Vec.Dot(fromA, piece.Along);
                        double beside = Math.Abs(Vec.Dot(fromA, piece.Across)) - piece.HalfWidth;
                        double beyond = Math.Max(-along, along - piece.LengthM);

                        // Off an open end is the shoulder, falling from the end. Round the outside of a
                        // turn the road carries on as far past its stretch as it is wide, as it is drawn.
                        bool pastEnd = along < 0.0 ? piece.OpenA : piece.OpenB;
                        double out_ = pastEnd ? Math.Max(beside, 0.0) + Math.Max(beyond, 0.0)
                                              : Math.Max(Math.Max(beside, beyond - piece.HalfWidth), 0.0);
                        if (out_ * ShoulderSlope > ShoulderDropM || (out_ > 0.02 && !piece.Shouldered)) continue;

                        // How far along, as the share of the way from one plane to the other measured
                        // along the stretch, so both stretches at a plane give the height of the point they share.
                        double fromPlaneA = -pastA / Math.Max(piece.CosA, 1e-3), toPlaneB = -pastB / Math.Max(piece.CosB, 1e-3);
                        double span = fromPlaneA + toPlaneB;
                        double t = span > 1e-9 ? Math.Clamp(fromPlaneA / span, 0.0, 1.0) : 0.5;

                        double3 on = piece.A + ((piece.B - piece.A) * t);
                        double over = Vec.Dot(at - on, up) + (out_ * ShoulderSlope);
                        if (over < deepest) continue;
                        if (out_ <= 0.02) asphalt = Math.Min(asphalt, over);
                        if (over < metres)
                        {
                            metres = over;
                            outM = out_;
                        }
                    }
                }
            }
        }
        if (asphalt - metres <= AsphaltUnderM) outM = 0.0;
        return !double.IsPositiveInfinity(metres);
    }
}
