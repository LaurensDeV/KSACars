using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class RaceLineTests
{
    [Fact]
    public void AnArrowheadIsBlueUnderTheSpeedAllowedAndRedWellOverIt()
    {
        Assert.Equal(0.0, RaceLine.Heat(20.0, 40.0));
        Assert.Equal(0.0, RaceLine.Heat(40.0 * RaceLine.CoolShare, 40.0), 9);
        Assert.Equal(1.0, RaceLine.Heat(40.0 * RaceLine.HotShare, 40.0), 9);
        Assert.Equal(1.0, RaceLine.Heat(90.0, 40.0));
        Assert.InRange(RaceLine.Heat(43.0, 40.0), 0.3, 0.7);
        Assert.Equal(1.0, RaceLine.Heat(10.0, 0.0));
        Assert.Equal(0, RaceLine.Level(0.0));
        Assert.Equal(RaceLine.Levels - 1, RaceLine.Level(1.0));
        Assert.Equal(1, RaceLine.Level(0.3));
    }

    [Fact]
    public void EveryArrowheadLiesOnTheRoadAndPointsAlongTheRoute()
    {
        foreach (AutopilotTests.Track track in new[] { AutopilotTests.Ring(60.0), AutopilotTests.Leaning(80.0, 15.0), AutopilotTests.Ramp() })
        {
            Route route = AutopilotTests.RouteOn(track);
            RaceLine.Arrow[] arrows = RaceLine.Lay(route, track.Road);
            Assert.Equal((int)Math.Floor(route.LengthM / RaceLine.EveryM), arrows.Length);

            foreach (RaceLine.Arrow arrow in arrows)
            {
                foreach (double3 corner in new[] { arrow.Tip, arrow.Left, arrow.Notch, arrow.Right })
                {
                    Assert.True(track.Road.TryHeightOver(corner, out double over), $"no road under an arrowhead at {arrow.S:F0} m");
                    Assert.InRange(over, RaceLine.LiftM - 0.005, RaceLine.LiftM + 0.005);
                }
                Route.Sample here = route.Samples[arrow.Sample];
                double3 back = (arrow.Left + arrow.Right) * 0.5;
                Assert.True(Vec.Dot(arrow.Tip - back, here.Tangent) > 0.9 * 2.0 * RaceLine.HalfLongM, $"the arrowhead at {arrow.S:F0} m points astray");
                Assert.True(Vec.Dot(arrow.Left - arrow.Right, here.Across) > 0.9 * 2.0 * RaceLine.HalfWideM);
                Assert.True(Vec.Dot(arrow.Facing, Vec.Unit(arrow.Tip)) > 0.9);
            }
        }
    }

    [Fact]
    public void AStretchIsWholeArrowheadsFacingUpAndGoesRoundAClosedRoutePastItsStart()
    {
        AutopilotTests.Track ring = AutopilotTests.Ring(60.0);
        Route route = AutopilotTests.RouteOn(ring);
        RaceLine.Arrow[] arrows = RaceLine.Lay(route, ring.Road);

        int first = arrows.Length - 5, most = arrows.Length + RaceLine.Most;
        RaceLine.Mesh mesh = RaceLine.Stretch(arrows, closed: true, first, most)!;
        Assert.Equal(most * RaceLine.Vertices, mesh.Positions.Length);
        Assert.Equal(most * RaceLine.Indices, mesh.Indices.Length);
        Assert.Equal(0.0, Vec.Len(mesh.Origin - arrows[first].Tip), 9);

        // The sixth is the route's first again, where a float puts it.
        Assert.True(Vec.Len(mesh.Origin + Wide(mesh.Positions[5 * RaceLine.Vertices]) - arrows[0].Tip) < 1e-3);
        for (int t = 0; t < mesh.Indices.Length; t += 3)
        {
            Assert.Equal(t / RaceLine.Indices, mesh.Indices[t] / RaceLine.Vertices);
            double3 a = Wide(mesh.Positions[mesh.Indices[t]]), b = Wide(mesh.Positions[mesh.Indices[t + 1]]), c = Wide(mesh.Positions[mesh.Indices[t + 2]]);
            double3 across = Vec.Cross(b - a, c - a);
            Assert.True(Vec.Len(across) > 0.05, "an arrowhead's triangle has no area");
            // Anticlockwise from above, which is the side KSA draws.
            Assert.True(Vec.Dot(across, Wide(mesh.Normals[mesh.Indices[t]])) > 0.0, "an arrowhead's triangle faces down");
        }

        // On an open route there is nothing past its end.
        AutopilotTests.Track straight = AutopilotTests.Straight();
        Route along = AutopilotTests.RouteOn(straight);
        RaceLine.Arrow[] few = RaceLine.Lay(along, straight.Road);
        Assert.Equal(7 * RaceLine.Vertices, RaceLine.Stretch(few, closed: false, few.Length - 7, 1000)!.Positions.Length);
        Assert.Null(RaceLine.Stretch(few, closed: false, few.Length, 1000));
        Assert.True(RaceLine.First(along.LengthM - 2.0, 10.0) >= few.Length);
        Assert.True(RaceLine.First(100.0, 50.0) > RaceLine.First(100.0, 0.0));
    }

    [Fact]
    public void ACarComingFastAtABendSeesRedBeforeItAndBlueOnTheStraightBehind()
    {
        // A straight into a tight bend: the plan brakes for it, so the arrowheads up to it are over the car's speed's worth.
        TrackWorld world = TrackWorld.Earth();
        Circuit circuit = AutopilotTests.Through(world, false, (0.0, 0.0, 0.0), (400.0, 0.0, 0.0), (430.0, 30.0, 0.0), (430.0, 120.0, 0.0));
        AutopilotTests.Track track = new(world, circuit, world.Surface(circuit));
        Route route = AutopilotTests.RouteOn(track);
        TrackCar car = TrackCar.F2004;
        Autopilot pilot = new(car.Profile, route, track.Road, car.MassKg, world.Gravity, world.Air);
        double[] allowed = pilot.Plan().ToArray();
        Assert.Equal(route.Count, allowed.Length);
        Assert.True(allowed.Min() < 40.0 && allowed[0] > 60.0, $"the plan is {allowed.Min():F0} to {allowed.Max():F0} m/s");

        RaceLine.Arrow[] arrows = RaceLine.Lay(route, track.Road);
        int first = RaceLine.First(200.0, 0.0);
        RaceLine.Run[] At(double speed, out int covered)
        {
            RaceLine.Run[] runs = new RaceLine.Run[12];
            int made = RaceLine.Runs(arrows, allowed, false, first, speed, runs, out covered);
            return runs[..made];
        }

        RaceLine.Run[] fast = At(70.0, out int count), slow = At(15.0, out _), faster = At(80.0, out _);
        Assert.Equal(RaceLine.Most, count);

        // Every arrowhead is in one run and no more, in order, and two runs that touch are two colours.
        Assert.Equal(0, fast[0].From);
        for (int r = 1; r < fast.Length; r++)
        {
            Assert.Equal(fast[r - 1].From + fast[r - 1].Count, fast[r].From);
            Assert.NotEqual(fast[r - 1].Level, fast[r].Level);
        }
        Assert.Equal(count, fast[^1].From + fast[^1].Count);

        Assert.Equal(0, fast[0].Level);
        Assert.Contains(fast, r => r.Level == RaceLine.Levels - 1);
        Assert.Equal([new RaceLine.Run(0, 0, count)], slow);

        // And it is red sooner the faster the car is.
        int Red(RaceLine.Run[] runs) => runs.First(r => r.Level == RaceLine.Levels - 1).From;
        Assert.True(Red(faster) < Red(fast));

        // With too few runs to hold them it stops short and does not write past them.
        Assert.Equal(2, RaceLine.Runs(arrows, allowed, false, first, 70.0, new RaceLine.Run[2], out _));
    }

    private static double3 Wide(float3 v) => new(v.X, v.Y, v.Z);
}
