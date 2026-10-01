using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class HeadlightsTests
{
    private static readonly double3 Up = new(1, 0, 0);
    private static readonly double3 Forward = new(0, 1, 0);

    public static TheoryData<string> Cars => [.. BuggyProfile.All.Select(p => p.PartId)];

    [Theory]
    [MemberData(nameof(Cars))]
    public void ACarHasALampEachSideAheadOfItsFrontWheels(string id)
    {
        BuggyProfile p = BuggyProfile.All.First(x => x.PartId == id);
        Assert.Equal(2, p.HeadLamps.Length);
        Assert.Equal(p.HeadLamps[0].Z, -p.HeadLamps[1].Z, 6);
        Assert.True(p.HeadLamps[0].Z > 0.2);
        Assert.All(p.HeadLamps, lamp => Assert.True(lamp.Y > p.Corners.Max(c => c.Hub.Y)));
        Assert.All(p.HeadLamps, lamp => Assert.InRange(lamp.X, 0.5, 1.1));
    }

    [Theory]
    [MemberData(nameof(Cars))]
    public void ACarHasATailLampEachSideBehindItsRearWheels(string id)
    {
        BuggyProfile p = BuggyProfile.All.First(x => x.PartId == id);
        Assert.Equal(2, p.TailLamps.Length);
        Assert.Equal(p.TailLamps[0].Z, -p.TailLamps[1].Z, 6);
        Assert.All(p.TailLamps, lamp => Assert.True(lamp.Y < p.Corners.Min(c => c.Hub.Y)));
    }

    [Fact]
    public void TheEldoradosTailLensesGlowRedAndItsMarkersAmber()
    {
        (string Suffix, uint Rgb)[] lenses = BuggyProfile.Eldorado.ColouredLenses;
        uint tail = lenses.First(l => l.Suffix == "TailLens").Rgb;
        uint marker = lenses.First(l => l.Suffix == "MarkerLens").Rgb;
        Assert.True((tail >> 16) > 200 && ((tail >> 8) & 0xFF) < 60 && (tail & 0xFF) < 60);
        Assert.True((marker >> 16) > 200 && ((marker >> 8) & 0xFF) is > 90 and < 190 && (marker & 0xFF) < 60);
    }

    [Fact]
    public void TailLampsPointBackAndDown()
    {
        double3 aim = Headlights.Aim(Up, -Forward, Headlights.Tail.DipRad);
        Assert.True(Vec.Dot(aim, Forward) < 0.0);
        Assert.True(Vec.Dot(aim, Up) < 0.0);
    }

    [Theory]
    [InlineData(-1.0, 10.0, true)]
    [InlineData(1.0, -3.0, true)]
    [InlineData(-1.0, -3.0, false)]
    [InlineData(1.0, 10.0, false)]
    [InlineData(0.0, 10.0, false)]
    [InlineData(-1.0, 0.0, false)]
    public void TheBrakesAreOnWhenTheDriverAsksAgainstTheWayItRolls(double throttle, double speed, bool braking) =>
        Assert.Equal(braking, Headlights.Braking(throttle, speed));

    [Fact]
    public void ThePanelThrowsThePartsSwitch()
    {
        Assert.Equal((BeamSetting.High, true), Headlights.Reconcile(BeamSetting.High, switchNow: false, seen: false));
        Assert.Equal((BeamSetting.Off, false), Headlights.Reconcile(BeamSetting.Off, switchNow: true, seen: true));
    }

    [Fact]
    public void ThePartsSwitchThrownInKsaWins()
    {
        Assert.Equal((BeamSetting.Low, true), Headlights.Reconcile(BeamSetting.Off, switchNow: true, seen: false));
        Assert.Equal((BeamSetting.High, true), Headlights.Reconcile(BeamSetting.High, switchNow: true, seen: false));
        Assert.Equal((BeamSetting.Off, false), Headlights.Reconcile(BeamSetting.High, switchNow: false, seen: true));
    }

    [Fact]
    public void TheLampsAreDarkWhenOff() => Assert.Null(Headlights.Shape(BeamSetting.Off));

    [Fact]
    public void MainBeamReachesFurtherAndBrighterThroughANarrowerCone()
    {
        Assert.True(Headlights.High.Range > Headlights.Low.Range * 2f);
        Assert.True(Headlights.High.Intensity > Headlights.Low.Intensity);
        Assert.True(Headlights.High.OuterAngle < Headlights.Low.OuterAngle);
    }

    [Fact]
    public void DippedBeamReachesTheGroundCloseToTheBumper()
    {
        // The lower edge of the cone, off a lamp 0.75 m up: nearer than two metres ahead.
        double edge = Headlights.Low.DipRad + (Headlights.Low.OuterAngle * 0.5);
        Assert.True(0.75 / Math.Tan(edge) < 2.0);
    }

    [Fact]
    public void DippedBeamPointsBelowLevelAndFurtherDownThanMainBeam()
    {
        double3 low = Headlights.Aim(Up, Forward, Headlights.Low.DipRad);
        Assert.True(Vec.Dot(low, Up) < 0.0);
        Assert.Equal(1.0, Vec.Len(low), 9);
        Assert.True(Headlights.High.DipRad < Headlights.Low.DipRad);
    }

    [Theory]
    [InlineData("off", BeamSetting.Off)]
    [InlineData("Low", BeamSetting.Low)]
    [InlineData("on", BeamSetting.Low)]
    [InlineData(" HIGH ", BeamSetting.High)]
    public void TheBridgeNamesASetting(string text, BeamSetting expected) => Assert.Equal(expected, Headlights.Parse(text));

    [Fact]
    public void AnythingElseIsRefused() => Assert.Null(Headlights.Parse("full"));
}
