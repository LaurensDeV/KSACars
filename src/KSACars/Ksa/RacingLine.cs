using System.Diagnostics;
using Brutal.Numerics;
using KSA;

namespace KSACars;

/// <summary>
/// The line to drive, drawn on the road ahead of the car being flown: <see cref="RaceLine"/>'s
/// arrowheads in one <see cref="RuntimeMesh"/>, a long stretch written at once, and handed to KSA's
/// renderer with the roads a run of one colour at a time.
///
/// <para>Writing a mesh waits for the graphics card, 30 ms of a frame, so only a new stretch is
/// written, every three kilometres or once for a lap shorter than that. What follows the car and its
/// speed is which indices each material draws, which costs nothing.</para>
///
/// <para>The line and the speeds are the route driver's own, for this car: the racing line it laps
/// by, and what its plan allows at each place, braking counted in. Nothing here is asked by a wheel
/// or by the physics, and nothing here stops a road being drawn.</para>
/// </summary>
internal static class RacingLine
{
    // A material a colour, each drawing this many runs of arrowheads at most: a run past that is left out.
    private const int RunsEach = 6;

    // The arrowheads written at once, which is 3 km of road.
    private const int StretchArrows = 1024;

    // The share the plan's driver takes of what it leaves spare: a line for hands on a keyboard, not for the watch.
    private const double Push = 0.3;

    // Further from the line than this the car is looked for anew along the whole of it, not just ahead of where it was.
    private const double LostM = 40.0;

    private sealed record Plan(Vehicle Craft, Celestial Body, int Generation, Route Route, RaceLine.Arrow[] Arrows, double[] AllowedMs);

    // The stretch that is written: the arrowhead it starts at and how many it is.
    private sealed record Shown(Celestial Body, double3 Origin, int First, int Count);

    private static readonly string[] Materials =
        [.. Enumerable.Range(0, RaceLine.Levels * RunsEach).Select(part => $"KSACars_RaceLine{part / RunsEach}_Material")];

    private static readonly RaceLine.Run[] Runs = new RaceLine.Run[RaceLine.Levels * RunsEach];
    private static readonly int[] Used = new int[RaceLine.Levels];

    private static RuntimeMesh? _mesh;
    private static Plan? _plan;
    private static volatile Shown? _shown;
    private static string? _stopped, _noPlan;
    private static (Vehicle? Craft, int Generation) _tried;
    private static int _index = -1;
    private static double _worstMs;
    private static int _runs, _writes;

    /// <summary>Whether the line is wanted. It is drawn only while a car is flown on a body with roads laid.</summary>
    public static bool Enabled { get; set; }

    public static bool Any => _shown is not null;

    /// <summary>The most draws the line is in a view, which the roads then leave free.</summary>
    public const int MostDraws = RaceLine.Levels * RunsEach;

    /// <summary>What is drawn and why not, for the bridge.</summary>
    public static Dictionary<string, object?> Report() => new()
    {
        ["racing_line"] = Enabled, ["line_drawn"] = Any, ["line_route_m"] = _plan is { } p ? Math.Round(p.Route.LengthM) : null,
        ["line_arrows_written"] = _mesh?.Vertices / RaceLine.Vertices, ["line_runs"] = _runs, ["line_writes"] = _writes,
        ["line_worst_write_ms"] = Math.Round(_worstMs, 2),
        ["line_warning"] = _stopped ?? _noPlan,
    };

    /// <summary>Once a frame, from the frame hook: it writes a mesh. Never throws.</summary>
    public static void Update()
    {
        try
        {
            if (!Enabled || _stopped is not null || KsaWorld.ControlledVehicle is not { IsDisposed: false } craft || Buggies.Of(craft) is not { } car)
            {
                Hide();
                return;
            }
            if (_plan is not { } plan || !ReferenceEquals(plan.Craft, craft) || plan.Generation != Roads.Generation)
            {
                Hide();
                _plan = null;
                if (ReferenceEquals(_tried.Craft, craft) && _tried.Generation == Roads.Generation) return;
                _tried = (craft, Roads.Generation);
                if ((_plan = Planned(craft, car.Drive.Profile)) is null) return;
                plan = _plan;
                _index = -1;
            }
            if (!car.PlaceValid) return;

            Route route = plan.Route;
            if (_index < 0) _index = route.Nearest(car.PlaceCcf);
            route.Locate(car.PlaceCcf, ref _index, 10.0 + car.SpeedMs, out double s, out double cross);
            if (Math.Abs(cross) > LostM)
            {
                _index = route.Nearest(car.PlaceCcf);
                route.Locate(car.PlaceCcf, ref _index, 0.0, out s, out _);
            }

            int first = RaceLine.First(s, car.SpeedMs), total = plan.Arrows.Length;
            if (route.Closed) first %= total;
            int runs = RaceLine.Runs(plan.Arrows, plan.AllowedMs, route.Closed, first, car.SpeedMs, Runs, out int count);
            if (count < 1 || Mesh() is not { } mesh)
            {
                Hide();
                return;
            }

            // Where the first is in what is written: on a closed route that goes on past the route's start.
            int at = _shown is { } was ? first - was.First + (first < was.First && route.Closed ? total : 0) : -1;
            if (_shown is not { } shown || at < 0 || at + count > shown.Count)
            {
                Stopwatch took = Stopwatch.StartNew();
                int most = route.Closed ? Math.Min(StretchArrows, total + RaceLine.Most) : StretchArrows;
                if (RaceLine.Stretch(plan.Arrows, route.Closed, first, most) is not { } stretch) return;
                int[] parts = new int[Materials.Length];
                parts[0] = stretch.Indices.Length;
                if (!mesh.Upload(new RuntimeMesh.Content(stretch.Positions, stretch.Normals, stretch.Uvs, stretch.Indices, parts, stretch.RadiusM), out string why))
                {
                    Stop($"the racing line could not be written: {why}");
                    return;
                }
                _writes++;
                _worstMs = Math.Max(_worstMs, took.Elapsed.TotalMilliseconds);
                shown = new Shown(plan.Body, stretch.Origin, first, stretch.Indices.Length / RaceLine.Indices);
                at = 0;
            }

            Array.Clear(Used);
            for (int part = 0; part < Materials.Length; part++) mesh.Draws(part, 0, 0);
            for (int r = 0; r < runs; r++)
            {
                RaceLine.Run run = Runs[r];
                if (Used[run.Level] >= RunsEach) continue;
                mesh.Draws((run.Level * RunsEach) + Used[run.Level]++, (at + run.From) * RaceLine.Indices, run.Count * RaceLine.Indices);
            }
            _runs = runs;
            _shown = shown;
        }
        catch (Exception e)
        {
            Stop($"the racing line threw: {e.GetBaseException().GetType().Name}: {e.GetBaseException().Message}");
        }
    }

    /// <summary>Has no line drawn until it is switched on again. Never throws.</summary>
    public static void Stop(string why)
    {
        _shown = null;
        if (_stopped is not null) return;
        _stopped = why;
        Enabled = false;
        Log.Warn(why);
    }

    /// <summary>Switches the line on or off, and lets one that was stopped be tried again.</summary>
    public static void Set(bool on)
    {
        Enabled = on;
        if (!on) return;
        (_stopped, _noPlan, _tried) = (null, null, default);
    }

    // Inside the engine's render: called through a hook that catches whatever this throws.
    public static void Draw(IViewport viewport)
    {
        if (_shown is not { } shown || _mesh is not { } mesh) return;

        doubleQuat turn = shown.Body.GetCcf2Cce();
        double3 at = shown.Body.GetPositionEcl() - viewport.GetCamera().PositionEcl + shown.Origin.Transform(turn);
        double3 x = new double3(1.0, 0.0, 0.0).Transform(turn), y = new double3(0.0, 1.0, 0.0).Transform(turn), z = new double3(0.0, 0.0, 1.0).Transform(turn);
        float4x4 transform = new((float)x.X, (float)x.Y, (float)x.Z, 0f, (float)y.X, (float)y.Y, (float)y.Z, 0f,
                                 (float)z.X, (float)z.Y, (float)z.Z, 0f, (float)at.X, (float)at.Y, (float)at.Z, 1f);
        var view = Program.Instance.SuperMeshRenderSystem.ViewForViewport(viewport);
        for (int part = 0; part < mesh.Parts.Length; part++)
        {
            if (mesh.PartIndices(part) <= 0) continue;
            mesh.Parts[part].Transform = transform;
            mesh.Parts[part].Draw(view);
        }
    }

    private static void Hide()
    {
        if (_shown is null) return;
        _shown = null;
        _mesh?.Empty();
    }

    // The racing line round the roads that are laid and what this car may do at each place of it. It reads the ground anew, once.
    private static Plan? Planned(Vehicle craft, BuggyProfile profile)
    {
        if (Roads.RouteOver(null, 0.0, Autopilot.TurnRadius(profile), out Celestial? body, out RoadSurface? surface, out string why,
                            Autopilot.RaceInside(profile)) is not { } route || body is null || !ReferenceEquals(craft.Parent, body))
        {
            _noPlan = string.IsNullOrEmpty(why) ? "the roads are on another body" : why;
            return null;
        }
        double3 at = KsaWorld.PositionEcl(craft);
        double gravity = Vec.Len(KsaWorld.GravityAt(craft, at));
        double air = KsaWorld.ReferenceAirDensityKgPerM3 * KsaWorld.AirDensityRatioAt(craft, at);
        // Where the circuit says a crest is to be jumped the line is not red for it.
        (double, double)[] jumps = [.. (Roads.CircuitOn(body)?.Jumps ?? []).Where(j => j is { Length: 2 }).Select(j => (j[0], j[1]))];
        Autopilot pilot = new(profile, route, surface, craft.TotalMass, gravity, air) { Push = Push, JumpZones = jumps };

        _noPlan = null;
        Plan plan = new(craft, body, Roads.Generation, route, RaceLine.Lay(route, surface), pilot.Plan().ToArray());
        Log.Info($"racing line: {route.LengthM:F0} m for {profile.DisplayName}, {plan.Arrows.Length} arrowheads, "
                 + $"{plan.AllowedMs.Min():F0} to {plan.AllowedMs.Max():F0} m/s");
        return plan;
    }

    // Room for a stretch, taken the first time one is drawn and kept: KSA never gives it back.
    private static RuntimeMesh? Mesh()
    {
        if (_mesh is not null) return _mesh;
        int vertices = StretchArrows * RaceLine.Vertices, indices = StretchArrows * RaceLine.Indices;
        if (RuntimeMesh.Reserve("KSACars_RaceLine", vertices, indices, out string why) is not { } block
            || (_mesh = RuntimeMesh.Over(block, "KSACars_RaceLine_Mesh", 0, vertices, 0, indices, Materials, out why)) is null)
        {
            Stop($"no room for the racing line: {why}");
        }
        return _mesh;
    }
}
