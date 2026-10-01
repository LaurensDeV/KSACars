using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class RightingTests
{
    private static readonly double3 Up = new(1, 0, 0);
    private static readonly double3 Forward = new(0, 1, 0);

    private static double3[] Hubs(BuggyProfile p, double comAboveHubs) =>
        [.. p.Corners.Select(c => c.Hub - new double3(p.Corners[0].Hub.X + comAboveHubs, 0, 0))];

    public static TheoryData<string> Cars => [.. BuggyProfile.All.Select(p => p.PartId)];

    private static BuggyProfile Car(string id) => BuggyProfile.All.First(p => p.PartId == id);

    [Theory]
    [MemberData(nameof(Cars))]
    public void ACarOnItsRoofIsRolledOverStillPointingTheSameWay(string id)
    {
        BuggyProfile p = Car(id);
        RightingMove move = Righting.Solve(p.Corners, Hubs(p, 0.3), Up, Forward, new double3(-1, 0, 0), 0.4);

        double3 stood = move.Turn * Up;
        Assert.Equal(-1.0, stood.X, 6);
        Assert.Equal(1.0, (move.Turn * Forward).Y, 6);
    }

    [Theory]
    [MemberData(nameof(Cars))]
    public void ACarOnItsSideStandsUpAndKeepsItsHeading(string id)
    {
        BuggyProfile p = Car(id);
        double3 groundUp = new(0, 0, 1);
        RightingMove move = Righting.Solve(p.Corners, Hubs(p, 0.3), Up, Forward, groundUp, 0.8);

        Assert.True(Vec.Len((move.Turn * Up) - groundUp) < 1e-6);
        Assert.Equal(1.0, (move.Turn * Forward).Y, 6);
    }

    [Theory]
    [MemberData(nameof(Cars))]
    public void TheLowestTyreIsLeftJustClearOfTheGround(string id)
    {
        BuggyProfile p = Car(id);
        double3[] hubs = Hubs(p, 0.3);
        const double comHeight = 0.25;
        RightingMove move = Righting.Solve(p.Corners, hubs, Up, Forward, new double3(-1, 0, 0), comHeight);

        double lowest = double.PositiveInfinity;
        for (int i = 0; i < hubs.Length; i++)
        {
            lowest = Math.Min(lowest, comHeight + move.Lift + Vec.Dot(hubs[i], Up) - p.Corners[i].Radius);
        }
        Assert.Equal(Righting.Clearance, lowest, 6);
        Assert.True(move.Lift > 0.0);
    }

    [Fact]
    public void ACarAlreadyOnItsWheelsIsNotTurned()
    {
        BuggyProfile p = BuggyProfile.Eldorado;
        RightingMove move = Righting.Solve(p.Corners, Hubs(p, 0.3), Up, Forward, Up, 0.7);
        Assert.True(Vec.Len((move.Turn * Forward) - Forward) < 1e-9);
    }
}
