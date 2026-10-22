using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class BarrierTests
{
    // A car 4 m long and 1.8 m wide, up X, ahead Y, left Z, as a part is: its four corners, and a wall along its left.
    private static readonly double3[] Corners = [new(0, 1.5, 0.9), new(0, 1.5, -0.9), new(0, -1.5, 0.9), new(0, -1.5, -0.9)];
    private static readonly double3 Left = new(0, 0, 1);
    private const double Mass = 600.0;
    private static readonly (double3, double3, double3) Inertia = (new(1.0 / 400.0, 0, 0), new(0, 1.0 / 250.0, 0), new(0, 0, 1.0 / 900.0));

    private static DriveImpulse Held(double3 velocity, double[] past, double dt = 1.0 / 60.0) =>
        Barrier.Hold(Corners, [Left, default, Left, default], past, velocity, default, Mass, Inertia, dt);

    [Fact]
    public void ACarClearOfTheWallIsNotPushed()
    {
        Assert.Equal(default, Held(new double3(0, 30, 2), [-0.1, 0, -0.1, 0]));
    }

    [Fact]
    public void ACarGoingIntoTheWallIsStoppedGoingInAndNotStoppedGoingAlong()
    {
        // Both left corners at the wall, 30 m/s along it and 2 m/s into it.
        DriveImpulse push = Held(new double3(0, 30, 2), [0.001, 0, 0.001, 0]);
        double3 after = new double3(0, 30, 2) + (push.Linear / Mass);

        Assert.True(after.Z <= 0.0 && after.Z > -0.5, $"into the wall at {after.Z:F2} m/s after");

        // What it lost along the wall is the friction's share of what it lost into it, and no more.
        double into = 2.0 - after.Z, along = 30.0 - after.Y;
        Assert.InRange(along, 0.9 * Barrier.Friction * into, 1.1 * Barrier.Friction * into);
        Assert.True(along < 1.0, $"{along:F2} m/s lost along the wall to a 2 m/s touch");
    }

    [Fact]
    public void ACarLeantOnTheWallIsSlowedByAShareOfHowHardItLeans()
    {
        // Pushed into the wall at half a g for a second, as a line beyond it asks: slowed by the friction's share of that.
        double3 velocity = new(0, 30, 0);
        double dt = 1.0 / 60.0, lean = 0.5 * 9.81;
        for (int step = 0; step < 60; step++)
        {
            velocity += Left * (lean * dt);
            velocity += Held(velocity, [0.0005, 0, 0.0005, 0], dt).Linear / Mass;
        }
        Assert.InRange(30.0 - velocity.Y, 0.8 * Barrier.Friction * lean, 1.2 * Barrier.Friction * lean);
        Assert.True(Math.Abs(velocity.Z) < 0.2, $"{velocity.Z:F2} m/s into the wall");
    }

    [Fact]
    public void ANoseThatTouchesIsTurnedAwayFromTheWall()
    {
        // Only the front left corner at the wall: the push is behind... ahead of the centre of mass, and turns the nose right.
        DriveImpulse push = Held(new double3(0, 30, 2), [0.001, 0, -0.5, 0]);
        double yaw = Vec.Dot(new double3(push.Angular.X / 400.0, 0, 0), new double3(1, 0, 0));
        Assert.True(yaw < 0.0, $"the nose is turned {yaw:F3} rad/s to the left, into the wall");
    }

    [Fact]
    public void ACarInTheWallIsEasedOutAndNotThrown()
    {
        DriveImpulse push = Held(new double3(0, 30, 0), [0.4, 0, 0.4, 0]);
        double out_ = -(push.Linear.Z / Mass);
        Assert.InRange(out_, 0.5, Barrier.MostPushOutMs + 0.01);
    }

    [Fact]
    public void OnABankedDeckWhatTheSpringsLiftTheCarByIsNotTakenForGoingIntoTheWall()
    {
        // A deck banked 19 degrees, the wall on its high side: level and out is partly the car's own up.
        double bank = 19.0 * Math.PI / 180.0;
        double3 level = new(Math.Sin(bank), 0, Math.Cos(bank)), up = new(1, 0, 0);
        double3 across = Barrier.Across(level, up);
        Assert.Equal(0.0, Vec.Dot(across, up), 12);
        Assert.Equal(1.0, Vec.Len(across), 12);

        // Running along the wall with a step of the springs' lift in hand, as a car is before gravity is taken off.
        double3 velocity = new(0.2, 30, 0);
        double[] past = [1e-5, 0, 1e-5, 0];
        DriveImpulse held = Barrier.Hold(Corners, [across, default, across, default], past, velocity, default, Mass, Inertia, 1.0 / 60.0);
        Assert.True(Vec.Len(held.Linear) / Mass < 0.01, $"pushed {Vec.Len(held.Linear) / Mass:F3} m/s by a wall it is not going into");

        // Which the level way out does take it for, and rubs the car for it every step.
        DriveImpulse wrong = Barrier.Hold(Corners, [level, default, level, default], past, velocity, default, Mass, Inertia, 1.0 / 60.0);
        Assert.True(Vec.Len(wrong.Linear) / Mass > 0.05, $"{Vec.Len(wrong.Linear) / Mass:F3} m/s");

        Assert.Equal(Vec.Zero, Barrier.Across(up, up));
    }
}
