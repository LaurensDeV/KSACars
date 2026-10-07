using Brutal.Numerics;

namespace KSACars;

public enum BeamSetting
{
    Off,
    Low,
    High,
}

/// <summary>One lamp's beam: how far it throws, how bright, its cone, and how far below level it points.</summary>
public readonly record struct BeamShape(float Range, float Intensity, float InnerAngle, float OuterAngle, double DipRad);

/// <summary>
/// A car's headlamps, as the spotlights KSA is asked to draw: a dipped beam that lights the ground
/// ahead and is cut off below the horizon, and a main beam that is narrower, level and far-reaching.
/// </summary>
public static class Headlights
{
    /// <summary>A tungsten sealed beam, warm rather than white.</summary>
    public static readonly float3 Colour = new(1.0f, 0.90f, 0.72f);

    public static readonly BeamShape Low = new(Range: 45f, Intensity: 60f, InnerAngle: 0.60f, OuterAngle: 1.20f, DipRad: 0.14);
    // KSA takes both angles as the whole cone, edge to edge.
    public static readonly BeamShape High = new(Range: 160f, Intensity: 400f, InnerAngle: 0.14f, OuterAngle: 0.40f, DipRad: 0.02);

    /// <summary>The beam a setting gives, or null with the lamps off.</summary>
    public static BeamShape? Shape(BeamSetting setting) => setting switch
    {
        BeamSetting.Low => Low,
        BeamSetting.High => High,
        _ => null,
    };

    /// <summary>A tail lamp's red, and how it spills onto the ground behind the car.</summary>
    public static readonly float3 TailColour = new(1.0f, 0.05f, 0.03f);

    public static readonly BeamShape Tail = new(Range: 7f, Intensity: 6f, InnerAngle: 0.5f, OuterAngle: 1.1f, DipRad: 0.35);

    /// <summary>How much brighter the tail lamps burn with the brakes on.</summary>
    public const float BrakeBoost = 4f;

    /// <summary>The brakes are on when the driver asks for the opposite of the way the car is rolling.</summary>
    public static bool Braking(double throttle, double forwardSpeed) =>
        (throttle < 0.0 && forwardSpeed > 0.5) || (throttle > 0.0 && forwardSpeed < -0.5);

    /// <summary>A brake lens's colour: as given while braking, an eighth as bright otherwise.</summary>
    public static uint BrakeLens(uint rgb, bool braking) =>
        braking ? rgb : (((rgb >> 16) & 0xFF) >> 3 << 16) | (((rgb >> 8) & 0xFF) >> 3 << 8) | ((rgb & 0xFF) >> 3);

    /// <summary>
    /// Keeps the panel's setting and the part's own light switch in step, whichever was moved.
    /// <paramref name="seen"/> is what the switch read last frame; a switch that differs from it was
    /// thrown in KSA's part window, and wins.
    /// </summary>
    public static (BeamSetting Beam, bool Switch) Reconcile(BeamSetting beam, bool switchNow, bool seen)
    {
        if (switchNow != seen)
        {
            return (switchNow ? (beam == BeamSetting.Off ? BeamSetting.Low : beam) : BeamSetting.Off, switchNow);
        }
        return (beam, beam != BeamSetting.Off);
    }

    /// <summary>Where a beam points: ahead, tipped below level by its dip.</summary>
    public static double3 Aim(double3 up, double3 forward, double dipRad) =>
        Vec.Unit((Vec.Unit(forward) * Math.Cos(dipRad)) - (Vec.Unit(up) * Math.Sin(dipRad)));

    /// <summary>
    /// What the lamps are doing, as the bridge and the panel name it; anything else is null.
    /// </summary>
    public static BeamSetting? Parse(string text) => text.Trim().ToLowerInvariant() switch
    {
        "off" => BeamSetting.Off,
        "low" or "on" => BeamSetting.Low,
        "high" => BeamSetting.High,
        _ => null,
    };
}
