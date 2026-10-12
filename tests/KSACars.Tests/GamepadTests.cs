using Xunit;

namespace KSACars.Tests;

public class GamepadTests
{
    private static PadState Rest => new(0, 0, 0, 0, -1, -1, PadButtons.None);

    [Fact]
    public void APadAtRestAsksNothing()
    {
        PadDrive d = Gamepad.Drive(Rest with { LeftX = 0.1, LeftY = -0.12, LeftTrigger = -0.95 });
        Assert.Equal(new DriveInput(0, 0), d.Input);
        Assert.Equal(0.0, d.Turn);
        Assert.False(d.Boost || d.ThrottleUp || d.ThrottleDown);
    }

    [Fact]
    public void TheRightTriggerDrivesAndTheLeftBrakes()
    {
        Assert.Equal(1.0, Gamepad.Drive(Rest with { RightTrigger = 1 }).Input.Throttle);
        Assert.Equal(-1.0, Gamepad.Drive(Rest with { LeftTrigger = 1 }).Input.Throttle);
        Assert.InRange(Gamepad.Drive(Rest with { RightTrigger = 0 }).Input.Throttle, 0.4, 0.6);
    }

    [Fact]
    public void TheStickSteersTheWayItIsPushedAndReachesFullLock()
    {
        Assert.Equal(1.0, Gamepad.Drive(Rest with { LeftX = -1 }).Input.Steer);
        Assert.Equal(-1.0, Gamepad.Drive(Rest with { LeftX = 1 }).Input.Steer);
        double half = Gamepad.Drive(Rest with { LeftX = -0.5 }).Input.Steer;
        Assert.InRange(half, 0.3, 0.5);
    }

    [Fact]
    public void ASteerHasNoStepAtTheDeadzonesEdge() =>
        Assert.InRange(Gamepad.Stick(Gamepad.StickDeadzone + 0.001), 0.0, 0.002);

    [Fact]
    public void TheKeysAndThePadAddAndNeverPassFull()
    {
        DriveInput both = Gamepad.Merge(new DriveInput(1, -1), new DriveInput(0.5, 0.25));
        Assert.Equal(new DriveInput(1, -0.75), both);
    }

    [Fact]
    public void AButtonIsPressedOnceHoweverLongItIsHeld()
    {
        Assert.Equal(PadButtons.Y, Gamepad.Pressed(PadButtons.A, PadButtons.A | PadButtons.Y));
        Assert.Equal(PadButtons.None, Gamepad.Pressed(PadButtons.A | PadButtons.Y, PadButtons.A | PadButtons.Y));
    }

    [Fact]
    public void TheRightStickIsTheViewsAndNeverTheCars()
    {
        PadDrive d = Gamepad.Drive(Rest with { RightX = 1, RightY = -1 });
        Assert.Equal(new DriveInput(0, 0), d.Input);
        Assert.Equal((1.0, -1.0), (d.LookX, d.LookY));
        Assert.Equal(0.0, d.Lean);
    }

    [Fact]
    public void AChaseComesRoundBehindTheCarTheShortWayAndNeverPastIt()
    {
        double azimuth = 3.0, heading = -3.0;
        for (int i = 0; i < 300; i++)
        {
            double next = ChaseView.Follow(azimuth, heading, 0.0, 1.0 / 60.0);
            Assert.True(next >= azimuth);
            azimuth = next;
        }
        Assert.Equal(heading + (2.0 * Math.PI), azimuth, 3);
        Assert.Equal(azimuth, ChaseView.Follow(azimuth, heading, 0.0, 100.0), 6);
    }

    [Fact]
    public void TheViewIsNeverTiltedUnderTheGroundOrOverTheTop()
    {
        Assert.Equal(ChaseView.HighestRad, ChaseView.Tilt(-1.0, 1.0, 10.0));
        Assert.Equal(ChaseView.LowestRad, ChaseView.Tilt(-1.0, -1.0, 10.0));
    }

    [Fact]
    public void TheViewLooksIntoACornerTheWayTheCarTurnsAndNoFurtherThanItsMost()
    {
        Assert.True(ChaseView.Lead(0.4) > 0.0);
        Assert.Equal(-ChaseView.Lead(0.4), ChaseView.Lead(-0.4));
        Assert.Equal(ChaseView.MostLeadRad, ChaseView.Lead(100.0));
        Assert.Equal(0.0, ChaseView.Lead(0.0));
    }
}
