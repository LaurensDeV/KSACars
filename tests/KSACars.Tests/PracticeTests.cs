using Xunit;

namespace KSACars.Tests;

public class PracticeTests
{
    private static string Shipped(string course)
    {
        DirectoryInfo? at = new(AppContext.BaseDirectory);
        while (at is not null && !Directory.Exists(Path.Combine(at.FullName, "src", "KSACars", "Circuits"))) at = at.Parent;
        return File.ReadAllText(Path.Combine(at!.FullName, "src", "KSACars", "Circuits", course + ".json"));
    }

    internal static Practice.Found? Practised(AutopilotTests.Track track, TrackCar car, out Route route, int laps = Practice.MostDrives,
                                             Action<Practice.Found>? improved = null)
    {
        TrackWorld world = track.World;
        Rival.Blind blind = Rival.BlindFor(car.Profile.DisplayName);
        route = Route.Of(track.Circuit, TrackWorld.DirOf, world.RadiusM, world.HeightAt, TrackWorld.LiftM, TrackWorld.SpacingM, null, 0.0,
                         out string why, Autopilot.TurnRadius(car.Profile), Autopilot.RaceInside(car.Profile) - (blind.NearerM * blind.Most))
                ?? throw new InvalidOperationException(why);
        return Practice.Run(car, world, track.Road, [], route, new Autopilot.Tuning().Toward(blind.Tune, blind.Most), null, improved, laps);
    }

    // Enough laps for the settings to have been turned once: PracticeWholeTests has all of a practice, for one car.
    [Theory]
    [InlineData("Manx", 90, 156.0)]
    [InlineData("Eldorado", 90, 130.0)]
    public void ACarThatPractisesACourseLapsItQuickerAndStillCleanly(string name, int laps, double under) => PractisesClub(name, laps, under);

    internal static void PractisesClub(string name, int laps, double under)
    {
        // where the course's file has it, which is a long way from where the rig's own tests stand a car
        Circuit circuit = Circuit.FromJson(Shipped("Club Motorsports"), out string bad, out _) ?? throw new InvalidOperationException(bad);
        TrackWorld world = TrackWorld.Earth();
        AutopilotTests.Track track = new(world, circuit, world.Surface(circuit));
        TrackCar car = TrackCar.Of(name);

        List<Practice.Found> given = [];
        Practice.Found found = Practised(track, car, out Route route, laps, given.Add) ?? throw new InvalidOperationException("it does not lap the course cleanly even as it races blind");
        Assert.True(found.Seconds < under, $"{found.Seconds:F1} s practised and {found.BlindSeconds:F1} s blind");
        Assert.InRange(found.Drives, 8, laps + 60);

        // it gave out a lap early, each one after quicker than the last, and the last is what it answers
        Assert.InRange(given.Count, 2, 20);
        Assert.InRange(given[0].Drives, 8, 45);
        Assert.Same(found, given[^1]);
        for (int i = 1; i < given.Count; i++) Assert.True(given[i].Seconds < given[i - 1].Seconds, $"lap {i} given out is no quicker than the one before");
        // and past the first it is a lap learnt place by place, with the driver's settings its own
        if (laps >= Practice.MostDrives) Assert.True(found.Tune.Pace.Distinct().Count() > 4, "every place has much the same pace");
        Assert.NotEqual((Rival.Racing.Tune with { Pace = [] }).Written(), (found.Tune with { Pace = [] }).Written());

        // and what it found is clean for a lap of it driven afresh
        (bool clean, double seconds) = Practice.Lap(car, world, track.Road, [], route, found.Tune, [1.0 / 60.0]);
        Assert.True(clean, "the lap it found is not clean when driven again");
        Assert.InRange(seconds, found.Seconds - 3.0, found.Seconds + 0.01);

        // and opponents that take all of it, each with bends and a line of its own, stay on the road
        Rival.Learnt learnt = new(found.Seconds, found.Tune, Autopilot.RaceInside(car.Profile), [], "", Most: 1.0);
        Assert.Equal(found.Tune.Written(), Rival.Read(learnt.ToJson(), out string unread)!.Tune.Written());
        foreach (int seed in new[] { 1, 2, 3, 4, 5, 6 })
        {
            Rival.Way way = Rival.Drive(learnt, seed, 0.0, Autopilot.RaceInside(car.Profile), car.Profile.DisplayName);
            Route own = Route.Of(circuit, TrackWorld.DirOf, world.RadiusM, world.HeightAt, TrackWorld.LiftM, TrackWorld.SpacingM, null, 0.0,
                                 out string why, Autopilot.TurnRadius(car.Profile), way.InsideM, way.LineM) ?? throw new InvalidOperationException(why);
            (bool stays, double took) = Practice.Lap(car, world, track.Road, [], own, way.Tune, [0.02]);
            Assert.True(stays, $"opponent {seed} leaves the road with the practised lap");
            Assert.True(took < found.BlindSeconds, $"opponent {seed} took {took:F1} s, no quicker than the {found.BlindSeconds:F1} s of racing blind");
        }
    }

    [Fact]
    public void PractisingIsStoppedWhenItIsNoLongerWanted()
    {
        AutopilotTests.Track ring = AutopilotTests.Ring(60.0);
        TrackCar car = TrackCar.F2004;
        Route route = AutopilotTests.RouteOn(ring, car: car);
        int asked = 0;
        Assert.Null(Practice.Run(car, ring.World, ring.Road, [], route, Rival.Racing.Tune, () => ++asked > 3));
        Assert.InRange(asked, 4, 40);
    }

    [Fact]
    public void ALapThatGoesWrongSaysWhereAndACleanOneHowLongEachStretchTook()
    {
        AutopilotTests.Track ring = AutopilotTests.Ring(60.0);
        TrackCar car = TrackCar.F2004;
        Route route = AutopilotTests.RouteOn(ring, car: car);

        Practice.Drove clean = Practice.Drive(car, ring.World, ring.Road, [], route, Rival.Racing.Tune, [0.02]);
        Assert.True(clean.Clean);
        Assert.True(double.IsNaN(clean.WrongAt));
        Assert.Equal(Rival.Places, clean.Stretches.Length);
        Assert.All(clean.Stretches, s => Assert.True(s > 0.0));
        Assert.Equal(clean.Seconds, clean.Stretches.Sum(), 1);

        // twice what the ring bears, which no car holds
        Practice.Drove wrong = Practice.Drive(car, ring.World, ring.Road, [], route, Practice.Paced(Rival.Racing.Tune, 2.0), [0.02]);
        Assert.False(wrong.Clean);
        Assert.InRange(wrong.WrongAt, 0.0, 1.0 - 1e-9);
    }

    [Fact]
    public void PractiseWithNoLapsToLookWithIsTheQuickestPaceEverywhere()
    {
        AutopilotTests.Track ring = AutopilotTests.Ring(60.0);
        TrackCar car = TrackCar.F2004;
        Route route = AutopilotTests.RouteOn(ring, car: car);
        List<Practice.Found> given = [];
        Practice.Found? found = Practice.Run(car, ring.World, ring.Road, [], route, Rival.Racing.Tune, null, given.Add, 0);
        Assert.NotNull(found);
        Assert.Single(given);
        Assert.Single(found.Tune.Pace);
        Assert.Equal(Rival.Racing.Tune.LookAheadSeconds, found.Tune.LookAheadSeconds);
    }
}
