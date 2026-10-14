using Brutal.Numerics;
using KSA;

namespace KSACars;

/// <summary>
/// Cars that race the one being flown, for a player: each a copy of the flown car, put in the next
/// box of the grid behind it, a kitten at its wheel, and driven round the laid course by the route
/// driver, <see cref="Autopilot"/>, from the moment the player's own car sets off.
///
/// <para>Each is a driver of its own, <see cref="Rival.Of"/> its number, so no two lap alike and the
/// same one is the same every race, and none laps alike twice. It holds back behind a car that is in its way and goes round
/// one where <see cref="Rival.Pass"/> finds the road for it. Knocked off the road it drives itself back on, and on its roof it is set on its wheels
/// to; only one that cannot, in a quarter of a minute, or is stuck, is stood back on the road. Asked from the panel, which is in the GUI pass, and done from the frame hook.</para>
/// </summary>
internal static class Opponents
{
    // How long a new car is waited for to arrive on the grid, in frames, before it is given up on.
    private const int ArriveWithinFrames = 600;

    // As near as this across the ground a car has arrived where it was sent.
    private const double ArrivedM = 30.0;

    // The player's car is taken to have set off at this speed.
    private const double SetOffMs = 1.0;

    // Laps and seconds no race reaches: an opponent drives until something ends it.
    private const int ManyLaps = 100_000;
    private const double Seconds = 1e7;

    // How long an opponent whose lap has ended stands on its brakes before it is put back on the road, in frames.
    private const int RecoverFrames = 30;

    // How long an opponent off the asphalt is left to drive itself back on before it is put back, while it is moving.
    private const double DriveBackSeconds = 15.0;

    // An opponent going slower than this for this long, with no car in its way, is stuck, and is put back.
    private const double StillMs = 0.5, StuckSeconds = 2.5;

    // How far ahead an opponent looks for a car in its way.
    private const double LookM = 250.0;

    // Held to less than this by a car in its way, an opponent standing still is waiting and not stuck.
    private const double WaitingMs = 3.0;

    // Seconds from the start in which no opponent begins a pass.
    private const double LaunchSeconds = 8.0;

    // How much slower than the last a driver may take every bend of a lap, half a normal of it, and the chance a lap that it fluffs the exit of one bend.
    private const double LapSpread = 0.015, ExitChance = 0.25;

    // Frames a car set on its wheels is left to settle before it drives on, and frames of racing after which it is righted afresh the next time.
    private const int RightedFrames = 90, ForgivenFrames = 1200;

    // The room a car needs round the place it is put back on the road at, and how many places on from where it left are tried for it.
    private const double ClearM = 8.0;
    private const int PlacesTried = 12;

    private enum Stage { Coming, Gridded, Racing, Righting, Recovering }

    private sealed class Car(Vehicle craft, int box, int generation, int seed)
    {
        public int Seed { get; } = seed;
        public Vehicle Craft { get; } = craft;
        public int Box { get; } = box;
        public int Generation { get; } = generation;
        public Stage Stage { get; set; } = Stage.Coming;

        // Whether its racing line has been worked out ahead of the start.
        public bool Warmed { get; set; }

        public Rival.Learnt? WarmedFor { get; set; }

        // Whether the log has said how it drives.
        public bool Told { get; set; }

        // How long it has stood still with nothing in its way, s.
        public double Still { get; set; }

        // Whether it has been set on its wheels where it lay and not yet driven on for long.
        public bool Righted { get; set; }
        public int Frames { get; set; }

        public Rival.Passing Passing { get; set; } = new();

        // The most it was let go at last frame, for a car in its way.
        public double Cap { get; set; } = double.PositiveInfinity;
    }

    private static readonly List<Car> Cars = [];
    private static bool _add, _clear, _warmedThisFrame;
    private static int _made;

    /// <summary>How much slower than the best lap known of the course the opponents are asked to be, as a share of it. Taken as each sets off.</summary>
    public static double Gap { get; set; } = Rival.DefaultGap;

    public static string Message { get; private set; } = "";

    public static int Count => Cars.Count;

    public static void Add() => _add = true;

    public static void Clear() => _clear = true;

    /// <summary>Once a frame, from the frame hook. Never throws.</summary>
    public static void Update()
    {
        try
        {
            if (_clear)
            {
                _clear = false;
                foreach (Car car in Cars) Remove(car);
                Cars.Clear();
                Message = "";
            }
            if (_add)
            {
                _add = false;
                Message = Begin() is { Length: > 0 } why ? $"no opponent: {why}" : Message;
            }

            _warmedThisFrame = false;
            for (int i = Cars.Count - 1; i >= 0; i--)
            {
                Car car = Cars[i];
                if (!KsaWorld.IsAlive(car.Craft) || car.Generation != Roads.Generation)
                {
                    // Its course is gone from under it, or it is: the car goes with it.
                    Remove(car);
                    Cars.RemoveAt(i);
                    continue;
                }
                Advance(car);
            }
        }
        catch (Exception e)
        {
            Message = $"no opponent: {e.GetBaseException().Message}";
            Log.Error("an opponent failed", e);
        }
    }

    // Has a copy of the flown car set down by the next box of the grid; what went wrong, or nothing.
    private static string Begin()
    {
        if (KsaWorld.ControlledVehicle is not { IsDisposed: false } flown || Buggies.Of(flown) is not { } mine) return "no car is being flown";
        if (Route(mine.Drive.Profile, out Celestial? body, out _, out string why) is not { } route || body is null) return why;
        if (!ReferenceEquals(flown.Parent, body)) return "the course is on another body";

        int box = 1;
        while (Cars.Any(c => c.Box == box)) box++;
        if (box >= StartGrid.BoxesOn(route)) return route.Closed ? "the grid is full" : "this course has no grid behind its start";

        (double s, double left) = Box(route, box);
        (double3 at, _, _) = route.Standing(s);
        double3 cce = at.Transform(body.GetCcf2Cce());
        string id = $"Opponent {++_made}";
        if (CraftSpawner.SpawnLike(flown, id, body, body.GetLatitudeFromCce(cce), body.GetLongitudeFromCce(cce)) is not { } craft)
        {
            return "the car could not be made";
        }
        // The quickest driver to the box nearest the front: a pass needs a straight, and a lap may have few.
        int[] drivers = Rival.Gridded(StartGrid.Boxes - 1);
        Cars.Add(new Car(craft, box, Roads.Generation, drivers[Math.Clamp(box - 1, 0, drivers.Length - 1)]));
        Practising.Want(flown);
        Message = $"{id} is coming to the grid";
        return "";
    }

    private static void Advance(Car car)
    {
        // Not a car until the frame's census has found it.
        if (Buggies.Of(car.Craft) is not { } buggy) return;
        BuggyProfile profile = buggy.Drive.Profile;

        if (car.Stage == Stage.Coming)
        {
            if (Route(profile, out Celestial? body, out RoadSurface? surface, out _) is not { } route || body is null || surface is null) return;
            (double s, double left) = Box(route, car.Box);
            (double3 at, _, _) = route.Standing(s);
            if (!KsaWorld.TryPlaceCcf(car.Craft, body, out double3 now)
                || Vec.Len(Vec.Cross(Vec.Unit(now), Vec.Unit(at))) * Vec.Len(at) > ArrivedM)
            {
                if (++car.Frames > ArriveWithinFrames) Message = $"{KsaWorld.DisplayName(car.Craft)} never reached the grid";
                return;
            }

            // What the launch window's Fill Seats does, for a car that was never launched from one.
            List<IVASeat> seats = [.. car.Craft.Parts.Modules.Get<IVASeat>()];
            if (seats.Count > 0) CrewAssignmentWindow.FillSeats([seats[0]], car.Craft.Parts, car.Craft.Id, hasLaunched: true);
            Laps.Place(car.Craft, profile, route, surface, s, left);
            car.Stage = Stage.Gridded;
            Message = $"{KsaWorld.DisplayName(car.Craft)} is on the grid: it goes when you do";
            return;
        }

        if (car.Stage == Stage.Gridded)
        {
            // Its line is worked out while it waits, one car a frame: nine worked out as the race starts is most of a second with nothing moving.
            // Again once it has a lap to drive by, which moves its line.
            Rival.Learnt? knows = Learnt(profile);
            if ((!car.Warmed || !ReferenceEquals(car.WarmedFor, knows)) && !_warmedThisFrame)
            {
                (car.Warmed, car.WarmedFor, _warmedThisFrame) = (true, knows, true);
                Rival.Way way = Way(car, profile);
                Roads.RouteOver(null, 0.0, Autopilot.TurnRadius(profile), out _, out _, out _,
                                way.InsideM > 0.0 ? way.InsideM : Autopilot.RaceInside(profile), way.LineM);
            }
            if (KsaWorld.ControlledVehicle is not { } flown || Buggies.Of(flown) is not { PlaceValid: true } mine || mine.SpeedMs < SetOffMs) return;
            Message = SetOff(car) is { Length: > 0 } why ? $"{KsaWorld.DisplayName(car.Craft)} would not start: {why}"
                                                           : $"{KsaWorld.DisplayName(car.Craft)} is racing";
            car.Passing.Launch = LaunchSeconds;
            return;
        }

        if (car.Stage == Stage.Racing)
        {
            if (++car.Frames > ForgivenFrames) car.Righted = false;
            if (Laps.Ended(car.Craft) is { } end)
            {
                // On its roof it is set on its wheels where it lies and drives on from there, once.
                if (end == LapEnd.Flipped && !car.Righted)
                {
                    (car.Stage, car.Frames, car.Righted) = (Stage.Righting, 0, true);
                    Buggies.Right(car.Craft);
                    Buggies.Hold(car.Craft, new DriveInput(0.0, 0.0, Brake: 1.0), 60.0);
                    Log.Info($"opponent: {KsaWorld.DisplayName(car.Craft)} is on its roof; set on its wheels to drive on");
                    return;
                }

                // Stopped behind a car in its way it is waiting, and drives on when the car does.
                if (end == LapEnd.Stuck && InTheWay(car, buggy, 0.0).CapMs < WaitingMs)
                {
                    if (SetOff(car) is { Length: > 0 } blocked) Message = $"{KsaWorld.DisplayName(car.Craft)} would not start again: {blocked}";
                    return;
                }

                // It could not drive itself back, is stuck, or is over again: it stands on its brakes until it is put back.
                PutBack(car, end.ToString());
                return;
            }

            (double side, double cap) = InTheWay(car, buggy, KsaWorld.IsPaused ? 0.0 : Program.GetPlayerDeltaTime());
            car.Cap = cap;
            Laps.Cap(car.Craft, cap);
            Laps.Side(car.Craft, side);
            if (cap < WaitingMs) Laps.Waiting(car.Craft);

            // Not moving with nothing in its way: against a wall, in a ditch, or on something. It is not left to try for long.
            bool still = buggy.PlaceValid && buggy.SpeedMs < StillMs && cap >= WaitingMs && !KsaWorld.IsPaused;
            car.Still = still ? car.Still + Program.GetPlayerDeltaTime() : 0.0;
            if (car.Still >= StuckSeconds)
            {
                Laps.Stop(car.Craft);
                PutBack(car, $"still for {car.Still:F1} s");
            }
            return;
        }

        if (car.Stage == Stage.Righting && ++car.Frames >= RightedFrames)
        {
            Buggies.Hold(car.Craft, default, 0.0);
            if (SetOff(car) is { Length: > 0 } no) Message = $"{KsaWorld.DisplayName(car.Craft)} would not start again: {no}";
            return;
        }

        if (car.Stage == Stage.Recovering && ++car.Frames >= RecoverFrames)
        {
            if (!buggy.PlaceValid || Route(profile, out _, out RoadSurface? surface, out _) is not { } route || surface is null) return;
            // Not onto another car: the nearest place on from where it left that has room. Two cars
            // waiting for each other's place to clear would wait for good.
            double from = route.Samples[route.Nearest(buggy.PlaceCcf)].S;
            Route.Sample? clear = null;
            for (int k = 0; k < PlacesTried && clear is null; k++)
            {
                double s = from + (k * ClearM);
                if (s >= route.LengthM) s = route.Closed ? s - route.LengthM : route.LengthM - 1.0;
                Route.Sample there = route.Samples[route.IndexAt(s)];
                double3 onto = there.At + (there.Across * there.CentreM);
                if (!Buggies.Cars.Any(o => !ReferenceEquals(o.Craft, car.Craft) && o.PlaceValid && Vec.Len(o.PlaceCcf - onto) < ClearM)) clear = there;
            }
            if (clear is not { } near) return;
            Buggies.Hold(car.Craft, default, 0.0);
            Laps.Place(car.Craft, profile, route, surface, near.S, near.CentreM);
            if (SetOff(car) is { Length: > 0 } not) Message = $"{KsaWorld.DisplayName(car.Craft)} would not start again: {not}";
        }
    }

    // Has a car stand on its brakes, to be stood back on the road once it has.
    private static void PutBack(Car car, string why)
    {
        (car.Stage, car.Frames, car.Still, car.Cap) = (Stage.Recovering, 0, 0.0, double.PositiveInfinity);
        Buggies.Hold(car.Craft, new DriveInput(0.0, 0.0, Brake: 1.0), 60.0);
        Log.Info($"opponent: {KsaWorld.DisplayName(car.Craft)} could not drive on, {why}; it will be put back on the road");
    }

    // Starts a car's lap as the driver its number is; what went wrong, or nothing.
    private static string SetOff(Car car)
    {
        (car.Stage, car.Frames, car.Still, car.Passing, car.Cap) = (Stage.Racing, 0, 0.0, new(), double.PositiveInfinity);
        if (Buggies.Of(car.Craft) is not { } buggy) return "it is no longer a car";
        BuggyProfile profile = buggy.Drive.Profile;

        Rival.Way way = Way(car, profile);
        Rival.Learnt? learnt = Learnt(profile);
        if (!Laps.Start(car.Craft, null, ManyLaps, 0.0, 0.0, Seconds, place: false, keepRows: false, jumps: false, out string why,
                        racing: true, push: way.Push, tune: way.Tune, insideM: way.InsideM, nudgesM: way.LineM,
                        offRoadSeconds: DriveBackSeconds, person: new Autopilot.Human(car.Seed, LapSpread, ExitChance)))
        {
            return why;
        }
        if (!car.Told)
        {
            car.Told = true;
            Log.Info(FormattableString.Invariant($"opponent: {KsaWorld.DisplayName(car.Craft)} drives every bend at {way.Tune.Pace.Min():F3} to {way.Tune.Pace.Max():F3}, ")
                     + (learnt is null ? "with no lap of this course to go by"
                                       : FormattableString.Invariant($"by a lap of {learnt.Seconds:F1} s measured at {learnt.Slowed.Length} slowings")));
        }
        return "";
    }

    // A lap of this course by this car, which the pace is then a share of the way to: one it has practised here, or one the mod comes with.
    private static Rival.Learnt? Learnt(BuggyProfile profile) =>
        Practising.LapOf(profile) ?? (Courses.Laid is { } course ? CircuitLibrary.LapOf(course, profile.DisplayName) : null);

    private static Rival.Way Way(Car car, BuggyProfile profile) =>
        Rival.Drive(Learnt(profile), car.Seed, Gap, Autopilot.RaceInside(profile), profile.DisplayName);

    // Where across the road a car is to run and the most it may go at, for the cars round it, the flown one among them.
    private static (double SideM, double CapMs) InTheWay(Car car, Buggies.Entry mine, double dt)
    {
        if (!mine.PlaceValid) return (0.0, double.PositiveInfinity);
        Autopilot? pilot = Laps.Pilot(car.Craft);
        Seen.Clear();
        foreach (Buggies.Entry other in Buggies.Cars)
        {
            if (ReferenceEquals(other.Craft, car.Craft) || !other.PlaceValid) continue;
            if (Rival.See(other.Craft, pilot, mine.PlaceCcf, mine.AheadCcf, other.PlaceCcf, other.AheadCcf, other.SpeedMs, LookM) is not { } seen) continue;
            // An opponent held to a crawl by a car in its own way is queued behind it, and is followed rather than gone round.
            bool queued = Cars.Any(c => ReferenceEquals(c.Craft, other.Craft) && c.Stage == Stage.Racing && c.Cap < WaitingMs);
            Seen.Add(seen with { Queued = queued });
        }
        ReadOnlySpan<Rival.Seen> cars = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Seen);

        // With no driver, or one not yet stepped, nothing is known of the road: no room either side is no pass.
        double free = pilot?.FreeMs ?? 0.0, left = 0.0, right = 0.0, straight = 0.0;
        pilot?.RoadAhead(Rival.RoomOver(cars, mine.SpeedMs), Rival.StraightPerM(Math.Max(free, mine.SpeedMs)), out left, out right, out straight);
        return Rival.Pass(car.Passing, cars, mine.SpeedMs, free, pilot?.CrossM ?? 0.0, left, right, straight, dt, pilot is null ? null : pilot.BrakeAt);
    }

    private static readonly List<Rival.Seen> Seen = [];

    // Where a box of the grid is: how far along the route, a closed one's last metres being behind its start, and how far to its left.
    private static (double S, double Left) Box(Route route, int box)
    {
        double s = StartGrid.PoleM - (box * StartGrid.BoxEveryM);
        if (s < 0.0) s += route.LengthM;
        Route.Sample here = route.Samples[route.IndexAt(s)];
        return (s, here.CentreM + StartGrid.Column(box, here.HalfWidth));
    }

    private static Route? Route(BuggyProfile profile, out Celestial? body, out RoadSurface? surface, out string why) =>
        Roads.RouteOver(null, 0.0, Autopilot.TurnRadius(profile), out body, out surface, out why);

    private static void Remove(Car car)
    {
        try
        {
            Laps.Stop(car.Craft);
            if (KsaWorld.IsAlive(car.Craft)) Universe.DestroyVehicle(car.Craft);
        }
        catch (Exception e)
        {
            Log.Warn($"an opponent could not be taken away: {e.GetBaseException().Message}");
        }
    }
}
