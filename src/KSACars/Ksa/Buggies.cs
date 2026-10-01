using BepuUtilities;
using Brutal.Numerics;
using KSA;

namespace KSACars;

/// <summary>
/// Every buggy in the world: its springs and tyres stepped inside the engine's physics window, and its
/// wheels, arms and coil-overs posed to match.
///
/// <para>The ground is read off the craft's physics state in the planet-fixed frame rather than its
/// analytic position, which on a landed craft differs by metres: a spring measured in centimetres
/// against the wrong one of those would throw the car off the planet.</para>
/// </summary>
internal sealed class Buggies
{
    internal sealed class Entry(Vehicle craft, Part part, BuggyProfile profile)
    {
        public Vehicle Craft { get; } = craft;
        public Part Part { get; } = part;
        public BuggyDrive Drive { get; } = new(profile);
        public Part?[] Wheels { get; } = new Part?[profile.Corners.Length];
        public Part?[] Arms { get; } = new Part?[profile.Corners.Length];
        public Part?[] Coils { get; } = new Part?[profile.Corners.Length];
        public Part? Steering { get; set; }
        public DriveInput Input { get; set; }
        public bool Stepped { get; set; }
        public bool Railed { get; set; }
        public double[] HubHeights { get; } = new double[profile.Corners.Length];
        public double Level { get; set; } = 1.0;
        public double SinceLog { get; set; }
        public KittenRenderable? HandsOn { get; set; }
        public DriverHands? Hands { get; set; }
        public bool Scraping { get; set; }
        public double Lift { get; set; }
        public double YawRate { get; set; }
        public double SideSpeed { get; set; }
    }

    // Inputs held by the bridge for so many simulated seconds, ahead of the keys.
    private static readonly Dictionary<Vehicle, (DriveInput Input, double Seconds)> Held = [];

    // Read by the physics prefix, which has only the vehicle to go on.
    private static readonly Dictionary<Vehicle, Entry> Active = [];

    private static bool _complained;

    public IEnumerable<Entry> All => Active.Values;

    public void Sync(IReadOnlyList<Vehicle> vehicles)
    {
        for (int i = 0; i < vehicles.Count; i++)
        {
            Vehicle v = vehicles[i];
            if (!KsaWorld.IsAlive(v) || Active.ContainsKey(v)) continue;
            if (Find(v) is not { } found) continue;

            Entry entry = new(v, found.Part, found.Profile);
            Resolve(entry);
            Active[v] = entry;
            Log.Info($"{found.Profile.DisplayName} on {KsaWorld.DisplayName(v)}: "
                     + $"{entry.Wheels.Count(w => w is not null)} wheel(s) found");
        }

        List<Vehicle>? gone = null;
        foreach ((Vehicle v, Entry e) in Active)
        {
            if (!KsaWorld.IsAlive(v) || Find(v)?.Part != e.Part) (gone ??= []).Add(v);
        }
        if (gone is not null) foreach (Vehicle v in gone) Active.Remove(v);
    }

    public void Clear()
    {
        Active.Clear();
        Held.Clear();
    }

    /// <summary>Holds a buggy's throttle and steering for so many simulated seconds, whoever is flying.</summary>
    public static bool Hold(Vehicle craft, DriveInput input, double seconds)
    {
        if (!Active.ContainsKey(craft)) return false;
        if (seconds > 0.0) Held[craft] = (input, seconds);
        else Held.Remove(craft);
        return true;
    }

    /// <summary>What a buggy is doing, for the bridge; null when the craft is not one.</summary>
    public static Dictionary<string, object?>? Report(Vehicle craft)
    {
        if (!Active.TryGetValue(craft, out Entry? e)) return null;
        BuggyDrive d = e.Drive;
        return new()
        {
            ["speed_ms"] = Math.Round(d.ForwardSpeed, 2),
            ["rpm"] = Math.Round(d.Rpm),
            ["gear"] = d.Gear + 1,
            ["steer_deg"] = Math.Round(d.SteerAngle * 180.0 / Math.PI, 1),
            ["throttle"] = e.Input.Throttle,
            ["steer_input"] = e.Input.Steer,
            ["held_s"] = Held.TryGetValue(craft, out var h) ? Math.Round(h.Seconds, 2) : 0.0,
            ["travel_m"] = d.Travel.Select(t => Math.Round(t, 3)).ToArray(),
            ["grounded"] = d.Grounded.ToArray(),
            ["hub_height_m"] = e.HubHeights.Select(t => Math.Round(t, 3)).ToArray(),
            ["level"] = Math.Round(e.Level, 4),
            ["on_rails"] = e.Railed,
            ["scraping"] = e.Scraping,
            ["yaw_dps"] = Math.Round(e.YawRate * 180.0 / Math.PI, 1),
            ["sideways_ms"] = Math.Round(e.SideSpeed, 2),
            ["stepped"] = e.Stepped,
            ["wheels_found"] = e.Wheels.Count(w => w is not null),
            ["seats"] = craft.SeatCount,
            ["crew"] = SeatedCount(craft),
            ["crew_names"] = CrewNames(craft),
            ["hands_solves"] = e.Hands?.Solves ?? 0,
            ["orbit_off_m"] = OrbitOffPhysics(craft, out double orbitOffMs) is double off ? Math.Round(off, 2) : -1.0,
            ["orbit_off_ms"] = Math.Round(orbitOffMs, 3),
            ["hands_miss_cm"] = e.Hands is { } h2 ? Math.Round(h2.MissCm, 2) : -1.0,
        };
    }

    /// <summary>
    /// Every buggy's crew but the flown craft's, whose KSA has just drawn: the same call, from the same
    /// camera, with the same player step.
    /// </summary>
    public static void DrawCrewOfUnflown(IViewport viewport, int inFrameIndex)
    {
        if (Active.Count == 0) return;
        Vehicle? flown = KsaWorld.ControlledVehicle;
        double dt = Program.GetPlayerDeltaTime();
        foreach (Entry e in Active.Values)
        {
            if (ReferenceEquals(e.Craft, flown) || !KsaWorld.IsAlive(e.Craft)) continue;

            double4x4 asmb2Ego = e.Craft.GetMatrixAsmb2Ego(viewport.GetCamera());
            foreach (IVASeat seat in e.Craft.Crew)
            {
                seat.UpdateSeatedRenderable(viewport, inFrameIndex, dt, in asmb2Ego);
            }
        }
    }

    /// <summary>The driver's seat of a buggy, for the bridge to get a kitten out of.</summary>
    public static IVASeat? DriverSeatOf(Vehicle craft) => Active.TryGetValue(craft, out Entry? e) ? DriverSeat(e) : null;

    // How far the craft's orbit, which KSA spawns an EVA kitten from, is from where the physics has it.
    private static double? OrbitOffPhysics(Vehicle craft, out double velocityOff)
    {
        velocityOff = -1.0;
        try
        {
            if (!craft.HasPhysicsBubble) return null;
            PhysicsStates states = craft.GetPhysicsStatesMutable();
            PhysicsStates.GetStatesCci(in states.Origin, in states.Kinematic, out double3 posCci, out double3 velCci, out _);
            StateVectors sv = craft.Orbit.StateVectors;
            velocityOff = Vec.Len(sv.VelocityCci - velCci);
            return Vec.Len(sv.PositionCci - posCci);
        }
        catch
        {
            return null;
        }
    }

    private static string[] CrewNames(Vehicle craft)
    {
        List<string> names = [];
        foreach (IVASeat seat in craft.Parts.Modules.Get<IVASeat>())
        {
            if (Universe.KittenRoster.Find(seat.AssignedKittenHash) is { } k) names.Add(k.Name);
        }
        return [.. names];
    }

    private static int SeatedCount(Vehicle craft)
    {
        int n = 0;
        foreach (IVASeat seat in craft.Parts.Modules.Get<IVASeat>())
        {
            if (seat.AssignedKittenHash != KeyHash.Zero) n++;
        }
        return n;
    }

    private static (Part Part, BuggyProfile Profile)? Find(Vehicle v)
    {
        try
        {
            ReadOnlySpan<Part> parts = v.Parts.Parts;
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] is not { } part) continue;
                foreach (BuggyProfile profile in BuggyProfile.All)
                {
                    if (part.Id == profile.PartId) return (part, profile);
                }
            }
        }
        catch
        {
            // Part tree can be mid-rebuild during staging or docking.
        }
        return null;
    }

    private static void Resolve(Entry e)
    {
        BuggyProfile p = e.Drive.Profile;
        for (int i = 0; i < p.Corners.Length; i++)
        {
            e.Wheels[i] = SubPart(e.Part, p.SubpartPrefix + "Wheel" + p.Corners[i].Key);
            e.Arms[i] = SubPart(e.Part, p.SubpartPrefix + "Arm" + p.Corners[i].Key);
            e.Coils[i] = SubPart(e.Part, p.SubpartPrefix + "Coil" + p.Corners[i].Key);
        }
        e.Steering = SubPart(e.Part, p.SubpartPrefix + "Steering");
    }

    // Matched on the Id the <Part> gives the subpart, which ends in the corner's key.
    private static Part? SubPart(Part part, string suffix)
    {
        try
        {
            ReadOnlySpan<Part> subs = part.SubParts;
            for (int i = 0; i < subs.Length; i++)
            {
                if (subs[i] is { } sub && sub.Id is { } id && id.EndsWith(suffix, StringComparison.Ordinal)) return sub;
            }
        }
        catch
        {
            // Unresolved subparts are simply not posed.
        }
        return null;
    }

    /// <summary>
    /// The springs, tyres and engine for one step. Called from <see cref="AttitudeHook"/>'s prefix, the
    /// one window where a velocity written is not overwritten before anything reads it.
    /// </summary>
    public static void Physics(Vehicle craft, double dt)
    {
        if (Active.Count == 0 || !Active.TryGetValue(craft, out Entry? e)) return;

        try
        {
            Step(e, dt);
        }
        catch (Exception ex)
        {
            Active.Remove(craft);
            if (_complained) return;
            _complained = true;
            Log.Error($"buggy physics failed on {KsaWorld.DisplayName(craft)}; it is no longer driven", ex);
        }
    }

    private static void Step(Entry e, double dt)
    {
        Vehicle craft = e.Craft;
        e.Input = ReadInput(craft);
        if (Held.TryGetValue(craft, out (DriveInput Input, double Seconds) held))
        {
            e.Input = held.Input;
            if (held.Seconds - dt <= 0.0) Held.Remove(craft);
            else Held[craft] = (held.Input, held.Seconds - dt);
        }

        // The engine integrates a frame as one impulse followed by its own sub-steps, so a spring is
        // only as stable as the frame is short. Under warp the car is left to its colliders.
        if (!(dt > 0.0) || dt > 0.1) return;
        if (!craft.HasPhysicsBubble || craft.Parent is not Celestial body) return;

        bool railed = craft.Situation.IsOnRails();
        e.Railed = railed;
        bool idle = e.Input.Throttle == 0.0 && e.Input.Steer == 0.0;

        // KSA only rails a car that has stood still, so one on rails with nobody at the wheel is parked:
        // it is exactly where it was, and costs nothing. Its speed from before it stopped says nothing.
        if (railed && idle) return;

        PhysicsStates states = craft.GetPhysicsStatesMutable();

        PhysicsStates.GetStatesCcf(in states.Origin, in states.Kinematic,
                                   out double3 positionCcf, out double3 velocityCcf, out doubleQuat body2Ccf);
        doubleQuat ccf2Body = body2Ccf.Inverse();
        doubleQuat body2Phys = states.Kinematic.Body2Phys;
        double3 spinBody = states.Kinematic.AngularVelocityPhys.Transform(body2Phys.Inverse());
        double3 velocityBody = velocityCcf.Transform(ccf2Body);

        double3 com = craft.CenterOfMassAsmb;
        doubleQuat part2Asmb = e.Part.Asmb2VehicleAsmb;
        double3 partOrigin = e.Part.PositionVehicleAsmb;
        double3 up = part2Asmb * new double3(1, 0, 0);
        double3 forward = part2Asmb * new double3(0, 1, 0);

        doubleQuat ccf2Cce = body.GetCcf2Cce();
        TerrainHeights ground = new(body, accurate: true);

        BuggyCorner[] corners = e.Drive.Profile.Corners;
        Span<WheelContact> contacts = stackalloc WheelContact[corners.Length];
        Span<double3> hubs = stackalloc double3[corners.Length];
        for (int i = 0; i < corners.Length; i++)
        {
            double3 hub = partOrigin + (part2Asmb * corners[i].Hub) - com;
            hubs[i] = hub;

            double3 atCcf = positionCcf + hub.Transform(body2Ccf);
            double radius = Vec.Len(atCcf);
            if (!(radius > 0.0)) continue;
            double3 dirCcf = atCcf / radius;
            if (!ground.TryHeight(dirCcf.Transform(ccf2Cce), out double height)) continue;

            e.HubHeights[i] = radius - (body.MeanRadius + height);
            contacts[i] = new WheelContact(
                Valid: true,
                HubHeight: radius - (body.MeanRadius + height),
                GroundUp: dirCcf.Transform(ccf2Body),
                HubVelocity: velocityBody + Vec.Cross(spinBody, hub));
        }

        double mass = craft.TotalMass;
        double gravity = Vec.Len(KsaWorld.GravityAt(craft, KsaWorld.PositionEcl(craft)));
        double air = KsaWorld.ReferenceAirDensityKgPerM3 * KsaWorld.AirDensityRatioAt(craft, KsaWorld.PositionEcl(craft));

        DriveImpulse impulse = e.Drive.Step(e.Input, contacts, hubs, up, forward, mass, gravity, air, dt);
        e.Stepped = true;
        e.Level = contacts[0].Valid ? Vec.Dot(up, contacts[0].GroundUp) : e.Level;
        e.YawRate = Vec.Dot(spinBody, up);
        e.Lift = Vec.Dot(impulse.Linear, up) / (mass * gravity * dt);
        e.SideSpeed = Vec.Dot(velocityBody, Vec.Cross(up, forward));
        // Whether any collider is on the ground: the tyres are not colliders, so this is the hull scraping.
        try { e.Scraping = craft.Situation.HasTerrainContact(); } catch { e.Scraping = false; }
        LogWhileDriven(e, dt);

        double3 dv = impulse.Linear / mass;
        Symmetric3x3 inverse = Symmetric3x3.Invert(craft.TotalMassPropsBody.Inertia);
        double3 l = impulse.Angular;
        double3 dw = new((inverse.XX * l.X) + (inverse.YX * l.Y) + (inverse.ZX * l.Z),
                         (inverse.YX * l.X) + (inverse.YY * l.Y) + (inverse.ZY * l.Z),
                         (inverse.ZX * l.X) + (inverse.ZY * l.Y) + (inverse.ZZ * l.Z));
        if (!Vec.IsFinite(dv) || !Vec.IsFinite(dw)) return;

        states.Kinematic.VelocityPhys += dv.Transform(body2Phys);
        states.Kinematic.AngularVelocityPhys += dw.Transform(body2Phys);

        // Woken the way KSA wakes a landed craft something has bumped into. Not rebuilt from its orbit,
        // as a craft in flight is: a landed craft on rails is held to the ground, not to that orbit, and
        // rebuilt from it the car is left standing still in space while the planet turns away at 400 m/s.
        if (railed) craft.TakeOffRails();
    }

    // Once a second in play; the bridge shortens it to watch a manoeuvre.
    public static double LogEverySeconds { get; set; } = 1.0;

    private static void LogWhileDriven(Entry e, double dt)
    {
        bool driven = e.Input.Throttle != 0.0 || e.Input.Steer != 0.0 || Math.Abs(e.Drive.ForwardSpeed) > 0.5;
        e.SinceLog += dt;
        if (!driven || e.SinceLog < LogEverySeconds) return;
        e.SinceLog = 0.0;

        BuggyDrive d = e.Drive;
        Log.Info($"buggy {KsaWorld.DisplayName(e.Craft)}: {d.ForwardSpeed:F1} m/s, gear {d.Gear + 1}, {d.Rpm:F0} rpm, "
                 + $"steer {d.SteerAngle * 180.0 / Math.PI:F1} deg, input {e.Input.Throttle:F1}/{e.Input.Steer:F1}, "
                 + $"travel {string.Join(" ", d.Travel.Select(t => t.ToString("F3")))}, "
                 + $"hubs {string.Join(" ", e.HubHeights.Select(t => t.ToString("F2")))} m, level {e.Level:F3}, "
                 + $"yaw {e.YawRate * 180.0 / Math.PI:F0} deg/s, sideways {e.SideSpeed:F1} m/s, step {dt * 1000.0:F1} ms, "
                 + $"lift {e.Lift:F2} g{(e.Scraping ? ", SCRAPING" : "")}");
    }

    // The craft's own held controls, so the player's bindings carry over: the pitch keys are the
    // throttle and the yaw keys steer. Nothing is held on a craft nobody is flying.
    private static DriveInput ReadInput(Vehicle craft)
    {
        ThrusterMapFlags flags;
        try { flags = craft.GetThrusterFlags(); }
        catch { return default; }

        double throttle = (flags.HasFlag(ThrusterMapFlags.PitchDown) ? 1.0 : 0.0)
                          - (flags.HasFlag(ThrusterMapFlags.PitchUp) ? 1.0 : 0.0);
        double steer = (flags.HasFlag(ThrusterMapFlags.YawLeft) ? 1.0 : 0.0)
                       - (flags.HasFlag(ThrusterMapFlags.YawRight) ? 1.0 : 0.0);
        return new DriveInput(throttle, steer);
    }

    /// <summary>Puts the wheels, arms, coil-overs and steering wheel where the drive last left them.</summary>
    public void Pose()
    {
        foreach (Entry e in Active.Values)
        {
            try
            {
                Pose(e);
            }
            catch (Exception ex)
            {
                if (_complained) continue;
                _complained = true;
                Log.Warn($"buggy pose: {ex.Message}");
            }
        }
    }

    private static void Pose(Entry e)
    {
        BuggyDrive d = e.Drive;
        BuggyCorner[] corners = d.Profile.Corners;
        for (int i = 0; i < corners.Length; i++)
        {
            BuggyCorner corner = corners[i];
            double angle = BuggyDrive.ArmAngle(corner, d.Travel[i]);
            doubleQuat armTurn = doubleQuat.CreateFromAxisAngle(new double3(0, 0, 1), angle);

            Place(e.Arms[i], corner.ArmPivot, armTurn, null);
            Place(e.Wheels[i], BuggyDrive.OnArm(corner, corner.Hub, angle),
                  BuggyDrive.WheelRotation(d.Spin[i], corner.Steers ? d.SteerAngle : 0.0), null);

            (doubleQuat coil, double3 squash) = BuggyDrive.CoilPose(corner, angle);
            Place(e.Coils[i], corner.CoilTop, coil, squash);
        }

        HoldTheWheel(e);

        Place(e.Steering, d.Profile.SteeringPivot,
              doubleQuat.CreateFromAxisAngle(d.Profile.SteeringAxis, d.SteerAngle * d.Profile.SteeringRatio), null);
    }

    // The seat builds its kitten lazily and again whenever the kitten changes, so this is checked each frame.
    private static void HoldTheWheel(Entry e)
    {
        if (DriverSeat(e) is not { Renderable: { } kitten } || ReferenceEquals(kitten, e.HandsOn)) return;
        if (DriverHands.TryAttach(kitten, e.Drive) is { } hands)
        {
            e.HandsOn = kitten;
            e.Hands = hands;
        }
    }

    // The seat nearest the one the profile says the driver sits in.
    private static IVASeat? DriverSeat(Entry e)
    {
        double3 eye = e.Part.PositionVehicleAsmb + (e.Part.Asmb2VehicleAsmb * e.Drive.Profile.DriverEye);
        IVASeat? best = null;
        double nearest = 0.25;
        foreach (IVASeat seat in e.Craft.Parts.Modules.Get<IVASeat>())
        {
            double d = Vec.Len(seat.PositionAsmb - eye);
            if (d < nearest)
            {
                nearest = d;
                best = seat;
            }
        }
        return best;
    }

    private static void Place(Part? body, double3 position, doubleQuat rotation, double3? scale)
    {
        if (body is null || !Vec.IsFinite(position)) return;

        body.PositionParentAsmb = position;
        body.Asmb2ParentAsmb = rotation;
        if (scale is { } s) body.Scale = s;

        // Part caches its matrices; without this the new value is stored and ignored.
        body.ResetCachedPosMatrixValues();
    }
}
