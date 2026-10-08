using Brutal.Numerics;
using KSA;

namespace KSACars;

/// <summary>
/// A circuit laid where the car being flown is, for a player: the mod's own courses and whatever is
/// in the library, each put with its first point at the car and its first road along the way the
/// car faces, and the car stood on its start.
///
/// <para>The panel only asks. The laying is done from the frame hook, since it writes meshes, which
/// waits for the graphics card.</para>
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

    /// <summary>Asks for a circuit of the library to be laid at the car being flown.</summary>
    public static void Lay(string name) => (_asked, _takeUp) = (name, false);

    /// <summary>Asks for the roads to be taken up, and what stood under them put back.</summary>
    public static void TakeUp() => (_asked, _takeUp) = (null, true);

    /// <summary>Once a frame, from the frame hook. Never throws.</summary>
    public static void Update()
    {
        if (_asked is null && !_takeUp) return;
        (string? name, bool takeUp) = (_asked, _takeUp);
        (_asked, _takeUp) = (null, false);
        try
        {
            if (takeUp)
            {
                Roads.Clear();
                (Laid, Message) = (null, "roads taken up");
                return;
            }
            Message = Put(name!) is { Length: > 0 } why ? $"not laid: {why}" : $"{name} laid: you are on its start";
        }
        catch (Exception e)
        {
            Message = $"not laid: {e.GetBaseException().Message}";
            Log.Error("laying a course failed", e);
        }
    }

    // Lays it and stands the car on its start; what went wrong, or nothing.
    private static string Put(string name)
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

        Roads.Lay(body, here, LiftM, SpacingM, whole: true);
        Roads.ClearClutter(ClutterMarginM);
        if (!Roads.Any) return "the roads could not be laid here";
        Laid = name;

        BuggyProfile profile = car.Drive.Profile;
        if (Roads.RouteOver(null, 0.0, Autopilot.TurnRadius(profile), out _, out RoadSurface? surface, out why) is { } route && surface is not null)
        {
            Laps.Place(craft, profile, route, surface);
        }
        Log.Info($"course: {name} laid at {lat:F5}, {lon:F5} on {body.Id}, turned {turn:F0} deg");
        return "";
    }
}
