using System.Globalization;
using System.Text.Json;
using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// What makes a route driver an opponent: a driver of its own for a seed, a little slower than the
/// quickest and surer in some bends than in others; how much slower than the best lap known of a
/// course a gap asked for is; and the speed it holds behind another opponent in its way, which a route
/// driver alone would drive into, and when it goes round that car and on which side. A player's car
/// near it is <see cref="Lanes"/>'.
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

    /// <summary>How much slower than a car it is too near the driver goes, m/s for every metre too near.</summary>
    public const double BackOffPerS = 2.0;

    /// <summary>
    /// A car slower than this is standing: it is gone round wherever it is, in a bend too, at no more
    /// than <see cref="CreepMs"/> while the driver is not yet out from behind it; a car this far to the
    /// side of the driver's line is still reckoned for the room round it; and what is behind it stops
    /// this far back.
    /// </summary>
    public const double StandingMs = 3.0, CreepMs = 9.0, StandingSideM = 4.5, StandingGapM = 14.0;

    /// <summary>How far a driver goes round a standing car from the edge of it, middle to middle, m: a car's width and more.</summary>
    public const double StandingPassM = 3.5;

    /// <summary>How much nearer the road's edge than its racing margin a driver may go round a car standing or crawling, m.</summary>
    public const double SqueezeM = 0.6;

    /// <summary>
    /// How much nearer the road's edge a driver may go for the nearest car in its way: <see cref="SqueezeM"/>
    /// for one standing or crawling, gone round slowly, and nothing for one gone round at speed.
    /// </summary>
    public static double Squeeze(ReadOnlySpan<Seen> cars, double freeMs)
    {
        Seen? nearest = null;
        foreach (Seen car in cars)
        {
            if (car.AheadM <= -PassClearM || car.AheadM > PassLookM || Math.Abs(car.SideM) > StandingSideM + car.WideM) continue;
            if (nearest is not { } n || Math.Abs(car.AheadM) < Math.Abs(n.AheadM)) nearest = car;
        }
        return nearest is { } c && (c.SpeedMs < StandingMs || Crawling(c.SpeedMs, freeMs)) ? SqueezeM : 0.0;
    }

    /// <summary>
    /// How much faster than a creep a driver goes by a standing car for every metre further from it than
    /// <see cref="AbreastM"/>, m/s; the seconds of a car coming backwards that are taken off the room to
    /// it; and how far on a driver goes for every metre it still has to move across, m.
    /// </summary>
    public const double PassByMsPerM = 8.0, ComingSeconds = 2.0, AcrossRunM = 5.0;

    /// <summary>How far short of a standing car the road's room is first asked of, m, and the seconds a pass may not fit before it is given up.</summary>
    public const double StandingFromM = 8.0, UnfitSeconds = 0.4;

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
            if (car.AheadM <= 0.0 || Math.Abs(car.SideM) > StandingSideM + car.WideM) continue;
            over = Math.Min(over, car.SpeedMs < StandingMs ? car.AheadM + StandingPastM : car.AheadM + MovingPastM + (2.0 * Math.Max(speedMs, 0.0)));
        }
        return over;
    }

    /// <summary>
    /// How far on the road the room to go round the nearest car in the way is first asked of: from a
    /// little before the driver is up to it, where it pulls out, and not from where the driver is. A
    /// racing line runs out to the edge on the side a pass will take somewhere on the way up to a car.
    /// </summary>
    public static double RoomFrom(ReadOnlySpan<Seen> cars, double speedMs)
    {
        double from = double.PositiveInfinity;
        foreach (Seen car in cars)
        {
            if (car.AheadM <= 0.0 || Math.Abs(car.SideM) > StandingSideM + car.WideM) continue;
            // Round a standing car only the road beside it matters: the driver slows to a creep wherever it is not yet out from behind it.
            from = Math.Min(from, car.AheadM - (car.SpeedMs < StandingMs ? StandingFromM : 5.0 + (PullOutSeconds * Math.Max(speedMs, 0.0))));
        }
        return double.IsPositiveInfinity(from) ? 0.0 : Math.Max(from, 0.0);
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
    /// <param name="gapM">The room kept behind a car that is moving, m, and <paramref name="gapSeconds"/> the seconds of the driver's own speed on top.</param>
    public static double FollowCap(double aheadM, double sideM, double leaderMs, double speedMs, Func<double, double>? brakeAt = null,
                                   double gapM = GapM, double gapSeconds = GapSeconds)
    {
        if (aheadM <= 0.0 || Math.Abs(sideM) > AbreastM) return double.PositiveInfinity;
        double lead = Math.Max(leaderMs, 0.0);
        // Behind a car that is standing, room to steer out from behind it: stopped at its tail there is no going round. One coming backwards takes some of it.
        double room = aheadM - (lead < StandingMs ? StandingGapM : gapM) - (gapSeconds * Math.Max(speedMs, 0.0)) - (Math.Max(-leaderMs, 0.0) * ComingSeconds);
        double brake = brakeAt is null ? FollowBrakeMs2 : Math.Max(FollowBrakeShare * brakeAt(lead), FollowBrakeMs2);
        double cap = room <= 0.0 ? lead : Math.Sqrt((lead * lead) + (2.0 * brake * room));
        // Nearer than a car's room behind it, as when it cut in, it drops back rather than sitting on its tail.
        if (aheadM < gapM) cap = Math.Max(lead - (BackOffPerS * (gapM - aheadM)), 0.0);
        // Far enough back that it would not be braking yet, or behind a car pulling away, the car ahead is nothing to it.
        return cap > Math.Max(speedMs, 0.0) + 5.0 ? double.PositiveInfinity : cap;
    }

    /// <summary>
    /// Another car as a driver sees it: how far on along the driver's route it is, how far to the
    /// driver's left, its speed the way the driver faces, and whether it is only waiting behind a car
    /// in its own way, which is followed and not gone round: that car is the one to go round.
    /// </summary>
    public readonly record struct Seen(object Who, double AheadM, double SideM, double SpeedMs, bool Queued = false)
    {
        /// <summary>Whether it is a player's car, and not another opponent: one near is <see cref="KSACars.Lanes"/>' to go round, and it never holds a driver up for long.</summary>
        public bool Player { get; init; }

        /// <summary>How much further to either side a car turned across the road reaches than one facing along it, m.</summary>
        public double WideM { get; init; }

        /// <summary>
        /// How much further than its middle to its left, and <see cref="RightM"/> to its right, the car is
        /// reckoned to be across the road: its width turned, where it has been lately and where it is
        /// going. Filled in by <see cref="Pass"/>.
        /// </summary>
        public double LeftM { get; init; }

        public double RightM { get; init; }

        /// <summary>How far to the left of the road's middle it is, m, where that is known: what says a car keeping to its lane is not moving across, while a racing line runs across under it.</summary>
        public double? RoadM { get; init; }

        /// <summary>How far it moves to the left of the driver's route for every metre on it goes keeping to its lane: a racing line running across the road under it.</summary>
        public double DriftPerM { get; init; }
    }

    // How far a line `at` to the left of the route is from a car's span across the road, middle to middle: nothing where the line is inside it.
    private static double Apart(Seen car, double crossM, double at)
    {
        double theirs = crossM + car.SideM;
        return at > theirs + car.LeftM ? at - theirs - car.LeftM : at < theirs - car.RightM ? theirs - car.RightM - at : 0.0;
    }

    // The same for a driver moving across from one line to another: the nearer of the way it goes.
    private static double Apart(Seen car, double crossM, double from, double to)
    {
        double theirs = crossM + car.SideM;
        (double lo, double hi) = (Math.Min(from, to), Math.Max(from, to));
        return hi < theirs - car.RightM ? theirs - car.RightM - hi : lo > theirs + car.LeftM ? lo - theirs - car.LeftM : 0.0;
    }

    // The edge of a car's span on one side: 1 its left, -1 its right.
    private static double Edge(Seen car, double crossM, int way) => crossM + car.SideM + (way > 0 ? car.LeftM : -car.RightM);

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

        /// <summary>What the lanes remember, and the seconds they go on deciding after the last look a player's car was near.</summary>
        internal Lanes.Memory Lanes { get; } = new();

        internal double LanesFor { get; set; }

        /// <summary>
        /// Seconds from the start in which no pass is begun, nor one taken up for being beside a car:
        /// on the grid every car ahead is standing, and a field that went round them all ran two
        /// abreast into the first bends.
        /// </summary>
        public double Launch { get; set; }

        /// <summary>The speed of the car being gone round at the last look, m/s, to tell it braking.</summary>
        internal double LeadMs { get; set; } = double.NaN;

        /// <summary>Seconds the pass on has not fitted the road or been open.</summary>
        internal double Unfit { get; set; }

        /// <summary>Each car seen at the last look: where it was across the road, how fast that was changing, and the most to either side it has been lately.</summary>
        internal Dictionary<object, Wander> Tracks { get; } = [];

        internal void Begin(object who, int side) => (Who, Side, Back, LeadMs, Unfit) = (who, side, false, double.NaN, 0.0);

        internal void End() => (Who, Side, Back, LeadMs, Unfit) = (null, 0, false, double.NaN, 0.0);
    }

    /// <summary>Where a car has been across the road, to the left of the driver's route.</summary>
    internal sealed class Wander
    {
        public double At, RateMs, Road, RoadMs, Lo, Hi;
        public bool Seen;
    }

    /// <summary>
    /// How fast what a car has been across the road fades, m/s; how quickly its rate across is taken
    /// up, s; the most of it that is believed, m/s; and the most seconds ahead it is carried to.
    /// </summary>
    public const double WanderFadeMs = 2.5, WanderLagSeconds = 0.3, WanderMostMs = 8.0, WanderAheadSeconds = 2.5;

    /// <summary>Moving across the driver's route slower than this, m/s, a car is keeping to the driver's own line.</summary>
    public const double OnLineMs = 0.5;

    // The cars as the driver is to reckon them: each as wide as it is turned, out to everywhere across the road it has been in the last
    // few seconds, and to where it will be by the time the driver is up to it. A car weaving across the road, or one the driver's line
    // bends into, is in the way before it is in front.
    private static Seen[] Reckon(Passing state, ReadOnlySpan<Seen> cars, double speedMs, double freeMs, double crossM, double dt)
    {
        foreach (Wander w in state.Tracks.Values) w.Seen = false;
        Seen[] reckoned = new Seen[cars.Length];
        for (int i = 0; i < cars.Length; i++)
        {
            Seen car = cars[i];
            double theirs = crossM + car.SideM, road = car.RoadM ?? theirs;
            // A car that moves across faster than any car does has been measured anew, as one round a bend found on another part of the route: it starts again from there.
            if (!state.Tracks.TryGetValue(car.Who, out Wander? w) || (dt > 0.0 && Math.Max(Math.Abs(theirs - w.At), Math.Abs(road - w.Road)) > (WanderMostMs * dt) + 0.5))
            {
                state.Tracks[car.Who] = w = new Wander { At = theirs, Road = road, Lo = road, Hi = road };
            }
            else if (dt > 0.0)
            {
                double lag = Math.Min(dt / WanderLagSeconds, 1.0);
                w.RateMs += (((theirs - w.At) / dt) - w.RateMs) * lag;
                w.RoadMs += (((road - w.Road) / dt) - w.RoadMs) * lag;
                // Where it has been is kept across the road, not across the route, which a racing line runs across under a car keeping to its lane.
                w.Lo = Math.Min(road, w.Lo + (WanderFadeMs * dt));
                w.Hi = Math.Max(road, w.Hi - (WanderFadeMs * dt));
                (w.At, w.Road) = (theirs, road);
            }
            w.Seen = true;
            // Up to it as fast as it is closing or as a pass would close, whichever is sooner.
            double closing = Math.Max(Math.Max(speedMs - car.SpeedMs, Math.Min(freeMs - car.SpeedMs, CrawlPassMs)), 1.0);
            double soon = car.AheadM > 0.0 ? Math.Min(car.AheadM / closing, WanderAheadSeconds) : 0.0;
            // Where it will be against the route: a car keeping to the driver's line keeps to it, and any other keeps to its lane, or goes on
            // moving across the road, while the route runs across the road under it where it goes on to.
            double going = Math.Abs(w.RateMs) < Math.Min(OnLineMs, Math.Abs(w.RoadMs)) ? theirs + (w.RateMs * soon)
                         : theirs + ((w.RoadMs + (car.DriftPerM * Math.Max(car.SpeedMs, 0.0))) * soon);
            reckoned[i] = car with
            {
                LeftM = Math.Max(Math.Max(w.Hi - road, going - theirs), 0.0) + car.WideM,
                RightM = Math.Max(Math.Max(road - w.Lo, theirs - going), 0.0) + car.WideM,
            };
        }
        foreach (object gone in state.Tracks.Where(t => !t.Value.Seen).Select(t => t.Key).ToArray()) state.Tracks.Remove(gone);
        return reckoned;
    }

    /// <summary>How far apart, middle to middle, a driver means to go by a car, and how far over <see cref="AbreastM"/> it is before the car no longer holds it back.</summary>
    public const double PassSideM = 3.0, PassMarginM = 0.3;

    /// <summary>How much quicker than a car a driver must be free to go to start round it, m/s, and to carry on once out.</summary>
    public const double PassFasterMs = 4.0, PassKeepMs = 1.5;

    /// <summary>How far past a car a driver is before it comes back to its line, and how far behind one before it goes back in after giving up.</summary>
    public const double PassClearM = 9.0, TuckM = 10.0;

    /// <summary>
    /// A car going at less than this share of what the road allows a driver is crawling: it is gone round
    /// wherever it is, as a standing one is, at no more than <see cref="CrawlPassMs"/> over its speed until
    /// clear of it. A pass of a moving car otherwise waits for a straight as long as the pass takes at the
    /// driver's own speed, which a racing line hardly has, and a field followed a car driven slowly for good.
    /// </summary>
    public const double CrawlShare = 0.6, CrawlPassMs = 12.0;

    private static bool Crawling(double leadMs, double freeMs) => leadMs >= StandingMs && leadMs < CrawlShare * freeMs;

    /// <summary>
    /// Less than this far behind a moving car's middle, and this far to the side of it it is going round,
    /// a driver's nose is alongside it: it holds its place and is no longer held back by that car.
    /// </summary>
    public const double AlongsideHoldM = 4.0, HoldSideM = 1.0;

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

    /// <summary>Half a car's length and half its width, m, for how far across the road one turned across it reaches.</summary>
    public const double CarHalfLengthM = 2.4, CarHalfWidthM = 0.95;

    /// <summary>
    /// Another car as the driver of a car at <paramref name="place"/> facing <paramref name="facing"/>
    /// sees it, or nothing past <paramref name="lookM"/>. Ahead, it is measured along the driver's
    /// route where it has one, by which a car round a bend is in the way; behind, in a straight line,
    /// a route being searched forwards only. Its speed is its velocity along the road where it is,
    /// so a car reversing towards the driver is coming at it whichever way it faces; and turned
    /// across the road it is wider by <see cref="Seen.WideM"/>.
    /// </summary>
    /// <param name="speedMs">The driver's own speed, for how far on the car will have gone by the time the driver is up to it.</param>
    public static Seen? See(object who, Autopilot? pilot, double3 place, double3 facing, double3 theirPlace, double3 theirFacing, double3 theirVelocity, double lookM,
                            double speedMs = 0.0)
    {
        double3 up = Vec.Unit(place), ahead = Vec.Unit(Vec.RejectFrom(facing, up)), left = Vec.Cross(up, ahead);
        double3 to = Vec.RejectFrom(theirPlace - place, up);
        if (Vec.Len(to) > lookM) return null;
        double along = Vec.Dot(to, ahead), side = Vec.Dot(to, left);
        // Its speed and its turn against the road where it is, not against the driver: round a bend the two face apart.
        double3 road = ahead;
        double? roadM = null;
        double drift = 0.0;
        double theirMs = Math.Max(Vec.Dot(theirVelocity, ahead), 0.0);
        double goes = theirMs * Math.Min(Math.Max(along, 0.0) / Math.Max(speedMs - theirMs, 1.0), WanderAheadSeconds);
        if (along > 0.0 && pilot is not null && pilot.Along(theirPlace, lookM, goes, out double on, out double aside, out double3 tangent, out double middle, out double perM))
        {
            (along, side, roadM, drift) = (on, aside, middle, perM);
            if (Vec.Len(Vec.RejectFrom(tangent, up)) > 0.1) road = Vec.Unit(Vec.RejectFrom(tangent, up));
        }
        double3 theirs = Vec.Unit(Vec.RejectFrom(theirFacing, up));
        double across = (CarHalfLengthM * Math.Abs(Vec.Dot(theirs, Vec.Cross(up, road)))) + (CarHalfWidthM * Math.Abs(Vec.Dot(theirs, road)));
        return new Seen(who, along, side, Vec.Dot(theirVelocity, road)) { WideM = Math.Max(across - CarHalfWidthM, 0.0), RoadM = roadM, DriftPerM = drift };
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
    /// <param name="road">What <see cref="KSACars.Lanes"/> needs of the road beyond this, or null to take it from the rest.</param>
    public static (double SideM, double CapMs) Pass(Passing state, ReadOnlySpan<Seen> cars, double speedMs, double freeMs, double crossM,
                                                    double leftM, double rightM, double straightM, double dt, Func<double, double>? brakeAt = null,
                                                    Lanes.Road? road = null)
    {
        Seen[] reckoned = Reckon(state, cars, speedMs, freeMs, crossM, dt);
        double step = Math.Max(dt, 0.0);
        state.LanesFor = Lanes.Near(reckoned, speedMs, freeMs, brakeAt) ? LanesHoldSeconds : Math.Max(state.LanesFor - step, 0.0);
        if (state.LanesFor > 0.0)
        {
            // The rules' pass is let go: back with them, a driver off its line beside a car takes up the side it is on.
            state.End();
            state.Rest = Math.Max(state.Rest - step, 0.0);
            state.Launch = Math.Max(state.Launch - step, 0.0);
            Lanes.Road asked = road ?? new Lanes.Road(crossM, 0.0, leftM, rightM, null, 0.0);
            (double lane, double most) = KSACars.Lanes.Choose(state.Lanes, reckoned, state.Tracks, speedMs, freeMs, crossM, asked, straightM, state.Launch > 0.0, dt, brakeAt);
            return (KeepLane(reckoned, crossM, lane), most);
        }
        state.Lanes.Forget();

        // Between opponents alone, the rules: a player's car further off than the lanes look is nothing to them.
        Seen[] others = reckoned.Any(c => c.Player) ? [.. reckoned.Where(c => !c.Player)] : reckoned;
        (double side, double cap) = Choose(state, others, speedMs, freeMs, crossM, leftM, rightM, straightM, dt, brakeAt);
        return (KeepLane(reckoned, crossM, side), cap);
    }

    /// <summary>How long the lanes go on deciding after the last look a player's car was near, s, and how near a car ahead always is, m.</summary>
    public const double LanesHoldSeconds = 1.0, PlanNearM = 40.0;

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
            double theirs = car.SideM > 0.0 ? Edge(car, crossM, -1) : Edge(car, crossM, 1);
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
        double lead = Math.Max(car.SpeedMs, 0.0);
        bool standing = lead < StandingMs;
        bool crawling = Crawling(lead, freeMs) && freeMs * (car.AheadM + PassClearM) / Closing(speedMs, freeMs, lead) > straightM;
        double side = Math.Clamp(Outside(cars, car, crossM, state.Side, standing ? StandingPassM : PassSideM), -rightM, leftM);
        double others = Held(cars, state.Who, crossM, side, speedMs, brakeAt, car);

        // Its nose alongside a moving car, a driver has the place: it keeps its line and goes on, and a car moved across into it touches it.
        if (!standing && car.AheadM < AlongsideHoldM && car.AheadM > -PassClearM && state.Side * -car.SideM >= HoldSideM)
        {
            (state.Unfit, state.Back) = (0.0, false);
            return (side, others);
        }
        bool fits = ClearOf(cars, car, crossM, side);
        bool open = freeMs - lead >= PassKeepMs && others >= lead + PassKeepMs
                    && (standing || crawling || freeMs * (car.AheadM + PassClearM) / Closing(speedMs, freeMs, lead) <= straightM);
        // Given up only when it has not fitted for a moment: the room ahead flickers as a racing line runs out to an edge and back.
        state.Unfit = car.AheadM > 0.0 && !(fits && (open || car.AheadM <= LevelM)) ? state.Unfit + Math.Max(dt, 0.0) : 0.0;
        if (state.Unfit > UnfitSeconds) state.Back = true;
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
            // So round every car standing beside it, the nearest of them not yet cleared setting the pace. A car coming backwards is nearer by what it covers.
            double nearest = double.PositiveInfinity, across = 0.0, clear = double.PositiveInfinity;
            foreach (Seen beside in cars)
            {
                if (!Equals(beside.Who, car.Who) && !Together(beside, car)) continue;
                double off = Apart(beside, crossM, crossM);
                if (beside.AheadM > -PassClearM) clear = Math.Min(clear, Math.Max(off, Apart(beside, crossM, side)) - AbreastM);
                if (beside.AheadM <= 0.0 || off > AbreastM + PassMarginM) continue;
                double near = beside.AheadM - (Math.Max(-beside.SpeedMs, 0.0) * ComingSeconds);
                if (near < nearest) (nearest, across) = (near, AbreastM + PassMarginM - off);
            }
            // As fast beside it as it is far to the side of it, and braked down to that, or to a creep where there is still a way across to go, in time.
            double by = Math.Min(CreepMs + (PassByMsPerM * Math.Max(double.IsFinite(clear) ? clear : 0.0, 0.0)), Math.Max(freeMs, CreepMs));
            double brake = brakeAt is null ? FollowBrakeMs2 : Math.Max(FollowBrakeShare * brakeAt(0.0), FollowBrakeMs2);
            double creep;
            if (double.IsPositiveInfinity(nearest)) creep = by;
            else
            {
                double room = nearest - 5.0 - (AcrossRunM * across);
                creep = Math.Max(Math.Clamp((nearest - 5.0) * 0.45, 1.2, CreepMs), room > 0.0 ? Math.Sqrt((CreepMs * CreepMs) + (2.0 * brake * room)) : 0.0);
            }
            if (car.AheadM > 0.0)
            {
                double ahead = car.AheadM - 5.0 - (Math.Max(-car.SpeedMs, 0.0) * ComingSeconds);
                creep = Math.Min(creep, Math.Sqrt((by * by) + (2.0 * brake * Math.Max(ahead, 0.0))));
            }
            return (side, Math.Min(others, creep));
        }
        // Not held either while it will be out from behind the car before it is up to it, which is reckoned on the car
        // keeping its speed: one braking hard ahead of a driver still behind it was run into at 18 m/s.
        double slowing = dt > 0.0 && double.IsFinite(state.LeadMs) ? (state.LeadMs - lead) / dt : 0.0;
        state.LeadMs = lead;
        double outOf = Nearest(cars, car, crossM);
        double moving = PassLagSeconds + ((AbreastM + PassMarginM - outOf) / Math.Max(Math.Min(Autopilot.SideRateMs, Autopilot.SideSlope * speedMs), 0.1));
        bool apart = outOf > AbreastM + PassMarginM
                     || (slowing < LeadBrakingMs2 && car.AheadM - TuckM > PassLagShare * (speedMs - lead) * moving);
        double held = apart ? double.PositiveInfinity : FollowCap(car.AheadM, 0.0, lead, speedMs, brakeAt);
        // Round a crawling car wherever it is, bends and all, no faster than a little over its speed until clear of it.
        if (crawling && car.AheadM > -PassClearM && outOf <= AbreastM + PassMarginM) held = Math.Min(held, lead + CrawlPassMs);
        return (side, Math.Min(others, held));
    }

    /// <summary>
    /// How far along the road from a car another is one obstacle with it, gone round as one, m, and how
    /// much faster or slower than it the other may be, m/s: two cars side by side.
    /// </summary>
    public const double TogetherM = 8.0, TogetherMs = 2.0;

    // Whether a car is another beside the car being gone round, both standing or going at nearly its speed: two cars stopped side by side,
    // one going round the nearer, had the other where it would go and in front of where it was, and never moved; and two driven abreast were never passed.
    private static bool Together(Seen car, Seen with) =>
        !Equals(car.Who, with.Who) && Math.Abs(car.AheadM - with.AheadM) <= TogetherM
        && ((with.SpeedMs < StandingMs && car.SpeedMs < StandingMs) || Math.Abs(car.SpeedMs - with.SpeedMs) <= TogetherMs);

    // How far to the side of the nearest of a car and the cars beside it the driver is.
    private static double Nearest(ReadOnlySpan<Seen> cars, Seen car, double crossM)
    {
        double off = Apart(car, crossM, crossM);
        foreach (Seen beside in cars)
        {
            if (Together(beside, car) && beside.AheadM > -PassClearM) off = Math.Min(off, Apart(beside, crossM, crossM));
        }
        return off;
    }

    // Where to the left of the route a driver runs to go round a car on one side, so far from it: far enough out to clear it and every car beside it on that side.
    private static double Outside(ReadOnlySpan<Seen> cars, Seen round, double crossM, int way, double byM)
    {
        double theirs = crossM + round.SideM, side = Edge(round, crossM, way) + (way * byM);
        foreach (Seen car in cars)
        {
            if (!Together(car, round)) continue;
            double at = Edge(car, crossM, way);
            if (way * (crossM + car.SideM - theirs) > -AbreastM) side = way > 0 ? Math.Max(side, at + byM) : Math.Min(side, at - byM);
        }
        return side;
    }

    // Whether a line so far to the left of the route clears a car and every car beside it.
    private static bool ClearOf(ReadOnlySpan<Seen> cars, Seen standing, double crossM, double sideM)
    {
        if (Apart(standing, crossM, sideM) < AbreastM + PassMarginM) return false;
        foreach (Seen car in cars)
        {
            if (Together(car, standing) && Apart(car, crossM, sideM) < AbreastM + PassMarginM) return false;
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
            double beside = Apart(car, crossM, crossM, sideM);
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
            if (Apart(car, crossM, crossM) <= AbreastM || Apart(car, crossM, 0.0) > AbreastM + PassMarginM) continue;
            state.Begin(car.Who, car.SideM < 0.0 ? 1 : -1);
            return;
        }
        if (state.Rest > 0.0) return;

        // The nearest car on the driver's line: one beyond it is that car's to pass first.
        Seen? nearest = null;
        foreach (Seen car in cars)
        {
            if (car.AheadM <= 0.0 || Apart(car, crossM, crossM, 0.0) > AbreastM) continue;
            if (nearest is not { } n || car.AheadM < n.AheadM) nearest = car;
        }
        // A car waiting behind another is followed: it goes when that one does, or the one it waits for is gone round first.
        if (nearest is not { } slow || (slow.Queued && slow.SpeedMs < StandingMs)) return;

        double lead = Math.Max(slow.SpeedMs, 0.0);
        if (freeMs - lead < PassFasterMs) return;
        bool holds = FollowCap(slow.AheadM, 0.0, lead, speedMs, brakeAt) < freeMs;
        if (!holds && slow.AheadM - GapM > (speedMs - lead) * PassSoonSeconds) return;
        bool standing = lead < StandingMs;
        if (!standing && !Crawling(lead, freeMs) && freeMs * (PullOutSeconds + ((slow.AheadM + PassClearM) / Closing(speedMs, freeMs, lead))) > straightM) return;

        double most = -1.0;
        int best = 0;
        foreach (int way in (ReadOnlySpan<int>)[1, -1])
        {
            // Already to one side of it, that is the side, unless it is standing and far enough ahead to cross behind it.
            if (Apart(slow, crossM, crossM) > AbreastM && way != (slow.SideM < 0.0 ? 1 : -1)
                && !(standing && slow.AheadM > 2.0 * StandingSideM / Autopilot.SideSlopeSlow)) continue;
            double side = Outside(cars, slow, crossM, way, standing ? StandingPassM : PassSideM);
            double spare = way > 0 ? leftM - side : side + rightM;
            // Round a standing or crawling car as far to the side as the road has, if that is far enough to be clear of it.
            if ((standing || Crawling(lead, freeMs)) && spare < 0.0 && ClearOf(cars, slow, crossM, Math.Clamp(side, -rightM, leftM)))
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
            if (Apart(car, crossM, sideM) <= AbreastM + PassMarginM) return true;
        }
        return false;
    }
}
