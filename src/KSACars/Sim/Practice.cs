using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// A car practising a course by itself, lapped in the rig. First every bend is taken quicker, try by
/// try, until a lap is not clean or two have been slower. Then, in turn until neither finds anything
/// or the laps allowed are spent, the driver's settings are each moved for as long as the lap is
/// quicker, and the pace at each of <see cref="Rival.Places"/> places round the lap is moved and kept
/// where the stretches about that place were quicker for it. A lap that goes wrong says where, so
/// only the places that led to it are left as they were.
///
/// <para>A way is clean if it is at the game's uneven steps and at the longest step the game takes,
/// which it takes every frame with a field of cars in it: a lap found at 25 ms left the road at it.
/// Each time, what was found is lapped again every way a search for a lap is, with less taken
/// where one goes wrong until all are clean, and only then given out: so there is a lap to drive by
/// after some thirty laps and a quicker one every fifty or so after.</para>
///
/// <para>It asks nothing of the game: a route, the surface it runs on and the car. So it is run beside
/// the game for whatever course is laid, a player's own among them, and what it finds is where the
/// rig and the game agree, which for a car on its wheels on asphalt they do to a second a lap.</para>
/// </summary>
internal static class Practice
{
    /// <summary>How much quicker each try is everywhere than the one before, and the quickest any place is tried.</summary>
    public const double PaceStep = 0.04, MostPace = 1.6;

    /// <summary>How much of what was taken over the car's own pace is given back each time the quickest way everywhere is not clean every way, and how often.</summary>
    public const double GiveBack = 0.15;
    public const int MostGivenBack = 12;

    /// <summary>The steps a place's pace is moved by, the least it is brought to, and what is taken off about a place a lap went wrong at when it is made sure of.</summary>
    public const double LeastPace = 0.7, LessPace = 0.03;
    public static readonly double[] PlaceSteps = [0.06, 0.03];

    /// <summary>The share of its grip a car is tried with, and of the air on its wings, to be sure of a lap that does not need all of either.</summary>
    public const double LessGrip = 0.94, LessAir = 0.94;

    /// <summary>The shares of every bend's speed a lap is measured at for what it gives up, from the least slowing to the most.</summary>
    public static readonly double[] Slowings = [0.95, 0.9, 0.85, 0.8, 0.7, 0.6];

    /// <summary>The laps a practice may take looking: half a minute to a minute of one core, by the car.</summary>
    public const int MostDrives = 300;

    /// <summary>What was found: the lap's time, the slowest of the ways it was tried; the driver's settings it was lapped with; and how many laps it took to find.</summary>
    public sealed record Found(double Seconds, Autopilot.Tuning Tune, double BlindSeconds, int Drives);

    /// <summary>The longest step KSA takes at no time warp, s, which it takes every frame while it draws fewer than 30 a second.</summary>
    public const double LongestStep = 1.0 / 30.0;

    // The game's own steps, as the suite laps with them: between 17 and 33 ms and no two alike.
    private static readonly double[] GameSteps = Drawn();

    private static double[] Drawn()
    {
        Random random = new(5554);
        double[] steps = new double[997];
        for (int i = 0; i < steps.Length; i++) steps[i] = 0.017 + (0.016 * random.NextDouble());
        return steps;
    }

    /// <summary>
    /// A standing lap and a flying one of a route by a car, in the rig: whether it was clean, which is
    /// finished with no wheel off the asphalt and the hull never down, and how long the flying lap took.
    /// </summary>
    /// <param name="steps">The steps it is stepped at, gone round: one for a steady step.</param>
    public static (bool Clean, double Seconds) Lap(TrackCar car, TrackWorld world, RoadSurface road, IReadOnlyList<RoadLoop> loops, Route route,
                                                  Autopilot.Tuning tune, double[] steps, Func<bool>? cancelled = null)
    {
        Drove drove = Drive(car, world, road, loops, route, tune, steps, cancelled);
        return (drove.Clean, drove.Seconds);
    }

    /// <summary>
    /// A lap as it went: clean or not, the flying lap's time, where round the lap it went wrong, as a
    /// share of it, and how long each of <see cref="Rival.Places"/> equal stretches of the flying lap took.
    /// </summary>
    public readonly record struct Drove(bool Clean, double Seconds, double WrongAt, double[] Stretches);

    /// <summary><see cref="Lap"/>, with where it went wrong and how long each stretch of it took.</summary>
    /// <param name="planAir">The air the driver takes there to be, where not the world's.</param>
    public static Drove Drive(TrackCar car, TrackWorld world, RoadSurface road, IReadOnlyList<RoadLoop> loops, Route route,
                              Autopilot.Tuning tune, double[] steps, Func<bool>? cancelled = null, double planAir = double.NaN)
    {
        TrackRig rig = new(car, world, road) { KeepsLog = false };
        rig.Loops.AddRange(loops);

        // Stood where the route starts, facing along it, wherever on the body that is.
        (double3 at, _, double3 ahead) = route.Standing(Math.Min(1.5 * BuggyDrive.Wheelbase(car.Profile), route.LengthM));
        double3 up = Vec.Unit(at), east = Vec.Unit(Vec.Cross(new double3(0, 0, 1), up)), north = Vec.Cross(up, east);
        (double eastM, double northM) = world.Flatten(at);
        rig.Place(eastM, northM, Math.Atan2(Vec.Dot(ahead, north), Vec.Dot(ahead, east)) * 180.0 / Math.PI,
                  Vec.Len(at) - world.RadiusM - world.HeightAt(up));
        rig.Settle(1.0);
        rig.Dt = steps[0];
        rig.Pattern = steps.Length > 1 ? steps : null;

        Autopilot pilot = new(car.Profile, route, road, car.MassKg, world.Gravity, double.IsFinite(planAir) ? planAir : world.Air, route.Closed ? 2 : 1, 0.0, 900.0) { Tune = tune };
        rig.Driver = pilot;
        double length = route.LengthM, flyingFrom = route.Closed ? length : 0.0;
        double[] passed = new double[Rival.Places + 1];
        int next = 0;
        (double wasM, double wasSeconds) = (0.0, 0.0);
        for (int step = 0; pilot.End == LapEnd.Running && step < 2_000_000 && rig.Finite; step++)
        {
            LapSummary now = pilot.Summary;
            // A lap that has gone wrong is not driven on, nor one nobody wants any more.
            if (now.OffAsphaltSeconds > 0.0 || rig.HullDownSteps > 0) return new Drove(false, 0.0, (now.ProgressM / length) % 1.0, []);
            if ((step & 255) == 0 && cancelled?.Invoke() == true) return new Drove(false, 0.0, double.NaN, []);
            rig.Step(default);

            now = pilot.Summary;
            for (; next <= Rival.Places; next++)
            {
                double mark = flyingFrom + (length * next / Rival.Places);
                if (now.ProgressM < mark) break;
                passed[next] = now.ProgressM > wasM ? wasSeconds + ((now.Seconds - wasSeconds) * (mark - wasM) / (now.ProgressM - wasM)) : now.Seconds;
            }
            (wasM, wasSeconds) = (now.ProgressM, now.Seconds);
        }
        LapSummary summary = pilot.Summary;
        bool clean = pilot.End == LapEnd.Finished && summary.OffAsphaltSeconds == 0.0 && rig.HullDownSteps == 0;
        if (!clean) return new Drove(false, 0.0, (summary.ProgressM / length) % 1.0, []);
        double[] stretches = new double[Rival.Places];
        for (int i = 0; i < stretches.Length; i++) stretches[i] = (i + 1 < next ? passed[i + 1] : summary.Seconds) - passed[i];
        return new Drove(true, route.Closed ? summary.LastLapSeconds : summary.Seconds, double.NaN, stretches);
    }

    /// <summary>
    /// The quickest way a car was found to lap a route cleanly, starting from a way of driving that is
    /// <paramref name="blind"/>; null if it does not lap it cleanly even so, or if it was stopped
    /// before it had any. Each way that is quicker than the last and clean every way a lap is tried
    /// is given to <paramref name="improved"/> as it is found, the first after some thirty laps.
    /// </summary>
    /// <param name="mostDrives">The laps it may take looking; what it has is made sure of past them.</param>
    public static Found? Run(TrackCar car, TrackWorld world, RoadSurface road, IReadOnlyList<RoadLoop> loops, Route route,
                             Autopilot.Tuning blind, Func<bool>? cancelled = null, Action<Found>? improved = null, int mostDrives = MostDrives)
    {
        // A car that does not lap cleanly as it races blind starts from less of the grip: what it then finds is still the most this course bears.
        foreach (double share in StartShares)
        {
            Autopilot.Tuning from = share >= 1.0 ? blind : blind with { GripShare = blind.GripShare * share };
            if (new Search(car, world, road, loops, route, from, cancelled, improved, mostDrives).Run() is { } found) return found;
            if (cancelled?.Invoke() == true) return null;
        }
        return null;
    }

    /// <summary>
    /// What a way of lapping gives up with every bend taken slower, each of <see cref="Slowings"/> as a
    /// share of the lap at its own pace, at the game's steps: what a gap asked of an opponent is read
    /// off. A slowing that does not lap cleanly is left out.
    /// </summary>
    public static (double Slowing, double Gap)[] Slowed(TrackCar car, TrackWorld world, RoadSurface road, IReadOnlyList<RoadLoop> loops, Route route,
                                                        Autopilot.Tuning tune, Func<bool>? cancelled = null)
    {
        Drove own = Drive(car, world, road, loops, route, tune, GameSteps, cancelled);
        if (!own.Clean) return [];
        List<(double, double)> slowed = [];
        foreach (double slowing in Slowings)
        {
            Drove lap = Drive(car, world, road, loops, route, tune with { Pace = [.. Enumerable.Range(0, Rival.Places).Select(i => tune.PaceAt((double)i / Rival.Places) * slowing)] },
                              GameSteps, cancelled);
            if (cancelled?.Invoke() == true) return [];
            if (lap.Clean) slowed.Add((slowing, (lap.Seconds / own.Seconds) - 1.0));
        }
        return [.. slowed];
    }

    /// <summary>The shares of the grip a car races blind with that practice is started from, the first it laps cleanly with being the one.</summary>
    public static readonly double[] StartShares = [1.0, 0.94, 0.88, 0.82, 0.76, 0.7];

    /// <summary>A way of driving with every bend of it taken so much quicker.</summary>
    public static Autopilot.Tuning Paced(Autopilot.Tuning tune, double pace) => tune with { Pace = [pace] };

    // One of the driver's settings that is turned: how it is read and set, how far a try moves it, and what it is kept within.
    private sealed record Setting(Func<Autopilot.Tuning, double> Of, Func<Autopilot.Tuning, double, Autopilot.Tuning> With, double Step, double Least, double Most);

    // The ones a lap of Club Motorsports was quicker for in any of the three cars, in the order of how much. The
    // shares of the grip, of a crest and of tipping never were: the pace is the grip's share, place by place.
    private static readonly Setting[] Settings =
    [
        new(t => t.LookAheadSeconds, (t, v) => t with { LookAheadSeconds = v }, 0.1, 0.2, 1.5),
        new(t => t.TyreShare, (t, v) => t with { TyreShare = v }, 0.05, 0.6, 1.0),
        new(t => t.CurveOverM, (t, v) => t with { CurveOverM = v }, 4.0, 0.0, 30.0),
        new(t => t.DipShare, (t, v) => t with { DipShare = v }, 0.2, 0.1, 1.0),
        new(t => t.BrakeShare, (t, v) => t with { BrakeShare = v }, 0.1, 0.4, 1.0),
        new(t => t.PedalSeconds, (t, v) => t with { PedalSeconds = v }, 0.05, 0.04, 0.4),
        new(t => t.BrakeLeadSeconds, (t, v) => t with { BrakeLeadSeconds = v }, 0.05, 0.0, 0.3),
        new(t => t.BrakeOnShare, (t, v) => t with { BrakeOnShare = v }, 0.02, 0.0, 0.1),
    ];

    private sealed class Search(TrackCar car, TrackWorld world, RoadSurface road, IReadOnlyList<RoadLoop> loops, Route route,
                                Autopilot.Tuning blind, Func<bool>? cancelled, Action<Found>? improved, int mostDrives)
    {
        private const int Places = Rival.Places;

        // A way as it was tried: the slower of its laps, where it went wrong, and each stretch's time over those laps.
        private readonly record struct Tried(bool Clean, double Seconds, double WrongAt, double[] Stretches);

        private readonly TrackCar _slippery = car with { Profile = car.Profile with { FrontGrip = car.Profile.FrontGrip * LessGrip, RearGrip = car.Profile.RearGrip * LessGrip } };

        private int _drives;
        private double _blindSeconds;
        private Found? _found;

        // The way that is built on, as a searched lap is tried, and the last that was sure, which is gone back to.
        private Tried _lap;
        private Autopilot.Tuning _tune = blind;
        private (Autopilot.Tuning Tune, Tried Lap) _sure;

        private bool Stopped => cancelled?.Invoke() == true;
        private bool Spent => _drives >= mostDrives || Stopped;

        private Drove Drive(Autopilot.Tuning tune, double[] steps, bool lessGrip = false, bool thinAir = false)
        {
            _drives++;
            if (thinAir) return Practice.Drive(car, world.Thinner(LessAir), road, loops, route, tune, steps, cancelled, world.Air);
            // The plan is the one the car with all its grip is driven by.
            return lessGrip ? Practice.Drive(_slippery, world, road, loops, route, tune with { GripShare = tune.GripShare / LessGrip }, steps, cancelled)
                            : Practice.Drive(car, world, road, loops, route, tune, steps, cancelled);
        }

        // While it is looked for a way is clean if it is at the game's steps and at the longest the game takes, which a
        // game under 30 frames a second takes every frame: there a car's springs are their softest.
        private Tried Try(Autopilot.Tuning tune)
        {
            Drove a = Drive(tune, GameSteps);
            if (!a.Clean) return new Tried(false, 0.0, a.WrongAt, []);
            Drove b = Drive(tune, [LongestStep]);
            if (!b.Clean) return new Tried(false, 0.0, b.WrongAt, []);
            return new Tried(true, Math.Max(a.Seconds, b.Seconds), double.NaN, [.. a.Stretches.Zip(b.Stretches, (x, y) => x + y)]);
        }

        public Found? Run()
        {
            Tried first = Try(Paced(blind, 1.0));
            if (!first.Clean || Stopped) return null;
            _blindSeconds = first.Seconds;

            List<(double Pace, double[] Stretches)> paces = [(1.0, first.Stretches)];
            if (!Everywhere(first, paces) || _found is null) return null;

            // The settings before the places: they move what every place bears. Round again for as long as a round finds anything.
            for (int round = 0; !Spent; round++)
            {
                double had = _found.Seconds;
                TurnSettings();
                if (Stopped) break;
                MakeSure();
                if (Spent) break;
                if (round == 0) Composite(paces);
                foreach (double step in PlaceSteps) PlaceByPlace(step);
                if (Stopped) break;
                MakeSure();
                if (_found.Seconds >= had) break;
            }
            return _found;
        }

        // Quicker everywhere until a lap goes wrong, or until quicker has twice been slower: past what its tyres bear a car is slower and not off.
        private bool Everywhere(Tried first, List<(double Pace, double[] Stretches)> paces)
        {
            (double pace, double best) = (1.0, first.Seconds);
            int slower = 0;
            for (double tried = 1.0 + PaceStep; tried <= MostPace + 1e-9 && slower < 2; tried += PaceStep)
            {
                Tried lap = Try(Paced(blind, tried));
                if (Stopped) return false;
                if (!lap.Clean) break;
                paces.Add((tried, lap.Stretches));
                if (lap.Seconds < best) (pace, best, slower) = (tried, lap.Seconds, 0);
                else slower++;
            }

            // The quickest, every way a searched lap is tried, with some of it given back until it is clean every way.
            for (int back = 0; back <= MostGivenBack; back++)
            {
                Autopilot.Tuning tune = Paced(blind, pace);
                Tried sure = Sure(tune, out double worst);
                if (Stopped) return false;
                if (sure.Clean)
                {
                    Keep(tune, sure, worst);
                    return true;
                }
                if (pace <= 1.0) return false;
                pace = Math.Max(1.0, 1.0 + ((pace - 1.0) * (1.0 - GiveBack)));
                if (pace - 1.0 < 0.5 * PaceStep) pace = 1.0;
            }
            return false;
        }

        private void Keep(Autopilot.Tuning tune, Tried sure, double worst)
        {
            (_tune, _lap, _sure) = (tune, sure, (tune, sure));
            _found = new Found(worst, tune, _blindSeconds, _drives);
            improved?.Invoke(_found);
        }

        private static double[] PaceOf(Autopilot.Tuning tune) => [.. Enumerable.Range(0, Places).Select(i => tune.PaceAt((double)i / Places))];

        private static int Wrapped(int place) => ((place % Places) + Places) % Places;

        // What a place's pace is judged by: the stretch either side of it, which its pace reaches over, and the one after, come into at the speed that leaves.
        private static double About(double[] stretches, int place) =>
            stretches[Wrapped(place - 1)] + stretches[place] + stretches[Wrapped(place + 1)];

        private bool Quicker(Autopilot.Tuning tune)
        {
            Tried lap = Try(tune);
            if (!lap.Clean || lap.Seconds >= _lap.Seconds) return false;
            (_tune, _lap) = (tune, lap);
            return true;
        }

        // Each place at the pace its own stretches were quickest at, of the laps taken quicker everywhere: no lap more than the one that tries it.
        private void Composite(List<(double Pace, double[] Stretches)> paces)
        {
            if (paces.Count < 2) return;
            double[] pace = new double[Places];
            for (int i = 0; i < Places; i++) pace[i] = paces.MinBy(p => About(p.Stretches, i)).Pace;
            Quicker(_tune with { Pace = pace });
        }

        // Every place's pace moved a step together, up and then down, and kept at the places whose own stretches were quicker for it.
        private void PlaceByPlace(double step)
        {
            foreach (double way in new[] { step, -step })
            {
                bool[] held = new bool[Places];
                for (int round = 0; round < 3 * Places && held.Contains(false) && !Spent; round++)
                {
                    double[] pace = PaceOf(_tune), moved = [.. pace];
                    for (int i = 0; i < Places; i++)
                    {
                        if (!held[i]) moved[i] = Math.Round(Math.Clamp(pace[i] + way, LeastPace, MostPace), 4);
                    }
                    Tried lap = Try(_tune with { Pace = moved });
                    if (!lap.Clean)
                    {
                        if (double.IsNaN(lap.WrongAt)) return;
                        // The places about where it went wrong are left as they were, and further back each time that is not enough.
                        int at = (int)(lap.WrongAt * Places) % Places;
                        for (int reach = 1; reach <= Places; reach++)
                        {
                            bool any = false;
                            for (int k = -reach; k <= 1; k++)
                            {
                                int i = Wrapped(at + k);
                                any |= !held[i];
                                held[i] = true;
                            }
                            if (any) break;
                        }
                        continue;
                    }

                    double[] merged = [.. pace];
                    bool all = true;
                    for (int i = 0; i < Places; i++)
                    {
                        if (held[i]) continue;
                        if (About(lap.Stretches, i) < About(_lap.Stretches, i)) merged[i] = moved[i];
                        else (held[i], all) = (true, false);
                    }
                    // A lap is taken for its time, not its stretches: the places that were no quicker are only moved no further.
                    if (lap.Seconds < _lap.Seconds) (_tune, _lap) = (_tune with { Pace = moved }, lap);
                    else if (all || !held.Contains(false)) break;
                    else Quicker(_tune with { Pace = merged });
                }
            }
        }

        // Each setting moved a step one way for as long as the lap is quicker for it, and if it is not at once, the other way.
        private void TurnSettings()
        {
            foreach (Setting setting in Settings)
            {
                foreach (double way in new[] { setting.Step, -setting.Step })
                {
                    int taken = 0;
                    for (; taken < 3 && !Spent; taken++)
                    {
                        double now = setting.Of(_tune), next = Math.Round(Math.Clamp(now + way, setting.Least, setting.Most), 4);
                        if (Math.Abs(next - now) < 1e-9 || !Quicker(setting.With(_tune, next))) break;
                    }
                    if (taken > 0) break;
                }
            }
        }

        // What was found since the last sure lap, lapped every way: where one goes wrong less is taken about that place, and
        // a lap that is not mended so, or is no quicker once it is, is dropped for the last sure one.
        private void MakeSure()
        {
            Autopilot.Tuning tune = _tune;
            for (int back = 0; _found is not null && _lap.Seconds < _sure.Lap.Seconds && back <= MostGivenBack; back++)
            {
                Tried sure = Sure(tune, out double worst);
                if (Stopped) return;
                if (sure.Clean)
                {
                    if (worst < _found.Seconds) Keep(tune, sure, worst);
                    break;
                }
                if (double.IsNaN(sure.WrongAt)) break;
                double[] pace = PaceOf(tune);
                int at = (int)(sure.WrongAt * Places) % Places;
                for (int k = -1; k <= 1; k++) pace[Wrapped(at + k)] = Math.Round(Math.Max(pace[Wrapped(at + k)] - LessPace, LeastPace), 4);
                tune = tune with { Pace = pace };
            }
            (_tune, _lap) = _sure;
        }

        // Five kinds of step, two again with less grip than the driver takes there to be and one in thinner air: the two a
        // search tries first, whose time and stretches are what the next try is held against, then those with less grip, where most go wrong.
        private Tried Sure(Autopilot.Tuning tune, out double worst)
        {
            worst = 0.0;
            Tried searched = Try(tune);
            if (!searched.Clean) return searched;
            foreach (double[] steps in new[] { GameSteps, [LongestStep] })
            {
                Drove lap = Drive(tune, steps, lessGrip: true);
                if (!lap.Clean) return new Tried(false, 0.0, lap.WrongAt, []);
            }
            // Air a little thinner than the driver takes it to be, which is less on the wings: the game's air is the height it is laid at.
            Drove thin = Drive(tune, GameSteps, thinAir: true);
            if (!thin.Clean) return new Tried(false, 0.0, thin.WrongAt, []);
            // An opponent takes some places up to 3% slower than the next, and a lap that only holds at its own pace leaves the
            // road then: at steady steps, where it was seen to.
            double[] pace = PaceOf(tune);
            double[][] steady = [[1.0 / 60.0], [0.020], [0.025]];
            foreach (double[] steps in steady)
            {
                for (int odd = 0; odd < 2; odd++)
                {
                    Drove lap = Drive(tune with { Pace = [.. pace.Select((p, i) => i % 2 == odd ? p * Rival.PaceLow : p)] }, steps);
                    if (!lap.Clean) return new Tried(false, 0.0, lap.WrongAt, []);
                }
            }
            worst = searched.Seconds;
            foreach (double[] steps in steady)
            {
                Drove lap = Drive(tune, steps);
                if (!lap.Clean) return new Tried(false, 0.0, lap.WrongAt, []);
                worst = Math.Max(worst, lap.Seconds);
            }
            return searched;
        }
    }
}
