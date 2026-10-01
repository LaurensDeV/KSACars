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
}
