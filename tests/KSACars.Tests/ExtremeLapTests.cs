using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

// Every car round every circuit of ExtremeCircuits, which are the ones lapped in game, at the car's own
// top speed where the road allows it and along the route the game's `lap` takes with nothing said.
public class ExtremeLapTests
{
    private static readonly ConcurrentDictionary<string, AutopilotTests.Track> Tracks = new();

    internal static AutopilotTests.Track Laid(string name) => Tracks.GetOrAdd(name, _ =>
    {
        TrackWorld world = TrackWorld.Earth();
        Circuit c = ExtremeCircuits.Of(name, world);
        return new AutopilotTests.Track(world, c, world.Surface(c));
    });

    // The game's own steps, as it was lapped: between 17 and 33 ms and no two alike.
    internal static readonly double[] GameSteps = Drawn();

    private static double[] Drawn()
    {
        Random random = new(5554);
        double[] steps = new double[997];
        for (int i = 0; i < steps.Length; i++) steps[i] = 0.017 + (0.016 * random.NextDouble());
        return steps;
    }

    private static AutopilotTests.Lap Lapped(TrackCar car, string circuit, bool gameSteps, bool jumps = false, double after = 0.0)
    {
        AutopilotTests.Track track = Laid(circuit);
        Route route = AutopilotTests.RouteOn(track, car: car);

        // Let go of at the line of a closed route a car is still at speed, and where it goes then is nobody's.
        return AutopilotTests.Drive(car, track, route, 1.0 / 60.0, after: route.Closed ? 0.0 : after,
                                    pattern: gameSteps ? GameSteps : null, jumps: jumps);
    }

    private static double HalfTrack(TrackCar car) => car.Profile.Corners.Max(c => Math.Abs(c.Hub.Z));

    public static TheoryData<string, string, bool> Laps()
    {
        TheoryData<string, string, bool> laps = [];
        foreach (string circuit in ExtremeCircuits.Names)
        {
            foreach (TrackCar car in TrackCar.All)
            {
                laps.Add(circuit, car.Name, false);
                laps.Add(circuit, car.Name, true);
            }
        }
        return laps;
    }

    [Theory]
    [MemberData(nameof(Laps))]
    public void EveryCarLapsEveryCircuitOnItsWheelsAndOnTheAsphalt(string circuit, string name, bool gameSteps)
    {
        TrackCar car = TrackCar.Of(name);
        AutopilotTests.Lap lap = Lapped(car, circuit, gameSteps, after: 4.0);
        LapSummary s = lap.Summary;

        Assert.True(lap.End == LapEnd.Finished, lap.Told);
        if (!lap.Route.Closed) Assert.True(Vec.Len(lap.Rig.Velocity) < 0.1, $"still moving at {Vec.Len(lap.Rig.Velocity):F2} m/s; {lap.Told}");
        Assert.True(s.OffAsphaltSeconds == 0.0, lap.Told);
        Assert.True(s.LongestFlightSeconds < 0.25, lap.Told);
        Assert.True(lap.Rig.Log.Min(l => l.UpDot) > 0.9 && s.MaxRollDeg < 10.0, $"rolled {s.MaxRollDeg:F1} deg; {lap.Told}");

        // On its line: a metre is a tenth of the road, and less than the room the narrowest road leaves.
        double room = lap.Route.Samples[0].HalfWidth - HalfTrack(car);
        Assert.True(s.MaxCrossM < Math.Min(1.0, room), lap.Told);

        // The game's colliders drag where a tyre rolls, and one that touches trips the car: the lowest
        // ends FloorM over the ground, so no spring is pushed up that far, less two centimetres.
        Assert.True(lap.HullSteps == 0 && s.MinHubM > 0.02 - car.FloorM, lap.Told);
    }

    // Round the grid by its junctions and not its rim: left at a T, straight over the raised crossroads
    // twice, right at two more Ts and left at the last, which is every way a junction is driven.
    internal static readonly int[] GridTurns = [1, 2, 5, 8, 9, 6, 5, 4, 1];

    public static TheoryData<string, bool> Turns()
    {
        TheoryData<string, bool> turns = [];
        foreach (TrackCar car in TrackCar.All)
        {
            turns.Add(car.Name, false);
            turns.Add(car.Name, true);
        }
        return turns;
    }

    internal static AutopilotTests.Lap Turned(TrackCar car, bool gameSteps)
    {
        AutopilotTests.Track track = Laid("Grid");
        return AutopilotTests.Drive(car, track, AutopilotTests.RouteOn(track, GridTurns, car: car), 1.0 / 60.0, pattern: gameSteps ? GameSteps : null);
    }

    [Theory]
    [MemberData(nameof(Turns))]
    public void EveryCarTurnsAtTheGridsJunctionsAndCrossesItsRaisedMiddleOnItsWheels(string name, bool gameSteps)
    {
        TrackCar car = TrackCar.Of(name);
        AutopilotTests.Lap lap = Turned(car, gameSteps);
        LapSummary s = lap.Summary;

        Assert.True(lap.Route.Closed && lap.End == LapEnd.Finished, lap.Told);
        Assert.True(s.OffAsphaltSeconds == 0.0 && s.LongestFlightSeconds < 0.25, lap.Told);
        Assert.True(lap.Rig.Log.Min(l => l.UpDot) > 0.9 && s.MaxRollDeg < 10.0, $"rolled {s.MaxRollDeg:F1} deg; {lap.Told}");
        Assert.True(s.MaxCrossM < Math.Min(1.0, lap.Route.Samples[0].HalfWidth - HalfTrack(car)), lap.Told);
        Assert.True(lap.HullSteps == 0 && s.MinHubM > 0.02 - car.FloorM, lap.Told);
    }

    // What the limit over a crest is for, and that it can be taken off to see a car fly.
    [Fact]
    public void LeftToJumpTheF2004LeavesTheCoasterAndHeldDownItDoesNot()
    {
        AutopilotTests.Lap jumped = Lapped(TrackCar.F2004, "Coaster", gameSteps: false, jumps: true);
        Assert.True(jumped.Summary.LongestFlightSeconds > 1.0, jumped.Told);
        Assert.True(jumped.End != LapEnd.Finished, jumped.Told);

        AutopilotTests.Lap held = Lapped(TrackCar.F2004, "Coaster", gameSteps: false);
        Assert.True(held.End == LapEnd.Finished && held.Summary.LongestFlightSeconds < 0.25, held.Told);
    }

    [Fact]
    public void ARoutesClimbBendsUpThroughADipAndDownOverACrest()
    {
        // The ramp eases 8 m up over 100 m, so its climb bends by 6 x 8 / 100^2 a metre at its foot and its top.
        Route route = AutopilotTests.RouteOn(AutopilotTests.Ramp());
        double foot = route.Samples[route.IndexAt(206.0)].Vertical, top = route.Samples[route.IndexAt(294.0)].Vertical;
        Assert.InRange(foot, 0.0035, 0.0048);
        Assert.InRange(top, -0.0048, -0.0035);
        Assert.InRange(route.Samples[route.IndexAt(250.0)].Vertical, -0.0004, 0.0004);
        Assert.InRange(route.Samples[route.IndexAt(100.0)].Vertical, -1e-6, 1e-6);
        Assert.InRange(route.Samples[route.IndexAt(400.0)].Vertical, -1e-6, 1e-6);
    }

    // Two 10 m roads meeting at 120 degrees leave a line down their middles a turn of 4 m radius. The
    // F2004 turns in 8.4 m, so its line has to go out to the edge and back.
    [Fact]
    public void AKinkIsTakenNoTighterThanTheCarTurnsAndStillOnTheAsphalt()
    {
        AutopilotTests.Track kinks = Laid("Kinks");
        double Tightest(Route route) => 1.0 / route.Samples.ToArray().Max(s => Math.Abs(s.Curvature));

        Assert.InRange(Tightest(AutopilotTests.RouteOn(kinks)), 3.0, 5.0);

        foreach (TrackCar car in TrackCar.All)
        {
            Route route = AutopilotTests.RouteOn(kinks, car: car);
            double turns = Autopilot.TurnRadius(car.Profile) / Autopilot.TurnMargin;
            Assert.True(Tightest(route) > turns, $"{car.Name} turns in {turns:F1} m and is asked for {Tightest(route):F1} m");

            double halfTrack = HalfTrack(car);
            foreach (Route.Sample s in route.Samples)
            {
                foreach (double side in new[] { -halfTrack, halfTrack })
                {
                    double3 wheel = s.At + (s.Across * side) + (Vec.Unit(s.At) * 0.3);
                    Assert.True(kinks.Road.TryLocate(wheel, null, out _, out double outM) && outM <= 0.02,
                                $"{car.Name}'s line is {outM:F2} m off the asphalt {s.S:F0} m along");
                }
            }
        }
    }

    // A car is weighed up from where its hubs are under its centre of mass, so one loaded high is slowed.
    [Fact]
    public void ACarWithItsWeightHighIsTakenRoundABendSlower()
    {
        AutopilotTests.Track ring = AutopilotTests.Ring(50.0);
        TrackCar car = TrackCar.Manx;
        double Allowed(double raisedM)
        {
            Route route = AutopilotTests.RouteOn(ring);
            Autopilot pilot = new(car.Profile, route, ring.Road, car.MassKg, 9.81, 1.225);
            double3[] hubs = [.. car.Profile.Corners.Select(c => c.Hub - car.Com - new double3(raisedM, 0, 0))];
            double3 at = route.Samples[0].At + (Vec.Unit(route.Samples[0].At) * car.Com.X);
            pilot.Step(at, doubleQuat.Identity, Vec.Zero, new double3(1, 0, 0), new double3(0, 1, 0), hubs, new double[4], 1.0 / 60.0);
            return pilot.Limit.ToArray().Min();
        }

        // 0.84 m of half track under 0.6 m is past what its tyres hold, so as it ships the grip is the
        // limit; under 1.2 m it is 0.6 x 0.84 / 1.2 of a g, 14 m/s on 50 m.
        Assert.InRange(Allowed(0.0), 19.0, 21.5);
        Assert.InRange(Allowed(0.6), 13.0, 15.0);
    }

    // The buggy's nose goes down 10 cm under its brakes, of the 18 cm its pads stand off the road.
    [Fact]
    public void TheEndOfARoadIsBrakedForGently()
    {
        TrackCar car = TrackCar.Manx;
        AutopilotTests.Track straight = AutopilotTests.Straight();
        double lowest = double.PositiveInfinity;
        AutopilotTests.Lap lap = AutopilotTests.Drive(car, straight, AutopilotTests.RouteOn(straight), 1.0 / 60.0,
            each: (rig, pilot) => lowest = rig.Where.East > 170.0 ? Math.Min(lowest, rig.Log.Count > 0 ? rig.Log[^1].ClearanceM : 0.0) : lowest);

        Assert.True(lap.End == LapEnd.Finished && lap.Summary.MaxSpeed > 15.0, lap.Told);
        Assert.True(lowest > -0.05, $"a spring was pushed up {-lowest * 100.0:F1} cm over the last 25 m");
    }

    // Not a test: with KSACARS_EXTREME_LAPS a path, every lap above as a table, for the numbers in a report.
    [Fact]
    public void WriteTheLapsAsATable()
    {
        if (Environment.GetEnvironmentVariable("KSACARS_EXTREME_LAPS") is not { Length: > 0 } path) return;

        StringBuilder table = new();
        table.Append("circuit\tcar\tsteps\troute_m\tprogress_m\t").AppendLine(AutopilotTests.Lap.Header);
        foreach (string circuit in ExtremeCircuits.Names)
        {
            foreach (TrackCar car in TrackCar.All)
            {
                foreach (bool gameSteps in new[] { false, true })
                {
                    AutopilotTests.Lap lap = Lapped(car, circuit, gameSteps);
                    table.Append(CultureInfo.InvariantCulture,
                                 $"{circuit}\t{car.Name}\t{(gameSteps ? "17-33 ms" : "16.7 ms")}\t{lap.Route.LengthM:F0}\t{lap.Summary.ProgressM:F0}\t")
                         .AppendLine(lap.Row());
                }
            }
        }
        foreach (TrackCar car in TrackCar.All)
        {
            foreach (bool gameSteps in new[] { false, true })
            {
                AutopilotTests.Lap lap = Turned(car, gameSteps);
                table.Append(CultureInfo.InvariantCulture,
                             $"Grid by its junctions\t{car.Name}\t{(gameSteps ? "17-33 ms" : "16.7 ms")}\t{lap.Route.LengthM:F0}\t{lap.Summary.ProgressM:F0}\t")
                     .AppendLine(lap.Row());
            }
        }
        File.WriteAllText(path, table.ToString());
    }
}
