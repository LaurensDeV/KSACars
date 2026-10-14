using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSACars.Tests;

// A driver that runs to one side of its line, the choice of when to go round a car, and the two together: two cars in two rigs on one road.
public class PassingTests(ITestOutputHelper output)
{
    private const double Room = 3.3, Far = 600.0;

    private static AutopilotTests.Track Long() => LongTrack.Value;

    private static readonly Lazy<AutopilotTests.Track> LongTrack = new(() =>
    {
        TrackWorld world = TrackWorld.Earth();
        Circuit c = AutopilotTests.Through(world, false, (0.0, 0.0, 0.0), (1000.0, 0.0, 0.0), (2000.0, 0.0, 0.0), (3000.0, 0.0, 0.0));
        return new AutopilotTests.Track(world, c, world.Surface(c));
    });

    public static TheoryData<string, string> OffsetRuns()
    {
        TheoryData<string, string> runs = [];
        foreach (TrackCar car in TrackCar.All)
        {
            runs.Add(car.Name, "straight");
            runs.Add(car.Name, "ring");
        }
        return runs;
    }

    [Theory]
    [MemberData(nameof(OffsetRuns))]
    public void TheDriverRunsToTheSideItIsAskedEasedAcrossAndHeldOnTheAsphalt(string name, string where)
    {
        TrackCar car = TrackCar.Of(name);
        AutopilotTests.Track track = where == "ring" ? AutopilotTests.Ring(300.0) : Long();
        Route route = AutopilotTests.RouteOn(track, car: car);
        double most = (0.5 * AutopilotTests.Width) - Autopilot.RaceInside(car.Profile);

        List<(double T, double Cross, double Aim)> seen = [];
        double step = 0.0;
        AutopilotTests.Lap lap = AutopilotTests.Drive(car, track, route, 1.0 / 60.0, cruise: 20.0, timeout: 50.0, each: (rig, pilot) =>
        {
            double t = pilot.Summary.Seconds;
            pilot.SideM = t < 8.0 ? 0.0 : t < 20.0 ? 2.0 : t < 32.0 ? -2.0 : 10.0;
            if (seen.Count > 0) step = Math.Max(step, Math.Abs(pilot.SideNowM - seen[^1].Aim));
            seen.Add((t, pilot.CrossM, pilot.SideNowM));
        });

        Assert.True(lap.End == LapEnd.TimedOut, lap.Told);
        Assert.True(lap.Summary.OffAsphaltSeconds == 0.0, lap.Told);
        double Worst(double from, double to, double want) => seen.Where(s => s.T >= from && s.T < to).Max(s => Math.Abs(s.Cross - want));
        Assert.True(Worst(4.0, 8.0, 0.0) < 0.3, $"on its line: {Worst(4.0, 8.0, 0.0):F2} m out");
        Assert.True(Worst(15.0, 20.0, 2.0) < 0.3, $"2 m to the left: {Worst(15.0, 20.0, 2.0):F2} m out");
        Assert.True(Worst(27.0, 32.0, -2.0) < 0.3, $"2 m to the right: {Worst(27.0, 32.0, -2.0):F2} m out");

        // moved across no faster than it is allowed, so 4 m takes two seconds and more
        Assert.True(step <= (Autopilot.SideRateMs / 60.0) + 1e-9, $"the aim stepped {step:F3} m");
        Assert.True(seen.First(s => s.T >= 21.0).Cross > 0.0, "a second after it was asked to cross, it was across");
        double fastest = 0.0;
        for (int i = 30; i < seen.Count; i++) fastest = Math.Max(fastest, Math.Abs(seen[i].Cross - seen[i - 30].Cross) / (seen[i].T - seen[i - 30].T));
        Assert.True(fastest < 1.5 * Autopilot.SideRateMs, $"it went across at {fastest:F1} m/s");

        // asked for more than the road has, it has what leaves the racing line's margin
        Assert.True(seen.Max(s => s.Aim) <= most + 1e-9 && seen[^1].Aim > most - 0.05, $"aimed {seen[^1].Aim:F2} m out with {most:F2} m of room");
        Assert.True(seen.Max(s => Math.Abs(s.Cross)) < most + 0.3, $"{seen.Max(s => Math.Abs(s.Cross)):F2} m out with {most:F2} m of room");
        Assert.True(lap.Summary.MaxCrossM < 2.0 * Autopilot.SideRateMs, $"it was {lap.Summary.MaxCrossM:F2} m from the line it was asked to be on");
    }

    [Fact]
    public void TheRoadAheadIsItsRoomEitherSideAndHowFarOnItFirstBends()
    {
        TrackCar car = TrackCar.F2004;
        double most = (0.5 * AutopilotTests.Width) - Autopilot.RaceInside(car.Profile);
        (double Left, double Right, double Straight) Ahead(AutopilotTests.Track track, double perM)
        {
            (double, double, double) found = default;
            AutopilotTests.Drive(car, track, AutopilotTests.RouteOn(track, car: car), 1.0 / 60.0, cruise: 10.0, timeout: 2.0, each: (_, pilot) =>
            {
                if (pilot.RoadAhead(Rival.PassLookM, perM, out double l, out double r, out double s)) found = (l, r, s);
            });
            return found;
        }
        (double left, double right, double straight) = Ahead(Long(), 1.0 / 1000.0);
        Assert.Equal(most, left, 2);
        Assert.Equal(most, right, 2);
        Assert.InRange(straight, Autopilot.StraightLookM - 2.0, Autopilot.StraightLookM);

        // a ring of 300 m is straight to a driver that calls 1/200 a bend and not to one that calls 1/400 one
        Assert.True(Ahead(AutopilotTests.Ring(300.0), 1.0 / 200.0).Straight > 500.0);
        Assert.True(Ahead(AutopilotTests.Ring(300.0), 1.0 / 400.0).Straight < 5.0);
    }

    private static Rival.Seen Car(string who, double ahead, double side, double speed) => new(who, ahead, side, speed);

    private static (double SideM, double CapMs) Look(Rival.Passing state, Rival.Seen[] cars, double speed = 22.0, double free = 50.0, double cross = 0.0,
                                                     double left = Room, double right = Room, double straight = Far, double dt = 1.0 / 60.0) =>
        Rival.Pass(state, cars, speed, free, cross, left, right, straight, dt);

    [Fact]
    public void BehindASlowerCarOnAStraightWithRoomItGoesOutToOneSide()
    {
        Rival.Passing state = new();
        (double side, _) = Look(state, [Car("slow", 30.0, 0.0, 20.0)]);
        Assert.Equal("slow", state.Who);
        Assert.Equal(Rival.PassSideM, Math.Abs(side), 9);

        // with nobody ahead, or a car off to the side, it is on its line and free
        Assert.Equal((0.0, double.PositiveInfinity), Look(new(), []));
        Assert.Equal((0.0, double.PositiveInfinity), Look(new(), [Car("beside", 30.0, 3.5, 20.0)]));
    }

    [Theory]
    [InlineData(5.0, Room, 0.0, 1)]
    [InlineData(Room, 5.0, 0.0, -1)]
    // the car is a metre left of the line: only its right has the room
    [InlineData(Room, Room, 1.0, -1)]
    [InlineData(Room, Room, -1.0, 1)]
    public void ItTakesTheSideWithMoreRoom(double left, double right, double theirs, int expected)
    {
        Rival.Passing state = new();
        (double side, _) = Look(state, [Car("slow", 30.0, theirs, 20.0)], left: left, right: right);
        Assert.Equal(expected, state.Side);
        Assert.Equal(theirs + (expected * Rival.PassSideM), side, 9);
    }

    [Fact]
    public void ItStaysBehindWhereAPassWouldNotBeOne()
    {
        Rival.Seen[] slow = [Car("slow", 30.0, 0.0, 20.0)];
        double follow = Rival.FollowCap(30.0, 0.0, 20.0, 22.0);
        void Stays(string why, (double SideM, double CapMs) got, Rival.Passing state)
        {
            Assert.True(state.Side == 0 && got.SideM == 0.0, why);
            Assert.Equal(follow, got.CapMs, 9);
        }

        Rival.Passing s = new();
        // a car not far under what the road allows, which is only gone round on a straight
        Stays("into a bend", Look(s, slow, free: 30.0, straight: 80.0), s);
        Stays("on a road too narrow for two", Look(s = new(), slow, left: 2.4, right: 2.4), s);
        Stays("behind a car hardly slower than it may go", Look(s = new(), slow, free: 20.0 + Rival.PassFasterMs - 0.5), s);
        Stays("with a car alongside on the only side with room",
              Look(s = new(), [.. slow, Car("third", 4.0, Rival.PassSideM, 22.0)], left: 4.0, right: 2.0), s);
        Stays("with a car just beyond on the only side with room",
              Look(s = new(), [.. slow, Car("third", 45.0, Rival.PassSideM, 20.0)], left: 4.0, right: 2.0), s);

        // the third car has one side: the other is taken
        Look(s = new(), [.. slow, Car("third", 4.0, Rival.PassSideM, 22.0)]);
        Assert.Equal(-1, s.Side);
    }

    [Fact]
    public void TheCarHoldsItBackUntilItIsOutFromBehindIt()
    {
        Rival.Passing state = new();
        Rival.Seen[] behind = [Car("slow", 30.0, 0.0, 20.0)];

        // coming up on it too fast to be out in time, it is held as a follower is
        (double side, double cap) = Look(state, behind, speed: 40.0);
        Assert.Equal(Rival.PassSideM, side, 9);
        Assert.Equal(Rival.FollowCap(30.0, 0.0, 20.0, 40.0), cap, 9);

        // half out it still is
        (_, cap) = Look(state, [Car("slow", 25.0, -1.5, 20.0)], speed: 30.0, cross: 1.5);
        Assert.Equal(Rival.FollowCap(25.0, 0.0, 20.0, 30.0), cap, 9);

        // and out, it is not
        (side, cap) = Look(state, [Car("slow", 22.0, -(Rival.AbreastM + Rival.PassMarginM + 0.05), 20.0)], speed: 25.0, cross: 2.55);
        Assert.True(double.IsPositiveInfinity(cap), $"held to {cap:F1} m/s out from behind it");
        Assert.Equal(Rival.PassSideM, side, 9);

        // a pass begun at the car's own speed, with time to move out, is not held at all
        Assert.True(double.IsPositiveInfinity(Look(new(), behind, speed: 21.0).CapMs));
    }

    [Fact]
    public void ItHoldsItsSideUntilItIsPastAndThenComesBackToItsLine()
    {
        Rival.Passing state = new();
        Look(state, [Car("slow", 30.0, 0.0, 20.0)]);
        foreach (double ahead in new[] { 10.0, 0.0, -5.0, -(Rival.PassClearM - 0.5) })
        {
            (double side, double cap) = Look(state, [Car("slow", ahead, -3.0, 20.0)], speed: 35.0, cross: 3.0);
            Assert.Equal(Rival.PassSideM, side, 9);
            Assert.True(double.IsPositiveInfinity(cap));
        }
        (double back, _) = Look(state, [Car("slow", -(Rival.PassClearM + 0.5), -3.0, 20.0)], speed: 35.0, cross: 3.0);
        Assert.Equal(0.0, back);
        Assert.Equal(0, state.Side);
    }

    [Fact]
    public void APassThatCannotBeFinishedIsGivenUpAndItGoesBackInBehind()
    {
        Rival.Passing state = new();
        Look(state, [Car("slow", 30.0, 0.0, 20.0)]);

        // the straight runs out with the car still ahead: a moment on, it drops back, on the side it is on
        (double side, double cap) = Look(state, [Car("slow", 6.0, -3.0, 20.0)], speed: 24.0, free: 30.0, cross: 3.0, straight: 20.0);
        Assert.False(state.Back);
        (side, cap) = Look(state, [Car("slow", 6.0, -3.0, 20.0)], speed: 24.0, free: 30.0, cross: 3.0, straight: 20.0, dt: Rival.UnfitSeconds);
        Assert.True(state.Back);
        Assert.Equal(Rival.PassSideM, side, 9);
        Assert.Equal(20.0 - Rival.DropMs, cap, 9);

        // and does not change its mind when the road opens again
        (side, cap) = Look(state, [Car("slow", 8.0, -3.0, 20.0)], speed: 18.0, cross: 3.0);
        Assert.True(state.Back && side == Rival.PassSideM && cap == 20.0 - Rival.DropMs);

        // far enough behind, it goes in, held as a follower
        (side, cap) = Look(state, [Car("slow", Rival.TuckM + 0.5, -3.0, 20.0)], speed: 17.0, cross: 3.0);
        Assert.Equal(0.0, side);
        Assert.Equal(0, state.Side);
        Assert.Equal(20.0, cap, 9);

        // and waits before it tries again
        Look(state, [Car("slow", 12.0, 0.0, 20.0)], speed: 20.0, dt: Rival.RestSeconds - 0.5);
        Assert.Equal(0, state.Side);
        Look(state, [Car("slow", 12.0, 0.0, 20.0)], speed: 20.0, dt: 1.0);
        Assert.NotEqual(0, state.Side);
    }

    [Fact]
    public void BesideTheCarWhenTheStraightRunsOutItGoesOn()
    {
        Rival.Passing state = new();
        Look(state, [Car("slow", 30.0, 0.0, 20.0)]);
        (double side, double cap) = Look(state, [Car("slow", Rival.LevelM - 0.5, -3.0, 20.0)], speed: 30.0, cross: 3.0, straight: 0.0);
        Assert.False(state.Back);
        Assert.Equal(Rival.PassSideM, side, 9);
        Assert.True(double.IsPositiveInfinity(cap));
    }

    [Fact]
    public void ItKeepsTheSideItChoseWhateverTheRoomDoes()
    {
        Rival.Passing state = new();
        Look(state, [Car("slow", 30.0, 0.0, 20.0)], left: 5.0, right: Room);
        Assert.Equal(1, state.Side);
        for (int i = 0; i < 200; i++)
        {
            // the roomier side is now the left and now the right, and the car wanders on its line
            bool odd = i % 2 == 1;
            double theirs = odd ? 0.3 : -0.3, mine = Math.Min(i * 0.05, 3.0);
            (double side, _) = Look(state, [Car("slow", 30.0 - (i * 0.1), theirs - mine, 20.0)], cross: mine, left: odd ? Room : 5.0, right: odd ? 5.0 : Room);
            Assert.Equal(1, state.Side);
            Assert.True(side > theirs + Rival.AbreastM, $"asked to {side:F2} m with the car at {theirs:F2}");
        }
    }

    [Fact]
    public void PastOneCarItDoesNotComeBackOntoAnotherBesideIt()
    {
        Rival.Passing state = new();
        Look(state, [Car("a", 30.0, 0.0, 20.0)]);
        Rival.Seen[] two = [Car("a", -(Rival.PassClearM + 0.5), -3.0, 20.0), Car("b", 1.0, -3.0, 20.0)];
        (double side, double cap) = Look(state, two, speed: 35.0, cross: 3.0);
        Assert.Equal("b", state.Who);
        Assert.Equal(1, state.Side);
        Assert.Equal(Rival.PassSideM, side, 9);
        Assert.True(double.IsPositiveInfinity(cap));

        // nor across it to a roomier side
        state = new();
        Look(state, [Car("b", 15.0, -3.0, 20.0)], speed: 30.0, cross: 3.0, left: Room, right: 9.0);
        Assert.Equal(1, state.Side);
    }

    private sealed class Runner(TrackCar car, AutopilotTests.Track track, Route route, double startM, double dt, double cruise = 0.0)
    {
        public TrackCar Car { get; } = car;
        public TrackRig Rig { get; } = Stood(car, track, route, startM, dt);
        public Autopilot Pilot { get; } = new(car.Profile, route, track.Road, car.MassKg, track.World.Gravity, track.World.Air, 100, cruise, 600.0);
        public Rival.Passing Passing { get; } = new();

        public double3 Facing => Rig.Attitude * new double3(0, 1, 0);
        public double HalfWidth => Car.Profile.Corners.Max(c => Math.Abs(c.Hub.Z)) + 0.2;
        public double HalfLength => 0.5 * BuggyDrive.Wheelbase(Car.Profile) + 1.0;

        private static TrackRig Stood(TrackCar car, AutopilotTests.Track track, Route route, double s, double dt)
        {
            TrackRig rig = new(car, track.World, track.Road) { KeepsLog = false };
            (double3 at, _, double3 ahead) = route.Standing(s);
            (double east, double north) = track.World.Flatten(at);
            rig.Place(east, north, Math.Atan2(ahead.Z, ahead.Y) * 180.0 / Math.PI, Vec.Len(at) - track.World.RadiusM - track.World.HeightAt(Vec.Unit(at)));
            rig.Settle(1.0);
            rig.Dt = dt;
            return rig;
        }

        // As Opponents looks: every other car as this one's driver sees it, the road ahead, and what to do about it.
        public Rival.Seen[] Look(IEnumerable<Runner> others, double dt)
        {
            Rival.Seen[] seen = [.. others.Select(o => Rival.See(o, Pilot, Rig.Position, Facing, o.Rig.Position, o.Facing, o.Rig.Velocity, 250.0, Vec.Len(Rig.Velocity))).OfType<Rival.Seen>()];
            double free = Pilot.FreeMs, left = 0.0, right = 0.0, straight = 0.0;
            Pilot.SqueezeM = Rival.Squeeze(seen, free);
            Pilot.RoadAhead(Rival.RoomOver(seen, Vec.Len(Rig.Velocity)), Rival.StraightPerM(Math.Max(free, Vec.Len(Rig.Velocity))), out left, out right, out straight, Rival.RoomFrom(seen, Vec.Len(Rig.Velocity)));
            (Pilot.SideM, Pilot.CapMs) = Rival.Pass(Passing, seen, Vec.Len(Rig.Velocity), free, Pilot.CrossM, left, right, straight, dt);
            return seen;
        }

        public void Step()
        {
            Rig.Driver = Pilot;
            Rig.Step(default);
        }
    }

    private sealed record Raced(double PassedAt, double BeganAt, double BackAt, double ClosingMs, double NearestM, double LeastBesideM, double MostCrossM, double LostMs, Runner Fast, Runner Slow, string Told);

    // A slow car ahead held to a speed and a quick one behind, each in its own rig on the same road, until `seconds` are up.
    private static Raced Race(TrackCar quick, TrackCar slowCar, AutopilotTests.Track track, Route route, double slowMs, double gapM, double seconds,
                              double dt = 1.0 / 60.0, double fromM = 30.0, double slowSideM = 0.0)
    {
        Runner slow = new(slowCar, track, route, fromM + gapM, dt), fast = new(quick, track, route, fromM, dt);
        slow.Pilot.CapMs = slowMs;
        slow.Pilot.SideM = slowSideM;
        double passedAt = double.NaN, beganAt = double.NaN, backAt = double.NaN, closing = 0.0, nearest = double.PositiveInfinity, beside = double.PositiveInfinity;
        double cross = 0.0, before = 0.0, least = double.PositiveInfinity;
        for (double t = 0.0; t < seconds && fast.Pilot.End == LapEnd.Running && slow.Pilot.End == LapEnd.Running; t += dt)
        {
            Rival.Seen[] seen = fast.Look([slow], dt);
            if (seen.Length == 1)
            {
                Rival.Seen s = seen[0];
                nearest = Math.Min(nearest, Vec.Len(fast.Rig.Position - slow.Rig.Position));
                if (Math.Abs(s.AheadM) < fast.HalfLength + slow.HalfLength) beside = Math.Min(beside, Math.Abs(s.SideM));
                if (double.IsNaN(beganAt) && fast.Passing.Side != 0) (beganAt, closing, before) = (t, Vec.Len(fast.Rig.Velocity) - Vec.Len(slow.Rig.Velocity), Vec.Len(fast.Rig.Velocity));
                if (!double.IsNaN(beganAt) && double.IsNaN(passedAt)) least = Math.Min(least, Vec.Len(fast.Rig.Velocity));
                if (double.IsNaN(passedAt) && s.AheadM < -Rival.PassClearM) passedAt = t;
            }
            if (!double.IsNaN(passedAt) && double.IsNaN(backAt) && Math.Abs(fast.Pilot.CrossM) < 0.3) backAt = t;
            cross = Math.Max(cross, Math.Abs(fast.Pilot.CrossM));
            // A car held at a standstill is standing and not stuck, as Opponents tells one that waits.
            if (slowMs <= 0.0) slow.Pilot.Waiting();
            slow.Step();
            fast.Step();
        }
        string told = $"{quick.Name} round {slowCar.Name} at {slowMs:F0} m/s: began {beganAt:F1} s closing at {closing:F1} m/s, past {passedAt:F1} s, on its line {backAt:F1} s; "
                    + $"nearest {nearest:F2} m, least apart while alongside {beside:F2} m, {cross:F2} m from its line at most, lost {before - least:F1} m/s; "
                    + $"fast {fast.Pilot.End} off {fast.Pilot.Summary.OffAsphaltSeconds:F2} s, slow {slow.Pilot.End} off {slow.Pilot.Summary.OffAsphaltSeconds:F2} s";
        return new Raced(passedAt, beganAt, backAt, closing, nearest, beside, cross, before - least, fast, slow, told);
    }

    public static TheoryData<string, string, double, double> Passes()
    {
        TheoryData<string, string, double, double> runs = [];
        foreach (TrackCar quick in TrackCar.All)
        {
            foreach (TrackCar slow in TrackCar.All)
            {
                foreach (double slowMs in new[] { 8.0, 15.0 }) runs.Add(quick.Name, slow.Name, slowMs, 1.0 / 60.0);
            }
        }
        runs.Add("F2004", "F2004", 30.0, 1.0 / 60.0);
        runs.Add("F2004", "Eldorado", 20.0, 0.02);
        runs.Add("F2004", "Eldorado", 20.0, 1.0 / 30.0);
        return runs;
    }

    [Theory]
    [MemberData(nameof(Passes))]
    public void AQuickCarGoesRoundASlowOneOnAStraightWithoutTouchingItOrLeavingTheRoad(string quick, string slow, double slowMs, double dt)
    {
        AutopilotTests.Track track = Long();
        Raced r = Race(TrackCar.Of(quick), TrackCar.Of(slow), track, AutopilotTests.RouteOn(track, car: TrackCar.Of(quick)), slowMs, 60.0, 45.0, dt);
        output.WriteLine(r.Told);
        Assert.True(!double.IsNaN(r.PassedAt) && !double.IsNaN(r.BackAt), r.Told);
        Assert.True(r.LeastBesideM > r.Fast.HalfWidth + r.Slow.HalfWidth, r.Told);
        Assert.True(r.Fast.Pilot.Summary.OffAsphaltSeconds == 0.0 && r.Slow.Pilot.Summary.OffAsphaltSeconds == 0.0, r.Told);
        Assert.True(r.Fast.Pilot.End == LapEnd.Running && r.Slow.Pilot.End == LapEnd.Running, r.Told);
        Assert.True(r.MostCrossM < (0.5 * AutopilotTests.Width) - Autopilot.RaceInside(r.Fast.Car.Profile) + 0.3, r.Told);
    }

    [Theory]
    [InlineData("F2004", "Eldorado", 15.0, 500.0)]
    [InlineData("F2004", "Manx", 5.0, 400.0)]
    [InlineData("F2004", "F2004", 40.0, 400.0)]
    [InlineData("Eldorado", "Manx", 10.0, 400.0)]
    public void ComingUpOnASlowCarAtSpeedItGoesRoundWithoutBrakingForIt(string quick, string slow, double slowMs, double gapM)
    {
        AutopilotTests.Track track = Long();
        Raced r = Race(TrackCar.Of(quick), TrackCar.Of(slow), track, AutopilotTests.RouteOn(track, car: TrackCar.Of(quick)), slowMs, gapM, 40.0);
        output.WriteLine(r.Told);
        Assert.True(!double.IsNaN(r.PassedAt) && !double.IsNaN(r.BackAt), r.Told);
        Assert.True(r.ClosingMs > 15.0, r.Told);
        Assert.True(r.LeastBesideM > r.Fast.HalfWidth + r.Slow.HalfWidth, r.Told);
        Assert.True(r.LostMs < 1.0, r.Told);
        Assert.True(r.Fast.Pilot.Summary.OffAsphaltSeconds == 0.0 && r.Slow.Pilot.Summary.OffAsphaltSeconds == 0.0, r.Told);
    }

    [Fact]
    public void WithNoPassingTheQuickCarStaysBehind()
    {
        // the same two cars with the road said to bend at once, the one ahead not far under what the road allows: the follower's cap alone
        AutopilotTests.Track track = Long();
        TrackCar car = TrackCar.F2004;
        Route route = AutopilotTests.RouteOn(track, car: car);
        Runner slow = new(car, track, route, 90.0, 1.0 / 60.0), fast = new(car, track, route, 30.0, 1.0 / 60.0);
        slow.Pilot.CapMs = 70.0;
        double least = double.PositiveInfinity;
        for (double t = 0.0; t < 30.0; t += 1.0 / 60.0)
        {
            Rival.Seen[] seen = [.. new[] { Rival.See(slow, fast.Pilot, fast.Rig.Position, fast.Facing, slow.Rig.Position, slow.Facing, slow.Rig.Velocity, 250.0) }.OfType<Rival.Seen>()];
            (fast.Pilot.SideM, fast.Pilot.CapMs) = Rival.Pass(fast.Passing, seen, Vec.Len(fast.Rig.Velocity), fast.Pilot.FreeMs, fast.Pilot.CrossM, Room, Room, 0.0, 1.0 / 60.0);
            if (seen.Length == 1) least = Math.Min(least, seen[0].AheadM);
            slow.Step();
            fast.Step();
        }
        Assert.Equal(0, fast.Passing.Side);
        Assert.InRange(least, Rival.GapM, 60.0);
        Assert.True(Vec.Len(fast.Rig.Velocity) < Vec.Len(slow.Rig.Velocity) + 3.0, "faster than the car it follows");
    }

    [Fact]
    public void ACarOffTheMiddleIsPassedOnItsRoomierSideAndOneOnARingWhereTheRingIsStraightEnough()
    {
        AutopilotTests.Track track = Long();
        Raced left = Race(TrackCar.F2004, TrackCar.Eldorado, track, AutopilotTests.RouteOn(track, car: TrackCar.F2004), 15.0, 60.0, 40.0, slowSideM: -1.5);
        output.WriteLine(left.Told);
        Assert.True(!double.IsNaN(left.PassedAt) && left.LeastBesideM > left.Fast.HalfWidth + left.Slow.HalfWidth, left.Told);
        Assert.True(left.Fast.Pilot.Summary.OffAsphaltSeconds == 0.0, left.Told);

        // round a ring of 300 m a car at 10 m/s is passed by one that may go 20: the pull is under what a pass is let have
        AutopilotTests.Track ring = AutopilotTests.Ring(300.0);
        Route round = AutopilotTests.RouteOn(ring, car: TrackCar.Eldorado);
        Runner slow = new(TrackCar.Manx, ring, round, 90.0, 1.0 / 60.0), fast = new(TrackCar.Eldorado, ring, round, 30.0, 1.0 / 60.0, cruise: 20.0);
        slow.Pilot.CapMs = 10.0;
        bool passed = false;
        double beside = double.PositiveInfinity;
        for (double t = 0.0; t < 60.0 && !passed; t += 1.0 / 60.0)
        {
            Rival.Seen[] seen = fast.Look([slow], 1.0 / 60.0);
            if (seen.Length == 1 && Math.Abs(seen[0].AheadM) < fast.HalfLength + slow.HalfLength) beside = Math.Min(beside, Math.Abs(seen[0].SideM));
            passed = seen.Length == 1 && seen[0].AheadM < -Rival.PassClearM;
            slow.Step();
            fast.Step();
        }
        Assert.True(passed && beside > fast.HalfWidth + slow.HalfWidth, $"passed {passed}, {beside:F2} m apart alongside");
        Assert.True(fast.Pilot.Summary.OffAsphaltSeconds == 0.0 && slow.Pilot.Summary.OffAsphaltSeconds == 0.0);
    }

    [Theory]
    [InlineData("F2004", 80.0)]
    [InlineData("Eldorado", 80.0)]
    [InlineData("Manx", 80.0)]
    [InlineData("F2004", 14.0)]
    [InlineData("Eldorado", 14.0)]
    public void ACarStandingOnTheRoadIsGoneRoundFromSpeedAndFromAStandstillBehindIt(string quick, double gapM)
    {
        // on the straight, come upon at speed and found from a standing start right behind it
        AutopilotTests.Track track = Long();
        TrackCar car = TrackCar.Of(quick);
        Raced round = Race(car, TrackCar.F2004, track, AutopilotTests.RouteOn(track, car: car), 0.0, gapM, 40.0);
        output.WriteLine(round.Told);
        Assert.True(!double.IsNaN(round.PassedAt), round.Told);
        Assert.True(round.LeastBesideM > round.Fast.HalfWidth + round.Slow.HalfWidth, round.Told);
        Assert.True(round.Fast.Pilot.Summary.OffAsphaltSeconds == 0.0, round.Told);
    }

    [Theory]
    [InlineData("F2004")]
    [InlineData("Eldorado")]
    public void ACarStandingInABendIsGoneRoundToo(string quick)
    {
        AutopilotTests.Track ring = AutopilotTests.Ring(60.0);
        TrackCar car = TrackCar.Of(quick);
        Raced round = Race(car, TrackCar.F2004, ring, AutopilotTests.RouteOn(ring, car: car), 0.0, 70.0, 40.0);
        output.WriteLine(round.Told);
        Assert.True(!double.IsNaN(round.PassedAt), round.Told);
        Assert.True(round.LeastBesideM > round.Fast.HalfWidth + round.Slow.HalfWidth, round.Told);
        Assert.True(round.Fast.Pilot.Summary.OffAsphaltSeconds == 0.0, round.Told);
    }

    [Theory]
    [InlineData(600.0)]
    [InlineData(1200.0)]
    [InlineData(2300.0)]
    [InlineData(3000.0)]
    public void ACarStandingOnTheRacingLineOfACourseIsGoneRound(double atM)
    {
        // on a racing line, which runs out to one edge or the other all the way round: the room to go round is asked of the road by the car, not of the next 150 m
        DirectoryInfo? repo = new(AppContext.BaseDirectory);
        while (repo is not null && !Directory.Exists(Path.Combine(repo.FullName, "src", "KSACars", "Circuits"))) repo = repo.Parent;
        Circuit circuit = Circuit.FromJson(File.ReadAllText(Path.Combine(repo!.FullName, "src", "KSACars", "Circuits", "Club Motorsports Flat.json")), out string bad, out _)
                          ?? throw new InvalidOperationException(bad);
        TrackWorld world = TrackWorld.Earth();
        AutopilotTests.Track track = new(world, circuit, world.Surface(circuit));
        TrackCar car = TrackCar.F2004;
        Route line = Route.Of(circuit, TrackWorld.DirOf, world.RadiusM, world.HeightAt, TrackWorld.LiftM, TrackWorld.SpacingM, null, 0.0,
                              out string why, Autopilot.TurnRadius(car.Profile), Autopilot.RaceInside(car.Profile)) ?? throw new InvalidOperationException(why);

        Raced round = Race(car, car, track, line, 0.0, 120.0, 60.0, fromM: atM - 120.0);
        output.WriteLine(round.Told);
        Assert.True(!double.IsNaN(round.PassedAt), round.Told);
        Assert.True(round.LeastBesideM > round.Fast.HalfWidth + round.Slow.HalfWidth, round.Told);
    }

    [Fact]
    public void OffTheGridNoPassIsBegunUntilTheFieldHasSetOff()
    {
        Rival.Passing state = new() { Launch = 8.0 };
        Rival.Seen[] ahead = [Car("ahead", 20.0, 0.0, 0.5)];
        Assert.Equal(0.0, Look(state, ahead, speed: 5.0).SideM);
        Look(state, ahead, speed: 5.0, dt: 8.1);
        Assert.NotEqual(0, state.Side);
    }

    [Fact]
    public void ACarWaitingBehindAnotherIsFollowedAndNotGoneRound()
    {
        Rival.Passing state = new();
        Assert.Equal(0.0, Look(state, [new Rival.Seen("waiting", 20.0, 0.0, 0.5, Queued: true)], speed: 5.0).SideM);
        Assert.Equal(0, state.Side);
        Look(state, [new Rival.Seen("standing", 20.0, 0.0, 0.5)], speed: 5.0);
        Assert.NotEqual(0, state.Side);
    }

    [Fact]
    public void APassOfAStandingCarThatCannotBeFinishedIsOverAndNotWaitedBehind()
    {
        Rival.Passing state = new();
        Rival.Seen[] standing = [Car("standing", 20.0, 0.0, 0.5)];
        Look(state, standing, speed: 5.0);
        Assert.NotEqual(0, state.Side);
        // the room goes, for more than a moment: behind a standing car there is no dropping back to, so the pass is over and another may be begun soon
        Look(state, standing, speed: 5.0, left: 0.5, right: 0.5);
        (_, double cap) = Look(state, standing, speed: 5.0, left: 0.5, right: 0.5, dt: Rival.UnfitSeconds);
        Assert.Equal(0, state.Side);
        Assert.InRange(state.Rest, 0.0, Rival.StandingRestSeconds);
        Assert.True(cap > 0.0, "held to a standstill behind it");
    }

    [Fact]
    public void ADriverThatKnowsItsBrakesFollowsCloser()
    {
        double known = Rival.FollowCap(60.0, 0.0, 20.0, 40.0, _ => 30.0), unknown = Rival.FollowCap(60.0, 0.0, 20.0, 40.0);
        Assert.True(known > unknown, $"{known:F1} m/s knowing its brakes and {unknown:F1} not");
        // never taken to brake harder than the share of what it does, nor softer than a car that does not know
        Assert.Equal(unknown, Rival.FollowCap(60.0, 0.0, 20.0, 40.0, _ => 1.0), 9);
    }

    [Fact]
    public void ADriverKeepsACarsWidthFromACarAlongsideWhereverItsLineGoes()
    {
        // a car 3 m to its left and a metre ahead: its line may come no nearer than a lane to it
        Assert.Equal(3.0 - Rival.LaneM, Rival.KeepLane([Car("beside", 1.0, 3.0, 40.0)], 0.0, 1.5), 9);
        // nearer than a lane already, it goes no nearer than it is
        Assert.Equal(0.0, Rival.KeepLane([Car("beside", 1.0, 2.0, 40.0)], 0.0, 1.5), 9);
        Assert.Equal(-1.0, Rival.KeepLane([Car("beside", -2.0, 2.0, 40.0)], -1.0, 1.0), 9);
        // on the right the same, and away from it the line goes where it likes
        Assert.Equal(-3.0 + Rival.LaneM, Rival.KeepLane([Car("beside", 0.0, -3.0, 40.0)], 0.0, -2.0), 9);
        Assert.Equal(1.5, Rival.KeepLane([Car("beside", 0.0, -3.0, 40.0)], 0.0, 1.5), 9);
        // a car ahead or behind, or in the driver's own lane, is not alongside
        Assert.Equal(1.5, Rival.KeepLane([Car("ahead", 20.0, 2.0, 40.0)], 0.0, 1.5), 9);
        Assert.Equal(1.5, Rival.KeepLane([Car("behind", 3.0, 0.2, 40.0)], 0.0, 1.5), 9);
    }

    [Fact]
    public void StillBehindACarThatBrakesHardItIsHeldBackByIt()
    {
        // on its way out to the left from behind a car 30 m ahead, and reckoned to be clear of it before it is up to it
        Rival.Passing state = new();
        Look(state, [Car("slow", 30.0, 0.0, 40.0)], speed: 45.0, free: 70.0, left: Room, right: 0.5);
        Assert.Equal(1, state.Side);
        (_, double coasting) = Look(state, [Car("slow", 29.9, -0.8, 40.0)], speed: 45.0, free: 70.0, cross: 0.8, left: Room, right: 0.5);
        Assert.True(double.IsPositiveInfinity(coasting), $"held to {coasting:F1} m/s behind a car keeping its speed");
        // the car brakes at 15 m/s2: still behind it, the driver is held back by it
        (_, double braking) = Look(state, [Car("slow", 29.0, -0.9, 39.75)], speed: 45.0, free: 70.0, cross: 0.9, left: Room, right: 0.5);
        Assert.Equal(1, state.Side);
        Assert.True(braking < 45.0, $"held to {braking:F1} m/s behind a car braking hard");
    }

    [Fact]
    public void TwoCarsStandingSideBySideAreGoneRoundAsOne()
    {
        // one on the driver's line 15 m ahead, another standing beside it 2.5 m to its left, the road's room on the left
        Rival.Seen[] standing = [Car("near", 15.0, 0.0, 0.0), Car("beside", 16.0, 2.5, 0.0)];
        Rival.Passing state = new();
        (double side, double cap) = Look(state, standing, speed: 2.0, left: 9.0, right: 1.0);
        Assert.Equal(1, state.Side);
        // out past both, and moving: neither holds it at a standstill
        Assert.True(side >= 2.5 + Rival.StandingPassM - 1e-9, $"{side:F1} m out, which is not past the second car");
        Assert.True(cap >= 1.0, $"held to {cap:F1} m/s");

        // with no room past the second, it is not begun on that side
        Rival.Passing narrow = new();
        Look(narrow, standing, speed: 2.0, left: 4.0, right: 1.0);
        Assert.NotEqual(1, narrow.Side);
    }

    [Fact]
    public void ACarDrivenSlowlyIsGoneRoundInABendTooAndNotFlatOut()
    {
        // 15 m/s where the road allows 50, and no straight ahead at all
        Rival.Passing state = new();
        (_, double cap) = Look(state, [Car("slow", 25.0, 0.0, 15.0)], speed: 16.0, free: 50.0, straight: 0.0);
        Assert.NotEqual(0, state.Side);
        Assert.InRange(cap, 0.0, 15.0 + Rival.CrawlPassMs);

        // one only a little slower is still followed until there is a straight
        Rival.Passing near = new();
        Look(near, [Car("slow", 25.0, 0.0, 40.0)], speed: 41.0, free: 50.0, straight: 0.0);
        Assert.Equal(0, near.Side);
    }
}
