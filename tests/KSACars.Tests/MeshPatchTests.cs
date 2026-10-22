using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class MeshPatchTests
{
    private const double SizeM = 10.0;

    public static TheoryData<int, double> Patches => new() { { MeshPatch.MaxCells, 0.0 }, { 12, 3.0 }, { 1, -2.0 }, { 7, 0.5 } };

    private static double3 Wide(float3 v) => new(v.X, v.Y, v.Z);

    [Theory]
    [MemberData(nameof(Patches))]
    public void ItHasTheVerticesAndIndicesItSaysItHas(int cells, double bendM)
    {
        MeshPatch patch = MeshPatch.Build(SizeM, cells, bendM);

        Assert.Equal((cells + 1) * (cells + 1), MeshPatch.VerticesFor(cells));
        Assert.Equal(cells * cells * 6, MeshPatch.IndicesFor(cells));
        Assert.Equal(MeshPatch.VerticesFor(cells), patch.Positions.Length);
        Assert.Equal(patch.Positions.Length, patch.Normals.Length);
        Assert.Equal(patch.Positions.Length, patch.Uvs.Length);
        Assert.Equal(MeshPatch.IndicesFor(cells), patch.Indices.Length);
        Assert.All(patch.Indices, i => Assert.InRange(i, 0, patch.Positions.Length - 1));
    }

    [Theory]
    [MemberData(nameof(Patches))]
    public void AVertexNormalIsTheSurfacesOwn(int cells, double bendM)
    {
        MeshPatch patch = MeshPatch.Build(SizeM, cells, bendM);
        const double step = 1e-5;

        for (int j = 0; j <= cells; j++)
        {
            for (int i = 0; i <= cells; i++)
            {
                double u = SizeM * (((double)i / cells) - 0.5), v = SizeM * j / cells;
                double3 across = MeshPatch.Point(SizeM, bendM, u + step, v) - MeshPatch.Point(SizeM, bendM, u - step, v);
                double3 along = MeshPatch.Point(SizeM, bendM, u, v + step) - MeshPatch.Point(SizeM, bendM, u, v - step);
                double3 measured = Vec.Unit(Vec.Cross(along, across));

                double3 normal = Wide(patch.Normals[(j * (cells + 1)) + i]);
                Assert.Equal(1.0, Vec.Len(normal), 5);
                Assert.True(Vec.Len(normal - measured) < 1e-4, $"at {i},{j} the normal is {normal} and the surface's is {measured}");
                Assert.True(normal.Y > 0.5);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Patches))]
    public void EveryTriangleFacesTheWayItsNormalsDoAndHasTextureToShow(int cells, double bendM)
    {
        MeshPatch patch = MeshPatch.Build(SizeM, cells, bendM);

        for (int t = 0; t < patch.Indices.Length; t += 3)
        {
            int a = patch.Indices[t], b = patch.Indices[t + 1], c = patch.Indices[t + 2];
            double3 face = Vec.Cross(Wide(patch.Positions[b]) - Wide(patch.Positions[a]), Wide(patch.Positions[c]) - Wide(patch.Positions[a]));
            Assert.True(Vec.Len(face) > 1e-6, $"triangle {t / 3} has no area");
            foreach (int corner in new[] { a, b, c })
            {
                Assert.True(Vec.Dot(Vec.Unit(face), Wide(patch.Normals[corner])) > 0.7, $"triangle {t / 3} faces away from its normals");
            }

            float2 ua = patch.Uvs[a], ub = patch.Uvs[b], uc = patch.Uvs[c];
            double uvArea = ((ub.X - ua.X) * (uc.Y - ua.Y)) - ((ub.Y - ua.Y) * (uc.X - ua.X));
            Assert.True(Math.Abs(uvArea) > 1e-6, $"triangle {t / 3} has no area in the texture");
        }
    }

    [Fact]
    public void ASmoothNormalIsNotTheFlatOne()
    {
        MeshPatch patch = MeshPatch.Build(SizeM, 12, 0.0);
        double worst = 0.0;
        for (int t = 0; t < patch.Indices.Length; t += 3)
        {
            int a = patch.Indices[t], b = patch.Indices[t + 1], c = patch.Indices[t + 2];
            double3 face = Vec.Unit(Vec.Cross(Wide(patch.Positions[b]) - Wide(patch.Positions[a]), Wide(patch.Positions[c]) - Wide(patch.Positions[a])));
            worst = Math.Max(worst, Vec.Len(face - Wide(patch.Normals[a])));
        }
        Assert.True(worst > 0.02, $"the normals differ from the faces by no more than {worst}");
    }

    [Fact]
    public void ACoarserBentPatchFitsWhereTheFinestWasAndIsADifferentShape()
    {
        MeshPatch finest = MeshPatch.Build(SizeM, MeshPatch.MaxCells, 0.0), bent = MeshPatch.Build(SizeM, 12, 3.0);

        Assert.True(bent.Positions.Length < finest.Positions.Length);
        Assert.True(bent.Indices.Length < finest.Indices.Length);
        Assert.Equal(MeshPatch.VerticesFor(MeshPatch.MaxCells), finest.Positions.Length);
        Assert.Equal(MeshPatch.VerticesFor(MeshPatch.MaxCells), MeshPatch.Build(SizeM, 500, 0.0).Positions.Length);

        Assert.Equal(3.0, bent.Positions[^1].X - finest.Positions[^1].X, 4);
        Assert.Equal(0.0, bent.Positions[0].X - finest.Positions[0].X, 4);
        Assert.InRange(finest.Radius(), SizeM, 1.2 * SizeM);
    }

    [Fact]
    public void TheTextureRepeatsEveryFewMetres()
    {
        MeshPatch patch = MeshPatch.Build(SizeM, 4, 0.0);
        Assert.Equal(SizeM / MeshPatch.TileM, patch.Uvs[^1].Y - patch.Uvs[0].Y, 5);
        Assert.Equal(SizeM / MeshPatch.TileM, patch.Uvs[^1].X - patch.Uvs[0].X, 5);
    }
}
