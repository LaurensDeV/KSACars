using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

// What of the roads is over a place on the ground, which is what clutter is cleared by: the roads' own
// outline, not a width either side of a line.
public class RoadCoverTests
{
    private static (TrackWorld World, RoadSurface Surface) Laid(Func<TrackWorld, Circuit> draw)
    {
        TrackWorld world = TrackWorld.Earth();
        return (world, world.Surface(draw(world)));
    }

    [Fact]
    public void ARoadThatWidensCoversMoreGroundWhereItIsWider()
    {
        (TrackWorld world, RoadSurface surface) = Laid(w =>
        {
            Circuit c = AutopilotTests.Through(w, false, (0.0, 0.0, 0.0), (300.0, 0.0, 0.0));
            return c.SetEndWidth(1, 2, 8.0).SetEndWidth(2, 1, 22.0);
        });

        Assert.Equal(RoadSurface.Cover.Ground, surface.Over(world.Dir(30.0, 0.0)));
        Assert.Equal(RoadSurface.Cover.Ground, surface.Over(world.Dir(270.0, 0.0)));

        // Nine metres from the middle: clear of the road and its bank at the narrow end, on the asphalt at the wide one.
        Assert.Equal(RoadSurface.Cover.None, surface.Over(world.Dir(30.0, 9.0)));
        Assert.Equal(RoadSurface.Cover.Ground, surface.Over(world.Dir(270.0, 9.0)));
        Assert.Equal(RoadSurface.Cover.None, surface.Over(world.Dir(270.0, 40.0)));
    }

    [Fact]
    public void TheGroundUnderADeckIsUnderADeckAndNotUnderARoad()
    {
        (TrackWorld world, RoadSurface surface) = Laid(w => AutopilotTests.Through(w, false,
            (0.0, 0.0, 0.0), (100.0, 0.0, 12.0), (300.0, 0.0, 12.0), (400.0, 0.0, 0.0)));

        Assert.Equal(RoadSurface.Cover.Deck, surface.Over(world.Dir(200.0, 0.0)));
        Assert.Equal(RoadSurface.Cover.None, surface.Over(world.Dir(200.0, 12.0)));
        Assert.Equal(RoadSurface.Cover.Ground, surface.Over(world.Dir(5.0, 0.0)));
    }

    [Fact]
    public void AJunctionCoversItsOwnShapeAndNotASquareRoundIt()
    {
        (TrackWorld world, RoadSurface surface) = Laid(w =>
        {
            Circuit c = AutopilotTests.Through(w, false, (-150.0, 0.0, 0.0), (0.0, 0.0, 0.0), (150.0, 0.0, 0.0));
            return c.Extend(2, w.Deg(140.0), 0.0, out _).Extend(2, w.Deg(-140.0), 0.0, out _);
        });

        Assert.Equal(RoadSurface.Cover.Ground, surface.Over(world.Dir(0.0, 0.0)));
        Assert.Equal(RoadSurface.Cover.Ground, surface.Over(world.Dir(6.0, 6.0)));

        // Between two arms, 25 m out along the diagonal: grass, with a road 18 m away on either hand.
        Assert.Equal(RoadSurface.Cover.None, surface.Over(world.Dir(18.0, 18.0)));
    }

    // Only what the outline covers goes, of all that is within reach of the line.
    [Fact]
    public void ClutterWithinReachIsOnlyTakenWhereItIsCovered()
    {
        TrackWorld world = TrackWorld.Earth();
        double3[] line = [.. Enumerable.Range(0, 60).Select(k => world.Dir(2.0 * k, 0.0))];
        int Taken(Func<double3, bool>? covered) => ClutterGrid.Under(line, world.RadiusM, 12.0, 400_000, covered)
            .Values.Sum(bits => bits.Sum(w => System.Numerics.BitOperations.PopCount(w)));

        int all = Taken(null);
        Assert.True(all > 100, $"{all}");
        Assert.Equal(0, Taken(_ => false));
        Assert.Equal(all, Taken(_ => true));

        // North of the line only: about half.
        double3 north = Vec.Unit(world.Dir(0.0, 100.0) - world.Dir(0.0, 0.0));
        Assert.InRange(Taken(at => Vec.Dot(at - line[0], north) > 0.0), all / 3, 2 * all / 3);
    }

    // Level, up to a deck 12 m over the ground along the equator, and down: its barriers are 5 m north and south of its middle.
    [Fact]
    public void ADecksBarrierIsFoundFromEitherSideOfItsEdgeAndTheGroundHasNone()
    {
        (TrackWorld world, RoadSurface surface) = Laid(w => AutopilotTests.Through(w, false,
            (0.0, 0.0, 0.0), (100.0, 0.0, 12.0), (300.0, 0.0, 12.0), (400.0, 0.0, 0.0)));
        double3 At(double east, double north, double up) => world.Dir(east, north) * (world.RadiusM + TrackWorld.LiftM + up);
        double3 northward = Vec.Unit(world.Dir(200.0, 10.0) - world.Dir(200.0, 0.0));

        Assert.True(surface.TryBarrier(At(200.0, 4.6, 12.3), out double inside, out double3 outward));
        Assert.InRange(inside, -0.45, -0.35);
        Assert.True(Vec.Dot(outward, northward) > 0.99);

        Assert.True(surface.TryBarrier(At(200.0, -5.1, 12.3), out double past, out outward));
        Assert.InRange(past, 0.05, 0.15);
        Assert.True(Vec.Dot(outward, northward) < -0.99);

        // Under the deck, far over it, and on the road before it leaves the ground, there is no wall.
        Assert.False(surface.TryBarrier(At(200.0, 4.6, 3.0), out _, out _));
        Assert.False(surface.TryBarrier(At(200.0, 4.6, 20.0), out _, out _));
        Assert.False(surface.TryBarrier(At(5.0, 4.6, 0.3), out _, out _));
    }
}
