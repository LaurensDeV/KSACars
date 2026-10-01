using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// The lift throttle, 0 to 1, and the stick while the car is off the ground: nose down to go forward,
/// bank left to go left, yaw left to turn.
/// </summary>
public readonly record struct LiftInput(double Throttle, double Pitch, double Roll, double Yaw);

/// <summary>What KSA's plume is computed from, in SI: pressures in pascals, temperatures in kelvin.</summary>
public readonly record struct FlameGas(float ExitRadius, float AreaRatio, float ChamberPressure, float ChamberTemperature,
                                       float ExitPressure, float ExitTemperature, float Gamma,
                                       float SpecificGasConstant, float ExhaustVelocity);

/// <summary>What a step of lift adds to the car's velocity and its spin, in its own frame.</summary>
public readonly record struct LiftPush(double3 Velocity, double3 Spin);

/// <summary>
/// Four rockets under the car, as one push: thrust along the car's own up, through its centre of mass,
/// so it is balanced wherever the crew sit, and in the air a hold that keeps the car level or leans it
/// the way the stick asks.
/// </summary>
public static class Lift
{
    /// <summary>Thrust at full throttle, in weights of the car: half throttle hovers.</summary>
    public const double MaxG = 2.0;

    /// <summary>How far the stick leans the car, which is what moves it across the ground.</summary>
    public const double TiltRad = 0.26;

    public const double YawRateRadPerSec = 0.9;

    /// <summary>What a car's throttle is set to before anyone has touched it: a gentle climb.</summary>
    public const double DefaultThrottle = 0.6;

    /// <summary>How fast a held throttle key moves the throttle, a second: KSA's own rate.</summary>
    public const double ThrottleRatePerSec = 0.7;

    /// <summary>The throttle after a step of the throttle keys being held.</summary>
    public static double Ramp(double throttle, bool up, bool down, double dt) =>
        Math.Clamp(throttle + (((up ? 1.0 : 0.0) - (down ? 1.0 : 0.0)) * ThrottleRatePerSec * dt), 0.0, 1.0);

    /// <summary>
    /// A flame's gas, for KSA's plume to be drawn from: a small nozzle of the size of Core's auxiliary
    /// engine, its chamber pressure following the throttle so the flame grows with it. Only what the
    /// flame looks like; the thrust is <see cref="Step"/>'s.
    /// </summary>
    public static FlameGas Flame(double throttle)
    {
        float chamber = (float)(Math.Clamp(throttle, 0.0, 1.0) * 30e5);
        return new FlameGas(ExitRadius: 0.10f, AreaRatio: 4f, ChamberPressure: chamber, ChamberTemperature: 3000f,
                            ExitPressure: chamber * 0.04f, ExitTemperature: 1700f, Gamma: 1.21f,
                            SpecificGasConstant: 681f, ExhaustVelocity: 2500f);
    }

    // Stiff enough to hold the car level against a kick, damped just short of critical so it does not rock.
    private const double HoldStiffness = 12.0;
    private const double HoldDamping = 6.0;
    private const double YawDamping = 4.0;

    /// <summary>
    /// One step. On the ground only the thrust is given: the springs hold the car's attitude there, and
    /// a hold fighting them on a slope would rock it.
    /// </summary>
    public static LiftPush Step(LiftInput input, bool airborne, double3 up, double3 forward, double3 groundUp,
                                double3 spin, double gravity, double dt)
    {
        double throttle = Math.Clamp(input.Throttle, 0.0, 1.0);
        if (throttle <= 0.0) return default;

        up = Vec.Unit(up);
        double3 push = up * (throttle * MaxG * gravity * dt);
        if (!airborne) return new LiftPush(push, Vec.Zero);

        groundUp = Vec.Unit(groundUp);
        double3 ahead = Vec.Unit(Vec.RejectFrom(forward, groundUp));
        double3 leftward = Vec.Cross(groundUp, ahead);
        double3 wanted = Vec.Unit(groundUp + (ahead * Math.Tan(Math.Clamp(input.Pitch, -1.0, 1.0) * TiltRad))
                                  + (leftward * Math.Tan(Math.Clamp(input.Roll, -1.0, 1.0) * TiltRad)));

        double yawRate = Vec.Dot(spin, up);
        double3 tumble = spin - (up * yawRate);
        double3 turn = (Vec.Cross(up, wanted) * HoldStiffness) - (tumble * HoldDamping)
                       + (up * (YawDamping * ((Math.Clamp(input.Yaw, -1.0, 1.0) * YawRateRadPerSec) - yawRate)));
        return new LiftPush(push, turn * dt);
    }
}
