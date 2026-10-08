using System.Globalization;
using System.Text;
using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

// Junctions laid as the game lays them and held to what they are said to be: one polygon of asphalt
// that every road stops at with no gap, no overlap and no step, drawn and collided with as one piece
// with the roads that meet there.
public class RoadJunctionTests
{
    private const double Hub = 0.33;

    internal sealed record Built(TrackWorld World, Circuit Circuit, RoadLaying.Network Net, RoadSurface Surface)
    {
        public RoadJunction Junction => Assert.Single(Net.Junctions);
    }

    internal static Built Build(TrackWorld world, Circuit circuit)
    {
        RoadLaying.Network net = RoadLaying.Laid(circuit, TrackWorld.DirOf, world.RadiusM, world.HeightAt, TrackWorld.LiftM, TrackWorld.SpacingM);
        return new Built(world, circuit, net, new RoadSurface(net.Ribbons));
    }

    // One road out of the point at the origin: which way, how long, how wide, how high its far end is,
    // and how far it turns there on its way to a point beyond, which is what makes it curve.
    internal readonly record struct Spoke(double BearingDeg, double LengthM = 150.0, double WidthM = 10.0, double FarHeightM = 0.0, double BendDeg = 0.0);

    // The point at the origin is 1, and each spoke's far end 2, 4, 6 and so on with the point beyond it one more.
    internal static Circuit Star(TrackWorld world, double heightM, double? radiusM, params Spoke[] spokes) => Star(world, heightM, radiusM, true, spokes);

    internal static Circuit Star(TrackWorld world, double heightM, double? radiusM, bool beyond, params Spoke[] spokes)
    {
        Circuit c = new Circuit { WidthM = 10.0 }.AddNode(0, 0, out int mid).SetHeight(mid, heightM).SetJunctionRadius(mid, radiusM);
        foreach (Spoke s in spokes)
        {
            double bearing = s.BearingDeg * Math.PI / 180.0, on = (s.BearingDeg + s.BendDeg) * Math.PI / 180.0;
            double east = s.LengthM * Math.Cos(bearing), north = s.LengthM * Math.Sin(bearing);
            c = c.Extend(mid, world.Deg(north), world.Deg(east), out int far).SetHeight(far, s.FarHeightM).SetWidth(mid, far, s.WidthM);
            if (!beyond) continue;
            c = c.Extend(far, world.Deg(north + (80.0 * Math.Sin(on))), world.Deg(east + (80.0 * Math.Cos(on))), out int past)
                 .SetHeight(past, s.FarHeightM).SetWidth(far, past, s.WidthM);
        }
        return c;
    }

    internal static Built Tee(double heightM = 0.0, Func<double, double, double>? ground = null)
    {
        TrackWorld world = TrackWorld.Earth(ground);
        return Build(world, Star(world, heightM, null, new Spoke(0.0, FarHeightM: heightM), new Spoke(180.0, FarHeightM: heightM), new Spoke(90.0, FarHeightM: heightM)));
    }

    private static double3 At(RoadJunction junction, Plan place, double aboveM = 0.0) =>
        junction.Chart.Dir(place) * (junction.Chart.RadiusM + junction.HeightAt(place) + aboveM);

    // Whether a place on the chart is on a run's asphalt, short of its ends.
    private static bool OnAsphalt(RoadRibbon ribbon, Plan place)
    {
        for (int i = 0; i < ribbon.LookupCount; i++)
        {
            if ((ribbon.LookupAt(i) - place).Len > ribbon.ReachM + (2.0 * RoadRibbon.LookupM)) continue;
            if (ribbon.Locate(place, i, out RoadRibbon.Section at, out double d, out double beyond) && !(beyond > 0.0) && Math.Abs(d) <= at.HalfWidth) return true;
        }
        return false;
    }

    private static int Answers(RoadJunction junction, Plan place) =>
        (junction.Contains(place) ? 1 : 0) + junction.Arms.Select(a => a.Ribbon!).Distinct().Count(r => OnAsphalt(r, place));

    private static RoadRibbon.Section End(RoadJunction.Arm arm) =>
        arm.AtStart ? arm.Ribbon!.At(0, 0.0) : arm.Ribbon!.At(arm.Ribbon.Line.Count - 1, arm.Ribbon.LengthM);

    private sealed record Measured(int Points, int Gaps, int Overlaps, double MouthStepM, double MouthTurnRad, double WalkStepM);

    // Everything a junction is held to that can be asked of its surface, and the worst of each.
    private static Measured Measure(Built built, RoadJunction junction, Random random, string what, double stride = 0.1)
    {
        int points = 0, gaps = 0, overlaps = 0;
        double mouthStep = 0.0, mouthTurn = 0.0;
        double reach = junction.Arms.Max(a => (a.MouthAt - junction.At).Len + a.HalfWidth) + 8.0;

        for (int k = 0; k < 200; k++)
        {
            double angle = 2.0 * Math.PI * random.NextDouble(), far = reach * Math.Sqrt(random.NextDouble());
            Plan place = junction.At + new Plan(far * Math.Cos(angle), far * Math.Sin(angle));
            int answers = Answers(junction, place);
            points++;
            if (answers > 1) overlaps++;
            if (answers != 1) continue;

            Assert.True(built.Surface.TryLocate(At(junction, place, 6.0), null, out double over, out double outM), $"{what}: no road under a place on the asphalt");
            Assert.True(outM == 0.0 && double.IsFinite(over), $"{what}: a place on the asphalt answered {outM} m out");
        }

        foreach (RoadJunction.Arm arm in junction.Arms)
        {
            RoadRibbon ribbon = arm.Ribbon!;
            RoadRibbon.Section end = End(arm);
            Plan left = end.Left, into = arm.AtStart ? -end.Heading : end.Heading;
            for (int k = -4; k <= 4; k++)
            {
                double d = 0.24 * k * end.HalfWidth;
                Plan on = end.At + (left * d);

                // Either side of the mouth, a hair and a hand from it: the junction's on one and the run's on the other.
                foreach (double off in new[] { 1e-4, 0.01, 0.3 })
                {
                    points += 2;
                    if (!junction.Contains(on + (into * off)) || OnAsphalt(ribbon, on + (into * off))) gaps++;
                    if (junction.Contains(on - (into * off)) || !OnAsphalt(ribbon, on - (into * off))) gaps++;
                    if (Answers(junction, on + (into * off)) > 1 || Answers(junction, on - (into * off)) > 1) overlaps++;
                }

                double height = end.Height + (d * end.BankTan);
                mouthStep = Math.Max(mouthStep, Math.Abs(height - junction.HeightAt(on)));
                double3 facing = ribbon.Normal(end, d, height, end.Slope + (d * end.BankRate), end.BankTan);
                mouthTurn = Math.Max(mouthTurn, Vec.AngleBetween(facing, junction.Normal(on)));
                Assert.True(Vec.AngleBetween(facing, junction.Normal(on)) < 1e-6, $"{what}: the road to {arm.Far} turns {Vec.AngleBetween(facing, junction.Normal(on)):E2} rad at its mouth, {d:F2} m across");

                // And as a wheel is told it, from one side of the mouth to the other.
                Assert.True(built.Surface.TryLocate(At(junction, on + (into * 1e-3), Hub), null, out double inside, out _, out double3? inFacing));
                Assert.True(built.Surface.TryLocate(At(junction, on - (into * 1e-3), Hub), null, out double outside, out _, out double3? outFacing));
                Assert.True(Math.Abs(inside - outside) < 2e-3 * (0.01 + Math.Abs(end.Slope) + Math.Abs(end.BankTan)), $"{what}: a wheel steps {inside - outside:E2} m over the mouth of the road to {arm.Far}");
                Assert.True(Vec.AngleBetween(inFacing!.Value, outFacing!.Value) < 1e-4, $"{what}: the surface turns {Vec.AngleBetween(inFacing.Value, outFacing.Value):E2} rad over the mouth of the road to {arm.Far}");
            }
        }
        return new Measured(points, gaps, overlaps, mouthStep, mouthTurn, Walked(built, junction, what, stride));
    }

    // A wheel rolled in along each arm's centre line, across the junction and out along each other arm,
    // its hub kept its height above whatever it was last told is under it: the most it stepped by, past its slope.
    private static double Walked(Built built, RoadJunction junction, string what, double stride)
    {
        double steepest = junction.Gradient.Len, worst = 0.0;
        foreach (RoadRibbon ribbon in junction.Arms.Select(a => a.Ribbon!).Distinct())
        {
            for (int road = 0; road < ribbon.Profile.Roads; road++) steepest = Math.Max(steepest, ribbon.Profile.Steepness(road, 0.25).SteepestGrade);
        }

        List<Plan> Out(RoadJunction.Arm arm)
        {
            List<Plan> line = [];
            double far = Math.Min(arm.MouthS + (40.0 * stride), 0.9 * arm.Out.LengthM);
            for (double s = arm.MouthS; s < far; s += stride) line.Add(arm.Centre(s));
            return line;
        }

        foreach (RoadJunction.Arm from in junction.Arms)
        {
            foreach (RoadJunction.Arm to in junction.Arms)
            {
                if (ReferenceEquals(from, to)) continue;
                List<double3> path = [];
                List<Plan> come = Out(from);
                come.Reverse();
                path.AddRange(come.Select(p => junction.Chart.Dir(p)));
                path.AddRange(junction.Across(from, to, stride).Select(Vec.Unit));
                path.AddRange(Out(to).Select(p => junction.Chart.Dir(p)));

                double surface = junction.Chart.RadiusM + junction.HeightM + 30.0;
                double? last = null;
                for (int i = 0; i < path.Count; i++)
                {
                    double rad = surface + Hub;
                    Assert.True(built.Surface.TryHeightOver(path[i] * rad, last, out double over), $"{what}: no road under a wheel going from {from.Far} to {to.Far}, {i} of {path.Count}");
                    double now = rad - over;
                    Assert.True(double.IsFinite(now), $"{what}: the surface is {now}");
                    if (i > 0) worst = Math.Max(worst, Math.Abs(now - surface) - (1.1 * steepest * stride));
                    (surface, last) = (now, Hub);
                }
            }
        }
        return worst;
    }

    // Meshes without their kerbs and barriers: the road's own solid, which is what is closed or one sheet.
    internal static List<RoadMeshData> Bare(IEnumerable<RoadMeshData> meshes) =>
        [.. meshes.Select(m => m with { Indices = m.Indices[..(m.Indices.Length - m.TrimIndices)], TrimIndices = 0 })];

    internal static List<RoadMeshData> Meshes(RoadLaying.Component component, Dictionary<RoadRibbon, List<RoadMeshData>>? runs = null)
    {
        List<RoadMeshData> meshes = [];
        foreach (RoadRibbon ribbon in component.Ribbons)
        {
            List<RoadMeshData> run = RoadTessellation.Mesh(ribbon, RoadDrawList.Fit);
            runs?.Add(ribbon, run);
            meshes.AddRange(run);
        }
        meshes.AddRange(component.Junctions.Select(RoadTessellation.Mesh));
        return meshes;
    }

    private static (float, float, float) Key(float3 p) => (p.X, p.Y, p.Z);

    // Every edge of a solid, by the floats themselves, with how many triangles go along it each way.
    private static Dictionary<((float, float, float), (float, float, float)), (int Forward, int Back)> Edges(RoadCollider solid)
    {
        Dictionary<((float, float, float), (float, float, float)), (int, int)> edges = [];
        for (int t = 0; t < solid.Corners.Length; t += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                (float, float, float) a = Key(solid.Corners[t + k]), b = Key(solid.Corners[t + ((k + 1) % 3)]);
                bool forward = a.CompareTo(b) < 0;
                var edge = forward ? (a, b) : (b, a);
                (int f, int r) = edges.GetValueOrDefault(edge);
                edges[edge] = forward ? (f + 1, r) : (f, r + 1);
            }
        }
        return edges;
    }

    // What the mesh of a junction and its arms is held to: its asphalt whole, every mouth's edge shared
    // by the junction's triangle and the run's, and the lot one solid with those corners the same to the last bit.
    private static void Meshed(Built built, RoadJunction junction, string what)
    {
        RoadLaying.Component component = RoadLaying.Components(built.Net.Ribbons).Single(c => c.Junctions.Contains(junction));
        RoadMeshData mesh = RoadTessellation.Mesh(junction);

        Assert.True(mesh.Positions.Length <= RoadDrawList.SlotVertices && mesh.Indices.Length <= RoadDrawList.SlotIndices,
            $"{what}: the junction's mesh is {mesh.Positions.Length} vertices and {mesh.Indices.Length} indices, more than a place holds");
        Assert.True(mesh.AsphaltIndices == junction.Faces.Length, $"{what}: {(junction.Faces.Length - mesh.AsphaltIndices) / 3} of the asphalt's triangles are slivers or folded");
        Assert.All(mesh.Positions, p => Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z), $"{what}: a vertex is not a number"));
        Assert.All(mesh.Normals, p => Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z), $"{what}: a facing is not a number"));

        for (int t = 0; t < mesh.Indices.Length; t += 3)
        {
            double3 a = mesh.Places[mesh.Indices[t]], b = mesh.Places[mesh.Indices[t + 1]], c = mesh.Places[mesh.Indices[t + 2]];
            double3 cross = Vec.Cross(b - a, c - a);
            Assert.True(0.5 * Vec.Len(cross) >= RoadTessellation.LeastAreaM2, $"{what}: a triangle of the junction has no area");
            float3 n = mesh.Normals[mesh.Indices[t]] + mesh.Normals[mesh.Indices[t + 1]] + mesh.Normals[mesh.Indices[t + 2]];
            Assert.True(Vec.Dot(cross, new double3(n.X, n.Y, n.Z)) > 0.0, $"{what}: a triangle of the junction faces away from its vertices");
            if (t >= mesh.AsphaltIndices) continue;

            // The asphalt is the plane itself, and nothing of it is outside the polygon a wheel is asked about.
            double3 middle = (a + b + c) / 3.0;
            Plan place = junction.Chart.Of(middle);
            Assert.True(junction.Surface(place, RoadTessellation.PlanToleranceM, out double height, out double outM) && outM == 0.0, $"{what}: a triangle of the asphalt is off the junction");
            Assert.True(Math.Abs(Vec.Len(middle) - junction.Chart.RadiusM - height) < RoadTessellation.HeightToleranceM, $"{what}: a triangle of the asphalt is off the plane");
        }

        Dictionary<RoadRibbon, List<RoadMeshData>> runs = new(ReferenceEqualityComparer.Instance);
        RoadCollider solid = Assert.IsType<RoadCollider>(RoadCollider.Of(Meshes(component, runs)));
        var edges = Edges(solid);
        foreach (RoadJunction.Arm arm in junction.Arms)
        {
            RoadMeshData run = runs[arm.Ribbon!][arm.AtStart ? 0 : ^1];
            double3[] row = arm.AtStart ? run.StartRow : run.EndRow;
            for (int k = 0; k < 2; k++)
            {
                double3 p = row[k] - solid.Origin, q = row[k + 1] - solid.Origin;
                (float, float, float) a = Key(new float3((float)p.X, (float)p.Y, (float)p.Z)), b = Key(new float3((float)q.X, (float)q.Y, (float)q.Z));
                Assert.True(edges.TryGetValue(a.CompareTo(b) < 0 ? (a, b) : (b, a), out (int Forward, int Back) shared) && shared == (1, 1),
                    $"{what}: the mouth of the road to {arm.Far} is an edge of {shared.Forward} triangle(s) one way and {shared.Back} the other, not of one each");
            }
        }
    }

    public static TheoryData<string> Shapes => new("T", "Y", "X", "five", "skew T", "wide on narrow", "curving through", "sharp fork");

    private static Built Shape(string name)
    {
        TrackWorld world = TrackWorld.Earth((e, n) => (0.02 * e) - (0.015 * n) + (0.4 * Math.Sin(e / 31.0) * Math.Cos(n / 23.0)));
        return Build(world, name switch
        {
            "T" => Star(world, 0.0, null, new Spoke(0.0), new Spoke(180.0), new Spoke(90.0, FarHeightM: 4.0)),
            "Y" => Star(world, 1.0, null, new Spoke(90.0), new Spoke(210.0, FarHeightM: 3.0), new Spoke(330.0, BendDeg: 30.0)),
            "X" => Star(world, 0.0, 9.0, new Spoke(10.0), new Spoke(190.0), new Spoke(100.0, FarHeightM: 2.0), new Spoke(280.0)),
            "five" => Star(world, 0.5, null, new Spoke(0.0), new Spoke(72.0, WidthM: 6.0), new Spoke(144.0, BendDeg: -25.0), new Spoke(216.0, WidthM: 14.0), new Spoke(288.0)),
            "skew T" => Star(world, 0.0, null, new Spoke(0.0), new Spoke(180.0), new Spoke(40.0, 200.0, 8.0, 2.0)),
            "wide on narrow" => Star(world, 0.0, 3.0, new Spoke(0.0, WidthM: 20.0), new Spoke(180.0, WidthM: 20.0), new Spoke(90.0, WidthM: 4.0), new Spoke(250.0, WidthM: 4.0)),
            "curving through" => Star(world, 0.0, null, new Spoke(15.0, BendDeg: 20.0), new Spoke(165.0, BendDeg: -20.0), new Spoke(270.0, FarHeightM: 3.0)),
            "sharp fork" => Star(world, 0.0, null, new Spoke(0.0, 300.0, 5.0), new Spoke(15.0, 300.0, 5.0), new Spoke(180.0, 300.0, 8.0)),
            _ => throw new ArgumentException(name),
        });
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void EveryRoadStopsAtItsMouthWithNoGapNoOverlapAndNoStepAndTheMeshIsOnePiece(string name)
    {
        Built built = Shape(name);
        if (built.Net.Refused.Count > 0)
        {
            Assert.Fail(string.Join("; ", built.Net.Refused));
        }
        RoadJunction junction = built.Junction;

        Measured m = Measure(built, junction, new Random(7), name);

        Assert.True(m.Gaps == 0 && m.Overlaps == 0, $"{name}: of {m.Points} places {m.Gaps} at a mouth were not the junction's on one side and the road's on the other, and {m.Overlaps} were on two surfaces");
        Assert.True(m.MouthStepM < 1e-9, $"{name}: a road is {m.MouthStepM:E2} m off the plane at its mouth");
        Assert.True(m.WalkStepM < 1e-3, $"{name}: a wheel walked through stepped {m.WalkStepM:F4} m more than the slope");
        Meshed(built, junction, name);
        Assert.Equal(name == "sharp fork" ? 1 : 0, junction.Bevels);
        Assert.Single(RoadLaying.Components(built.Net.Ribbons));
    }

    [Fact]
    public void AJunctionsPlaneIsTheRoadsThatGoesThroughAndASideRoadIsBroughtToIt()
    {
        // A through road climbing 2% to the east and a level side road from the north.
        TrackWorld world = TrackWorld.Earth();
        Circuit c = Star(world, 2.0, null, false, new Spoke(0.0, 100.0, FarHeightM: 4.0), new Spoke(180.0, 100.0, FarHeightM: 0.0), new Spoke(90.0, 100.0, FarHeightM: 2.0));
        Built built = Build(world, c);
        RoadJunction junction = built.Junction;

        Near.Equal(junction.Gradient.E, 0.02, 1e-9);
        Near.Equal(junction.Gradient.N, 0.0, 1e-9);
        Near.Equal(junction.HeightAt(junction.At), 2.0 + TrackWorld.LiftM, 1e-9);
        Assert.False(junction.Deck);

        // The through road is a ramp all the way: the junction is the same 2% it would have had with no side road.
        foreach (RoadJunction.Arm arm in junction.Arms.Where(a => a.Partner >= 0))
        {
            RoadRibbon ribbon = arm.Ribbon!;
            for (double s = 0.0; s <= 80.0; s += 1.0)
            {
                RoadRibbon.Section at = arm.AtStart ? ribbon.At(s) : ribbon.At(ribbon.LengthM - s);
                Near.Equal(Math.Abs(at.Slope), 0.02, 1e-9);
            }
        }

        // The side road arrives level along itself and leaning as the through road climbs, as wide as it is and no wider.
        RoadJunction.Arm side = junction.Arms.Single(a => a.Partner < 0);
        RoadRibbon.Section mouth = End(side);
        Near.Equal(mouth.Slope, 0.0, 1e-9);
        Near.Equal(Math.Abs(mouth.BankTan), 0.02, 1e-9);
        Near.Equal(mouth.BankRate, 0.0, 1e-12);
        Near.Equal(mouth.HalfRate, 0.0, 1e-12);

        // Banked 3 degrees through the junction, the plane leans as the through road does, and the side road climbs up it.
        Built banked = Build(world, c.SetBank(1, 2, 3.0).SetBank(1, 3, -3.0));
        Near.Equal(banked.Junction.Gradient.E, 0.02, 1e-9);
        Near.Equal(banked.Junction.Gradient.N, Math.Tan(3.0 * Math.PI / 180.0), 1e-9);
        Near.Equal(Math.Abs(End(banked.Junction.Arms.Single(a => a.Partner < 0)).Slope), Math.Tan(3.0 * Math.PI / 180.0), 1e-9);
    }

    [Fact]
    public void WithNothingGoingThroughThePlaneIsTheNearestToEveryArmsClimbAndNoSteeperThanItMayBe()
    {
        TrackWorld world = TrackWorld.Earth();
        Built gentle = Build(world, Star(world, 2.0, null, new Spoke(90.0, 100.0, FarHeightM: 4.0), new Spoke(210.0, 100.0, FarHeightM: 1.0), new Spoke(330.0, 100.0, FarHeightM: 1.0)));
        Assert.All(gentle.Junction.Arms, a => Assert.Equal(-1, a.Partner));
        Near.Equal(gentle.Junction.Gradient.E, 0.0, 1e-9);
        Near.Equal(gentle.Junction.Gradient.N, 0.02, 1e-9);

        Built steep = Build(world, Star(world, 0.0, null, new Spoke(90.0, 60.0, FarHeightM: 30.0), new Spoke(210.0, 60.0), new Spoke(330.0, 60.0)));
        Near.Equal(steep.Junction.Gradient.Len, RoadJunction.MostGrade, 1e-9);
    }

    [Fact]
    public void AJunctionIsNeverUnderTheGroundAndOnAHillsideItStandsOnFill()
    {
        // Ground rising 8% to the north under a level through road: the plane is level across, as the road is.
        Built built = Tee(0.0, TrackWorld.SideSlope(0.08));
        RoadJunction junction = built.Junction;
        Near.Equal(junction.Gradient.Len, 0.0, 1e-9);

        double least = double.PositiveInfinity, most = 0.0;
        foreach (Plan p in junction.Boundary.Append(junction.At))
        {
            double over = junction.HeightAt(p) - built.World.HeightAt(junction.Chart.Dir(p));
            (least, most) = (Math.Min(least, over), Math.Max(most, over));
        }
        Assert.InRange(least, TrackWorld.LiftM - 1e-6, TrackWorld.LiftM + 0.02);
        Assert.InRange(most, 1.0, 2.5);
        foreach (RoadJunction.Arm arm in junction.Arms)
        {
            RoadRibbon.Section mouth = End(arm);
            arm.Ribbon!.Profile.Above(mouth.S, out double above, out _, out _);
            Assert.True(above >= TrackWorld.LiftM - 1e-9, $"the road to {arm.Far} is {above:F3} m over its own ground at its mouth");
        }
    }

    [Fact]
    public void ACornerWithNoRoomIsRoundedByLessAndARoadTooShortForAMouthKeepsMostOfItself()
    {
        TrackWorld world = TrackWorld.Earth();
        Built roomy = Build(world, Star(world, 0.0, 30.0, new Spoke(0.0), new Spoke(180.0), new Spoke(90.0)));
        Near.Equal(roomy.Junction.RadiusM, 30.0, 1e-9);

        // A side road 30 m long has 13.5 m to its mouth at most: the corner is 5 m back, and 30 m of rounding does not fit.
        Built tight = Build(world, Star(world, 0.0, 30.0, new Spoke(0.0), new Spoke(180.0), new Spoke(90.0, 30.0)));
        RoadJunction junction = tight.Junction;
        RoadJunction.Arm side = junction.Arms.Single(a => a.Partner < 0);
        Assert.InRange(junction.RadiusM, 6.0, 8.0);
        Assert.InRange(side.MouthS, 13.0, RoadJunction.MostMouthShare * 30.0 + 1e-9);
        Assert.True(side.Ribbon!.LengthM >= 0.5 * 30.0);
        Measured m = Measure(tight, junction, new Random(3), "tight");
        Assert.True(m.Gaps == 0 && m.Overlaps == 0 && m.WalkStepM < 1e-3);
        Meshed(tight, junction, "tight");

        Built square = Build(world, Star(world, 0.0, 0.0, new Spoke(0.0), new Spoke(180.0), new Spoke(90.0)));
        Assert.Equal(0.0, square.Junction.RadiusM);
        Near.Equal(square.Junction.Arms.Single(a => a.Partner < 0).MouthS, 5.0 + RoadJunction.MouthPastM, 1e-6);
        Meshed(square, square.Junction, "square");
    }

    [Fact]
    public void TwoJunctionsARoadApartEachKeepToTheirOwnHalfOfIt()
    {
        // Two T junctions 24 m apart on one through road.
        TrackWorld world = TrackWorld.Earth();
        Circuit c = new Circuit { WidthM = 10.0 }.AddNode(0, world.Deg(-150.0), out int west).Extend(west, 0, world.Deg(-12.0), out int a)
            .Extend(a, 0, world.Deg(12.0), out int b).Extend(b, 0, world.Deg(150.0), out _)
            .Extend(a, world.Deg(120.0), world.Deg(-12.0), out _).Extend(b, world.Deg(-120.0), world.Deg(12.0), out _);
        Built built = Build(world, c);

        Assert.Empty(built.Net.Refused);
        Assert.Equal(2, built.Net.Junctions.Count);
        RoadRibbon between = built.Net.Ribbons.Single(r => r.StartJunction is not null && r.EndJunction is not null);
        Assert.InRange(between.LengthM, 0.1 * 24.0 - 1e-6, 24.0);
        Assert.All(built.Net.Junctions, j => Assert.True(j.RadiusM < RoadJunction.DefaultRadiusM, "the corner beside the short road was rounded in full"));
        foreach (RoadJunction junction in built.Net.Junctions)
        {
            Measured m = Measure(built, junction, new Random(11), "pair");
            Assert.True(m.Gaps == 0 && m.Overlaps == 0 && m.WalkStepM < 1e-3, $"{m}");
            Meshed(built, junction, "pair");
        }
        Assert.Single(RoadLaying.Components(built.Net.Ribbons));
    }

    [Fact]
    public void RoadsTooNearlyAlongsideToPartAreNoJunctionAndSayWhyAndAreLaidOverOneAnother()
    {
        // Two 20 m roads 10 degrees apart do not part for 114 m, and the shorter is 100 m long.
        TrackWorld world = TrackWorld.Earth();
        Built built = Build(world, Star(world, 0.0, null, new Spoke(0.0, 100.0, 20.0), new Spoke(10.0, 100.0, 20.0), new Spoke(180.0, 100.0, 20.0)));

        Assert.Empty(built.Net.Junctions);
        (int node, string why) = Assert.Single(built.Net.Refused);
        Assert.Equal(1, node);
        Assert.Contains("too nearly alongside", why);

        // As with no junction at all: one run goes through, the other ends at the point, and a wheel there is on a road.
        Assert.All(built.Net.Ribbons, r => Assert.True(r.StartJunction is null && r.EndJunction is null));
        Assert.Equal(1, built.Net.Ribbons.Count(r => r.Spans.Count(s => s.From == 1 || s.To == 1) == 2));
        foreach ((double e, double n) in new[] { (0.0, 0.0), (30.0, 0.0), (29.0, 5.0), (-30.0, 2.0) })
        {
            Assert.True(built.Surface.TryLocate(world.Dir(e, n) * (world.RadiusM + TrackWorld.LiftM + Hub), null, out double over, out double outM));
            Assert.True(outM == 0.0 && Math.Abs(over - Hub) < 0.02, $"{over:F3} m over the road at {e}, {n}");
        }
    }

    [Fact]
    public void AJunctionHighOverTheGroundIsADeckClosedAllRoundWithItsRoads()
    {
        Built built = Tee(8.0);
        RoadJunction junction = built.Junction;
        Assert.True(junction.Deck);
        Assert.All(junction.Arms, a => Assert.True(End(a).Deck));

        Measured m = Measure(built, junction, new Random(5), "deck");
        Assert.True(m.Gaps == 0 && m.Overlaps == 0 && m.WalkStepM < 1e-3, $"{m}");
        Meshed(built, junction, "deck");

        // Every edge of the one solid is two triangles', going opposite ways: nothing is open at a mouth, a side or underneath.
        RoadLaying.Component component = Assert.Single(RoadLaying.Components(built.Net.Ribbons));
        var edges = Edges(RoadCollider.Of(Bare(Meshes(component)))!);
        Assert.True(edges.Count > 500);
        Assert.All(edges, e => Assert.True(e.Value == (1, 1), $"an edge of the deck's solid is {e.Value.Forward} triangle(s) one way and {e.Value.Back} the other"));

        // Off the deck's edge beside the junction there is nothing, and under it a wheel is under a bridge.
        Plan beside = junction.At + new Plan(0.0, -6.0);
        Assert.False(built.Surface.TryHeightOver(At(junction, beside, Hub), out _));
        Assert.False(built.Surface.TryHeightOver(At(junction, junction.At, -3.0), out _));
        Assert.True(built.Surface.TryHeightOver(At(junction, junction.At, -0.2), -0.1, out double sunk));
        Near.Equal(sunk, -0.2, 1e-6);
    }

    [Fact]
    public void OnTheGroundAJunctionAndItsRoadsAreOneSheetWithNoHoleAndAVergeRoundEveryCorner()
    {
        Built built = Tee();
        RoadJunction junction = built.Junction;
        Assert.False(junction.Deck);
        RoadLaying.Component component = Assert.Single(RoadLaying.Components(built.Net.Ribbons));
        RoadMeshData mesh = RoadTessellation.Mesh(junction);
        Assert.Equal(0, mesh.Folded);
        Assert.True(mesh.EarthIndices > 0 && mesh.DeckIndices == 0);

        // The sheet's only open edges are its feet, which go once round the whole of it.
        var edges = Edges(RoadCollider.Of(Bare(Meshes(component)))!);
        Assert.All(edges, e => Assert.True(e.Value is (1, 1) or (1, 0) or (0, 1), $"an edge of the sheet is {e.Value.Forward} triangle(s) one way and {e.Value.Back} the other"));
        Dictionary<(float, float, float), List<(float, float, float)>> open = [];
        foreach (var edge in edges.Where(e => e.Value != (1, 1)).Select(e => e.Key))
        {
            if (!open.TryGetValue(edge.Item1, out var a)) open[edge.Item1] = a = [];
            if (!open.TryGetValue(edge.Item2, out var b)) open[edge.Item2] = b = [];
            a.Add(edge.Item2);
            b.Add(edge.Item1);
        }
        Assert.All(open, o => Assert.Equal(2, o.Value.Count));
        HashSet<(float, float, float)> seen = [];
        int loops = 0;
        foreach ((float, float, float) start in open.Keys)
        {
            if (!seen.Add(start)) continue;
            loops++;
            Stack<(float, float, float)> next = new([start]);
            while (next.TryPop(out var at))
            {
                foreach (var to in open[at].Where(seen.Add)) next.Push(to);
            }
        }
        Assert.Equal(1, loops);

        // A wheel coming over the verge in the corner between two roads is brought up to the asphalt, as the mesh is.
        RoadJunction.Chain chain = junction.Chains.First(c => (c.To - c.From + junction.Boundary.Length) % junction.Boundary.Length > 4);
        int middle = (chain.From + 5) % junction.Boundary.Length;
        Plan rim = junction.Boundary[middle], outward = junction.Outward[middle];
        Assert.InRange(junction.OutAt(middle), RoadRibbon.VergeM, 3.0);
        foreach (double off in new[] { 0.5, 1.4, 1.8 })
        {
            Assert.True(junction.Surface(rim + (outward * off), RoadSurface.EdgeM, out double height, out double outM));
            Near.Equal(outM, off, 3e-3);
            Near.Equal(height, junction.HeightAt(rim) - RoadRibbon.Drop(off), 2e-3);
            Assert.True(built.Surface.TryLocate(At(junction, rim + (outward * off), Hub), null, out _, out double told) && told > 0.0);
        }
        Assert.False(junction.Surface(rim + (outward * (junction.OutAt(middle) + 0.2)), RoadSurface.EdgeM, out _, out _));
        for (int v = 0; v < mesh.Positions.Length; v++)
        {
            double3 at = mesh.Places[v];
            Plan place = junction.Chart.Of(at);
            if (!junction.Surface(place, 1e-6, out double height, out _)) continue;
            Assert.True(Math.Abs(Vec.Len(at) - junction.Chart.RadiusM - height) < RoadTessellation.HeightToleranceM, $"a vertex of the junction's mesh is {Vec.Len(at) - junction.Chart.RadiusM - height:F4} m off what a wheel is told");
        }
    }

    [Fact]
    public void TwoRoadsAtAPointAreNeverAJunctionHoweverSharplyTheyMeet()
    {
        TrackWorld world = TrackWorld.Earth();
        Circuit kink = new Circuit { WidthM = 10.0 }.AddNode(0, world.Deg(-100.0), out int a).Extend(a, 0, 0, out int b)
            .Extend(b, world.Deg(100.0), 0, out _).SetCorner(b, 0.0);
        Built built = Build(world, kink);
        Assert.Empty(built.Net.Junctions);
        Assert.Empty(built.Net.Refused);
        Assert.Equal(2, Assert.Single(built.Net.Ribbons).Spans.Count);
    }

    [Fact]
    public void TheGridIsEightRunsRoundFiveJunctionsTheMiddleOneADeckAndOneSolid()
    {
        TrackWorld world = TrackWorld.Earth();
        Built built = Build(world, ExtremeCircuits.Of("Grid", world));

        Assert.Empty(built.Net.Refused);
        Assert.Equal(5, built.Net.Junctions.Count);
        Assert.Equal(4, built.Net.Junctions.Count(j => j.Arms.Count == 3));
        RoadJunction middle = built.Net.Junctions.Single(j => j.Arms.Count == 4);
        Assert.True(middle.Deck);
        Assert.All(built.Net.Junctions.Where(j => j.Arms.Count == 3), j => Assert.False(j.Deck));
        Assert.Equal(8, built.Net.Ribbons.Count);

        Random random = new(9);
        foreach (RoadJunction junction in built.Net.Junctions)
        {
            Measured m = Measure(built, junction, random, $"grid {junction.Node}");
            Assert.True(m.Gaps == 0 && m.Overlaps == 0 && m.MouthStepM < 1e-9 && m.WalkStepM < 1e-3, $"grid {junction.Node}: {m}");
            Meshed(built, junction, $"grid {junction.Node}");
        }
        Assert.Single(RoadLaying.Components(built.Net.Ribbons));
    }

    [Fact]
    public void ACircuitTwentyKilometresLongAndAllJoinedIsOneSolid()
    {
        // A ladder: two rails 5 km long 200 m apart and a rung every 500 m.
        TrackWorld world = TrackWorld.Earth((e, n) => (3.0 * Math.Sin(e / 400.0)) + (1.0 * Math.Cos(n / 90.0)));
        Circuit c = new() { WidthM = 10.0 };
        int[,] ids = new int[21, 2];
        for (int i = 0; i <= 20; i++)
        {
            for (int j = 0; j < 2; j++) c = c.AddNode(world.Deg(200.0 * j), world.Deg(500.0 * i), out ids[i, j]);
        }
        for (int i = 0; i <= 20; i++)
        {
            c = c.Connect(ids[i, 0], ids[i, 1]);
            for (int j = 0; j < 2 && i < 20; j++) c = c.Connect(ids[i, j], ids[i + 1, j]);
        }
        Built built = Build(world, c);

        Assert.Empty(built.Net.Refused);
        Assert.Equal(38, built.Net.Junctions.Count);
        double length = built.Net.Ribbons.Sum(r => r.LengthM);
        Assert.InRange(length, 22_000.0, 24_500.0);
        RoadLaying.Component component = Assert.Single(RoadLaying.Components(built.Net.Ribbons));
        RoadCollider solid = RoadCollider.Of(Meshes(component))!;
        Assert.InRange(solid.Triangles, 20_000, 120_000);
        Assert.InRange(solid.RadiusM, 5_000.0, 5_200.0);
        // Drawn, the junctions go two to a place in the pool, and with the roads' meshes it is not full.
        int junctions = RoadTessellation.Gather([.. built.Net.Junctions.Select(j => (j, RoadTessellation.Mesh(j)))], RoadDrawList.Fit).Count;
        int runs = built.Net.Ribbons.Sum(r => RoadTessellation.Mesh(r, RoadDrawList.Fit).Count);
        Assert.Equal(19, junctions);
        Assert.True(runs + junctions > RoadDrawList.MostSlots, "the ladder no longer fills the pool: say so in CLAUDE.md");
        Told($"ladder: {length:F0} m of road, {built.Net.Junctions.Count} junctions, one solid of {solid.Triangles} triangles reaching {solid.RadiusM:F0} m from its origin; "
           + $"drawn as {runs} meshes of runs and {junctions} of junctions, {built.Net.Junctions.Sum(j => RoadTessellation.Mesh(j).Positions.Length)} vertices of junction in all");
    }

    [Fact]
    public void JunctionsNearOneAnotherShareAMeshSoACircuitOfManyStillHasPlacesForItsRoads()
    {
        TrackWorld world = TrackWorld.Earth();
        Built grid = Build(world, ExtremeCircuits.Of("Grid", world));
        List<(RoadJunction, RoadMeshData)> each = [.. grid.Net.Junctions.Select(j => (j, RoadTessellation.Mesh(j)))];

        (int[] nodes, RoadMeshData all) = Assert.Single(RoadTessellation.Gather(each, RoadDrawList.Fit));
        Assert.Equal(grid.Net.Junctions.Select(j => j.Node).Order(), nodes.Order());
        Assert.True(all.Positions.Length <= RoadDrawList.SlotVertices && all.Indices.Length <= RoadDrawList.SlotIndices);
        Assert.Equal(each.Sum(e => e.Item2.Positions.Length), all.Positions.Length);
        Assert.Equal(each.Sum(e => e.Item2.AsphaltIndices), all.AsphaltIndices);
        Assert.Equal(each.Sum(e => e.Item2.EarthIndices), all.EarthIndices);
        Assert.Equal(each.Sum(e => e.Item2.DeckIndices), all.DeckIndices);
        Assert.Equal(all.Indices.Length, all.AsphaltIndices + all.EarthIndices + all.DeckIndices + all.TrimIndices);

        // The same triangles, each still of its own kind, about the one origin.
        HashSet<(double3, double3, double3, int)> Triangles(RoadMeshData mesh)
        {
            HashSet<(double3, double3, double3, int)> found = [];
            for (int t = 0; t < mesh.Indices.Length; t += 3)
            {
                int kind = t < mesh.AsphaltIndices ? 0 : t < mesh.AsphaltIndices + mesh.EarthIndices ? 1 : 2;
                found.Add((mesh.Places[mesh.Indices[t]], mesh.Places[mesh.Indices[t + 1]], mesh.Places[mesh.Indices[t + 2]], kind));
            }
            return found;
        }
        HashSet<(double3, double3, double3, int)> apart = [];
        foreach ((_, RoadMeshData mesh) in each) apart.UnionWith(Triangles(mesh));
        Assert.True(apart.SetEquals(Triangles(all)));
        for (int v = 0; v < all.Positions.Length; v++)
        {
            float3 p = all.Positions[v];
            Assert.True(Vec.Len(all.Origin + new double3(p.X, p.Y, p.Z) - all.Places[v]) < 1e-4);
        }
        int[] drawn = RoadDrawList.ByMaterial(all, out int road, out int earth);
        Assert.Equal(all.Indices.Length, drawn.Length);
        Assert.Equal(all.AsphaltIndices + all.DeckIndices, road);
        Assert.Equal(all.EarthIndices, earth);

        // Too far apart to share an origin, each has its own.
        Assert.Equal(5, RoadTessellation.Gather(each, RoadDrawList.Fit with { LengthM = 50.0 }).Count);
        Assert.Equal(5, RoadTessellation.Gather(each, RoadDrawList.Fit with { Vertices = 400 }).Count);
    }

    // ---- fuzz --------------------------------------------------------------------------------------

    private static readonly List<string> Report = [];

    private static void Told(string line)
    {
        lock (Report) Report.Add(line);
    }

    private static (TrackWorld World, Circuit Circuit, string What) Drawn(int seed)
    {
        Random random = new(seed);
        int n = 3 + random.Next(4);
        double a = 0.1 * (random.NextDouble() - 0.5), b = 0.1 * (random.NextDouble() - 0.5), bumps = 1.5 * random.NextDouble();
        TrackWorld world = TrackWorld.Earth((e, north) => (a * e) + (b * north) + (bumps * Math.Sin(e / 37.0) * Math.Cos(north / 29.0)));

        // No two arms nearer than 10 degrees; and more often than not two of them nearly opposite, as a road going through is.
        double[] gaps = [.. Enumerable.Range(0, n).Select(_ => random.NextDouble())];
        double total = gaps.Sum();
        Spoke[] spokes = new Spoke[n];
        double bearing = 360.0 * random.NextDouble();
        for (int i = 0; i < n; i++)
        {
            spokes[i] = new Spoke(bearing, 60.0 + (140.0 * random.NextDouble()), Math.Round(4.0 + (16.0 * random.NextDouble()), 1),
                                  random.Next(3) == 0 ? 0.0 : 6.0 * random.NextDouble(), random.Next(2) == 0 ? 0.0 : 80.0 * (random.NextDouble() - 0.5));
            bearing += 10.0 + ((360.0 - (10.0 * n)) * gaps[i] / total);
        }
        double height = random.Next(2) == 0 ? 0.0 : 5.0 * random.NextDouble();
        double? radius = random.Next(4) switch { 0 => null, 1 => 0.0, 2 => 12.0, _ => 30.0 * random.NextDouble() };
        Circuit c = Star(world, height, radius, spokes);
        if (random.Next(2) == 0) c = c.SetBank(1, 2, 12.0 * (random.NextDouble() - 0.5));
        if (random.Next(3) == 0) c = c.SetBank(1, 4, 12.0 * (random.NextDouble() - 0.5));
        string what = $"seed {seed}: " + string.Join(" ", spokes.Select(s => $"[{s.BearingDeg % 360.0:F0} deg {s.LengthM:F0} m w{s.WidthM:F1} h{s.FarHeightM:F1} bend {s.BendDeg:F0}]"))
                    + $" at h{height:F1} r{radius?.ToString("F1", CultureInfo.InvariantCulture) ?? "-"}";
        return (world, c, what);
    }

    [Fact]
    public void JunctionsOfThreeToSixRoadsOfAnyWidthAngleHeightAndBendHaveNoGapNoOverlapAndNoStep()
    {
        const int count = 150;
        int made = 0, refused = 0, bevels = 0, decks = 0, points = 0;
        Dictionary<string, int> reasons = [];
        double mouthStep = 0.0, mouthTurn = 0.0, walkStep = 0.0;
        for (int seed = 1; seed <= count; seed++)
        {
            (TrackWorld world, Circuit circuit, string what) = Drawn(seed);
            Built built = Build(world, circuit);

            if (built.Net.Junctions.Count == 0)
            {
                // Refused, and said: the roads are still laid, and a wheel at the point is on one.
                Assert.True(built.Net.Refused.Count == 1 && built.Net.Refused[0].Why.Length > 0, what);
                Assert.True(built.Surface.TryHeightOver(world.Dir(0.0, 0.0) * (world.RadiusM + 200.0), out double over) && double.IsFinite(over), what);
                refused++;
                string reason = built.Net.Refused[0].Why.StartsWith("the roads to") ? "too nearly alongside" : built.Net.Refused[0].Why;
                reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
                continue;
            }

            RoadJunction junction = built.Junction;
            Assert.All(junction.Arms, a => Assert.True(a.Ribbon is not null && a.MouthS <= Math.Min(RoadJunction.MostMouthShare * a.Out.LengthM, 2.0 * RoadJunction.MostMouthWidths * a.HalfWidth) + 1e-9, what));
            Measured m = Measure(built, junction, new Random(seed), what, stride: 0.3);
            Assert.True(m.Gaps == 0 && m.Overlaps == 0, $"{what}: of {m.Points} places {m.Gaps} at a mouth were not on the one surface they should be and {m.Overlaps} were on two");
            Assert.True(m.MouthStepM < 1e-9, $"{what}: a road is {m.MouthStepM:E2} m off the plane at its mouth");
            Assert.True(m.WalkStepM < 1e-3, $"{what}: a wheel walked through stepped {m.WalkStepM:F4} m more than the slope");
            Meshed(built, junction, what);

            made++;
            bevels += junction.Bevels;
            decks += junction.Deck ? 1 : 0;
            points += m.Points;
            (mouthStep, mouthTurn, walkStep) = (Math.Max(mouthStep, m.MouthStepM), Math.Max(mouthTurn, m.MouthTurnRad), Math.Max(walkStep, m.WalkStepM));
        }

        Told($"fuzz: {count} drawn, {made} junctions made and {refused} refused ({string.Join(", ", reasons.Select(r => $"{r.Value} {r.Key}"))}), {bevels} corners joined straight across, {decks} decks; {points} places asked, "
           + $"none in a gap or on two surfaces; worst at a mouth {mouthStep:E2} m in height and {mouthTurn:E2} rad in facing; worst step walked through {walkStep:E2} m past the slope");
        Assert.InRange(made, count / 2, count);
        Assert.True(refused > 0 && bevels > 0 && decks > 0, "the draw no longer has a junction of every kind in it");
    }

    // Not a test: with KSACARS_JUNCTIONS a path, what the tests above measured, for the numbers in a report.
    [Fact]
    public void Reported()
    {
        if (Environment.GetEnvironmentVariable("KSACARS_JUNCTIONS") is not { Length: > 0 } path) return;
        ACircuitTwentyKilometresLongAndAllJoinedIsOneSolid();
        JunctionsOfThreeToSixRoadsOfAnyWidthAngleHeightAndBendHaveNoGapNoOverlapAndNoStep();

        TrackWorld world = TrackWorld.Earth();
        Built grid = Build(world, ExtremeCircuits.Of("Grid", world));
        RoadCollider solid = RoadCollider.Of(Meshes(RoadLaying.Components(grid.Net.Ribbons).Single()))!;
        Told($"grid: {grid.Net.Ribbons.Sum(r => r.LengthM):F0} m of road, {grid.Net.Junctions.Count} junctions, one solid of {solid.Triangles} triangles reaching {solid.RadiusM:F0} m; "
           + $"run meshes {grid.Net.Ribbons.Sum(r => RoadTessellation.Mesh(r, RoadDrawList.Fit).Count)}, junction meshes of "
           + string.Join(", ", grid.Net.Junctions.Select(j => RoadTessellation.Mesh(j)).Select(m => $"{m.Positions.Length} v / {m.Indices.Length / 3} t")));

        StringBuilder text = new();
        lock (Report)
        {
            foreach (string line in Report.Distinct()) text.AppendLine(line);
        }
        File.WriteAllText(path, text.ToString());
    }
}
