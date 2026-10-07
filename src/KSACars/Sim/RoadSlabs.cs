using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// One box of a road's collider: its centre from the body's centre, the three directions its edges
/// run in, and how long it is along the road and how wide across it (m). It is
/// <see cref="RoadSlabs.ThicknessM"/> deep along <paramref name="Normal"/>.
/// </summary>
public readonly record struct RoadSlab(double3 Centre, double3 Along, double3 Across, double3 Normal, double LengthM, double WidthM);

/// <summary>
/// A laid road as the boxes a physics engine is given for it: one for each stretch between two
/// points of its line, its top face on the line, level across, and the rest of it hanging below.
///
/// <para>One a stretch and none joined up: the line follows the ground at every point, so off dead
/// level ground no two stretches are in line, and a box over several would stand off the surface a
/// wheel is told.</para>
/// </summary>
public static class RoadSlabs
{
    /// <summary>
    /// How deep a box is. Deeper than a road is drawn, so that what falls onto one is not through it
    /// within a step, and no deeper, so that a car still fits under a deck.
    /// </summary>
    public const double ThicknessM = 0.5;

    // A physics engine keeps a shape for every size of box it has been given and never lets one go,
    // so sizes come in steps. Rounded up: a box too big overlaps its neighbour, one too small leaves a gap.
    public const double LengthStepM = 0.25, WidthStepM = 0.5;

    private readonly record struct Stretch(double3 A, double3 B, double3 Ahead, double3 Flat, double LengthM);

    /// <param name="line">The road's centre line on its surface, from the body's centre.</param>
    public static void Add(List<RoadSlab> to, double3[] line, double halfWidth, bool closed)
    {
        List<Stretch> stretches = [];
        for (int i = 0; i + 1 < line.Length; i++)
        {
            double3 along = line[i + 1] - line[i], flat = Vec.RejectFrom(along, line[i]);
            double length = Vec.Len(along);
            if (length > 1e-6 && Vec.Len(flat) > 1e-6) stretches.Add(new Stretch(line[i], line[i + 1], along / length, Vec.Unit(flat), length));
        }

        int count = stretches.Count;
        bool ring = closed && count > 1;
        double width = Math.Ceiling((2.0 * halfWidth / WidthStepM) - 1e-9) * WidthStepM;
        for (int i = 0; i < count; i++)
        {
            Stretch s = stretches[i];
            double3 up = Vec.Unit(s.A);
            double before = i > 0 || ring ? Reach(stretches[(i + count - 1) % count].Flat, s.Flat, up, halfWidth) : 0.0;
            double after = i + 1 < count || ring ? Reach(s.Flat, stretches[(i + 1) % count].Flat, up, halfWidth) : 0.0;

            double length = Math.Ceiling(((s.LengthM + before + after) / LengthStepM) - 1e-9) * LengthStepM;
            double3 across = Vec.Unit(Vec.Cross(up, s.Ahead));
            double3 normal = Vec.Cross(s.Ahead, across);
            double3 middle = ((s.A + s.B) * 0.5) + (s.Ahead * (0.5 * (after - before)));
            to.Add(new RoadSlab(middle - (normal * (0.5 * ThicknessM)), s.Ahead, across, normal, length, width));
        }
    }

    // Boxes end square, so at a turn each carries on to where the outside edges of the two would
    // meet, and no further than the surface a wheel is told does. Only for a turn seen from above:
    // over a crest two boxes share the edge between them already.
    private static double Reach(double3 from, double3 to, double3 up, double halfWidth)
    {
        double sin = Math.Abs(Vec.Dot(Vec.Cross(from, to), up)), cos = Vec.Dot(from, to);
        return halfWidth * (cos > 0.0 ? Math.Min(sin / (1.0 + cos), 1.0) : 1.0);
    }
}
