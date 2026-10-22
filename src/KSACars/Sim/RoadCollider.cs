using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// Roads that are joined as the one solid a physics engine is given for them: every triangle of the
/// meshes of their runs and of the junctions those stop at, which are the ones drawn, about one origin.
///
/// <para>One for all of it and not one a mesh, because a physics engine smooths a contact at an edge
/// two triangles of one solid share and not where two solids meet, and a hull sliding from one onto
/// the next is stopped or thrown there. So a corner can be kilometres from the origin, where a float
/// is exact to a tenth of a millimetre at 2 km and half a millimetre at 5. Each is narrowed from where
/// it was worked out and not from its mesh's floats, so the triangles of two meshes that share a row,
/// and of a run and the junction at its mouth, share its corners to the last bit.</para>
///
/// <para>A road on the ground is a sheet with nothing under it, its banks' feet below the ground; a
/// raised one is closed, as its mesh is.</para>
/// </summary>
/// <param name="Origin">From the body's centre: the middle of the box round the run.</param>
/// <param name="Corners">
/// Three a triangle, from the origin, in the order a physics engine that takes (c - a) x (b - a) for
/// a triangle's solid side wants: a mesh's own order, which is anticlockwise from outside, with its
/// last two exchanged.
/// </param>
/// <param name="RadiusM">The furthest any corner is from the origin.</param>
internal sealed record RoadCollider(double3 Origin, float3[] Corners, double RadiusM)
{
    public int Triangles => Corners.Length / 3;

    /// <summary>The solid of <paramref name="meshes"/> that are joined, or none if they have no triangle.</summary>
    public static RoadCollider? Of(IReadOnlyList<RoadMeshData> meshes)
    {
        int corners = 0;
        double3 low = new(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity), high = -low;
        foreach (RoadMeshData mesh in meshes)
        {
            corners += mesh.Indices.Length;
            foreach (double3 at in mesh.Places)
            {
                low = new double3(Math.Min(low.X, at.X), Math.Min(low.Y, at.Y), Math.Min(low.Z, at.Z));
                high = new double3(Math.Max(high.X, at.X), Math.Max(high.Y, at.Y), Math.Max(high.Z, at.Z));
            }
        }
        if (corners == 0) return null;

        double3 origin = (low + high) * 0.5;
        float3[] all = new float3[corners];
        double radius = 0.0;
        int n = 0;
        foreach (RoadMeshData mesh in meshes)
        {
            for (int t = 0; t + 2 < mesh.Indices.Length; t += 3)
            {
                all[n++] = Narrow(mesh.Places[mesh.Indices[t]] - origin, ref radius);
                all[n++] = Narrow(mesh.Places[mesh.Indices[t + 2]] - origin, ref radius);
                all[n++] = Narrow(mesh.Places[mesh.Indices[t + 1]] - origin, ref radius);
            }
        }
        return new RoadCollider(origin, all, radius);
    }

    private static float3 Narrow(double3 from, ref double radius)
    {
        radius = Math.Max(radius, Vec.Len(from));
        return new float3((float)from.X, (float)from.Y, (float)from.Z);
    }
}
