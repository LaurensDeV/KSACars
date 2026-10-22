using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class SteeringGripTests
{
    private static readonly BuggyProfile P = BuggyProfile.Manx;

    // The seated kitten's shoulders and arm, measured off KSA_Cat's seated idle: model space, cm.
    private static readonly double3 RightShoulder = new(-13.1, 44.0, -3.0);
    private static readonly double3 LeftShoulder = new(13.1, 44.0, -3.0);
    private const double ArmCm = 7.4 + 7.3;

    [Fact]
    public void TheWheelIsWhereTheKittensHandsCanReachIt()
    {
        double3 centre = SteeringGrip.ToKittenModel(P, P.SteeringPivot);
        Assert.Equal(0.0, centre.X, 6);
        Assert.InRange(centre.Y, 34.0, 41.0);
        Assert.InRange(centre.Z, 10.0, 16.0);
    }

    [Theory]
    [InlineData(-32.0)]
    [InlineData(-10.0)]
    [InlineData(0.0)]
    [InlineData(10.0)]
    [InlineData(32.0)]
    public void EveryGripAcrossTheLockIsWithinAnArmsLength(double steerDeg)
    {
        (double3 left, double3 right) = SteeringGrip.GripsPart(P, steerDeg * Math.PI / 180.0);
        foreach ((double3 grip, double3 shoulder) in new[] { (left, LeftShoulder), (right, RightShoulder) })
        {
            double3 g = SteeringGrip.ToKittenModel(P, grip);
            double3 wrist = g + (Vec.Unit(shoulder - g) * (SteeringGrip.PalmMetres * 100.0));
            Assert.True(Vec.Len(wrist - shoulder) < ArmCm * 0.98, $"{Vec.Len(wrist - shoulder):F1} cm at {steerDeg} deg");
        }
    }

    [Theory]
    [InlineData(-35.0)]
    [InlineData(-8.0)]
    [InlineData(0.0)]
    [InlineData(8.0)]
    [InlineData(35.0)]
    public void TheEldoradosDriverReachesItsBigWheel(double steerDeg)
    {
        BuggyProfile e = BuggyProfile.Eldorado;
        (double3 left, double3 right) = SteeringGrip.GripsPart(e, steerDeg * Math.PI / 180.0);
        foreach ((double3 grip, double3 shoulder) in new[] { (left, LeftShoulder), (right, RightShoulder) })
        {
            double3 g = SteeringGrip.ToKittenModel(e, grip);
            double3 wrist = g + (Vec.Unit(shoulder - g) * (SteeringGrip.PalmMetres * 100.0));
            Assert.True(Vec.Len(wrist - shoulder) < ArmCm * 0.98, $"{Vec.Len(wrist - shoulder):F1} cm at {steerDeg} deg");
        }
    }

    [Theory]
    [InlineData(-20.0)]
    [InlineData(-6.0)]
    [InlineData(0.0)]
    [InlineData(6.0)]
    [InlineData(20.0)]
    public void TheF2004sDriverReachesItsWheel(double steerDeg)
    {
        BuggyProfile f = BuggyProfile.F2004;
        (double3 left, double3 right) = SteeringGrip.GripsPart(f, steerDeg * Math.PI / 180.0);
        foreach ((double3 grip, double3 shoulder) in new[] { (left, LeftShoulder), (right, RightShoulder) })
        {
            double3 g = SteeringGrip.ToKittenModel(f, grip);
            double3 wrist = g + (Vec.Unit(shoulder - g) * (SteeringGrip.PalmMetres * 100.0));
            Assert.True(Vec.Len(wrist - shoulder) < ArmCm * 0.98, $"{Vec.Len(wrist - shoulder):F1} cm at {steerDeg} deg");
        }
    }

    [Fact]
    public void EachHandIsOnItsOwnSideAndTurningLeftLiftsTheRightHand()
    {
        (double3 left, double3 right) = SteeringGrip.GripsPart(P, 0.0);
        Assert.True(SteeringGrip.ToKittenModel(P, left).X > 5.0);
        Assert.True(SteeringGrip.ToKittenModel(P, right).X < -5.0);

        (_, double3 turned) = SteeringGrip.GripsPart(P, 20.0 * Math.PI / 180.0);
        Assert.True(SteeringGrip.ToKittenModel(P, turned).Y > SteeringGrip.ToKittenModel(P, right).Y + 2.0);
    }

    [Fact]
    public void TheSolvedArmKeepsItsLengthsAndBendsTheWayItIsTold()
    {
        double3 s = RightShoulder, target = new(-9.0, 36.0, 8.0), bend = new(-0.5, -1.0, 0.0);
        (double3 elbow, double3 wrist) = SteeringGrip.SolveElbow(s, target, 7.4, 7.3, bend);

        Assert.Equal(7.4, Vec.Len(elbow - s), 6);
        Assert.Equal(7.3, Vec.Len(wrist - elbow), 6);
        Assert.True(Vec.Len(wrist - target) < 1e-6);

        double3 mid = (s + wrist) * 0.5;
        Assert.True(Vec.Dot(elbow - mid, bend) > 0.0, "the elbow bent away from where it was told");
    }

    [Fact]
    public void ATargetOutOfReachIsReachedTowardRatherThanOverstretched()
    {
        double3 s = RightShoulder;
        (double3 elbow, double3 wrist) = SteeringGrip.SolveElbow(s, s + new double3(0, 0, 40.0), 7.4, 7.3, new double3(0, -1, 0));

        Assert.True(Vec.Len(wrist - s) <= 14.7 * 0.98 + 1e-9);
        Assert.Equal(7.4, Vec.Len(elbow - s), 6);
        Assert.Equal(7.3, Vec.Len(wrist - elbow), 6);
    }
}
