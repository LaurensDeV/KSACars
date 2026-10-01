using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class HoverTests
{
    private static readonly double3 Up = new(1, 0, 0);
    private const double Dt = 0.02;

    // A car falling or climbing along the ground's up, with nothing but gravity and the hover on it.
    private static double ClimbAfter(double climb, double gravity, double3 carUp, double seconds)
    {
        for (double t = 0.0; t < seconds; t += Dt)
            climb += Vec.Dot(Hover.Push(carUp, Up, climb, gravity, Dt), Up) - (gravity * Dt);
        return climb;
    }

    [Theory]
    [InlineData(9.81)]
    [InlineData(1.62)]
    public void StandingStillItCarriesTheCarsWeightAndNoMore(double gravity)
    {
        double3 push = Hover.Push(Up, Up, 0.0, gravity, Dt);
        Assert.Equal(gravity * Dt, push.X, 9);
        Assert.Equal(0.0, ClimbAfter(0.0, gravity, Up, 5.0), 6);
    }

    [Theory]
    [InlineData(4.0)]
    [InlineData(-4.0)]
    public void AClimbOrAFallIsBrakedToNothing(double climb)
    {
        Assert.InRange(ClimbAfter(climb, 1.62, Up, 3.0), -0.05, 0.05);
        Assert.InRange(ClimbAfter(climb, 9.81, Up, 3.0), -0.05, 0.05);
    }

    [Fact]
    public void LeantOverItStillHoldsItsHeight()
    {
        var leant = new double3(Math.Cos(0.26), Math.Sin(0.26), 0.0);
        Assert.Equal(0.0, ClimbAfter(0.0, 9.81, leant, 3.0), 6);
        Assert.True(Hover.Push(leant, Up, 0.0, 9.81, Dt).Y > 0.0);
    }

    [Fact]
    public void ItNeverAsksForMoreThanTheRocketsHave()
    {
        Assert.Equal(Lift.MaxG * 9.81 * Dt, Hover.Push(Up, Up, -100.0, 9.81, Dt).X, 9);
        Assert.Equal(-Downforce.AccelMs2 * Dt, Hover.Push(Up, Up, 100.0, 9.81, Dt).X, 9);
    }
}
