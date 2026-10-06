using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

/// <summary>
/// The top of a launch pad under a wheel. The frame is the pad's: +X up, as KSA stands a static
/// object, so a wheel drops along -X.
/// </summary>
public class PadSurfaceTests
{
    private static readonly double3 X = new(1, 0, 0), Y = new(0, 1, 0), Z = new(0, 0, 1);
    private static readonly double3 Down = new(-1, 0, 0);

    private static PadSolid Box(double3 centre, double3 half) => new(centre, X, Y, Z, half, Round: false);

    /// <summary>A disc lying flat: KSA's cylinders run along their own Y, turned here onto the pad's up.</summary>
    private static PadSolid Disc(double3 centre, double radius, double halfThickness) =>
        new(centre, Y, X, Z, new double3(radius, halfThickness, 0), Round: true);

    [Fact]
    public void AWheelOverADeckIsThatFarAboveItsTop()
    {
        PadSurface pad = new([Box(new double3(1, 0, 0), new double3(0.5, 10, 10))]);

        Assert.True(pad.TryDrop(new double3(3, 2, -4), Down, 30, out double metres));
        Assert.Equal(1.5, metres, 9);
    }

    [Fact]
    public void BesideTheDeckThereIsNothingUnderTheWheel()
    {
        PadSurface pad = new([Box(new double3(1, 0, 0), new double3(0.5, 10, 10))]);

        Assert.False(pad.TryDrop(new double3(3, 10.5, 0), Down, 30, out _));
    }

    [Fact]
    public void TheHigherOfTwoDecksIsTheOneStoodOn()
    {
        PadSurface pad = new([
            Box(new double3(0, 0, 0), new double3(0.2, 50, 50)),
            Box(new double3(1, 0, 0), new double3(0.5, 5, 5)),
        ]);

        Assert.True(pad.TryDrop(new double3(4, 0, 0), Down, 30, out double onTheDeck));
        Assert.Equal(2.5, onTheDeck, 9);
        Assert.True(pad.TryDrop(new double3(4, 20, 0), Down, 30, out double onTheApron));
        Assert.Equal(3.8, onTheApron, 9);
    }

    [Fact]
    public void ARoofOverheadIsNotTheGround()
    {
        PadSurface pad = new([
            Box(new double3(0, 0, 0), new double3(0.2, 50, 50)),
            Box(new double3(6, 0, 0), new double3(0.5, 5, 5)),
        ]);

        Assert.True(pad.TryDrop(new double3(1, 0, 0), Down, 30, out double metres));
        Assert.Equal(0.8, metres, 9);
    }

    [Fact]
    public void AWheelSunkIntoTheDeckIsAtNoHeight()
    {
        PadSurface pad = new([Box(new double3(1, 0, 0), new double3(0.5, 10, 10))]);

        Assert.True(pad.TryDrop(new double3(1.2, 0, 0), Down, 30, out double metres));
        Assert.Equal(0.0, metres, 9);
    }

    [Fact]
    public void ARoundDeckEndsAtItsRim()
    {
        PadSurface pad = new([Disc(new double3(1.5, 0, 0), radius: 7.5, halfThickness: 0.05)]);

        Assert.True(pad.TryDrop(new double3(3, 5, 5), Down, 30, out double metres));
        Assert.Equal(1.45, metres, 9);
        Assert.False(pad.TryDrop(new double3(3, 6, 6), Down, 30, out _));
    }

    [Fact]
    public void ARampIsHigherFurtherUpIt()
    {
        // A slab tipped 0.1 rad about Z, so its top climbs along +Y.
        double c = Math.Cos(0.1), s = Math.Sin(0.1);
        PadSolid ramp = new(new double3(0, 0, 0), new double3(c, -s, 0), new double3(s, c, 0), Z,
                            new double3(0.1, 10, 2), Round: false);
        PadSurface pad = new([ramp]);

        Assert.True(pad.TryDrop(new double3(5, -4, 0), Down, 30, out double low));
        Assert.True(pad.TryDrop(new double3(5, 4, 0), Down, 30, out double high));
        Assert.Equal(8 * Math.Tan(0.1), low - high, 9);
    }

    [Fact]
    public void ADeckOutOfReachIsNotMet()
    {
        PadSurface pad = new([Box(new double3(1, 0, 0), new double3(0.5, 10, 10))]);

        Assert.False(pad.TryDrop(new double3(40, 0, 0), Down, 30, out _));
    }
}
