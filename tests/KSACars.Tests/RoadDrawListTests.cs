using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSACars.Tests;

/// <summary>A circuit's meshes as the game draws them and collides with them: cut to fit, picked by distance, and as one solid a run.</summary>
public class RoadDrawListTests(ITestOutputHelper output)
{
    private sealed record Built(List<(RoadRibbon Ribbon, List<RoadMeshData> ByLength, List<RoadMeshData> Fitted)> Runs);

    private static readonly Dictionary<string, Built> Laid = [];

    private static Circuit Through(TrackWorld world, double width, bool closed, params (double East, double North, double Height)[] points)
    {
        Circuit circuit = new Circuit { WidthM = width }.AddNode(world.Deg(points[0].North), world.Deg(points[0].East), out int first)
            .SetHeight(first, points[0].Height);
        int last = first;
        for (int i = 1; i < points.Length; i++)
        {
            circuit = circuit.Extend(last, world.Deg(points[i].North), world.Deg(points[i].East), out last).SetHeight(last, points[i].Height);
        }
        return closed ? circuit.Connect(last, first) : circuit;
    }

    // Two straights and two half circles, five kilometres round, on ground that rolls a little.
    private static (TrackWorld, Circuit) FiveKilometres()
    {
        TrackWorld world = TrackWorld.Earth((e, n) => (2.0 * Math.Sin(e / 180.0)) + (1.0 * Math.Cos(n / 90.0)));
        const double r = 270.0, half = 800.0;
        List<(double, double, double)> points = [(-half, -r, 0.0), (half, -r, 0.0)];
        for (int i = 1; i < 8; i++) points.Add((half + (r * Math.Cos((-Math.PI / 2.0) + (i * Math.PI / 8.0))), r * Math.Sin((-Math.PI / 2.0) + (i * Math.PI / 8.0)), 0.0));
        points.Add((half, r, 0.0));
        points.Add((-half, r, 0.0));
        for (int i = 1; i < 8; i++) points.Add((-half + (r * Math.Cos((Math.PI / 2.0) + (i * Math.PI / 8.0))), r * Math.Sin((Math.PI / 2.0) + (i * Math.PI / 8.0)), 0.0));
        return (world, Through(world, 12.0, true, [.. points]));
    }

    // A ring that is a deck all the way round, and a road that climbs from the ground onto a deck and comes down again.
    private static (TrackWorld, Circuit) Named(string name)
    {
        TrackWorld world = TrackWorld.Earth();
        return name switch
        {
            "five" => FiveKilometres(),
            "deck" => (world, Through(world, 8.0, true, (0, 0, 9), (150, 0, 9), (150, 150, 9), (0, 150, 9))),
            "ramp" => (world, Through(world, 8.0, false, (0, 0, 0), (150, 0, 0), (300, 20, 9), (450, 20, 9), (600, 0, 0), (700, 0, 0))),
            _ => (world, ExtremeCircuits.Of(name, world)),
        };
    }

    private static Built Of(string name)
    {
        lock (Laid)
        {
            if (!Laid.TryGetValue(name, out Built? built))
            {
                (TrackWorld world, Circuit circuit) = Named(name);
                List<RoadRibbon> ribbons = RoadLaying.Ribbons(circuit, TrackWorld.DirOf, world.RadiusM, world.HeightAt, TrackWorld.LiftM, 2.0);
                Laid[name] = built = new Built([.. ribbons.Select(r => (r, RoadTessellation.Mesh(r), RoadTessellation.Mesh(r, RoadDrawList.Fit)))]);
            }
            return built;
        }
    }

    public static TheoryData<string> Circuits => new(ExtremeCircuits.Names.Append("five").Append("deck").Append("ramp"));

    private static double3 Wide(float3 v) => new(v.X, v.Y, v.Z);

    // ---- cut to fit --------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Circuits))]
    public void EveryMeshFitsItsPlaceAndBetweenThemTheyAreTheSameTriangles(string name)
    {
        int slots = 0, vertices = 0, indices = 0, fullest = 0;
        double length = 0.0;
        foreach ((RoadRibbon ribbon, List<RoadMeshData> byLength, List<RoadMeshData> fitted) in Of(name).Runs)
        {
            length += ribbon.LengthM;
            Assert.Equal(byLength.Sum(m => m.AsphaltIndices), fitted.Sum(m => m.AsphaltIndices));
            Assert.Equal(byLength.Sum(m => m.EarthIndices), fitted.Sum(m => m.EarthIndices));
            Assert.Equal(byLength.Sum(m => m.DeckIndices), fitted.Sum(m => m.DeckIndices));
            Assert.Equal(byLength.Sum(m => m.Folded), fitted.Sum(m => m.Folded));

            double from = 0.0;
            foreach (RoadMeshData mesh in fitted)
            {
                Assert.InRange(mesh.Positions.Length, 3, RoadDrawList.SlotVertices);
                Assert.InRange(mesh.Indices.Length, 3, RoadDrawList.SlotIndices);
                Assert.True(mesh.ToS - mesh.FromS <= RoadDrawList.MostMeshM + RoadTessellation.MostStepM, $"a mesh of {name} is {mesh.ToS - mesh.FromS:F0} m of road");
                Assert.True(mesh.RadiusM < 0.5 * RoadDrawList.MostMeshM + 60.0, $"a mesh of {name} reaches {mesh.RadiusM:F0} m from its origin");
                Assert.Equal(mesh.Positions.Length, mesh.Places.Length);
                Near.Equal(from, mesh.FromS, 1e-6);
                from = mesh.ToS;
                slots++;
                vertices += mesh.Positions.Length;
                indices += mesh.Indices.Length;
                fullest = Math.Max(fullest, mesh.Positions.Length);
            }
            Near.Equal(ribbon.LengthM, from, 1e-6);
        }

        output.WriteLine($"{name}: {length:F0} m in {Of(name).Runs.Count} run(s), {slots} mesh(es) of {vertices} vertices and {indices} indices, the fullest {fullest}");
        Assert.InRange(slots, 1, RoadDrawList.MostSlots);

        // Where the road is dense a place is filled before the next is begun.
        if (name != "five" && name != "Speedway") Assert.True(slots <= (vertices / 1600) + Of(name).Runs.Count + 1, $"{slots} meshes for {vertices} vertices");
    }

    [Fact]
    public void WhatThePoolHoldsIsAQuarterOfTheEnginesBuffers()
    {
        Assert.Equal(0, RoadDrawList.MostSlots % RoadDrawList.BlockSlots);
        Assert.InRange(RoadDrawList.MostSlots * RoadDrawList.SlotVertices, 100_000, 120_000);
        Assert.True(RoadDrawList.MostSlots * (long)RoadDrawList.SlotIndices < 500_000);

        // Two draws a mesh at most, so every place in the pool can be drawn in one view.
        Assert.True(2 * RoadDrawList.MostSlots <= RoadDrawList.MostDraws);
        Assert.True(RoadDrawList.MostDraws < 256);
    }

    [Theory]
    [InlineData("ramp")]
    [InlineData("Grid")]
    public void DrawnTheAsphaltAndADecksSidesComeFirstAndTheEarthLast(string name)
    {
        foreach ((_, _, List<RoadMeshData> fitted) in Of(name).Runs)
        {
            foreach (RoadMeshData mesh in fitted)
            {
                int[] drawn = RoadDrawList.ByMaterial(mesh, out int road, out int earth);
                Assert.Equal(mesh.AsphaltIndices + mesh.DeckIndices, road);
                Assert.Equal(mesh.EarthIndices, earth);
                Assert.Equal(mesh.Indices.Length, road + earth);
                Assert.Equal(mesh.Indices.AsSpan(0, mesh.AsphaltIndices).ToArray(), drawn.AsSpan(0, mesh.AsphaltIndices).ToArray());
                Assert.Equal(mesh.Indices.AsSpan(mesh.AsphaltIndices + mesh.EarthIndices).ToArray(), drawn.AsSpan(mesh.AsphaltIndices, mesh.DeckIndices).ToArray());
                Assert.Equal(mesh.Indices.AsSpan(mesh.AsphaltIndices, mesh.EarthIndices).ToArray(), drawn.AsSpan(road).ToArray());
            }
        }
        if (name == "ramp") Assert.Contains(Of(name).Runs.SelectMany(r => r.Fitted), m => m.DeckIndices > 0 && m.EarthIndices > 0);
    }

    // ---- picked by distance ------------------------------------------------------------------------

    [Fact]
    public void WithPlacesForAllEveryMeshHasOneAndWithFewerTheNearestDo()
    {
        double[] range = [500.0, 20.0, 0.0, 3000.0, 20.0, 80.0];
        Assert.Equal([0, 1, 2, 3, 4, 5], RoadDrawList.Nearest(range, 6));
        Assert.Equal([0, 1, 2, 3, 4, 5], RoadDrawList.Nearest(range, 60));
        Assert.Equal([2, 1, 4], RoadDrawList.Nearest(range, 3));
        Assert.Empty(RoadDrawList.Nearest(range, 0));
        Assert.Empty(RoadDrawList.Nearest([], 4));

        Assert.Equal(0.0, RoadDrawList.Range(30.0, 50.0));
        Assert.Equal(70.0, RoadDrawList.Range(120.0, 50.0));
    }

    [Fact]
    public void AViewDrawsWhatIsInRangeAndWithinItsDrawsTheNearest()
    {
        double[] range = [500.0, 20.0, 0.0, 9000.0, 20.0, 80.0, 10.0];
        int[] draws = [2, 2, 1, 2, 0, 2, 2];
        int[] picked = new int[range.Length];

        // All that are in range and have something to draw, in their own order.
        int count = RoadDrawList.Pick(range, draws, 8000.0, 200, picked, out int left);
        Assert.Equal([0, 1, 2, 5, 6], picked[..count]);
        Assert.Equal(0, left);

        // Five draws: the nearest three take them, and the next two are left out.
        count = RoadDrawList.Pick(range, draws, 8000.0, 5, picked, out left);
        Assert.Equal([2, 6, 1], picked[..count]);
        Assert.Equal(2, left);

        // One that does not fit does not stop a nearer-than-the-rest one that does.
        count = RoadDrawList.Pick([0.0, 5.0, 9.0], [2, 2, 1], 100.0, 3, picked, out left);
        Assert.Equal([0, 2], picked[..count]);
        Assert.Equal(1, left);

        Assert.Equal(0, RoadDrawList.Pick(range, draws, 8000.0, 0, picked, out left));
        Assert.Equal(5, left);
        Assert.Equal(0, RoadDrawList.Pick([double.NaN], [2], 8000.0, 200, picked, out _));
    }

    [Fact]
    public void APlaceIsOutToOneHolderAtATimeAndComesBackToBeGivenAgain()
    {
        SlotLedger ledger = new();
        Assert.Equal(-1, ledger.Take());

        ledger.Grow(3);
        Assert.Equal((3, 3, 0), (ledger.Size, ledger.Free, ledger.Used));
        Assert.Equal([0, 1, 2], new[] { ledger.Take(), ledger.Take(), ledger.Take() });
        Assert.Equal(-1, ledger.Take());
        Assert.Equal(3, ledger.Used);

        ledger.Give(1);
        ledger.Give(1);
        ledger.Give(7);
        ledger.Give(-1);
        Assert.Equal(1, ledger.Free);
        Assert.Equal(1, ledger.Take());
        Assert.Equal(-1, ledger.Take());

        ledger.Grow(2);
        Assert.Equal([3, 4], new[] { ledger.Take(), ledger.Take() });
        for (int slot = 0; slot < 5; slot++) ledger.Give(slot);
        Assert.Equal((5, 5, 0), (ledger.Size, ledger.Free, ledger.Used));

        HashSet<int> given = [];
        for (int i = 0; i < 5; i++) Assert.True(given.Add(ledger.Take()));
    }

    // ---- one solid a run ---------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Circuits))]
    public void ASolidIsEveryTriangleOfItsRunSolidOnTheSideTheMeshFaces(string name)
    {
        foreach ((RoadRibbon ribbon, _, List<RoadMeshData> fitted) in Of(name).Runs)
        {
            RoadCollider solid = Assert.IsType<RoadCollider>(RoadCollider.Of(fitted));
            Assert.Equal(fitted.Sum(m => m.Indices.Length / 3), solid.Triangles);
            Assert.True(solid.RadiusM < (0.5 * ribbon.LengthM) + 60.0, $"{solid.RadiusM:F0} m round {ribbon.LengthM:F0} m of road");

            int t = 0;
            double furthest = 0.0;
            foreach (RoadMeshData mesh in fitted)
            {
                for (int i = 0; i < mesh.Indices.Length; i += 3, t++)
                {
                    double3 a = Wide(solid.Corners[3 * t]), b = Wide(solid.Corners[(3 * t) + 1]), c = Wide(solid.Corners[(3 * t) + 2]);
                    furthest = Math.Max(furthest, Math.Max(Vec.Len(a), Math.Max(Vec.Len(b), Vec.Len(c))));

                    // The same three places as the mesh's triangle, to what a float holds this far from the origin.
                    int[] corners = [mesh.Indices[i], mesh.Indices[i + 2], mesh.Indices[i + 1]];
                    double3[] drawn = [.. corners.Select(v => mesh.Origin + Wide(mesh.Positions[v]))];
                    double slack = 1e-5 + (solid.RadiusM * 2.5e-7);
                    Assert.True(Vec.Len(solid.Origin + a - drawn[0]) < slack && Vec.Len(solid.Origin + b - drawn[1]) < slack
                                && Vec.Len(solid.Origin + c - drawn[2]) < slack, $"triangle {t} of {name} is not where its mesh's is");

                    // What a physics engine takes for the solid side, against the way the mesh's own vertices face.
                    double3 front = Vec.Cross(c - a, b - a);
                    double3 facing = Wide(mesh.Normals[corners[0]]) + Wide(mesh.Normals[corners[1]]) + Wide(mesh.Normals[corners[2]]);
                    Assert.True(Vec.Dot(Vec.Unit(front), Vec.Unit(facing)) > 0.5, $"triangle {t} of {name} is solid from behind");
                }
            }
            Near.Equal(solid.RadiusM, furthest, 1e-3);
        }
    }

    // Edges by the floats themselves: nothing is welded, so an edge two triangles share is one only if its corners are the same to the last bit.
    private static Dictionary<((float, float, float), (float, float, float)), int> Edges(RoadCollider solid)
    {
        Dictionary<((float, float, float), (float, float, float)), int> edges = [];
        for (int t = 0; t < solid.Corners.Length; t += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                float3 p = solid.Corners[t + k], q = solid.Corners[t + ((k + 1) % 3)];
                (float, float, float) a = (p.X, p.Y, p.Z), b = (q.X, q.Y, q.Z);
                var edge = a.CompareTo(b) < 0 ? (a, b) : (b, a);
                edges[edge] = edges.GetValueOrDefault(edge) + 1;
            }
        }
        return edges;
    }

    [Fact]
    public void WhereTwoMeshesMeetTheSolidsTrianglesShareTheirCornersToTheLastBit()
    {
        // A deck all the way round is closed, so in one solid of it every edge is two triangles': none is left
        // open where one mesh ends and the next begins.
        (_, _, List<RoadMeshData> deck) = Assert.Single(Of("deck").Runs);
        Assert.True(deck.Count > 1);
        Dictionary<((float, float, float), (float, float, float)), int> edges = Edges(RoadCollider.Of(deck)!);
        Assert.True(edges.Count > 300);
        Assert.All(edges, e => Assert.True(e.Value == 2, $"an edge of the deck's solid is {e.Value} triangle(s)'"));

        // On the ground the sheet's only open edges are its two feet: where the meshes meet there are none.
        (_, _, List<RoadMeshData> ring) = Assert.Single(Of("five").Runs);
        Assert.True(ring.Count > 10);
        RoadCollider solid = RoadCollider.Of(ring)!;
        int open = Edges(solid).Count(e => e.Value == 1), stretches = ring.Sum(m => m.AsphaltIndices) / 12;
        Assert.InRange(open, 2 * stretches, (2 * stretches) + 4);
    }

    [Fact]
    public void ARunWithNoTriangleIsNoSolid()
    {
        Assert.Null(RoadCollider.Of([]));
    }
}
