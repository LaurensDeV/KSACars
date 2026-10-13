using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class HoverTests
{
    private static readonly double3 Up = new(1, 0, 0);
    private const double Dt = 0.02;

    // A car falling or climbing along the ground's up, stepped as the engine steps it: the push at the
    // start of the step, then gravity through it. Returns the climb as each step starts, and the
    // height gained in all.
    private static (double Climb, double Rise) Fly(double climb, double gravity, double3 carUp, double dt, int steps)
    {
        double rise = 0.0;
        for (int i = 0; i < steps; i++)
        {
            double pushed = climb + Vec.Dot(Hover.Push(carUp, Up, climb, gravity, dt), Up);
            rise += (pushed * dt) - (0.5 * gravity * dt * dt);
            climb = pushed - (gravity * dt);
        }
        return (climb, rise);
    }

    [Theory]
    [InlineData(9.81, 0.02)]
    [InlineData(1.62, 0.02)]
    [InlineData(9.81, 1.0)]
    public void HoldingStillItNeitherRisesNorSinksWhateverTheStep(double gravity, double dt)
    {
        // as a step starts the car is falling at half a step of gravity, which the push turns round
        (double climb, double rise) = Fly(-0.5 * gravity * dt, gravity, Up, dt, 500);
        Assert.Equal(-0.5 * gravity * dt, climb, 6);
        Assert.Equal(0.0, rise, 6);
    }

    [Theory]
    [InlineData(4.0, 0.02)]
    [InlineData(-4.0, 0.02)]
    [InlineData(4.0, 0.5)]
    [InlineData(-4.0, 2.0)]
    [InlineData(4.0, 20.0)]
    public void AClimbOrAFallIsBrakedToNothingWhateverTheStep(double climb, double dt)
    {
        int steps = (int)Math.Ceiling(10.0 / dt) + 10;
        (_, double early) = Fly(climb, 9.81, Up, dt, steps);
        (_, double late) = Fly(climb, 9.81, Up, dt, steps * 2);
        Assert.InRange(late - early, -0.01, 0.01);
        Assert.True(Math.Sign(early) == Math.Sign(climb) || Math.Abs(early) < 0.5 * 9.81 * dt * dt);
    }

    [Fact]
    public void LeantOverItStillHoldsItsHeight()
    {
        var leant = new double3(Math.Cos(0.26), Math.Sin(0.26), 0.0);
        (_, double rise) = Fly(-0.5 * 9.81 * Dt, 9.81, leant, Dt, 200);
        Assert.Equal(0.0, rise, 6);
        Assert.True(Hover.Push(leant, Up, 0.0, 9.81, Dt).Y > 0.0);
    }

    [Fact]
    public void ItNeverAsksForMoreThanTheRocketsHave()
    {
        Assert.Equal(Lift.MaxG * 9.81 * Dt, Hover.Push(Up, Up, -100.0, 9.81, Dt).X, 9);
        Assert.Equal(-Downforce.AccelMs2 * Dt, Hover.Push(Up, Up, 100.0, 9.81, Dt).X, 9);
    }

    // A car hovering over a turning planet at 45 degrees north, in the frame that turns with it, stepped
    // as the engine steps it: the push, then gravity and the frame's own accelerations through the step.
    // Returns how far across the ground it has gone and how fast it is going across it.
    private static (double Drift, double Speed) HoverOverEarth(bool held, bool leaning, double3 velocity, double dt, double seconds)
    {
        const double radius = 6.371e6, gravity = 9.81;
        var spin = new double3(0, 0, 7.292e-5);
        double3 start = new double3(Math.Cos(Math.PI / 4), 0, Math.Sin(Math.PI / 4)) * radius;
        double3 at = start;
        for (double t = 0.0; t < seconds; t += dt)
        {
            double3 groundUp = Vec.Unit(at);
            velocity += Hover.Push(groundUp, groundUp, Vec.Dot(velocity, groundUp), gravity, dt);
            if (held) velocity += Hover.Hold(velocity, groundUp, Hover.FrameAccel(spin, at, velocity), leaning, dt);
            double3 accel = (groundUp * -gravity) + Hover.FrameAccel(spin, at, velocity);
            at += (velocity * dt) + (accel * (0.5 * dt * dt));
            velocity += accel * dt;
        }
        double3 up = Vec.Unit(start);
        return (Vec.Len(Vec.RejectFrom(at - start, up)), Vec.Len(Vec.RejectFrom(velocity, up)));
    }

    [Theory]
    [InlineData(0.02)]
    [InlineData(1.0)]
    public void ThePlanetDoesNotTurnAwayUnderAHover(double dt)
    {
        (double loose, _) = HoverOverEarth(held: false, leaning: false, Vec.Zero, dt, 120.0);
        Assert.True(loose > 50.0, $"only held up, it should slide towards the equator, and went {loose:F1} m");

        (double drift, double speed) = HoverOverEarth(held: true, leaning: false, Vec.Zero, dt, 120.0);
        Assert.True(drift < 0.05, $"drifted {drift:F3} m in two minutes");
        Assert.True(speed < 0.02, $"still going {speed:F4} m/s across the ground");
    }

    [Fact]
    public void LetGoOfTheLeanItComesToRestAndLeaningItKeepsItsSpeed()
    {
        var north = new double3(-Math.Sin(Math.PI / 4), 0, Math.Cos(Math.PI / 4));
        (_, double let) = HoverOverEarth(held: true, leaning: false, north * 10.0, Dt, 10.0);
        Assert.True(let < 0.01, $"still going {let:F2} m/s ten seconds after the lean was let go");

        (_, double leant) = HoverOverEarth(held: true, leaning: true, north * 10.0, Dt, 10.0);
        Assert.InRange(leant, 9.99, 10.01);
    }
}
