using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class CircuitTests
{
    private const double Radius = 6_371_000.0;

    private static double3 DirOf(double latDeg, double lonDeg)
    {
        double lat = latDeg * Math.PI / 180.0, lon = lonDeg * Math.PI / 180.0;
        return new double3(Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));
    }

    // Degrees of latitude or, at the equator, longitude that are so many metres.
    private static double Deg(double metres) => metres / Radius * 180.0 / Math.PI;

    private static double3 Heading(double3[] line, Index from, Index to) => Vec.Unit(line[to] - line[from]);

    private static double FromChord(double3 p, double3 a, double3 b)
    {
        double3 ab = b - a;
        double t = Math.Clamp(Vec.Dot(p - a, ab) / Vec.Len2(ab), 0.0, 1.0);
        return Vec.Len(p - (a + (ab * t))) * Radius;
    }

    [Fact]
    public void ACircuitComesBackFromItsFileAsItWentIn()
    {
        Circuit c = new Circuit { Name = "Cape Test Track", Body = "Earth", WidthM = 12.0 }
            .AddNode(0.0, 0.0, out int a).Extend(a, 0.0, Deg(300), out int b).Extend(b, Deg(200), Deg(300), out int d)
            .SetCorner(b, 0.4).SetWidth(a, b, 8.0).SetHandle(b, d, new Circuit.Place(Deg(50), Deg(320)));

        Circuit? back = Circuit.FromJson(c.ToJson(), out string why, out int dropped);

        Assert.NotNull(back);
        Assert.Equal("", why);
        Assert.Equal(0, dropped);
        Assert.Equal(c.ToJson(), back.ToJson());
        Assert.Equal(8.0, back.WidthOf(back.Roads[0]));
        Assert.Equal(12.0, back.WidthOf(back.Roads[1]));
        Assert.Equal(new Circuit.Place(Deg(50), Deg(320)), back.Roads[1].FromHandle);
    }

    [Fact]
    public void ACircuitFromANewerBuildIsRefusedAndABrokenRoadIsLeftOut()
    {
        Assert.Null(Circuit.FromJson("""{"version": 99, "nodes": [], "roads": []}""", out string why, out _));
        Assert.Contains("newer", why);
        Assert.Null(Circuit.FromJson("not a circuit", out why, out _));
        Assert.NotEqual("", why);

        Circuit? read = Circuit.FromJson("""
            {"version": 1, "name": "x", "body": "Luna",
             "nodes": [{"id": 1, "lat_deg": 0, "lon_deg": 0}, {"id": 2, "lat_deg": 0, "lon_deg": 0.01, "corner": 9}],
             "roads": [{"from": 1, "to": 2}, {"from": 2, "to": 1}, {"from": 1, "to": 1}, {"from": 1, "to": 7}]}
            """, out why, out int dropped);

        Assert.NotNull(read);
        Assert.Single(read.Roads);
        Assert.Equal(3, dropped);
        Assert.Equal(1.0, read.Nodes[0].Corner);
        Assert.Equal(Circuit.MaxCorner, read.Nodes[1].Corner);
    }

    [Fact]
    public void AnEditAnswersANewCircuitAndLeavesTheOldOneAsItWas()
    {
        Circuit start = new Circuit().AddNode(0, 0, out int a).Extend(a, 0, 0.01, out int b).Extend(b, 0.01, 0.01, out int c);
        Circuit joined = start.Connect(c, a);

        Assert.Equal(2, start.Roads.Count);
        Assert.Equal(3, joined.Roads.Count);
        Assert.Same(joined, joined.Connect(a, c));
        Assert.Same(joined, joined.Connect(a, a));

        Circuit without = joined.RemoveNode(b);
        Assert.Equal(2, without.Nodes.Count);
        Assert.Single(without.Roads);
        Assert.True(without.Roads[0].Joins(a, c));
    }

    [Fact]
    public void SplittingARoadKeepsItsFarHandlesAndMovingAPointTakesItsHandlesAlong()
    {
        Circuit.Place pull = new(0.002, 0.001);
        Circuit c = new Circuit().AddNode(0, 0, out int a).Extend(a, 0, 0.01, out int b).SetHandle(a, b, pull).SetWidth(a, b, 6.0);

        Circuit split = c.Split(a, b, 0.001, 0.005, out int mid);
        Assert.Equal(2, split.Roads.Count);
        Assert.Equal(pull, split.Roads.Single(r => r.Joins(a, mid)).FromHandle);
        Assert.All(split.Roads, r => Assert.Equal(6.0, r.WidthM));
        Assert.DoesNotContain(split.Roads, r => r.Joins(a, b));

        Circuit moved = c.MoveNode(a, 0.01, 0.02);
        Assert.Equal(new Circuit.Place(0.012, 0.021), moved.Roads[0].FromHandle);
    }

    [Fact]
    public void ANameIsMadeSafeForAFile()
    {
        Assert.Equal("Luna Crater Ring.json", Circuit.FileName("  Luna Crater Ring "));
        Assert.Equal("a___b_c.json", Circuit.FileName("a/..b:c"));
        Assert.Equal("circuit.json", Circuit.FileName(""));
    }

    [Fact]
    public void ARoadThroughPointsInALineIsStraight()
    {
        Circuit c = new Circuit().AddNode(0, 0, out int a).Extend(a, 0, Deg(200), out int b).Extend(b, 0, Deg(500), out _);

        foreach (RoadLayout.Stretch s in RoadLayout.Of(c, DirOf, Radius, 2.0))
        {
            Assert.All(s.Line, p => Assert.True(Math.Abs(p.Z) * Radius < 1e-6, "left the equator"));
            for (int i = 1; i < s.Line.Length; i++)
            {
                Assert.True(Vec.Len(s.Line[i] - s.Line[i - 1]) * Radius < 2.6);
            }
        }
    }

    [Fact]
    public void ACornerOfNoStrengthIsAKinkAndOneOfFullStrengthIsACurveWithNoKinkInIt()
    {
        Circuit bend = new Circuit().AddNode(0, 0, out int a).Extend(a, 0, Deg(300), out int b).Extend(b, Deg(300), Deg(300), out int d);

        List<RoadLayout.Stretch> kinked = RoadLayout.Of(bend.SetCorner(a, 0).SetCorner(b, 0).SetCorner(d, 0), DirOf, Radius, 2.0);
        // A straight road stands a couple of millimetres off its chord, by the body's own curve.
        Assert.All(kinked[0].Line, p => Assert.True(FromChord(p, DirOf(0, 0), DirOf(0, Deg(300))) < 5e-3));
        Assert.True(Vec.Dot(Heading(kinked[0].Line, ^2, ^1), Heading(kinked[1].Line, 0, 1)) < 0.1);

        List<RoadLayout.Stretch> curved = RoadLayout.Of(bend, DirOf, Radius, 2.0);
        Assert.True(Vec.Dot(Heading(curved[0].Line, ^2, ^1), Heading(curved[1].Line, 0, 1)) > 0.999);
        Assert.True(curved[0].Line.Max(p => FromChord(p, DirOf(0, 0), DirOf(0, Deg(300)))) > 5.0, "the road did not swing wide of the corner");
    }

    [Fact]
    public void AtACrossroadsBothRoadsGoStraightThrough()
    {
        Circuit c = new Circuit().AddNode(0, 0, out int mid)
            .Extend(mid, 0, Deg(200), out _).Extend(mid, 0, -Deg(200), out _)
            .Extend(mid, Deg(200), 0, out _).Extend(mid, -Deg(200), 0, out _);

        foreach (RoadLayout.Stretch s in RoadLayout.Of(c, DirOf, Radius, 2.0))
        {
            double3 far = s.Line[^1];
            Assert.All(s.Line, p => Assert.True(FromChord(p, DirOf(0, 0), far) < 1e-3, "a road bent at the crossroads"));
        }
    }

    [Fact]
    public void ASideRoadMeetsAThroughRoadSquareAndDoesNotBendIt()
    {
        // The through road runs east-west with a kink of 20 degrees at the junction; the side road comes up from the south.
        double k = Math.Tan(20.0 * Math.PI / 180.0);
        Circuit c = new Circuit().AddNode(0, 0, out int mid)
            .Extend(mid, 0, -Deg(200), out _).Extend(mid, Deg(200 * k), Deg(200), out _).Extend(mid, -Deg(200), 0, out int south);

        List<RoadLayout.Stretch> roads = RoadLayout.Of(c, DirOf, Radius, 1.0);
        Assert.True(Vec.Dot(Heading(roads[0].Line, 1, 0), Heading(roads[1].Line, 0, 1)) > 0.999, "the through road has a kink in it");

        RoadLayout.Stretch side = roads.Single(r => r.To == south);
        Assert.All(side.Line, p => Assert.True(FromChord(p, DirOf(0, 0), DirOf(-Deg(200), 0)) < 1e-3, "the side road bent"));
    }

    [Fact]
    public void AHandleSetByHandIsWhereTheRoadLeavesTowards()
    {
        Circuit c = new Circuit().AddNode(0, 0, out int a).Extend(a, 0, Deg(300), out int b)
            .SetHandle(a, b, new Circuit.Place(Deg(100), 0));

        double3[] line = RoadLayout.Of(c, DirOf, Radius, 1.0)[0].Line;

        Assert.True(Vec.Dot(Heading(line, 0, 1), new double3(0, 0, 1)) > 0.99, "the road did not leave north");
        Assert.True(Vec.Len(line[^1] - DirOf(0, Deg(300))) * Radius < 1e-6);
    }
}

public class CircuitHistoryTests
{
    [Fact]
    public void AnEditIsOneStepBackAndADragIsOneHoweverFarItWent()
    {
        Circuit empty = new();
        CircuitHistory h = new(empty);
        h.Do(h.Now.AddNode(0, 0, out int a));
        Circuit one = h.Now;

        h.BeginDrag();
        for (int i = 1; i <= 20; i++) h.Do(h.Now.MoveNode(a, i * 0.001, 0));
        h.EndDrag();
        Assert.Equal(0.02, h.Now.Nodes[0].LatDeg, 9);

        Assert.True(h.Undo());
        Assert.Same(one, h.Now);
        Assert.True(h.Undo());
        Assert.Same(empty, h.Now);
        Assert.False(h.Undo());

        Assert.True(h.Redo());
        Assert.True(h.Redo());
        Assert.Equal(0.02, h.Now.Nodes[0].LatDeg, 9);
        Assert.False(h.CanRedo);
    }

    [Fact]
    public void ANewEditForgetsWhatWasUndoneAndADragThatWentNowhereIsNoStep()
    {
        CircuitHistory h = new(new Circuit());
        h.Do(h.Now.AddNode(0, 0, out _));
        h.Do(h.Now.AddNode(1, 1, out _));
        h.Undo();
        h.Do(h.Now.AddNode(2, 2, out _));
        Assert.False(h.CanRedo);

        Circuit before = h.Now;
        h.BeginDrag();
        h.EndDrag();
        h.Undo();
        Assert.NotSame(before, h.Now);
        Assert.Single(h.Now.Nodes);

        Assert.True(h.Unsaved);
        h.MarkSaved();
        Assert.False(h.Unsaved);
    }
}

public class RoadHeightTests
{
    private static double3 DirOf(double latDeg, double lonDeg)
    {
        double lat = latDeg * Math.PI / 180.0, lon = lonDeg * Math.PI / 180.0;
        return new double3(Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));
    }

    [Fact]
    public void ARoadRampsFromOnePointsHeightToTheNextAndIsLevelAtBoth()
    {
        Circuit c = new Circuit().AddNode(0, 0, out int a).Extend(a, 0, 0.003, out int b).Extend(b, 0, 0.006, out _).SetHeight(b, 12.0);
        Assert.Equal(12.0, Circuit.FromJson(c.ToJson(), out _, out _)!.Find(b)!.HeightM);
        Assert.Equal(0.0, Circuit.FromJson("""{"nodes": [{"id": 1, "lat_deg": 0, "lon_deg": 0}], "roads": []}""", out _, out _)!.Nodes[0].HeightM);

        List<RoadLayout.Stretch> roads = RoadLayout.Of(c, DirOf, 6_371_000.0, 2.0);
        double[] up = roads[0].HeightM, down = roads[1].HeightM;

        Assert.Equal(0.0, up[0], 9);
        Assert.Equal(12.0, up[^1], 9);
        Assert.Equal(6.0, up[up.Length / 2], 0);
        Assert.True(up[1] - up[0] < 0.01 && up[^1] - up[^2] < 0.01, "the ramp does not ease into its ends");
        Assert.Equal(12.0, down[0], 9);
        Assert.Equal(0.0, down[^1], 9);
        for (int i = 1; i < up.Length; i++) Assert.True(up[i] >= up[i - 1]);
    }
}

public class GodViewTests
{
    private static readonly double3 Pole = new(0, 0, 1);
    private const double Radius = 6_371_000.0;

    [Fact]
    public void TheEyeIsItsDistanceFromTheTargetAndLooksAtIt()
    {
        GodView view = new(Vec.Unit(new double3(1, 0.3, -0.4)), 0.7, 1.0, 400.0);
        (double3 eye, double3 forward) = view.Pose(Pole, Radius);

        double3 target = view.Target * Radius;
        Assert.Equal(400.0, Vec.Len(eye - target), 6);
        Assert.True(Vec.Len(Vec.Unit(target - eye) - forward) < 1e-9);
        Assert.Equal(400.0 * Math.Sin(1.0), Vec.Dot(eye - target, view.Target), 6);
    }

    [Fact]
    public void FacingNorthAheadIsNorthAndRightIsEast()
    {
        GodView view = new(new double3(1, 0, 0), 0.0, 1.0, 100.0);
        (double3 ahead, double3 right) = view.Facing(Pole);
        Assert.True(Vec.Len(ahead - new double3(0, 0, 1)) < 1e-12);
        Assert.True(Vec.Len(right - new double3(0, 1, 0)) < 1e-12);

        GodView east = view.Turn(Math.PI / 2.0, 0.0);
        Assert.True(Vec.Len(east.Facing(Pole).Ahead - new double3(0, 1, 0)) < 1e-12);
    }

    [Fact]
    public void PanningMovesTheTargetThatFarOverTheGroundAndTurningAndZoomingStayInBounds()
    {
        GodView view = new(new double3(1, 0, 0), 0.0, 1.0, 100.0);
        GodView moved = view.Pan(Pole, Radius, 30.0, 40.0);
        Assert.Equal(50.0, Vec.Len(moved.Target - view.Target) * Radius, 3);
        Assert.Equal(1.0, Vec.Len(moved.Target), 12);

        Assert.Equal(GodView.MaxPitchRad, view.Turn(0, 9).PitchRad);
        Assert.Equal(GodView.MinPitchRad, view.Turn(0, -9).PitchRad);
        Assert.Equal(GodView.MinDistanceM, view.Zoom(100).DistanceM);
        Assert.Equal(GodView.MaxDistanceM, view.Zoom(-100).DistanceM);
        Assert.True(view.Zoom(1).DistanceM < view.DistanceM);
    }
}

public class RoadRunTests
{
    private const double Radius = 6_371_000.0;

    private static double3 DirOf(double latDeg, double lonDeg)
    {
        double lat = latDeg * Math.PI / 180.0, lon = lonDeg * Math.PI / 180.0;
        return new double3(Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));
    }

    private static double Deg(double metres) => metres / Radius * 180.0 / Math.PI;

    [Fact]
    public void ARoadThroughAJunctionIsOneRunAndTheSideRoadIsSunkWhereItMeetsIt()
    {
        Circuit c = new Circuit().AddNode(0, 0, out int mid)
            .Extend(mid, 0, -Deg(200), out int west).Extend(mid, 0, Deg(200), out _).Extend(mid, -Deg(200), 0, out _)
            .Extend(west, 0, -Deg(400), out _);

        List<RoadLayout.Run> runs = RoadLayout.Runs(c, DirOf, Radius, 2.0);

        Assert.Equal(2, runs.Count);
        RoadLayout.Run through = runs.Single(r => r.Line.Length > 250), side = runs.Single(r => r.Line.Length < 250);
        Assert.Equal(0.0, through.SinkStartM);
        Assert.Equal(0.0, through.SinkEndM);
        Assert.Equal(600.0, Vec.Len(through.Line[0] - through.Line[^1]) * Radius, 0);
        for (int i = 1; i < through.Line.Length; i++)
        {
            double step = Vec.Len(through.Line[i] - through.Line[i - 1]) * Radius;
            Assert.InRange(step, 0.5, 2.6);
        }
        Assert.True(side.SinkStartM + side.SinkEndM > 0.0, "the side road lies in the through road's plane");
        Assert.False(through.Closed);
    }

    [Fact]
    public void ARingIsOneClosedRunAndThreeRoadsMeetingAtAThirdOfATurnAreAllAtDifferentDepths()
    {
        Circuit ring = new Circuit().AddNode(0, 0, out int a).Extend(a, 0, Deg(200), out int b).Extend(b, Deg(200), Deg(200), out int d)
            .Extend(d, Deg(200), 0, out int e).Connect(e, a);
        RoadLayout.Run run = Assert.Single(RoadLayout.Runs(ring, DirOf, Radius, 2.0));
        Assert.True(run.Closed);
        Assert.True(Vec.Len(run.Line[0] - run.Line[^1]) * Radius < 1e-6);

        Circuit star = new Circuit().AddNode(0, 0, out int mid).Extend(mid, Deg(200), 0, out _)
            .Extend(mid, -Deg(100), Deg(173), out _).Extend(mid, -Deg(100), -Deg(173), out _);
        List<RoadLayout.Run> arms = RoadLayout.Runs(star, DirOf, Radius, 2.0);
        Assert.Equal(3, arms.Count);
        Assert.Equal(3, arms.Select(r => Math.Round(r.SinkStartM + r.SinkEndM, 3)).Distinct().Count());
    }

    [Fact]
    public void RoadsOfDifferentWidthsAreNotJoined()
    {
        Circuit c = new Circuit().AddNode(0, 0, out int a).Extend(a, 0, Deg(200), out int b).Extend(b, 0, Deg(400), out int d).SetWidth(b, d, 6.0);
        Assert.Equal(2, RoadLayout.Runs(c, DirOf, Radius, 2.0).Count);
    }
}

public class RoadSurfaceTests
{
    private const double R = 1_737_000.0;

    // A road along +Y on top of the body at +X, so many metres above its mean radius.
    private static double3[] Line(double heightM, double fromM, double toM, double acrossM = 0.0)
    {
        List<double3> line = [];
        for (double y = fromM; y <= toM; y += 2.0) line.Add(Vec.Unit(new double3(R, y, acrossM)) * (R + heightM));
        return [.. line];
    }

    private static double3 At(double upM, double alongM, double acrossM) => Vec.Unit(new double3(R, alongM, acrossM)) * (R + upM);

    [Fact]
    public void AWheelOverTheRoadIsItsHeightAboveItAndOneWellOffTheEdgeOrPastTheEndHasNoRoad()
    {
        RoadSurface road = new([(Line(3.0, 0.0, 200.0), 4.0, false)]);

        Assert.True(road.TryHeightOver(At(3.4, 101.0, 2.5), out double over));
        Assert.Equal(0.4, over, 3);
        Assert.True(road.TryHeightOver(At(2.9, 57.3, -3.9), out over));
        Assert.Equal(-0.1, over, 3);

        Assert.False(road.TryHeightOver(At(3.4, 101.0, 12.0), out _));
        Assert.False(road.TryHeightOver(At(3.4, 209.0, 0.0), out _));
        Assert.False(road.TryHeightOver(At(3.4, -9.0, 0.0), out _));
        Assert.False(new RoadSurface(Array.Empty<(double3[], double, bool)>()).TryHeightOver(At(3.4, 10.0, 0.0), out _));
    }

    [Fact]
    public void TheRoadFallsAwayPastItsEdgeAndItsEndAsAShoulderAndNotAStep()
    {
        RoadSurface road = new([(Line(0.2, 0.0, 200.0), 4.0, false)]);

        double last = 0.0;
        for (double across = 3.0; across <= 7.0; across += 0.25)
        {
            Assert.True(road.TryHeightOver(At(0.2, 100.0, across), out double over));
            Assert.True(over - last < 0.25 * RoadSurface.ShoulderSlope + 1e-3, $"a step of {over - last:F3} m at {across} m out");
            last = over;
        }
        Assert.Equal(3.0 * RoadSurface.ShoulderSlope, last, 2);

        Assert.True(road.TryHeightOver(At(0.2, 203.0, 0.0), out double pastEnd));
        Assert.Equal(3.0 * RoadSurface.ShoulderSlope, pastEnd, 2);
    }

    [Fact]
    public void ARoadWellOverheadIsABridgeAndTheRoadUnderItIsTheOneAnswered()
    {
        RoadSurface roads = new([(Line(0.2, 0.0, 200.0), 4.0, false), (Line(9.0, 0.0, 200.0), 4.0, false)]);

        Assert.True(roads.TryHeightOver(At(0.5, 100.0, 0.0), out double under));
        Assert.Equal(0.3, under, 3);
        Assert.True(roads.TryHeightOver(At(9.3, 100.0, 0.0), out double onTop));
        Assert.Equal(0.3, onTop, 3);

        // A wheel a metre and a half into the upper road has run into it, not driven under it.
        Assert.True(roads.TryHeightOver(At(7.5, 100.0, 0.0), out double sunk));
        Assert.Equal(-1.5, sunk, 3);
        Assert.True(roads.TryHeightOver(At(6.0, 100.0, 0.0), out double beneath));
        Assert.Equal(5.8, beneath, 3);
    }

    [Theory]
    [InlineData(0.15)]
    [InlineData(0.58)]
    [InlineData(1.0)]
    public void OverARampAWheelIsItsHeightStraightAboveTheSurfaceAllTheWayUp(double slope)
    {
        List<double3> ramp = [];
        for (double y = 0.0; y <= 60.0; y += 2.0) ramp.Add(Vec.Unit(new double3(R, y, 0.0)) * (R + (slope * y)));
        RoadSurface road = new([([.. ramp], 4.0, false)]);

        for (double along = 0.5; along < 59.5; along += 0.37)
        {
            Assert.True(road.TryHeightOver(At((slope * along) + 0.33, along, 1.0), out double over), $"no road at {along} m");
            Assert.True(Math.Abs(over - 0.33) < 0.01, $"{over:F3} m over the ramp at {along:F2} m along a slope of {slope}");
        }
    }

    [Fact]
    public void AWheelThatWasOnARoadIsStillOnItHoweverDeepAndIsNotTakenUpToABridgeOverIt()
    {
        RoadSurface roads = new([(Line(0.2, 0.0, 200.0), 4.0, false), (Line(9.0, 0.0, 200.0), 4.0, false)]);

        // Five metres into the upper road: nothing, seen fresh, and the upper road still, for a wheel that was on it.
        Assert.True(roads.TryHeightOver(At(4.0, 100.0, 0.0), out double fresh));
        Assert.Equal(3.8, fresh, 3);
        Assert.True(roads.TryHeightOver(At(4.0, 100.0, 0.0), 0.3, out double still));
        Assert.Equal(3.8, still, 3);
        Assert.True(roads.TryHeightOver(At(4.0, 100.0, 0.0), -3.5, out double into));
        Assert.Equal(-5.0, into, 3);

        // A road moved a long way from under a wheel is not the road it was on: the wheel is over what is under it now.
        Assert.True(roads.TryHeightOver(At(40.0, 100.0, 0.0), 0.3, out double left));
        Assert.Equal(31.0, left, 3);

        // On the lower road under the bridge, a wheel stays on the lower road.
        Assert.True(roads.TryHeightOver(At(0.5, 100.0, 0.0), 0.33, out double under));
        Assert.Equal(0.3, under, 3);
    }

    [Fact]
    public void ARaisedRoadHasNoShoulderSoNothingBesideItIsToldItIsUnderIt()
    {
        double3[] line = Line(1.5, 0.0, 200.0);
        double[] up = [.. line.Select(_ => 1.5)], down = [.. line.Select(_ => 0.07)];
        RoadSurface raised = new([(line, 4.0, false, (double[]?)up)]);
        RoadSurface onTheGround = new([(Line(0.07, 0.0, 200.0), 4.0, false, (double[]?)down)]);

        // A hub a third of a metre over the grass, walked in from the side and from past the end.
        for (double across = 12.0; across > 4.1; across -= 0.25)
        {
            Assert.False(raised.TryHeightOver(At(0.33, 100.0, across), out _), $"caught {across} m out");
            Assert.False(raised.TryHeightOver(At(0.33, 100.0, across), 0.3, out _), $"caught {across} m out, remembered");
        }
        Assert.False(raised.TryHeightOver(At(0.33, 203.0, 0.0), out _));
        Assert.True(raised.TryHeightOver(At(1.83, 100.0, 3.9), out double on));
        Assert.Equal(0.33, on, 3);

        Assert.True(onTheGround.TryHeightOver(At(0.33, 100.0, 6.0), out double shoulder));
        Assert.Equal(0.26 + (2.0 * RoadSurface.ShoulderSlope), shoulder, 2);
    }

    [Fact]
    public void AWideRoadIsFoundFromItsEdgeAcrossTheSearchGrid()
    {
        RoadSurface road = new([(Line(0.0, 0.0, 400.0), 20.0, false)]);
        for (double along = 5.0; along < 395.0; along += 7.3)
        {
            Assert.True(road.TryHeightOver(At(1.0, along, 19.5), out double over), $"lost at {along} m");
            Assert.Equal(1.0, over, 2);
        }
    }
}

public class RoadLayingTests
{
    private const double Radius = 6_371_000.0;

    private static double3 DirOf(double latDeg, double lonDeg)
    {
        double lat = latDeg * Math.PI / 180.0, lon = lonDeg * Math.PI / 180.0;
        return new double3(Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));
    }

    private static double Deg(double metres) => metres / Radius * 180.0 / Math.PI;

    [Fact]
    public void ALaidRoadStandsOnTheGroundByItsLiftAndItsPointsHeightsAndDipsWhereItJoinsAnother()
    {
        Circuit c = new Circuit { WidthM = 10.0 }.AddNode(0, 0, out int mid)
            .Extend(mid, 0, -Deg(200), out _).Extend(mid, 0, Deg(200), out int east).Extend(mid, -Deg(200), 0, out _)
            .SetHeight(east, 6.0);

        List<RoadLaying.Strip> strips = RoadLaying.Lay(c, DirOf, Radius, dir => 50.0 + (1000.0 * dir.Z), 0.07, 2.0);

        RoadLaying.Strip through = strips.Single(s => s.LengthM > 300.0), side = strips.Single(s => s.LengthM < 300.0);
        Assert.Equal(400.0, through.LengthM, 0);
        Assert.Equal(5.0, through.HalfWidth);

        for (int i = 0; i < through.Line.Length; i++)
        {
            double3 dir = Vec.Unit(through.Line[i]);
            Assert.Equal(Radius + 50.0 + (1000.0 * dir.Z) + through.AboveGroundM[i], Vec.Len(through.Line[i]), 6);
        }
        Assert.Equal(0.07, through.AboveGroundM.Min(), 9);
        Assert.Equal(6.07, through.AboveGroundM.Max(), 9);

        // The side road meets the through road at one of its ends, and is under it there by its sink.
        double atJunction = Math.Min(side.AboveGroundM[0], side.AboveGroundM[^1]);
        Assert.InRange(0.07 - atJunction, 0.005, 0.03);
        Assert.Equal(0.07, side.AboveGroundM[side.AboveGroundM.Length / 2], 9);

        Assert.True(RoadLaying.Surface(strips).TryHeightOver(through.Line[10] + (Vec.Unit(through.Line[10]) * 0.33), out double over));
        Assert.Equal(0.33, over, 2);
    }
}
