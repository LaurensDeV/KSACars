using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// A laid road as the solid a physics engine is given for it: one closed deck with its top on the
/// road's line, level across, cut into lengths of about <see cref="ChunkM"/>.
///
/// <para>Every point of the line has one cross-section, which the stretches either side of it both
/// end on, so the top is one surface with no end of a piece for a sliding hull to meet. The section
/// lies on the line that halves the turn there, as the surface a wheel is told is cut, and is
/// widened so the road keeps its width round the bend, by no more than <see cref="MitreCap"/>:
/// a kink sharper than 120 degrees has its outside corner cut short. A bend tighter than the road's
/// half width crosses the sections over on the inside, and the top there faces down.</para>
///
/// <para>Between two sections the top is two flat triangles, where the surface a wheel is told runs
/// straight from one section to the next along the stretch. Those are one plane on a straight and on
/// a level bend. Round a climbing bend they part along the diagonal, by the gradient times the half
/// width times the tangent of half the turn: 6 mm on an 8 m road climbing 1 in 20 round a 30 m
/// radius with a point every 2 m, and 7 cm on a 6 m road climbing 1 in 8 round a 5 m radius.</para>
///
/// <para>A triangle is solid from one side only, the side (c - a) x (b - a) points to, and every one
/// here is listed so that points out of the deck. A sheet would let through what came at it from
/// below, so the deck has an underside, two sides and, where a road ends, a cap.</para>
///
/// <para>A chunk is closed in itself. Two that only met at a section would each show a hull sliding
/// off the other an edge, and a physics engine smooths the edges inside one mesh and not those
/// between two: the hull stops dead on it. So each runs on inside the next as a tongue, sinking from
/// the section they share to a tenth of a metre down over <see cref="TongueM"/>, and a hull coming
/// the other way meets a slope of 1 in 200 where it would have met the edge.</para>
/// </summary>
public static class RoadDeck
{
    /// <summary>
    /// About how long a chunk is. Short enough that a corner's place from the chunk's own origin is
    /// exact to a hundredth of a millimetre in single precision and that only the road near a physics
    /// bubble has to be in it, and long enough that its two tongues are a fifth of it.
    /// </summary>
    public const double ChunkM = 200.0;

    /// <summary>The most a section is widened by at a turn.</summary>
    public const double MitreCap = 2.0;

    /// <summary>How far a chunk's tongue runs on inside the next chunk.</summary>
    public const double TongueM = 20.0;

    // Two points nearer than this, seen from above, are one: triangles that thin are lines in single precision.
    private const double LeastStretchM = 0.01;

    // A tongue's end: how much narrower it is, how far down its top is and how far up its underside,
    // as shares of the width and the depth.
    private const double TuckAcross = 0.1, TuckTop = 0.2, TuckBottom = 0.25;

    /// <summary>
    /// A length of a deck, closed: its triangles, three entries of <see cref="Deck.Corners"/> each,
    /// and a ball round them. It and the next chunk use the same four corners where they meet.
    /// </summary>
    public sealed record Chunk(double3 Origin, double RadiusM, int[] Triangles);

    /// <summary>
    /// A road's deck. <paramref name="Corners"/> are from the body's centre, four a section: top
    /// left, top right, bottom left and bottom right, looking along the road. The first
    /// <paramref name="Sections"/> fours are the road's own, in order, and the rest the tongues'.
    /// </summary>
    public sealed record Deck(double3[] Corners, int Sections, Chunk[] Chunks)
    {
        public int TriangleCount => Chunks.Sum(c => c.Triangles.Length / 3);
    }

    /// <param name="line">The road's centre line on its surface, from the body's centre; a ring's ends on its own first point.</param>
    public static Deck Build(double3[] line, double halfWidth, bool closed, double depthM, double chunkM = ChunkM)
    {
        List<double3> points = [];
        foreach (double3 p in line)
        {
            if (points.Count == 0 || Vec.Len(Vec.RejectFrom(p - points[^1], p)) >= LeastStretchM) points.Add(p);
        }
        if (closed && points.Count > 1 && Vec.Len(Vec.RejectFrom(points[^1] - points[0], points[0])) < LeastStretchM) points.RemoveAt(points.Count - 1);

        int n = points.Count;
        bool ring = closed && n >= 3;
        if (n < 2) return new Deck([], 0, []);

        List<double3> corners = [];
        double3[] left = new double3[n];
        for (int i = 0; i < n; i++)
        {
            double3 at = points[i], up = Vec.Unit(at);
            double3 before = ring || i > 0 ? Vec.Unit(Vec.RejectFrom(at - points[(i + n - 1) % n], up)) : Vec.Zero;
            double3 after = ring || i + 1 < n ? Vec.Unit(Vec.RejectFrom(points[(i + 1) % n] - at, up)) : before;
            if (!(ring || i > 0)) before = after;

            // Where the road doubles straight back no line halves the turn, and the section is square to the way on.
            double3 both = before + after;
            double3 ahead = Vec.Len(both) > 1e-6 ? Vec.Unit(both) : after;
            left[i] = Vec.Cross(up, ahead) * (halfWidth * Math.Min(1.0 / Math.Max(Vec.Dot(ahead, after), 1e-9), MitreCap));
            Section(corners, at, left[i], depthM, 0.0);
        }

        int stretches = ring ? n : n - 1;
        double[] middle = new double[stretches];
        double length = 0.0;
        for (int s = 0; s < stretches; s++)
        {
            double stretch = Vec.Len(points[(s + 1) % n] - points[s]);
            middle[s] = length + (0.5 * stretch);
            length += stretch;
        }

        int count = Math.Max(1, (int)Math.Round(length / chunkM));
        int[] chunkOf = new int[stretches];
        for (int s = 0; s < stretches; s++) chunkOf[s] = Math.Min((int)(middle[s] * count / length), count - 1);
        bool whole = chunkOf[0] == chunkOf[^1];

        // A tongue: the sections on from a chunk's end, sunk further the further on, as far as TongueM.
        int Tongue(List<int> to, int from, int step)
        {
            int rows = 0, most = ring ? n - 2 : step < 0 ? from : n - 1 - from;
            double reach = 0.0;
            for (; rows < most && reach < TongueM; rows++) reach += Vec.Len(points[Mod(from + (step * (rows + 1)), n)] - points[Mod(from + (step * rows), n)]);

            int last = 4 * from;
            double gone = 0.0;
            for (int k = 1; k <= rows; k++)
            {
                int row = Mod(from + (step * k), n);
                gone += Vec.Len(points[row] - points[Mod(row - step, n)]);
                Section(corners, points[row], left[row], depthM, gone / reach);
                if (step > 0) Stretch(to, last, corners.Count - 4);
                else Stretch(to, corners.Count - 4, last);
                last = corners.Count - 4;
            }
            return last;
        }

        List<Chunk> chunks = [];
        for (int first = 0; first < stretches;)
        {
            int past = first;
            while (past < stretches && chunkOf[past] == chunkOf[first]) past++;

            List<int> to = [];
            if (!whole || !ring)
            {
                int start = !ring && first == 0 ? 0 : Tongue(to, first, -1);
                Face(to, start, start + 2, start + 3, start + 1);
            }

            for (int s = first; s < past; s++) Stretch(to, 4 * s, 4 * ((s + 1) % n));

            if (!whole || !ring)
            {
                int end = !ring && past == stretches ? 4 * past : Tongue(to, past % n, 1);
                Face(to, end + 1, end + 3, end + 2, end);
            }

            double3 low = corners[to[0]], high = low;
            foreach (int corner in to)
            {
                low = new double3(Math.Min(low.X, corners[corner].X), Math.Min(low.Y, corners[corner].Y), Math.Min(low.Z, corners[corner].Z));
                high = new double3(Math.Max(high.X, corners[corner].X), Math.Max(high.Y, corners[corner].Y), Math.Max(high.Z, corners[corner].Z));
            }
            chunks.Add(new Chunk((low + high) * 0.5, 0.5 * Vec.Len(high - low), [.. to]));
            first = past;
        }
        return new Deck([.. corners], n, [.. chunks]);
    }

    private static int Mod(int i, int n) => ((i % n) + n) % n;

    // A section's four corners: the road's own with nothing tucked, and a tongue's end with all of it.
    private static void Section(List<double3> corners, double3 at, double3 left, double depthM, double tucked)
    {
        double3 up = Vec.Unit(at), across = left * (1.0 - (TuckAcross * tucked));
        double3 top = at - (up * (TuckTop * tucked * depthM)), bottom = at - (up * ((1.0 - (TuckBottom * tucked)) * depthM));
        corners.AddRange([top + across, top - across, bottom + across, bottom - across]);
    }

    // The top, the underside and the two sides between the section starting at corner a and the one at b.
    private static void Stretch(List<int> to, int a, int b)
    {
        Face(to, a + 1, b + 1, b, a);
        Face(to, a + 3, a + 2, b + 2, b + 3);
        Face(to, a, b, b + 2, a + 2);
        Face(to, b + 1, a + 1, a + 3, b + 3);
    }

    // A face's four corners going round anticlockwise as seen from outside the deck.
    private static void Face(List<int> to, int a, int b, int c, int d) => to.AddRange([a, c, b, a, d, c]);
}
