using BepuUtilities;
using Brutal.Numerics;
using KSA;
using KSA.Rendering.Lighting;

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
        public Part?[] Uprights { get; } = new Part?[profile.Corners.Length];
        public double AirborneSeconds { get; set; }
        public bool DragShed { get; set; }
        public double GameSkinAreaM2 { get; set; }
        public Part? Steering { get; set; }
        public DriveInput Input { get; set; }
        public bool Stepped { get; set; }
        public bool Railed { get; set; }
        public double StepSeconds { get; set; }
        public string Skipped { get; set; } = "";
        public double[] HubHeights { get; } = new double[profile.Corners.Length];

        /// <summary>How far each hub was above the road under it last step, or null with no road under it.</summary>
        public double?[] RoadOver { get; } = new double?[profile.Corners.Length];

        /// <summary>Which laying of the roads <see cref="RoadOver"/> is of.</summary>
        public int RoadGeneration { get; set; }
        public double Level { get; set; } = 1.0;
        public double SinceLog { get; set; }
        public KittenRenderable? HandsOn { get; set; }
        public DriverHands? Hands { get; set; }
        public bool Scraping { get; set; }
        public double SpringLift { get; set; }
        public double YawRate { get; set; }
        public double SideSpeed { get; set; }
        public BeamSetting Beam { get; set; }
        public bool SwitchSeen { get; set; }
        /// <summary>The throttle the rockets burn at once lit, and what they are giving now.</summary>
        public double Throttle { get; set; } = Lift.DefaultThrottle;
        public double Rockets { get; set; }
        public bool Lit { get; set; }
        public double Turn { get; set; }
        public VolumetricExhaustInstance?[]? Flames { get; set; }
        public VolumetricExhaustInstance?[]? BoostFlames { get; set; }
        public VolumetricExhaustInstance?[]? DownFlames { get; set; }
        public bool Downforce { get; set; }
        public bool Pressed { get; set; }
        public bool Boosting { get; set; }
        public Part?[] ScoopBlades { get; } = new Part?[profile.Scoops.Length];
        public Part?[] HatchCups { get; } = new Part?[profile.Hatches.Length];
        public Part?[] HatchBlades { get; } = new Part?[profile.Hatches.Length * Hatch.Blades];

        /// <summary>How far open each group's hatches are, 0 to 1, and whether the driver wants them open.</summary>
        public double[] HatchOpen { get; } = new double[3];
        public bool[] HatchWanted { get; } = new bool[3];
        public bool Ready(ThrusterGroup group) => Hatch.Ready(HatchOpen[(int)group]);

        /// <summary>Which of the profile's scoops is on, or -1 for none.</summary>
        public int Scoop { get; set; } = -1;
        public int? ScoopSet { get; set; }
        public bool ScoopOn => Scoop >= 0;
        public double Climb { get; set; }
    }

    // Inputs held by the bridge for so many simulated seconds, ahead of the keys.
    private static readonly Dictionary<Vehicle, (DriveInput Input, double Turn, bool Boost, double Seconds)> Held = [];

    // Read by the physics prefix, which has only the vehicle to go on.
    private static readonly Dictionary<Vehicle, Entry> Active = [];

    // Cars asked to be set back on their wheels, and whether instead to be tipped onto the roof.
    private static readonly Dictionary<Vehicle, bool> ToRight = [];

    private static bool _complained;
    private long _posedAtMs;

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
            // A saved car comes back with its switch as it was left.
            if (found.Part.LightSwitch is { LightIsActive: true })
            {
                entry.Beam = BeamSetting.Low;
                entry.SwitchSeen = true;
            }
            Active[v] = entry;
            Log.Info($"{found.Profile.DisplayName} on {KsaWorld.DisplayName(v)}: "
                     + $"{entry.Wheels.Count(w => w is not null)} wheel(s) found");
        }

        List<Vehicle>? gone = null;
        foreach ((Vehicle v, Entry e) in Active)
        {
            if (!KsaWorld.IsAlive(v) || Find(v)?.Part != e.Part) (gone ??= []).Add(v);
        }
        if (gone is not null)
        {
            foreach (Vehicle v in gone)
            {
                Active.Remove(v);
                RailsHook.Forget(v);
            }
        }

        bool scooping = false;
        bool pushers = false;
        foreach (Entry e in Active.Values)
        {
            SetScoop(e);
            scooping |= e.ScoopOn;
            pushers |= e.Drive.Profile.Scoops.Length > 0;
        }
        KsaWorld.LoosenClutter(scooping, pushers);

        foreach (Entry e in Active.Values)
        {
            if (e.Drive.Profile.HasRockets) KsaWorld.ShowThrottle(e.Craft, e.Throttle);
            if (e.Part.LightSwitch is not { } lamps) continue;
            (e.Beam, lamps.LightIsActive) = Headlights.Reconcile(e.Beam, lamps.LightIsActive, e.SwitchSeen);
            e.SwitchSeen = lamps.LightIsActive;
        }
    }

    public void Clear()
    {
        Active.Clear();
        Held.Clear();
        ToRight.Clear();
    }

    /// <summary>Holds a buggy's throttle and steering for so many simulated seconds, whoever is flying.</summary>
    public static bool Hold(Vehicle craft, DriveInput input, double seconds, double turn = 0.0, bool boost = false)
    {
        if (!Active.ContainsKey(craft)) return false;
        if (seconds > 0.0) Held[craft] = (input, turn, boost, seconds);
        else Held.Remove(craft);
        return true;
    }

    /// <summary>The car a craft is, or null when it is not one.</summary>
    public static Entry? Of(Vehicle craft) => Active.GetValueOrDefault(craft);

    /// <summary>
    /// Sets a car back on its wheels where it lies, at its next physics step. <paramref name="tip"/>
    /// turns it onto its roof instead, for the bridge to have something to right.
    /// </summary>
    public static bool Right(Vehicle craft, bool tip = false)
    {
        if (!Active.ContainsKey(craft)) return false;
        ToRight[craft] = tip;
        return true;
    }

    /// <summary>
    /// A car's lit headlamps, handed to KSA as spotlights beside the craft's own lights. The matrix is
    /// the one KSA is drawing the craft with, so the beams sit on the lamps as drawn.
    /// </summary>
    public static void LightLamps(PartTree parts, in double4x4 asmb2Ego, IViewport viewport)
    {
        if (Active.Count == 0) return;
        foreach (Entry e in Active.Values)
        {
            if (!ReferenceEquals(e.Craft.Parts, parts)) continue;
            // A flat battery puts the lamps out with the lenses, which KSA darkens itself.
            if (Headlights.Shape(e.Beam) is not { } beam || e.Part.IsLightSwitchedOff()) return;

            doubleQuat part2Asmb = e.Part.Asmb2VehicleAsmb;
            double3 up = part2Asmb * new double3(1, 0, 0);
            double3 forward = part2Asmb * new double3(0, 1, 0);
            Shine(e, e.Drive.Profile.HeadLamps, Headlights.Aim(up, forward, beam.DipRad), beam, Headlights.Colour,
                  beam.Intensity, ELightFlags.CastsShadows | ELightFlags.SoftShadows, in asmb2Ego, viewport);

            BeamShape tail = Headlights.Tail;
            bool braking = Headlights.Braking(e.Input.Throttle, e.Drive.ForwardSpeed);
            float brake = braking ? Headlights.BrakeBoost : 1f;
            Shine(e, e.Drive.Profile.TailLamps, Headlights.Aim(up, -forward, tail.DipRad), tail, Headlights.TailColour,
                  tail.Intensity * brake, ELightFlags.None, in asmb2Ego, viewport);
            return;
        }
    }

    private static void Shine(Entry e, double3[] lamps, double3 d, BeamShape beam, float3 colour, float intensity,
                              ELightFlags flags, in double4x4 asmb2Ego, IViewport viewport)
    {
        double3 aim = new((d.X * asmb2Ego.M11) + (d.Y * asmb2Ego.M21) + (d.Z * asmb2Ego.M31),
                          (d.X * asmb2Ego.M12) + (d.Y * asmb2Ego.M22) + (d.Z * asmb2Ego.M32),
                          (d.X * asmb2Ego.M13) + (d.Y * asmb2Ego.M23) + (d.Z * asmb2Ego.M33));
        foreach (double3 lamp in lamps)
        {
            double3 at = (e.Part.PositionVehicleAsmb + (e.Part.Asmb2VehicleAsmb * lamp)).Transform(asmb2Ego);
            Program.LightSystem.CreateLightInstance(
                Light.CreateSpotLight(at, aim, beam.Range, beam.OuterAngle, beam.InnerAngle, colour, intensity, flags),
                viewport);
        }
    }

    private const string FlameTemplate = "EngineAAuxiliary";

    // The downforce rockets burn steadily, and short: they sit in front of the driver.
    private const double DownFlame = 0.35;

    /// <summary>
    /// A flying car's flames, handed to KSA's plume renderer beside the craft's own engines. No engine is
    /// behind them: the gas each is drawn from is <see cref="Lift.Flame"/>'s, sized by the throttle.
    /// </summary>
    public static void Flames(Vehicle craft, Camera camera, VolumetricExhaustRenderer renderer, double frameDeltaTime)
    {
        if (Active.Count == 0 || renderer.Disabled || !Active.TryGetValue(craft, out Entry? e)) return;

        BuggyProfile p = e.Drive.Profile;
        e.Flames = Burn(e, e.Flames, p.RocketNozzles, new double3(-1, 0, 0), e.Rockets, camera, renderer, frameDeltaTime);
        e.BoostFlames = Burn(e, e.BoostFlames, p.BoostNozzles, new double3(0, -1, 0), e.Boosting ? 1.0 : 0.0,
                             camera, renderer, frameDeltaTime);
        e.DownFlames = Burn(e, e.DownFlames, p.DownNozzles, new double3(1, 0, 0), e.Pressed ? DownFlame : 0.0,
                            camera, renderer, frameDeltaTime);
    }

    // One set of flames, all at one throttle and all pointing one way in the part's frame. Returns the
    // instances to keep: null once they are out and burnt down, so the next burn starts from cold.
    private static VolumetricExhaustInstance?[]? Burn(
        Entry e, VolumetricExhaustInstance?[]? flames, double3[] nozzles, double3 direction, double throttle,
        Camera camera, VolumetricExhaustRenderer renderer, double frameDeltaTime)
    {
        if (nozzles.Length == 0 || (throttle <= 0.0 && flames is null)) return flames;

        Vehicle craft = e.Craft;
        flames ??= new VolumetricExhaustInstance?[nozzles.Length];

        bool burning = throttle > 0.0;
        FlameGas f = Lift.Flame(throttle);
        GasProperties gas = new() { Gamma = f.Gamma, SpecificGasConstant = f.SpecificGasConstant };
        GasConditions inlet = new() { Pressure = f.ChamberPressure, Temperature = f.ChamberTemperature };
        GasConditions exhaust = new() { Pressure = f.ExitPressure, Temperature = f.ExitTemperature };

        double3 at = KsaWorld.PositionEcl(craft);
        double air = KsaWorld.AirDensityRatioAt(craft, at);
        float ambient = (float)(air * 101325.0);
        float airDensity = (float)(air * KsaWorld.ReferenceAirDensityKgPerM3);
        float3 airVelocity = craft.Parent is { } parent
            ? float3.Pack(craft.GetSurfaceVelocityCci().Transform(parent.GetCci2Cce()))
            : float3.Zero;

        double simDt = frameDeltaTime * Universe.GetSimulationSpeed();
        double now = Universe.GetElapsedSeconds();
        double3 craftEgo = camera.GetPositionEgo(craft);
        doubleQuat body2Cce = craft.Body2Cce;
        doubleQuat part2Asmb = e.Part.Asmb2VehicleAsmb;
        float3 axis = float3.Pack((part2Asmb * direction).Transform(body2Cce));

        bool any = false;
        for (int i = 0; i < nozzles.Length; i++)
        {
            if (flames[i] is not { } flame)
            {
                VolumetricExhaustReference reference = new() { Id = FlameTemplate };
                reference.Load();
                flame = flames[i] = new VolumetricExhaustInstance(reference);
            }
            if (flame.Template is not { } template) continue;

            // A flame going out keeps the shape it last had, which is what its dying is drawn from.
            if (burning)
            {
                flame.LastPlumeData = RocketNozzle.ComputePlumeData(
                    in gas, in exhaust, in inlet, inlet.Pressure, f.ExhaustVelocity, ambient, f.ExitRadius,
                    f.ExitRadius / MathF.Sqrt(f.AreaRatio), RocketDesign.SolveMachNumberFromAreaRatio(gas, f.AreaRatio),
                    RocketNozzle.ComputeMinGasVisibilityDensity(template, f.ExitRadius));
            }

            double3 nozzle = e.Part.PositionVehicleAsmb + (part2Asmb * nozzles[i]);
            float3 where = float3.Pack(craftEgo + craft.PosAsmbToBody(nozzle).Transform(body2Cce));
            flame.UpdateState(now, burning, simDt, in gas, in exhaust, f.ExhaustVelocity, where, axis, where, axis,
                              ambient, airVelocity, airDensity);
            if (!flame.IsLive) continue;

            any = true;
            renderer.AddInstance(flame, default, in ExhaustAxialFade.NoFadeOut, in ExhaustDiamondFade.None);
        }

        return !burning && !any ? null : flames;
    }

    /// <summary>The colour a subpart's lenses glow, as 0xRRGGBB, or null for KSA's own white.</summary>
    public static uint? LensColour(Part part)
    {
        if (Active.Count == 0) return null;
        foreach (Entry e in Active.Values)
        {
            if (!ReferenceEquals(e.Part, part.FullPart) || part.Id is not { } id) continue;
            foreach ((string suffix, uint rgb) in e.Drive.Profile.ColouredLenses)
            {
                if (id.EndsWith(e.Drive.Profile.SubpartPrefix + suffix, StringComparison.Ordinal)) return rgb;
            }
            foreach ((string suffix, uint rgb) in e.Drive.Profile.BrakeLenses)
            {
                if (!id.EndsWith(e.Drive.Profile.SubpartPrefix + suffix, StringComparison.Ordinal)) continue;
                return Headlights.BrakeLens(rgb, Headlights.Braking(e.Input.Throttle, e.Drive.ForwardSpeed));
            }
            return null;
        }
        return null;
    }

    // Puts the scoop's colliders out on the blade or back inside the hull, once each time it changes.
    private static void SetScoop(Entry e)
    {
        ScoopProfile[] scoops = e.Drive.Profile.Scoops;
        if (scoops.Length == 0 || e.ScoopSet == e.Scoop) return;
        e.ScoopSet = e.Scoop;

        foreach (ColliderModule collider in e.Part.SubtreeModules.Get<ColliderModule>())
        {
            for (int k = 0; k < scoops.Length; k++)
            {
                foreach (ScoopCollider box in scoops[k].Colliders)
                {
                    if (collider.TemplateId != box.Id) continue;
                    bool on = k == e.Scoop;
                    collider.PositionPartAsmb = on ? box.Deployed : scoops[k].Stowed;
                    if (box.DeployedTurn is { } turn) collider.Collider2PartAsmb = on ? turn : doubleQuat.Identity;
                    collider.NeedsColliderUpdate = true;
                }
            }
        }
        Log.Info($"{KsaWorld.DisplayName(e.Craft)}: {(e.ScoopOn ? scoops[e.Scoop].Name + " on" : "scoop off")}");
    }

    /// <summary>Lights or cuts a car's rockets, as KSA's engine start and shutdown keys do.</summary>
    public static void Ignite(Entry e, bool on)
    {
        if (!e.Drive.Profile.HasRockets) return;
        e.Lit = on;
        KsaWorld.TrySetEngineOn(e.Craft, on);
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
            ["skipped"] = e.Skipped,
            ["situation"] = craft.Situation.ToString(),
            ["rails_hook"] = RailsHook.Installed,
            ["step_ms"] = Math.Round(e.StepSeconds * 1000.0, 1),
            ["lights"] = e.Beam.ToString().ToLowerInvariant(),
            ["scoop"] = e.ScoopOn ? e.Drive.Profile.Scoops[e.Scoop].Name : "off",
            ["boosting"] = e.Boosting,
            ["game_drag"] = !e.DragShed,
            ["downforce"] = e.Downforce,
            ["clutter_collisions"] = KsaWorld.ClutterCollisions,
            ["rock_weight"] = KsaWorld.RockWeight,
            ["lift"] = Math.Round(e.Rockets, 2),
            ["lift_throttle"] = Math.Round(e.Throttle, 2),
            ["rockets_lit"] = e.Lit,
            ["climb_ms"] = Math.Round(e.Climb, 2),
            ["flames_live"] = e.Flames?.Count(f => f is { IsLive: true }) ?? 0,
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
            e.Uprights[i] = SubPart(e.Part, p.SubpartPrefix + "Upright" + p.Corners[i].Key);
        }
        e.Steering = SubPart(e.Part, p.SubpartPrefix + "Steering");
        for (int k = 0; k < p.Scoops.Length; k++) e.ScoopBlades[k] = SubPart(e.Part, p.SubpartPrefix + p.Scoops[k].SubpartSuffix);
        for (int k = 0; k < p.Hatches.Length; k++)
        {
            e.HatchCups[k] = SubPart(e.Part, p.SubpartPrefix + p.Hatches[k].Suffix);
            for (int b = 0; b < Hatch.Blades; b++)
                e.HatchBlades[(k * Hatch.Blades) + b] = SubPart(e.Part, $"{p.SubpartPrefix}Iris{p.Hatches[k].Suffix}{b}");
        }
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
        if (!Active.TryGetValue(craft, out Entry? e))
        {
            // No longer a car the mod drives: it gets KSA's air drag back, here, where the worker is not reading it.
            if (Shed.Count > 0 && Shed.Remove(craft)) craft.GetPhysicsStatesMutable().Props.RecomputeAerodynamicProperties();
            return;
        }

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

    // KSA's height field is asked in its own frame, and a wheel's direction is in the body's.
    private sealed class GroundCcf(TerrainHeights ground, doubleQuat ccf2Cce) : ITerrainHeights
    {
        public bool TryHeight(double3 dirFromCentre, out double metres) =>
            ground.TryHeight(dirFromCentre.Transform(ccf2Cce), out metres);
    }

    private static void Step(Entry e, double dt)
    {
        Vehicle craft = e.Craft;
        e.Input = ReadInput(craft);
        e.Turn = ReadTurn(craft);
        bool boost = craft.GetSprintInput();
        if (Held.TryGetValue(craft, out (DriveInput Input, double Turn, bool Boost, double Seconds) held))
        {
            e.Input = held.Input;
            e.Turn = held.Turn;
            boost = held.Boost;
            if (held.Seconds - dt <= 0.0) Held.Remove(craft);
            else Held[craft] = (held.Input, held.Turn, held.Boost, held.Seconds - dt);
        }
        // A rocket fires once its hatch is open; until then the driver only wants it.
        e.HatchWanted[(int)ThrusterGroup.Boost] = boost && e.Drive.Profile.HasBoost;
        e.Boosting = e.HatchWanted[(int)ThrusterGroup.Boost] && e.Ready(ThrusterGroup.Boost);

        // KSA's own engine keys: start and shutdown light and cut the rockets, and the throttle keys move
        // the mod's throttle, because KSA pins its own at full on a craft with no engine.
        (bool more, bool less) = KsaWorld.ThrottleKeys(craft);
        if (dt > 0.0) e.Throttle = Lift.Ramp(e.Throttle, more, less, dt);
        e.Lit = e.Drive.Profile.HasRockets && (KsaWorld.EngineOn(craft) ?? e.Lit);
        e.HatchWanted[(int)ThrusterGroup.Lift] = e.Lit;
        e.HatchWanted[(int)ThrusterGroup.Down] = e.Downforce && e.Drive.Profile.HasDownforce;
        e.Rockets = e.Lit && e.Ready(ThrusterGroup.Lift) ? e.Throttle : 0.0;
        e.Pressed = e.HatchWanted[(int)ThrusterGroup.Down] && e.Ready(ThrusterGroup.Down);

        // KSA puts a craft out of the air onto its orbit unless an engine of its own is firing.
        bool burning = e.Rockets > 0.0 || e.Boosting;
        RailsHook.Mark(craft, burning);

        // The engine integrates a frame as one impulse followed by its own sub-steps, so a spring is
        // only as stable as the frame is short. Under warp the springs are left out and the car rests on
        // its colliders; the rockets are a plain push, good for a step of any length, and carry on.
        if (!(dt > 0.0)) return;
        bool warped = dt > 0.1;
        e.StepSeconds = dt;
        e.Skipped = !craft.HasPhysicsBubble ? "no physics bubble" : craft.Parent is not Celestial ? "no parent body" : "";
        if (!craft.HasPhysicsBubble || craft.Parent is not Celestial body) return;

        bool railed = craft.Situation.IsOnRails();
        e.Railed = railed;
        bool idle = e.Input.Throttle == 0.0 && e.Input.Steer == 0.0 && !e.Lit && !e.HatchWanted[(int)ThrusterGroup.Boost] && !e.Downforce;

        // KSA only rails a car that has stood still, so one on rails with nobody at the wheel is parked:
        // it is exactly where it was, and costs nothing. Its speed from before it stopped says nothing.
        bool righting = ToRight.Remove(craft, out bool tip);
        if (railed && idle && !righting) return;

        PhysicsStates states = craft.GetPhysicsStatesMutable();

        // A car coasting on its orbit is rebuilt from it, as KSA rebuilds a craft it is about to split;
        // one standing on the ground is on rails too, held to the ground and not to an orbit, and is
        // only woken, below.
        bool coasting = railed && !craft.Situation.HasAnyContact();
        if (coasting)
        {
            if (!burning) return;
            Orbit orbit = craft.Orbit;
            states.UpdateFromAnalytic(orbit, in orbit.StateVectors, craft.Body2Cce, craft.BodyRates, Situation.Maneuvering);
            railed = false;
        }

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
        LaunchPads pads = LaunchPads.On(body);

        BuggyCorner[] corners = e.Drive.Profile.Corners;
        Span<WheelContact> contacts = stackalloc WheelContact[corners.Length];
        Span<double3> hubs = stackalloc double3[corners.Length];
        for (int i = 0; i < corners.Length; i++) hubs[i] = partOrigin + (part2Asmb * corners[i].Hub) - com;

        if (righting)
        {
            double comRadius = Vec.Len(positionCcf);
            if (!(comRadius > 0.0)) return;
            double3 upCcf = positionCcf / comRadius;
            if (!ground.TryHeight(upCcf.Transform(ccf2Cce), out double under)) return;

            double3 groundUp = upCcf.Transform(ccf2Body);
            double comHeight = comRadius - (body.MeanRadius + under);
            if (pads.TryHeightOver(positionCcf, out double overPad)) comHeight = Math.Min(comHeight, overPad);
            if (Roads.TryHeightOver(body, positionCcf, null, out double overRoad)) comHeight = Math.Min(comHeight, overRoad);
            RightingMove move = tip
                ? new RightingMove(doubleQuat.CreateFromAxisAngle(forward, Math.PI), 1.5 - comHeight)
                : Righting.Solve(corners, hubs, up, forward, groundUp, comHeight);

            // The turn is in the car's own frame, so it goes on first; and the car is left standing
            // still over the ground, which is what the planet-fixed velocity measures.
            states.Kinematic.PositionPhys += groundUp.Transform(body2Phys) * move.Lift;
            states.Kinematic.Body2Phys = body2Phys * move.Turn;
            states.Kinematic.VelocityPhys -= velocityBody.Transform(body2Phys);
            states.Kinematic.AngularVelocityPhys = default;
            if (railed) craft.TakeOffRails();
            Log.Info($"{KsaWorld.DisplayName(craft)} {(tip ? "tipped onto its roof" : "set back on its wheels")}, lifted {move.Lift:F2} m");
            return;
        }

        if (e.RoadGeneration != Roads.Generation)
        {
            Array.Clear(e.RoadOver);
            e.RoadGeneration = Roads.Generation;
        }

        RoadStop stop = WheelGround.Read(positionCcf, body2Ccf, velocityBody, spinBody, hubs, e.Drive.Profile,
                                         body.MeanRadius, new GroundCcf(ground, ccf2Cce), pads.TryHeightOver,
                                         Roads.SurfaceOn(body), e.RoadOver, contacts, e.HubHeights);
        if (stop.Fired)
        {
            states.Kinematic.PositionPhys += stop.Up.Transform(body2Phys) * stop.LiftM;
            states.Kinematic.VelocityPhys += stop.Up.Transform(body2Phys) * stop.SpeedMs;
        }

        double mass = craft.TotalMass;
        double gravity = Vec.Len(KsaWorld.GravityAt(craft, KsaWorld.PositionEcl(craft)));
        double air = KsaWorld.ReferenceAirDensityKgPerM3 * KsaWorld.AirDensityRatioAt(craft, KsaWorld.PositionEcl(craft));

        // Both sets of rockets at once hold the car where it is, so neither pushes on its own.
        bool hovering = e.Pressed && e.Rockets > 0.0;
        // Pressing the car down is for the tyres' grip, which under warp there is none of.
        bool pressed = e.Pressed && !hovering && !warped;
        DriveImpulse impulse = warped
            ? default
            : e.Drive.Step(e.Input, contacts, hubs, up, forward, mass, Downforce.Load(pressed, gravity), air, dt);
        e.Stepped = true;
        e.Level = contacts[0].Valid ? Vec.Dot(up, contacts[0].GroundUp) : e.Level;
        e.YawRate = Vec.Dot(spinBody, up);
        e.SpringLift = Vec.Dot(impulse.Linear, up) / (mass * gravity * dt);
        e.SideSpeed = Vec.Dot(velocityBody, Vec.Cross(up, forward));
        e.Climb = Vec.Dot(velocityCcf, Vec.Unit(positionCcf));
        // Whether any collider is on the ground: the tyres are not colliders, so this is the hull scraping.
        try { e.Scraping = craft.Situation.HasTerrainContact(); } catch { e.Scraping = false; }
        LogWhileDriven(e, dt);

        double3 dv = impulse.Linear / mass;
        Symmetric3x3 inverse = Symmetric3x3.Invert(craft.TotalMassPropsBody.Inertia);
        double3 l = impulse.Angular;
        double3 dw = new((inverse.XX * l.X) + (inverse.YX * l.Y) + (inverse.ZX * l.Z),
                         (inverse.YX * l.X) + (inverse.YY * l.Y) + (inverse.ZY * l.Z),
                         (inverse.ZX * l.X) + (inverse.ZY * l.Y) + (inverse.ZZ * l.Z));

        // The wheels are off the ground, so the keys that drove them fly the car instead.
        // Under warp the wheels are not stepped, so the hull's own contact says whether it is down.
        bool airborne = warped ? !e.Scraping : !e.Drive.Grounded.Any(g => g);
        ShedGameDrag(e, craft, ref states.Props, airborne, burning, dt);
        double3 groundUpBody = Vec.Unit(positionCcf).Transform(ccf2Body);
        LiftPush lift = Lift.Step(new LiftInput(e.Rockets, e.Input.Throttle, e.Input.Steer, e.Turn), airborne, up, forward,
                                  groundUpBody, spinBody, gravity, dt);
        dv += hovering ? Hover.Push(up, groundUpBody, e.Climb, gravity, dt) : lift.Velocity + Downforce.Push(pressed, up, dt);
        dv += Boost.Push(e.Boosting, forward, dt);
        dw += lift.Spin;
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

    /// <summary>Whether KSA's own air drag is kept off a car on its wheels. The bridge clears it to compare.</summary>
    public static bool ShedsGameDrag { get; set; } = true;

    // Crafts whose aerodynamic record this has zeroed, so one that stops being a car can be given it back.
    private static readonly HashSet<Vehicle> Shed = [];

    // KSA drags a craft by its colliders' bounding box, a car on its wheels included, and skips all of it
    // while the record's area is zero. Zeroed every step, because KSA rebuilds it when a kitten boards;
    // rebuilt from the same box once the car is flying. docs/KSA-MODDING-NOTES.md has the mechanism.
    private static void ShedGameDrag(Entry e, Vehicle craft, ref VehicleProperties props, bool airborne, bool burning, double dt)
    {
        e.AirborneSeconds = airborne ? e.AirborneSeconds + dt : 0.0;
        bool shed = ShedsGameDrag && BuggyDrive.ShedsGameDrag(e.AirborneSeconds, burning);
        if (shed)
        {
            props.AerodynamicCdABody = default;
            Shed.Add(craft);
        }
        else if (e.DragShed)
        {
            props.RecomputeAerodynamicProperties();
            Shed.Remove(craft);
        }
        e.DragShed = shed;
        e.GameSkinAreaM2 = 0.1 * props.TotalSurfaceArea;
    }

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
                 + $"lift {e.SpringLift:F2} g, game drag {(e.DragShed ? "off" : "on")} (skin {e.GameSkinAreaM2:F2} m2)"
                 + $"{(e.Scraping ? ", SCRAPING" : "")}");
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

    // The roll keys, which turn a car in the air: its pitch and yaw keys lean it there.
    private static double ReadTurn(Vehicle craft)
    {
        ThrusterMapFlags flags;
        try { flags = craft.GetThrusterFlags(); }
        catch { return 0.0; }

        return (flags.HasFlag(ThrusterMapFlags.RollLeft) ? 1.0 : 0.0) - (flags.HasFlag(ThrusterMapFlags.RollRight) ? 1.0 : 0.0);
    }

    /// <summary>Puts the wheels, arms, coil-overs and steering wheel where the drive last left them.</summary>
    public void Pose()
    {
        // The hatches open in the world's time, so slow motion slows them and a pause holds them; a long
        // gap between frames is a hitch, and is not turned through.
        long now = Environment.TickCount64;
        double dt = _posedAtMs == 0 ? 0.0 : Math.Min((now - _posedAtMs) / 1000.0, 0.1) * Math.Max(KsaWorld.SimulationSpeed, 0.0);
        _posedAtMs = now;

        foreach (Entry e in Active.Values)
        {
            try
            {
                Pose(e, dt);
            }
            catch (Exception ex)
            {
                if (_complained) continue;
                _complained = true;
                Log.Warn($"buggy pose: {ex.Message}");
            }
        }
    }

    private static void Pose(Entry e, double dt)
    {
        BuggyDrive d = e.Drive;
        BuggyCorner[] corners = d.Profile.Corners;
        for (int i = 0; i < corners.Length; i++)
        {
            BuggyCorner corner = corners[i];
            double angle = BuggyDrive.ArmAngle(corner, d.Travel[i]);
            doubleQuat armTurn = doubleQuat.CreateFromAxisAngle(new double3(0, 0, 1), angle);

            Place(e.Arms[i], corner.ArmPivot, armTurn, null);
            double3 hub = BuggyDrive.OnArm(corner, corner.Hub, angle);
            double steer = corner.Steers ? d.SteerAngle : 0.0;
            Place(e.Wheels[i], hub, BuggyDrive.WheelRotation(d.Spin[i], steer), null);
            // An upright rises and steers with its wheel and does not turn with it.
            Place(e.Uprights[i], hub, BuggyDrive.WheelRotation(0.0, steer), null);

            (doubleQuat coil, double3 squash) = BuggyDrive.CoilPose(corner, angle);
            Place(e.Coils[i], corner.CoilTop, coil, squash);
        }

        HoldTheWheel(e);

        HatchProfile[] hatches = d.Profile.Hatches;
        // An iris stays open on a flame that is still dying, however long the sim speed makes that.
        for (int g = 0; g < e.HatchOpen.Length; g++)
        {
            bool burning = (ThrusterGroup)g switch
            {
                ThrusterGroup.Lift => e.Flames is not null,
                ThrusterGroup.Boost => e.BoostFlames is not null,
                _ => e.DownFlames is not null,
            };
            e.HatchOpen[g] = Hatch.Advance(e.HatchOpen[g], e.HatchWanted[g] || burning, dt);
        }
        for (int k = 0; k < hatches.Length; k++)
        {
            Place(e.HatchCups[k], hatches[k].At, Hatch.Facing(hatches[k]), null);
            for (int b = 0; b < Hatch.Blades; b++)
            {
                (double3 at, doubleQuat turned, double3 size) = Hatch.Blade(hatches[k], b, e.HatchOpen[(int)hatches[k].Group]);
                Place(e.HatchBlades[(k * Hatch.Blades) + b], at, turned, size);
            }
        }

        // Hidden by being shrunk to nothing inside the hull: a subpart has no switch for being drawn.
        for (int k = 0; k < d.Profile.Scoops.Length; k++)
        {
            bool on = k == e.Scoop;
            Place(e.ScoopBlades[k], on ? Vec.Zero : d.Profile.Scoops[k].Stowed, doubleQuat.Identity,
                  on ? new double3(1, 1, 1) : new double3(0.001, 0.001, 0.001));
        }

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
