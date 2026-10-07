using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class RoadChartTests
{
    public static TheoryData<double> Bodies => new() { 1_737_400.0, 6_371_000.0 };

    private static RoadChart Chart(double radius) => new(TrackWorld.DirOf(31.0, -62.0), radius, new double3(0, 0, 1));

    [Theory]
    [MemberData(nameof(Bodies))]
    public void APlaceComesBackFromTheChartWhereItWentIn(double radius)
    {
        RoadChart chart = Chart(radius);
        for (int i = 0; i < 360; i += 7)
        {
            foreach (double r in new[] { 0.0, 3.0, 400.0, 5_000.0 })
            {
                Plan at = new(r * Math.Cos(i * Math.PI / 180.0), r * Math.Sin(i * Math.PI / 180.0));
                double3 dir = chart.Dir(at);

                Assert.Equal(1.0, Vec.Len(dir), 12);
                Assert.True((chart.Of(dir * radius) - at).Len < 1e-6, $"{(chart.Of(dir) - at).Len} m out at {r} m");
                Assert.True(Vec.Len((chart.Dir(chart.Of(dir)) - dir) * radius) < 1e-6);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Bodies))]
    public void EastAndNorthAreWhereTheBodysAre(double radius)
    {
        RoadChart chart = Chart(radius);
        double metre = 180.0 / Math.PI / radius;
        double3 north = TrackWorld.DirOf(31.0 + (100.0 * metre), -62.0);
        double3 east = TrackWorld.DirOf(31.0, -62.0 + (100.0 * metre / Math.Cos(31.0 * Math.PI / 180.0)));

        Assert.True((chart.Of(north) - new Plan(0.0, 100.0)).Len < 1e-2);
        Assert.True((chart.Of(east) - new Plan(100.0, 0.0)).Len < 1e-2);
    }

    // Two short steps from one place, each way round the compass: the angle between them on the ground
    // is the angle between them on the chart, and each is too long by the scale the chart says.
    [Theory]
    [MemberData(nameof(Bodies))]
    public void AnglesAreKeptAndLengthsAreOutByTheScaleItSays(double radius)
    {
        RoadChart chart = Chart(radius);
        foreach (Plan at in new[] { new Plan(0.0, 0.0), new Plan(5_000.0, 0.0), new Plan(-3_000.0, 4_000.0), new Plan(40_000.0, -90_000.0) })
        {
            double3 here = chart.Dir(at) * radius;
            double scale = chart.Scale(at);
            Assert.Equal(1.0 + (((at.E * at.E) + (at.N * at.N)) / (4.0 * radius * radius)), scale, 14);

            chart.Compass(at, out double3 up, out double3 east, out double3 north);
            Assert.Equal(0.0, Vec.Dot(east, north), 12);
            Assert.True(Vec.Len(Vec.Cross(east, north) - up) < 1e-12);

            for (int i = 0; i < 360; i += 30)
            {
                Plan one = new(Math.Cos(i * Math.PI / 180.0), Math.Sin(i * Math.PI / 180.0));
                Plan other = new(Math.Cos((i + 50) * Math.PI / 180.0), Math.Sin((i + 50) * Math.PI / 180.0));
                double3 a = (chart.Dir(at + one) * radius) - here, b = (chart.Dir(at + other) * radius) - here;

                Assert.Equal(50.0, Vec.AngleBetween(a, b) * 180.0 / Math.PI, 4);
                Assert.Equal(1.0 / scale, Vec.Len(a), 6);
                Assert.True(Vec.Len(Vec.Unit(a) - ((east * one.E) + (north * one.N))) < 1e-5);
            }
        }
    }

    [Fact]
    public void AtFiveKilometresOnLunaALengthIsTwoPartsInAMillionLong()
    {
        Assert.Equal(2.07e-6, Chart(1_737_400.0).Scale(new Plan(5_000.0, 0.0)) - 1.0, 8);
    }
}

public class RoadLineTests
{
    private static readonly RoadArc Bend = new(new Plan(0, 0), new Plan(60, 0), new Plan(100, 30), new Plan(100, 110));
    private static readonly RoadArc Ess = new(new Plan(0, 0), new Plan(80, 60), new Plan(40, -60), new Plan(120, 0));

    // A corner strength of nothing: both handles have no length, and the parameter stands still at each end.
    private static readonly RoadArc Stalled = new(new Plan(0, 0), new Plan(0, 0), new Plan(90, 40), new Plan(90, 40));

    public static TheoryData<string> Arcs => new() { "bend", "ess", "stalled", "straight" };

    private static RoadArc Arc(string name) => name switch
    {
        "bend" => Bend,
        "ess" => Ess,
        "stalled" => Stalled,
        _ => RoadArc.Straight(new Plan(3, 4), new Plan(-70, 90)),
    };

    private static (Plan[] At, double[] Along) Dense(RoadArc arc, int steps = 200_000)
    {
        Plan[] at = new Plan[steps + 1];
        double[] along = new double[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            at[i] = arc.At((double)i / steps);
            if (i > 0) along[i] = along[i - 1] + (at[i] - at[i - 1]).Len;
        }
        return (at, along);
    }

    [Theory]
    [MemberData(nameof(Arcs))]
    public void ACurvesLengthIsWhatALineOfManyPointsAlongItMeasures(string name)
    {
        RoadArc arc = Arc(name);
        (_, double[] along) = Dense(arc);

        Assert.True(Math.Abs(arc.LengthM - along[^1]) < 1e-6 * along[^1], $"{arc.LengthM} m against {along[^1]} m");
        for (int i = 0; i < along.Length; i += 9973)
        {
            Assert.True(Math.Abs(arc.LengthAt((double)i / (along.Length - 1)) - along[i]) < 1e-6 * along[^1]);
        }
    }

    [Theory]
    [MemberData(nameof(Arcs))]
    public void APointSoFarAlongIsWhereThatLinePutsIt(string name)
    {
        RoadArc arc = Arc(name);
        RoadLine line = new([arc], false);
        (Plan[] at, double[] along) = Dense(arc);

        for (int k = 0; k <= 400; k++)
        {
            double s = arc.LengthM * k / 400.0;
            int i = Array.BinarySearch(along, s);
            if (i < 0) i = ~i - 1;
            i = Math.Clamp(i, 0, at.Length - 2);
            Plan there = at[i] + ((at[i + 1] - at[i]) * ((s - along[i]) / Math.Max(along[i + 1] - along[i], 1e-12)));

            RoadLine.Point point = line.At(s);
            Assert.True((point.At - there).Len < 1e-3, $"{(point.At - there).Len} m out at {s} m along");
            Assert.True(Math.Abs(arc.LengthAt(arc.TimeAt(s)) - s) < 1e-8);
        }
    }

    [Theory]
    [MemberData(nameof(Arcs))]
    public void ItsHeadingAndItsCurvatureAreThoseOfThePointsAlongIt(string name)
    {
        RoadLine line = new([Arc(name)], false);
        const double step = 1e-3;

        for (double s = 1.0; s < line.LengthM - 1.0; s += 0.73)
        {
            RoadLine.Point before = line.At(s - step), here = line.At(s), after = line.At(s + step);
            Plan measured = (after.At - before.At) * (0.5 / step);

            Assert.Equal(1.0, here.Heading.Len, 9);
            Assert.True((here.Heading - measured).Len < 1e-5, $"heading out by {(here.Heading - measured).Len} at {s} m");
            double turned = Plan.Cross(before.Heading, after.Heading) / (2.0 * step);
            Assert.True(Math.Abs(here.Curvature - turned) < 1e-5, $"curvature {here.Curvature} against {turned} at {s} m");
        }
    }

    [Fact]
    public void AStalledCurveHasAHeadingAtItsEndsAndNoCurvatureAnywhere()
    {
        RoadLine line = new([Stalled], false);
        Plan along = new Plan(90, 40).Unit();
        foreach (double s in new[] { 0.0, 1e-9, 1e-4, 10.0, line.LengthM - 1e-7, line.LengthM })
        {
            RoadLine.Point point = line.At(s);
            Assert.True((point.Heading - along).Len < 1e-9, $"heading {point.Heading} at {s} m");
            Assert.Equal(0.0, point.Curvature, 9);
            Assert.True((point.At - (along * s)).Len < 1e-6);
        }
    }

    [Fact]
    public void CurvesEndToEndAreOneLineAndAClosedOneGoesRoundAgain()
    {
        Plan[] square = [new(0, 0), new(100, 0), new(100, 100), new(0, 100)];
        RoadLine open = RoadLine.Through(square, false), ring = RoadLine.Through(square, true);

        Assert.Equal(300.0, open.LengthM, 9);
        Assert.Equal(400.0, ring.LengthM, 9);
        Assert.True((open.At(150.0).At - new Plan(100, 50)).Len < 1e-9);
        Assert.True((open.At(999.0).At - new Plan(0, 100)).Len < 1e-9);
        Assert.True((ring.At(350.0).At - new Plan(0, 50)).Len < 1e-9);
        Assert.True((ring.At(-50.0).At - new Plan(0, 50)).Len < 1e-9);
        Assert.True((ring.At(450.0).At - new Plan(50, 0)).Len < 1e-9);

        // At a kink the line has the heading it leaves with, and the one it came with on the curve before.
        Assert.True((open.At(100.0).Heading - new Plan(0, 1)).Len < 1e-9);
        Assert.True((open.At(0, 100.0).Heading - new Plan(1, 0)).Len < 1e-9);
        Assert.Equal(Math.PI / 2.0, open.TurnAt(1), 9);
        Assert.Equal(0.0, open.TurnAt(0), 9);
        Assert.Equal(Math.PI / 2.0, ring.TurnAt(0), 9);
    }

    // What RoadLayout samples through the body's own frame and what the chart's curve gives at the same
    // share of the way along are one place, on every road of every circuit that is lapped.
    [Fact]
    public void ARoadOnTheChartIsWhereItIsSampledThroughTheBodysFrame()
    {
        TrackWorld world = TrackWorld.Earth();
        double worst = 0.0;
        foreach (string name in ExtremeCircuits.Names)
        {
            Circuit circuit = ExtremeCircuits.Of(name, world);
            RoadChart chart = RoadLayout.Chart(circuit, TrackWorld.DirOf, world.RadiusM);
            Dictionary<int, double3> at = RoadLayout.Places(circuit, TrackWorld.DirOf, world.RadiusM);
            List<RoadLayout.Stretch> stretches = RoadLayout.Of(circuit, TrackWorld.DirOf, world.RadiusM, 2.0);

            for (int r = 0; r < circuit.Roads.Count; r++)
            {
                RoadArc arc = RoadLayout.Arc(chart, RoadLayout.Controls(circuit, at, circuit.Roads[r], TrackWorld.DirOf, world.RadiusM));
                double3[] line = stretches[r].Line;
                for (int i = 0; i < line.Length; i++)
                {
                    worst = Math.Max(worst, (chart.Of(line[i]) - arc.At((double)i / (line.Length - 1))).Len);
                }
            }
        }
        Assert.True(worst < 1e-3, $"{worst} m apart");
    }

    [Fact]
    public void ABendTighterThanItsRoadNeedsIsReportedWithWhereAndHowTight()
    {
        // A quarter circle of 12 m radius between two straights, as curves.
        const double r = 12.0, k = 0.5522847498 * r;
        RoadLine line = new(
        [
            RoadArc.Straight(new Plan(-50, 0), new Plan(0, 0)),
            new RoadArc(new Plan(0, 0), new Plan(k, 0), new Plan(r, r - k), new Plan(r, r)),
            RoadArc.Straight(new Plan(r, r), new Plan(r, 80)),
        ], false);

        Assert.Empty(line.TooTight(_ => 10.0));

        RoadLine.Tight tight = Assert.Single(line.TooTight(_ => 1.25 * (10.0 + 1.5)));
        Assert.InRange(tight.FromS, 50.0, 50.6);
        Assert.InRange(tight.ToS, 49.4 + (0.5 * Math.PI * r), 50.1 + (0.5 * Math.PI * r));
        Assert.InRange(tight.LeastRadiusM, 11.9, 12.0);
        Assert.Equal(14.375, tight.NeededM, 9);
    }

    [Fact]
    public void AKinkIsReportedAsABendOfNoRadius()
    {
        RoadLine line = RoadLine.Through([new Plan(0, 0), new Plan(90, 0), new Plan(135, 78)], false);

        RoadLine.Tight kink = Assert.Single(line.TooTight(_ => 8.0));
        Assert.Equal(90.0, kink.FromS, 9);
        Assert.Equal(90.0, kink.ToS, 9);
        Assert.Equal(0.0, kink.LeastRadiusM);
    }
}
