using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// Two rockets on the tail, lit while the boost key is held: a push along the car's own forward,
/// through its centre of mass, so it neither lifts the nose nor turns the car.
/// </summary>
public static class Boost
{
    /// <summary>
    /// What the rockets add, in metres a second squared, whatever the gravity: where the ground gives the
    /// tyres little to push against, as on Luna, this is what moves a rock.
    /// </summary>
    public const double AccelMs2 = 12.0;

    /// <summary>What a step of boost adds to the car's velocity, in its own frame.</summary>
    public static double3 Push(bool lit, double3 forward, double dt) =>
        lit ? Vec.Unit(forward) * (AccelMs2 * dt) : Vec.Zero;
}
