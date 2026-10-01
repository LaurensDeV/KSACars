using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class ScoopTests
{
    [Fact]
    public void TheEldoradoCarriesThreeScoopsAndTheBuggyNone()
    {
        Assert.Empty(BuggyProfile.Manx.Scoops);
        Assert.Equal(["Scoop", "Scoop XXL", "Scoop Mega"], BuggyProfile.Eldorado.Scoops.Select(s => s.Name));
        Assert.Equal(["Default", "XL", "XXL"], BuggyProfile.Eldorado.Scoops.Select(s => s.Size));
    }

    [Fact]
    public void EachScoopReachesFurtherOutThanTheOneBefore()
    {
        ScoopProfile[] scoops = BuggyProfile.Eldorado.Scoops;
        double Reach(ScoopProfile s) => s.Colliders.Max(c => Math.Abs(c.Deployed.Z));
        Assert.True(Reach(scoops[1]) > Reach(scoops[0]) * 1.5);
        Assert.True(Reach(scoops[2]) > Reach(scoops[1]) * 1.5);
    }

    [Fact]
    public void NoColliderIsSharedBetweenScoops()
    {
        string[] ids = [.. BuggyProfile.Eldorado.Scoops.SelectMany(s => s.Colliders).Select(c => c.Id)];
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void EveryScoopIsStowedInsideTheHullAndDeployedAheadOfTheNose()
    {
        foreach (ScoopProfile s in BuggyProfile.Eldorado.Scoops)
        {
            Assert.InRange(s.Stowed.Y, -2.0, 2.0);
            Assert.All(s.Colliders, c => Assert.True(c.Deployed.Y > 2.6));
        }
    }

    [Fact]
    public void ABoxDeclaredLyingDownIsStoodUpAsABlade()
    {
        doubleQuat turn = ScoopProfile.Upright(0.0);
        // Its height, declared along Z, goes up; its thickness, along X, goes forward; its length across.
        Assert.True(Vec.Len((turn * new double3(0, 0, 1)) - new double3(1, 0, 0)) < 1e-9);
        Assert.True(Vec.Len((turn * new double3(1, 0, 0)) - new double3(0, 1, 0)) < 1e-9);
        Assert.True(Vec.Len((turn * new double3(0, 1, 0)) - new double3(0, 0, 1)) < 1e-9);
    }

    [Fact]
    public void ALeftWingIsSweptForwardAtItsTip()
    {
        // The left wing's box runs along +Z; swept, its outer end is further forward than its inner.
        ScoopCollider left = BuggyProfile.Eldorado.Scoops[1].Colliders.First(c => c.Deployed.Z > 0.5);
        double3 along = left.DeployedTurn!.Value * new double3(0, 1, 0);
        Assert.True(along.Z > 0.8);
        Assert.True(along.Y > 0.3);
        Assert.Equal(0.0, along.X, 9);
    }
}
