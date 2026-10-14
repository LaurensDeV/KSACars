namespace KSACars;

/// <summary>
/// Where across the road a driver runs and the most it goes at while a player's car is near: a
/// handful of lanes across the road, each tried at a few speeds and rolled a few seconds ahead against
/// every car as <see cref="Rival"/> reckons it will go on, and the cheapest taken in time lost,
/// touching and moving across. Going round is steering, and a speed under what the road allows is
/// what is left where no lane is clear; behind a player's car that holds it up even that runs out,
/// and the driver pushes by at a little over its speed.
/// </summary>
internal static class Lanes
{
    /// <summary>What a driver remembers from one look to the next.</summary>
    public sealed class Memory
    {
        internal double Side, Cap = double.PositiveInfinity;
        internal bool Has;
        internal readonly object?[] Kept = new object?[MostCars];

        /// <summary>The player's car being overtaken, or none.</summary>
        public object? Who { get; internal set; }

        /// <summary>Seconds until another overtake may be begun, after one given up.</summary>
        public double Cooldown { get; internal set; }

        /// <summary>Seconds lately the driver has been held under what the road allows by a player's car slower than it.</summary>
        public double Held { get; internal set; }

        /// <summary>Whether it has been held long enough to push by.</summary>
        public bool Pushing => Held >= PushAfterSeconds;

        internal double Unfit;
        internal int PushSide;
        internal readonly int[] Edged = new int[EdgeCars];
        internal readonly Dictionary<object, double> LastMs = [];
        internal readonly double[] Limits = new double[LimitCount], Right = new double[LimitCount], Left = new double[LimitCount];

        internal void Forget() => (Has, Side, Cap, Who, Unfit, Held) = (false, 0.0, double.PositiveInfinity, null, 0.0, 0.0);
    }

    /// <summary>
    /// What a driver knows of the road for this: where it aims now, how far its middle is ahead of where
    /// the cars are measured from, the room either side over the stretch a pass takes, and the speed the
    /// route allows every <see cref="LimitStepM"/> on, with the tightest bend over that.
    /// </summary>
    public readonly record struct Road(double SideNowM, double MiddleM, double LeftM, double RightM, double[]? Limits, double BendPerM)
    {
        /// <summary>How far to the right and the left of the route the car's middle may be every <see cref="LimitStepM"/> on, m, where that is known.</summary>
        public double[]? Right { get; init; }

        public double[]? Left { get; init; }
    }

    /// <summary>
    /// The road as a driver's lanes ask it: its room either side only as far as the nearest car in the
    /// way is come up on and passed, since asked of the whole pass, out to where the racing line meets
    /// an edge past the car, a lane chosen in it was squeezed into the car.
    /// </summary>
    /// <param name="middleM">How far the driver's middle is ahead of where its route places it.</param>
    public static Road RoadOf(Autopilot pilot, Memory m, ReadOnlySpan<Rival.Seen> cars, double speedMs, double middleM)
    {
        double over = Rival.PassLookM, v = Math.Max(speedMs, 0.0);
        foreach (Rival.Seen c in cars)
        {
            if (c.AheadM <= 0.0 || Math.Abs(c.SideM) > Rival.StandingSideM + c.WideM) continue;
            double va = Math.Max(c.SpeedMs, 0.0);
            over = Math.Min(over, c.AheadM + (va * Math.Min(c.AheadM / Math.Max(v - va, 1.0), 2.0 * HorizonS)) + Rival.StandingPastM);
        }
        pilot.RoadAhead(over, 1.0, out double left, out double right, out _);
        bool known = pilot.Ahead(m.Limits, m.Right, m.Left, LimitStepM, out double bend);
        return new Road(pilot.SideNowM, middleM, left, right, known ? m.Limits : null, bend) { Right = known ? m.Right : null, Left = known ? m.Left : null };
    }

    /// <summary>How far apart the road's speeds are read ahead, m, and how many.</summary>
    public const double LimitStepM = 2.0;

    public const int LimitCount = 160;

    /// <summary>How far ahead a choice is rolled out, s, and the step it is rolled in.</summary>
    public const double HorizonS = 3.0, StepS = 0.2;

    /// <summary>The most even lanes tried across the road, the most cars rolled against, and how many of the nearest have a lane just clear of either side.</summary>
    public const int MostSides = 11, MostCars = 16, EdgeCars = 3;

    /// <summary>How fast the driver is taken to gain speed, m/s2, and how far its car lags the line it aims at, s.</summary>
    public const double PullMs2 = 6.0, LagS = 0.6;

    /// <summary>Room kept to a car's side and, at the least, behind it, m, the seconds of closing speed added behind, and the near miss under which a touch is close.</summary>
    public const double SideMarginM = 1.0, AlongMarginM = 2.0, AlongMarginS = 0.3, PadM = 0.3;

    /// <summary>Half a car's length and width, m.</summary>
    public const double HalfLongM = 2.3, HalfWideM = 0.9;

    /// <summary>
    /// How much slower than the road allows, m/s, a player's car is, now or by the next bend it brakes for,
    /// for an overtake of it; how long the plan may hold the driver behind it before the overtake is given
    /// up, s; and how long before the next.
    /// </summary>
    public const double OvertakeMs = 3.0, AbortSeconds = 0.6, CooldownSeconds = 3.0;

    /// <summary>
    /// How long a player's car slower than <see cref="PushShare"/> of what the road allows may hold the
    /// driver under it before the driver pushes by, s, and how much faster than it it then goes, m/s. KSA's
    /// contact between two cars is plastic: each changes by half the closing speed.
    /// </summary>
    public const double PushAfterSeconds = 0.6, PushShare = 0.9, PushMs = 4.0;

    /// <summary>How far across a car's tail a driver pushing by it aims, as a share of the two half widths: a glancing push turns it aside.</summary>
    public const double PushAcross = 0.6;

    /// <summary>Coming at the driver faster than this, m/s, a car is going the other way: gone by, and never pushed.</summary>
    public const double ComingMs = 0.5, ComingMarginShare = 0.5;

    // Time held is forgotten at this share of the rate it is gained, so a car weaving in and out of the driver's lane still adds up.
    private const double HeldFade = 0.5;

    /// <summary>The room an overtaking driver keeps behind a player's car, m and seconds of its own speed.</summary>
    public const double CloseGapM = 2.5, CloseGapSeconds = 0.2;

    // Weights, in seconds of time lost: a touch, a near thing a second, a metre of moving across, a metre off the line a second, a change of mind.
    private const double TouchCost = 50.0, PadCost = 3.0, NearCost = 4.0, MoveCost = 0.04, OffLineCost = 0.03, KeepBetter = 0.1, DitherCost = 0.1;

    // A touch of a player's car being overtaken and of one being pushed by: a little, and more for every m/s it is hit at.
    private const double LightTouch = 6.0, LightPerMs = 2.0, PushTouch = 0.5, PushPerMs = 1.0;

    // Seconds of time a metre outside the road's room is worth for every second of it, and how far ahead of the car, in seconds, the driver aims.
    private const double OffCost = 10.0, AimAheadS = 0.6;

    // How far two cars overlap along the road before a touch is beside one and not into its tail, m.
    private const double RubOverlapM = 1.5;

    // A car still in the same lane past the horizon holds the driver to its speed for this many seconds more.
    private const double FollowOnS = 4.0;

    /// <summary>A car that has swung across more than this, m, is taken to be anywhere in the band it swung over once its motion now is no guide, after <see cref="WeaveForS"/>.</summary>
    public const double WeaveM = 1.0, WeaveForS = 0.5, CarryS = 0.8;

    /// <summary>
    /// Whether a player's car is near enough to be the lanes' to decide: ahead within what the driver
    /// brakes in from its speed and the horizon's run, or alongside.
    /// </summary>
    public static bool Near(ReadOnlySpan<Rival.Seen> cars, double speedMs, double freeMs, Func<double, double>? brakeAt)
    {
        double v = Math.Max(speedMs, 0.0), brake = Brake(brakeAt, v);
        foreach (Rival.Seen c in cars)
        {
            if (!c.Player || c.AheadM < -Rival.AlongsideM || Math.Abs(c.SideM) > Rival.StandingSideM + Rival.AbreastM + c.WideM) continue;
            double lead = Math.Max(c.SpeedMs, 0.0);
            double reach = Math.Max(Rival.PlanNearM, (v * HorizonS) + (Math.Max((v * v) - (lead * lead), 0.0) / (2.0 * brake)) + Rival.GapM);
            if (c.AheadM <= reach && (lead < Math.Max(v, freeMs) || c.AheadM < Rival.AlongsideM)) return true;
        }
        return false;
    }

    private static double Brake(Func<double, double>? brakeAt, double v) =>
        brakeAt is null ? Rival.FollowBrakeMs2 : Math.Max(Rival.FollowBrakeShare * brakeAt(v), Rival.FollowBrakeMs2);

    /// <param name="cars">The cars as <see cref="Rival.Pass"/> reckons them, their spans filled in.</param>
    /// <param name="tracks">Where each has been across the road and how fast it moves across.</param>
    /// <param name="straightM">How far on the route first bends, for whether another opponent may be gone round.</param>
    /// <param name="launch">Whether the field is still setting off, when nothing is gone round or pushed.</param>
    public static (double SideM, double CapMs) Choose(Memory m, ReadOnlySpan<Rival.Seen> cars, IReadOnlyDictionary<object, Rival.Wander> tracks, double speedMs,
                                                      double freeMs, double crossM, Road road, double straightM, bool launch, double dt,
                                                      Func<double, double>? brakeAt)
    {
        double step = Math.Max(dt, 0.0);
        m.Cooldown = Math.Max(m.Cooldown - step, 0.0);
        double v0 = Math.Max(speedMs, 0.0), brake = Brake(brakeAt, v0), free0 = Math.Max(freeMs, 0.0);
        double[]? limits = road.Limits;
        double bend = road.BendPerM;
        double freeBase = free0 / SideFactor(road.SideNowM, bend);

        Span<double> oA = stackalloc double[MostCars], oY = stackalloc double[MostCars], oVa = stackalloc double[MostCars], oVy = stackalloc double[MostCars];
        Span<double> oHa = stackalloc double[MostCars], oLo = stackalloc double[MostCars], oHi = stackalloc double[MostCars], oTouch = stackalloc double[MostCars];
        Span<double> oPer = stackalloc double[MostCars], oNear = stackalloc double[MostCars];
        Span<bool> oPlayer = stackalloc bool[MostCars], oBraking = stackalloc bool[MostCars], oFrozen = stackalloc bool[MostCars];
        int n = 0;

        // The player's car to overtake: the nearest ahead in or near the driver's path that is, or soon will be, slower than the road allows it.
        if (m.Who is not null && (Find(cars, m.Who) is not { } still || still.AheadM < -Rival.PassClearM || launch)) (m.Who, m.Unfit) = (null, 0.0);
        if (m.Who is null && m.Cooldown <= 0.0 && !launch)
        {
            Rival.Seen? pick = null;
            foreach (Rival.Seen c in cars)
            {
                if (!c.Player || c.AheadM <= 0.0 || Math.Abs(c.SideM) > Rival.StandingSideM + Rival.AbreastM + c.WideM) continue;
                if (Soon(c, limits, road.MiddleM, free0) > free0 - OvertakeMs) continue;
                if (pick is not { } p || c.AheadM < p.AheadM) pick = c;
            }
            if (pick is { } got && got.AheadM < Math.Max(Rival.PlanNearM, (v0 * HorizonS) + 30.0)) (m.Who, m.Unfit) = (got.Who, 0.0);
        }

        // Cars gone from sight are forgotten in a lump: there are never more than a field's worth.
        if (m.LastMs.Count > 2 * MostCars) m.LastMs.Clear();
        foreach (Rival.Seen c in cars)
        {
            if (n == MostCars) break;
            double a0 = c.AheadM - road.MiddleM, aH = a0 + (c.SpeedMs * HorizonS), reach = (v0 * HorizonS) + (0.5 * PullMs2 * HorizonS * HorizonS), near = (4.0 * HalfLongM) + AlongMarginM;
            bool braking = m.LastMs.TryGetValue(c.Who, out double was) && step > 0.0 && (was - c.SpeedMs) / step > Rival.LeadBrakingMs2;
            m.LastMs[c.Who] = c.SpeedMs;
            // Never near over the horizon however the driver goes: ahead and staying further ahead than it can reach, or behind and staying behind.
            if ((a0 > near && aH - reach > near) || (a0 < -near && aH < -near)) continue;
            double y = crossM + c.SideM;
            Rival.Wander? w = tracks.TryGetValue(c.Who, out Rival.Wander? t) ? t : null;
            m.Kept[n] = c.Who;
            (oA[n], oY[n], oVa[n], oHa[n], oPlayer[n], oBraking[n]) = (a0, y, c.SpeedMs, HalfWideM + c.WideM, c.Player, braking);
            oVy[n] = w?.RateMs ?? 0.0;
            (oLo[n], oHi[n]) = (y - Math.Max(c.RightM - c.WideM, 0.0), y + Math.Max(c.LeftM - c.WideM, 0.0));
            bool mine = c.Player && Equals(c.Who, m.Who);
            (oTouch[n], oPer[n], oNear[n]) = !c.Player || launch ? (TouchCost, TouchCost / 10.0, 1.0)
                                           : m.Pushing ? (PushTouch, PushPerMs, 0.0)
                                           : mine ? (LightTouch, LightPerMs, 0.5)
                                           : (TouchCost, TouchCost / 10.0, 1.0);
            // Its nose beside a player's car it is overtaking, the driver has the place: the car moving across is the one that touches.
            oFrozen[n] = mine && a0 < Rival.AlongsideHoldM && a0 > -Rival.PassClearM && Math.Abs(y - crossM) >= HalfWideM + oHa[n] - 0.2;
            // Another opponent that may not be gone round here is the whole road: it is followed.
            if (!c.Player && a0 > 2.0 * HalfLongM && (launch || !Passable(c, v0, free0, straightM))) oHa[n] = 100.0;
            if (c.Player && launch && a0 > 2.0 * HalfLongM) oHa[n] = 100.0;
            n++;
        }
        if (n == 0)
        {
            m.Forget();
            m.Has = true;
            return (0.0, double.PositiveInfinity);
        }

        // Lanes are offsets from the route, which runs across the road: the widest room over the stretch the horizon covers, each held to the road's room where it is.
        double lo = -Math.Max(road.RightM, 0.0), hi = Math.Max(road.LeftM, 0.0);
        if (road.Right is { } rightAt && road.Left is { } leftAt)
        {
            int over = Math.Clamp((int)(((v0 * HorizonS) + road.MiddleM + (4.0 * HalfLongM)) / LimitStepM), 1, rightAt.Length);
            (lo, hi) = (0.0, 0.0);
            for (int i = 0; i < over; i++) (lo, hi) = (Math.Min(lo, -rightAt[i]), Math.Max(hi, leftAt[i]));
        }
        Span<double> sides = stackalloc double[MostSides + 3 + (2 * MostCars)];
        int ns = 0;
        int even = Math.Clamp((int)Math.Ceiling((hi - lo) / 0.5), 1, MostSides - 1);
        for (int i = 0; i <= even; i++) sides[ns++] = lo + ((hi - lo) * i / even);
        sides[ns++] = Math.Clamp(0.0, lo, hi);
        sides[ns++] = Math.Clamp(road.SideNowM, lo, hi);
        if (m.Has) sides[ns++] = Math.Clamp(m.Side, lo, hi);
        // Just clear of each of the nearest cars either side, which threads a gap the even lanes step over.
        for (int pick = 0; pick < EdgeCars; pick++)
        {
            int k = -1;
            for (int j = 0; j < n; j++)
            {
                if (oHa[j] > 50.0 || oA[j] < -2.0 * HalfLongM || Array.IndexOf(m.Edged, j, 0, pick) >= 0) continue;
                if (k < 0 || Math.Abs(oA[j]) < Math.Abs(oA[k])) k = j;
            }
            if (k < 0) break;
            m.Edged[pick] = k;
            double by = HalfWideM + oHa[k] + PadM + 0.05;
            sides[ns++] = Math.Clamp(oHi[k] + by, lo, hi);
            sides[ns++] = Math.Clamp(oLo[k] - by, lo, hi);
        }
        ns = Distinct(sides[..ns], 0.15);

        int nearest = -1, next = -1;
        for (int k = 0; k < n; k++)
        {
            if (oA[k] <= 0.0) continue;
            if (nearest < 0 || oA[k] < oA[nearest]) (next, nearest) = (nearest, k);
            else if (next < 0 || oA[k] < oA[next]) next = k;
        }

        // Speeds: free, the two nearest cars' own and following each, a push past a player's, the speed now, a creep and a stop.
        Span<double> caps = stackalloc double[12];
        int nc = 0;
        caps[nc++] = double.PositiveInfinity;
        bool anyOpponent = false;
        for (int k = 0; k < n; k++) anyOpponent |= !oPlayer[k] && Math.Abs(oA[k]) < 40.0;
        foreach (int k in (ReadOnlySpan<int>)[nearest, next])
        {
            if (k < 0) continue;
            double va = Math.Max(oVa[k], 0.0);
            if (va < freeBase) caps[nc++] = va;
            double f = Follow(m, k, oA[k] + road.MiddleM, oVa[k], v0, oPlayer[k], brakeAt);
            if (double.IsFinite(f)) caps[nc++] = f;
            if (oPlayer[k] && !launch) caps[nc++] = Push(oA[k], va, brake);
        }
        caps[nc++] = v0;
        caps[nc++] = Rival.CreepMs;
        if (!m.Pushing || anyOpponent || launch)
        {
            caps[nc++] = 4.0;
            caps[nc++] = 0.0;
        }
        nc = Distinct(caps[..nc], 0.5);

        // How far the driver would go free and on its line, which time lost is measured against.
        Roll(oA, oY, oVa, oVy, oHa, oLo, oHi, oTouch, oPer, oNear, oFrozen, 0, v0, freeBase, limits, road.Right, road.Left, road.MiddleM, bend, double.PositiveInfinity, brake,
                            crossM, road.SideNowM, road.SideNowM, lo, hi, double.PositiveInfinity, out double eFree, out double vFree);
        double best = double.PositiveInfinity, bestSide = 0.0, bestCap = double.PositiveInfinity, keep = double.PositiveInfinity;
        for (int si = 0; si < ns; si++)
        {
            double side = sides[si];
            double lateral = (MoveCost * Math.Abs(side - road.SideNowM)) + (OffLineCost * HorizonS * Math.Abs(side)) + (m.Has ? DitherCost * Math.Abs(side - m.Side) : 0.0);
            for (int ci = 0; ci < nc; ci++)
            {
                double cap = caps[ci];
                bool kept = m.Has && Math.Abs(side - m.Side) < 0.26 && SameCap(cap, m.Cap);
                double bound = kept ? double.PositiveInfinity : best - lateral;
                if (bound <= 0.0) continue;
                double cost = lateral + Roll(oA, oY, oVa, oVy, oHa, oLo, oHi, oTouch, oPer, oNear, oFrozen, n, v0, freeBase, limits, road.Right, road.Left, road.MiddleM, bend, cap, brake,
                                             crossM, road.SideNowM, side, lo, hi, bound, out double gone, out double vEnd) + Lost(eFree, vFree, gone, vEnd);
                if (cost < best) (best, bestSide, bestCap) = (cost, side, cap);
                if (kept) keep = Math.Min(keep, cost);
            }
        }
        // What was chosen last stays unless something is better by a margin.
        if (m.Has && keep <= best + KeepBetter) (bestSide, bestCap) = (Math.Clamp(m.Side, lo, hi), m.Cap);

        // Held back by a car braking hard in the driver's own lane, or by one it will not be out from behind in time: what the plan takes for
        // a pass may be a car that stops in front of it.
        double floor = double.PositiveInfinity, rate = SideRate(v0);
        for (int k = 0; k < n; k++)
        {
            double va = Math.Max(oVa[k], 0.0);
            double need = HalfWideM + Math.Min(oHa[k], 50.0) + 0.2, off = Math.Abs(oY[k] - crossM);
            if (oA[k] <= 0.0 || off >= need || bestCap <= va + 1.0) continue;
            bool pushed = oPlayer[k] && m.Pushing && !launch;
            if (!oBraking[k])
            {
                if (va < Rival.StandingMs) continue;
                double clearIn = Math.Abs(bestSide - oY[k]) >= need ? LagS + ((need - off) / rate) : double.PositiveInfinity;
                if ((oA[k] - (2.0 * HalfLongM)) / Math.Max(v0 - va, 0.1) > clearIn) continue;
            }
            double f = Follow(m, k, oA[k] + road.MiddleM, oVa[k], v0, oPlayer[k], brakeAt);
            floor = Math.Min(floor, pushed && !oBraking[k] ? Math.Max(f, Push(oA[k], va, brake)) : f);
        }
        bestCap = Math.Min(bestCap, floor);

        // How the plan holds the driver: behind a player's car slower than the road allows, it is timed towards pushing by;
        // behind the one it overtakes, not beside it, the overtake is given up after a moment.
        int holder = -1;
        for (int k = 0; k < n; k++)
        {
            if (oA[k] <= 0.0 || oHa[k] > 50.0 || (Math.Abs(bestSide - oY[k]) >= HalfWideM + oHa[k] + 0.2 && Math.Abs(oY[k] - crossM) >= HalfWideM + oHa[k])) continue;
            if (holder < 0 || oA[k] < oA[holder]) holder = k;
        }
        // Held is timed against whichever car is nearest ahead in any lane: one weaving the whole road's width is in the driver's lane half the time.
        int first = -1;
        for (int k = 0; k < n; k++)
        {
            if (oA[k] > 0.0 && oA[k] < Rival.PlanNearM && oHa[k] <= 50.0 && (first < 0 || oA[k] < oA[first])) first = k;
        }
        // A car coming the other way is past in a moment, and is gone by and never pushed.
        bool held = bestCap < PushShare * free0 && first >= 0 && oPlayer[first] && !launch && oVa[first] > -ComingMs && Math.Max(oVa[first], 0.0) < PushShare * free0;
        m.Held = held ? m.Held + step : Math.Max(m.Held - (HeldFade * step), 0.0);
        if (m.Who is not null && holder >= 0 && Equals(m.Kept[holder], m.Who) && bestCap <= Math.Max(oVa[holder], 0.0) + 1.0 && oA[holder] > Rival.AlongsideHoldM)
        {
            m.Unfit += step;
            if (m.Unfit > AbortSeconds) (m.Who, m.Unfit, m.Cooldown) = (null, 0.0, CooldownSeconds);
        }
        else m.Unfit = 0.0;

        // Held long enough by a player's car, the driver pushes by it: half across its tail on the side with more road, a little faster than it.
        if (m.Pushing && held && !anyOpponent)
        {
            double y = oY[first];
            if (m.PushSide == 0) m.PushSide = hi - y >= y - lo ? 1 : -1;
            // A car standing is not shoved from behind but gone by, clear of its side where the road has room and against its corner where not:
            // pushed square on, one stopped across the nose stayed there, and the driver backed away and drove into it again.
            bool standing = oVa[first] < Rival.StandingMs;
            double across = standing ? HalfWideM + oHa[first] + PadM : PushAcross * (HalfWideM + oHa[first]);
            double aim = y + (m.PushSide * across);
            bestSide = Math.Clamp(aim, lo, hi);
            // Clear of a standing car's side there is nothing to push, and the plan's own speed goes by it.
            if (!(standing && Math.Abs(bestSide - aim) < 0.05)) bestCap = oBraking[first] ? Math.Min(bestCap, floor) : Push(oA[first], Math.Max(oVa[first], 0.0), brake);
        }
        else m.PushSide = 0;

        (m.Has, m.Side, m.Cap) = (true, bestSide, bestCap);
        return (bestSide, bestCap);
    }

    // Up to a car being pushed by as fast as still meets it at only PushMs over its speed.
    private static double Push(double aheadM, double leadMs, double brake)
    {
        double at = leadMs + PushMs;
        return Math.Sqrt((at * at) + (2.0 * brake * Math.Max(aheadM - (2.0 * HalfLongM), 0.0)));
    }

    // Following a car, closer behind the player's car being overtaken.
    private static double Follow(Memory m, int k, double aheadM, double leadMs, double v0, bool player, Func<double, double>? brakeAt) =>
        player && m.Who is not null && Equals(m.Kept[k], m.Who)
            ? Rival.FollowCap(aheadM, 0.0, leadMs, v0, brakeAt, CloseGapM, CloseGapSeconds)
            : Rival.FollowCap(aheadM, 0.0, leadMs, v0, brakeAt);

    // How fast a car will be by the next bend it brakes for: no faster than the driver's own road allows a little on from it.
    private static double Soon(Rival.Seen c, double[]? limits, double middleM, double free0)
    {
        double ms = Math.Max(c.SpeedMs, 0.0);
        if (limits is null) return ms;
        for (double on = 0.0; on <= 2.0 * Math.Max(ms, 5.0); on += 2.0 * LimitStepM) ms = Math.Min(ms, Limit(limits, c.AheadM - middleM + on, free0));
        return ms;
    }

    // Whether another opponent may be gone round here, as the rules would: standing or crawling anywhere, or on a straight long enough
    // with speed to spare. One standing in a queue is waited behind.
    private static bool Passable(Rival.Seen c, double v0, double free0, double straightM)
    {
        double lead = Math.Max(c.SpeedMs, 0.0);
        if (c.Queued && lead < Rival.StandingMs) return false;
        if (lead < Rival.StandingMs || (lead >= Rival.StandingMs && lead < Rival.CrawlShare * free0)) return true;
        double closing = Math.Max((0.5 * (Math.Max(v0, lead) + free0)) - lead, 1.0);
        return free0 - lead >= Rival.PassFasterMs && free0 * (Rival.PullOutSeconds + ((c.AheadM + Rival.PassClearM) / closing)) <= straightM;
    }

    private static Rival.Seen? Find(ReadOnlySpan<Rival.Seen> cars, object? who)
    {
        foreach (Rival.Seen car in cars)
        {
            if (Equals(car.Who, who)) return car;
        }
        return null;
    }

    private static double Limit(double[]? limits, double onM, double free) =>
        limits is null || limits.Length == 0 ? free : limits[Math.Clamp((int)(Math.Max(onM, 0.0) / LimitStepM), 0, limits.Length - 1)];

    // Leaves one of any values nearer each other than so much, in the order first met; how many are left.
    private static int Distinct(Span<double> values, double within)
    {
        int kept = 0;
        for (int i = 0; i < values.Length; i++)
        {
            bool near = false;
            for (int j = 0; j < kept && !near; j++) near = values[j] == values[i] || Math.Abs(values[j] - values[i]) < within;
            if (!near) values[kept++] = values[i];
        }
        return kept;
    }

    private static bool SameCap(double a, double b) => (double.IsInfinity(a) && double.IsInfinity(b)) || Math.Abs(a - b) < 0.5;

    // The plan's speed is for the route's own bend: a line beside it is tighter by twice its share of the radius.
    private static double SideFactor(double side, double bend) => Math.Clamp(1.0 - (2.0 * Math.Abs(side) * bend), 0.5, 1.0);

    private static double SideRate(double v) =>
        Math.Max(Math.Min(Autopilot.SideRateMs, (v < Autopilot.SideSlowMs ? Autopilot.SideSlopeSlow : Autopilot.SideSlope) * v), 0.05);

    // Seconds lost against going free on the line: the distance short at the speed it would have had, and the time to get back up to that speed.
    private static double Lost(double eFree, double vFree, double gone, double vEnd)
    {
        double v = Math.Max(vFree, 1.0), slower = Math.Max(vFree - vEnd, 0.0);
        return (Math.Max(eFree - gone, 0.0) / v) + (slower * slower / (2.0 * PullMs2 * v));
    }

    // One side and one cap rolled out against every car: the risk of touching and being held, and how far the driver goes.
    private static double Roll(Span<double> oA, Span<double> oY, Span<double> oVa, Span<double> oVy, Span<double> oHa, Span<double> oLo, Span<double> oHi,
                               Span<double> oTouch, Span<double> oPer, Span<double> oNear, Span<bool> oFrozen, int n, double v0, double freeBase, double[]? limits,
                               double[]? rightAt, double[]? leftAt, double middleM, double bend, double cap, double brake, double cross, double aim0, double side,
                               double lo, double hi, double bound,
                               out double gone, out double vEnd)
    {
        double v = v0, e = 0.0, aim = aim0, c = cross, risk = 0.0;
        int steps = (int)(HorizonS / StepS), touched = 0;
        // Making back for the line once past is for a car got past in the look ahead: one already behind leaves every lane alike, and the lane
        // the driver was in kept it off its line into the next bend.
        bool passing = false;
        for (int k = 0; k < n && !passing; k++) passing = oA[k] >= -(2.0 * HalfLongM) - AlongMarginM;
        Span<double> oa = stackalloc double[MostCars], ov = stackalloc double[MostCars];
        for (int k = 0; k < n; k++) (oa[k], ov[k]) = (oA[k], oVa[k]);
        (gone, vEnd) = (0.0, v0);
        for (int i = 1; i <= steps; i++)
        {
            double t = i * StepS;
            double free = Limit(limits, e + middleM, freeBase) * SideFactor(aim, bend);
            double target = Math.Min(free, cap);
            v = v < target ? Math.Min(v + (PullMs2 * StepS), target) : Math.Max(v - (brake * StepS), target);
            double ePrev = e;
            e += v * StepS;
            double move = SideRate(v) * StepS;
            // Past every car that was ahead, the driver makes back for its line: held to a lane past the car, a lane round one met in a bend's
            // entry ran off where the line crossed to the apex, and only a crawl, covering little road in the look ahead, was ever clean.
            bool past = passing;
            for (int k = 0; k < n && past; k++) past = oA[k] <= 0.0 || oa[k] < e - (2.0 * HalfLongM) - AlongMarginM;
            aim += Math.Clamp((past ? 0.0 : side) - aim, -move, move);
            if (rightAt is not null && leftAt is not null)
            {
                // The driver holds its aim to the road's room a little ahead; the car, lagging it, can still be outside the room where it is.
                int ahead = Math.Clamp((int)((e + middleM + (AimAheadS * v)) / LimitStepM), 0, rightAt.Length - 1);
                aim = Math.Clamp(aim, -rightAt[ahead], leftAt[ahead]);
                c += (aim - c) * Math.Min(StepS / LagS, 1.0);
                int at = Math.Clamp((int)((e + middleM) / LimitStepM), 0, rightAt.Length - 1);
                risk += OffCost * StepS * Math.Max(Math.Max(c - leftAt[at], -rightAt[at] - c), 0.0);
            }
            else c += (aim - c) * Math.Min(StepS / LagS, 1.0);
            if (risk > bound) return double.PositiveInfinity;
            double weight = 1.0 - (0.5 * t / HorizonS);
            for (int k = 0; k < n; k++)
            {
                if ((touched & (1 << k)) != 0) continue;
                double oaPrev = oa[k];
                // A car ahead on the road slows for what the road ahead of it allows.
                if (ov[k] > 0.0 && limits is not null) ov[k] = Math.Min(ov[k], Math.Max(Limit(limits, oa[k] + middleM, ov[k]), ov[k] - (brake * StepS)));
                oa[k] += ov[k] * StepS;
                double oy = oFrozen[k] ? oY[k] : Lateral(oY[k], oVy[k], oLo[k], oHi[k], t, c, lo, hi, ov[k]);
                // Swept over the step: at speed a step carries the driver further than two cars are long.
                double r1 = oa[k] - e, r0 = oaPrev - ePrev;
                double along = (r0 > 0.0) != (r1 > 0.0) ? -1.0 : Math.Min(Math.Abs(r0), Math.Abs(r1)) - (2.0 * HalfLongM);
                double across = Math.Abs(oy - c) - HalfWideM - oHa[k];
                double closing = oa[k] > e ? Math.Max(v - ov[k], 0.0) : Math.Max(ov[k] - v, 0.0);
                double sep = Math.Max(along, across);
                // A touch is counted once, by how hard it is: counted a step at a time, going through a car fast was cheaper than braking.
                if (sep < 0.0)
                {
                    // Beside a car being overtaken a touch is a rub; into its tail it is what a pass is not.
                    bool rub = Math.Abs(r1) < (2.0 * HalfLongM) - RubOverlapM;
                    risk += (oTouch[k] < TouchCost && oNear[k] > 0.0 && !rub ? TouchCost * (1.0 + (closing / 10.0)) : oTouch[k] + (oPer[k] * closing)) * weight;
                    touched |= 1 << k;
                    continue;
                }
                if (oNear[k] <= 0.0) continue;
                if (sep < PadM)
                {
                    risk += PadCost * oNear[k] * StepS * weight * (1.0 - (sep / PadM));
                    continue;
                }
                // A car coming the other way is beside the driver for a moment: met with less room than one followed or gone by, and not more for the closing.
                bool coming = ov[k] < -ComingMs;
                double room = (AlongMarginM + (coming ? 0.0 : AlongMarginS * closing)) * oNear[k], margin = (coming ? ComingMarginShare : 1.0) * SideMarginM * oNear[k];
                if (along < room && across < margin) risk += NearCost * StepS * weight * (1.0 - (Math.Max(across, 0.0) / margin)) * (1.0 - (Math.Max(along, 0.0) / room));
            }
        }
        (gone, vEnd) = (e, v);

        // Past the horizon, still behind a car in the same lane holds the driver to that car's speed for a while yet.
        for (int k = 0; k < n; k++)
        {
            if (oa[k] <= e || (touched & (1 << k)) != 0) continue;
            double oy = oFrozen[k] ? oY[k] : Lateral(oY[k], oVy[k], oLo[k], oHi[k], HorizonS, c, lo, hi, ov[k]);
            if (Math.Abs(oy - c) - HalfWideM - oHa[k] >= 0.3) continue;
            double held = Math.Max(ov[k], 0.0), free = Limit(limits, e + middleM, freeBase);
            if (held < free) risk += FollowOnS * (free - held) / Math.Max(free, 1.0);
        }
        return risk;
    }

    // Where a car is across the road at a time ahead: carried on at its rate across for a moment, and past that, if it weaves, the place in its band nearest the driver.
    private static double Lateral(double y, double vy, double yLo, double yHi, double t, double ego, double lo, double hi, double alongMs)
    {
        double ahead = Math.Clamp(y + (vy * Math.Min(t, CarryS)), lo - 1.5, hi + 1.5);
        // Only a car going the driver's way can be wherever the driver goes in the band it has swung over: one standing or coming the other way
        // has no time to, and a car driven the wrong way along a racing line sweeps the road as the line does, which made every lane a crash.
        if (t <= WeaveForS || yHi - yLo < WeaveM || alongMs <= Rival.StandingMs) return ahead;
        return Math.Clamp(ego, Math.Min(yLo, ahead), Math.Max(yHi, ahead));
    }
}
