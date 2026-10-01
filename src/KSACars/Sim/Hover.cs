using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// The rockets under the car and the ones on top of it firing together: between them they carry the
/// car's weight and no more, so it hangs where it is. Leaning still moves it across the ground.
/// </summary>
public static class Hover
{
    /// <summary>How hard a climb or a fall is braked, a second: a 3 m/s climb is gone in about one.</summary>
    public const double ClimbDamping = 3.0;

    // Leant past this the rockets do not try to carry the whole weight: there is no thrust that would.
    private const double LeastUpright = 0.5;

    /// <summary>
    /// What a step of hovering adds to the car's velocity, in its own frame: along the car's own up,
    /// enough that its share along <paramref name="groundUp"/> cancels gravity and brakes
    /// <paramref name="climb"/>, the climb as the step starts, within what the two sets of rockets can give. The braking is the
    /// decay's own solution over the step, so a step of any length takes off part of the climb and
    /// never more than all of it.
    /// </summary>
    public static double3 Push(double3 up, double3 groundUp, double climb, double gravity, double dt)
    {
        up = Vec.Unit(up);
        double upright = Math.Max(Vec.Dot(up, Vec.Unit(groundUp)), LeastUpright);
        // The engine takes the push at the start of its step and lets gravity act through it, so the
        // car's climb over the step is half a step of gravity less than its climb right after the push.
        // That average is what is braked to nothing; braking the sampled climb leaves the car rising.
        double mean = climb + (0.5 * gravity * dt);
        double braked = mean * (1.0 - Math.Exp(-ClimbDamping * dt));
        double push = Math.Clamp(((gravity * dt) - braked) / upright, -Downforce.AccelMs2 * dt, Lift.MaxG * gravity * dt);
        return up * push;
    }
}
