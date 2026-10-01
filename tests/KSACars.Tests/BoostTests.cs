using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class BoostTests
{
    private static readonly double3 Forward = new(0, 1, 0);

    [Fact]
    public void UnlitItPushesNothing() => Assert.Equal(Vec.Zero, Boost.Push(false, Forward, 0.02));

    [Fact]
    public void LitItPushesStraightAheadAtItsAcceleration()
    {
        double3 push = Boost.Push(true, Forward * 3.0, 0.5);
        Assert.Equal(Boost.AccelMs2 * 0.5, push.Y, 9);
        Assert.Equal(0.0, push.X, 9);
        Assert.Equal(0.0, push.Z, 9);
    }

    [Fact]
    public void OnlyTheEldoradoHasABoostAndItsPortsAreBehindTheRearWheelsEitherSide()
    {
        Assert.False(BuggyProfile.Manx.HasBoost);
        BuggyProfile p = BuggyProfile.Eldorado;
        Assert.Equal(2, p.BoostNozzles.Length);
        Assert.Equal(0.0, p.BoostNozzles.Sum(n => n.Z), 9);
        Assert.All(p.BoostNozzles, n => Assert.True(n.Y < p.Corners.Min(c => c.Hub.Y)));
    }
}
