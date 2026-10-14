using Xunit;

namespace KSACars.Tests;

// Going round a car that is in the way however it is: far off and standing, too near, coming backwards, beside another, or moving across the road.
public class OvertakeTests
{
    private const double Room = 3.3;

    private static Rival.Seen Car(string who, double ahead, double side, double speed) => new(who, ahead, side, speed);

    private static (double SideM, double CapMs) Look(Rival.Passing state, Rival.Seen[] cars, double speed = 22.0, double free = 50.0, double cross = 0.0,
                                                     double left = Room, double right = Room, double straight = 600.0, double dt = 1.0 / 60.0) =>
        Rival.Pass(state, cars, speed, free, cross, left, right, straight, dt);

    [Fact]
    public void AStandingCarFarAheadIsDrivenUpToAtSpeedAndNotAtACreep()
    {
        // 200 m ahead on a straight the road allows 80 m/s on: braked down to it in time, not crept up on for 200 m
        Rival.Passing state = new();
        (_, double cap) = Look(state, [Car("standing", 200.0, 0.0, 0.0)], speed: 50.0, free: 80.0, left: 6.0, right: 6.0);
        Assert.NotEqual(0, state.Side);
        Assert.True(cap > 30.0, $"held to {cap:F1} m/s 200 m short of it");

        // and near it, still behind it, at a creep
        Rival.Passing near = new();
        (_, double creep) = Look(near, [Car("standing", 12.0, 0.0, 0.0)], speed: 5.0, free: 80.0, left: 6.0, right: 6.0);
        Assert.InRange(creep, 0.0, Rival.CreepMs);
    }

    [Fact]
    public void TooNearACarItDropsBackRatherThanSittingOnItsTail()
    {
        Assert.True(Rival.FollowCap(3.0, 0.0, 15.0, 15.0) < 15.0, "held to the speed of a car 3 m ahead of its middle");
        Assert.Equal(15.0, Rival.FollowCap(8.0, 0.0, 15.0, 30.0), 9);
    }

    [Fact]
    public void ACarComingBackwardsIsFollowedFurtherBackThanOneStanding()
    {
        double standing = Rival.FollowCap(30.0, 0.0, 0.0, 10.0), coming = Rival.FollowCap(30.0, 0.0, -3.0, 10.0);
        Assert.True(coming < standing, $"{coming:F1} m/s behind a car reversing at 3 m/s and {standing:F1} behind one standing");
    }

    [Fact]
    public void TwoCarsDrivenSideBySideAreGoneRoundAsOne()
    {
        // one on the driver's line and one 2.5 m to its left, both at 12 m/s, and the road's room on the left
        Rival.Passing state = new();
        (double side, double cap) = Look(state, [Car("near", 11.0, 0.0, 12.0), Car("beside", 11.5, 2.5, 12.0)], speed: 12.0, free: 60.0, left: 9.0, right: 1.0);
        Assert.Equal(1, state.Side);
        Assert.True(side >= 2.5 + Rival.PassSideM - 1e-9, $"{side:F1} m out, which is not past the second car");

        // out past both, neither holds it
        (_, cap) = Look(state, [Car("near", 3.0, -5.5, 12.0), Car("beside", 3.5, -3.0, 12.0)], speed: 14.0, free: 60.0, cross: 5.5, left: 9.0, right: 1.0);
        Assert.True(cap > 20.0, $"held to {cap:F1} m/s out beside them");
    }

    [Fact]
    public void ACarWeavingAcrossTheRoadIsInTheWayWhereverItHasLatelyBeen()
    {
        // on the driver's line, and half a second later 3 m to its left: it may be back
        Rival.Passing state = new();
        Look(state, [Car("weaving", 30.0, 0.0, 8.0)], speed: 20.0, free: 25.0, left: 1.0, right: 1.0);
        (double side, double cap) = Look(state, [Car("weaving", 24.0, 3.0, 8.0)], speed: 20.0, free: 25.0, left: 1.0, right: 1.0, dt: 0.5);
        Assert.False(side == 0.0 && double.IsPositiveInfinity(cap), "taken to be out of the way 3 m to the side");
    }

    [Fact]
    public void ACarMovingAcrossIntoTheDriversLineIsInTheWayBeforeItIsThere()
    {
        // 6 m to the right and moving left at 6 m/s: in the driver's line before the driver is up to it
        Rival.Passing state = new();
        Look(state, [Car("crossing", 22.0, -6.0, 15.0)], speed: 22.0, free: 30.0, left: 1.0, right: 1.0);
        (double side, double cap) = Look(state, [Car("crossing", 20.25, -4.5, 15.0)], speed: 22.0, free: 30.0, left: 1.0, right: 1.0, dt: 0.25);
        Assert.False(side == 0.0 && double.IsPositiveInfinity(cap), "taken to be out of the way 4.5 m to the side");
    }
}
