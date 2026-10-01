using System.Reflection;
using Brutal.Numerics;
using KSA;
using KSA.Rendering.Water.Data;

namespace KSACars;

/// <summary>
/// Every direct touch of KSA's internals lives here. KSA is pre-release and its API moves, so keeping
/// the surface in one file means a game update breaks one place, not ten.
///
/// All positions and velocities are in the ecliptic frame (Ecl): inertial, metres, and the frame
/// <see cref="Vehicle.GetPositionEcl"/> answers in.
/// </summary>
internal static class KsaWorld
{
    /// <summary>Earth's sea-level air (kg/m^3), the one reference every density ratio here is a multiple of.</summary>
    public const double ReferenceAirDensityKgPerM3 = 1.225;

    // A craft's engine switch and its held throttle keys are private to it, and KSA has no accessor for
    // either. Null when KSA has renamed them, and then a car's rockets cannot be lit from the keys.
    private static readonly FieldInfo? ManualInputs =
        typeof(Vehicle).GetField("_manualControlInputs", BindingFlags.NonPublic | BindingFlags.Instance) is { } f
        && f.FieldType == typeof(ManualControlInputs) ? f : null;

    private static readonly FieldInfo? EngineKeys =
        typeof(Vehicle).GetField("_engineFlags", BindingFlags.NonPublic | BindingFlags.Instance) is { } f
        && f.FieldType == typeof(EngineFlags) ? f : null;

    /// <summary>Whether the engine keys can be read off a craft at all.</summary>
    public static bool EngineControlsReachable => ManualInputs is not null && EngineKeys is not null;

    /// <summary>Whether a craft's engines are switched on, as the engine start and shutdown keys leave it.</summary>
    public static bool? EngineOn(Vehicle craft)
    {
        try { return ManualInputs?.GetValue(craft) is ManualControlInputs inputs ? inputs.EngineOn : null; }
        catch { return null; }
    }

    /// <summary>Switches a craft's engines on or off, as the engine start and shutdown keys do.</summary>
    public static bool TrySetEngineOn(Vehicle craft, bool on)
    {
        try
        {
            if (ManualInputs?.GetValue(craft) is not ManualControlInputs inputs) return false;
            inputs.EngineOn = on;
            ManualInputs.SetValue(craft, inputs);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Puts a throttle on a craft's own throttle gauge. KSA reads the gauge after the GUI pass and
    /// clamps the value back in the next physics window, so this is written every frame and never read.
    /// </summary>
    public static void ShowThrottle(Vehicle craft, double throttle)
    {
        try
        {
            if (ManualInputs?.GetValue(craft) is not ManualControlInputs inputs) return;
            inputs.EngineThrottle = (float)Math.Clamp(throttle, 0.0, 1.0);
            ManualInputs.SetValue(craft, inputs);
        }
        catch
        {
            // The gauge reads full, as it does on any craft with no engine.
        }
    }

    /// <summary>Which of the throttle keys are held on a craft.</summary>
    public static (bool Up, bool Down) ThrottleKeys(Vehicle craft)
    {
        try
        {
            return EngineKeys?.GetValue(craft) is EngineFlags flags
                ? ((flags & EngineFlags.ThrottleUp) != 0, (flags & EngineFlags.ThrottleDown) != 0)
                : (false, false);
        }
        catch
        {
            return (false, false);
        }
    }

    // The field of view KSA's main camera starts with, for when it cannot be read.
    private const double DefaultFovDeg = 50.0;

    // Below this KSA names the speed "paused" -- its SimSpeed constructor calls anything under 1e-4
    // paused -- while Universe.IsPaused() tests against exactly zero, so the world would keep running
    // under a label saying it had stopped.
    private const double SlowestSimSpeed = 0.001;

    /// <summary>The vehicle the player is currently flying, or null in menus.</summary>
    public static Vehicle? ControlledVehicle => Program.ControlledVehicle;

    /// <summary>
    /// The player has a craft to fly. Not "the flight scene is up": <c>Universe.DestroyVehicle</c>
    /// clears <c>ControlledVehicle</c> when the craft being flown is destroyed and the scene carries on,
    /// so anything about the scene asks <see cref="InFlightScene"/>.
    /// </summary>
    public static bool InFlight => Program.ControlledVehicle is { IsDisposed: false };

    /// <summary>The flight scene is up, whether or not the player has a craft in it.</summary>
    public static bool InFlightScene
    {
        get
        {
            try
            {
                return Program.Editor is null && Universe.CurrentSystem is not null;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// The simulated seconds KSA's last step actually advanced the world by: zero while paused, the
    /// real span under warp. The bridge's <c>step</c> counts these, never the player's clock.
    /// </summary>
    public static double SimStepSeconds => Universe.GetLastSimStep().DeltaTime;

    /// <summary>True while the simulation is stopped. KSA defines this as speed exactly zero.</summary>
    public static bool IsPaused => Universe.IsPaused();

    /// <summary>Current timewarp factor; 1.0 is real time, 0.0 is paused.</summary>
    public static double SimulationSpeed => Universe.SimulationSpeed;

    /// <summary>
    /// Sets the world's simulation speed, including values slower than the in-game controls reach.
    /// <c>SetSimulationSpeed</c> rejects only speeds above <c>SimSpeed.MaxSpeed</c> and assigns the field
    /// directly, so a value set here holds until something else changes it.
    /// </summary>
    /// <returns>False if the value was not finite or not positive; the speed is left alone.</returns>
    public static bool SetSimulationSpeed(double speed)
    {
        if (!double.IsFinite(speed) || speed <= 0.0) return false;

        Universe.SetSimulationSpeed(new SimSpeed(Math.Max(speed, SlowestSimSpeed)));
        return true;
    }

    /// <summary>
    /// Stops the world, or starts it again at real time. Separate from <see cref="SetSimulationSpeed"/>,
    /// which refuses zero: a slow world and a stopped one are different requests.
    /// </summary>
    /// <returns>False only if the call threw.</returns>
    public static bool SetPaused(bool paused)
    {
        try
        {
            Universe.SetSimulationSpeed(new SimSpeed(paused ? 0.0 : 1.0));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>False once the vehicle has been destroyed or unloaded.</summary>
    public static bool IsAlive(Vehicle? v) => v is { IsDisposed: false };

    /// <summary>
    /// Every live vehicle in the world, built at most once a frame and shared by everything that walks
    /// it. Freshness is a generation rather than a frame count, because a destroyed vehicle stays in a
    /// held list as a disposed reference: each pass invalidates it on the way in.
    /// </summary>
    public static IReadOnlyList<Vehicle> Vehicles
    {
        get
        {
            if (_censusFresh) return _census;

            CollectVehicles(_census);
            _censusFresh = true;
            return _census;
        }
    }

    /// <summary>Throws the shared census away. Called at the top of each pass.</summary>
    public static void InvalidateCensus() => _censusFresh = false;

    private static readonly List<Vehicle> _census = [];

    private static bool _censusFresh;

    // Universe.CurrentSystem.All rather than Program.VehiclesInFrame: the latter is a per-frame scratch
    // buffer refilled at a point in the tick a StarMap hook does not line up with, and reads back empty
    // from there. The system's collection is valid from any hook.
    private static void CollectVehicles(List<Vehicle> into)
    {
        into.Clear();

        try
        {
            if (Universe.CurrentSystem is { } system)
            {
                ReadOnlySpan<Astronomical> all = system.All.AsSpan();
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] is Vehicle { IsDisposed: false } v) into.Add(v);
                }
            }
        }
        catch (Exception e)
        {
            Log.Warn($"vehicle enumeration failed: {e.Message}");
        }

        if (into.Count != 0) return;

        try
        {
            ReadOnlySpan<Vehicle> inFrame = Program.VehiclesInFrame;
            for (int i = 0; i < inFrame.Length; i++)
            {
                if (inFrame[i] is { IsDisposed: false } v) into.Add(v);
            }
        }
        catch
        {
            // Nothing more to try.
        }
    }

    public static double3 PositionEcl(Vehicle v) => v.GetPositionEcl();

    public static double3 VelocityEcl(Vehicle v) => v.GetVelocityEcl();

    /// <summary>
    /// Sets a craft down at a latitude and longitude on the named body. <c>Vehicle.TeleportToLocation</c>
    /// builds the kinematic state from the craft's own bounding box, so it arrives upright and resting
    /// on the ground rather than with its origin there.
    /// </summary>
    public static bool TryPlaceOnSurface(Vehicle craft, string bodyName,
                                         double latitudeDeg, double longitudeDeg)
    {
        if (!IsAlive(craft)) return false;
        if (!double.IsFinite(latitudeDeg) || !double.IsFinite(longitudeDeg)) return false;

        try
        {
            if (Universe.CurrentSystem is not { } system) return false;

            for (int i = 0; i < system.Count; i++)
            {
                if (system.GetIndex(i) is not Celestial body) continue;
                if (body.Id != bodyName) continue;

                craft.TeleportToLocation(body, latitudeDeg, longitudeDeg);
                return true;
            }

            return false;
        }
        catch (Exception e)
        {
            Log.Warn($"could not place {DisplayName(craft)}: {e.Message}");
            return false;
        }
    }

    private static ScreenshotCapture? _shots;

    private static bool _lookedForShots;

    private static bool _warnedAboutShots;

    /// <summary>
    /// Asks the game to save a screenshot of its own framebuffer, into
    /// <c>Documents/exports/screenshots/</c>. It needs no window focus, cannot photograph somebody's
    /// desktop, and leaves the UI out unless asked.
    ///
    /// <para><c>ScreenshotCapture.Request</c> is public and <c>Program._screenshotCapture</c> is not, so
    /// it is reflected once, as the game's own <c>screenshot</c> command reaches it. A KSA rename turns
    /// this off rather than breaking anything.</para>
    /// </summary>
    public static bool TryRequestScreenshot(int scale = 1, string flags = "")
    {
        try
        {
            if (!_lookedForShots)
            {
                _lookedForShots = true;
                _shots = typeof(Program)
                         .GetField("_screenshotCapture", BindingFlags.NonPublic | BindingFlags.Instance)
                         ?.GetValue(Program.Instance) as ScreenshotCapture;

                if (_shots is null && !_warnedAboutShots)
                {
                    _warnedAboutShots = true;
                    Log.Warn("screenshots: Program._screenshotCapture did not resolve; "
                             + "scripted captures will write nothing");
                }
            }

            if (_shots is null) return false;

            // A capture already running is dropped by the engine with its own warning, so asking
            // again while one is in flight costs nothing and loses only that frame.
            _shots.Request(scale, flags);
            return true;
        }
        catch (Exception e)
        {
            if (!_warnedAboutShots)
            {
                _warnedAboutShots = true;
                Log.Warn($"screenshots: could not ask for one: {e.Message}");
            }
            return false;
        }
    }

    /// <summary>
    /// The sea's level against the mean sphere, on a body that has one. A body with no ocean hands back
    /// no reference; never its <c>IsValid()</c>, which tests a level of 0 m against a 100 km bar and is
    /// false wherever there is water.
    /// </summary>
    public static bool TrySeaLevel(Celestial body, out double level)
    {
        level = 0.0;

        try
        {
            if (body.GetOceanReference() is not { } sea || !(sea.Density > 0.0)) return false;

            level = sea.Level;
            return double.IsFinite(level);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// How high the star stands over the horizon at a place, in degrees; negative is below it. What a
    /// dark capture is checked against before anything is called broken.
    /// </summary>
    public static double SunElevationDeg(Celestial body, double3 positionEcl)
    {
        try
        {
            if (!TryStarPositionEcl(out double3 starEcl)) return double.NaN;

            double3 up = Vec.Unit(positionEcl - body.GetPositionEcl());
            double3 toSun = Vec.Unit(starEcl - positionEcl);
            if (!Vec.IsFinite(up) || !Vec.IsFinite(toSun)) return double.NaN;

            return Math.Asin(Math.Clamp(Vec.Dot(up, toSun), -1.0, 1.0)) * 180.0 / Math.PI;
        }
        catch
        {
            return double.NaN;
        }
    }

    // The system's StellarBody rather than a name: the type is the system's own answer to which body is
    // the sun.
    private static bool TryStarPositionEcl(out double3 positionEcl)
    {
        positionEcl = default;

        try
        {
            if (Universe.CurrentSystem is not { } system) return false;

            for (int i = 0; i < system.Count; i++)
            {
                if (system.GetIndex(i) is not StellarBody star) continue;

                positionEcl = star.GetPositionEcl();

                return Vec.IsFinite(positionEcl);
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public static string DisplayName(Vehicle v)
    {
        try { return string.IsNullOrEmpty(v.Id) ? "unnamed" : v.Id; }
        catch { return "unnamed"; }
    }

    /// <summary>
    /// Local "up" at a craft: the radial-out direction from the body it is bound to. Falls back to its
    /// velocity direction, and finally to +Z, so the caller always gets a unit vector.
    /// </summary>
    public static double3 LocalUp(Vehicle craft)
    {
        try
        {
            if (craft.Parent is IPosition parent)
            {
                double3 up = Vec.Unit(craft.GetPositionEcl() - parent.GetPositionEcl());
                if (!up.Equals(Vec.Zero)) return up;
            }
        }
        catch
        {
            // Parent can be null or mid-transition during scene changes; fall through.
        }

        double3 alongTrack = Vec.Unit(craft.GetVelocityEcl());
        return alongTrack.Equals(Vec.Zero) ? new double3(0, 0, 1) : alongTrack;
    }

    /// <summary>The body a craft is at, or null in deep space.</summary>
    public static Celestial? ParentBody(Vehicle craft)
    {
        try
        {
            return craft.Parent as Celestial;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// How fast the ground under a point is moving, in the ecliptic frame: the body's own motion plus
    /// its spin at that radius. A craft's speed over the ground is its velocity less this.
    /// </summary>
    public static double3 GroundVelocityAt(Vehicle craft, double3 positionEcl)
    {
        try
        {
            if (craft.Parent is not Celestial body) return SafeVelocityEcl(craft);

            double3 ground = GroundVelocityAt(body, positionEcl);

            return Vec.IsFinite(ground) ? ground : SafeVelocityEcl(craft);
        }
        catch
        {
            return SafeVelocityEcl(craft);
        }
    }

    private static double3 GroundVelocityAt(Celestial body, double3 positionEcl)
    {
        try
        {
            // Cce, not the Cci that GetBodyRates answers with: the separation below is a Cce vector, and
            // the two frames differ by the body's axial tilt -- 23.4 degrees on Earth, which at 465 m/s of
            // surface speed invents up to 190 m/s of velocity out of nothing.
            double3 spin = ((IParentBody)body).GetAngularVelocityCce();
            double3 fromCentre = positionEcl - body.GetPositionEcl();

            // Without the spin the answer is wrong by at most the surface speed; zero would be wrong by
            // the whole orbital velocity.
            if (!Vec.IsFinite(spin) || !Vec.IsFinite(fromCentre)) return body.GetVelocityEcl();

            return body.GetVelocityEcl() + Vec.Cross(spin, fromCentre);
        }
        catch
        {
            return Vec.Zero;
        }
    }

    // Called from catch blocks, so it may not throw: GetVelocityEcl does on a craft just disposed.
    private static double3 SafeVelocityEcl(Vehicle craft)
    {
        try
        {
            return VelocityEcl(craft);
        }
        catch
        {
            return Vec.Zero;
        }
    }

    /// <summary>
    /// Gravitational acceleration at <paramref name="positionEcl"/> from the craft's parent body, in Ecl.
    /// Zero if the parent or its gravity parameter is unavailable.
    /// </summary>
    public static double3 GravityAt(Vehicle craft, double3 positionEcl)
    {
        try
        {
            if (craft.Parent is not Celestial body) return Vec.Zero;

            double mu = ((IParentBody)body).Mu;
            double3 toCentre = body.GetPositionEcl() - positionEcl;
            double dist2 = Vec.Len2(toCentre);
            return mu > 0.0 && dist2 >= 1.0 ? Vec.Unit(toCentre) * (mu / dist2) : Vec.Zero;
        }
        catch
        {
            return Vec.Zero;
        }
    }

    /// <summary>
    /// The density of the air at a point, as a multiple of <see cref="ReferenceAirDensityKgPerM3"/> --
    /// Earth's sea-level air, whatever body this is, so a car's drag is the air actually there.
    ///
    /// <para><b>A body with no atmosphere reads 0.0, and that is an answer rather than a failure</b>:
    /// every airless body hands back no reference. The fallback when the air genuinely cannot be read
    /// -- a throw, or no body under the craft -- is 1.0, a car that keeps its drag being a far less
    /// confusing failure than one that silently loses it.</para>
    /// </summary>
    public static double AirDensityRatioAt(Vehicle craft, double3 positionEcl)
    {
        try
        {
            if (craft.Parent is not Celestial body) return 1.0;

            double altitude = Vec.Len(positionEcl - body.GetPositionEcl()) - body.MeanRadius;

            // Never gate on KSA's own IsValid(): DistanceReference.IsValid requires over 100 km, and the
            // atmosphere's applies it to the scale height, 8 km on Earth, so it is false for every real
            // atmosphere. Check the terms this divides by instead.
            AtmosphereReference? atmosphere = body.GetAtmosphereReference();
            if (atmosphere?.Physical is not { } air) return 0.0;

            double seaLevel = air.SeaLevelDensity;
            double scaleHeight = air.ScaleHeight.InMeters();
            if (!(seaLevel > 0.0) || !(scaleHeight > 0.0)) return 0.0;

            if (altitude < 0.0) altitude = 0.0;
            if (altitude >= air.Height) return 0.0;

            double ratio = air.GetAtmosphericDensityAtAltitude(altitude) / ReferenceAirDensityKgPerM3;
            return double.IsFinite(ratio) && ratio >= 0.0 ? ratio : 1.0;
        }
        catch
        {
            return 1.0;
        }
    }

    /// <summary>The main view's own field of view (deg), or the engine's default if unreadable.</summary>
    public static double MainViewFovDeg()
    {
        try
        {
            if (Program.MainViewport?.GetCamera() is not { } camera) return DefaultFovDeg;

            double degrees = double.RadiansToDegrees(camera.GetFieldOfView());

            return double.IsFinite(degrees) && degrees > 0.0 && degrees < 180.0
                ? degrees
                : DefaultFovDeg;
        }
        catch
        {
            return DefaultFovDeg;
        }
    }

    /// <summary>
    /// Points the camera at a craft and takes control of it: follow, control, match the zoom. Fewer
    /// leaves the camera watching one craft while the controls drive another.
    ///
    /// <para>It does not rebuild the vehicle's derived data, and nor does the engine when it switches
    /// craft: the rebuild reaches the shapes registry, which the vehicle worker holds for its whole run.</para>
    /// </summary>
    /// <returns>False if the craft is gone, or the engine refused any part of it.</returns>
    public static bool GoTo(Vehicle? vehicle)
    {
        if (!IsAlive(vehicle)) return false;

        try
        {
            Camera? camera = Program.GetMainCamera();
            if (camera is null) return false;

            camera.SetFollow(vehicle!, tidalLocking: true);
            Program.ControlledVehicle = vehicle;
            Program.MainViewport.OrbitController.DistancePower = vehicle!.OrbitView.DistancePower;
            return true;
        }
        catch (Exception e)
        {
            Log.Warn($"could not go to {DisplayName(vehicle!)}: {e.Message}");
            return false;
        }
    }

    /// <summary>
    /// Moves the main orbit camera to a pair of angles. Written on the followed craft's stored view: the
    /// controller's own angles are sprung back towards that every frame.
    /// </summary>
    public static bool TryWriteMainOrbit(double azimuth, double elevation)
    {
        if (!double.IsFinite(azimuth) || !double.IsFinite(elevation)) return false;

        try
        {
            if (Program.GetMainCamera()?.Following?.OrbitView is not { } view) return false;

            view.Azimuth = azimuth;

            // The game clamps it on every path that writes it, so a value past the pole would be one
            // this camera can never report back and the write would look refused forever.
            view.Elevation = Math.Clamp(elevation, -Math.PI / 2.0, Math.PI / 2.0);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
