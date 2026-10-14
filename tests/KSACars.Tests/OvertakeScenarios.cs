using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSACars.Tests;

// Opponents against scripted cars in every awkward way a player has of being in the way, in the rig: run only with OVERTAKE_RUN=1.
// The scripted cars are moved, not driven, and nothing touches anything: a contact is two footprints overlapping.
public class OvertakeScenarios(ITestOutputHelper output)
{
    private const double FootHalfLength = 2.3, FootHalfWidth = 0.9, StartBackM = 400.0, FinishOnM = 700.0, Timeout = 120.0;

    // A scripted car: where it is along the route and across it, which way it faces against the route, and its speed along and across.
    private sealed class Scripted
    {
        public double S, Lat, Yaw, Along, Across, Road, Aim;
        public bool YawSet;
        public double3 Place, Facing, Velocity;
    }

    private delegate void Behave(double t, double dt, Scripted p, Route route);

    private sealed record Scenario(string Name, string Course, double PlaceM, double MeetMs, Behave[] Players, double[] StartLats, int Opponents = 1, string? Where = null);

    private sealed record Course(string Name, AutopilotTests.Track Track, Route Route, Dictionary<string, double> Places);

    private static readonly Lazy<Course> Club = new(() =>
    {
        DirectoryInfo? repo = new(AppContext.BaseDirectory);
        while (repo is not null && !Directory.Exists(Path.Combine(repo.FullName, "src", "KSACars", "Circuits"))) repo = repo.Parent;
        Circuit circuit = Circuit.FromJson(File.ReadAllText(Path.Combine(repo!.FullName, "src", "KSACars", "Circuits", "Club Motorsports Flat.json")), out string bad, out _)
                          ?? throw new InvalidOperationException(bad);
        TrackWorld world = TrackWorld.Earth();
        AutopilotTests.Track track = new(world, circuit, world.Surface(circuit));
        Route route = RacingRoute(track);
        return new Course("club", track, route, Places(route));
    });

    private static readonly Lazy<Course> LongCourse = new(() =>
    {
        TrackWorld world = TrackWorld.Earth();
        Circuit c = AutopilotTests.Through(world, false, (0.0, 0.0, 0.0), (1000.0, 0.0, 0.0), (2000.0, 0.0, 0.0), (3000.0, 0.0, 0.0));
        AutopilotTests.Track track = new(world, c, world.Surface(c));
        return new Course("long", track, RacingRoute(track), new() { ["straight"] = 1500.0 });
    });

    private static Route RacingRoute(AutopilotTests.Track track)
    {
        TrackCar car = TrackCar.F2004;
        Rival.Way way = Way(1);
        return Route.Of(track.Circuit, TrackWorld.DirOf, track.World.RadiusM, track.World.HeightAt, TrackWorld.LiftM, TrackWorld.SpacingM, null, 0.0,
                        out string why, Autopilot.TurnRadius(car.Profile), way.InsideM, way.LineM) ?? throw new InvalidOperationException(why);
    }

    private static Rival.Way Way(int seed) => Rival.Drive(null, seed, 0.0, Autopilot.RaceInside(TrackCar.F2004.Profile), "F2004");

    // The middle of the longest straight, the tightest bend's apex, and 60 m either side of it.
    private static Dictionary<string, double> Places(Route route)
    {
        ReadOnlySpan<Route.Sample> s = route.Samples;
        int apex = 0;
        for (int i = 0; i < s.Length; i++) if (s[i].S > StartBackM + 100.0 && Math.Abs(s[i].Curvature) > Math.Abs(s[apex].Curvature)) apex = i;
        double bestLen = 0.0, bestMid = 0.0, from = -1.0;
        for (int i = 0; i < s.Length; i++)
        {
            bool straight = Math.Abs(s[i].Curvature) < 1.0 / 600.0;
            if (straight && from < 0.0) from = s[i].S;
            if ((!straight || i == s.Length - 1) && from >= 0.0)
            {
                if (s[i].S - from > bestLen) (bestLen, bestMid) = (s[i].S - from, 0.5 * (from + s[i].S));
                from = -1.0;
            }
        }
        double a = s[apex].S;
        return new() { ["straight"] = bestMid, ["entry"] = a - 60.0, ["apex"] = a, ["exit"] = a + 60.0 };
    }

    private static double Wrap(Route r, double s) => r.Closed ? ((s % r.LengthM) + r.LengthM) % r.LengthM : Math.Clamp(s, 0.0, r.LengthM - 1.0);

    private static Route.Sample At(Route r, double s) => r.Samples[r.IndexAt(Wrap(r, s))];

    private static void Place(Scripted p, Route r, double dt)
    {
        double s = Wrap(r, p.S);
        int i = r.IndexAt(s);
        Route.Sample a = r.Samples[i];
        double3 on = r.Ahead(i, s, 0.0);
        double3 up = Vec.Unit(on);
        p.Place = on + (a.Across * p.Lat) + (up * 0.3);
        double yaw = p.YawSet ? p.Yaw : Math.Abs(p.Along) > 0.3 ? Math.Atan2(p.Across, p.Along) : p.Yaw;
        p.Facing = (a.Tangent * Math.Cos(yaw)) + (a.Across * Math.Sin(yaw));
        p.Velocity = (a.Tangent * p.Along) + (a.Across * p.Across);
    }

    // ---- behaviours ----

    private static readonly ConcurrentDictionary<Route, double[]> Plans = new();

    // A slow car is slower than what the road allows a racing driver there: never faster than 0.8 of it.
    private static double Slow(Route r, double s, double ms)
    {
        double[] plan = Plans.GetOrAdd(r, route =>
        {
            TrackCar car = TrackCar.F2004;
            AutopilotTests.Track track = ReferenceEquals(route, Club.Value.Route) ? Club.Value.Track : LongCourse.Value.Track;
            return new Autopilot(car.Profile, route, track.Road, car.MassKg, track.World.Gravity, track.World.Air, 1, 0.0, 1e6) { Tune = Way(1).Tune }.Plan().ToArray();
        });
        return Math.Min(ms, 0.8 * plan[r.IndexAt(Wrap(r, s))]);
    }

    // A driver keeping to a lane drifts across to it as the road's width and middle move, no faster than this, m/s.
    private const double LaneDriftMs = 1.5;

    private static Behave Constant(double ms, Func<Route.Sample, double> lane) => (t, dt, p, r) =>
    {
        // Drifted across the road, not across the route, which a racing line runs across under it.
        Route.Sample here = At(r, p.S);
        double road = lane(here) - here.CentreM;
        p.Road = dt > 0.0 && lane != Line ? p.Road + Math.Clamp(road - p.Road, -LaneDriftMs * dt, LaneDriftMs * dt) : road;
        double lat = p.Road + here.CentreM;
        p.Across = dt > 0.0 ? (lat - p.Lat) / dt : 0.0;
        (p.Along, p.Lat) = (Slow(r, p.S, ms), lat);
    };

    private static Behave Weave(double ms, double amp, double period) => (t, dt, p, r) =>
    {
        double lat = At(r, p.S).CentreM + (amp * Math.Sin(2.0 * Math.PI * t / period));
        p.Across = dt > 0.0 ? (lat - p.Lat) / dt : 0.0;
        (p.Along, p.Lat) = (ms, lat);
    };

    private static Behave Sideways(double yawDeg, Func<Route.Sample, double> lane) => (t, dt, p, r) =>
    {
        (p.Along, p.Across, p.Lat, p.Yaw, p.YawSet) = (0.0, 0.0, lane(At(r, p.S)), yawDeg * Math.PI / 180.0, true);
    };

    // Turning across the road at a crawl, back and forth: a car being turned round after a spin.
    private static Behave Shuffle(double amp, double period) => (t, dt, p, r) =>
    {
        double lat = At(r, p.S).CentreM + (amp * Math.Sin(2.0 * Math.PI * t / period));
        p.Across = dt > 0.0 ? (lat - p.Lat) / dt : 0.0;
        (p.Along, p.Lat, p.Yaw, p.YawSet) = (0.0, lat, Math.PI / 2.0, true);
    };

    private static Behave Reverse(double ms, Func<Route.Sample, double> lane) => (t, dt, p, r) =>
    {
        double lat = lane(At(r, p.S));
        (p.Along, p.Across, p.Lat, p.Yaw, p.YawSet) = (-ms, 0.0, lat, 0.0, true);
    };

    // Where across the route the nearest opponent behind a blocker is, or NaN with none within reach: what a blocker moves across towards.
    [ThreadStatic] private static double _chaserLat;

    // A player keeping an opponent behind it: it moves across towards the opponent's line, a little late and no faster than a car is moved across.
    private static Behave Block(double ms, double rateMs, double lagSeconds) => (t, dt, p, r) =>
    {
        Route.Sample here = At(r, p.S);
        double edge = here.HalfWidth - 1.2;
        p.Aim = double.IsNaN(_chaserLat) ? p.Aim : dt > 0.0 ? p.Aim + ((_chaserLat - p.Aim) * Math.Min(dt / lagSeconds, 1.0)) : _chaserLat;
        double want = Math.Clamp(p.Aim, here.CentreM - edge, here.CentreM + edge);
        double lat = dt > 0.0 ? p.Lat + Math.Clamp(want - p.Lat, -rateMs * dt, rateMs * dt) : p.Lat;
        p.Across = dt > 0.0 ? (lat - p.Lat) / dt : 0.0;
        (p.Along, p.Lat) = (Slow(r, p.S, ms), lat);
    };

    // Driven the wrong way round, facing back down the road.
    private static Behave WrongWay(double ms, Func<Route.Sample, double> lane) => (t, dt, p, r) =>
    {
        double lat = lane(At(r, p.S));
        (p.Along, p.Across, p.Lat, p.Yaw, p.YawSet) = (-ms, 0.0, lat, Math.PI, true);
    };

    private static Behave BrakeCheck(double hi, double lo, double period) => (t, dt, p, r) =>
    {
        double want = Slow(r, p.S, (t % period) < 0.5 * period ? hi : lo);
        double rate = want > p.Along ? 6.0 : 12.0;
        p.Along += Math.Clamp(want - p.Along, -rate * dt, rate * dt);
        (p.Across, p.Lat) = (0.0, 0.0);
    };

    private static double Line(Route.Sample s) => 0.0;
    private static double Middle(Route.Sample s) => s.CentreM;
    private static double LeftEdge(Route.Sample s) => s.CentreM + s.HalfWidth - 1.2;
    private static double RightEdge(Route.Sample s) => s.CentreM - s.HalfWidth + 1.2;

    private static IEnumerable<Scenario> Scenarios()
    {
        foreach (Course course in new[] { Club.Value, LongCourse.Value })
        {
            foreach ((string where, double at) in course.Places)
            {
                string c = course.Name;
                foreach (double v in new[] { 5.0, 15.0, 30.0 })
                {
                    yield return new($"slow{v:F0}-line", c, at, v, [Constant(v, Line)], [0.0]);
                    yield return new($"slow{v:F0}-middle", c, at, v, [Constant(v, Middle)], [0.0]);
                    yield return new($"slow{v:F0}-left", c, at, v, [Constant(v, LeftEdge)], [0.0]);
                    yield return new($"slow{v:F0}-right", c, at, v, [Constant(v, RightEdge)], [0.0]);
                }
                foreach ((double amp, double period) in new[] { (2.0, 4.0), (4.0, 6.0), (5.0, 3.0) })
                {
                    yield return new($"weave{amp:F0}/{period:F0}s", c, at, 8.0, [Weave(8.0, amp, period)], [0.0]);
                }
                yield return new("sideways-line", c, at, 0.0, [Sideways(90.0, Line)], [0.0]);
                yield return new("sideways-middle", c, at, 0.0, [Sideways(90.0, Middle)], [0.0]);
                yield return new("sideways-left", c, at, 0.0, [Sideways(-80.0, LeftEdge)], [0.0]);
                yield return new("shuffle3/8s", c, at, 0.0, [Shuffle(3.0, 8.0)], [0.0]);
                yield return new("reverse2-line", c, at, 0.0, [Reverse(2.0, Line)], [0.0]);
                yield return new("wrongway3-line", c, at, 0.0, [WrongWay(3.0, Line)], [0.0]);
                yield return new("wrongway6-middle", c, at, 0.0, [WrongWay(6.0, Middle)], [0.0]);
                yield return new("reverse5-middle", c, at, 0.0, [Reverse(5.0, Middle)], [0.0]);
                yield return new("brakecheck30/8", c, at, 19.0, [BrakeCheck(30.0, 8.0, 8.0)], [0.0]);
                yield return new("brakecheck20/3", c, at, 11.0, [BrakeCheck(20.0, 3.0, 6.0)], [0.0]);
                foreach ((double v, double rate, double lag) in new[] { (15.0, 3.0, 0.3), (30.0, 3.0, 0.3), (30.0, 5.0, 0.3), (20.0, 2.5, 0.6), (30.0, 2.5, 0.6) })
                {
                    yield return new(FormattableString.Invariant($"block{v:F0}/{rate:F1}/{lag:F1}"), c, at, v, [Block(v, rate, lag)], [0.0]);
                }
                yield return new("abreast12", c, at, 12.0, [Constant(12.0, s => s.CentreM + 2.2), Constant(12.0, s => s.CentreM - 2.2)], [0.0, 0.0]);
                yield return new("queue3-slow10", c, at, 10.0, [Constant(10.0, Line)], [0.0], 3);
            }
        }
    }

    // Variants nothing was tuned on, at other places, speeds and amplitudes: OVERTAKE_HELDOUT=1 runs these instead.
    private static IEnumerable<Scenario> HeldOut()
    {
        Course club = Club.Value, line = LongCourse.Value;
        (Course Course, string Where, double At)[] places =
        [
            (club, "before-entry", club.Places["entry"] - 150.0), (club, "past-exit", club.Places["exit"] + 140.0),
            (club, "straight-late", club.Places["straight"] + 120.0), (line, "straight-early", 900.0),
        ];
        foreach ((Course course, string where, double at) in places)
        {
            string c = course.Name;
            yield return new("slow10-line", c, at, 10.0, [Constant(10.0, Line)], [0.0], Where: where);
            yield return new("slow22-middle", c, at, 22.0, [Constant(22.0, Middle)], [0.0], Where: where);
            yield return new("slow8-left", c, at, 8.0, [Constant(8.0, LeftEdge)], [0.0], Where: where);
            yield return new("weave3/5s", c, at, 10.0, [Weave(10.0, 3.0, 5.0)], [0.0], Where: where);
            yield return new("weave1/2s", c, at, 6.0, [Weave(6.0, 1.0, 2.0)], [0.0], Where: where);
            yield return new("sideways45-right", c, at, 0.0, [Sideways(45.0, RightEdge)], [0.0], Where: where);
            yield return new("sideways90-left", c, at, 0.0, [Sideways(90.0, LeftEdge)], [0.0], Where: where);
            yield return new("shuffle2/5s", c, at, 0.0, [Shuffle(2.0, 5.0)], [0.0], Where: where);
            yield return new("reverse3-left", c, at, 0.0, [Reverse(3.0, LeftEdge)], [0.0], Where: where);
            yield return new("wrongway4-right", c, at, 0.0, [WrongWay(4.0, RightEdge)], [0.0], Where: where);
            yield return new("wrongway10-line", c, at, 0.0, [WrongWay(10.0, Line)], [0.0], Where: where);
            yield return new("brakecheck25/5", c, at, 15.0, [BrakeCheck(25.0, 5.0, 5.0)], [0.0], Where: where);
            yield return new("block25/4.0/0.4", c, at, 25.0, [Block(25.0, 4.0, 0.4)], [0.0], Where: where);
            yield return new("block12/2.0/0.2", c, at, 12.0, [Block(12.0, 2.0, 0.2)], [0.0], Where: where);
            yield return new("abreast18", c, at, 18.0, [Constant(18.0, s => s.CentreM + 2.4), Constant(18.0, s => s.CentreM - 2.4)], [0.0, 0.0], Where: where);
            yield return new("queue3-slow15", c, at, 15.0, [Constant(15.0, Middle)], [0.0], 3, Where: where);
        }
    }

    // ---- the race ----

    private sealed class Opp
    {
        public required TrackRig Rig;
        public required Autopilot Pilot;
        public Rival.Passing Passing = new();
        public double Gone0 = double.NaN, Still, StuckAt = double.NaN, FinishedAt = double.NaN, PassedAt = double.NaN, Follow, Contacts, ContactSeconds;
        public double MinGap = double.PositiveInfinity, MinSpeedNear = double.PositiveInfinity;
        public int ContactEvents, PlayerContacts, OppContacts;
        public double Kept, HeldBack;
        public bool Touching, TouchingPlayer, TouchingOpp;
        public double Cap = double.PositiveInfinity;
        public double3 Facing => Rig.Attitude * new double3(0, 1, 0);
        public double Gone => Pilot.Summary.ProgressM - Gone0;
    }

    private static Opp Stood(Course course, double s, double lat, double dt, int seed)
    {
        TrackCar car = TrackCar.F2004;
        TrackRig rig = new(car, course.Track.World, course.Track.Road) { KeepsLog = false };
        (double3 at, _, double3 ahead) = course.Route.Standing(Wrap(course.Route, s));
        at += At(course.Route, s).Across * lat;
        (double east, double north) = course.Track.World.Flatten(at);
        rig.Place(east, north, Math.Atan2(ahead.Z, ahead.Y) * 180.0 / Math.PI, Vec.Len(at) - course.Track.World.RadiusM - course.Track.World.HeightAt(Vec.Unit(at)));
        rig.Settle(1.0);
        rig.Dt = dt;
        Rival.Way way = Way(seed);
        Autopilot pilot = new(car.Profile, course.Route, course.Track.Road, car.MassKg, course.Track.World.Gravity, course.Track.World.Air, 100, 0.0, 1e6)
        {
            Tune = way.Tune, Push = way.Push, OffRoadFor = 15.0,
        };
        rig.Driver = pilot;
        return new Opp { Rig = rig, Pilot = pilot };
    }

    private static readonly ConcurrentDictionary<string, double[]> FreeTimes = new();
    private static readonly ConcurrentDictionary<string, double> FreeOff = new();

    // The time a lone opponent from a place takes to have gone each metre on from it.
    private static double[] Free(Course course, double s0, double lat, double dt, int seed)
    {
        return FreeTimes.GetOrAdd(FormattableString.Invariant($"{course.Name}|{s0:F1}|{lat:F1}|{dt:F4}|{seed}"), _ =>
        {
            Opp o = Stood(course, s0, lat, dt, seed);
            double[] times = new double[(int)(StartBackM + FinishOnM + 50)];
            int filled = 0;
            for (double t = 0.0; t < Timeout && filled < times.Length; t += dt)
            {
                o.Rig.Step(default);
                if (double.IsNaN(o.Gone0)) o.Gone0 = o.Pilot.Summary.ProgressM;
                while (filled < times.Length && o.Gone >= filled) times[filled++] = t;
                if (o.Pilot.End != LapEnd.Running) break;
            }
            for (; filled < times.Length; filled++) times[filled] = double.PositiveInfinity;
            FreeOff[FormattableString.Invariant($"{course.Name}|{s0:F1}|{lat:F1}|{dt:F4}|{seed}")] = o.Pilot.Summary.OffAsphaltSeconds;
            return times;
        });
    }

    // Where across the route an opponent is, if it is behind a scripted car and within 80 m of it.
    private static double? See(Opp o, Scripted p, double3 place)
    {
        double3 up = Vec.Unit(p.Place), ahead = Vec.Unit(Vec.RejectFrom(p.Facing, up));
        double3 to = Vec.RejectFrom(place - p.Place, up);
        double along = Vec.Dot(to, ahead);
        return along < -2.0 && along > -80.0 ? p.Lat + Vec.Dot(to, Vec.Cross(up, ahead)) : null;
    }

    // Seen as the game sees it: Opponents hands See the other car's place, facing and speed.
    private static Rival.Seen? See(object who, Opp me, double3 place, double3 facing, double3 velocity) =>
        Rival.See(who, me.Pilot, me.Rig.Position, me.Facing, place, facing, velocity, 250.0, Vec.Len(me.Rig.Velocity));

    public sealed record Result(string Scenario, string Course, string Where, double Dt, int Opponent, bool Passed, double PassedAt, double Lost,
                                double FollowSeconds, double MinGapM, int ContactEvents, double ContactSeconds, double OffAsphalt, bool Stuck,
                                string End, double MinSpeedNear, int PlayerContacts, int OppContacts, double KeptSeconds, double HeldSeconds);

    // How far an F2004's middle is ahead of where its route places it.
    private static readonly double MiddleM = 0.5 * BuggyDrive.Wheelbase(TrackCar.F2004.Profile);

    private static List<Result> Run(Scenario sc, string where, double dt)
    {
        Course course = sc.Course == "club" ? Club.Value : LongCourse.Value;
        Route route = course.Route;
        List<Opp> opps = [];
        List<double[]> free = [];
        for (int k = 0; k < sc.Opponents; k++)
        {
            double s0 = sc.PlaceM - StartBackM - (k * 12.0), lat = k == 0 ? 0.0 : (k % 2 == 1 ? 2.0 : -2.0);
            opps.Add(Stood(course, s0, lat, dt, k + 1));
            free.Add(Free(course, s0, lat, dt, k + 1));
        }
        // Met at the place by a lone opponent: the scripted car starts as far back as it goes in the time that takes.
        double meet = free[0][(int)StartBackM];
        Scripted[] players = [.. sc.Players.Select((_, i) => new Scripted { S = Math.Max(sc.PlaceM - (sc.MeetMs * (double.IsFinite(meet) ? meet : 20.0)), sc.PlaceM - StartBackM + 100.0), Lat = sc.StartLats[i] })];
        for (int i = 0; i < players.Length; i++)
        {
            sc.Players[i](0.0, 0.0, players[i], route);
            Place(players[i], route, dt);
        }

        List<Rival.Seen> seen = [];
        string key = FormattableString.Invariant($"{sc.Name}|{sc.Course}|{where}|{dt:F3}");
        StringBuilder? trace = Environment.GetEnvironmentVariable("OVERTAKE_TRACE") is { Length: > 0 } tr && key == tr ? new("t,opp,gone,speed,free,cap,ask,cross,side,back,who,ahead,sidem,seenms,gap,touch,left,right,straight,rest,nleft,nright,nwide,off,sidenow,lanes,held,ot,cars\n") : null;
        for (double t = 0.0; t < Timeout; t += dt)
        {
            if (opps.All(o => !double.IsNaN(o.FinishedAt) || !double.IsNaN(o.StuckAt) || o.Pilot.End != LapEnd.Running)) break;
            foreach (Opp me in opps)
            {
                if (me.Pilot.End != LapEnd.Running || !double.IsNaN(me.StuckAt)) continue;
                seen.Clear();
                for (int i = 0; i < players.Length; i++)
                {
                    if (See(players[i], me, players[i].Place, players[i].Facing, players[i].Velocity) is { } p) seen.Add(p with { Player = true });
                }
                foreach (Opp other in opps)
                {
                    if (ReferenceEquals(other, me)) continue;
                    if (See(other, me, other.Rig.Position, other.Facing, other.Rig.Velocity) is { } o) seen.Add(o with { Queued = other.Cap < 3.0 });
                }
                Rival.Seen[] cars = [.. seen];
                double speed = Vec.Len(me.Rig.Velocity), free0 = me.Pilot.FreeMs;
                double left = 0.0, right = 0.0, straight = 0.0;
                me.Pilot.SqueezeM = Rival.Squeeze(cars, free0);
                me.Pilot.RoadAhead(Rival.RoomOver(cars, speed), Rival.StraightPerM(Math.Max(free0, speed)), out left, out right, out straight, Rival.RoomFrom(cars, speed));
                Lanes.Road road = Lanes.RoadOf(me.Pilot, me.Passing.Lanes, cars, speed, MiddleM);
                (me.Pilot.SideM, me.Pilot.CapMs) = Rival.Pass(me.Passing, cars, speed, free0, me.Pilot.CrossM, left, right, straight, dt, me.Pilot.BrakeAt, road);
                me.Cap = me.Pilot.CapMs;
                if (me.Cap < 3.0) me.Pilot.Waiting();

                // As Opponents: under 0.5 m/s for 2.5 s with nothing holding it is put back.
                me.Still = speed < 0.5 && me.Cap >= 3.0 && t > 1.0 ? me.Still + dt : 0.0;
                if (me.Still >= 2.5) me.StuckAt = t;

                if (trace is not null)
                {
                    Rival.Seen n = cars.Length > 0 ? cars.MinBy(c => Math.Abs(c.AheadM)) : default;
                    trace.AppendLine(FormattableString.Invariant($"{t:F3},{opps.IndexOf(me)},{me.Gone:F1},{speed:F2},{free0:F2},{Math.Min(me.Cap, 999):F2},{me.Pilot.SideM:F2},{me.Pilot.CrossM:F2},{me.Passing.Side},{(me.Passing.Back ? 1 : 0)},{(me.Passing.Who is null ? "" : me.Passing.Who is Opp ? "opp" : "player")},{n.AheadM:F2},{n.SideM:F2},{n.SpeedMs:F2},{me.MinGap:F2},{(me.Touching ? 1 : 0)},{left:F2},{right:F2},{straight:F1},{me.Passing.Rest:F2},{n.LeftM:F2},{n.RightM:F2},{n.WideM:F2},{me.Pilot.Summary.OffAsphaltSeconds:F2},{me.Pilot.SideNowM:F2},{me.Passing.LanesFor:F1},{me.Passing.Lanes.Held:F2},{(me.Passing.Lanes.Who is null ? 0 : 1)},{string.Join(" ", cars.Select(c => FormattableString.Invariant($"[{c.AheadM:F1}/{c.SideM:F2}/{c.SpeedMs:F1}/{c.WideM:F2}/D{c.DriftPerM:F3}/r{c.RoadM:F2}]")))}"));
                }
                foreach (Scripted p in players)
                {
                    if (See(p, me, p.Place, p.Facing, p.Velocity) is not { } q) continue;
                    if (q.AheadM > 0.0 && q.AheadM < 40.0) me.Follow += dt;
                    // Kept behind it: not closing on it.
                    if (q.AheadM > 0.0 && q.AheadM < 40.0 && speed <= Math.Max(q.SpeedMs, 0.0) + 1.0 && me.Gone > 10.0) me.Kept += dt;
                    // Held by it: held under what the road allows, and slower than that, with it in front.
                    if (q.AheadM > 0.0 && q.AheadM < 40.0 && me.Cap < free0 - 2.0 && speed < free0 - 2.0 && me.Gone > 10.0) me.HeldBack += dt;
                    if (double.IsNaN(me.PassedAt) && q.AheadM < -Rival.PassClearM && t > 0.5 && me.Gone > 10.0) me.PassedAt = t;
                    if (Math.Abs(q.AheadM) < 30.0 && me.Gone > 20.0) me.MinSpeedNear = Math.Min(me.MinSpeedNear, speed);
                }
            }
            foreach (Opp o in opps) if (o.Pilot.End == LapEnd.Running && double.IsNaN(o.StuckAt)) o.Rig.Step(default);
            foreach (Opp o in opps) if (double.IsNaN(o.Gone0)) o.Gone0 = o.Pilot.Summary.ProgressM;
            for (int i = 0; i < players.Length; i++)
            {
                _chaserLat = double.NaN;
                foreach (Opp o in opps)
                {
                    if (See(o, players[i], o.Rig.Position) is { } behind) _chaserLat = behind;
                }
                sc.Players[i](t, dt, players[i], route);
                players[i].S += players[i].Along * dt;
                Place(players[i], route, dt);
            }

            // Footprints, every opponent against every scripted car and every other opponent.
            foreach (Opp me in opps)
            {
                bool player = false, opp = false;
                double3 up = Vec.Unit(me.Rig.Position);
                foreach (Scripted p in players) player |= Footprints(me, up, p.Place, p.Facing);
                foreach (Opp other in opps) if (!ReferenceEquals(other, me)) opp |= Footprints(me, up, other.Rig.Position, other.Facing);
                bool touching = player || opp;
                if (touching && !me.Touching) me.ContactEvents++;
                if (player && !me.TouchingPlayer) me.PlayerContacts++;
                if (opp && !me.TouchingOpp) me.OppContacts++;
                (me.TouchingPlayer, me.TouchingOpp) = (player, opp);
                if (touching) me.ContactSeconds += dt;
                me.Touching = touching;
                if (double.IsNaN(me.FinishedAt) && me.Gone >= StartBackM + FinishOnM) me.FinishedAt = t;
            }
        }

        if (trace is not null) File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("OVERTAKE_OUT") ?? Path.GetTempPath(), "trace.csv"), trace.ToString());
        List<Result> results = [];
        for (int k = 0; k < opps.Count; k++)
        {
            Opp o = opps[k];
            double s0k = sc.PlaceM - StartBackM - (k * 12.0), latk = k == 0 ? 0.0 : (k % 2 == 1 ? 2.0 : -2.0);
            double freeOff = FreeOff.GetValueOrDefault(FormattableString.Invariant($"{course.Name}|{s0k:F1}|{latk:F1}|{dt:F4}|{k + 1}"));
            double lost = double.IsNaN(o.FinishedAt) ? double.PositiveInfinity : o.FinishedAt - free[k][(int)(StartBackM + FinishOnM)];
            results.Add(new(sc.Name, sc.Course, where, dt, k, !double.IsNaN(o.PassedAt), o.PassedAt, lost, o.Follow, o.MinGap, o.ContactEvents, o.ContactSeconds,
                            o.Pilot.Summary.OffAsphaltSeconds - freeOff, !double.IsNaN(o.StuckAt), o.Pilot.End.ToString(), o.MinSpeedNear,
                            o.PlayerContacts, o.OppContacts, o.Kept, o.HeldBack));
        }
        return results;
    }

    // Whether the opponent's footprint overlaps another's, keeping the least gap between them.
    private static bool Footprints(Opp me, double3 up, double3 theirPlace, double3 theirFacing)
    {
        double3 e1 = Vec.Unit(Vec.RejectFrom(me.Facing, up)), e2 = Vec.Cross(up, e1);
        (double x, double y) Flat(double3 v) => (Vec.Dot(v, e1), Vec.Dot(v, e2));
        (double x, double y)[] Box(double3 at, double3 facing)
        {
            (double cx, double cy) = Flat(at - me.Rig.Position);
            (double fx, double fy) = Flat(Vec.Unit(Vec.RejectFrom(facing, up)));
            double n = Math.Sqrt((fx * fx) + (fy * fy));
            (fx, fy) = n > 0.0 ? (fx / n, fy / n) : (1.0, 0.0);
            (double lx, double ly) = (-fy, fx);
            return [.. new[] { (1, 1), (1, -1), (-1, -1), (-1, 1) }.Select(c =>
                (cx + (c.Item1 * FootHalfLength * fx) + (c.Item2 * FootHalfWidth * lx), cy + (c.Item1 * FootHalfLength * fy) + (c.Item2 * FootHalfWidth * ly)))];
        }
        var a = Box(me.Rig.Position, me.Facing);
        var b = Box(theirPlace, theirFacing);
        bool overlap = true;
        foreach (var poly in new[] { a, b })
        {
            for (int i = 0; i < 4 && overlap; i++)
            {
                (double ex, double ey) = (poly[(i + 1) % 4].x - poly[i].x, poly[(i + 1) % 4].y - poly[i].y);
                (double nx, double ny) = (-ey, ex);
                double amin = a.Min(p => (p.x * nx) + (p.y * ny)), amax = a.Max(p => (p.x * nx) + (p.y * ny));
                double bmin = b.Min(p => (p.x * nx) + (p.y * ny)), bmax = b.Max(p => (p.x * nx) + (p.y * ny));
                if (amax < bmin || bmax < amin) overlap = false;
            }
        }
        double gap = 0.0;
        if (!overlap)
        {
            gap = double.PositiveInfinity;
            foreach ((var p, var q) in new[] { (a, b), (b, a) })
            {
                foreach (var pt in p)
                {
                    for (int i = 0; i < 4; i++) gap = Math.Min(gap, SegDist(pt, q[i], q[(i + 1) % 4]));
                }
            }
        }
        me.MinGap = Math.Min(me.MinGap, gap);
        return overlap;
    }

    private static double SegDist((double x, double y) p, (double x, double y) a, (double x, double y) b)
    {
        (double dx, double dy) = (b.x - a.x, b.y - a.y);
        double t = Math.Clamp((((p.x - a.x) * dx) + ((p.y - a.y) * dy)) / ((dx * dx) + (dy * dy)), 0.0, 1.0);
        (double qx, double qy) = (a.x + (t * dx) - p.x, a.y + (t * dy) - p.y);
        return Math.Sqrt((qx * qx) + (qy * qy));
    }

    // A player's car turned across the line, driven the wrong way down it, two driven abreast on a 10 m road and one blocking: none of them
    // costs an opponent on a straight more than a moment. The rules alone lost 5 to 8 s to each, slowing to a creep or held behind.
    [Theory]
    [InlineData("sideways-line")]
    [InlineData("wrongway3-line")]
    [InlineData("abreast12")]
    [InlineData("block30/2.5/0.6")]
    public void APlayersCarOnAStraightCannotHoldAnOpponentUp(string name)
    {
        Scenario sc = Scenarios().First(s => s.Name == name && s.Course == "long");
        foreach (double dt in new[] { 0.021, 1.0 / 30.0 })
        {
            Result r = Run(sc, "straight", dt).Single();
            Assert.True(r.Passed && !r.Stuck, $"{name} at {dt:F3} s: not past it");
            Assert.True(r.Lost < 1.5, $"{name} at {dt:F3} s: lost {r.Lost:F2} s");
            Assert.True(r.HeldSeconds < 1.0, $"{name} at {dt:F3} s: held behind it for {r.HeldSeconds:F2} s");
        }
    }

    [Fact]
    public void Overtakes()
    {
        if (Environment.GetEnvironmentVariable("OVERTAKE_RUN") != "1") return;
        string? only = Environment.GetEnvironmentVariable("OVERTAKE_ONLY");
        List<(Scenario Sc, string Where, double Dt)> runs = [];
        foreach (Scenario sc in Environment.GetEnvironmentVariable("OVERTAKE_HELDOUT") == "1" ? HeldOut() : Scenarios())
        {
            if (only is { Length: > 0 } && !sc.Name.Contains(only, StringComparison.Ordinal)) continue;
            Course course = sc.Course == "club" ? Club.Value : LongCourse.Value;
            string where = sc.Where ?? course.Places.First(p => p.Value == sc.PlaceM).Key;
            foreach (double dt in new[] { 0.021, 1.0 / 30.0 }) runs.Add((sc, where, dt));
        }
        ConcurrentBag<Result> all = [];
        Parallel.ForEach(runs, r => { foreach (Result x in Run(r.Sc, r.Where, r.Dt)) all.Add(x); });

        string dir = Environment.GetEnvironmentVariable("OVERTAKE_OUT") ?? Path.GetTempPath();
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, Environment.GetEnvironmentVariable("OVERTAKE_NAME") ?? "overtakes.csv");
        StringBuilder csv = new("scenario,course,where,dt,opp,passed,passed_at,lost_s,follow_s,min_gap_m,contacts,contact_s,off_asphalt_s,stuck,end,min_speed_near,contacts_player,contacts_opp,kept_s,held_s\n");
        foreach (Result r in all.OrderBy(r => r.Scenario).ThenBy(r => r.Course).ThenBy(r => r.Where).ThenBy(r => r.Dt).ThenBy(r => r.Opponent))
        {
            csv.AppendLine(string.Join(',', r.Scenario, r.Course, r.Where, r.Dt.ToString("F3", CultureInfo.InvariantCulture), r.Opponent, r.Passed ? 1 : 0,
                                       F(r.PassedAt), F(r.Lost), F(r.FollowSeconds), F(r.MinGapM), r.ContactEvents, F(r.ContactSeconds), F(r.OffAsphalt),
                                       r.Stuck ? 1 : 0, r.End, F(r.MinSpeedNear), r.PlayerContacts, r.OppContacts, F(r.KeptSeconds), F(r.HeldSeconds)));
        }
        File.WriteAllText(file, csv.ToString());
        output.WriteLine($"{all.Count} results to {file}");
    }

    private static string F(double v) => double.IsFinite(v) ? v.ToString("F2", CultureInfo.InvariantCulture) : (double.IsNaN(v) ? "" : "inf");
}
