using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class DownforceTests
{
    private static readonly double3 Up = new(1, 0, 0);

    [Fact]
    public void UnlitItPushesNothingAndTheCarWeighsWhatGravityMakesIt()
    {
        Assert.Equal(Vec.Zero, Downforce.Push(false, Up, 0.02));
        Assert.Equal(1.62, Downforce.Load(false, 1.62), 9);
    }

    [Fact]
    public void LitItPushesTheCarOntoItsWheelsAndTheSpringsCarryThatToo()
    {
        double3 push = Downforce.Push(true, Up * 2.0, 0.5);
        Assert.Equal(-Downforce.AccelMs2 * 0.5, push.X, 9);
        Assert.Equal(1.62 + Downforce.AccelMs2, Downforce.Load(true, 1.62), 9);
    }

    [Fact]
    public void OnlyTheEldoradoHasThemTwoOnTheBonnetAndTwoOnTheBoot()
    {
        Assert.False(BuggyProfile.Manx.HasDownforce);
        BuggyProfile p = BuggyProfile.Eldorado;
        Assert.Equal(4, p.DownNozzles.Length);
        Assert.Equal(0.0, p.DownNozzles.Sum(n => n.Z), 9);
        Assert.Equal(2, p.DownNozzles.Count(n => n.Y > 0));
        Assert.All(p.DownNozzles, n => Assert.True(n.X > 0.9));
    }
}
