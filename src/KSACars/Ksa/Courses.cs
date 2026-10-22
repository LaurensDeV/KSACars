using Brutal.Numerics;
using KSA;

namespace KSACars;

/// <summary>
/// A circuit laid where the car being flown is, for a player: the mod's own courses and whatever is
/// in the library, each put with its first point at the car and its first road along the way the
/// car faces, and the car stood on its start.
///
/// <para>The panel only asks. A course is worked out beside the game, which a long one takes a second
/// over, and laid from the frame hook once that is done, since laying writes meshes, which waits for the
/// graphics card.</para>
/// </summary>
internal static class Courses
{
    private const double LiftM = 0.07, SpacingM = 2.0, ClutterMarginM = 1.5;

    private static string? _asked;
    private static bool _takeUp;

    /// <summary>What the last asking came to, for the panel to say.</summary>
    public static string Message { get; private set; } = "";

    /// <summary>The name of the course that is laid, or nothing.</summary>
    public static string? Laid { get; private set; }

    /// <summary>The course that is laid as its file has it, which is the same wherever and whichever way it is laid; null with none laid from a file.</summary>
    public static string? LaidShape { get; private set; }

    /// <summary>Asks for a circuit of the library to be laid at the car being flown.</summary>
    public static void Lay(string name) => (_asked, _takeUp) = (name, false);

    /// <summary>
    /// Asks for a circuit to be laid exactly where it is, on the body the car being flown is on, and
    /// the car left where it stands: for an online guest, laying the course as the host has it laid.
    /// <paramref name="shape"/> is the course as its file has it, which a practised lap is kept by.
    /// </summary>
    public static void LayAsIs(string name, Circuit circuit, string? shape) => (_asIs, _takeUp) = ((name, circuit, shape), false);

    private static (string Name, Circuit Circuit, string? Shape)? _asIs;

    /// <summary>Asks for the roads to be taken up, and what stood under them put back.</summary>
    public static void TakeUp() => (_asked, _takeUp) = (null, true);

    // A course being worked out beside the game: its name, the craft it is for, and the work; Stand is false for one laid as it is.
    private static (string Name, Vehicle Craft, double Lat, double Lon, double Turn, Task<Roads.Prepared> Work, bool Stand, string? Shape)? _coming;

    /// <summary>Whether a course is being worked out or waits to be laid.</summary>
    public static bool Busy => _coming is not null || _asked is not null || _asIs is not null;

    /// <summary>Once a frame, from the frame hook. Never throws.</summary>
    public static void Update()
    {
        try
        {
            if (_takeUp)
            {
                (_asked, _takeUp, _coming) = (null, false, null);
                Roads.Clear();
                (Laid, LaidShape, Message, _asIs) = (null, null, "roads taken up", null);
                return;
            }
            if (_asked is { } name)
            {
                _asked = null;
                Message = Begin(name) is { Length: > 0 } why ? $"not laid: {why}" : $"laying {name}...";
            }
            if (_asIs is { } asIs)
            {
                _asIs = null;
                if (KsaWorld.ControlledVehicle is not { IsDisposed: false } craft || craft.Parent is not Celestial body) Message = "not laid: no car is being flown";
                else
                {
                    _coming = (asIs.Name, craft, 0.0, 0.0, 0.0, Roads.Prepare(body, asIs.Circuit, LiftM, SpacingM), false, asIs.Shape);
                    Message = $"laying {asIs.Name}...";
                }
            }
            if (_coming is not { Work.IsCompleted: true } done) return;
            _coming = null;
            if (!done.Work.IsCompletedSuccessfully) throw done.Work.Exception?.GetBaseException() ?? new InvalidOperationException("the course was not worked out");
            Message = Put(done.Name, done.Craft, done.Lat, done.Lon, done.Turn, done.Work.Result, done.Stand, done.Shape) is { Length: > 0 } not
                ? $"not laid: {not}" : done.Stand ? $"{done.Name} laid: you are on its start" : $"{done.Name} laid";
        }
        catch (Exception e)
        {
            _coming = null;
            Message = $"not laid: {e.GetBaseException().Message}";
            Log.Error("laying a course failed", e);
        }
    }

    /// <summary>Waits for a course that is being worked out and lays it: for the bridge, whose reply says what was laid.</summary>
    public static void Finish()
    {
        try { _coming?.Work.Wait(); }
        catch (AggregateException) { }
        Update();
    }

    // Has the course worked out beside the game, where the car is now; what went wrong, or nothing.
    private static string Begin(string name)
    {
        if (KsaWorld.ControlledVehicle is not { IsDisposed: false } craft || Buggies.Of(craft) is not { } car) return "no car is being flown";
        if (craft.Parent is not Celestial body) return "the car is not on a body";
        if (!KsaWorld.TryCraftSurfacePoint(craft, out _, out double lat, out double lon, out _)) return "where the car is could not be read";
        if (CircuitLibrary.Load(name, out string why) is not { } read) return why;

        // The first road along the way the car faces, so it is on the start and looking down the course.
        double turn = 0.0;
        if (car.PlaceValid)
        {
            double3 up = Vec.Unit(car.PlaceCcf);
            (double3 east, double3 north) = GodView.Compass(up, Vec.Unit(body.GetDirCcfFromLatLon(90.0, 0.0)));
            turn = (Math.Atan2(Vec.Dot(car.AheadCcf, east), Vec.Dot(car.AheadCcf, north)) * 180.0 / Math.PI) - read.StartBearingDeg();
        }
        Circuit here = read.MovedTo(lat, lon, turn, body.MeanRadius, body.Id);
        _coming = (name, craft, lat, lon, turn, Roads.Prepare(body, here, LiftM, SpacingM), true, null);
        return "";
    }

    // Lays what was worked out and stands the car on its start; what went wrong, or nothing.
    private static string Put(string name, Vehicle craft, double lat, double lon, double turn, Roads.Prepared prepared, bool stand, string? shape)
    {
        if (craft.IsDisposed || Buggies.Of(craft) is not { } car) return "the car is gone";

        Roads.Lay(prepared.Body, prepared.Circuit, LiftM, SpacingM, whole: true, prepared: prepared);
        Roads.ClearClutter(ClutterMarginM);
        if (!Roads.Any) return "the roads could not be laid here";
        Laid = name;
        // The course as its file has it, wherever it is laid: what a lap practised on it is kept by.
        LaidShape = shape ?? CircuitLibrary.Load(name, out _)?.ToJson();
        if (!stand)
        {
            Log.Info($"course: {name} laid as it was given, on {prepared.Body.Id}");
            return "";
        }

        BuggyProfile profile = car.Drive.Profile;
        if (Roads.RouteOver(null, 0.0, Autopilot.TurnRadius(profile), out _, out RoadSurface? surface, out _) is { } route && surface is not null)
        {
            // In the pole's box of the grid painted there.
            Route.Sample pole = route.Samples[route.IndexAt(StartGrid.PoleM)];
            Laps.Place(craft, profile, route, surface, StartGrid.PoleM, pole.CentreM + StartGrid.Column(0, pole.HalfWidth));
        }
        Log.Info($"course: {name} laid at {lat:F5}, {lon:F5} on {prepared.Body.Id}, turned {turn:F0} deg");
        return "";
    }
}
