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
        SpringHz = 1.05,
        DampingRatio = 0.26,
        BumpTravel = 0.12,
        DroopTravel = 0.12,
        AntiRoll = 0.25,
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
