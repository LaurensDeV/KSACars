using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// A course's start, painted on its asphalt: a chequered band between two white lines across the
/// road, from the line inside one edge to the line inside the other, and behind it the boxes of a grid, in two columns a box apart.
///
/// <para>Every corner is on the road's own surface, as the arrowheads of <see cref="RaceLine"/> are,
/// and all of it is one mesh in one material: a quad is white or black by which half of the paint's
/// picture its corners are in.</para>
/// </summary>
internal static class StartGrid
{
    /// <summary>How far along the route the car on pole stands, and how far past it the line is.</summary>
    public const double PoleM = 5.0, LineAheadM = 4.5;

    /// <summary>A chequer's side, and how deep each white line either side of the two rows is.</summary>
    public const double SquareM = 0.6, LineM = 0.3;

    /// <summary>How many boxes a closed course has, how far apart along it they are, and the most a column is from the road's middle.</summary>
    public const int Boxes = 10;
    public const double BoxEveryM = 8.0, ColumnM = 2.4;

    /// <summary>A box: how wide it is, how far ahead of the car's middle its front is, how long its sides are and how thick its paint.</summary>
    public const double BoxWideM = 2.6, BoxFrontM = 2.9, BoxSideM = 1.2, BoxPaintM = 0.2;

    /// <summary>
    /// The share of a road's width, in from each edge, that the line leaves bare: the white line the
    /// asphalt's own picture has inside each edge, which the chequers stop at and do not cross.
    /// </summary>
    public const double EdgeShare = 0.046;

    /// <summary>A road with less than this either side of its middle has one column of boxes, down the middle.</summary>
    public const double NarrowM = 3.5;

    /// <summary>How far over the asphalt the paint is drawn: low enough to throw no shadow under itself, and under the racing line's arrowheads.</summary>
    public const double LiftM = 0.02;

    private const float WhiteU = 0.25f, BlackU = 0.75f;

    /// <summary>How far to the left of the road's middle a box's column is: the pole's to the left, the next to the right.</summary>
    public static double Column(int box, double halfWidthM) =>
        halfWidthM < NarrowM ? 0.0 : (box % 2 == 0 ? 1.0 : -1.0) * Math.Min(ColumnM, 0.45 * halfWidthM);

    /// <summary>How many boxes a route has: an open one only its pole, having no road behind its start.</summary>
    public static int BoxesOn(Route route) => route.Closed && route.LengthM > 4.0 * Boxes * BoxEveryM ? Boxes : 1;

    /// <summary>The start of a route as a mesh about an origin of its own, or null where the route is too short for one.</summary>
    public static RaceLine.Mesh? Lay(Route route, RoadSurface? road)
    {
        if (route.Samples.Length < 2 || route.LengthM < PoleM + LineAheadM + (2.0 * (SquareM + LineM))) return null;

        List<double3> corners = [];
        List<double3> facings = [];
        List<float> shades = [];

        // A quad from its nearer edge to its further one along the route and from its right edge to its left across it.
        void Quad(double from, double to, double right, double left, float shade)
        {
            double3 facing = default;
            ReadOnlySpan<(double S, double Left)> at = [(from, right), (from, left), (to, left), (to, right)];
            foreach ((double s, double l) in at) corners.Add(On(route, road, s, l, ref facing));
            facings.Add(facing);
            shades.Add(shade);
        }

        double line = PoleM + LineAheadM;
        Route.Sample across = route.Samples[route.IndexAt(line)];
        double middle = across.CentreM, half = across.HalfWidth * (1.0 - (2.0 * EdgeShare));
        int columns = Math.Max(2, (int)Math.Round(2.0 * half / SquareM));
        double wide = 2.0 * half / columns;

        Quad(line, line + LineM, middle - half, middle + half, WhiteU);
        for (int row = 0; row < 2; row++)
        {
            double from = line + LineM + (row * SquareM);
            for (int c = 0; c < columns; c++)
            {
                double right = middle - half + (c * wide);
                Quad(from, from + SquareM, right, right + wide, (c + row) % 2 == 0 ? WhiteU : BlackU);
            }
        }
        Quad(line + LineM + (2.0 * SquareM), line + (2.0 * LineM) + (2.0 * SquareM), middle - half, middle + half, WhiteU);

        int boxes = BoxesOn(route);
        for (int box = 0; box < boxes; box++)
        {
            double s = PoleM - (box * BoxEveryM);
            if (s < 0.0) s += route.LengthM;
            Route.Sample here = route.Samples[route.IndexAt(s)];
            double centre = here.CentreM + Column(box, here.HalfWidth), front = s + BoxFrontM;
            Quad(front - BoxPaintM, front, centre - (0.5 * BoxWideM), centre + (0.5 * BoxWideM), WhiteU);
            Quad(front - BoxPaintM - BoxSideM, front - BoxPaintM, centre - (0.5 * BoxWideM), centre - (0.5 * BoxWideM) + BoxPaintM, WhiteU);
            Quad(front - BoxPaintM - BoxSideM, front - BoxPaintM, centre + (0.5 * BoxWideM) - BoxPaintM, centre + (0.5 * BoxWideM), WhiteU);
        }

        int quads = shades.Count;
        float3[] positions = new float3[quads * 4], normals = new float3[quads * 4];
        float2[] uvs = new float2[quads * 4];
        int[] indices = new int[quads * 6];
        double3 origin = corners[0];
        double reach = 0.0;
        for (int q = 0; q < quads; q++)
        {
            for (int c = 0; c < 4; c++)
            {
                double3 from = corners[(q * 4) + c] - origin;
                reach = Math.Max(reach, Vec.Len(from));
                positions[(q * 4) + c] = new float3((float)from.X, (float)from.Y, (float)from.Z);
                normals[(q * 4) + c] = new float3((float)facings[q].X, (float)facings[q].Y, (float)facings[q].Z);
                // Spread over its half of the picture: with one place for all four corners KSA draws the quad speckled with holes.
                uvs[(q * 4) + c] = new float2(shades[q] + (c is 1 or 2 ? 0.15f : -0.15f), c >= 2 ? 0.8f : 0.2f);
            }
            // Anticlockwise seen from above, the side KSA draws: near right, far right, far left and near right, far left, near left.
            int v = q * 4, n = q * 6;
            (indices[n], indices[n + 1], indices[n + 2]) = (v, v + 3, v + 2);
            (indices[n + 3], indices[n + 4], indices[n + 5]) = (v, v + 2, v + 1);
        }
        return new RaceLine.Mesh(origin, positions, normals, uvs, indices, reach);
    }

    // A place so far along a route and so far to its left, on the road's surface where there is one under it.
    private static double3 On(Route route, RoadSurface? road, double s, double left, ref double3 facing)
    {
        if (route.Closed) s = ((s % route.LengthM) + route.LengthM) % route.LengthM;
        s = Math.Clamp(s, 0.0, route.LengthM - 1e-6);
        int i = route.IndexAt(s);
        Route.Sample here = route.Samples[i];
        double3 p = here.At + (here.Tangent * (s - here.S)) + (here.Across * left);
        double3 radial = Vec.Unit(p);
        facing = radial;
        if (road is null || !road.TryLocate(p + radial, null, out double over, out _, out double3? face)) return p + (radial * LiftM);
        if (face is { } f) facing = f;
        return p + (radial * (1.0 - over + LiftM));
    }
}
