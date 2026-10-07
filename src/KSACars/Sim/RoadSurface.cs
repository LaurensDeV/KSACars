using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// The tops of the roads laid on a body, as something a wheel can be over: each road a strip of its
/// width either side of a line of points, level across.
///
/// <para>The points are in the body's own frame, where the ground does not move. A road up to
/// <see cref="StepM"/> above a point counts as under it; one higher than that is a bridge overhead,
/// and the lower road is the one answered. That is taller than a car, which cannot be under such a
/// road anyway, because a car meeting a ramp at speed is a metre into it within one step and has to
/// be pushed back out of it, not let through as if it had driven underneath.</para>
///
/// <para>Past its edge and past its ends a road falls away at <see cref="ShoulderSlope"/>, which is
/// not drawn: a road stands a hand above the ground, and a wheel meeting that as a step is thrown.</para>
/// </summary>
public sealed class RoadSurface
{
    public const double StepM = 2.0;

    /// <summary>How far the shoulder drops for each metre out from the road, and how far it drops before it ends.</summary>
    public const double ShoulderSlope = 1.0 / 15.0, ShoulderDropM = 0.5;

    private const double CellM = 32.0;

    // OpenA and OpenB: the road ends there, with nothing carrying on from it.
    private readonly record struct Piece(double3 A, double3 B, double HalfWidth, bool OpenA, bool OpenB);

    private readonly Dictionary<(int, int, int), List<Piece>> _cells = [];
    private readonly int _reach;

    /// <param name="roads">Each road's centre line on its surface, from the body's centre, half its width, and whether it is a ring.</param>
    public RoadSurface(IEnumerable<(double3[] Line, double HalfWidth, bool Closed)> roads)
    {
        double widest = 0.0;
        foreach ((double3[] line, double halfWidth, bool closed) in roads)
        {
            widest = Math.Max(widest, halfWidth);
            for (int i = 1; i < line.Length; i++)
            {
                (int, int, int) cell = Cell(line[i - 1]);
                if (!_cells.TryGetValue(cell, out List<Piece>? pieces)) _cells[cell] = pieces = [];
                pieces.Add(new Piece(line[i - 1], line[i], halfWidth, !closed && i == 1, !closed && i == line.Length - 1));
            }
        }
        // A piece is filed under where it starts, so the search goes a piece's length and a half width past its own cell.
        _reach = 1 + (int)Math.Ceiling((widest + (ShoulderDropM / ShoulderSlope)) / CellM);
    }

    private static (int, int, int) Cell(double3 p) => ((int)Math.Floor(p.X / CellM), (int)Math.Floor(p.Y / CellM), (int)Math.Floor(p.Z / CellM));

    /// <summary>How far a point is above the road under it (m), negative when it is in it; false with no road under it.</summary>
    public bool TryHeightOver(double3 at, out double metres) => TryHeightOver(at, null, out metres);

    /// <summary>
    /// The same for a point that was <paramref name="last"/> above a road a moment ago: it is still
    /// over that road however far into it it has got, and the road answered is the one whose surface is
    /// nearest where it was. That is what tells a car that has run into a ramp from one under a bridge.
    /// </summary>
    public bool TryHeightOver(double3 at, double? last, out double metres)
    {
        if (last is { } before)
        {
            // Further than a car moves in a step, the road has been moved from under it, and it is
            // looked for afresh: a road raised in the editor does not take a car up with it.
            if (Nearest(at, before, out metres) && Math.Abs(metres - before) <= FollowedM) return true;
            last = null;
        }
        return Nearest(at, last, out metres);
    }

    /// <summary>How far a road's surface can be from where a wheel was over it a step ago and still be the road it was on.</summary>
    public const double FollowedM = 25.0;

    private bool Nearest(double3 at, double? last, out double metres)
    {
        metres = double.PositiveInfinity;
        if (_cells.Count == 0) return false;

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
                        // Where along the piece the point is, seen from above: measured along the piece
                        // itself, a point over a ramp is put too far down it and so too near its surface.
                        double3 up = Vec.Unit(piece.A);
                        double3 flat = Vec.RejectFrom(piece.B - piece.A, up);
                        double length = Math.Max(Vec.Len(flat), 1e-6);
                        double t = Vec.Dot(at - piece.A, flat) / (length * length);

                        // Past a piece's end is the next piece's to answer, unless the road ends there:
                        // then it is the shoulder, level with the end and falling away from it.
                        double past = Math.Max(Math.Max(-t, t - 1.0), 0.0) * length;
                        if (past > 0.02 && !(t < 0.0 ? piece.OpenA : piece.OpenB)) continue;

                        double3 on = piece.A + ((piece.B - piece.A) * Math.Clamp(t, 0.0, 1.0));
                        double3 off = at - on;
                        double height = Vec.Dot(off, up);
                        double3 beside = at - (piece.A + (flat * t));
                        double out_ = Math.Max(Vec.Len(beside - (up * Vec.Dot(beside, up))) - piece.HalfWidth, 0.0) + past;
                        if (out_ * ShoulderSlope > ShoulderDropM) continue;

                        double over = height + (out_ * ShoulderSlope);
                        if (last is { } was)
                        {
                            if (Math.Abs(over - was) >= Math.Abs(metres - was)) continue;
                        }
                        else if (over < -StepM || over >= metres)
                        {
                            continue;
                        }
                        metres = over;
                    }
                }
            }
        }
        return !double.IsPositiveInfinity(metres);
    }
}
