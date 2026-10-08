using System.Globalization;
using System.Text;
using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class RoadLoopTests
{
    private const double HalfWidth = 4.0;

    private static RoadLoop Loop(TrackWorld world, double length = 110.0, double shift = 12.0) =>
        new(world.Dir(0.0, 0.0) * world.RadiusM, world.Dir(1.0, 0.0) - world.Dir(0.0, 0.0), length, shift, HalfWidth);

    [Fact]
    public void ItLeavesTheGroundLevelGoesRightRoundAndComesBackLevelToOneSide()
    {
        TrackWorld world = TrackWorld.Earth();
        RoadLoop loop = Loop(world);
        double3 up = world.Dir(0.0, 0.0), east = Vec.Unit(world.Dir(1.0, 0.0) - up);

        double3 start = loop.Point(0.0, 0.0, out double3 along, out double3 facing);
        Assert.True(Vec.Len(start - (up * world.RadiusM)) < 1e-6);
        Assert.True(Vec.Dot(along, east) > 1.0 - 1e-9 && Vec.Dot(facing, up) > 1.0 - 1e-9);
        Assert.Equal(0.0, loop.Bend(0.0), 9);
        Assert.Equal(0.0, loop.Bend(loop.LengthM), 9);

        // Over the top it is upside down and going back the way it came.
        loop.Point(0.5 * loop.LengthM, 0.0, out along, out facing);
        Assert.True(Vec.Dot(along, east) < -1.0 + 1e-6 && Vec.Dot(facing, up) < -1.0 + 1e-6);
        Assert.InRange(loop.HeightM, 0.2 * loop.LengthM, 0.35 * loop.LengthM);

        double3 end = loop.Point(loop.LengthM, 0.0, out along, out facing);
        Assert.True(Vec.Dot(along, east) > 1.0 - 1e-6 && Vec.Dot(facing, up) > 1.0 - 1e-6);
        Assert.Equal(0.0, Vec.Dot(end - start, up), 3);
        Assert.Equal(loop.ShiftM, Vec.Dot(end - start, loop.Left), 6);

        // No step in where it is or which way it runs, all the way round.
        double3 last = start;
        for (double s = 0.5; s <= loop.LengthM; s += 0.5)
        {
            double3 here = loop.Point(s, 0.0, out _, out _);
            Assert.InRange(Vec.Len(here - last), 0.49, 0.52);
            last = here;
        }
    }

    [Fact]
    public void AWheelOverItIsToldHowFarOffItsAsphaltItIsTheWayThatFaces()
    {
        TrackWorld world = TrackWorld.Earth();
        RoadLoop loop = Loop(world);
        for (double s = 1.0; s < loop.LengthM; s += 3.7)
        {
            foreach (double left in new[] { -3.8, 0.0, 3.5 })
            {
                double3 on = loop.Point(s, left, out _, out double3 facing);
                Assert.True(loop.TryLocate(on + (facing * 0.33), out double along, out double aside, out double over, out double3 told), $"nothing at {s:F1} m, {left} m left");
                Assert.Equal(s, along, 3);
                Assert.Equal(left, aside, 3);
                Assert.Equal(0.33, over, 3);
                Assert.True(Vec.Dot(told, facing) > 1.0 - 1e-6);
            }
        }

        // Beside it, past its ends and well off it there is no loop.
        Assert.False(loop.TryLocate(loop.Point(30.0, HalfWidth + 0.5, out _, out double3 f) + (f * 0.3), out _, out _, out _, out _));
        Assert.False(loop.TryLocate(loop.Point(-5.0, 0.0, out _, out f) + (f * 0.3), out _, out _, out _, out _));
        Assert.False(loop.TryLocate(loop.Point(40.0, 0.0, out _, out f) + (f * 4.0), out _, out _, out _, out _));
    }

    internal sealed record Run(bool Out, double LeastWheels, double Deepest, double MostG, double LeastSpeed, double EndLeft, double EndUp, double EndAhead, string Told);

    // A car run at the loop along the road's middle and steered round it by a point ahead on its middle, the speed held.
    internal static Run Drive(TrackCar car, double speed, double dt, double length = 110.0, double shift = 11.0, string? csv = null)
    {
        TrackWorld world = TrackWorld.Earth();
        RoadLoop loop = Loop(world, length, shift);
        TrackRig rig = new(car, world) { Dt = dt };
        rig.Loops.Add(loop);
        rig.Place(-80.0, 0.0, 0.0);
        rig.Settle(1.0);
        rig.Launch(speed);

        double3 up = new(1, 0, 0), forward = new(0, 1, 0);
        double3 east = Vec.Unit(world.Dir(1.0, 0.0) - world.Dir(0.0, 0.0)), origin = world.Dir(0.0, 0.0) * world.RadiusM;
        double wheelbase = BuggyDrive.Wheelbase(car.Profile), along = -80.0;
        int least = 4, wheelsOff = 0;
        double deepest = double.NegativeInfinity, mostG = 0.0, slowest = speed;
        StringBuilder rows = new("t,s,left,speed,wheels,deepest,up,hub0,hub1,hub2,hub3,steer\n");
        bool through = false;
        for (int step = 0; step < 4000 && rig.Finite; step++)
        {
            // Where the car is along the loop, or along the level road before it and after it.
            double left;
            if (loop.TryLocate(rig.Position, out double s, out left, out _, out _)) along = s;
            else
            {
                double x = Vec.Dot(rig.Position - origin, east);
                bool after = along > 0.5 * loop.LengthM;
                along = after ? loop.LengthM + Vec.Dot(rig.Position - loop.Point(loop.LengthM, 0.0, out _, out _), east) : Math.Min(x, 0.0);
                left = Vec.Dot(rig.Position - origin, loop.Left) - (after ? loop.ShiftM : 0.0);
            }
            if (along > loop.LengthM + 60.0)
            {
                through = true;
                break;
            }

            double v = Vec.Len(rig.Velocity);
            double reach = Math.Max(6.0, 0.3 * v);
            double3 to = doubleQuat.Conjugate(rig.Attitude) * (loop.Point(along + reach, 0.0, out _, out _) - rig.Position);
            double bearing = Math.Atan2(to.Z, to.Y);
            double lockRad = BuggyDrive.SteerLock(car.Profile, v, world.Gravity, BuggyDrive.WingLoad(car.Profile, v, world.Air, car.MassKg));
            double steer = Math.Clamp(Math.Atan2(2.0 * wheelbase * Math.Sin(bearing), reach) / lockRad, -1.0, 1.0);

            double3 before = rig.Velocity;
            TrackSample sample = rig.Step(new DriveInput(Math.Clamp(0.5 * (speed - v), -1.0, 1.0), steer));
            if (along > 0.0 && along < loop.LengthM)
            {
                least = Math.Min(least, sample.Grounded);
                if (sample.Grounded < 4) wheelsOff++;
                deepest = Math.Max(deepest, sample.DeepestM);
                mostG = Math.Max(mostG, Vec.Len(((rig.Velocity - before) / sample.Dt) + (rig.Up * world.Gravity)) / world.Gravity);
                slowest = Math.Min(slowest, v);
            }
            rows.Append(string.Create(CultureInfo.InvariantCulture, $"{rig.Time:F3},{along:F2},{left:F2},{v:F2},{sample.Grounded},{sample.DeepestM:F3},{sample.UpDot:F2},{rig.HubHeights[0]:F3},{rig.HubHeights[1]:F3},{rig.HubHeights[2]:F3},{rig.HubHeights[3]:F3},{steer:F2}\n"));
        }
        if (csv is not null) File.WriteAllText(csv, rows.ToString());

        double3 end = rig.Position - loop.Point(loop.LengthM, 0.0, out _, out _);
        double endUp = Vec.Dot(rig.Attitude * up, rig.Up), endAhead = Vec.Dot(rig.Attitude * forward, east);
        return new Run(through, least, deepest, mostG, slowest, Vec.Dot(end, loop.Left), endUp, endAhead,
            $"{car.Name} at {speed:F0} m/s, {1000.0 * dt:F0} ms: {(through ? "through" : $"stopped at {along:F0} m")}, least wheels down {least}, {wheelsOff} steps with one off, "
            + $"springs {deepest * 100.0:F1} cm past their stops, {mostG:F1} g, slowest {slowest:F1} m/s, out {Vec.Dot(end, loop.Left):F2} m left, up {endUp:F2}, ahead {endAhead:F2}");
    }

    public static TheoryData<string, double, double> Runs()
    {
        TheoryData<string, double, double> runs = [];
        foreach (TrackCar car in TrackCar.All)
        {
            foreach (double speed in new[] { 25.0, 30.0 })
            {
                foreach (double dt in new[] { 1.0 / 60.0, 0.02 }) runs.Add(car.Name, speed, dt);
            }
        }
        return runs;
    }

    [Theory]
    [MemberData(nameof(Runs))]
    public void EveryCarGoesRoundOnItsWheelsAndComesOutStraightWhereTheLoopDoes(string name, double speed, double dt)
    {
        Run run = Drive(TrackCar.Of(name), speed, dt);

        Assert.True(run.Out, run.Told);
        Assert.True(run.LeastWheels == 4, run.Told);

        // On its bump stops and no further into the road than a step of the loop's bend carries it.
        Assert.True(run.Deepest < 0.03, run.Told);
        Assert.True(run.EndUp > 0.99 && run.EndAhead > 0.99 && Math.Abs(run.EndLeft) < 0.3, run.Told);
        Assert.True(run.LeastSpeed > 12.0, run.Told);
    }

    [Fact]
    public void ACarTooSlowForItComesOffTheTop()
    {
        // 15 m/s is not the 24 it takes to be carried 30 m up and still be pressed to the road there.
        Run run = Drive(TrackCar.Manx, 15.0, 1.0 / 60.0);
        Assert.True(run.LeastWheels == 0, run.Told);
    }

    [Fact]
    public void AWheelPastItsTravelIsHeldOnItsBumpStopAndOneInsideItIsLeftAlone()
    {
        // Four times its weight is 8 cm of the F2004's springs, which have 2: 5 cm in and still closing at 1 m/s.
        BuggyDrive drive = new(BuggyProfile.F2004);
        double3 up = new(1, 0, 0);
        double3[] hubs = [.. BuggyProfile.F2004.Corners.Select(c => c.Hub)];
        WheelContact[] pressed = [.. hubs.Select(h => new WheelContact(true, BuggyProfile.F2004.Corners[0].Radius - 0.05, up, new double3(-1.0, 30.0, 0.0)))];
        (double3, double3, double3) inertia = (new(1.0 / 400.0, 0, 0), new(0, 1.0 / 250.0, 0), new(0, 0, 1.0 / 900.0));
        DriveImpulse stop = drive.BumpStops(pressed, hubs, up, new double3(-1.0, 30.0, 0.0), default, 605.0, inertia, 0.02);
        Assert.True(stop.Linear.X / 605.0 > 0.9, $"a car going into its stops at 1 m/s is pushed {stop.Linear.X / 605.0:F2} m/s back");
        Assert.Equal(0.0, stop.Linear.Y, 6);

        // And nothing for a wheel inside its travel.
        WheelContact[] riding = [.. hubs.Select(h => new WheelContact(true, BuggyProfile.F2004.Corners[0].Radius, up, new double3(-1.0, 30.0, 0.0)))];
        Assert.Equal(default, drive.BumpStops(riding, hubs, up, new double3(-1.0, 30.0, 0.0), default, 605.0, inertia, 0.02));
    }

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("KSACARS_LOOP") is not { Length: > 0 } file) return;
        StringBuilder told = new();
        foreach (TrackCar car in TrackCar.All)
        {
            foreach (double speed in new[] { 20.0, 25.0, 30.0, 40.0 })
            {
                foreach (double dt in new[] { 1.0 / 60.0, 0.02, 1.0 / 30.0 })
                {
                    told.AppendLine(Drive(car, speed, dt, csv: car.Name == "F2004" && speed == 25.0 && dt == 0.02 ? file + ".csv" : null).Told);
                }
            }
        }
        File.WriteAllText(file, told.ToString());
    }
}
