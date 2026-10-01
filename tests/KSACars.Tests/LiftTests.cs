using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class LiftTests
{
    private static readonly double3 Up = new(1, 0, 0);
    private static readonly double3 Forward = new(0, 1, 0);
    private static readonly double3 Left = new(0, 0, 1);
    private const double G = 9.81;
    private const double Dt = 1.0 / 60.0;

    [Fact]
    public void OnlyTheEldoradoFlies()
    {
        Assert.True(BuggyProfile.Eldorado.HasRockets);
        Assert.False(BuggyProfile.Manx.HasRockets);
    }

    [Fact]
    public void TheFourNozzlesAreUnderTheFloorAndBalancedAboutTheCentreLine()
    {
        BuggyProfile p = BuggyProfile.Eldorado;
        Assert.Equal(4, p.RocketNozzles.Length);
        Assert.Equal(0.0, p.RocketNozzles.Sum(n => n.Y), 9);
        Assert.Equal(0.0, p.RocketNozzles.Sum(n => n.Z), 9);
        Assert.All(p.RocketNozzles, n => Assert.True(n.X < p.Corners.Min(c => c.Hub.X)));
        Assert.All(p.RocketNozzles, n => Assert.InRange(n.Y, p.Corners.Min(c => c.Hub.Y), p.Corners.Max(c => c.Hub.Y)));
    }

    [Fact]
    public void TheFlameGrowsWithTheThrottleAndIsOutWithoutIt()
    {
        Assert.Equal(0f, Lift.Flame(0).ChamberPressure);
        Assert.True(Lift.Flame(1).ChamberPressure > Lift.Flame(0.5).ChamberPressure);
        Assert.True(Lift.Flame(1).ExitPressure < Lift.Flame(1).ChamberPressure);
    }

    [Fact]
    public void NoThrottleIsNoPush() =>
        Assert.Equal(default, Lift.Step(new LiftInput(0, 1, 1, 1), true, Up, Forward, Up, new double3(1, 1, 1), G, Dt));

    [Fact]
    public void TheThrottleThatHoversCancelsGravityExactly()
    {
        LiftPush push = Lift.Step(new LiftInput(1.0 / Lift.MaxG, 0, 0, 0), true, Up, Forward, Up, Vec.Zero, G, Dt);
        Assert.Equal(G * Dt, push.Velocity.X, 12);
        Assert.Equal(0.0, Vec.Len(push.Spin), 12);
    }

    [Fact]
    public void ThrustFollowsTheCarsOwnUpNotTheGrounds()
    {
        double3 leaning = Vec.Unit(new double3(1, 0.3, 0));
        LiftPush push = Lift.Step(new LiftInput(1, 0, 0, 0), false, leaning, Forward, Up, Vec.Zero, G, Dt);
        Assert.True(Vec.Len(Vec.Unit(push.Velocity) - leaning) < 1e-9);
    }

    [Fact]
    public void OnTheGroundTheSpringsKeepTheAttitude()
    {
        LiftPush push = Lift.Step(new LiftInput(1, 1, 1, 1), false, Up, Forward, Vec.Unit(new double3(1, 0, 0.3)),
                                  new double3(0.5, 0.5, 0.5), G, Dt);
        Assert.Equal(0.0, Vec.Len(push.Spin), 12);
    }

    // A box in free flight under the hold alone, the car's frame carried in the attitude.
    private static (double Tilt, double Spin) Fly(doubleQuat attitude, double3 spinWorld, LiftInput input, double seconds)
    {
        for (double t = 0; t < seconds; t += Dt)
        {
            doubleQuat toBody = doubleQuat.Inverse(attitude);
            LiftPush push = Lift.Step(input, true, Up, Forward, (toBody * Up), (toBody * spinWorld), G, Dt);
            spinWorld += (attitude * push.Spin);
            double rate = Vec.Len(spinWorld);
            if (rate > 1e-12)
            {
                attitude = doubleQuat.Normalize(doubleQuat.CreateFromAxisAngle(spinWorld / rate, rate * Dt) * attitude);
            }
        }
        return (Vec.AngleBetween((attitude * Up), Up), Vec.Len(spinWorld));
    }

    [Fact]
    public void ATiltedCarComesLevelAndStaysThere()
    {
        (double tilt, double spin) = Fly(doubleQuat.CreateFromAxisAngle(Left, 0.5), Vec.Zero, new LiftInput(0.5, 0, 0, 0), 4.0);
        Assert.True(tilt < 0.01, $"still {tilt * 180 / Math.PI:F1} deg off level");
        Assert.True(spin < 0.01);
    }

    [Fact]
    public void ATumbleDiesAway()
    {
        (double tilt, double spin) = Fly(doubleQuat.Identity, new double3(0, 1.5, 1.0), new LiftInput(0.5, 0, 0, 0), 4.0);
        Assert.True(tilt < 0.01 && spin < 0.01, $"{tilt:F3} rad, {spin:F3} rad/s");
    }

    [Fact]
    public void TheStickLeansTheNoseDownSoTheThrustPullsForward()
    {
        doubleQuat attitude = doubleQuat.Identity;
        double3 spin = Vec.Zero;
        for (int i = 0; i < 240; i++)
        {
            doubleQuat toBody = doubleQuat.Inverse(attitude);
            LiftPush push = Lift.Step(new LiftInput(0.5, 1, 0, 0), true, Up, Forward, (toBody * Up), (toBody * spin), G, Dt);
            spin += (attitude * push.Spin);
            double rate = Vec.Len(spin);
            if (rate > 1e-12) attitude = doubleQuat.Normalize(doubleQuat.CreateFromAxisAngle(spin / rate, rate * Dt) * attitude);
        }
        double3 thrust = (attitude * Up);
        Assert.Equal(Lift.TiltRad, Vec.AngleBetween(thrust, Up), 2);
        Assert.True(Vec.Dot(thrust, Forward) > 0.2);
    }

    [Fact]
    public void BankingLeftLeansTheThrustToTheLeftAndLeavesTheNoseWhereItWas()
    {
        doubleQuat attitude = doubleQuat.Identity;
        double3 spin = Vec.Zero;
        for (int i = 0; i < 240; i++)
        {
            doubleQuat toBody = doubleQuat.Inverse(attitude);
            LiftPush push = Lift.Step(new LiftInput(0.5, 0, 1, 0), true, Up, Forward, toBody * Up, toBody * spin, G, Dt);
            spin += attitude * push.Spin;
            double rate = Vec.Len(spin);
            if (rate > 1e-12) attitude = doubleQuat.Normalize(doubleQuat.CreateFromAxisAngle(spin / rate, rate * Dt) * attitude);
        }
        double3 thrust = attitude * Up;
        Assert.Equal(Lift.TiltRad, Vec.AngleBetween(thrust, Up), 2);
        Assert.True(Vec.Dot(thrust, Left) > 0.2);
        Assert.True(Math.Abs(Vec.Dot(attitude * Forward, Left)) < 0.02);
    }

    [Fact]
    public void AHeldThrottleKeyMovesTheThrottleAtKsasRateAndStopsAtTheEnds()
    {
        Assert.Equal(0.5 + (Lift.ThrottleRatePerSec * 0.1), Lift.Ramp(0.5, up: true, down: false, 0.1), 9);
        Assert.Equal(0.5 - (Lift.ThrottleRatePerSec * 0.1), Lift.Ramp(0.5, up: false, down: true, 0.1), 9);
        Assert.Equal(0.5, Lift.Ramp(0.5, up: true, down: true, 0.1), 9);
        Assert.Equal(1.0, Lift.Ramp(0.98, up: true, down: false, 1.0), 9);
        Assert.Equal(0.0, Lift.Ramp(0.02, up: false, down: true, 1.0), 9);
    }

    [Fact]
    public void YawLeftTurnsTheNoseTowardsTheLeft()
    {
        LiftPush push = Lift.Step(new LiftInput(0.5, 0, 0, 1), true, Up, Forward, Up, Vec.Zero, G, Dt);
        Assert.True(Vec.Dot(push.Spin, Up) > 0.0);
        // About +X a positive turn carries +Y towards +Z, which is the car's left.
        Assert.True(Vec.Dot((doubleQuat.CreateFromAxisAngle(Up, 0.1) * Forward), Left) > 0.0);
    }
}
