using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using Brutal.Numerics;
using KSA;

namespace KSACars;

/// <summary>
/// The laps being driven by <see cref="Autopilot"/>: for each car one driver, the route it follows and
/// what it has recorded, started and asked after through the bridge.
///
/// <para>Kept by vehicle and not in <see cref="Buggies.Entry"/>, which is made again whenever a car
/// stops being driven for a frame. The driver is stepped from the physics window, by
/// <see cref="Buggies"/>; everything else here runs from the frame hook, which is where a lap that is
/// over is noticed and written down. Both are the game's main thread.</para>
///
/// <para>A lap is filed in the bridge's folder under <c>laps/</c>: a summary when it ends, and with
/// rows asked for a line a step beside it, added to about once a second so a crash keeps most of
/// them.</para>
/// </summary>
internal static class Laps
{
    // Wall seconds a running lap may go without a step, with the world not paused, before it is called off.
    private const double NotSteppedSeconds = 5.0;

    private const double FlushSeconds = 1.0;

    // Half an hour at sixty steps a second.
    private const int MostRows = 108_000;

    private sealed class Lap(Vehicle craft, Autopilot pilot, Route route, int generation, LapRow[]? rows, string stem)
    {
        public Vehicle Craft { get; } = craft;
        public Autopilot Pilot { get; } = pilot;
        public Route Route { get; } = route;
        public int Generation { get; } = generation;
        public LapRow[]? Rows { get; } = rows;
        public string Stem { get; } = stem;
        public string Name { get; init; } = "";
        public string Car { get; init; } = "";
        public string Through { get; init; } = "";
        public double OffsetM { get; init; }
        public double MassKg { get; init; }
        public double Gravity { get; init; }
        public double AirDensity { get; init; }
        public int LapsWanted { get; init; }
        public bool Placed { get; init; }
        public int StepsSeen { get; set; }
        public double Unstepped { get; set; }
        public int RowsFiled { get; set; }
        public double SinceFlush { get; set; }
        public bool Filed { get; set; }
    }

    private static readonly Dictionary<Vehicle, Lap> All = [];
    private static bool _complained;

    private static string Folder => Path.Combine(Bridge.Root, "laps");

    /// <summary>Whether a lap's driver has this car's wheel. Asked from the physics window.</summary>
    public static bool Driving(Vehicle craft) =>
        All.Count > 0 && All.TryGetValue(craft, out Lap? lap) && lap.Pilot.End == LapEnd.Running;

    /// <summary>
    /// One step of a car's lap: the throttle and steering for it. From the physics window, where
    /// nothing may throw, so whatever goes wrong ends the lap and is its reason.
    /// </summary>
    /// <param name="hullDown">Whether a collider of the car's was on something as the step before ended.</param>
    public static DriveInput Step(Vehicle craft, double3 positionCcf, doubleQuat body2Ccf, double3 velocityCcf, double3 up, double3 forward,
                                  ReadOnlySpan<double3> hubs, ReadOnlySpan<double> hubHeights, double dt, bool hullDown)
    {
        if (!All.TryGetValue(craft, out Lap? lap)) return default;
        try
        {
            if (lap.Generation != Roads.Generation)
            {
                lap.Pilot.Finish(LapEnd.RoadsRelaid);
                return default;
            }
            return lap.Pilot.Step(positionCcf, body2Ccf, velocityCcf, up, forward, hubs, hubHeights, dt, hullDown);
        }
        catch (Exception e)
        {
            lap.Pilot.Finish(LapEnd.Failed, e.Message);
            return default;
        }
    }

    /// <summary>
    /// Sets a driver going on a car, round the circuit that is laid; false with the reason.
    /// </summary>
    /// <param name="through">The circuit's points to pass through in order, or null to follow its first road round.</param>
    /// <param name="speedMs">The speed to hold where no bend asks for less; nothing is the car's own top speed.</param>
    /// <param name="place">Whether the car is first stood on the road at the route's start, facing along it.</param>
    /// <param name="jumps">Whether crests and dips are taken at whatever the bends allow.</param>
    public static bool Start(Vehicle craft, IReadOnlyList<int>? through, int laps, double speedMs, double offsetM, double timeoutSeconds,
                             bool place, bool keepRows, bool jumps, out string why)
    {
        if (Buggies.Of(craft) is not { } car)
        {
            why = "that craft is not a buggy";
            return false;
        }
        BuggyProfile profile = car.Drive.Profile;
        if (Roads.RouteOver(through, offsetM, Autopilot.TurnRadius(profile), out Celestial? body, out RoadSurface? surface, out why) is not { } route)
        {
            return false;
        }
        if (!ReferenceEquals(craft.Parent, body))
        {
            why = $"the circuit is laid on {body?.Id}, and the car is not there";
            return false;
        }

        double3 at = KsaWorld.PositionEcl(craft);
        double gravity = Vec.Len(KsaWorld.GravityAt(craft, at));
        double air = KsaWorld.ReferenceAirDensityKgPerM3 * KsaWorld.AirDensityRatioAt(craft, at);

        LapRow[]? rows = keepRows ? new LapRow[MostRows] : null;
        Autopilot pilot = new(profile, route, surface, craft.TotalMass, gravity, air, laps, speedMs,
                              timeoutSeconds > 0.0 ? timeoutSeconds : 600.0, rows) { Jumps = jumps };

        if (place)
        {
            // Far enough along that the rear wheels are on the road where it starts at a dead end.
            (double3 on, double3 roadUp, double3 ahead) = route.Standing(Math.Min(1.5 * BuggyDrive.Wheelbase(profile), route.LengthM));
            double3 radial = Vec.Unit(on);
            if (surface!.TryLocate(on + radial, null, out double over, out _, out double3? facing))
            {
                on += radial * (1.0 - over);

                // The way the asphalt faces there, which a banked road leans and the route's own line does not.
                if (facing is { } face) (roadUp, ahead) = (face, Vec.Unit(Vec.RejectFrom(ahead, face)));
            }
            Buggies.Stand(craft, on, roadUp, ahead);
        }

        string name = KsaWorld.DisplayName(craft);
        string safe = string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_'));
        All[craft] = new Lap(craft, pilot, route, Roads.Generation, rows,
                             Path.Combine(Folder, $"{DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{safe}"))
        {
            Name = name, Car = profile.DisplayName, Through = through is null ? "" : string.Join(',', through), OffsetM = offsetM,
            LapsWanted = Math.Max(laps, 1), Placed = place, MassKg = craft.TotalMass, Gravity = gravity, AirDensity = air,
        };
        Log.Info($"lap: {name} set off round {route.LengthM:F0} m of {(route.Closed ? "circuit" : "road")}, "
                 + $"{pilot.CruiseMs:F0} m/s at most, {offsetM:F1} m left of centre");
        return true;
    }

    /// <summary>Ends a car's lap where it is; false if it has none running.</summary>
    public static bool Stop(Vehicle craft)
    {
        if (!Driving(craft)) return false;
        All[craft].Pilot.Finish(LapEnd.Stopped);
        return true;
    }

    /// <summary>What a car's lap has come to so far, or came to; null if it has driven none.</summary>
    public static Dictionary<string, object?>? Status(Vehicle craft) => All.TryGetValue(craft, out Lap? lap) ? Report(lap) : null;

    /// <summary>
    /// Once a frame, from the hook KSA always calls: ends a lap whose car is gone, whose roads were
    /// laid again or which is no longer stepped, and files what has been recorded. Nothing here may throw.
    /// </summary>
    public static void Update(double dtPlayer)
    {
        if (All.Count == 0) return;
        try
        {
            List<Vehicle>? gone = null;
            foreach (Lap lap in All.Values)
            {
                if (lap.Filed)
                {
                    if (!KsaWorld.IsAlive(lap.Craft)) (gone ??= []).Add(lap.Craft);
                    continue;
                }
                if (lap.Pilot.End == LapEnd.Running) Watch(lap, dtPlayer);

                bool over = lap.Pilot.End != LapEnd.Running;
                lap.SinceFlush += dtPlayer;
                if (lap.Rows is not null && (over || lap.SinceFlush >= FlushSeconds)) FileRows(lap);
                if (!over) continue;

                // Before it is written: a summary that cannot be is not tried again every frame.
                lap.Filed = true;
                Directory.CreateDirectory(Folder);
                File.WriteAllText(lap.Stem + ".json", JsonSerializer.Serialize(Report(lap), JsonOptions));
                Log.Info($"lap: {lap.Name} {lap.Pilot.End} {lap.Pilot.Why} after {lap.Pilot.Summary.Seconds:F1} s and "
                         + $"{lap.Pilot.Summary.ProgressM:F0} m; filed as {lap.Stem}.json");
            }
            if (gone is not null)
            {
                foreach (Vehicle craft in gone) All.Remove(craft);
            }
        }
        catch (Exception e)
        {
            if (_complained) return;
            _complained = true;
            Log.Warn($"lap: could not keep up with a lap: {e.Message}");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // A lap ends itself from inside the physics window; this ends one that window no longer reaches.
    private static void Watch(Lap lap, double dtPlayer)
    {
        Autopilot pilot = lap.Pilot;
        if (!KsaWorld.IsAlive(lap.Craft))
        {
            pilot.Finish(LapEnd.NotDriven, "the craft is gone");
            return;
        }
        if (Buggies.Of(lap.Craft) is not { } car)
        {
            pilot.Finish(LapEnd.NotDriven, "the craft is no longer a car");
            return;
        }
        if (lap.Generation != Roads.Generation)
        {
            pilot.Finish(LapEnd.RoadsRelaid);
            return;
        }

        int steps = pilot.Summary.Steps;
        if (steps != lap.StepsSeen || KsaWorld.IsPaused || !KsaWorld.InFlightScene)
        {
            lap.StepsSeen = steps;
            lap.Unstepped = 0.0;
            return;
        }
        lap.Unstepped += dtPlayer;
        if (lap.Unstepped < NotSteppedSeconds) return;
        pilot.Finish(LapEnd.NotDriven, car.Skipped.Length > 0 ? car.Skipped
                                       : car.StepSeconds > 0.1 ? $"steps of {car.StepSeconds:F2} s, past what the springs are stepped at"
                                       : $"not stepped for {NotSteppedSeconds:F0} s");
    }

    private static void FileRows(Lap lap)
    {
        lap.SinceFlush = 0.0;
        int upTo = lap.Pilot.RowCount;
        if (lap.Rows is not { } rows || upTo <= lap.RowsFiled) return;

        StringBuilder text = new();
        if (lap.RowsFiled == 0) text.Append(LapRow.Header).Append('\n');
        for (int i = lap.RowsFiled; i < upTo; i++) rows[i].AppendTo(text);
        Directory.CreateDirectory(Folder);
        File.AppendAllText(lap.Stem + ".csv", text.ToString());
        lap.RowsFiled = upTo;
    }

    private static Dictionary<string, object?> Report(Lap lap)
    {
        Autopilot pilot = lap.Pilot;
        LapSummary s = pilot.Summary;
        return new()
        {
            ["craft"] = lap.Name,
            ["car"] = lap.Car,
            ["running"] = pilot.End == LapEnd.Running,
            ["end"] = pilot.End.ToString(),
            ["why"] = pilot.Why,
            ["route"] = lap.Through.Length > 0 ? lap.Through : "the first road, followed round",
            ["route_m"] = Math.Round(lap.Route.LengthM, 1),
            ["route_closed"] = lap.Route.Closed,
            ["offset_m"] = lap.OffsetM,
            ["placed"] = lap.Placed,
            ["cruise_ms"] = Math.Round(pilot.CruiseMs, 1),
            ["jumps"] = pilot.Jumps,
            ["mass_kg"] = Math.Round(lap.MassKg, 1),
            ["gravity_ms2"] = Math.Round(lap.Gravity, 4),
            ["air_kgm3"] = Math.Round(lap.AirDensity, 4),
            ["laps_wanted"] = lap.LapsWanted,
            ["laps"] = s.Laps,
            ["seconds"] = Math.Round(s.Seconds, 2),
            ["last_lap_s"] = Math.Round(s.LastLapSeconds, 2),
            ["progress_m"] = Math.Round(s.ProgressM, 1),
            ["distance_m"] = Math.Round(s.DistanceM, 1),
            ["steps"] = s.Steps,
            ["longest_step_ms"] = Math.Round(s.LongestStepSeconds * 1000.0, 1),
            ["max_speed_ms"] = Math.Round(s.MaxSpeed, 2),
            ["max_cross_m"] = Math.Round(s.MaxCrossM, 3),
            ["mean_cross_m"] = Math.Round(s.MeanCrossM, 3),
            ["off_asphalt_s"] = Math.Round(s.OffAsphaltSeconds, 2),
            ["air_s"] = Math.Round(s.AirSeconds, 2),
            ["longest_flight_s"] = Math.Round(s.LongestFlightSeconds, 2),
            ["hull_down_s"] = Math.Round(s.HullSeconds, 2),
            ["hub_low_m"] = s.Steps > 0 ? Math.Round(s.MinHubM, 3) : null,
            ["hub_high_m"] = s.Steps > 0 ? Math.Round(s.MaxHubM, 3) : null,
            ["max_roll_deg"] = Math.Round(s.MaxRollDeg, 1),
            ["max_pitch_deg"] = Math.Round(s.MaxPitchDeg, 1),
            ["brake_toggles"] = s.BrakeToggles,
            ["rows"] = lap.Rows is null ? null : pilot.RowCount,
            ["rows_full"] = lap.Rows is { } rows && pilot.RowCount >= rows.Length,
            ["summary_file"] = lap.Filed ? lap.Stem + ".json" : null,
            ["rows_file"] = lap.RowsFiled > 0 ? lap.Stem + ".csv" : null,
        };
    }
}
