using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// A square of curved surface as the vertices and triangles of a mesh, for the experiment that draws
/// one made while the game runs: crowned across, waved along and bent sideways, so that a normal for
/// each vertex shades it differently from a flat one for each triangle.
///
/// <para>In a right-handed frame of its own, as the road's slab is: X across, Y up and Z along, with
/// X from minus half the size to plus half and Z from nothing to the size. A triangle's corners go
/// round anticlockwise seen from above, which is the side KSA draws.</para>
/// </summary>
internal sealed record MeshPatch(float3[] Positions, float3[] Normals, float2[] Uvs, int[] Indices)
{
    /// <summary>The most cells a side that a patch is built with, and so what a slot for one has to hold.</summary>
    public const int MaxCells = 32;

    /// <summary>How far the texture goes before it repeats (m).</summary>
    public const double TileM = 4.0;

    private const double CrownShare = 0.05, WaveShare = 0.03;

    public static int VerticesFor(int cells) => (cells + 1) * (cells + 1);

    public static int IndicesFor(int cells) => cells * cells * 6;

    /// <summary>
    /// A patch <paramref name="sizeM"/> across of <paramref name="cells"/> by as many quads, its far
    /// end carried <paramref name="bendM"/> to the side.
    /// </summary>
    public static MeshPatch Build(double sizeM, int cells, double bendM)
    {
        cells = Math.Clamp(cells, 1, MaxCells);
        int side = cells + 1;
        float3[] positions = new float3[side * side], normals = new float3[side * side];
        float2[] uvs = new float2[side * side];
        int[] indices = new int[IndicesFor(cells)];

        for (int j = 0; j < side; j++)
        {
            for (int i = 0; i < side; i++)
            {
                double u = sizeM * (((double)i / cells) - 0.5), v = sizeM * j / cells;
                int at = (j * side) + i;
                positions[at] = Narrow(Point(sizeM, bendM, u, v));
                normals[at] = Narrow(Normal(sizeM, bendM, u, v));
                uvs[at] = new float2((float)(u / TileM), (float)(v / TileM));
            }
        }

        int n = 0;
        for (int j = 0; j < cells; j++)
        {
            for (int i = 0; i < cells; i++)
            {
                int near = (j * side) + i, far = near + side;
                indices[n++] = near;
                indices[n++] = far;
                indices[n++] = far + 1;
                indices[n++] = near;
                indices[n++] = far + 1;
                indices[n++] = near + 1;
            }
        }
        return new MeshPatch(positions, normals, uvs, indices);
    }

    /// <summary>Where the surface is at <paramref name="u"/> across and <paramref name="v"/> along.</summary>
    public static double3 Point(double sizeM, double bendM, double u, double v) =>
        new(u + (bendM * v * v / (sizeM * sizeM)), Height(sizeM, u, v), v);

    /// <summary>The surface's own unit normal there, from its slopes and not from any triangle.</summary>
    public static double3 Normal(double sizeM, double bendM, double u, double v)
    {
        double across = -CrownShare * Math.PI * Math.Sin(Math.PI * u / sizeM);
        double along = WaveShare * 2.0 * Math.PI * Math.Cos(2.0 * Math.PI * v / sizeM);
        double shear = 2.0 * bendM * v / (sizeM * sizeM);
        return Vec.Unit(new double3(-across, 1.0, (shear * across) - along));
    }

    /// <summary>The furthest any vertex is from the patch's origin (m).</summary>
    public double Radius()
    {
        double furthest = 0.0;
        foreach (float3 p in Positions) furthest = Math.Max(furthest, Math.Sqrt((p.X * p.X) + (p.Y * p.Y) + (p.Z * p.Z)));
        return furthest;
    }

    private static double Height(double sizeM, double u, double v) =>
        sizeM * ((CrownShare * Math.Cos(Math.PI * u / sizeM)) + (WaveShare * Math.Sin(2.0 * Math.PI * v / sizeM)));

    private static float3 Narrow(double3 v) => new((float)v.X, (float)v.Y, (float)v.Z);
}
