using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class HatchTests
{
    public static TheoryData<int> Hatches => [.. Enumerable.Range(0, BuggyProfile.Eldorado.Hatches.Length)];

    [Fact]
    public void TheEldoradoHasAHatchForEveryThrusterAndTheBuggyNone()
    {
        Assert.Empty(BuggyProfile.Manx.Hatches);
        HatchProfile[] hatches = BuggyProfile.Eldorado.Hatches;
        Assert.Equal(4, hatches.Count(h => h.Group == ThrusterGroup.Lift));
        Assert.Equal(2, hatches.Count(h => h.Group == ThrusterGroup.Boost));
        Assert.Equal(4, hatches.Count(h => h.Group == ThrusterGroup.Down));
        Assert.Equal(hatches.Length, hatches.Select(h => h.Suffix).Distinct().Count());
    }

    // A blade's outline in the part's frame: its straight edge's ends, then round its rim.
    private static IEnumerable<double3> Outline(HatchProfile h, int b, double open)
    {
        (double3 at, doubleQuat turned, double3 size) = Hatch.Blade(h, b, open);
        for (int k = 0; k <= 36; k++)
        {
            double a = (-Math.PI / 2.0) + (k * Math.PI / 36.0);
            var model = new double3(0.0, (Hatch.Radius * Math.Cos(a)) - Hatch.Radius, Hatch.Radius * Math.Sin(a));
            yield return at + (turned * new double3(model.X * size.X, model.Y * size.Y, model.Z * size.Z));
        }
    }

    [Theory]
    [MemberData(nameof(Hatches))]
    public void ClosedEveryBladeIsHalfTheDiscJustUnderTheSkin(int i)
    {
        HatchProfile h = BuggyProfile.Eldorado.Hatches[i];
        double3 outward = Vec.Unit(h.Out);
        for (int b = 0; b < Hatch.Blades; b++)
        {
            (_, doubleQuat turned, double3 size) = Hatch.Blade(h, b, 0.0);
            Assert.Equal(1.0, size.Y, 9);
            Assert.Equal(1.0, size.Z, 9);
            Assert.True(Vec.Dot(turned * new double3(1, 0, 0), outward) > 0.999999);
            foreach (double3 p in Outline(h, b, 0.0))
            {
                Assert.InRange(Vec.Dot(p - h.At, outward), -0.003, -Hatch.BladeDepth + 1e-9);
                Assert.Equal(Hatch.Radius, Vec.Len(Vec.RejectFrom(p - h.At, outward)), 6);
            }
        }
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.9)]
    public void PartOpenTheBladesLeaveAHexagonAndHardlyLeaveTheHole(double open)
    {
        HatchProfile h = BuggyProfile.Eldorado.Hatches[6];
        double3 outward = Vec.Unit(h.Out);
        double3 centre = h.At;
        for (int b = 0; b < Hatch.Blades; b++)
        {
            double3[] outline = [.. Outline(h, b, open)];
            double3 inward = Vec.Unit(Vec.RejectFrom(centre - outline[18], outward));

            // the straight edge is the aperture's side: both ends as far from the centre along the blade
            Assert.Equal(Hatch.Aperture(open), -Vec.Dot(outline[0] - centre, inward), 6);
            Assert.Equal(Hatch.Aperture(open), -Vec.Dot(outline[^1] - centre, inward), 6);
            // and its ends are on the hole's edge, so the next blade's edge meets it there
            Assert.Equal(Hatch.Radius, Vec.Len(Vec.RejectFrom(outline[0] - centre, outward)), 6);
            foreach (double3 p in outline)
            {
                double r = Vec.Len(Vec.RejectFrom(p - centre, outward));
                Assert.InRange(r, Hatch.Radius - 1e-9, Hatch.Radius + 0.0075);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Hatches))]
    public void OpenABladeIsDrawnBackToNothingAtTheRim(int i)
    {
        (_, _, double3 size) = Hatch.Blade(BuggyProfile.Eldorado.Hatches[i], 2, 1.0);
        Assert.True(size.Y <= 0.001);
        Assert.True(size.Z <= 0.05);
    }

    [Fact]
    public void AnIrisTakesATenthOfASecondEitherWayAndStopsAtItsEnds()
    {
        double open = 0.0;
        int steps = 0;
        while (!Hatch.Ready(open) && steps < 1000) { open = Hatch.Advance(open, true, 0.01); steps++; }
        Assert.InRange(steps * 0.01, 0.09, 0.12);

        Assert.Equal(1.0, Hatch.Advance(1.0, true, 1.0), 9);
        Assert.Equal(0.0, Hatch.Advance(0.2, false, 1.0), 9);
        Assert.False(Hatch.Ready(0.9));
    }

    [Fact]
    public void NoThrusterFiresBeforeItsIrisIsOpen()
    {
        Assert.False(Hatch.Ready(Hatch.Advance(0.0, true, Hatch.OpenSeconds * 0.5)));
        Assert.True(Hatch.Ready(Hatch.Advance(0.0, true, Hatch.OpenSeconds)));
    }
}
