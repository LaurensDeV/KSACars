using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class TrackRigTests
{
    private static BuggyDriveTests.Rig FlatRig(TrackCar car, double dt) =>
        new(dt, car.Profile, car.Com) { RigMass = car.MassKg, Box = car.Name == "F2004" ? new double3(0.5, 5.6, 2.0) : null };

    // The flat Rig is what every car was tuned against, so on a sphere with nothing on it this one has
    // to be that rig: a planet's curve under a wheelbase is a fraction of a micron.
    [Theory]
    [InlineData("Manx", 1.0 / 60.0)]
    [InlineData("Manx", 1.0 / 30.0)]
    [InlineData("Eldorado", 1.0 / 60.0)]
    [InlineData("Eldorado", 1.0 / 30.0)]
    [InlineData("F2004", 1.0 / 60.0)]
    [InlineData("F2004", 1.0 / 30.0)]
    public void OnABareSphereItSettlesAndPullsAwayAsTheFlatRigDoes(string name, double dt)
    {
        TrackCar car = TrackCar.Of(name);
        BuggyDriveTests.Rig flat = FlatRig(car, dt);
        flat.Position = car.Com + new double3(0.05, 0, 0);
        TrackRig rig = new(car, TrackWorld.Earth()) { Dt = dt };
        rig.Position += rig.Up * 0.05;

        for (double t = 0.0; t < 6.0; t += dt)
        {
            flat.Step(default);
            rig.Step(default);
        }
        double rests = rig.TerrainUnderM - flat.Position.X;
        Assert.True(Math.Abs(rests) < 1e-5, $"rests {rests * 1e6:F1} microns from where the flat rig does");
        Assert.True(Vec.Len(rig.Velocity) < 0.01, $"still moving at {Vec.Len(rig.Velocity):F3} m/s");

        for (double t = 0.0; t < 5.0; t += dt)
        {
            flat.Step(new DriveInput(1.0, 0.0));
            rig.Step(new DriveInput(1.0, 0.0));
        }
        double speed = Vec.Dot(rig.Velocity, rig.Forward);
        Assert.True(Math.Abs(speed - flat.Velocity.Y) < 0.001 * flat.Velocity.Y,
                    $"{speed:F3} m/s after five seconds against the flat rig's {flat.Velocity.Y:F3}");
        double gone = flat.Position.Y - car.Com.Y;
        Assert.True(Math.Abs(rig.Where.East - gone) < 0.001 * gone, $"{rig.Where.East:F2} m gone against the flat rig's {gone:F2}");
        Assert.True(rig.Log[^1].UpDot > 0.999, "tipped");
        Assert.DoesNotContain(rig.Log, s => s.HullDown);
    }

    [Theory]
    [InlineData("Manx")]
    [InlineData("Eldorado")]
    [InlineData("F2004")]
    public void ItIsPlacedStandingOnASlopeASideSlopeAndADeckAndStaysThere(string name)
    {
        TrackCar car = TrackCar.Of(name);
        TrackWorld deckWorld = TrackWorld.Earth();
        Circuit deck = new Circuit().AddNode(0.0, deckWorld.Deg(-100.0), out int a).Extend(a, 0.0, deckWorld.Deg(100.0), out int b)
            .SetHeight(a, 3.0).SetHeight(b, 3.0);

        (TrackWorld World, RoadSurface? Road, double Above, double Heading, string What)[] places =
        [
            (TrackWorld.Earth(TrackWorld.Slope(0.2)), null, 0.0, 0.0, "facing up a 20% slope"),
            (TrackWorld.Earth(TrackWorld.Slope(0.2)), null, 0.0, 180.0, "facing down a 20% slope"),
            (TrackWorld.Earth(TrackWorld.SideSlope(0.15)), null, 0.0, 0.0, "across a 15% slope"),
            (TrackWorld.Luna(TrackWorld.Slope(-0.1)), null, 0.0, 30.0, "on a 10% slope on Luna"),
            (deckWorld, deckWorld.Surface(deck), 3.0, 0.0, "on a deck 3 m up"),
        ];
        foreach ((TrackWorld world, RoadSurface? road, double above, double heading, string what) in places)
        {
            TrackRig rig = new(car, world, road);
            rig.Place(10.0, 0.0, heading, above);
            (double east, double north) = rig.Where;
            for (double t = 0.0; t < 3.0; t += rig.Dt) rig.Step(default);

            TrackSample last = rig.Log[^1];
            // A standing tyre gives back 0.7 of its speed a step, so on a slope the car creeps at what
            // gravity adds in a step over that: 5 cm/s on 20% at 60 Hz.
            Assert.True(Math.Abs(rig.Where.East - east) + Math.Abs(rig.Where.North - north) < 0.4,
                        $"{what}: crept {rig.Where.East - east:F3} m east and {rig.Where.North - north:F3} m north");
            Assert.True(Vec.Len(rig.Velocity) < 0.15, $"{what}: still moving at {Vec.Len(rig.Velocity):F3} m/s");
            Assert.True(Math.Abs(last.PitchDeg) < 2.0 && Math.Abs(last.RollDeg) < 2.0,
                        $"{what}: {last.PitchDeg:F2} deg of pitch and {last.RollDeg:F2} of roll off the ground under it");
            Assert.Equal(4, last.Grounded);
            Assert.Equal(road is null ? 0 : 4, last.OnRoad);
            Assert.DoesNotContain(rig.Log, s => s.HullDown);
        }
    }

    [Fact]
    public void OverASinusoidAndAStepInTheGroundItStaysOnItsWheels()
    {
        foreach (TrackCar car in TrackCar.All)
        {
            TrackRig waves = new(car, TrackWorld.Earth(TrackWorld.Sinusoid(0.3, 40.0)));
            waves.Launch(15.0);
            for (double t = 0.0; t < 8.0; t += waves.Dt) waves.Step(default);
            Assert.True(waves.Finite && waves.Log.Min(s => s.UpDot) > 0.9, $"{car.Name} over the waves");
            Assert.True(waves.Where.East > 80.0, $"{car.Name} got {waves.Where.East:F0} m over the waves");

            TrackRig kerb = new(car, TrackWorld.Earth(TrackWorld.Step(-0.1, 20.0)));
            kerb.Launch(10.0);
            for (double t = 0.0; t < 5.0; t += kerb.Dt) kerb.Step(default);
            Assert.True(kerb.Finite && kerb.Log.Min(s => s.UpDot) > 0.9, $"{car.Name} off the kerb");
            Assert.True(Math.Abs(kerb.TerrainUnderM - car.Com.X) < 0.03, $"{car.Name} rests {kerb.TerrainUnderM:F3} m up after the kerb");
        }
    }

    // A run that went wrong on a frame-time hitch is only a bug report if it can be run again.
    [Fact]
    public void ARunOnRandomStepsIsTheSameRunWhenItsStepsArePlayedBack()
    {
        TrackCar car = TrackCar.Eldorado;
        TrackWorld world = TrackWorld.Earth(TrackWorld.Sinusoid(0.1, 60.0));
        TrackRig random = new(car, world) { Jitter = new Random(20261007) };
        random.Launch(20.0);
        for (int i = 0; i < 400; i++) random.Step(new DriveInput(0.5, 0.1));

        Assert.InRange(random.Log.Min(s => s.Dt), TrackRig.LeastStep, 0.01);
        Assert.InRange(random.Log.Max(s => s.Dt), 0.09, TrackRig.MostStep);
        Assert.True(random.Finite && random.Log.Min(s => s.UpDot) > 0.9, "the random steps upset it");

        TrackRig replay = new(car, world) { Pattern = [.. random.Log.Select(s => s.Dt)] };
        replay.Launch(20.0);
        for (int i = 0; i < 400; i++) replay.Step(new DriveInput(0.5, 0.1));

        Assert.Equal(random.Position, replay.Position);
        Assert.Equal(random.Velocity, replay.Velocity);
        Assert.Equal(random.Spin, replay.Spin);
    }
}
