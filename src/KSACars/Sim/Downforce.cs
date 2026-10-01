using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// Rockets on the bonnet and the boot that fire upward and press the car onto the ground: a push along
/// the car's own down, through its centre of mass. Where gravity gives the tyres little to grip with,
/// as on Luna, this is what lets the car steer, brake and shove.
/// </summary>
public static class Downforce
{
    /// <summary>What the rockets add, in metres a second squared: about a g, whatever the body.</summary>
    public const double AccelMs2 = 10.0;

    /// <summary>What a step of downforce adds to the car's velocity, in its own frame.</summary>
    public static double3 Push(bool lit, double3 up, double dt) =>
        lit ? Vec.Unit(up) * (-AccelMs2 * dt) : Vec.Zero;

    /// <summary>
    /// The weight the springs and tyres carry, as an acceleration: gravity, and the rockets on top of it.
    /// The springs are sized off this, so pressed down the car rides at its usual height.
    /// </summary>
    public static double Load(bool lit, double gravity) => gravity + (lit ? AccelMs2 : 0.0);
}
