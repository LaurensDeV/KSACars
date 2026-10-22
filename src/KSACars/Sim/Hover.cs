using Brutal.Numerics;

namespace KSACars;

/// <summary>
/// The rockets under the car and the ones on top of it firing together: between them they carry the
/// car's weight and no more, so it hangs where it is, over the ground and not over the stars: a planet
/// turns under what is only held up. Leaning still moves it across the ground.
/// </summary>
public static class Hover
{
    /// <summary>How hard a climb or a fall is braked, a second: a 3 m/s climb is gone in about one.</summary>
    public const double ClimbDamping = 3.0;

    /// <summary>How hard its speed over the ground is braked with no lean asked, a second.</summary>
    public const double DriftDamping = 1.0;

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

    /// <summary>
    /// What a turning body's frame does to a craft off its ground, as an acceleration in that frame:
    /// thrown outward from the axis, and turned aside by its own speed. <paramref name="spin"/> is the
    /// body's, and the place and the velocity are in the frame that turns with it.
    /// </summary>
    public static double3 FrameAccel(double3 spin, double3 position, double3 velocity) =>
        -Vec.Cross(spin, Vec.Cross(spin, position)) - (Vec.Cross(spin, velocity) * 2.0);

    /// <summary>
    /// What a step of hovering adds to hold the car over the ground: <paramref name="frameAccel"/>
    /// taken out, which would slide it towards the equator, and with no lean asked its speed across the
    /// ground braked, by the decay's own solution so a step of any length takes off no more than all of it.
    /// </summary>
    public static double3 Hold(double3 velocity, double3 groundUp, double3 frameAccel, bool leaning, double dt)
    {
        double3 push = frameAccel * -dt;
        if (leaning) return push;
        // The engine lets the frame act through its step after the push, as it does gravity, so it is
        // the step's average speed that is braked: braking the sampled one creeps half a step of it a step.
        double3 mean = Vec.RejectFrom(velocity + (frameAccel * (-0.5 * dt)), Vec.Unit(groundUp));
        return push - (mean * (1.0 - Math.Exp(-DriftDamping * dt)));
    }
}
