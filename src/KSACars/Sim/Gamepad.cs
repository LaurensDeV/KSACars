namespace KSACars;

[Flags]
public enum PadButtons
{
    None = 0,
    A = 1 << 0,
    B = 1 << 1,
    X = 1 << 2,
    Y = 1 << 3,
    LeftBumper = 1 << 4,
    RightBumper = 1 << 5,
    Back = 1 << 6,
    Start = 1 << 7,
    DpadUp = 1 << 11,
    DpadRight = 1 << 12,
    DpadLeft = 1 << 14,
}

/// <summary>
/// A gamepad as it is read: sticks from -1 to 1 with right and down positive, triggers from -1 at rest
/// to 1 pulled home, which is how GLFW gives them.
/// </summary>
public readonly record struct PadState(double LeftX, double LeftY, double RightX, double RightY,
                                       double LeftTrigger, double RightTrigger, PadButtons Buttons);

/// <summary>What a gamepad asks of a car this step.</summary>
/// <param name="Turn">The roll keys' share: -1 right, +1 left.</param>
/// <param name="Lean">The pitch keys' share for a car in the air: +1 nose down.</param>
/// <param name="LookX">The view turned: +1 to the right.</param>
/// <param name="LookY">The view tilted: +1 with the stick pulled back.</param>
public readonly record struct PadDrive(DriveInput Input, double Turn, double Lean, bool Boost, bool ThrottleUp, bool ThrottleDown,
                                       double LookX, double LookY);

/// <summary>
/// A gamepad as a car's controls: the right trigger is the throttle and the left the brake and reverse,
/// the left stick steers and, pushed away or pulled back, leans a car in the air, the D-pad turns one there,
/// and the right stick is the view's.
/// </summary>
public static class Gamepad
{
    /// <summary>How far a stick is off its centre before it counts, which a worn stick rests inside.</summary>
    public const double StickDeadzone = 0.15;

    /// <summary>The share of a trigger's travel that is slack at its rest.</summary>
    public const double TriggerDeadzone = 0.05;

    /// <summary>A stick's axis with its dead centre taken out, and the rest stretched to reach 1.</summary>
    public static double Stick(double raw)
    {
        double size = Math.Abs(raw);
        if (!(size > StickDeadzone)) return 0.0;
        return Math.Sign(raw) * Math.Min((size - StickDeadzone) / (1.0 - StickDeadzone), 1.0);
    }

    /// <summary>A trigger from 0 at rest to 1 pulled home.</summary>
    public static double Trigger(double raw)
    {
        double pulled = (raw + 1.0) / 2.0;
        if (!(pulled > TriggerDeadzone)) return 0.0;
        return Math.Min((pulled - TriggerDeadzone) / (1.0 - TriggerDeadzone), 1.0);
    }

    public static PadDrive Drive(PadState pad)
    {
        double throttle = Trigger(pad.RightTrigger) - Trigger(pad.LeftTrigger);
        double turn = (pad.Buttons.HasFlag(PadButtons.DpadLeft) ? 1.0 : 0.0) - (pad.Buttons.HasFlag(PadButtons.DpadRight) ? 1.0 : 0.0);
        return new PadDrive(new DriveInput(throttle, -Stick(pad.LeftX)), turn, -Stick(pad.LeftY),
                            pad.Buttons.HasFlag(PadButtons.A),
                            pad.Buttons.HasFlag(PadButtons.RightBumper),
                            pad.Buttons.HasFlag(PadButtons.LeftBumper),
                            Stick(pad.RightX), Stick(pad.RightY));
    }

    /// <summary>The keys and the pad together, so either drives and neither has to be let go of.</summary>
    public static DriveInput Merge(DriveInput keys, DriveInput pad) =>
        new(Math.Clamp(keys.Throttle + pad.Throttle, -1.0, 1.0), Math.Clamp(keys.Steer + pad.Steer, -1.0, 1.0),
            Math.Max(keys.Brake, pad.Brake));

    /// <summary>The buttons that went down since the read before.</summary>
    public static PadButtons Pressed(PadButtons before, PadButtons now) => now & ~before;
}
