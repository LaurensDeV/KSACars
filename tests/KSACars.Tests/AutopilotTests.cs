using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

// The driver that follows a route, on the rig that can leave the ground: every road here is laid as
// the game lays one, and the car is stepped as Buggies steps it, with the driver asked where Buggies
// asks it.
public class AutopilotTests
{
    internal const double Width = 10.0;

    public static readonly double[] Steps = [1.0 / 120.0, 1.0 / 60.0, 0.02, 1.0 / 30.0, 0.05];

    internal sealed record Lap(LapEnd End, string Why, LapSummary Summary, TrackRig Rig, Route Route, Autopilot Pilot)
    {
        public const string Header = "end\tlaps\tsecs\tlap_s\tmax_ms\tmax_cross_m\tmean_cross_m\toff_asphalt_s\tair_s\tlongest_flight_s\t"
                                   + "hub_low_m\thub_high_m\troll_deg\tpitch_deg\tbrake_toggles\thull_steps";

        public int HullSteps => Rig.Log.Count(s => s.HullDown);

        public string Row() => string.Join('\t', new[]
        {
            End.ToString(), Summary.Laps.ToString(CultureInfo.InvariantCulture), F(Summary.Seconds, 1), F(Summary.LastLapSeconds, 1),
            F(Summary.MaxSpeed, 1), F(Summary.MaxCrossM, 2), F(Summary.MeanCrossM, 2), F(Summary.OffAsphaltSeconds, 2),
            F(Summary.AirSeconds, 2), F(Summary.LongestFlightSeconds, 2), F(Summary.MinHubM, 3), F(Summary.MaxHubM, 3),
            F(Summary.MaxRollDeg, 1), F(Summary.MaxPitchDeg, 1), Summary.BrakeToggles.ToString(CultureInfo.InvariantCulture),
            HullSteps.ToString(CultureInfo.InvariantCulture),
        });

        public string Told => $"{End} {Why} after {Summary.Seconds:F1} s and {Summary.ProgressM:F0} m: cross {Summary.MaxCrossM:F2} m, "
                            + $"off the asphalt {Summary.OffAsphaltSeconds:F2} s, in the air {Summary.AirSeconds:F2} s, "
                            + $"hubs {Summary.MinHubM:F3} to {Summary.MaxHubM:F3} m, hull down {HullSteps} steps";

        private static string F(double v, int places) => v.ToString("F" + places, CultureInfo.InvariantCulture);
    }

    internal sealed record Track(TrackWorld World, Circuit Circuit, RoadSurface Road);

    private static readonly ConcurrentDictionary<string, Track> Tracks = new();

    private static Track Laid(string key, Func<TrackWorld, Circuit> circuit) => Tracks.GetOrAdd(key, _ =>
    {
        TrackWorld world = TrackWorld.Earth();
        Circuit c = circuit(world);
        return new Track(world, c, world.Surface(c));
    });

    // A circuit through points so many metres east and north, each so far above the ground, joined in order.
    internal static Circuit Through(TrackWorld world, bool closed, params (double East, double North, double Height)[] points)
    {
        Circuit circuit = new Circuit { WidthM = Width }.AddNode(world.Deg(points[0].North), world.Deg(points[0].East), out int first)
            .SetHeight(first, points[0].Height);
        int last = first;
        for (int i = 1; i < points.Length; i++)
        {
            circuit = circuit.Extend(last, world.Deg(points[i].North), world.Deg(points[i].East), out last).SetHeight(last, points[i].Height);
        }
        return closed ? circuit.Connect(last, first) : circuit;
    }

    // A ring driven anticlockwise, so every bend is to the left.
    internal static Track Ring(double radius) => Laid($"ring{radius}", world =>
    {
        int points = radius > 150.0 ? 24 : 12;
        return Through(world, true, [.. Enumerable.Range(0, points).Select(k =>
            (radius * Math.Cos(2.0 * Math.PI * k / points), radius * Math.Sin(2.0 * Math.PI * k / points), 0.0))]);
    });

    // A ring that leans the same way all the way round: the left of a lap anticlockwise is its inside.
    internal static Track Leaning(double radius, double bankDeg) => Laid($"leaning{radius}/{bankDeg}", world =>
    {
        Circuit c = Ring(radius).Circuit;
        foreach (Circuit.Road road in c.Roads) c = c.SetBank(road.From, road.To, bankDeg).SetBank(road.To, road.From, bankDeg);
        return c;
    });

    // Level, up 50 m at 80%, and level again: the height's own points along the ramp hold it to that.
    internal static Track Wall() => Laid("wall", world => Through(world, false,
        (-150.0, 0.0, 0.0), (0.0, 0.0, 0.0), (20.0, 0.0, 8.0), (40.0, 0.0, 24.0), (60.0, 0.0, 40.0), (82.5, 0.0, 50.0), (200.0, 0.0, 50.0)));

    // A through road east along the equator, and from its middle a side road north that climbs four metres.
    internal static Track Junction() => Laid("junction", world =>
    {
        Circuit c = Through(world, false, (-150.0, 0.0, 0.0), (0.0, 0.0, 0.0), (150.0, 0.0, 0.0));
        return c.Extend(2, world.Deg(140.0), 0.0, out int side).SetHeight(side, 4.0);
    });

    // Level, up eight metres over a hundred, level on the deck, and down again.
    internal static Track Ramp() => Laid("ramp", world => Through(world, false,
        (-200.0, 0.0, 0.0), (0.0, 0.0, 0.0), (100.0, 0.0, 8.0), (300.0, 0.0, 8.0), (400.0, 0.0, 0.0), (600.0, 0.0, 0.0)));

    internal static Track Straight() => Laid("straight", world => Through(world, false, (0.0, 0.0, 0.0), (200.0, 0.0, 0.0)));

    // A figure of eight that crosses itself at the middle, six metres over itself.
    internal static Track Eight() => Laid("eight", world =>
    {
        const int points = 12;
        return Through(world, true, [.. Enumerable.Range(0, points).Select(k =>
        {
            double t = (Math.PI / 12.0) + (2.0 * Math.PI * k / points);
            return (160.0 * Math.Cos(t), 160.0 * Math.Sin(t) * Math.Cos(t), k is 2 or 3 ? 6.0 : 0.0);
        })]);
    });

    internal static Route RouteOn(Track track, IReadOnlyList<int>? through = null, double offset = 0.0, TrackCar? car = null)
    {
        Route? route = Route.Of(track.Circuit, TrackWorld.DirOf, track.World.RadiusM, track.World.HeightAt, TrackWorld.LiftM,
                                TrackWorld.SpacingM, through, offset, out string why, car is null ? 0.0 : Autopilot.TurnRadius(car.Profile));
        Assert.True(route is not null, why);
        return route!;
    }

    // The car stood on the road where the route starts, facing along it, and driven until the driver
    // says the lap is over; then left alone for `after` seconds.
    internal static Lap Drive(TrackCar car, Track track, Route route, double dt, int laps = 1, double cruise = 0.0,
                              double timeout = 300.0, double after = 0.0, Action<TrackRig, Autopilot>? each = null,
                              double[]? pattern = null, LapRow[]? rows = null, bool jumps = false, double push = 0.0,
                              IReadOnlyList<(double, double)>? jumpZones = null, Autopilot.Tuning? tune = null)
    {
        TrackRig rig = new(car, track.World, track.Road);
        (double3 at, _, double3 ahead) = route.Standing(1.5 * BuggyDrive.Wheelbase(car.Profile));
        (double east, double north) = track.World.Flatten(at);
        double above = Vec.Len(at) - track.World.RadiusM - track.World.HeightAt(Vec.Unit(at));
        rig.Place(east, north, Math.Atan2(ahead.Z, ahead.Y) * 180.0 / Math.PI, above);
        rig.Settle(1.0);
        rig.Dt = dt;
        rig.Pattern = pattern;

        Autopilot pilot = new(car.Profile, route, track.Road, car.MassKg, track.World.Gravity, track.World.Air, laps, cruise, timeout, rows) { Jumps = jumps, Push = push, JumpZones = jumpZones ?? [], Tune = tune ?? new() };
        rig.Driver = pilot;
        for (int guard = 0; pilot.End == LapEnd.Running && guard < 2_000_000 && rig.Finite; guard++)
        {
            each?.Invoke(rig, pilot);
            rig.Step(default);
        }
        for (double t = 0.0; t < after; t += rig.Step(default).Dt) { }
        return new Lap(pilot.End, pilot.Why, pilot.Summary, rig, route, pilot);
    }

    // How far the driver may be from its line with every wheel still on the asphalt, less a margin.
    private static double Room(TrackCar car, double offset = 0.0, double margin = 0.5)
    {
        double halfTrack = car.Profile.Corners.Max(c => Math.Abs(c.Hub.Z));
        return (0.5 * Width) - halfTrack - Math.Abs(offset) - margin;
    }

    public static TheoryData<string, double, double> RingRuns()
    {
        TheoryData<string, double, double> runs = [];
        foreach (TrackCar car in TrackCar.All)
        {
            foreach (double radius in new[] { 30.0, 50.0, 100.0 })
            {
                foreach (double dt in Steps) runs.Add(car.Name, radius, dt);
            }
        }
        return runs;
    }

    [Theory]
    [MemberData(nameof(RingRuns))]
    public void EveryCarLapsARingOnTheAsphaltAtEveryStep(string name, double radius, double dt)
    {
        TrackCar car = TrackCar.Of(name);
        Track ring = Ring(radius);
        Lap lap = Drive(car, ring, RouteOn(ring), dt, laps: 2);

        Assert.True(lap.End == LapEnd.Finished && lap.Summary.Laps == 2, lap.Told);
        Assert.True(lap.Summary.OffAsphaltSeconds == 0.0, lap.Told);
        Assert.True(lap.Summary.MaxCrossM < Room(car), lap.Told);
        Assert.True(lap.Rig.Log.Min(s => s.UpDot) > 0.9, lap.Told);
        Assert.True(lap.HullSteps == 0 && lap.Summary.AirSeconds == 0.0, lap.Told);
    }

    [Theory]
    [InlineData("F2004", 20.0)]
    [InlineData("Eldorado", 20.0)]
    [InlineData("Manx", 20.0)]
    [InlineData("F2004", -30.0)]
    [InlineData("Eldorado", -30.0)]
    public void ARingThatLeansOutOfItsBendOrIntoItIsLappedOnTheAsphalt(string name, double bankDeg)
    {
        TrackCar car = TrackCar.Of(name);
        Track ring = Leaning(80.0, bankDeg);
        Lap lap = Drive(car, ring, RouteOn(ring), 1.0 / 60.0, pattern: ExtremeLapTests.GameSteps);

        Assert.True(lap.End == LapEnd.Finished, lap.Told);
        Assert.True(lap.Summary.OffAsphaltSeconds == 0.0 && lap.Summary.MaxCrossM < Room(car), lap.Told);

        // Against the road's own face, which is what a lap's roll is measured from.
        Assert.True(lap.Summary.MaxRollDeg < 8.0 && lap.Summary.LongestFlightSeconds == 0.0, lap.Told);
    }

    [Fact]
    public void ARoutesSlopeIsTheRoadsTangentAndItsBendIsTheRoadsOwn()
    {
        Route route = RouteOn(Wall());
        Route.Sample steepest = route.Samples.ToArray().MaxBy(s => s.Slope);

        // 16 m up in 20 m along the ground, which is 0.62 of a metre for each metre along the road.
        Assert.InRange(steepest.Slope, 0.75, 0.90);

        // From level to that steep: all the bending into the climb adds up to the angle it reaches.
        double turned = 0.0;
        for (int i = 1; i < route.Samples.Length; i++)
        {
            turned += Math.Max(route.Samples[i].Vertical, 0.0) * (route.Samples[i].S - route.Samples[i - 1].S);
        }
        Assert.InRange(turned, 0.9 * Math.Atan(steepest.Slope), 1.1 * Math.Atan(steepest.Slope));
    }

    [Theory]
    [InlineData(1.0 / 60.0)]
    [InlineData(0.02)]
    [InlineData(0.05)]
    public void TheF2004HoldsAHighCruiseRoundABigRing(double dt)
    {
        Track ring = Ring(300.0);
        Lap lap = Drive(TrackCar.F2004, ring, RouteOn(ring), dt, laps: 1, cruise: 70.0);

        Assert.True(lap.End == LapEnd.Finished, lap.Told);
        Assert.True(lap.Summary.MaxSpeed > 65.0, $"reached {lap.Summary.MaxSpeed:F1} m/s; {lap.Told}");
        Assert.True(lap.Summary.OffAsphaltSeconds == 0.0 && lap.Summary.MaxCrossM < Room(TrackCar.F2004), lap.Told);
    }

    public static TheoryData<string, double> EdgeRuns()
    {
        TheoryData<string, double> runs = [];
        foreach (TrackCar car in TrackCar.All)
        {
            foreach (double side in new[] { -1.0, 0.0, 1.0 }) runs.Add(car.Name, side);
        }
        return runs;
    }

    // A real wheel rolled round a bend a hand from the road's edge: the surface has no hole and no
    // step under it, or the hub's height would show one.
    [Theory]
    [MemberData(nameof(EdgeRuns))]
    public void ALapAlongEitherEdgeOfABendFindsNoHoleOrStepUnderAWheel(string name, double side)
    {
        TrackCar car = TrackCar.Of(name);
        Track ring = Ring(50.0);
        double offset = side * Room(car, margin: EdgeGapM);
        Lap lap = Drive(car, ring, RouteOn(ring, offset: offset), 1.0 / 60.0, laps: 1, cruise: 12.0);

        Assert.True(lap.End == LapEnd.Finished, lap.Told);
        Assert.True(lap.Summary.OffAsphaltSeconds == 0.0, lap.Told);
        Assert.True(lap.Summary.MaxCrossM < EdgeGapM, lap.Told);

        // Still on its springs the whole way: never past the bump stop, never unloaded to the droop.
        Assert.True(lap.Summary.MinHubM > -car.Profile.BumpTravel && lap.Summary.MaxHubM < car.Profile.DroopTravel, lap.Told);
        Assert.True(lap.Rig.Log.All(s => s.OnRoad == 4 && s.Grounded == 4), lap.Told);
    }

    private const double EdgeGapM = 0.3;

    [Theory]
    [InlineData("Manx", "1,2,3")]
    [InlineData("Manx", "1,2,4")]
    [InlineData("Manx", "4,2,3")]
    [InlineData("Eldorado", "1,2,3")]
    [InlineData("Eldorado", "1,2,4")]
    [InlineData("Eldorado", "4,2,3")]
    [InlineData("F2004", "1,2,3")]
    [InlineData("F2004", "1,2,4")]
    [InlineData("F2004", "4,2,3")]
    public void EachWayThroughAJunctionIsTheWayTheRouteSays(string name, string through)
    {
        TrackCar car = TrackCar.Of(name);
        Track junction = Junction();
        int[] nodes = [.. through.Split(',').Select(int.Parse)];
        Lap lap = Drive(car, junction, RouteOn(junction, nodes, car: car), 1.0 / 60.0, after: 3.0);

        Assert.True(lap.End == LapEnd.Finished, lap.Told);
        Assert.True(lap.Summary.OffAsphaltSeconds == 0.0, lap.Told);
        Assert.True(lap.HullSteps == 0 && lap.Summary.LongestFlightSeconds < 0.1, lap.Told);

        // It ends on the road the route ends on: at the top of the side road, or east along the through road.
        (double east, double north) = lap.Rig.Where;
        if (nodes[^1] == 4) Assert.True(north > 120.0 && Math.Abs(east) < 5.0 && lap.Rig.TerrainUnderM > 3.5, $"ended {east:F1} east, {north:F1} north; {lap.Told}");
        else Assert.True(east > 130.0 && Math.Abs(north) < 5.0, $"ended {east:F1} east, {north:F1} north; {lap.Told}");
    }

    [Theory]
    [InlineData("Manx", 20.0)]
    [InlineData("Eldorado", 20.0)]
    [InlineData("Eldorado", 40.0)]
    [InlineData("F2004", 20.0)]
    [InlineData("F2004", 40.0)]
    public void UpARampAlongADeckAndDownAgainItStaysOnItsWheels(string name, double cruise)
    {
        TrackCar car = TrackCar.Of(name);
        Track ramp = Ramp();
        // At the speed asked for and no less: left to itself the driver slows for the foot and the top of a ramp.
        Lap lap = Drive(car, ramp, RouteOn(ramp), 1.0 / 60.0, cruise: cruise, after: 3.0, jumps: true);

        Assert.True(lap.End == LapEnd.Finished, lap.Told);
        Assert.True(lap.Summary.MaxSpeed > cruise - 2.0 && lap.Summary.MaxSpeed < cruise + 3.0, lap.Told);
        Assert.True(lap.Summary.LongestFlightSeconds < 0.3, lap.Told);
        Assert.True(lap.Summary.MaxCrossM < 0.5 && lap.Summary.OffAsphaltSeconds == 0.0, lap.Told);
        Assert.True(lap.Rig.Log.Max(s => s.TerrainUnderM) is > 8.0 and < 10.0, $"highest {lap.Rig.Log.Max(s => s.TerrainUnderM):F2} m; {lap.Told}");
        Assert.True(Vec.Len(lap.Rig.Velocity) < 0.2 && lap.Rig.TerrainUnderM < 1.5, $"ends at {Vec.Len(lap.Rig.Velocity):F2} m/s; {lap.Told}");
    }

    [Theory]
    [InlineData("Manx", false)]
    [InlineData("Manx", true)]
    [InlineData("Eldorado", false)]
    [InlineData("Eldorado", true)]
    [InlineData("F2004", false)]
    [InlineData("F2004", true)]
    public void RoundAFigureOfEightItIsOnTheBridgeAndThenUnderIt(string name, bool backwards)
    {
        TrackCar car = TrackCar.Of(name);
        Track eight = Eight();
        int[] nodes = [.. Enumerable.Range(1, 12), 1];
        if (backwards) Array.Reverse(nodes);
        Lap lap = Drive(car, eight, RouteOn(eight, nodes), 1.0 / 60.0);

        Assert.True(lap.End == LapEnd.Finished && lap.Summary.Laps == 1, lap.Told);
        Assert.True(lap.Summary.OffAsphaltSeconds == 0.0 && lap.HullSteps == 0, lap.Told);
        Assert.True(lap.Summary.LongestFlightSeconds < 0.1, lap.Told);

        // Over the middle twice: once on the deck and once on the ground under it, and nowhere between.
        List<double> crossings = [.. lap.Rig.Log.Where(s => Math.Abs(s.East) < 4.0 && Math.Abs(s.North) < 4.0).Select(s => s.TerrainUnderM - car.Com.X)];
        Assert.Contains(crossings, h => h > 5.5);
        Assert.Contains(crossings, h => h < 0.5);
        Assert.DoesNotContain(crossings, h => h is > 0.5 and < 5.5);
    }

    [Theory]
    [InlineData("Manx")]
    [InlineData("Eldorado")]
    [InlineData("F2004")]
    public void AtTheEndOfARoadItStopsAndStaysStopped(string name)
    {
        TrackCar car = TrackCar.Of(name);
        Track straight = Straight();
        double slowest = double.PositiveInfinity;
        Lap lap = Drive(car, straight, RouteOn(straight), 1.0 / 60.0, after: 4.0,
                        each: (rig, _) => slowest = Math.Min(slowest, Vec.Dot(rig.Velocity, rig.Forward)));

        Assert.True(lap.End == LapEnd.Finished, lap.Told);
        Assert.True(lap.Summary.MaxSpeed > 15.0, lap.Told);
        Assert.True(slowest > -0.05, $"rolled back at {slowest:F2} m/s");
        Assert.True(Vec.Len(lap.Rig.Velocity) < 0.1, $"still moving at {Vec.Len(lap.Rig.Velocity):F2} m/s");
        Assert.True(lap.Rig.Where.East is > 192.0 and < 199.0, $"stopped {lap.Rig.Where.East:F1} m along a 200 m road");
        Assert.True(lap.Rig.Log[^1].OnRoad == 4, "a wheel is off the end");
    }

    // Thrown sideways, to the outside of the bend, eight seconds into two laps of the 50 m ring.
    private static Lap Kicked(TrackCar car, double kick, out bool kicked)
    {
        Track ring = Ring(50.0);
        bool done = false;
        Lap lap = Drive(car, ring, RouteOn(ring), 1.0 / 60.0, laps: 2, timeout: 200.0, each: (rig, pilot) =>
        {
            if (done || pilot.Summary.Seconds < 8.0) return;
            done = true;
            rig.Velocity += (rig.Attitude * new double3(0, 0, -1)) * kick;
        });
        kicked = done;
        return lap;
    }

    [Theory]
    [InlineData("Manx", 4.0)]
    [InlineData("Eldorado", 4.0)]
    [InlineData("F2004", 4.0)]
    [InlineData("Manx", 15.0)]
    [InlineData("Eldorado", 15.0)]
    [InlineData("F2004", 15.0)]
    public void KnockedOffItsLineItRecoversOrEndsWithAReason(string name, double kick)
    {
        TrackCar car = TrackCar.Of(name);
        Lap lap = Kicked(car, kick, out bool kicked);

        Assert.True(kicked, "the lap was over before the kick");
        Assert.True(lap.End != LapEnd.Running && lap.End != LapEnd.TimedOut && lap.End != LapEnd.Failed, lap.Told);
        if (kick < 5.0) Assert.True(lap.End == LapEnd.Finished && lap.Summary.Laps == 2, lap.Told);
    }

    [Fact]
    public void OutOfTimeItEndsAndLetsGoOfTheCar()
    {
        Track ring = Ring(100.0);
        TrackRig? seen = null;
        Lap lap = Drive(TrackCar.Manx, ring, RouteOn(ring), 1.0 / 60.0, laps: 9, timeout: 4.0, each: (rig, _) => seen = rig);

        Assert.True(lap.End == LapEnd.TimedOut, lap.Told);
        Assert.InRange(lap.Summary.Seconds, 4.0, 4.05);
        Assert.Equal(default, lap.Pilot.Step(seen!.Position, seen.Attitude, seen.Velocity, new double3(1, 0, 0), new double3(0, 1, 0), [], [], 0.02));
    }

    // Set down on the grass beside the road, it has a second of being off it and is called off.
    [Fact]
    public void StartedOffTheRoadItEndsAsOffTheRoad()
    {
        Track straight = Straight();
        Route route = RouteOn(straight);
        TrackRig rig = new(TrackCar.Eldorado, straight.World, straight.Road);
        rig.Place(60.0, 40.0, 0.0);
        rig.Settle(1.0);
        Autopilot pilot = new(TrackCar.Eldorado.Profile, route, straight.Road, TrackCar.Eldorado.MassKg, 9.81, 1.225);
        rig.Driver = pilot;
        for (int i = 0; i < 600 && pilot.End == LapEnd.Running; i++) rig.Step(default);

        Assert.Equal(LapEnd.OffRoad, pilot.End);
        Assert.InRange(pilot.Summary.Seconds, 1.0, 1.05);
    }

    [Fact]
    public void TheSameRunTwiceIsTheSameRun()
    {
        Track eight = Eight();
        Lap one = Drive(TrackCar.Eldorado, eight, RouteOn(eight), 0.02);
        Lap two = Drive(TrackCar.Eldorado, eight, RouteOn(eight), 0.02);

        Assert.Equal(one.Summary, two.Summary);
        Assert.Equal(one.Rig.Position, two.Rig.Position);
        Assert.Equal(one.End, two.End);
    }

    [Fact]
    public void ARouteNoRoadJoinsIsRefusedWithTheReason()
    {
        Track junction = Junction();
        Route? route = Route.Of(junction.Circuit, TrackWorld.DirOf, junction.World.RadiusM, junction.World.HeightAt, TrackWorld.LiftM,
                                TrackWorld.SpacingM, [1, 3], 0.0, out string why);
        Assert.Null(route);
        Assert.Contains("1 and 3", why);
    }

    [Fact]
    public void ARouteRoundARingIsARingAndItsBendsAreTheRingsRadius()
    {
        Route route = RouteOn(Ring(50.0));
        Assert.True(route.Closed);
        Assert.InRange(route.LengthM, 2.0 * Math.PI * 49.0, 2.0 * Math.PI * 51.0);
        foreach (Route.Sample s in route.Samples)
        {
            Assert.InRange(s.Curvature, 1.0 / 60.0, 1.0 / 42.0);
            Assert.InRange(s.Slope, -1e-6, 1e-6);
        }

        // Hugging the outside the bend is wider, and the road's centre is to the left of the line.
        Route outside = RouteOn(Ring(50.0), offset: -3.0);
        Assert.InRange(outside.LengthM, 2.0 * Math.PI * 52.0, 2.0 * Math.PI * 54.0);
        Assert.Equal(3.0, outside.Samples[0].CentreM);
    }

    // Where the route crosses itself the nearest stretch is the other road; the one a car is on is
    // the one a little further on than it last was.
    [Fact]
    public void ProgressFollowsTheRouteThroughItsOwnCrossing()
    {
        Route route = RouteOn(Eight());
        int index = 0;
        double gone = 0.0;
        int laps = 0;
        for (double s = 0.0; s < route.LengthM * 1.5; s += 0.7)
        {
            double along = s % route.LengthM;
            double3 on = route.Ahead(route.IndexAt(along), along, 0.0);
            if (route.Locate(on - (Vec.Unit(on) * 5.0), ref index, 10.0, out double found, out double cross)) laps++;
            double progress = (laps * route.LengthM) + found;
            Assert.True(Math.Abs(progress - s) < 0.05 && Math.Abs(cross) < 0.05, $"at {s:F1} m told {progress:F1} m and {cross:F2} m aside");
            Assert.True(progress >= gone - 1e-6, $"went back from {gone:F1} to {progress:F1}");
            gone = progress;
        }
        Assert.Equal(1, laps);
    }

    [Theory]
    [InlineData("Manx")]
    [InlineData("Eldorado")]
    [InlineData("F2004")]
    public void ACarIsStoodOnARampFacingUpIt(string name)
    {
        TrackCar car = TrackCar.Of(name);
        Route route = RouteOn(Ramp());
        (double3 at, double3 surfaceUp, double3 ahead) = route.Standing(250.0);
        Assert.InRange(Math.Asin(Vec.Dot(ahead, Vec.Unit(at))), 0.08, 0.13);

        // From any attitude: the turn is in the car's own frame, as the game writes it.
        doubleQuat body2Ccf = doubleQuat.CreateFromAxisAngle(Vec.Unit(new double3(0.3, -0.5, 0.8)), 2.1);
        doubleQuat ccf2Body = doubleQuat.Conjugate(body2Ccf);
        double3 up = new(1, 0, 0), forward = new(0, 1, 0);
        doubleQuat stood = body2Ccf * Righting.Facing(up, forward, ccf2Body * surfaceUp, ccf2Body * ahead);
        Assert.True(Vec.Len((stood * up) - surfaceUp) < 1e-9, "its up is not the road's");
        Assert.True(Vec.Len((stood * forward) - ahead) < 1e-9, $"it faces {Vec.Len((stood * forward) - ahead):E2} off the route");

        double3[] hubs = [.. car.Profile.Corners.Select(c => c.Hub - car.Com)];
        double height = Righting.StandingHeight(car.Profile.Corners, hubs, up);
        foreach ((BuggyCorner corner, double3 hub) in car.Profile.Corners.Zip(hubs))
        {
            double clear = height + hub.X - corner.Radius;
            Assert.InRange(clear, Righting.Clearance - 1e-9, Righting.Clearance + 0.1);
        }
    }

    // Not a test: with KSACARS_LAPS set to a path, every lap above as a table, for the numbers in a report; and
    // with KSACARS_LAP_ROWS a path, one lap a step a line, which KSACARS_LAP_ROWS_OF names as car, fixture and
    // step in milliseconds ("F2004,100,50" is the 100 m ring).
    [Fact]
    public void WriteTheLapsAsATable()
    {
        if (Environment.GetEnvironmentVariable("KSACARS_LAPS") is not { Length: > 0 } path) return;

        StringBuilder table = new();
        table.Append("fixture\tcar\tdt_ms\t").AppendLine(Lap.Header);
        void Row(string fixture, TrackCar car, double dt, Lap lap) =>
            table.Append(CultureInfo.InvariantCulture, $"{fixture}\t{car.Name}\t{dt * 1000.0:F1}\t").AppendLine(lap.Row());

        foreach (TrackCar car in TrackCar.All)
        {
            foreach (double radius in new[] { 30.0, 50.0, 100.0 })
            {
                foreach (double dt in Steps) Row($"ring {radius:F0} m, 2 laps", car, dt, Drive(car, Ring(radius), RouteOn(Ring(radius)), dt, laps: 2));
            }
            foreach (double side in new[] { -1.0, 0.0, 1.0 })
            {
                double offset = side * Room(car, margin: EdgeGapM);
                Row($"ring 50 m at 12 m/s, {offset:+0.00;-0.00;0.00} m left", car, 1.0 / 60.0,
                    Drive(car, Ring(50.0), RouteOn(Ring(50.0), offset: offset), 1.0 / 60.0, cruise: 12.0));
            }
            foreach (string through in new[] { "1,2,3", "1,2,4", "4,2,3" })
            {
                Row($"junction {through}", car, 1.0 / 60.0,
                    Drive(car, Junction(), RouteOn(Junction(), [.. through.Split(',').Select(int.Parse)]), 1.0 / 60.0));
            }
            foreach (double cruise in new[] { 20.0, 40.0 })
            {
                Row($"ramp 8 m over 100 m at {cruise:F0} m/s", car, 1.0 / 60.0, Drive(car, Ramp(), RouteOn(Ramp()), 1.0 / 60.0, cruise: cruise));
            }
            foreach (bool backwards in new[] { false, true })
            {
                int[] nodes = [.. Enumerable.Range(1, 12), 1];
                if (backwards) Array.Reverse(nodes);
                Row($"figure of eight{(backwards ? ", backwards" : "")}", car, 1.0 / 60.0, Drive(car, Eight(), RouteOn(Eight(), nodes), 1.0 / 60.0));
            }
            Row("straight to its end", car, 1.0 / 60.0, Drive(car, Straight(), RouteOn(Straight()), 1.0 / 60.0));
            foreach (double kick in new[] { 4.0, 15.0 })
            {
                Row($"ring 50 m, 2 laps, thrown sideways at {kick:F0} m/s", car, 1.0 / 60.0, Kicked(car, kick, out _));
            }
        }
        if (Environment.GetEnvironmentVariable("KSACARS_LAP_ROWS") is { Length: > 0 } rowsPath)
        {
            string[] what = (Environment.GetEnvironmentVariable("KSACARS_LAP_ROWS_OF") ?? "Eldorado,eight,16.7").Split(',');
            TrackCar car = TrackCar.Of(what[0]);
            Track track = what[1] switch { "eight" => Eight(), "junction" => Junction(), "ramp" => Ramp(), _ => Ring(double.Parse(what[1], CultureInfo.InvariantCulture)) };
            Route route = RouteOn(track, what[1] == "junction" ? [1, 2, 4] : null);
            LapRow[] rows = new LapRow[200_000];
            TrackRig rig = new(car, track.World, track.Road);
            (double3 at, _, double3 ahead) = route.Standing(1.5 * BuggyDrive.Wheelbase(car.Profile));
            (double east, double north) = track.World.Flatten(at);
            rig.Place(east, north, Math.Atan2(ahead.Z, ahead.Y) * 180.0 / Math.PI);
            rig.Settle(1.0);
            rig.Dt = double.Parse(what[2], CultureInfo.InvariantCulture) / 1000.0;
            Autopilot pilot = new(car.Profile, route, track.Road, car.MassKg, track.World.Gravity, track.World.Air, 2, 0.0, 300.0, rows);
            rig.Driver = pilot;
            while (pilot.End == LapEnd.Running) rig.Step(default);
            StringBuilder csv = new();
            csv.AppendLine(LapRow.Header);
            for (int i = 0; i < pilot.RowCount; i++) rows[i].AppendTo(csv);
            File.WriteAllText(rowsPath, csv.ToString());
        }
        foreach (double dt in Steps)
        {
            Row("ring 300 m at 70 m/s", TrackCar.F2004, dt, Drive(TrackCar.F2004, Ring(300.0), RouteOn(Ring(300.0)), dt, cruise: 70.0));
        }
        File.WriteAllText(path, table.ToString());
    }
}
