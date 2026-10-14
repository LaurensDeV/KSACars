using System.Globalization;
using System.Text.Json;
using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// What makes a route driver an opponent: a driver of its own for a seed, a little slower than the
/// quickest and surer in some bends than in others; how much slower than the best lap known of a
/// course a gap asked for is; and the speed it holds behind a car that is in its way, which a route
/// driver alone would drive into, and when it goes round that car and on which side.
/// </summary>
internal static class Rival
{
    /// <summary>The places round a lap a driver's pace and line are its own at.</summary>
    public const int Places = 24;

    /// <summary>The most a driver's lap is slower than the gap asked of all of them, and the share of a bend's speed it gives up at each place.</summary>
    public const double GapSpread = 0.04, PaceLow = 0.97, PaceHigh = 1.0;

    /// <summary>The gap to the best lap known that an opponent is asked for unless the panel says otherwise, and the most it may be.</summary>
    public const double DefaultGap = 0.0, MostGap = 0.2;

    /// <summary>
    /// What a lap gives up for every bend taken slower by a share, where nothing was measured of the
    /// course: about 0.4 of it on the courses that were, and the least any bend is slowed to.
    /// </summary>
    public const double GapPerSlowing = 0.4, LeastSlowing = 0.5;

    /// <summary>
    /// How any car is raced on a course nothing is known of: the driver's settings, how much nearer
    /// each edge than the car's own racing line its line runs, and the share of the way to it that the
    /// hardest pace is. An opponent's pace is a share of the way from the driver's own cautious
    /// settings to these.
    /// </summary>
    public sealed record Blind(Autopilot.Tuning Tune, double NearerM, double Most);

    /// <summary>
    /// The driver with all it knows of the car in its plan (<see cref="Autopilot.Tuning.Model"/> 2),
    /// a little more of the grip and of the brakes taken, and a bend's tightness averaged over 7 m.
    /// Nothing in it is one car's or one course's: every car laps the suite's circuits cleanly with
    /// it, with 6% less grip too, the F2004 round Club Motorsports in 77 s where the driver's own
    /// settings take 95. It brakes 0.3 s early: its pedal lags a stop by 2 m/s, and on the edge of a
    /// bend that was enough to run wide where gravity or the air were a little other than the rig's.
    /// </summary>
    public static readonly Blind Racing = new(new Autopilot.Tuning { Model = 2.0, GripShare = 0.92, BrakeShare = 0.8, CurveOverM = 7.0, BrakeLeadSeconds = 0.3 }, 0.0, 1.0);

    /// <summary>The way of racing blind of the car a profile's name is: the same for every car.</summary>
    public static Blind BlindFor(string car) => Racing;

    /// <summary>The share of the way to a lap searched for with a line of its own that the hardest pace is.</summary>
    public const double MostOfLearnt = 0.55;

    /// <summary>The room a driver keeps behind a car, standing, and the seconds of its own speed it keeps on top.</summary>
    public const double GapM = 5.0, GapSeconds = 0.5;

    /// <summary>
    /// A car slower than this is standing: it is gone round wherever it is, in a bend too, at no more
    /// than <see cref="CreepMs"/>, a car's length to its side since it may be across the road, and
    /// what is behind it stops this far back.
    /// </summary>
    public const double StandingMs = 3.0, CreepMs = 9.0, StandingSideM = 4.5, StandingGapM = 14.0;

    /// <summary>A car further to the side than this is not in the way.</summary>
    public const double AbreastM = 2.2;

    /// <summary>
    /// How hard a driver is taken to brake for a car ahead, m/s2, where its own brakes are not known,
    /// and the share of what they do that it is taken to brake at where they are: braking for a car
    /// at a fraction of what an F2004 does, a driver stayed so far back that no pass was ever begun, and
    /// at 0.8 of it an opponent braking for a car ahead at full lock in game spun.
    /// </summary>
    public const double FollowBrakeMs2 = 8.0, FollowBrakeShare = 0.6;

    /// <summary>A driver: how much slower than the gap asked its lap is, as a share, and what it makes of each bend.</summary>
    public sealed record Driver(double Gap, double[] Pace);

    /// <summary>
    /// A lap of one course by one car that was searched for, as a course's file of laps has it: the
    /// time it took in the rig, the driver's settings, how far inside each edge its line keeps and
    /// where that line is moved across the road; and the course it is of, as that file's SHA-256.
    /// </summary>
    /// <param name="Most">The share of the way to it that the hardest pace is: all of it for a lap that was practised, which moves no line, and 0.55 for one that was searched for with a line of its own.</param>
    public sealed record Learnt(double Seconds, Autopilot.Tuning Tune, double InsideM, double[] LineM, string CircuitSha256, double Most = MostOfLearnt)
    {
        /// <summary>
        /// What the lap gave up with every bend taken slower, as it was lapped: each share of a bend's
        /// speed and how much slower than the lap that was, from the least slowing to the most. Empty
        /// where it was not measured.
        /// </summary>
        public (double Slowing, double Gap)[] Slowed { get; init; } = [];

        /// <summary>As its file has it.</summary>
        public string ToJson() => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["seconds"] = Math.Round(Seconds, 3), ["circuit_sha256"] = CircuitSha256, ["inside_m"] = Math.Round(InsideM, 4),
            ["tune"] = Tune.Written(), ["line_m"] = LineM, ["most"] = Most,
            ["slowed"] = Slowed.Select(p => new[] { Math.Round(p.Slowing, 4), Math.Round(p.Gap, 5) }).ToArray(),
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>How an opponent is to be driven: the driver's push and settings, its line, and how far inside the edges that keeps, or nothing for the car's own.</summary>
    public sealed record Way(double Push, Autopilot.Tuning Tune, double[] LineM, double InsideM);

    /// <summary>A learnt lap from its file's text, or null with what is wrong with it.</summary>
    public static Learnt? Read(string json, out string why)
    {
        try
        {
            using JsonDocument file = JsonDocument.Parse(json);
            JsonElement root = file.RootElement;
            if (new Autopilot.Tuning().With(root.GetProperty("tune").GetString() ?? "", out why) is not { } tune) return null;
            double[] line = [.. root.GetProperty("line_m").EnumerateArray().Select(n => n.GetDouble())];
            double seconds = root.GetProperty("seconds").GetDouble(), inside = root.GetProperty("inside_m").GetDouble();
            if (!(seconds > 0.0) || !(inside > 0.0) || line.Any(n => !double.IsFinite(n)))
            {
                why = "a time, a margin or a line that is not a number over nothing";
                return null;
            }
            why = "";
            double most = root.TryGetProperty("most", out JsonElement share) ? Math.Clamp(share.GetDouble(), 0.0, 1.0) : MostOfLearnt;
            (double, double)[] slowed = root.TryGetProperty("slowed", out JsonElement table)
                ? [.. table.EnumerateArray().Select(p => (p[0].GetDouble(), p[1].GetDouble())).Where(p => double.IsFinite(p.Item1) && double.IsFinite(p.Item2))]
                : [];
            return new Learnt(seconds, tune, inside, line, root.GetProperty("circuit_sha256").GetString() ?? "", most) { Slowed = slowed };
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            why = e.Message;
            return null;
        }
    }

    /// <summary>
    /// How the opponent a seed is drives, a gap slower than the best lap known of this course by its
    /// car: one learnt here, all of it or <see cref="Learnt.Most"/> of a lap searched for with a line
    /// of its own, or with none its car's <see cref="Blind"/>. Every bend is taken slower by the share
    /// that gives up the gap, the driver's own and the one asked together, read off what the lap was
    /// measured to give up where it was and reckoned at <see cref="GapPerSlowing"/> where it was not.
    /// </summary>
    /// <param name="gap">How much slower than that lap all of them are asked to be, as a share of it.</param>
    /// <param name="ownInsideM">How far inside each edge the car's own racing line keeps.</param>
    /// <param name="car">The car's name, as its profile has it.</param>
    /// <param name="flatOut">Whether the driver is the lap and no more: no gap, none of its own, and every bend at the lap's pace.</param>
    public static Way Drive(Learnt? learnt, int seed, double gap, double ownInsideM, string car, bool flatOut = false)
    {
        Driver own = flatOut ? FlatOut : Of(seed);
        if (flatOut) gap = 0.0;
        Autopilot.Tuning tune;
        double[] line = new double[Places];
        double inside;
        if (learnt is null)
        {
            Blind blind = BlindFor(car);
            tune = new Autopilot.Tuning().Toward(blind.Tune, blind.Most);
            inside = ownInsideM - (blind.NearerM * blind.Most);
        }
        else
        {
            // Not all the way to a searched lap: it was the best of thousands tried, and past this share of it a driver leaves the road.
            tune = new Autopilot.Tuning().Toward(learnt.Tune, learnt.Most);
            for (int i = 0; i < Places; i++)
            {
                double at = (double)i / Places;
                line[i] = learnt.LineM.Length == 0 ? 0.0 : learnt.LineM[(int)(at * learnt.LineM.Length) % learnt.LineM.Length] * learnt.Most;
            }
            inside = ownInsideM + ((learnt.InsideM - ownInsideM) * learnt.Most);
        }

        // What the driver's own bends already give up is not asked of every bend again.
        double walked = GapPerSlowing * (1.0 - own.Pace.Average());
        double slowing = Slowing(learnt?.Slowed ?? [], Math.Max(Math.Clamp(gap, 0.0, MostGap) + own.Gap - walked, 0.0));
        double[] bends = new double[Places];
        for (int i = 0; i < Places; i++) bends[i] = tune.PaceAt((double)i / Places) * own.Pace[i] * slowing;
        return new Way(0.0, tune with { Pace = bends }, line, inside);
    }

    /// <summary>
    /// The share of every bend's speed that gives up a gap, read between the slowings a lap was
    /// measured at, or with none reckoned at <see cref="GapPerSlowing"/>; never under <see cref="LeastSlowing"/>.
    /// </summary>
    public static double Slowing(IReadOnlyList<(double Slowing, double Gap)> slowed, double gap)
    {
        if (gap <= 0.0) return 1.0;
        (double m, double g) was = (1.0, 0.0);
        foreach ((double m, double g) in slowed.OrderByDescending(p => p.Slowing))
        {
            if (g >= gap && g > was.g) return Math.Max(was.m + ((m - was.m) * (gap - was.g) / (g - was.g)), LeastSlowing);
            if (g > was.g) was = (m, g);
        }
        // Past the last measured, on at the reckoned rate.
        return Math.Max(was.m - ((gap - was.g) / GapPerSlowing), LeastSlowing);
    }

    /// <summary>A driver with nothing of its own: the lap it is given, all of it, everywhere.</summary>
    public static readonly Driver FlatOut = new(0.0, [.. Enumerable.Repeat(1.0, Places)]);

    /// <summary>The driver a seed is: the same one every time.</summary>
    public static Driver Of(int seed)
    {
        Random dice = new(unchecked((seed * 7919) + 104729));
        double gap = dice.NextDouble() * GapSpread;

        // A walk round the lap, so a driver is good through a run of bends and not at one place and bad at the next.
        double[] pace = new double[Places];
        double p = dice.NextDouble();
        for (int i = 0; i < Places; i++)
        {
            p = Math.Clamp(p + ((dice.NextDouble() - 0.5) * 0.5), 0.0, 1.0);
            pace[i] = PaceLow + ((PaceHigh - PaceLow) * p);
        }
        return new Driver(gap, pace);
    }

    /// <summary>How far past a standing car, and past a moving one before the driver's own speed is counted, the road has to have room to one side.</summary>
    public const double StandingPastM = 15.0, MovingPastM = 25.0;

    /// <summary>
    /// How much of the road ahead has to have room to one side for a driver to go round the nearest car
    /// in its way: as far as that takes and no more. A racing line runs out to an edge somewhere in any
    /// long stretch, and room that is asked of all of it is never there.
    /// </summary>
    public static double RoomOver(ReadOnlySpan<Seen> cars, double speedMs)
    {
        double over = PassLookM;
        foreach (Seen car in cars)
        {
            if (car.AheadM <= 0.0 || Math.Abs(car.SideM) > StandingSideM) continue;
            over = Math.Min(over, car.SpeedMs < StandingMs ? car.AheadM + StandingPastM : car.AheadM + MovingPastM + (2.0 * Math.Max(speedMs, 0.0)));
        }
        return over;
    }

    /// <summary>How quick the driver a seed is, as one number to put drivers in order by: the share of its lap it gives up, its own and in the bends, taken from nothing.</summary>
    public static double Strength(int seed)
    {
        Driver driver = Of(seed);
        return -(driver.Gap + (GapPerSlowing * (1.0 - driver.Pace.Average())));
    }

    /// <summary>
    /// The drivers of so many seeds from the quickest to the slowest, which is the order they are
    /// gridded in: a driver goes round a car only on a straight with room, and until one comes a
    /// quick one behind a slow one is as slow as it.
    /// </summary>
    public static int[] Gridded(int count) => [.. Enumerable.Range(1, count).OrderByDescending(Strength).ThenBy(seed => seed)];

    /// <summary>
    /// The most a driver goes at with a car so far ahead of it and so far to its side going at
    /// <paramref name="leaderMs"/> the way the driver faces: the leader's own speed and what can still
    /// be braked off in the room there is, or no limit where the car is not in the way.
    /// </summary>
    /// <param name="brakeAt">How hard the driver's car brakes at a speed, m/s2, all of it: at the leader's speed, the least it brakes at on the way down to it.</param>
    public static double FollowCap(double aheadM, double sideM, double leaderMs, double speedMs, Func<double, double>? brakeAt = null)
    {
        if (aheadM <= 0.0 || Math.Abs(sideM) > AbreastM) return double.PositiveInfinity;
        double lead = Math.Max(leaderMs, 0.0);
        // Behind a car that is standing, room to steer out from behind it: stopped at its tail there is no going round.
        double room = aheadM - (lead < StandingMs ? StandingGapM : GapM) - (GapSeconds * Math.Max(speedMs, 0.0));
        double brake = brakeAt is null ? FollowBrakeMs2 : Math.Max(FollowBrakeShare * brakeAt(lead), FollowBrakeMs2);
        double cap = room <= 0.0 ? lead : Math.Sqrt((lead * lead) + (2.0 * brake * room));
        // Far enough back that it would not be braking yet, or behind a car pulling away, the car ahead is nothing to it.
        return cap > Math.Max(speedMs, 0.0) + 5.0 ? double.PositiveInfinity : cap;
    }

    /// <summary>
    /// Another car as a driver sees it: how far on along the driver's route it is, how far to the
    /// driver's left, its speed the way the driver faces, and whether it is only waiting behind a car
    /// in its own way, which is followed and not gone round: that car is the one to go round.
    /// </summary>
    public readonly record struct Seen(object Who, double AheadM, double SideM, double SpeedMs, bool Queued = false);

    /// <summary>What a driver remembers of the car it is going round, from one look to the next: the caller's, one a driver.</summary>
    public sealed class Passing
    {
        /// <summary>The car being gone round, or none.</summary>
        public object? Who { get; private set; }

        /// <summary>The side of it the driver goes: 1 its left, -1 its right, 0 with no pass on.</summary>
        public int Side { get; private set; }

        /// <summary>Whether the pass is given up and the driver is dropping back to go in behind.</summary>
        public bool Back { get; internal set; }

        /// <summary>Seconds until another pass may be started, after one given up.</summary>
        public double Rest { get; internal set; }

        /// <summary>
        /// Seconds from the start in which no pass is begun, nor one taken up for being beside a car:
        /// on the grid every car ahead is standing, and a field that went round them all ran two
        /// abreast into the first bends.
        /// </summary>
        public double Launch { get; set; }

        /// <summary>The speed of the car being gone round at the last look, m/s, to tell it braking.</summary>
        internal double LeadMs { get; set; } = double.NaN;

        internal void Begin(object who, int side) => (Who, Side, Back, LeadMs) = (who, side, false, double.NaN);

        internal void End() => (Who, Side, Back, LeadMs) = (null, 0, false, double.NaN);
    }

    /// <summary>How far apart, middle to middle, a driver means to go by a car, and how far over <see cref="AbreastM"/> it is before the car no longer holds it back.</summary>
    public const double PassSideM = 3.0, PassMarginM = 0.3;

    /// <summary>How much quicker than a car a driver must be free to go to start round it, m/s, and to carry on once out.</summary>
    public const double PassFasterMs = 4.0, PassKeepMs = 1.5;

    /// <summary>How far past a car a driver is before it comes back to its line, and how far behind one before it goes back in after giving up.</summary>
    public const double PassClearM = 9.0, TuckM = 10.0;

    /// <summary>Slowing faster than this, m/s2, the car being gone round is braking, and a driver not yet out from behind it is held back by it.</summary>
    public const double LeadBrakingMs2 = 3.0;

    /// <summary>Less than this far behind a car's middle a driver is beside it, and goes on with a pass it would otherwise give up.</summary>
    public const double LevelM = 2.0;

    /// <summary>The seconds moving out takes, the seconds from a car at which a pass is started, and the seconds after giving one up before the next, or one of a standing car.</summary>
    public const double PullOutSeconds = 1.5, PassSoonSeconds = 4.0, RestSeconds = 3.0, StandingRestSeconds = 1.0;

    /// <summary>The seconds a car is behind its driver's aim in moving across, and how much longer than reckoned moving out is taken to be.</summary>
    public const double PassLagSeconds = 1.0, PassLagShare = 1.3;

    /// <summary>Further than this from its line a driver is off it, for a car between the two.</summary>
    public const double OffLineM = 1.0;

    /// <summary>How much slower than the car it gave up passing a driver goes to drop behind it, m/s.</summary>
    public const double DropMs = 3.0;

    /// <summary>A car this far behind, or this far beyond the one being passed, on the side a driver would go, has that side.</summary>
    public const double PassBehindM = 10.0, PassBeyondM = 20.0;

    /// <summary>How far ahead the road's room either side is asked of <see cref="Autopilot.RoadAhead"/> for a pass.</summary>
    public const double PassLookM = 150.0;

    /// <summary>The sideways pull, m/s2, under which a stretch is straight enough to pass on, and the tightest that is at any speed, 1/m.</summary>
    public const double StraightMs2 = 2.5, StraightMostPerM = 1.0 / 150.0;

    /// <summary>The bend under which the road is straight enough to pass on at a speed, 1/m.</summary>
    public static double StraightPerM(double speedMs) => Math.Min(StraightMs2 / Math.Max(speedMs * speedMs, 1.0), StraightMostPerM);

    /// <summary>
    /// Another car as the driver of a car at <paramref name="place"/> facing <paramref name="facing"/>
    /// sees it, or nothing past <paramref name="lookM"/>. Ahead, it is measured along the driver's
    /// route where it has one, by which a car round a bend is in the way; behind, in a straight line,
    /// a route being searched forwards only. A car facing the other way is taken to be standing.
    /// </summary>
    public static Seen? See(object who, Autopilot? pilot, double3 place, double3 facing, double3 theirPlace, double3 theirFacing, double theirMs, double lookM)
    {
        double3 up = Vec.Unit(place), ahead = Vec.Unit(Vec.RejectFrom(facing, up));
        double3 to = Vec.RejectFrom(theirPlace - place, up);
        if (Vec.Len(to) > lookM) return null;
        double along = Vec.Dot(to, ahead), side = Vec.Dot(to, Vec.Cross(up, ahead));
        if (along > 0.0 && pilot is not null && pilot.Along(theirPlace, lookM, out double on, out double left)) (along, side) = (on, left);
        return new Seen(who, along, side, Vec.Dot(Vec.Unit(theirFacing), ahead) > -0.3 ? theirMs : 0.0);
    }

    /// <summary>
    /// Where across the road a driver is to run and the most it may go at, for the cars round it: on
    /// its line and held behind a car in its way, or out to one side of that car and past it.
    ///
    /// <para>A pass is started behind a car the driver is free to go <see cref="PassFasterMs"/> quicker
    /// than, if the road is straight for as far as the pass takes and one side has room for the two
    /// abreast with no third car there: the side with more to spare. That side is kept until the
    /// pass is over. The car holds the driver back until they are <see cref="AbreastM"/> and
    /// <see cref="PassMarginM"/> apart, or will be before the driver is up to it, and the driver comes back to its line <see cref="PassClearM"/>
    /// past it. A pass that can no longer be finished, the driver not yet beside the car, is given
    /// up: it drops back, holding its side, goes in behind and waits <see cref="RestSeconds"/>.</para>
    /// </summary>
    /// <param name="freeMs">The speed the driver's plan allows here with no car in the way.</param>
    /// <param name="crossM">How far to the left of its route the driver's car is.</param>
    /// <param name="leftM">How far to the left of the route a car's middle may be over the stretch ahead, and <paramref name="rightM"/> to the right.</param>
    /// <param name="straightM">How far on the route first bends.</param>
    /// <param name="brakeAt">How hard the driver's car brakes at a speed, m/s2, or null for <see cref="FollowBrakeMs2"/>.</param>
    public static (double SideM, double CapMs) Pass(Passing state, ReadOnlySpan<Seen> cars, double speedMs, double freeMs, double crossM,
                                                    double leftM, double rightM, double straightM, double dt, Func<double, double>? brakeAt = null)
    {
        (double side, double cap) = Choose(state, cars, speedMs, freeMs, crossM, leftM, rightM, straightM, dt, brakeAt);
        return (KeepLane(cars, crossM, side), cap);
    }

    /// <summary>Half a car's width and the room kept from a car alongside, middle to middle, m, and how far ahead or behind a car is alongside.</summary>
    public const double LaneM = 2.4, AlongsideM = 7.0;

    /// <summary>
    /// The side a driver asks to run at, moved no nearer than <see cref="LaneM"/> to a car alongside it,
    /// and where it is already nearer, no nearer than it is: a racing line runs across the road, and the
    /// car being passed followed it into the one beside it, the two side by side into a bend.
    /// </summary>
    public static double KeepLane(ReadOnlySpan<Seen> cars, double crossM, double sideM)
    {
        double want = sideM;
        foreach (Seen car in cars)
        {
            if (Math.Abs(car.AheadM) > AlongsideM || Math.Abs(car.SideM) < 0.5) continue;
            double theirs = crossM + car.SideM;
            if (car.SideM > 0.0 && want > theirs - LaneM) want = Math.Max(theirs - LaneM, Math.Min(crossM, want));
            if (car.SideM < 0.0 && want < theirs + LaneM) want = Math.Min(theirs + LaneM, Math.Max(crossM, want));
        }
        return want;
    }

    private static (double SideM, double CapMs) Choose(Passing state, ReadOnlySpan<Seen> cars, double speedMs, double freeMs, double crossM,
                                                       double leftM, double rightM, double straightM, double dt, Func<double, double>? brakeAt)
    {
        state.Rest = Math.Max(state.Rest - Math.Max(dt, 0.0), 0.0);
        state.Launch = Math.Max(state.Launch - Math.Max(dt, 0.0), 0.0);
        if (state.Side != 0 && (Find(cars, state.Who) is not { } still || still.AheadM < -PassClearM)) state.End();
        if (state.Side == 0) Start(state, cars, speedMs, freeMs, crossM, leftM, rightM, straightM, brakeAt);
        if (state.Side == 0) return (0.0, Held(cars, null, crossM, 0.0, speedMs, brakeAt));

        Seen car = Find(cars, state.Who)!.Value;
        double lead = Math.Max(car.SpeedMs, 0.0), theirs = crossM + car.SideM;
        bool standing = lead < StandingMs;
        double side = Math.Clamp(standing ? Outside(cars, car, crossM, state.Side) : theirs + (state.Side * PassSideM), -rightM, leftM);
        double others = Held(cars, state.Who, crossM, side, speedMs, brakeAt, standing ? car : null);

        bool fits = standing ? ClearOf(cars, car, crossM, side) : Math.Abs(side - theirs) >= AbreastM + PassMarginM;
        bool open = freeMs - lead >= PassKeepMs && others >= lead + PassKeepMs
                    && (standing || freeMs * (car.AheadM + PassClearM) / Closing(speedMs, freeMs, lead) <= straightM);
        if (car.AheadM > 0.0 && !(fits && (open || car.AheadM <= LevelM))) state.Back = true;
        if (state.Back && standing)
        {
            // There is no dropping back behind a car that stands, which would be waiting for it for good: the pass is over, and the next may take either side.
            state.End();
            state.Rest = StandingRestSeconds;
            return (0.0, Held(cars, null, crossM, 0.0, speedMs, brakeAt));
        }
        if (state.Back)
        {
            if (car.AheadM < TuckM) return (side, Math.Min(others, Math.Max(lead - DropMs, 0.0)));
            state.End();
            state.Rest = RestSeconds;
            return (0.0, Held(cars, null, crossM, 0.0, speedMs, brakeAt));
        }
        if (standing)
        {
            // Round a standing car at a creep, and slower the nearer it is while not yet clear of it to the side: never stopped, which is not steering out.
            // So round every car standing beside it, the nearest of them not yet cleared setting the pace.
            double nearest = double.PositiveInfinity;
            foreach (Seen beside in cars)
            {
                if (!Equals(beside.Who, car.Who) && !Together(beside, car)) continue;
                if (beside.AheadM > 0.0 && Math.Abs(beside.SideM) <= AbreastM + PassMarginM) nearest = Math.Min(nearest, beside.AheadM);
            }
            double creep = double.IsPositiveInfinity(nearest) ? CreepMs : Math.Clamp((nearest - 5.0) * 0.45, 1.2, CreepMs);
            return (side, Math.Min(others, creep));
        }
        // Not held either while it will be out from behind the car before it is up to it, which is reckoned on the car
        // keeping its speed: one braking hard ahead of a driver still behind it was run into at 18 m/s.
        double slowing = dt > 0.0 && double.IsFinite(state.LeadMs) ? (state.LeadMs - lead) / dt : 0.0;
        state.LeadMs = lead;
        double moving = PassLagSeconds + ((AbreastM + PassMarginM - Math.Abs(car.SideM)) / Math.Max(Math.Min(Autopilot.SideRateMs, Autopilot.SideSlope * speedMs), 0.1));
        bool apart = Math.Abs(car.SideM) > AbreastM + PassMarginM
                     || (slowing < LeadBrakingMs2 && car.AheadM - TuckM > PassLagShare * (speedMs - lead) * moving);
        return (side, Math.Min(others, apart ? double.PositiveInfinity : FollowCap(car.AheadM, 0.0, lead, speedMs, brakeAt)));
    }

    /// <summary>How far along the road from a standing car another standing car is one obstacle with it, gone round as one, m.</summary>
    public const double TogetherM = 8.0;

    // Whether a car is another that stands beside a standing car being gone round: two cars stopped side by side,
    // one going round the nearer, had the other where it would go and in front of where it was, and never moved.
    private static bool Together(Seen car, Seen standing) =>
        !Equals(car.Who, standing.Who) && standing.SpeedMs < StandingMs && car.SpeedMs < StandingMs && Math.Abs(car.AheadM - standing.AheadM) <= TogetherM;

    // Where to the left of the route a driver runs to go round a standing car on one side: far enough out to clear it and every car standing beside it on that side.
    private static double Outside(ReadOnlySpan<Seen> cars, Seen standing, double crossM, int way)
    {
        double theirs = crossM + standing.SideM, side = theirs + (way * StandingSideM);
        foreach (Seen car in cars)
        {
            if (!Together(car, standing)) continue;
            double at = crossM + car.SideM;
            if (way * (at - theirs) > -AbreastM) side = way > 0 ? Math.Max(side, at + StandingSideM) : Math.Min(side, at - StandingSideM);
        }
        return side;
    }

    // Whether a line so far to the left of the route clears a standing car and every car standing beside it.
    private static bool ClearOf(ReadOnlySpan<Seen> cars, Seen standing, double crossM, double sideM)
    {
        if (Math.Abs(crossM + standing.SideM - sideM) < AbreastM + PassMarginM) return false;
        foreach (Seen car in cars)
        {
            if (Together(car, standing) && Math.Abs(crossM + car.SideM - sideM) < AbreastM + PassMarginM) return false;
        }
        return true;
    }

    private static Seen? Find(ReadOnlySpan<Seen> cars, object? who)
    {
        foreach (Seen car in cars)
        {
            if (Equals(car.Who, who)) return car;
        }
        return null;
    }

    // How fast a car is come up on over a pass, the driver getting from the speed it has to the one it is free to go.
    private static double Closing(double speedMs, double freeMs, double leadMs) =>
        Math.Max((0.5 * (Math.Max(speedMs, leadMs) + freeMs)) - leadMs, 1.0);

    // The most a driver goes at for every car but one: a car is in its way beside where it is or beside where it is going.
    private static double Held(ReadOnlySpan<Seen> cars, object? but, double crossM, double sideM, double speedMs, Func<double, double>? brakeAt,
                               Seen? goneRound = null)
    {
        double cap = double.PositiveInfinity;
        foreach (Seen car in cars)
        {
            if (but is not null && Equals(car.Who, but)) continue;
            if (goneRound is { } round && Together(car, round)) continue;
            double beside = Math.Min(Math.Abs(car.SideM), Math.Abs(crossM + car.SideM - sideM));
            cap = Math.Min(cap, FollowCap(car.AheadM, beside, car.SpeedMs, speedMs, brakeAt));
        }
        return cap;
    }

    private static void Start(Passing state, ReadOnlySpan<Seen> cars, double speedMs, double freeMs, double crossM,
                              double leftM, double rightM, double straightM, Func<double, double>? brakeAt)
    {
        if (state.Launch > 0.0) return;

        // Off its line with a car between it and the line, a driver is passing that car whether it chose to or not: the side it is on is kept.
        foreach (Seen car in cars)
        {
            if (Math.Abs(crossM) <= OffLineM || car.AheadM <= -PassClearM || car.AheadM >= TuckM) continue;
            if (Math.Abs(car.SideM) <= AbreastM || Math.Abs(crossM + car.SideM) > AbreastM + PassMarginM) continue;
            state.Begin(car.Who, car.SideM < 0.0 ? 1 : -1);
            return;
        }
        if (state.Rest > 0.0) return;

        // The nearest car on the driver's line: one beyond it is that car's to pass first.
        Seen? nearest = null;
        foreach (Seen car in cars)
        {
            if (car.AheadM <= 0.0 || Math.Min(Math.Abs(car.SideM), Math.Abs(crossM + car.SideM)) > AbreastM) continue;
            if (nearest is not { } n || car.AheadM < n.AheadM) nearest = car;
        }
        // A car waiting behind another is followed: it goes when that one does, or the one it waits for is gone round first.
        if (nearest is not { } slow || (slow.Queued && slow.SpeedMs < StandingMs)) return;

        double lead = Math.Max(slow.SpeedMs, 0.0);
        if (freeMs - lead < PassFasterMs) return;
        bool holds = FollowCap(slow.AheadM, 0.0, lead, speedMs, brakeAt) < freeMs;
        if (!holds && slow.AheadM - GapM > (speedMs - lead) * PassSoonSeconds) return;
        bool standing = lead < StandingMs;
        if (!standing && freeMs * (PullOutSeconds + ((slow.AheadM + PassClearM) / Closing(speedMs, freeMs, lead))) > straightM) return;

        double theirs = crossM + slow.SideM, most = -1.0;
        int best = 0;
        foreach (int way in (ReadOnlySpan<int>)[1, -1])
        {
            // Already to one side of it, that is the side, unless it is standing and far enough ahead to cross behind it.
            if (Math.Abs(slow.SideM) > AbreastM && way != (slow.SideM < 0.0 ? 1 : -1)
                && !(standing && slow.AheadM > 2.0 * StandingSideM / Autopilot.SideSlopeSlow)) continue;
            double side = standing ? Outside(cars, slow, crossM, way) : theirs + (way * PassSideM);
            double spare = way > 0 ? leftM - side : side + rightM;
            // Round a standing car as far to the side as the road has, if that is far enough to be clear of it.
            if (standing && spare < 0.0 && ClearOf(cars, slow, crossM, Math.Clamp(side, -rightM, leftM)))
            {
                (side, spare) = (Math.Clamp(side, -rightM, leftM), 0.0);
            }
            if (spare < 0.0 || spare <= most || Taken(cars, slow, crossM, side)) continue;
            (best, most) = (way, spare);
        }
        if (best != 0) state.Begin(slow.Who, best);
    }

    // Whether a third car is where a pass would be driven: alongside, close behind, or just beyond the car being passed.
    private static bool Taken(ReadOnlySpan<Seen> cars, Seen slow, double crossM, double sideM)
    {
        foreach (Seen car in cars)
        {
            if (Equals(car.Who, slow.Who) || Together(car, slow) || car.AheadM < -PassBehindM || car.AheadM > slow.AheadM + PassClearM + PassBeyondM) continue;
            // A car behind in the driver's own lane follows it out, and takes nothing from it.
            if (car.AheadM < 0.0 && Math.Abs(car.SideM) <= AbreastM) continue;
            if (Math.Abs(crossM + car.SideM - sideM) <= AbreastM + PassMarginM) return true;
        }
        return false;
    }
}
