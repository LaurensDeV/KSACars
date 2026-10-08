using Xunit;

namespace KSACars.Tests;

public class RoadWarningsTests
{
    private static List<RoadWarning> Of(Func<TrackWorld, Circuit> draw)
    {
        TrackWorld world = TrackWorld.Earth();
        Circuit c = draw(world);
        return RoadWarning.Of(c, RoadLaying.Laid(c, TrackWorld.DirOf, world.RadiusM, world.HeightAt, TrackWorld.LiftM, TrackWorld.SpacingM), TrackWorld.DirOf);
    }

    [Fact]
    public void AnOrdinaryRoadHasNothingWrongWithIt()
    {
        Assert.Empty(Of(w => AutopilotTests.Ring(100.0).Circuit));
        Assert.Empty(Of(w => AutopilotTests.Junction().Circuit));
    }

    [Fact]
    public void ABendTooTightForItsWidthIsSaid()
    {
        List<RoadWarning> warnings = Of(w => AutopilotTests.Through(w, false, (0.0, 0.0, 0.0), (40.0, 0.0, 0.0), (40.0, 6.0, 0.0), (0.0, 6.0, 0.0)) with { WidthM = 20.0 });
        Assert.Contains(warnings, x => x.Text.Contains("tighter than"));
    }

    [Theory]
    [InlineData(20.0, null)]
    [InlineData(55.0, "the Eldorado stops")]
    [InlineData(75.0, "only the F2004")]
    [InlineData(140.0, "no car climbs")]
    public void AGradeIsSaidByWhoStopsOnIt(double rise, string? said)
    {
        // The height between two level stretches is steepest at half as much again as its mean, which is rise over 150 m.
        List<RoadWarning> warnings = Of(w => AutopilotTests.Through(w, false, (-100.0, 0.0, 0.0), (0.0, 0.0, 0.0), (150.0, 0.0, rise), (300.0, 0.0, rise)));
        if (said is null) Assert.DoesNotContain(warnings, x => x.Text.Contains("grade"));
        else Assert.Contains(warnings, x => x.Text.Contains(said));
    }

    // A side road 9 m long off a road 10 m wide: its mouth would be inside the through road's own edge.
    [Fact]
    public void ARoadTooShortForItsJunctionIsSaidAtThePoint()
    {
        List<RoadWarning> warnings = Of(w =>
        {
            Circuit c = AutopilotTests.Through(w, false, (-150.0, 0.0, 0.0), (0.0, 0.0, 0.0), (150.0, 0.0, 0.0));
            return c.Extend(2, w.Deg(9.0), 0.0, out _);
        });
        RoadWarning warning = Assert.Single(warnings, x => x.Text.Contains("junction", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(warning.Node);
    }

    // And one long enough is a junction with nothing to say, its corners rounded by as much as fits.
    [Fact]
    public void ASideRoadOfFourteenMetresIsStillAJunction()
    {
        Assert.Empty(Of(w =>
        {
            Circuit c = AutopilotTests.Through(w, false, (-150.0, 0.0, 0.0), (0.0, 0.0, 0.0), (150.0, 0.0, 0.0));
            return c.Extend(2, w.Deg(14.0), 0.0, out _);
        }));
    }
}
