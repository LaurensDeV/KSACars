using System.Text;
using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class RoadTessellationTests
{
    private static readonly double Degree = Math.PI / 180.0;

    private sealed record Built(TrackWorld World, List<RoadRibbon> Ribbons, RoadSurface Surface, List<(RoadRibbon Ribbon, List<RoadMeshData> Meshes)> Meshes);

    private static Built Build(TrackWorld world, Circuit circuit)
    {
        List<RoadRibbon> ribbons = RoadLaying.Ribbons(circuit, TrackWorld.DirOf, world.RadiusM, world.HeightAt, TrackWorld.LiftM, 2.0);
        return new Built(world, ribbons, new RoadSurface(ribbons), [.. ribbons.Select(r => (r, RoadTessellation.Mesh(r)))]);
    }

    private static readonly Dictionary<string, Built> Laid = [];

    private static Built Extreme(string name)
    {
        lock (Laid)
        {
            if (!Laid.TryGetValue(name, out Built? built))
            {
                TrackWorld world = TrackWorld.Earth();
                Laid[name] = built = Build(world, ExtremeCircuits.Of(name, world));
            }
            return built;
        }
    }

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

    // A road that climbs, turns both ways, leans into its bends and changes width, over ground that rolls.
    private static Built Winding()
    {
        TrackWorld world = TrackWorld.Earth((e, n) => (1.5 * Math.Sin(e / 40.0)) + (0.8 * Math.Cos(n / 25.0)) + (0.02 * e));
        Circuit c = Through(world, 10.0, false, (0, 0, 0), (120, 0, 1), (220, 60, 3), (300, 160, 3), (420, 180, 0.5), (520, 120, 0));
        return Build(world, c.SetBank(2, 1, 5.0).SetBank(3, 2, 8.0).SetBank(3, 4, 8.0).SetBank(4, 5, -6.0).SetEndWidth(3, 4, 14.0).SetEndWidth(5, 4, 7.0));
    }

    // Two straights and two half circles, five kilometres round, on ground that rolls a little.
    private static Built FiveKilometres()
    {
        TrackWorld world = TrackWorld.Earth((e, n) => (2.0 * Math.Sin(e / 180.0)) + (1.0 * Math.Cos(n / 90.0)));
        const double r = 270.0, half = 800.0;
        List<(double, double, double)> points = [(-half, -r, 0.0), (half, -r, 0.0)];
        for (int i = 1; i < 8; i++) points.Add((half + (r * Math.Cos((-Math.PI / 2.0) + (i * Math.PI / 8.0))), r * Math.Sin((-Math.PI / 2.0) + (i * Math.PI / 8.0)), 0.0));
        points.Add((half, r, 0.0));
        points.Add((-half, r, 0.0));
        for (int i = 1; i < 8; i++) points.Add((-half + (r * Math.Cos((Math.PI / 2.0) + (i * Math.PI / 8.0))), r * Math.Sin((Math.PI / 2.0) + (i * Math.PI / 8.0)), 0.0));
        return Build(world, Through(world, 12.0, true, [.. points]));
    }

    private static double3 At(RoadMeshData mesh, int vertex)
    {
        float3 p = mesh.Positions[vertex];
        return mesh.Origin + new double3(p.X, p.Y, p.Z);
    }

    private static double3 Wide(float3 v) => new(v.X, v.Y, v.Z);

    // Vertices by where they are: two meshes have each their own at the row they share, a few
    // thousandths of a millimetre apart for being floats from two origins.
    private sealed class Weld
    {
        private const double CellM = 1e-3, WithinM = 2e-4;
        private readonly Dictionary<(long, long, long), List<int>> _cells = [];
        public readonly List<double3> At = [];

        public int Of(double3 p)
        {
            long x = (long)Math.Floor(p.X / CellM), y = (long)Math.Floor(p.Y / CellM), z = (long)Math.Floor(p.Z / CellM);
            for (long i = x - 1; i <= x + 1; i++)
            {
                for (long j = y - 1; j <= y + 1; j++)
                {
                    for (long k = z - 1; k <= z + 1; k++)
                    {
                        if (!_cells.TryGetValue((i, j, k), out List<int>? there)) continue;
                        foreach (int id in there)
                        {
                            if (Vec.Len(At[id] - p) < WithinM) return id;
                        }
                    }
                }
            }
            At.Add(p);
            if (!_cells.TryGetValue((x, y, z), out List<int>? cell)) _cells[(x, y, z)] = cell = [];
            cell.Add(At.Count - 1);
            return At.Count - 1;
        }
    }

    // How many triangles each edge is an edge of.
    private static Dictionary<(int, int), int> Edges(Weld weld, IEnumerable<RoadMeshData> meshes, Func<int, bool> kinds)
    {
        Dictionary<(int, int), int> edges = [];
        foreach (RoadMeshData mesh in meshes)
        {
            foreach ((int a, int b, int c, int kind) in Triangles(mesh))
            {
                if (!kinds(kind)) continue;
                int[] corners = [weld.Of(At(mesh, a)), weld.Of(At(mesh, b)), weld.Of(At(mesh, c))];
                for (int i = 0; i < 3; i++)
                {
                    (int p, int q) = (corners[i], corners[(i + 1) % 3]);
                    (int, int) edge = p < q ? (p, q) : (q, p);
                    edges[edge] = edges.GetValueOrDefault(edge) + 1;
                }
            }
        }
        return edges;
    }

    private static IEnumerable<(int A, int B, int C, int Kind)> Triangles(RoadMeshData mesh)
    {
        // The road's own: its kerbs and barriers are last and are not the surface, nor part of what is closed.
        for (int t = 0; t < mesh.Indices.Length - mesh.TrimIndices; t += 3)
        {
            int kind = t < mesh.AsphaltIndices ? 0 : t < mesh.AsphaltIndices + mesh.EarthIndices ? 1 : 2;
            yield return (mesh.Indices[t], mesh.Indices[t + 1], mesh.Indices[t + 2], kind);
        }
    }

    public static TheoryData<string> Circuits => new(ExtremeCircuits.Names.Append("five").Append("winding"));

    private static Built Named(string name) => name == "five" ? FiveKilometres() : name == "winding" ? Winding() : Extreme(name);

    [Theory]
    [MemberData(nameof(Circuits))]
    public void EveryMeshIsWholeAndFacesTheWayItsVerticesDo(string name)
    {
        Built built = Named(name);
        foreach ((RoadRibbon ribbon, List<RoadMeshData> meshes) in built.Meshes)
        {
            Assert.Equal(Math.Max(1, (int)Math.Round(ribbon.LengthM / RoadTessellation.ChunkM)), meshes.Count);
            foreach (RoadMeshData mesh in meshes)
            {
                Assert.Equal(mesh.Positions.Length, mesh.Normals.Length);
                Assert.Equal(mesh.Positions.Length, mesh.Uvs.Length);
                Assert.Equal(mesh.Indices.Length, mesh.AsphaltIndices + mesh.EarthIndices + mesh.DeckIndices + mesh.TrimIndices);
                Assert.True(mesh.AsphaltIndices > 0 && mesh.Indices.Length % 3 == 0);
                Assert.All(mesh.Indices, i => Assert.InRange(i, 0, mesh.Positions.Length - 1));

                // A stretch of a hundred metres, about its own middle: nothing a float cannot hold to a hundredth of a millimetre.
                Assert.InRange(mesh.RadiusM, 1.0, 200.0);
                double furthest = 0.0;
                for (int i = 0; i < mesh.Positions.Length; i++)
                {
                    double3 p = Wide(mesh.Positions[i]), n = Wide(mesh.Normals[i]);
                    Assert.True(Vec.IsFinite(p) && Vec.IsFinite(n) && float.IsFinite(mesh.Uvs[i].X) && float.IsFinite(mesh.Uvs[i].Y), $"{name}: vertex {i} is not a number");
                    Assert.Equal(1.0, Vec.Len(n), 5);
                    furthest = Math.Max(furthest, Vec.Len(p));
                }
                Assert.Equal(mesh.RadiusM, furthest, 3);

                foreach ((int a, int b, int c, int kind) in Triangles(mesh))
                {
                    double3 cross = Vec.Cross(At(mesh, b) - At(mesh, a), At(mesh, c) - At(mesh, a));
                    Assert.True(0.5 * Vec.Len(cross) >= 0.99 * RoadTessellation.LeastAreaM2, $"{name}: a triangle of {0.5 * Vec.Len(cross) * 1e4:F3} cm2");
                    double3 normals = Wide(mesh.Normals[a]) + Wide(mesh.Normals[b]) + Wide(mesh.Normals[c]);
                    Assert.True(Vec.Dot(Vec.Unit(cross), normals) > 0.0, $"{name}: a triangle faces away from its vertices");

                    float2 ua = mesh.Uvs[a], ub = mesh.Uvs[b], uc = mesh.Uvs[c];
                    double texture = Math.Abs(((ub.X - ua.X) * (double)(uc.Y - ua.Y)) - ((ub.Y - ua.Y) * (double)(uc.X - ua.X)));
                    Assert.True(texture > 1e-7, $"{name}: a triangle of kind {kind} has no texture to show");
                }
            }
        }
    }

    // How far a point is, up or down, from the nearest surface of any stretch of a ribbon that passes under or over it.
    private static double Nearest(RoadRibbon ribbon, double3 at)
    {
        Plan place = ribbon.Chart.Of(at);
        double nearest = double.PositiveInfinity;
        for (int i = 0; i < ribbon.LookupCount; i++)
        {
            if ((ribbon.LookupAt(i) - place).Len > ribbon.ReachM + 6.0) continue;
            if (!ribbon.Locate(place, i, out RoadRibbon.Section on, out double d, out double beyond)) continue;
            if (!ribbon.Surface(on, d, beyond, out double surface, out _, out _, out _)) continue;
            nearest = Math.Min(nearest, Math.Abs(Vec.Len(at) - ribbon.Chart.RadiusM - surface));
        }
        return nearest;
    }

    // The place along and across a ribbon that a point is nearest the asphalt of, whether or not there is surface there.
    private static (RoadRibbon.Section At, double D) Foot(RoadRibbon ribbon, double3 at)
    {
        Plan place = ribbon.Chart.Of(at);
        (RoadRibbon.Section At, double D) best = default;
        double nearest = double.PositiveInfinity;
        for (int i = 0; i < ribbon.LookupCount; i++)
        {
            // No road here is more than 14 m wide.
            if ((ribbon.LookupAt(i) - place).Len > 12.0) continue;
            if (!ribbon.Locate(place, i, out RoadRibbon.Section on, out double d, out _)) continue;
            double across = Math.Clamp(d, -on.HalfWidth, on.HalfWidth);
            double off = Math.Abs(Vec.Len(at) - ribbon.Chart.RadiusM - on.Height - (across * on.BankTan)) + Math.Abs(d - across);
            if (off < nearest) (nearest, best) = (off, (on, d));
        }
        Assert.True(nearest < 10.0, $"{nearest} m from any road");
        return best;
    }

    // The asphalt is a road's width of triangles and nothing else: across it and along it, through every
    // mesh's border and round to the start of a closed run, each edge between two triangles is one edge.
    [Theory]
    [InlineData("Speedway")]
    [InlineData("Coaster")]
    [InlineData("Spiral")]
    [InlineData("Eight")]
    [InlineData("winding")]
    public void TheAsphaltHasNoHoleOrSeamAndItsBorderIsTheRoadsEdge(string name)
    {
        Built built = Named(name);
        (RoadRibbon ribbon, List<RoadMeshData> meshes) = Assert.Single(built.Meshes);

        Weld weld = new();
        int border = 0;
        double furthest = 0.0;
        foreach (((int p, int q), int count) in Edges(weld, meshes, kind => kind == 0))
        {
            Assert.True(count <= 2, $"an edge of the asphalt is shared by {count} triangles");
            if (count == 2) continue;
            border++;
            bool along = true;
            foreach (double3 end in new[] { weld.At[p], weld.At[q] })
            {
                (RoadRibbon.Section at, double d) = Foot(ribbon, end);
                bool edge = Math.Abs(Math.Abs(d) - at.HalfWidth) < 1e-3, runEnd = !ribbon.Closed && (at.S < 1e-3 || at.S > ribbon.LengthM - 1e-3);
                Assert.True(edge || runEnd, $"the asphalt has an open edge {d:F3} m across, {at.S:F2} m along");
                along &= edge && !runEnd;
            }

            // Between two rows the mesh's edge is straight and the road's is not: no further apart than is allowed.
            if (!along) continue;
            (RoadRibbon.Section middle, double across) = Foot(ribbon, (weld.At[p] + weld.At[q]) * 0.5);
            furthest = Math.Max(furthest, Math.Abs(Math.Abs(across) - middle.HalfWidth));
        }
        Assert.True(furthest <= RoadTessellation.PlanToleranceM, $"{name}: the asphalt's edge is {furthest * 1000.0:F2} mm from the road's");
        Assert.True(name is "Speedway" or "winding" ? furthest > 1e-4 : furthest >= 0.0, $"{name}: {furthest * 1000.0:F3} mm");
        Assert.True(border > 100);
    }

    [Fact]
    public void MeshesThatMeetShareTheirRowToTheLastBit()
    {
        foreach (string name in new[] { "Speedway", "Coaster", "five" })
        {
            (RoadRibbon ribbon, List<RoadMeshData> meshes) = Assert.Single(Named(name).Meshes);
            Assert.True(meshes.Count > 5);
            for (int i = 0; i < meshes.Count; i++)
            {
                if (i + 1 == meshes.Count && !ribbon.Closed) break;
                RoadMeshData here = meshes[i], next = meshes[(i + 1) % meshes.Count];
                Assert.Equal(here.ToS, next.FromS == 0.0 && i + 1 == meshes.Count ? ribbon.LengthM : next.FromS);
                for (int k = 0; k < 3; k++)
                {
                    Assert.True(here.EndRow[k].Equals(next.StartRow[k]), $"{name}: meshes {i} and {i + 1} do not meet at one row");
                }
            }
        }
    }

    // A deck is closed all round: its top, its sides, its underside and both its ends, with no edge that is only one triangle's.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ADeckIsClosedAllRound(bool ring)
    {
        TrackWorld world = TrackWorld.Earth();
        Circuit c = ring
            ? Through(world, 8.0, true, (0, 0, 9), (150, 0, 9), (150, 150, 9), (0, 150, 9))
            : Through(world, 8.0, false, (0, 0, 9), (150, 20, 9), (260, 120, 9));
        (RoadRibbon ribbon, List<RoadMeshData> meshes) = Assert.Single(Build(world, c).Meshes);
        Assert.True(ribbon.At(10.0).Deck);

        Assert.All(meshes, mesh =>
        {
            Assert.Equal(0, mesh.EarthIndices);
            Assert.True(mesh.DeckIndices > 0);
        });
        Dictionary<(int, int), int> edges = Edges(new Weld(), meshes, _ => true);
        Assert.True(edges.Count > 300);
        Assert.All(edges, e => Assert.True(e.Value == 2, $"an edge of the deck is shared by {e.Value} triangles"));

        // Half a metre under its top, straight down.
        RoadMeshData first = meshes[0];
        double lowest = Enumerable.Range(0, first.Positions.Length).Min(i => Vec.Len(At(first, i)));
        Near.Equal(world.RadiusM + TrackWorld.LiftM + 9.0 - RoadRibbon.DeckThickM, lowest, 1e-3);
    }

    // Every triangle, at its middle and at the middle of each of its edges, against the surface it is of.
    [Theory]
    [InlineData("Speedway")]
    [InlineData("Coaster")]
    [InlineData("Spiral")]
    [InlineData("Hairpins")]
    [InlineData("winding")]
    [InlineData("five")]
    public void NoTriangleIsFurtherFromTheSurfaceThanItIsAllowed(string name)
    {
        Built built = Named(name);
        double worstHeight = 0.0, worstEarth = 0.0;
        int asked = 0, earth = 0, pastFoot = 0;

        // Off the asphalt a place that is out to the side by what is allowed is also off in height by the bank's slope times that.
        double allowed = RoadTessellation.HeightToleranceM + (RoadRibbon.BankSlope * RoadTessellation.PlanToleranceM);
        string worstAt = "", worstEarthAt = "";
        TrackWorld world = built.World;
        foreach ((RoadRibbon ribbon, List<RoadMeshData> meshes) in built.Meshes)
        {
            Assert.Empty(ribbon.TooTight());
            foreach (RoadMeshData mesh in meshes)
            {
                Assert.Equal(0, mesh.Folded);
                int skip = name is "five" or "Speedway" or "Spiral" or "Hairpins" ? 3 : 1, t = 0;
                foreach ((int a, int b, int c, int kind) in Triangles(mesh))
                {
                    if (kind == 2 || t++ % skip != 0) continue;
                    double3 pa = At(mesh, a), pb = At(mesh, b), pc = At(mesh, c);

                    // Not an end's flat face, which is upright and has no height over anything.
                    if (kind == 1 && Vec.Dot(Vec.Unit(Vec.Cross(pb - pa, pc - pa)), Vec.Unit(pa)) < 0.5) continue;
                    double3 middle = (pa + pb + pc) * (1.0 / 3.0);
                    foreach (double3 p in new[] { middle, (pa + pb) * 0.5, (pb + pc) * 0.5, (pc + pa) * 0.5 })
                    {
                        // Asked as a wheel would, from a hand above it. The middle of an edge along a deck's side or a
                        // bank's foot may be a hair past where anything is: no further than is allowed to the side,
                        // which a step that far back towards the triangle's middle shows.
                        double3 up = Vec.Unit(p) * 0.1;
                        if (!built.Surface.TryLocate(p + up, null, out double over, out double outM))
                        {
                            bool found = built.Surface.TryLocate(p + up + (Vec.Unit(middle - p) * RoadTessellation.PlanToleranceM), null, out over, out outM);

                            // A bank's foot is where the ground was read to be, every two metres, and the mesh's is straight
                            // between two rows: under the ground either way, and not where the surface is held to anything.
                            if (!found && kind == 1)
                            {
                                pastFoot++;
                                continue;
                            }
                            Assert.True(found, $"{name}: a triangle of the asphalt is over nothing");
                            outM = 0.0;
                        }

                        double off = Math.Abs(over - 0.1);
                        if (kind == 0)
                        {
                            // Over a road that passes underneath, a wheel a hair past a deck's edge is told the road below.
                            if (off > RoadTessellation.HeightToleranceM)
                            {
                                off = Math.Min(Nearest(ribbon, p), Nearest(ribbon, p + (Vec.Unit(middle - p) * RoadTessellation.PlanToleranceM)));
                            }
                            asked++;
                            if (off > worstHeight) worstAt = $"{ribbon.Chart.Of(p)} in the mesh from {mesh.FromS:F1} to {mesh.ToS:F1} m";
                            worstHeight = Math.Max(worstHeight, off);
                        }
                        else if (outM > 0.0)
                        {
                            // Where two stretches of road pass close by, the bank of one runs under the verge of the
                            // other, and a wheel is told the higher: the triangle is held to the stretch it is of.
                            if (off > allowed) off = Nearest(ribbon, p);
                            earth++;
                            if (off > worstEarth) worstEarthAt = $"{world.Flatten(p)}, {outM:F3} m out, {over - 0.1:F3} m over, in the mesh from {mesh.FromS:F1} to {mesh.ToS:F1} m";
                            worstEarth = Math.Max(worstEarth, off);
                        }
                    }
                }
            }
        }

        // Fewer of the earth than there were: round a bend the verge is a kerb, which is not asked here.
        Assert.True(asked > 5000 && earth > 1500 && pastFoot < earth / 10, $"{asked} places on the asphalt, {earth} off it and {pastFoot} past the bank's foot");
        Assert.True(worstHeight <= RoadTessellation.HeightToleranceM, $"{name}: the asphalt's triangles are {worstHeight * 1000.0:F2} mm off the surface, at {worstAt}");
        Assert.True(worstEarth <= allowed, $"{name}: the verge's and the bank's triangles are {worstEarth * 1000.0:F2} mm off the surface, at {worstEarthAt}");
    }

    [Theory]
    [InlineData("Speedway")]
    [InlineData("Coaster")]
    [InlineData("Spiral")]
    [InlineData("Hairpins")]
    [InlineData("Eight")]
    [InlineData("winding")]
    public void FromOneRowToTheNextTheRoadFacesNoMoreThanADegreeAnotherWay(string name)
    {
        double along = 0.0, corners = 0.0;
        foreach ((RoadRibbon ribbon, List<RoadMeshData> meshes) in Named(name).Meshes)
        {
            RoadTessellation.Layout layout = RoadTessellation.Stations(ribbon);
            double3 Facing(RoadRibbon.Section at, double share)
            {
                double d = share * at.HalfWidth;
                return ribbon.Normal(at, d, at.Height + (d * at.BankTan), at.Slope + (d * at.BankRate), at.BankTan);
            }
            foreach ((int from, int to) in layout.Stretches)
            {
                foreach (double share in new[] { -1.0, 0.0, 1.0 })
                {
                    along = Math.Max(along, Vec.AngleBetween(Facing(layout.Stations[from].At, share), Facing(layout.Stations[to].At, share)));
                }
            }

            foreach (RoadMeshData mesh in meshes)
            {
                foreach ((int a, int b, int c, int kind) in Triangles(mesh))
                {
                    if (kind != 0) continue;
                    double3 na = Wide(mesh.Normals[a]), nb = Wide(mesh.Normals[b]), nc = Wide(mesh.Normals[c]);
                    corners = Math.Max(corners, Math.Max(Vec.AngleBetween(na, nb), Math.Max(Vec.AngleBetween(nb, nc), Vec.AngleBetween(nc, na))));
                }
            }
        }

        // Along the road, a degree, and a hair for the crease at a point of the circuit. Across it a road
        // that climbs round a bend is steeper along its inside edge than along its middle, and with a row
        // of three vertices that is what two corners of a triangle differ by: on the spiral, a degree more.
        Assert.True(along <= 1.05 * Degree, $"{name}: {along / Degree:F3} deg from one row to the next");
        Assert.True(corners <= 2.5 * Degree, $"{name}: {corners / Degree:F3} deg between two corners of a triangle");
    }

    [Fact]
    public void RowsAreWhereTheRoadsShapeNeedsThemAndAtEveryPointAndBorder()
    {
        // Straight and level: as far apart as rows go.
        TrackWorld flat = TrackWorld.Earth();
        (RoadRibbon straight, _) = Assert.Single(Build(flat, Through(flat, 10.0, false, (0, 0, 0), (1000, 0, 0))).Meshes);
        RoadTessellation.Layout rows = RoadTessellation.Stations(straight);
        Assert.Equal(101, rows.Stations.Length);
        Assert.All(rows.Stretches, s => Near.Equal(RoadTessellation.MostStepM, rows.Stations[s.To].S - rows.Stations[s.From].S, 0.01));

        foreach (string name in new[] { "Speedway", "Coaster", "winding", "five" })
        {
            (RoadRibbon ribbon, List<RoadMeshData> meshes) = Assert.Single(Named(name).Meshes);
            RoadTessellation.Layout layout = RoadTessellation.Stations(ribbon);
            double[] at = [.. layout.Stations.Select(s => s.S)];

            foreach (RoadRibbon.Span span in ribbon.Spans) Assert.Contains(at, s => Math.Abs(s - span.FromS) < 1e-6);
            for (int k = 1; k < meshes.Count; k++) Assert.Contains(at, s => Math.Abs(s - (ribbon.LengthM * k / meshes.Count)) < 1e-6);
            for (int i = 1; i < ribbon.SideStretches; i++)
            {
                if (ribbon.DeckOver(i - 1) != ribbon.DeckOver(i)) Assert.Contains(at, s => Math.Abs(s - (i * ribbon.SideStepM)) < 1e-6);
            }

            foreach ((int from, int to) in layout.Stretches)
            {
                double step = (to == 0 ? ribbon.LengthM : at[to]) - at[from];
                Assert.InRange(step, 0.2, RoadTessellation.MostStepM + 0.01);

                // No closer than the least but to reach a row that has to be there, and then by no less than half.
                RoadRibbon.Section middle = ribbon.At(at[from] + (0.5 * step));
                double edge = Math.Abs(middle.Curvature) / (1.0 - (Math.Abs(middle.Curvature) * middle.HalfWidth));
                Assert.True(step * edge <= 1.5 * Degree, $"{name}: the road's edge turns {step * edge / Degree:F2} deg in a stretch {at[from]:F1} m along");
            }
        }
    }

    // What a mesh costs. Where a road turns in a small radius its rows are half a metre apart, a degree of
    // turn at its inside edge being less than that; on a circuit a car can be driven fast round they are few.
    [Fact]
    public void AFiveKilometreCircuitIsFifteenThousandVerticesAndFewerRowsThanOneEveryTwoMetres()
    {
        (int Stations, int Vertices, int Indices, double Length) Count(Built built)
        {
            (int stations, int vertices, int indices, double length) = (0, 0, 0, 0.0);
            foreach ((RoadRibbon ribbon, List<RoadMeshData> meshes) in built.Meshes)
            {
                stations += RoadTessellation.Stations(ribbon).Stations.Length;
                vertices += meshes.Sum(m => m.Positions.Length);
                indices += meshes.Sum(m => m.Indices.Length);
                length += ribbon.LengthM;
            }
            return (stations, vertices, indices, length);
        }

        (int stations, int vertices, int indices, double length) = Count(FiveKilometres());
        Assert.InRange(length, 4800.0, 5000.0);
        Assert.InRange(stations, 1000, 1600);
        Assert.InRange(vertices, 12_000, 18_000);
        Assert.InRange(indices, 36_000, 60_000);

        Dictionary<string, (int Rows, int EveryTwoMetres)> rows = [];
        foreach (string name in ExtremeCircuits.Names)
        {
            (int count, _, _, double metres) = Count(Extreme(name));
            rows[name] = (count, (int)(metres / 2.0));
        }
        Assert.True(rows["Speedway"].Rows < rows["Speedway"].EveryTwoMetres);
        Assert.True(rows["Kinks"].Rows < rows["Kinks"].EveryTwoMetres);
        Assert.All(rows, r => Assert.True(r.Value.Rows < 3 * r.Value.EveryTwoMetres, $"{r.Key}: {r.Value.Rows} rows"));
    }

    [Fact]
    public void RoundAKinkTheRoadIsAFanAndWhatFoldsOnItsInsideIsLeftOutAndCounted()
    {
        Built kinks = Extreme("Kinks");
        (RoadRibbon ribbon, List<RoadMeshData> meshes) = Assert.Single(kinks.Meshes);
        Assert.Equal(5, ribbon.TooTight().Count);
        Assert.True(meshes.Sum(m => m.Folded) > 0);

        // Out by the edge on the outside of each kink there is asphalt, where a wheel is told there is.
        RoadTessellation.Layout layout = RoadTessellation.Stations(ribbon);
        Assert.True(layout.Stations.GroupBy(s => s.S).Count(g => g.Count() > 3) == 5);
        foreach (RoadLine.Tight kink in ribbon.TooTight())
        {
            RoadRibbon.Section before = ribbon.At(kink.FromS - 0.01), after = ribbon.At(kink.FromS + 0.01);
            double turn = Plan.Cross(before.Heading, after.Heading);
            Plan out_ = ((before.Left + after.Left) * -Math.Sign(turn)).Unit() * (before.HalfWidth - 0.05);
            double3 corner = ribbon.Chart.Dir(ribbon.At(kink.FromS).At + out_) * (ribbon.Chart.RadiusM + before.Height);

            Assert.True(kinks.Surface.TryLocate(corner + (Vec.Unit(corner) * 0.3), null, out double over, out double outM));
            Near.Equal(0.3, over, 1e-3);
            Assert.Equal(0.0, outM);
            Assert.Contains(meshes, mesh => Triangles(mesh).Any(t => t.Kind == 0 && Inside(corner, At(mesh, t.A), At(mesh, t.B), At(mesh, t.C))));
        }
    }

    // Whether a place is over a triangle, seen from above.
    private static bool Inside(double3 p, double3 a, double3 b, double3 c)
    {
        double3 up = Vec.Unit(p);
        double ab = Vec.Dot(Vec.Cross(b - a, p - a), up), bc = Vec.Dot(Vec.Cross(c - b, p - b), up), ca = Vec.Dot(Vec.Cross(a - c, p - c), up);
        return (ab >= -1e-6 && bc >= -1e-6 && ca >= -1e-6) || (ab <= 1e-6 && bc <= 1e-6 && ca <= 1e-6);
    }

    [Fact]
    public void PastTheEndOfARoadOnTheGroundTheMeshFallsAwayAsAWheelIsToldItDoes()
    {
        TrackWorld world = TrackWorld.Earth();
        Built built = Build(world, Through(world, 10.0, false, (0, 0, 1), (200, 0, 1)));
        (RoadRibbon ribbon, List<RoadMeshData> meshes) = Assert.Single(built.Meshes);
        double reach = Math.Max(ribbon.At(0.0).ToeLeft, ribbon.At(0.0).ToeRight);
        Assert.InRange(reach, 4.0, 4.5);

        int past = 0;
        foreach (RoadMeshData mesh in meshes)
        {
            foreach ((int a, int b, int c, int kind) in Triangles(mesh))
            {
                double3 middle = (At(mesh, a) + At(mesh, b) + At(mesh, c)) * (1.0 / 3.0);
                (double east, _) = world.Flatten(middle);
                if (east is > 0.0 and < 200.0) continue;
                Assert.Equal(1, kind);
                Assert.InRange(east, -reach - 1e-3, 200.0 + reach + 1e-3);
                past++;

                Assert.True(built.Surface.TryLocate(middle + (Vec.Unit(middle) * 0.2), null, out double over, out double outM), $"nothing {east:F2} m east");
                Near.Equal(0.2, over, 1e-4);
                Assert.True(outM > 0.0);
            }
        }
        Assert.True(past >= 20, $"{past} triangles past the ends");
    }

    // A ring that leans 25 degrees over half of itself and not at all over the rest, wide enough that
    // its high edge is a deck where it leans: so the road changes from a deck to a bank on the ground
    // while it is still leaning, and the bank's end under the deck is closed by a wall across the road.
    [Fact]
    public void NoWallOfABankedRoadsMeshStandsAboveTheRoad()
    {
        TrackWorld world = TrackWorld.Earth();
        Circuit c = AutopilotTests.Ring(120.0).Circuit with { WidthM = 14.0 };
        foreach (Circuit.Road road in c.Roads)
        {
            c = c.SetBank(road.From, road.To, road.From <= 6 ? -25.0 : 0.0).SetBank(road.To, road.From, road.To <= 6 ? -25.0 : 0.0);
        }
        RoadLaying.Network net = RoadLaying.Laid(c, TrackWorld.DirOf, world.RadiusM, world.HeightAt, TrackWorld.LiftM, TrackWorld.SpacingM);
        RoadSurface surface = new(net.Ribbons);

        int walls = 0;
        foreach (RoadRibbon ribbon in net.Ribbons)
        {
            foreach (RoadMeshData mesh in RoadTessellation.Mesh(ribbon, RoadDrawList.Fit))
            {
                for (int t = 0; t + 2 < mesh.Indices.Length - mesh.TrimIndices; t += 3)
                {
                    double3 a = mesh.Places[mesh.Indices[t]], b = mesh.Places[mesh.Indices[t + 1]], d = mesh.Places[mesh.Indices[t + 2]];
                    double3 normal = Vec.Cross(b - a, d - a);
                    if (Vec.Len(normal) < 0.5 || Math.Abs(Vec.Dot(Vec.Unit(normal), Vec.Unit(a))) > 0.3) continue;

                    // Its middle and the middle of each side: none over the road it is under.
                    foreach (double3 at in new[] { (a + b + d) / 3.0, (a + b) * 0.5, (b + d) * 0.5, (d + a) * 0.5 })
                    {
                        if (!surface.TryLocate(at + (Vec.Unit(at) * 3.0), null, out double over, out double outM) || outM > 0.0) continue;
                        walls++;
                        Assert.True(over - 3.0 < 0.01, $"a wall's triangle stands {over - 3.0:F3} m above the asphalt: corners {Vec.Len(a) - world.RadiusM:F2}, "
                                                       + $"{Vec.Len(b) - world.RadiusM:F2} and {Vec.Len(d) - world.RadiusM:F2} m up, {Vec.Len(b - a):F2}, {Vec.Len(d - b):F2} and {Vec.Len(a - d):F2} m apart");
                    }
                }
            }
        }
        Assert.True(walls > 0, "the ring has no wall under its asphalt: it does not change from a deck to a bank");
    }

    private static (TrackWorld World, List<(RoadRibbon Ribbon, List<RoadMeshData> Meshes)> Runs) Meshed(Func<TrackWorld, Circuit> draw)
    {
        TrackWorld world = TrackWorld.Earth();
        RoadLaying.Network net = RoadLaying.Laid(draw(world), TrackWorld.DirOf, world.RadiusM, world.HeightAt, TrackWorld.LiftM, TrackWorld.SpacingM);
        return (world, [.. net.Ribbons.Select(r => (r, RoadTessellation.Mesh(r, RoadDrawList.Fit)))]);
    }

    [Fact]
    public void ABendHasKerbsAndAStraightHasNone()
    {
        (_, var straight) = Meshed(w => AutopilotTests.Through(w, false, (0.0, 0.0, 0.0), (300.0, 0.0, 0.0)));
        Assert.All(straight.SelectMany(r => r.Meshes), m => Assert.Equal(0, m.TrimIndices));

        // A ring of 60 m is all bend, and each stretch of it has a kerb either side: two triangles each.
        (_, var ring) = Meshed(w => AutopilotTests.Ring(60.0).Circuit);
        int kerbs = ring.SelectMany(r => r.Meshes).Sum(m => m.TrimIndices), asphalt = ring.SelectMany(r => r.Meshes).Sum(m => m.AsphaltIndices);
        Assert.Equal(asphalt, kerbs);

        // And one of 300 m is no bend to slow for.
        (_, var wide) = Meshed(w => AutopilotTests.Ring(300.0).Circuit);
        Assert.All(wide.SelectMany(r => r.Meshes), m => Assert.Equal(0, m.TrimIndices));
    }

    [Fact]
    public void ADeckHasABarrierOutsideEachEdgeAndTheGroundHasNone()
    {
        (TrackWorld world, var runs) = Meshed(w => AutopilotTests.Through(w, false,
            (0.0, 0.0, 0.0), (100.0, 0.0, 12.0), (300.0, 0.0, 12.0), (400.0, 0.0, 0.0)));
        (RoadRibbon ribbon, List<RoadMeshData> meshes) = Assert.Single(runs);
        RoadSurface surface = new([ribbon]);

        int corners = 0;
        double highest = 0.0;
        foreach (RoadMeshData mesh in meshes)
        {
            for (int t = mesh.Indices.Length - mesh.TrimIndices; t < mesh.Indices.Length; t++)
            {
                // No corner of it is over the asphalt, where a wheel would be: the road is as wide as it was.
                double3 at = mesh.Places[mesh.Indices[t]];
                bool over = surface.TryLocate(at + (Vec.Unit(at) * 2.0), null, out double above, out double outM);
                Assert.False(over && outM <= 0.0 && Math.Abs(world.Flatten(at).North) < (0.5 * AutopilotTests.Width) - 0.01, $"a barrier's corner is over the road, {world.Flatten(at)}");
                corners++;
                highest = Math.Max(highest, Vec.Len(at) - world.RadiusM);
            }
        }
        Assert.True(corners > 200, $"{corners}");
        Assert.InRange(highest, 12.0 + RoadTessellation.BarrierHighM - 0.1, 12.0 + RoadTessellation.BarrierHighM + 0.2);

        // And it is in what the physics is given, so it stops a car.
        Assert.Equal(meshes.Sum(m => m.Indices.Length / 3), RoadCollider.Of(meshes)!.Triangles);
    }
}
