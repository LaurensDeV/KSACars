using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Brutal.Numerics;
using KSA;

namespace KSACars;

/// <summary>
/// The opponents' practice: for the course that is laid and a car that is to race on it,
/// <see cref="Practice"/> run beside the game, on a thread of its own that touches nothing of KSA's,
/// and what it finds kept for the opponents to drive by and for the next time that course is laid.
///
/// <para>A course a player draws has no lap that was searched for, and this is what its opponents
/// have in its place: started when the first opponent is asked for, a first lap after some thirty
/// laps in the rig, a few seconds, and a quicker one every few seconds after for up to a minute.
/// Each is the opponents' as it is found. Until the first, an opponent races blind.</para>
/// </summary>
internal static class Practising
{
    // Changed by hand when the driver or the rig changes what a kept lap means: a lap kept under another is not read.
    private const int Version = 4;

    // The ground a practising car has under it where there is no road: far enough down that only the road is ever met.
    private const double NoGroundM = -1000.0;

    private sealed record Asked(int Generation, string Car);

    private static readonly ConcurrentDictionary<Asked, Rival.Learnt?> Done = new();
    private static readonly ConcurrentDictionary<Asked, byte> Running = new();

    /// <summary>What the panel says of it.</summary>
    public static string Message { get; private set; } = "";

    /// <summary>Where practised laps are kept, beside the circuits a player has.</summary>
    public static string Folder => Path.Combine(Directory.GetParent(CircuitLibrary.Folder)?.FullName ?? CircuitLibrary.Folder, "Laps");

    /// <summary>The lap practised of the course that is laid by the car a profile is, or null while there is none.</summary>
    public static Rival.Learnt? LapOf(BuggyProfile profile) =>
        Done.TryGetValue(new Asked(Roads.Generation, profile.DisplayName), out Rival.Learnt? learnt) ? learnt : null;

    /// <summary>
    /// Has a car practise the course that is laid, if it has not and is not. From the frame hook: what
    /// it needs of the game is read here, and the thread is given that and nothing of KSA's.
    /// </summary>
    public static void Want(Vehicle craft)
    {
        try
        {
            if (Buggies.Of(craft) is not { } car || craft.Parent is not Celestial body) return;
            BuggyProfile profile = car.Drive.Profile;
            Asked asked = new(Roads.Generation, profile.DisplayName);
            if (Done.ContainsKey(asked) || !Running.TryAdd(asked, 0)) return;

            if (Roads.SurfaceOn(body) is not { } road || Roads.CircuitOn(body) is not { } circuit)
            {
                Running.TryRemove(asked, out _);
                return;
            }
            double3 at = KsaWorld.PositionEcl(craft);
            double gravity = Vec.Len(KsaWorld.GravityAt(craft, at));
            double air = KsaWorld.ReferenceAirDensityKgPerM3 * KsaWorld.AirDensityRatioAt(craft, at);

            // The car as the engine has it, in its part's own frame, which is the frame its hubs are in.
            double3 com = (craft.CenterOfMassAsmb - car.Part.PositionVehicleAsmb).Transform(car.Part.Asmb2VehicleAsmb.Inverse());
            var inertia = craft.TotalMassPropsBody.Inertia;
            (double floor, double roof) = Hull(profile);
            TrackCar rigCar = new(profile.DisplayName, profile, com, craft.TotalMass, new double3(inertia.XX, inertia.YY, inertia.ZZ), floor, roof);
            TrackWorld world = new(body.MeanRadius, gravity, air, (_, _) => NoGroundM);
            IReadOnlyList<RoadLoop> loops = Roads.LoopsOn(body);

            // What says this course, on ground like this, for this car, under this driver: the key a lap is
            // kept by. A course laid at the car is never laid in quite the same place twice, so it is the
            // course as its file has it, with how long its roads came out and how much they climb here,
            // which is what the ground under it changes; a course that is no file's is itself, where it is.
            double climbM = 0.0;
            Route? middle = Roads.RouteOver(null, 0.0, Autopilot.TurnRadius(profile), out _, out _, out _);
            foreach (Route.Sample sample in middle is null ? [] : middle.Samples) climbM += Math.Abs(sample.Slope) * Route.SpacingM;
            string shape = Courses.LaidShape is { } filed
                ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{filed}|{(middle?.LengthM ?? 0.0) / 2.0:F0}|{climbM / 4.0:F0}")
                : circuit.ToJson();
            string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"{shape}|{body.Id}|{profile.DisplayName}|{craft.TotalMass:F0}|{gravity:F1}|{air:F2}|{Version}"))));
            string file = Path.Combine(Folder, key + ".json");

            Thread thread = new(() => Run(asked, rigCar, world, road, loops, profile, key, file))
            {
                IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "KSACars practice",
            };
            Message = $"{profile.DisplayName} is practising this course...";
            thread.Start();
        }
        catch (Exception e)
        {
            Log.Error("practice could not be started", e);
        }
    }

    // Beside the game: nothing of KSA's is touched here, and what is laid is swapped whole and never changed.
    private static void Run(Asked asked, TrackCar car, TrackWorld world, RoadSurface road, IReadOnlyList<RoadLoop> loops, BuggyProfile profile,
                            string key, string file)
    {
        try
        {
            if (Kept(file) is { } kept)
            {
                Done[asked] = kept;
                Message = $"{profile.DisplayName} knows this course: {RaceBoard.Clock(kept.Seconds)}";
                Log.Info($"practice: {profile.DisplayName} has a lap of this course kept, {kept.Seconds:F1} s");
                return;
            }

            // A practice that was cut short left the best it had: driven by at once, and practised past.
            string unfinished = Path.ChangeExtension(file, ".part.json");
            Rival.Learnt? best = Kept(unfinished);
            if (best is not null) Done[asked] = best;

            Rival.Blind blind = Rival.BlindFor(profile.DisplayName);
            double inside = Autopilot.RaceInside(profile) - (blind.NearerM * blind.Most);
            bool Stale() => Roads.Generation != asked.Generation;
            Stopwatch took = Stopwatch.StartNew();
            if (Roads.RouteOver(null, 0.0, Autopilot.TurnRadius(profile), out _, out _, out string why, inside) is not { } route || Stale())
            {
                Log.Info($"practice: {profile.DisplayName} has no route to practise on ({why})");
                return;
            }

            void Improved(Practice.Found lap)
            {
                if (Stale() || (best is not null && lap.Seconds >= best.Seconds)) return;
                best = new Rival.Learnt(lap.Seconds, lap.Tune, inside, [], key, Most: 1.0);
                Done[asked] = best;
                Message = $"{profile.DisplayName} is practising this course: {RaceBoard.Clock(lap.Seconds)} so far";
                Log.Info($"practice: {profile.DisplayName} round {route.LengthM:F0} m: {lap.Seconds:F1} s for {lap.BlindSeconds:F1} s blind, "
                         + $"{lap.Drives} laps in {took.Elapsed.TotalSeconds:F1} s");
                Keep(unfinished, best);
            }

            Autopilot.Tuning start = new Autopilot.Tuning().Toward(blind.Tune, blind.Most);
            // What the car is to the rig, and how a first lap of it goes: what there is to go on if the rig and the game part.
            Practice.Drove first = Practice.Drive(car, world, road, loops, route, start, [0.025], Stale);
            string went = first.Clean ? FormattableString.Invariant($"is clean, {first.Seconds:F1} s")
                                      : FormattableString.Invariant($"goes wrong {first.WrongAt * route.LengthM:F0} m round {route.LengthM:F0} m");
            Log.Info(FormattableString.Invariant($"practice: {profile.DisplayName} as the rig has it: {car.MassKg:F0} kg, centre of mass {car.Com.X:F2} {car.Com.Y:F2} {car.Com.Z:F2}, inertia {car.Inertia.X:F0} {car.Inertia.Y:F0} {car.Inertia.Z:F0}, gravity {world.Gravity:F2}, air {world.Air:F3}; a lap racing blind {went}"));
            Practice.Run(car, world, road, loops, route, start, Stale, Improved);
            if (Stale()) return;
            if (best is null)
            {
                Done[asked] = null;
                Message = $"{profile.DisplayName} could not lap this course cleanly in practice";
                Log.Info($"practice: {profile.DisplayName} does not lap {route.LengthM:F0} m of course cleanly in the rig; its opponents race blind");
                return;
            }

            Message = $"{profile.DisplayName} has practised: {RaceBoard.Clock(best.Seconds)}";
            Log.Info($"practice: {profile.DisplayName} has done practising in {took.Elapsed.TotalSeconds:F1} s, {best.Seconds:F1} s: {best.Tune.Written()}");
            Keep(file, best);
            try
            {
                File.Delete(unfinished);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Left there it is only read where the whole practice's file is not.
            }
        }
        catch (Exception e)
        {
            Log.Error("practice failed", e);
        }
        finally
        {
            Running.TryRemove(asked, out _);
            // What was found for roads that are gone is nobody's.
            foreach (Asked old in Done.Keys.Where(k => k.Generation != Roads.Generation)) Done.TryRemove(old, out _);
        }
    }

    private static void Keep(string file, Rival.Learnt lap)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(file, lap.ToJson());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"practice: the lap could not be kept ({e.Message})");
        }
    }

    private static Rival.Learnt? Kept(string file)
    {
        try
        {
            return File.Exists(file) ? Rival.Read(File.ReadAllText(file), out _) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // How far over the ground each car's lowest collider ends and its highest tops out, at rest: from KSACarsGameData.xml.
    private static (double FloorM, double RoofM) Hull(BuggyProfile profile) => profile.DisplayName switch
    {
        "Ferrari F2004" => (0.14, 0.70),
        "Cadillac Eldorado" => (0.25, 1.00),
        _ => (0.18, 1.10),
    };
}
