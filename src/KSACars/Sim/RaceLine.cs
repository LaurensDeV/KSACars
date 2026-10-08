using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// The line to drive, drawn on the road ahead of a car as arrowheads: where the route is, and by
/// their colour whether the car is going slower than the route allows there, or faster.
///
/// <para>Where each arrowhead lies is worked out once for a route, every corner of it on the road's
/// own surface, and they are written to the graphics card a long stretch at a time. What changes as
/// the car moves is which of them are drawn and in which colour, since the colour of each is the
/// car's speed now against the speed the route allows there, braking for what comes after it counted
/// in: runs of arrowheads of one colour, each run a draw.</para>
/// </summary>
internal static class RaceLine
{
    /// <summary>How far apart the arrowheads are along the route, and how far ahead of the car they are drawn.</summary>
    public const double EveryM = 3.0, AheadM = 210.0;

    /// <summary>How far ahead of the car's middle the first is: past its nose.</summary>
    public const double FromM = 4.0;

    /// <summary>An arrowhead's half width, its half length and how far its notch is behind its tip.</summary>
    public const double HalfWideM = 0.7, HalfLongM = 0.6, NotchM = 0.55;

    /// <summary>How far over the asphalt it is drawn, which is what keeps it from flickering in it.</summary>
    public const double LiftM = 0.04;

    /// <summary>The most arrowheads drawn at once, and what each is made of.</summary>
    public const int Most = (int)(AheadM / EveryM), Vertices = 4, Indices = 6;

    /// <summary>How many colours there are, and how long the line is taken to be behind the car, s.</summary>
    public const int Levels = 4;
    public const double LagSeconds = 0.03;

    /// <summary>Arrowheads of one colour that follow one another: which colour, the first of them and how many.</summary>
    public readonly record struct Run(int Level, int From, int Count);

    /// <summary>The share of the allowed speed under which an arrowhead is all blue, and over which it is all red.</summary>
    public const double CoolShare = 0.97, HotShare = 1.2;

    /// <summary>One arrowhead: how far along the route it is, the sample it is at, its tip, left, notch and right, and the way the road faces under it.</summary>
    public readonly record struct Arrow(double S, int Sample, double3 Tip, double3 Left, double3 Notch, double3 Right, double3 Facing);

    /// <summary>A stretch of arrowheads, about an origin of its own so a float holds it.</summary>
    public sealed record Mesh(double3 Origin, float3[] Positions, float3[] Normals, float2[] Uvs, int[] Indices, double RadiusM);

    /// <summary>
    /// How an arrowhead is coloured, from 0, blue, through a half, yellow, to 1, red: nothing while
    /// the car is under the speed allowed there, and all of it by <see cref="HotShare"/> of that speed.
    /// </summary>
    public static double Heat(double speedMs, double allowedMs)
    {
        if (!(allowedMs > 0.0)) return 1.0;
        return Math.Clamp(((speedMs / allowedMs) - CoolShare) / (HotShare - CoolShare), 0.0, 1.0);
    }

    /// <summary>Every arrowhead of a route, from its start, each corner on the road's surface where there is one under it.</summary>
    public static Arrow[] Lay(Route route, RoadSurface? road)
    {
        ReadOnlySpan<Route.Sample> samples = route.Samples;
        int count = (int)Math.Floor(route.LengthM / EveryM);
        if (samples.Length < 2 || count < 1) return [];

        Arrow[] arrows = new Arrow[count];
        for (int k = 0; k < count; k++)
        {
            double s = k * EveryM;
            int i = route.IndexAt(s);
            Route.Sample here = samples[i];
            double3 at = here.At + (here.Tangent * (s - here.S));
            double3 up = Vec.Unit(at);
            double3 facing = up;
            double3 On(double left, double ahead)
            {
                double3 p = at + (here.Across * left) + (here.Tangent * ahead);
                double3 radial = Vec.Unit(p);
                if (road is null || !road.TryLocate(p + radial, null, out double over, out _, out double3? face)) return p + (radial * LiftM);
                if (face is { } f) facing = f;
                return p + (radial * (1.0 - over + LiftM));
            }
            double3 tip = On(0.0, HalfLongM), left = On(HalfWideM, -HalfLongM), right = On(-HalfWideM, -HalfLongM);
            arrows[k] = new Arrow(s, i, tip, left, On(0.0, HalfLongM - NotchM), right, facing);
        }
        return arrows;
    }

    /// <summary>Which of the <see cref="Levels"/> colours a heat is drawn in: blue, yellow, orange, red.</summary>
    public static int Level(double heat) => Math.Clamp((int)(heat * Levels), 0, Levels - 1);

    /// <summary>
    /// The first arrowhead drawn for a car so far along the route: past its nose, and as far again as
    /// it goes before the line is next looked at. Past the route's last where the car is near its end.
    /// </summary>
    public static int First(double s, double speedMs) => (int)Math.Ceiling((s + FromM + (Math.Max(speedMs, 0.0) * LagSeconds)) / EveryM);

    /// <summary>
    /// So many arrowheads from one on as a mesh, round a closed route past its start and on an open
    /// one no further than its end. They are written once and drawn a run at a time, so the mesh has
    /// no colour of its own. Null with none.
    /// </summary>
    public static Mesh? Stretch(ReadOnlySpan<Arrow> arrows, bool closed, int first, int most)
    {
        if (arrows.Length == 0 || first < 0) return null;
        int count = closed ? most : Math.Min(most, arrows.Length - first);
        if (count < 1) return null;

        float3[] positions = new float3[count * Vertices], normals = new float3[count * Vertices];
        float2[] uvs = new float2[count * Vertices];
        int[] indices = new int[count * Indices];
        double3 origin = arrows[first % arrows.Length].Tip;
        double reach = 0.0;
        for (int k = 0; k < count; k++)
        {
            Arrow arrow = arrows[(first + k) % arrows.Length];
            int v = k * Vertices, n = k * Indices;
            ReadOnlySpan<double3> corners = [arrow.Tip, arrow.Left, arrow.Notch, arrow.Right];
            for (int c = 0; c < Vertices; c++)
            {
                double3 from = corners[c] - origin;
                reach = Math.Max(reach, Vec.Len(from));
                positions[v + c] = new float3((float)from.X, (float)from.Y, (float)from.Z);
                normals[v + c] = new float3((float)arrow.Facing.X, (float)arrow.Facing.Y, (float)arrow.Facing.Z);
                uvs[v + c] = new float2(0.5f, 0.5f);
            }
            // Anticlockwise seen from above, the side KSA draws: tip, left, notch and tip, notch, right.
            (indices[n], indices[n + 1], indices[n + 2]) = (v, v + 1, v + 2);
            (indices[n + 3], indices[n + 4], indices[n + 5]) = (v, v + 2, v + 3);
        }
        return new Mesh(origin, positions, normals, uvs, indices, reach);
    }

    /// <summary>
    /// The arrowheads from <paramref name="first"/> to <see cref="AheadM"/> on as runs of one colour
    /// each, for a car at this speed; how many runs, and through <paramref name="count"/> how many
    /// arrowheads they cover. A run's start is counted from <paramref name="first"/>.
    /// </summary>
    /// <param name="allowedMs">The speed the route allows at each of its samples.</param>
    public static int Runs(ReadOnlySpan<Arrow> arrows, ReadOnlySpan<double> allowedMs, bool closed, int first, double speedMs, Span<Run> runs, out int count)
    {
        count = arrows.Length == 0 || first < 0 ? 0 : closed ? Math.Min(Most, arrows.Length) : Math.Min(Most, arrows.Length - first);
        int made = 0;
        for (int k = 0; k < count; k++)
        {
            Arrow arrow = arrows[(first + k) % arrows.Length];
            int level = Level(Heat(speedMs, arrow.Sample < allowedMs.Length ? allowedMs[arrow.Sample] : 0.0));
            if (made > 0 && runs[made - 1].Level == level) runs[made - 1] = runs[made - 1] with { Count = runs[made - 1].Count + 1 };
            else if (made < runs.Length) runs[made++] = new Run(level, k, 1);
            else return made;
        }
        return made;
    }
}
