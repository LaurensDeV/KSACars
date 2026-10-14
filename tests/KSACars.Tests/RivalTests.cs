using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class RivalTests
{
    [Fact]
    public void ASeedIsTheSameDriverEveryTimeAndAnotherSeedAnother()
    {
        Rival.Driver a = Rival.Of(3), again = Rival.Of(3), b = Rival.Of(4);
        Assert.Equal(a.Gap, again.Gap);
        Assert.Equal(a.Pace, again.Pace);
        Assert.NotEqual(a.Gap, b.Gap);
        Assert.NotEqual(a.Pace, b.Pace);
    }

    [Fact]
    public void EveryDriverIsWithinWhatADriverMayBe()
    {
        double least = 1.0, most = 0.0;
        for (int seed = 1; seed <= 200; seed++)
        {
            Rival.Driver d = Rival.Of(seed);
            Assert.InRange(d.Gap, 0.0, Rival.GapSpread);
            Assert.Equal(Rival.Places, d.Pace.Length);
            Assert.All(d.Pace, p => Assert.InRange(p, Rival.PaceLow, Rival.PaceHigh));
            // from one place to the next a driver changes its mind only so far
            for (int i = 1; i < Rival.Places; i++) Assert.True(Math.Abs(d.Pace[i] - d.Pace[i - 1]) <= 0.26 * (Rival.PaceHigh - Rival.PaceLow));
            (least, most) = (Math.Min(least, d.Gap), Math.Max(most, d.Gap));
        }
        Assert.True(most - least > 0.75 * Rival.GapSpread, "two hundred drivers are all of a pace");
    }

    [Fact]
    public void AGapIsReadBetweenTheSlowingsALapWasMeasuredAtAndReckonedWhereItWasNot()
    {
        (double, double)[] slowed = [(0.95, 0.02), (0.9, 0.05), (0.8, 0.12)];
        Assert.Equal(1.0, Rival.Slowing(slowed, 0.0), 12);
        Assert.Equal(0.975, Rival.Slowing(slowed, 0.01), 12);
        Assert.Equal(0.95, Rival.Slowing(slowed, 0.02), 12);
        Assert.Equal(0.85, Rival.Slowing(slowed, 0.085), 12);
        // past the last measured, on at the reckoned rate, and never under the least
        Assert.Equal(0.8 - (0.04 / Rival.GapPerSlowing), Rival.Slowing(slowed, 0.16), 12);
        Assert.Equal(Rival.LeastSlowing, Rival.Slowing(slowed, 5.0), 12);
        // nothing measured, all of it reckoned
        Assert.Equal(1.0 - (0.04 / Rival.GapPerSlowing), Rival.Slowing([], 0.04), 12);
        // a slowing that gave up nothing, as on a course the car is flat out round, is passed over and the rest reckoned
        Assert.Equal(1.0 - (0.0001 / Rival.GapPerSlowing), Rival.Slowing([(0.95, 0.0), (0.9, 0.0)], 0.0001), 12);
    }

    [Fact]
    public void ACarInTheWayIsFollowedAndOneBesideOrBehindOrFarAheadIsNot()
    {
        // close behind a slower car: its speed and no more
        Assert.Equal(20.0, Rival.FollowCap(8.0, 0.5, 20.0, 40.0), 9);
        // a stopped car right ahead is stopped for
        Assert.Equal(0.0, Rival.FollowCap(6.0, 0.0, 0.0, 10.0), 9);
        // one coming the other way is no faster than stopped
        Assert.Equal(0.0, Rival.FollowCap(6.0, 0.0, -15.0, 10.0), 9);

        // with room there is more than the leader's speed, and less the nearer it is
        double far = Rival.FollowCap(60.0, 0.0, 20.0, 40.0), near = Rival.FollowCap(40.0, 0.0, 20.0, 40.0);
        Assert.True(near > 20.0 && far > near && far < 45.0, $"{near:F1} then {far:F1} m/s");

        Assert.True(double.IsPositiveInfinity(Rival.FollowCap(10.0, Rival.AbreastM + 0.1, 0.0, 40.0)), "a car beside it");
        Assert.True(double.IsPositiveInfinity(Rival.FollowCap(-3.0, 0.0, 0.0, 40.0)), "a car behind it");
        Assert.True(double.IsPositiveInfinity(Rival.FollowCap(190.0, 0.0, 30.0, 40.0)), "a car far ahead going nearly as fast");
        Assert.True(double.IsPositiveInfinity(Rival.FollowCap(12.0, 0.0, 60.0, 30.0)), "a faster car pulling away");
    }

    [Fact]
    public void TheDriverGoesNoFasterThanItIsHeldTo()
    {
        AutopilotTests.Track ring = AutopilotTests.Ring(60.0);
        TrackCar car = TrackCar.All[0];
        Route route = AutopilotTests.RouteOn(ring, car: car);
        AutopilotTests.Lap free = AutopilotTests.Drive(car, ring, route, 1.0 / 60.0);
        AutopilotTests.Lap held = AutopilotTests.Drive(car, ring, route, 1.0 / 60.0, each: (_, pilot) => pilot.CapMs = 6.0);
        Assert.True(free.Summary.MaxSpeed > 10.0, $"the ring is too slow to tell: {free.Summary.MaxSpeed:F1} m/s");
        Assert.Equal(LapEnd.Finished, held.End);
        Assert.InRange(held.Summary.MaxSpeed, 4.0, 7.0);
    }

    [Fact]
    public void ThrownOffTheRoadADriverGivenTheTimeDrivesItselfBackOn()
    {
        int thrownOff = 0;
        foreach (TrackCar car in TrackCar.All)
        {
            AutopilotTests.Lap ended = AutopilotTests.Kicked(car, 15.0, out _, Autopilot.OffRoadSeconds);
            if (ended.End != LapEnd.OffRoad) continue;
            thrownOff++;

            AutopilotTests.Lap back = AutopilotTests.Kicked(car, 15.0, out bool kicked, 15.0);
            Assert.True(kicked, "the lap was over before the kick");
            Assert.True(back.End == LapEnd.Finished && back.Summary.Laps == 2, $"{car.Profile.DisplayName}: {back.Told}");
            Assert.True(back.Summary.OffAsphaltSeconds > Autopilot.OffRoadSeconds, "it was never off the road for long");
        }
        Assert.True(thrownOff > 0, "no car is thrown off the road by the kick, so nothing was tried");
    }

    [Fact]
    public void SettingsAShareOfTheWayToOthersAreTheirOwnAtNoneOfItAndTheOthersAtAll()
    {
        Autopilot.Tuning own = new(), other = new() { GripShare = 1.5, BrakeLeadSeconds = 0.02, Pace = [1.2, 0.8] };
        Assert.Equal(own.GripShare, own.Toward(other, 0.0).GripShare, 12);
        Assert.Equal(1.5, own.Toward(other, 1.0).GripShare, 12);
        Assert.Equal((own.GripShare + 1.5) / 2.0, own.Toward(other, 0.5).GripShare, 12);
        Assert.Equal(0.02, own.Toward(other, 1.0).BrakeLeadSeconds, 12);
        Assert.Equal(1.0, own.Toward(other, 0.0).PaceAt(0.0), 12);
        Assert.Equal(1.1, own.Toward(other, 0.5).PaceAt(0.0), 12);
        Assert.Equal(0.8, own.Toward(other, 1.0).PaceAt(0.5), 12);
    }

    private static string Repo()
    {
        DirectoryInfo? at = new(AppContext.BaseDirectory);
        while (at is not null && !Directory.Exists(Path.Combine(at.FullName, "src", "KSACars", "Circuits"))) at = at.Parent;
        return at?.FullName ?? throw new InvalidOperationException("the repository was not found above the tests");
    }

    public static TheoryData<string, string> ShippedLaps()
    {
        TheoryData<string, string> laps = [];
        string folder = Path.Combine(Repo(), "src", "KSACars", "Circuits", "Laps");
        foreach (string file in Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories) : [])
        {
            laps.Add(Path.GetFileName(Path.GetDirectoryName(file))!, Path.GetFileNameWithoutExtension(file));
        }
        return laps;
    }

    // An opponent with nobody in its way, lapped twice in the rig at the game's steps.
    private static AutopilotTests.Lap Raced(Circuit circuit, TrackCar car, Rival.Learnt? learnt, int seed, double gap)
    {
        TrackWorld world = TrackWorld.Earth();
        AutopilotTests.Track track = new(world, circuit, world.Surface(circuit));
        Rival.Way way = Rival.Drive(learnt, seed, gap, Autopilot.RaceInside(car.Profile), car.Profile.DisplayName);
        Route route = Route.Of(circuit, TrackWorld.DirOf, world.RadiusM, world.HeightAt, TrackWorld.LiftM, TrackWorld.SpacingM, null, 0.0,
                               out string why, Autopilot.TurnRadius(car.Profile), way.InsideM > 0.0 ? way.InsideM : Autopilot.RaceInside(car.Profile),
                               way.LineM) ?? throw new InvalidOperationException(why);
        return AutopilotTests.Drive(car, track, route, ExtremeLapTests.GameSteps[0], laps: 2, timeout: 600.0, pattern: ExtremeLapTests.GameSteps,
                                    push: way.Push, tune: way.Tune);
    }

    [Theory]
    [MemberData(nameof(ShippedLaps))]
    public void ALapTheModComesWithIsOfItsCourseAndAnOpponentIsCleanAndQuickerTheMoreOfItItTakes(string course, string carName)
    {
        string circuits = Path.Combine(Repo(), "src", "KSACars", "Circuits");
        byte[] file = File.ReadAllBytes(Path.Combine(circuits, course + ".json"));
        Rival.Learnt learnt = Rival.Read(File.ReadAllText(Path.Combine(circuits, "Laps", course, carName + ".json")), out string why)
                              ?? throw new InvalidOperationException(why);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file)), learnt.CircuitSha256, ignoreCase: true);

        Circuit circuit = Circuit.FromJson(System.Text.Encoding.UTF8.GetString(file), out string bad, out _) ?? throw new InvalidOperationException(bad);
        TrackCar car = TrackCar.All.Single(c => c.Profile.DisplayName == carName);

        double before = double.MaxValue;
        foreach (double gap in new[] { 0.2, 0.1, 0.0 })
        {
            foreach (int seed in new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 })
            {
                AutopilotTests.Lap lap = Raced(circuit, car, learnt, seed, gap);
                Assert.True(lap.End == LapEnd.Finished && lap.Summary.OffAsphaltSeconds == 0.0, $"gap {gap}, opponent {seed}: {lap.Told}");
            }
            double took = Raced(circuit, car, learnt, 1, gap).Summary.LastLapSeconds;
            Assert.True(took < before, $"gap {gap} took {took:F1} s, no quicker than the {before:F1} s of the gap before");
            before = took;
        }
        // with a lap to go by an opponent is quicker than without, or the lap is not worth coming with
        double without = Raced(circuit, car, null, 1, 0.0).Summary.LastLapSeconds;
        Assert.True(before < without - 1.0, $"{before:F1} s with the lap and {without:F1} s without");
    }

    public static TheoryData<string, string> AnyCourse()
    {
        TheoryData<string, string> all = [];
        foreach (string circuit in ExtremeCircuits.Names)
        {
            foreach (TrackCar car in TrackCar.All) all.Add(circuit, car.Name);
        }
        return all;
    }

    // A lap an opponent may drive: finished, on its wheels, and no wheel off the asphalt for longer than a kerb is ridden.
    private static bool Clean(AutopilotTests.Lap lap) => lap.End == LapEnd.Finished && lap.Summary.OffAsphaltSeconds < 0.5 && lap.HullSteps == 0;

    // A course nothing is known of, lapped twice on the racing line at the game's steps.
    private static AutopilotTests.Lap RacedBlind(AutopilotTests.Track track, TrackCar car, Rival.Way? way)
    {
        TrackWorld world = track.World;
        double inside = way is { InsideM: > 0.0 } ? way.InsideM : Autopilot.RaceInside(car.Profile);
        Route route = Route.Of(track.Circuit, TrackWorld.DirOf, world.RadiusM, world.HeightAt, TrackWorld.LiftM, TrackWorld.SpacingM, null, 0.0,
                               out string why, Autopilot.TurnRadius(car.Profile), inside, way?.LineM) ?? throw new InvalidOperationException(why);
        return AutopilotTests.Drive(car, track, route, ExtremeLapTests.GameSteps[0], laps: route.Closed ? 2 : 1, timeout: 900.0,
                                    pattern: ExtremeLapTests.GameSteps, push: way?.Push ?? 0.0, tune: way?.Tune);
    }

    [Theory]
    [MemberData(nameof(AnyCourse))]
    public void OnACourseNothingIsKnownOfAnOpponentIsCleanWhereverTheCautiousDriverIsAndNoSlower(string circuit, string carName)
    {
        AutopilotTests.Track track = ExtremeLapTests.Laid(circuit);
        TrackCar car = TrackCar.Of(carName);
        AutopilotTests.Lap cautious = RacedBlind(track, car, null);
        if (!Clean(cautious)) return;

        foreach (double gap in new[] { 0.1, 0.0 })
        {
            foreach (int seed in new[] { 1, 2, 3, 4, 5 })
            {
                AutopilotTests.Lap lap = RacedBlind(track, car, Rival.Drive(null, seed, gap, Autopilot.RaceInside(car.Profile), car.Profile.DisplayName));
                Assert.True(Clean(lap), $"gap {gap}, opponent {seed}: {lap.Told}");
            }
        }
        // the quickest of five flat out is no slower than the cautious driver, to within what its own caution in a bend costs it
        double best = Enumerable.Range(1, 5).Min(seed => RacedBlind(track, car, Rival.Drive(null, seed, 0.0, Autopilot.RaceInside(car.Profile), car.Profile.DisplayName)).Summary.Seconds);
        Assert.True(best < cautious.Summary.Seconds * 1.03, $"{best:F1} s flat out and {cautious.Summary.Seconds:F1} s cautious");
    }

    [Fact]
    public void ACarRoundTheBendIsAheadAlongTheRouteAndNotOffToOneSide()
    {
        AutopilotTests.Track ring = AutopilotTests.Ring(60.0);
        TrackCar car = TrackCar.All[0];
        Route route = AutopilotTests.RouteOn(ring, car: car);
        bool asked = false;
        AutopilotTests.Drive(car, ring, route, 1.0 / 60.0, each: (rig, pilot) =>
        {
            if (asked || pilot.Summary.Seconds < 6.0) return;
            asked = true;
            Route.Sample here = route.Samples[route.Nearest(rig.Position)];
            double3 there = route.Samples[route.IndexAt((here.S + 40.0) % route.LengthM)].At;

            Assert.True(pilot.Along(there, 250.0, out double ahead, out double side));
            Assert.InRange(ahead, 36.0, 44.0);
            Assert.InRange(side, -1.0, 1.0);

            // a straight line from the car has it metres to one side, which is why the route is asked
            double3 up = Vec.Unit(rig.Position), nose = Vec.Unit(Vec.RejectFrom(rig.Attitude * new double3(0, 1, 0), up));
            double3 to = Vec.RejectFrom(there - rig.Position, up);
            Assert.True(Vec.Len(to - (nose * Vec.Dot(to, nose))) > 5.0, "the ring is too wide a bend to tell");
        });
        Assert.True(asked, "the lap was over before it was asked");
    }

    [Fact]
    public void TheGridIsEveryDriverOnceFromTheQuickestToTheSlowest()
    {
        int[] grid = Rival.Gridded(9);
        Assert.Equal(Enumerable.Range(1, 9), grid.Order());
        for (int i = 1; i < grid.Length; i++) Assert.True(Rival.Strength(grid[i - 1]) >= Rival.Strength(grid[i]));
        Assert.Equal(grid, Rival.Gridded(9));
    }

    // Each lap's time, racing blind round a course with straights at the game's steps, as a person or not.
    private static (AutopilotTests.Lap Lap, List<double> Laps) Lapped(Autopilot.Human? person)
    {
        AutopilotTests.Track track = ExtremeLapTests.Laid("Speedway");
        TrackCar car = TrackCar.F2004;
        Rival.Way way = Rival.Drive(null, 1, 0.0, Autopilot.RaceInside(car.Profile), car.Profile.DisplayName);
        Route route = Route.Of(track.Circuit, TrackWorld.DirOf, track.World.RadiusM, track.World.HeightAt, TrackWorld.LiftM, TrackWorld.SpacingM, null, 0.0,
                               out string why, Autopilot.TurnRadius(car.Profile), way.InsideM, way.LineM) ?? throw new InvalidOperationException(why);
        List<double> laps = [];
        AutopilotTests.Lap lap = AutopilotTests.Drive(car, track, route, ExtremeLapTests.GameSteps[0], laps: 6, timeout: 900.0, pattern: ExtremeLapTests.GameSteps,
                                                      tune: way.Tune, person: person, each: (_, pilot) =>
                                                      {
                                                          if (pilot.Summary.Laps > laps.Count) laps.Add(pilot.Summary.LastLapSeconds);
                                                      });
        return (lap, laps);
    }

    [Fact]
    public void APersonLapsUnlikeItselfButAlikeEveryRaceNeverQuickerAndOnTheRoad()
    {
        (AutopilotTests.Lap steady, List<double> same) = Lapped(null);
        (AutopilotTests.Lap person, List<double> laps) = Lapped(new Autopilot.Human(3, 0.015, 1.0));
        (_, List<double> again) = Lapped(new Autopilot.Human(3, 0.015, 1.0));
        Assert.Equal(LapEnd.Finished, person.End);
        Assert.Equal(0.0, person.Summary.OffAsphaltSeconds);
        Assert.Equal(0, person.Rig.HullDownSteps);

        // the same person laps the same laps every race, and none of them alike
        Assert.Equal(laps, again);
        Assert.True(laps.Skip(1).Max() - laps.Skip(1).Min() > 0.05, $"every lap alike: {string.Join(", ", laps.Select(t => t.ToString("F2")))}");
        // a flying lap is never quicker than the driver who is the same every lap, and a fluffed exit is had nearly every lap
        for (int i = 1; i < laps.Count; i++) Assert.True(laps[i] >= same[i] - 0.05, $"lap {i}: {laps[i]:F2} s, where the same driver takes {same[i]:F2}");
        Assert.InRange(person.Summary.Fumbles, laps.Count - 1, laps.Count + 1);
        Assert.Equal(0, steady.Summary.Fumbles);
    }
}
