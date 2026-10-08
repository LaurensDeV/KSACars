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
    public void ACircuitsLoopStandsWhereItsRoadsEndAndIsKeptInItsFile()
    {
        // A road east to a point, and one on from where a loop of 110 m comes down, 12 m to its left.
        TrackWorld world = TrackWorld.Earth();
        double on = 200.0 + RoadLoop.Reach(110.0);
        Circuit circuit = new Circuit { WidthM = 8.0, RadiusM = world.RadiusM }
            .AddNode(0.0, 0.0, out int a).Extend(a, 0.0, world.Deg(200.0), out int foot)
            .AddNode(world.Deg(12.0), world.Deg(on), out int down).Extend(down, world.Deg(12.0), world.Deg(on + 200.0), out _)
            .AddLoop(foot, down, 110.0);

        Circuit back = Circuit.FromJson(circuit.ToJson(), out string why, out _)!;
        Assert.True(back is not null, why);
        Assert.Equal([new Circuit.Loop(foot, down, 110.0)], back.Loops);
        Assert.Equal(circuit.ToJson(), back.ToJson());
        Assert.Null(back.RemoveNode(down).Loops);

        RoadLaying.Network net = RoadLaying.Laid(circuit, TrackWorld.DirOf, world.RadiusM, world.HeightAt, TrackWorld.LiftM, TrackWorld.SpacingM);
        RoadLoop loop = RoadLoop.Of(circuit.Loops![0], net.Ribbons, out why)!;
        Assert.True(loop is not null, why);
        Assert.Equal(12.0, loop.ShiftM, 2);
        Assert.Equal(4.0, loop.HalfWidthM, 6);

        // It starts on the first road's asphalt going the way that road arrives, and comes down on the second's.
        RoadSurface roads = RoadLaying.Surface(RoadLaying.Strips(net.Ribbons, world.HeightAt, TrackWorld.SpacingM));
        double3 start = loop.Point(0.0, 0.0, out double3 along, out _), end = loop.Point(loop.LengthM, 0.0, out _, out _);
        Assert.True(roads.TryHeightOver(loop.Point(-1.0, 0.0, out _, out _), out double over) && Math.Abs(over) < 0.01, $"{over:F3} m over the road it leaves");
        Assert.True(roads.TryHeightOver(loop.Point(loop.LengthM + 1.0, 0.0, out _, out _), out over) && Math.Abs(over) < 0.01, $"{over:F3} m over the road it comes down on");
        Assert.True(Vec.Dot(along, Vec.Unit(world.Dir(1.0, 0.0) - world.Dir(0.0, 0.0))) > 0.999);
        Assert.Equal(RoadLoop.Reach(110.0), Vec.Dot(end - start, along), 2);

        Assert.Null(RoadLoop.Of(new Circuit.Loop(a, 99, 110.0), net.Ribbons, out why));
        Assert.Contains("no road", why);
    }

    [Fact]
    public void ItsMeshIsWholeFacesOutAndHasTheRoadsLinesAlongIt()
    {
        RoadLoop loop = Loop(TrackWorld.Earth());
        RoadMeshData mesh = loop.Mesh(2048);
        Assert.True(mesh.Positions.Length <= 2048 && mesh.Indices.Length % 3 == 0);
        Assert.Equal(mesh.Indices.Length, mesh.AsphaltIndices + mesh.DeckIndices);

        for (int t = 0; t < mesh.Indices.Length; t += 3)
        {
            int a = mesh.Indices[t], b = mesh.Indices[t + 1], c = mesh.Indices[t + 2];
            double3 across = Vec.Cross(mesh.Places[b] - mesh.Places[a], mesh.Places[c] - mesh.Places[a]);
            Assert.True(Vec.Len(across) > 1e-3, "a triangle of the loop has no area");
            double3 said = new(mesh.Normals[a].X, mesh.Normals[a].Y, mesh.Normals[a].Z);
            Assert.True(Vec.Dot(Vec.Unit(across), said) > 0.9, $"triangle {t / 3} faces away from its own vertices");
            if (t >= mesh.AsphaltIndices) continue;

            // The asphalt is the loop's own, and takes of the lined picture what a road that long does.
            double3 middle = (mesh.Places[a] + mesh.Places[b] + mesh.Places[c]) / 3.0;
            Assert.True(loop.TryLocate(middle + (said * 0.3), out _, out _, out double over, out _) && Math.Abs(over - 0.3) < 0.02, $"triangle {t / 3} is {over:F3} m off the loop");
            double along = Math.Max(mesh.Uvs[a].Y, Math.Max(mesh.Uvs[b].Y, mesh.Uvs[c].Y)) - Math.Min(mesh.Uvs[a].Y, Math.Min(mesh.Uvs[b].Y, mesh.Uvs[c].Y));
            Assert.InRange(along, 0.5 / RoadTessellation.MarkingsM, 1.5 / RoadTessellation.MarkingsM);
        }
        Assert.Equal(40, loop.Mesh(9 * 41).Positions.Length / 9 - 1);
    }

    [Fact]
    public void ACarOnItIsSteeredTowardsItsMiddleAndOneOffItIsNot()
    {
        TrackWorld world = TrackWorld.Earth();
        RoadLoop loop = Loop(world);
        double3 at = loop.Point(20.0, 0.0, out double3 along, out double3 facing);
        double3 left = loop.Left;

        // The car's up, ahead and left as the loop's are there: X, Y and Z of the car.
        double3 X(doubleQuat q) => q * new double3(1, 0, 0);
        doubleQuat turn = FromAxes(facing, along, left);
        Assert.True(Vec.Len(X(turn) - facing) < 1e-9);

        // To the right of the middle it is steered left, to the left of it right, and the middle bears left as the loop does.
        Assert.True(loop.Steer(at - (left * 2.0) + (facing * 0.4), turn, 25.0, 3.0) > 0.05);
        Assert.True(loop.Steer(at + (left * 2.0) + (facing * 0.4), turn, 25.0, 3.0) < -0.05);
        Assert.InRange(loop.Steer(at + (facing * 0.4), turn, 25.0, 3.0)!.Value, 0.0, 0.1);
        Assert.Null(loop.Steer(at + (facing * 5.0), turn, 25.0, 3.0));
    }

    // The turn that carries a car's X, Y and Z to three ways at right angles.
    private static doubleQuat FromAxes(double3 x, double3 y, double3 z)
    {
        double trace = x.X + y.Y + z.Z;
        if (trace > 0.0)
        {
            double s = 0.5 / Math.Sqrt(trace + 1.0);
            return new doubleQuat((y.Z - z.Y) * s, (z.X - x.Z) * s, (x.Y - y.X) * s, 0.25 / s);
        }
        if (x.X > y.Y && x.X > z.Z)
        {
            double s = 2.0 * Math.Sqrt(1.0 + x.X - y.Y - z.Z);
            return new doubleQuat(0.25 * s, (y.X + x.Y) / s, (z.X + x.Z) / s, (y.Z - z.Y) / s);
        }
        if (y.Y > z.Z)
        {
            double s = 2.0 * Math.Sqrt(1.0 + y.Y - x.X - z.Z);
            return new doubleQuat((y.X + x.Y) / s, 0.25 * s, (z.Y + y.Z) / s, (z.X - x.Z) / s);
        }
        double r = 2.0 * Math.Sqrt(1.0 + z.Z - x.X - y.Y);
        return new doubleQuat((z.X + x.Z) / r, (z.Y + y.Z) / r, 0.25 * r, (x.Y - y.X) / r);
    }

    // A lap with a loop in it: east along a straight, round the loop, on and back round by the north.
    private static (AutopilotTests.Track Track, RoadLoop Loop, int[] Path) LoopedLap(bool closed)
    {
        TrackWorld world = TrackWorld.Earth();
        double on = 300.0 + RoadLoop.Reach(110.0);
        Circuit c = new Circuit { WidthM = 8.0, RadiusM = world.RadiusM }
            .AddNode(0.0, 0.0, out int start).Extend(start, 0.0, world.Deg(300.0), out int foot)
            .AddNode(world.Deg(12.0), world.Deg(on), out int down).Extend(down, world.Deg(12.0), world.Deg(on + 200.0), out int east)
            .AddLoop(foot, down, 110.0);
        int[] path = [start, foot, down, east];
        if (closed)
        {
            c = c.Extend(east, world.Deg(110.0), world.Deg(on + 290.0), out int a).Extend(a, world.Deg(220.0), world.Deg(on + 200.0), out int b)
                 .Extend(b, world.Deg(220.0), world.Deg(-150.0), out int d).Extend(d, world.Deg(110.0), world.Deg(-240.0), out int e)
                 .Extend(e, 0.0, world.Deg(-150.0), out int f).Connect(f, start);
            path = [start, foot, down, east, a, b, d, e, f, start];
        }
        RoadLaying.Network net = RoadLaying.Laid(c, TrackWorld.DirOf, world.RadiusM, world.HeightAt, TrackWorld.LiftM, TrackWorld.SpacingM);
        return (new AutopilotTests.Track(world, c, world.Surface(c)), RoadLoop.Of(c.Loops![0], net.Ribbons, out _)!, path);
    }

    [Fact]
    public void ARouteGoesRoundALoopAndOnByTheRoadItComesDownOn()
    {
        (AutopilotTests.Track track, RoadLoop loop, int[] path) = LoopedLap(closed: false);
        Route named = AutopilotTests.RouteOn(track, path), followed = AutopilotTests.RouteOn(track);

        // The road to it, all of it, and the road on; and found without being told the way.
        Assert.Equal(300.0 + 110.0 + 200.0, named.LengthM, 0);
        Assert.Equal(named.LengthM, followed.LengthM, 6);
        Assert.InRange(named.Samples.ToArray().Count(x => x.LoopM > 0.0), 105, 115);

        double last = -1.0;
        double3 before = named.Samples[0].At;
        foreach (Route.Sample sample in named.Samples)
        {
            Assert.True(sample.S > last, "the route goes back on itself");
            Assert.True(Vec.Len(sample.At - before) < 1.6, $"a step of {Vec.Len(sample.At - before):F2} m in the route at {sample.S:F0} m");
            (last, before) = (sample.S, sample.At);
        }

        // Over its top the route is upside down where the loop is, and a car there is found on it and not on the road below.
        int index = named.IndexAt(300.0 + 55.0);
        Route.Sample top = named.Samples[index];
        Assert.True(Vec.Dot(top.Tangent, named.Samples[0].Tangent) < -0.99);
        Assert.True(Vec.Len(top.At - loop.Point(top.S - 300.0, 0.0, out _, out _)) < 0.6);
        int near = index - 3;
        named.Locate(top.At + (loop.Left * 1.5), ref near, 20.0, out double s, out double cross);
        Assert.Equal(top.S, s, 0);
        Assert.Equal(1.5, cross, 1);

        (double3 at, double3 up, double3 ahead) = named.Standing(300.0 + 55.0);
        Assert.True(Vec.Dot(up, Vec.Unit(at)) < -0.99 && Vec.Dot(ahead, top.Tangent) > 0.99);
    }

    [Theory]
    [InlineData("F2004", false, 1.0 / 60.0)]
    [InlineData("F2004", true, 0.02)]
    [InlineData("Eldorado", true, 1.0 / 60.0)]
    [InlineData("Eldorado", false, 0.02)]
    public void TheDriverTakesACarRoundALoopInALap(string name, bool closed, double dt)
    {
        (AutopilotTests.Track track, RoadLoop loop, int[] path) = LoopedLap(closed);
        TrackCar car = TrackCar.Of(name);
        Route route = AutopilotTests.RouteOn(track, path, car: car);

        int fewest = 4, steps = 0;
        double deepest = double.NegativeInfinity;
        double slowest = double.PositiveInfinity, fastest = 0.0;
        AutopilotTests.Lap lap = AutopilotTests.Drive(car, track, route, dt, laps: 1, each: (rig, pilot) =>
        {
            if (rig.Loops.Count == 0) rig.Loops.Add(loop);
            if (!loop.TryLocate(rig.Position, out _, out _, out _, out _)) return;
            steps++;
            fewest = Math.Min(fewest, rig.Drive.Grounded.Count(g => g));
            for (int i = 0; i < 4; i++) deepest = Math.Max(deepest, car.Profile.Corners[i].Radius - car.Profile.BumpTravel - rig.HubHeights[i]);
            double speed = Vec.Len(rig.Velocity);
            (slowest, fastest) = (Math.Min(slowest, speed), Math.Max(fastest, speed));
        });

        Assert.True(lap.End == LapEnd.Finished, lap.Told);
        Assert.True(steps > 100 && fewest == 4 && deepest < 0.04, $"on the loop for {steps} steps, fewest wheels down {fewest}, {deepest * 100.0:F1} cm past the stops; {lap.Told}");
        Assert.True(lap.Summary.OffAsphaltSeconds == 0.0 && lap.HullSteps == 0, lap.Told);

        // In at the speed that carries it over, whatever it could have been doing on the straight.
        double into = Autopilot.LoopSpeed(110.0, track.World.Gravity);
        Assert.True(fastest < into + 6.0 && slowest > 12.0, $"{slowest:F1} to {fastest:F1} m/s on a loop gone into at {into:F1}; {lap.Told}");
    }

    [Fact]
    public void TheLineToDriveGoesRoundTheLoopOnItsAsphalt()
    {
        (AutopilotTests.Track track, RoadLoop loop, int[] path) = LoopedLap(closed: false);
        Route route = AutopilotTests.RouteOn(track, path);
        int on = 0;
        foreach (RaceLine.Arrow arrow in RaceLine.Lay(route, track.Road))
        {
            if (!(route.Samples[arrow.Sample].LoopM > 0.0) || arrow.S < 303.0) continue;
            on++;
            foreach (double3 corner in new[] { arrow.Tip, arrow.Left, arrow.Notch, arrow.Right })
            {
                Assert.True(loop.TryLocate(corner, out _, out _, out double over, out double3 facing), $"an arrowhead at {arrow.S:F0} m is not on the loop");
                Assert.InRange(over, 0.02, RaceLine.LiftM + RaceLine.LoopLiftM + 0.02);
                Assert.True(Vec.Dot(facing, arrow.Facing) > 0.99);
            }
        }
        Assert.InRange(on, 32, 37);
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
