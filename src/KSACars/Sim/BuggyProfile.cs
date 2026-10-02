using Brutal.Numerics;

namespace KSACars;

/// <summary>One corner of a buggy: where its wheel, arm and coil-over are, in the part frame.</summary>
public sealed record BuggyCorner(
    string Key,
    double3 Hub,
    double Radius,
    double3 ArmPivot,
    double3 CoilTop,
    double3 CoilBottom,
    bool Steers,
    bool Driven);

/// <summary>
/// One of a scoop's collider boxes: where it goes while the scoop is on, and how it is turned there if
/// it is declared lying down. Off, it is where the part declares it, turned as the part declares it.
/// </summary>
public sealed record ScoopCollider(string Id, double3 Deployed, doubleQuat? DeployedTurn = null);

/// <summary>
/// A scoop a car can carry on its nose: its name, what the panel calls its size, the subpart that is its blade, and where each of its colliders
/// goes while it is on. Off, the blade is shrunk away inside the hull and the colliders sit where the
/// part declares them, inside the hull's own.
/// </summary>
public sealed record ScoopProfile(string Name, string Size, string SubpartSuffix, double3 Stowed, ScoopCollider[] Colliders)
{
    /// <summary>
    /// The turn that stands a box up as a blade. A box taller than the hull cannot be stowed upright
    /// inside it, so it is declared lying down — thin along the part's up, long along its length, its
    /// height across its width — and this lays its thickness along the car, its length across it and
    /// its height up, then swings it <paramref name="yawRad"/> about the vertical for a swept wing.
    /// </summary>
    public static doubleQuat Upright(double yawRad) =>
        doubleQuat.CreateFromAxisAngle(new double3(1, 0, 0), yawRad)
        * doubleQuat.CreateFromAxisAngle(Vec.Unit(new double3(1, 1, 1)), 2.0 * Math.PI / 3.0);
}

/// <summary>
/// A wheeled ground vehicle. KSA has no wheels, so everything here is the mod's: the springs hold the
/// hull off the ground, the tyres grip it, and the part's colliders only matter once a spring bottoms out.
///
/// <para>Part frame: +X up from the ground, +Y forward, +Z the left side. Every point is the rest pose,
/// with the car on its springs.</para>
/// </summary>
public sealed record BuggyProfile
{
    public required string PartId { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>What the part's subpart Ids end in before the body's name: <c>Buggy_WheelFL</c>, <c>Eldo_Wheel</c>.</summary>
    public required string SubpartPrefix { get; init; }

    public required BuggyCorner[] Corners { get; init; }

    /// <summary>The engine's sounds: <c>{SoundPrefix}Start</c>, <c>Idle</c>, <c>Load</c> and <c>Stop</c>.</summary>
    public required string SoundPrefix { get; init; }

    /// <summary>The RPM the loop under load was recorded at.</summary>
    public double LoadRecordedRpm { get; init; } = 2500.0;

    /// <summary>The RPM the idle loop was recorded at, or zero where that is <see cref="IdleRpm"/>.</summary>
    public double IdleRecordedRpm { get; init; }

    public required double3 SteeringPivot { get; init; }

    /// <summary>The column's axis, pointing at the driver's chest.</summary>
    public required double3 SteeringAxis { get; init; }

    /// <summary>Turns of the steering wheel per turn of the front wheels. Past a quarter turn each way a driver's arms cross.</summary>
    public double SteeringRatio { get; init; } = 3.0;

    public double SteeringRimRadius { get; init; } = 0.11;

    /// <summary>Just ahead of each headlamp's lens, so the car's own nose is not in the beam.</summary>
    public required double3[] HeadLamps { get; init; }

    /// <summary>Just behind each tail lamp.</summary>
    public required double3[] TailLamps { get; init; }

    /// <summary>
    /// The subparts that are lenses of a colour, by what their Ids end in, and that colour as 0xRRGGBB.
    /// A lens in the body glows white.
    /// </summary>
    public (string Suffix, uint Rgb)[] ColouredLenses { get; init; } = [];

    /// <summary>
    /// Where the rockets' flames leave their ports, each pointing straight down. A car with none does
    /// not fly.
    /// </summary>
    public double3[] RocketNozzles { get; init; } = [];

    public bool HasRockets => RocketNozzles.Length > 0;

    /// <summary>
    /// Where the boost rockets' flames leave their ports on the tail, each pointing straight back. A car
    /// with none has no boost.
    /// </summary>
    public double3[] BoostNozzles { get; init; } = [];

    public bool HasBoost => BoostNozzles.Length > 0;

    /// <summary>
    /// Where the downforce rockets' flames leave their ports on the bonnet and the boot, each pointing
    /// straight up. A car with none cannot be pressed down.
    /// </summary>
    public double3[] DownNozzles { get; init; } = [];

    public bool HasDownforce => DownNozzles.Length > 0;

    /// <summary>The hatches its thrusters fire through; none on a car without thrusters.</summary>
    public HatchProfile[] Hatches { get; init; } = [];

    /// <summary>The scoops this car can carry, one at a time; none for a car without.</summary>
    public ScoopProfile[] Scoops { get; init; } = [];

    /// <summary>The driver seat's eye point, as its <c>IVASeat</c> declares it.</summary>
    public required double3 DriverEye { get; init; }

    /// <summary>Bounce frequency of the car on its springs. Low is what makes it springy.</summary>
    public double SpringHz { get; init; } = 1.6;

    /// <summary>Fraction of critical damping. Well under one, so a landing bounces before it settles.</summary>
    public double DampingRatio { get; init; } = 0.3;

    /// <summary>
    /// Each anti-roll bar's stiffness as a share of a corner's spring, pushing against the difference
    /// between the two wheels of an axle. Trims the lean in a corner and leaves one wheel's bounce alone.
    /// </summary>
    public double AntiRoll { get; init; } = 0.35;

    /// <summary>
    /// Damping of an axle's two wheels moving against each other, as a fraction of critical for a
    /// corner's spring, on top of <see cref="DampingRatio"/>. It slows a roll and leaves a bounce alone.
    /// </summary>
    public double RollDamping { get; init; }

    /// <summary>How far above the ground a tyre's side force acts on the body: the roll centre's height.</summary>
    public double RollCentreHeight { get; init; }

    public double BumpTravel { get; init; } = 0.14;
    public double DroopTravel { get; init; } = 0.14;

    /// <summary>How much stiffer than the spring the bump stop is once the wheel runs out of travel.</summary>
    public double BumpStopFactor { get; init; } = 8.0;

    public double FrontGrip { get; init; } = 1.0;

    /// <summary>
    /// A little more than the front, so the car pushes wide rather than swapping ends when a corner is
    /// overcooked. Less than the front spins it on a full-lock turn at speed.
    /// </summary>
    public double RearGrip { get; init; } = 1.1;

    /// <summary>Drive as a fraction of the car's weight, at a standstill.</summary>
    public double LaunchAccelG { get; init; } = 0.55;

    public double TopSpeed { get; init; } = 28.0;
    public double ReverseTopSpeed { get; init; } = 8.0;
    public double BrakeG { get; init; } = 0.9;
    public double RollingResistance { get; init; } = 0.015;
    public double DragAreaM2 { get; init; } = 0.8;

    public double MaxSteerDeg { get; init; } = 32.0;

    /// <summary>
    /// How far past the angle the front tyres can hold the lock goes at speed. One is the grip limit;
    /// a little over lets the car be thrown into a slide, and far over spins it on every corner.
    /// </summary>
    public double SteerOverGrip { get; init; } = 1.25;

    public double SteerRateDegPerSec { get; init; } = 140.0;

    public double IdleRpm { get; init; } = 900.0;
    public double RedlineRpm { get; init; } = 4800.0;

    /// <summary>Of the RPM a wheel speed implies, from a gearbox nobody sees: four gears, shifted at the top.</summary>
    public double[] GearTopSpeeds { get; init; } = [7.0, 13.0, 20.0, 28.0];

    /// <summary>The buggy that ships, a Manx-style tub on VW running gear.</summary>
    public static readonly BuggyProfile Manx = new()
    {
        PartId = "KSACars_Prefab_Buggy",
        DisplayName = "Beach Buggy",
        SubpartPrefix = "Buggy_",
        SoundPrefix = "KSACarsBuggy",
        Corners =
        [
            Corner("FL", front: true, left: true),
            Corner("FR", front: true, left: false),
            Corner("RL", front: false, left: true),
            Corner("RR", front: false, left: false),
        ],
        SteeringPivot = new double3(0.76, 0.31, -0.28),
        SteeringAxis = Vec.Unit(new double3(0.52, -0.85, 0.0)),
        DriverEye = new double3(0.99, 0.18, -0.28),
        HeadLamps = [new double3(0.88, 1.56, 0.30), new double3(0.88, 1.56, -0.30)],
        // the tub has no tail lenses, so these only throw their red on the ground
        TailLamps = [new double3(0.70, -2.06, 0.45), new double3(0.70, -2.06, -0.45)],
    };

    /// <summary>
    /// A 1976 Cadillac Eldorado convertible, top down: 2.3 tonnes on soft springs, front-wheel drive
    /// through a three-speed automatic, and a 500 cu in V8 that idles low. Its suspension is out of
    /// sight, so each wheel rides a long notional arm that moves it very nearly straight up.
    /// </summary>
    public static readonly BuggyProfile Eldorado = new()
    {
        PartId = "KSACars_Prefab_Eldorado",
        DisplayName = "Cadillac Eldorado",
        SubpartPrefix = "Eldo_",
        SoundPrefix = "KSACarsEldo",
        Corners =
        [
            EldoCorner("FL", front: true, left: true),
            EldoCorner("FR", front: true, left: false),
            EldoCorner("RL", front: false, left: true),
            EldoCorner("RR", front: false, left: false),
        ],
        // left-hand drive: the wheel and the driver on the car's left, +Z
        SteeringPivot = new double3(0.88, 0.302, 0.40),
        SteeringAxis = Vec.Unit(new double3(0.60, -0.80, 0.0)),
        SteeringRatio = 6.0,
        SteeringRimRadius = 0.195,
        DriverEye = new double3(1.07, 0.202, 0.40),
        HeadLamps = [new double3(0.74, 2.70, 0.77), new double3(0.74, 2.70, -0.77)],
        TailLamps = [new double3(0.55, -3.07, 0.72), new double3(0.55, -3.07, -0.72)],
        ColouredLenses = [("TailLens", 0xFF1408), ("MarkerLens", 0xFF8C14)],
        // the lips of two nozzles on the tail panel, above the bumper
        BoostNozzles = [new double3(0.74, -2.922, 0.70), new double3(0.74, -2.922, -0.70)],
        // the lips of four nozzles, two on the bonnet and two on the boot
        DownNozzles =
        [
            new double3(1.027, 1.70, 0.45), new double3(1.027, 1.70, -0.45),
            new double3(0.999, -2.50, 0.45), new double3(0.999, -2.50, -0.45),
        ],
        // the centre of each hole and the way the skin faces there, fitted to the body mesh
        Hatches =
        [
            new("RocketFL", new double3(0.1944, 0.85, 0.55), new double3(-0.9984, 0.0567, 0), ThrusterGroup.Lift),
            new("RocketFR", new double3(0.1944, 0.85, -0.55), new double3(-0.9984, 0.0567, 0), ThrusterGroup.Lift),
            new("RocketRL", new double3(0.19, -0.85, 0.55), new double3(-1, 0, 0), ThrusterGroup.Lift),
            new("RocketRR", new double3(0.19, -0.85, -0.55), new double3(-1, 0, 0), ThrusterGroup.Lift),
            new("BoostL", new double3(0.74, -2.878, 0.7), new double3(0, -1, 0), ThrusterGroup.Boost),
            new("BoostR", new double3(0.74, -2.878, -0.7), new double3(0, -1, 0), ThrusterGroup.Boost),
            new("DownFL", new double3(0.9827, 1.7, 0.45), new double3(0.9978, 0.0623, 0.0226), ThrusterGroup.Down),
            new("DownFR", new double3(0.9827, 1.7, -0.45), new double3(0.9978, 0.0623, -0.0226), ThrusterGroup.Down),
            new("DownRL", new double3(0.9549, -2.5, 0.45), new double3(0.9987, -0.05, 0), ThrusterGroup.Down),
            new("DownRR", new double3(0.9549, -2.5, -0.45), new double3(0.9987, -0.05, 0), ThrusterGroup.Down),
        ],
        // each blade's flat middle, and each wing at its own middle; the first scoop's boxes come down to
        // 18 cm off the ground, which is what a small rock would pass under
        Scoops =
        [
            new ScoopProfile("Scoop", "Default", "Scoop", new double3(0.75, -0.18, 0.0),
            [
                new("KSACars_EldoScoopMidCollider", new double3(0.59, 2.82, 0.0)),
                new("KSACars_EldoScoopLeftCollider", new double3(0.59, 3.256, 1.125)),
                new("KSACars_EldoScoopRightCollider", new double3(0.59, 3.256, -1.125)),
            ]),
            // 6 m across and 2.25 m tall, its wings swept 26 degrees: boxes 1.9 m tall, from 20 cm off the ground
            new ScoopProfile("Scoop XXL", "XL", "ScoopXXL", new double3(0.65, -0.18, 0.0),
            [
                new("KSACars_EldoScoopXXLMidCollider", new double3(1.15, 2.86, 0.0), ScoopProfile.Upright(0.0)),
                new("KSACars_EldoScoopXXLLeftCollider", new double3(1.15, 3.57, 1.875), ScoopProfile.Upright(-0.4538)),
                new("KSACars_EldoScoopXXLRightCollider", new double3(1.15, 3.57, -1.875), ScoopProfile.Upright(0.4538)),
            ]),
            // 10 m across and 3.7 m tall, its wings swept 28 degrees. Its boxes are in two tiers, because a
            // box as tall as the blade would not fit across the hull lying down.
            new ScoopProfile("Scoop Mega", "XXL", "ScoopMega", new double3(0.65, -0.18, 0.0),
            [
                new("KSACars_EldoScoopMegaMidLowCollider", new double3(1.025, 2.91, 0.0), ScoopProfile.Upright(0.0)),
                new("KSACars_EldoScoopMegaMidHighCollider", new double3(2.675, 2.91, 0.0), ScoopProfile.Upright(0.0)),
                new("KSACars_EldoScoopMegaLeftLowCollider", new double3(1.025, 4.06, 3.10), ScoopProfile.Upright(-0.4887)),
                new("KSACars_EldoScoopMegaLeftHighCollider", new double3(2.675, 4.06, 3.10), ScoopProfile.Upright(-0.4887)),
                new("KSACars_EldoScoopMegaRightLowCollider", new double3(1.025, 4.06, -3.10), ScoopProfile.Upright(0.4887)),
                new("KSACars_EldoScoopMegaRightHighCollider", new double3(2.675, 4.06, -3.10), ScoopProfile.Upright(0.4887)),
            ]),
        ],
        // the lips of four nozzles, 4.4 cm under the floor once their hatches are open
        RocketNozzles = Under(0.146, 0.85, -0.85, 0.55),
        SpringHz = 1.05,
        DampingRatio = 0.26,
        BumpTravel = 0.12,
        DroopTravel = 0.12,
        // Soft in roll and well damped in it: at the bounce's damping a steering reversal at speed swings
        // the lean past 17 degrees, where the outside tyres' grip tips the car over them.
        AntiRoll = 0.1,
        RollDamping = 0.5,
        RollCentreHeight = 0.2,
        FrontGrip = 0.95,
        RearGrip = 1.0,
        LaunchAccelG = 0.4,
        TopSpeed = 55.0,
        ReverseTopSpeed = 8.0,
        BrakeG = 0.8,
        DragAreaM2 = 0.95,
        MaxSteerDeg = 35.0,
        SteerOverGrip = 1.2,
        SteerRateDegPerSec = 110.0,
        IdleRpm = 650.0,
        RedlineRpm = 4400.0,
        LoadRecordedRpm = 1900.0,
        // a cold start's fast idle, the Cadillac recording's 95 Hz firing rate
        IdleRecordedRpm = 1400.0,
        GearTopSpeeds = [18.0, 34.0, 55.0],
    };

    // Four points on the floor: a pair ahead and a pair behind, either side of the centre line.
    private static double3[] Under(double floor, double front, double rear, double side) =>
        [new(floor, front, side), new(floor, front, -side), new(floor, rear, side), new(floor, rear, -side)];

    /// <summary>Every car the mod drives.</summary>
    public static readonly BuggyProfile[] All = [Manx, Eldorado];

    // Off the Blender source, from the 1976 specification: 126.3 in wheelbase, 63.7/63.6 in tracks,
    // LR78-15 tyres. The arm is notional and 2 m long, and the coil-over eyes are never drawn.
    private static BuggyCorner EldoCorner(string key, bool front, bool left)
    {
        double z = left ? 1.0 : -1.0;
        double y = front ? 1.604 : -1.604;
        double3 hub = new(0.36, y, (front ? 0.809 : 0.8075) * z);
        return new BuggyCorner(key, hub, 0.36, hub + new double3(0, 2.0, 0), hub + new double3(0.30, 0, 0),
                               hub + new double3(0.05, 0, 0), Steers: front, Driven: front);
    }

    // Off the Blender source: the hubs, the trailing-arm bushings and the coil-over eyes.
    private static BuggyCorner Corner(string key, bool front, bool left)
    {
        double z = left ? 1.0 : -1.0;
        return front
            ? new BuggyCorner(key, new double3(0.34, 1.15, 0.84 * z), 0.34, new double3(0.40, 1.55, 0.62 * z),
                              new double3(0.78, 1.29, 0.64 * z), new double3(0.37, 1.21, 0.66 * z),
                              Steers: true, Driven: false)
            : new BuggyCorner(key, new double3(0.40, -0.95, 0.88 * z), 0.40, new double3(0.44, -0.45, 0.62 * z),
                              new double3(0.86, -0.75, 0.66 * z), new double3(0.43, -0.85, 0.68 * z),
                              Steers: false, Driven: true);
    }
}
