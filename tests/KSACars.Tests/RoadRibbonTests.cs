using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class RoadRibbonTests
{
    private const double Lift = TrackWorld.LiftM;

    private static List<RoadRibbon> Lay(TrackWorld world, Circuit circuit, double spacing = 2.0) =>
        RoadLaying.Ribbons(circuit, TrackWorld.DirOf, world.RadiusM, world.HeightAt, Lift, spacing);

    private static Circuit Through(TrackWorld world, double width, params (double East, double North, double Height)[] points)
    {
        Circuit circuit = new Circuit { WidthM = width }.AddNode(world.Deg(points[0].North), world.Deg(points[0].East), out int last)
            .SetHeight(last, points[0].Height);
        for (int i = 1; i < points.Length; i++)
        {
            circuit = circuit.Extend(last, world.Deg(points[i].North), world.Deg(points[i].East), out last).SetHeight(last, points[i].Height);
        }
        return circuit;
    }

    // A road that climbs, turns both ways, leans into its bends and changes width, over ground that rolls.
    private static (TrackWorld World, Circuit Circuit) Winding()
    {
        TrackWorld world = TrackWorld.Earth((e, n) => (1.5 * Math.Sin(e / 40.0)) + (0.8 * Math.Cos(n / 25.0)) + (0.02 * e));
        Circuit c = Through(world, 10.0, (0, 0, 0), (120, 0, 1), (220, 60, 3), (300, 160, 3), (420, 180, 0.5), (520, 120, 0));
        c = c.SetBank(2, 1, 5.0).SetBank(3, 2, 8.0).SetBank(3, 4, 8.0).SetBank(4, 5, -6.0).SetEndWidth(3, 4, 14.0).SetEndWidth(5, 4, 7.0);
        return (world, c);
    }

    private static double3 On(RoadRibbon ribbon, double s, double d)
    {
        RoadRibbon.Section at = ribbon.At(s);
        Assert.True(ribbon.Surface(at, d, 0.0, out double height, out _, out _, out _), $"no surface at {s} m along, {d} m across");
        return ribbon.Point(at, d, height);
    }

    [Fact]
    public void TheFacingIsTheSurfacesOwnEverywhereOnTheAsphaltTheVergeAndTheBank()
    {
        (TrackWorld world, Circuit circuit) = Winding();
        RoadRibbon ribbon = Assert.Single(Lay(world, circuit));
        const double step = 1e-4;
        int asked = 0, offAsphalt = 0;

        for (double s = 1.0; s < ribbon.LengthM - 1.0; s += 1.37)
        {
            RoadRibbon.Section at = ribbon.At(s);
            foreach (double share in new[] { -1.25, -1.08, -0.9, -0.4, 0.0, 0.5, 0.95, 1.1, 1.3 })
            {
                double d = share * at.HalfWidth;
                if (!ribbon.Surface(at, d, 0.0, out double height, out double outM, out double along, out double across)) continue;

                // Not across a crease or the end of the bank, where the surface has two facings or one side.
                double toe = d > 0.0 ? at.ToeLeft : at.ToeRight;
                if (outM > 0.0 && (outM < 0.01 || Math.Abs(outM - RoadRibbon.VergeM) < 0.01 || outM > toe - 0.05)) continue;
                if (outM == 0.0 && at.HalfWidth - Math.Abs(d) < 0.01) continue;

                double3 normal = ribbon.Normal(at, d, height, along, across);
                double3 alongStep = On(ribbon, s + step, d) - On(ribbon, s - step, d);
                double3 acrossStep = On(ribbon, s, d + step) - On(ribbon, s, d - step);
                double3 measured = Vec.Unit(Vec.Cross(alongStep, acrossStep));

                asked++;
                if (outM > 0.0) offAsphalt++;
                Assert.Equal(1.0, Vec.Len(normal), 9);
                Assert.True(Vec.Dot(normal, Vec.Unit(ribbon.Point(at, d, height))) > 0.8, "the facing is not up");
                Assert.True(Vec.Len(normal - measured) < 2e-5, $"facing out by {Vec.Len(normal - measured)} at {s:F1} m along, {d:F2} m across, {outM:F2} m out");
            }
        }
        Assert.True(asked > 2000 && offAsphalt > 500, $"{asked} places asked, {offAsphalt} of them off the asphalt");
    }

    [Fact]
    public void TheFacingHasNoJumpAlongTheRoadOrAcrossItAtAnyPointOfTheCircuit()
    {
        (TrackWorld world, Circuit circuit) = Winding();
        RoadRibbon ribbon = Assert.Single(Lay(world, circuit));
        RoadSurface surface = new([ribbon]);

        double3 Facing(double s, double d)
        {
            RoadRibbon.Section at = ribbon.At(s);
            ribbon.Surface(at, d, 0.0, out double height, out _, out _, out _);
            Assert.True(surface.TryLocate(ribbon.Point(at, d, height + 0.3), null, out double over, out double outM, out double3? facing), $"no road at {s}, {d}");
            Assert.Equal(0.3, over, 6);
            Assert.Equal(0.0, outM);
            return facing!.Value;
        }

        // Either side of each point, a hair apart. On the centre line nothing steps. Off it the centre
        // line's curvature does, two cubic curves meeting with one heading and two bends, and a climbing
        // road is then steeper along its inside edge on one side of the point than on the other, by its
        // slope times how far across times the step in curvature: four hundredths of a degree at the most here.
        for (int point = 1; point < ribbon.Spans.Count; point++)
        {
            double s = ribbon.Spans[point].FromS;
            RoadRibbon.Section before = ribbon.At(point - 1, s), after = ribbon.At(point, s);
            Near.Equal(0.0, Vec.AngleBetween(Facing(s - 1e-6, 0.0), Facing(s + 1e-6, 0.0)), 1e-6);
            foreach (double d in new[] { -3.0, 3.0 })
            {
                double turned = Vec.AngleBetween(Facing(s - 1e-6, d), Facing(s + 1e-6, d));
                double crease = Math.Abs((after.Slope + (d * after.BankRate)) * d * (after.Curvature - before.Curvature));
                Assert.True(turned <= (1.5 * crease) + 1e-6, $"the facing jumps by {turned} rad at point {point}, {d} m across, where {crease} is the crease");
                Assert.True(turned < 1e-3, $"the facing jumps by {turned} rad at point {point}, {d} m across");
            }
        }

        // And walked a centimetre at a time, along and across: no more turn in a step than a bend gives.
        double most = 0.0;
        foreach (double share in new[] { -0.9, 0.0, 0.9 })
        {
            double3 last = Facing(2.0, share * ribbon.At(2.0).HalfWidth);
            for (double s = 2.01; s < ribbon.LengthM - 2.0; s += 0.01)
            {
                double3 here = Facing(s, share * ribbon.At(s).HalfWidth);
                most = Math.Max(most, Vec.AngleBetween(last, here));
                last = here;
            }
        }
        for (double s = 10.0; s < ribbon.LengthM - 10.0; s += 7.3)
        {
            double half = ribbon.At(s).HalfWidth - 0.05;
            double3 last = Facing(s, -half);
            for (double d = -half + 0.01; d <= half; d += 0.01)
            {
                double3 here = Facing(s, d);
                most = Math.Max(most, Vec.AngleBetween(last, here));
                last = here;
            }
        }
        // The most is the crease at a point, out by the edge: seven hundredths of a degree.
        Assert.True(most < 2e-3, $"the facing turns {most * 180.0 / Math.PI:F4} deg in a centimetre");
    }

    [Fact]
    public void APlaceOnTheSurfaceIsFoundAgainFromWhereItIs()
    {
        (TrackWorld world, Circuit circuit) = Winding();
        RoadRibbon ribbon = Assert.Single(Lay(world, circuit));
        RoadSurface surface = new([ribbon]);
        Random random = new(3);

        for (int i = 0; i < 4000; i++)
        {
            double s = random.NextDouble() * ribbon.LengthM;
            RoadRibbon.Section at = ribbon.At(s);
            double d = ((2.0 * random.NextDouble()) - 1.0) * (at.HalfWidth + 2.5);
            if (!ribbon.Surface(at, d, 0.0, out double height, out double outM, out _, out _)) continue;

            Assert.True(surface.TryLocate(ribbon.Point(at, d, height + 0.2), null, out double over, out double told, out _), $"lost at {s:F2}, {d:F2}");
            Assert.True(Math.Abs(over - 0.2) < 1e-6, $"{over} m over the surface at {s:F2} m along, {d:F2} m across");
            Assert.True(Math.Abs(told - (outM <= RoadSurface.EdgeM ? 0.0 : outM)) < 1e-6);
        }
    }

    // What a point carried along the centre line at 80 m/s is told is under it, a centimetre at a time.
    private static (double MostStep, double MostSlopeStep, double MostAccel) Driven(RoadSurface surface, RoadRibbon ribbon, double from, double to)
    {
        const double each = 0.01, speed = 80.0;
        double mostStep = 0.0, mostSlopeStep = 0.0, mostAccel = 0.0;
        double before = double.NaN, last = double.NaN, lastSlope = double.NaN;
        for (double s = from; s <= to; s += each)
        {
            RoadRibbon.Section at = ribbon.At(s);
            double3 point = ribbon.Point(at, 0.0, at.Height + 5.0);
            Assert.True(surface.TryHeightOver(point, out double over), $"no road {s} m along");
            double height = Vec.Len(point) - over;
            if (!double.IsNaN(last))
            {
                double slope = (height - last) / each;
                mostStep = Math.Max(mostStep, Math.Abs(height - last) - (1.01 * Math.Abs(at.Slope) * each));
                if (!double.IsNaN(lastSlope)) mostSlopeStep = Math.Max(mostSlopeStep, Math.Abs(slope - lastSlope));
                if (!double.IsNaN(before)) mostAccel = Math.Max(mostAccel, Math.Abs(height - (2.0 * last) + before) / (each * each) * speed * speed);
                lastSlope = slope;
            }
            (before, last) = (last, height);
        }
        return (mostStep, mostSlopeStep, mostAccel);
    }

    [Fact]
    public void ASteadyClimbThroughSeveralPointsIsARampToAPointDrivenAlongIt()
    {
        TrackWorld world = TrackWorld.Earth();
        Circuit climb = Through(world, 10.0, (0, 0, 0), (100, 0, 5), (200, 0, 10), (300, 0, 15), (400, 0, 20), (500, 0, 25));
        RoadRibbon ribbon = Assert.Single(Lay(world, climb));
        RoadSurface surface = new([ribbon]);

        (double step, double slopeStep, double accel) = Driven(surface, ribbon, 1.0, ribbon.LengthM - 1.0);

        // The heights are a body's radius and more, told to its last digits: a part in ten to the sixteenth of
        // six thousand kilometres is a nanometre, which over a centimetre twice is all this is.
        Assert.True(step < 1e-6, $"a step of {step} m");
        Assert.True(slopeStep < 1e-5, $"the slope steps by {slopeStep}");
        Assert.True(accel < 1.0, $"{accel} m/s2 at 80 m/s on a steady climb");
        for (int road = 0; road < ribbon.Profile.Roads; road++)
        {
            (double grade, double radius) = ribbon.Profile.Steepness(road);
            Near.Equal(0.05, grade, 1e-9);
            Assert.True(radius > 1e6);
        }
    }

    [Fact]
    public void OverCrestsAndDipsAndRollingGroundThereIsNoStepInHeightOrInSlope()
    {
        (TrackWorld world, Circuit circuit) = Winding();
        RoadRibbon ribbon = Assert.Single(Lay(world, circuit));
        RoadSurface surface = new([ribbon]);

        (double step, double slopeStep, double accel) = Driven(surface, ribbon, 1.0, ribbon.LengthM - 1.0);

        double mostBend = 0.0;
        for (double s = 1.0; s < ribbon.LengthM - 1.0; s += 0.01) mostBend = Math.Max(mostBend, Math.Abs(ribbon.At(s).Bend));
        Assert.True(step < 1e-6, $"a step of {step} m");
        Assert.True(slopeStep <= (1.05 * mostBend * 0.01) + 1e-5, $"the slope steps by {slopeStep} where its bend gives {mostBend * 0.01}");
        Assert.True(accel <= (1.05 * 6400.0 * mostBend) + 1.0, $"{accel} m/s2 where the bend gives {6400.0 * mostBend}");
        Assert.InRange(mostBend, 1e-4, 0.02);
    }

    [Fact]
    public void TheRoadIsNeverUnderTheGroundAcrossItsWholeWidthOnAnyGround()
    {
        Random random = new(21);
        for (int run = 0; run < 12; run++)
        {
            double a = 0.5 + (3.0 * random.NextDouble()), b = 0.5 + (2.0 * random.NextDouble()), tilt = (random.NextDouble() - 0.5) * 0.3;
            double waveE = 6.0 + (60.0 * random.NextDouble()), waveN = 5.0 + (40.0 * random.NextDouble());
            TrackWorld world = TrackWorld.Earth((e, n) => (a * Math.Sin(e / waveE)) + (b * Math.Cos(n / waveN)) + (tilt * n) + (0.05 * e));
            Circuit c = Through(world, 8.0 + (run % 3 * 4.0), (0, 0, 0), (90, 20, run % 2), (160, 90, 0), (260, 110, 0));
            c = c.SetBank(2, 1, run % 4 * 3.0).SetGroundSmooth(new[] { 0.0, 12.0, 30.0, 80.0 }[run % 4]);
            RoadRibbon ribbon = Assert.Single(Lay(world, c));

            // At every place the ground was read, and at each of the five places across it was read at there.
            int steps = (int)Math.Round(ribbon.LengthM / ribbon.SideStepM);
            for (int i = 0; i <= steps; i++)
            {
                RoadRibbon.Section at = ribbon.At(Math.Min(i * ribbon.SideStepM, ribbon.LengthM));
                foreach (double share in new[] { -1.0, -0.5, 0.0, 0.5, 1.0 })
                {
                    double d = share * at.HalfWidth;
                    Plan place = at.At + (at.Left * d);
                    double ground = world.HeightAt(ribbon.Chart.Dir(place)), road = at.Height + (d * at.BankTan);
                    Assert.True(road >= ground + Lift - 1e-6, $"run {run}: the road is {ground + Lift - road:F3} m under where it should be, {at.S:F1} m along and {d:F1} m across");
                }
            }
        }
    }

    [Fact]
    public void ABankRaisesTheLeftEdgeOfARoadTravelledFromItsFromEndWhicheverWayTheRunTakesIt()
    {
        TrackWorld world = TrackWorld.Earth();
        Circuit c = new Circuit { WidthM = 10.0 }.AddNode(0, 0, out int a).Extend(a, 0, world.Deg(200), out int b);
        Circuit banked = c.SetBank(a, b, 6.0).SetBank(b, a, 6.0);

        // Travelling east, left is north. The ground is under the low edge and not under the middle:
        // a road that leans is filled under, so it is its lift above the ground at its south edge.
        RoadRibbon ribbon = Assert.Single(Lay(world, banked));
        RoadSurface surface = new([ribbon]);
        double tan = Math.Tan(6.0 * Math.PI / 180.0);
        foreach (double north in new[] { -5.0, -4.0, -1.0, 0.0, 2.5, 4.9 })
        {
            Assert.True(surface.TryHeightOver(world.Dir(100.0, north) * (world.RadiusM + 3.0), out double over));
            Near.Equal(3.0 - Lift - ((5.0 + north) * tan), over, 1e-6);
        }

        // The same road saved the other way round leans the other way.
        Circuit other = new Circuit { WidthM = 10.0 }.AddNode(0, world.Deg(200), out int far).Extend(far, 0, 0, out int near)
            .SetBank(far, near, 6.0).SetBank(near, far, 6.0);
        RoadSurface reversed = new(Lay(world, other));
        Assert.True(reversed.TryHeightOver(world.Dir(100.0, 4.0) * (world.RadiusM + 3.0), out double south));
        Near.Equal(3.0 - Lift - ((5.0 - 4.0) * tan), south, 1e-6);
    }

    [Fact]
    public void PastTheEdgeAVergeThenABankGoDownIntoTheGroundAndOnAHillsideTheLowSideGoesFurther()
    {
        TrackWorld world = TrackWorld.Earth(TrackWorld.SideSlope(0.1));
        Circuit c = Through(world, 10.0, (0, 0, 2), (300, 0, 2));
        RoadRibbon ribbon = Assert.Single(Lay(world, c));
        RoadRibbon.Section at = ribbon.At(150.0);

        // Level across over ground climbing to the north: the ground is highest under the north edge,
        // and the road is its lift and its height over that.
        Near.Equal(0.5 + Lift + 2.0, at.Height, 1e-6);
        Assert.False(at.Deck);

        // North, the bank meets ground coming up to it; south, ground falling away from it.
        double north = at.ToeLeft, south = at.ToeRight;
        Assert.True(south > north + 2.0, $"{south} m to the south and {north} m to the north");
        foreach ((double toe, double side) in new[] { (north, 1.0), (south, -1.0) })
        {
            double edge = at.Height, end = edge - RoadRibbon.Drop(toe);
            double ground = 0.1 * side * (5.0 + toe);
            Near.Equal(ground - RoadRibbon.BuriedM, end, 1e-3);

            Assert.True(ribbon.Surface(at, side * (5.0 + 1.0), 0.0, out double verge, out double outM, out _, out double across));
            Near.Equal(edge - RoadRibbon.VergeSlope, verge, 1e-9);
            Near.Equal(1.0, outM, 1e-9);
            Near.Equal(-side * RoadRibbon.VergeSlope, across, 1e-12);
            Assert.True(ribbon.Surface(at, side * (5.0 + 2.5), 0.0, out double bank, out _, out _, out across));
            Near.Equal(edge - 0.1 - 0.5, bank, 1e-9);
            Near.Equal(-side * RoadRibbon.BankSlope, across, 1e-12);
            Assert.False(ribbon.Surface(at, side * (5.0 + toe + 0.01), 0.0, out _, out _, out _, out _));
        }
    }

    [Fact]
    public void ARoadHighOverTheGroundIsADeckAndADipUnderTheHeightBetweenTwoDecksIsOneToo()
    {
        TrackWorld world = TrackWorld.Earth();

        // Up to six metres, down to three for a few metres between two points close together, up again, and down to the ground.
        RoadRibbon dipped = Assert.Single(Lay(world, Through(world, 10.0, (0, 0, 6), (100, 0, 6), (108, 0, 3), (116, 0, 6), (220, 0, 6), (400, 0, 0))));
        Assert.True(dipped.At(50.0).Deck);
        Assert.InRange(dipped.At(108.0).Height, 3.0, 3.2);
        Assert.True(dipped.At(108.0).Deck, "a dip of a few metres between two decks changes what the road is");
        Assert.True(dipped.At(200.0).Deck);
        Assert.False(dipped.At(395.0).Deck);

        // It changes once on the way down, at one of the places its sides were worked out at, and is a bank from there on.
        int changes = 0;
        bool last = true;
        for (int i = (int)(220.0 / dipped.SideStepM); i < dipped.SideStretches; i++)
        {
            if (dipped.DeckOver(i) != last) changes++;
            last = dipped.DeckOver(i);
        }
        Assert.Equal(1, changes);

        // A long way under the height is not held: the road comes down to a bank and goes up to a deck again.
        RoadRibbon valley = Assert.Single(Lay(world, Through(world, 10.0, (0, 0, 6), (100, 0, 6), (160, 0, 1), (220, 0, 1), (280, 0, 6), (380, 0, 6))));
        Assert.True(valley.At(50.0).Deck);
        Assert.False(valley.At(190.0).Deck);
        Assert.True(valley.At(330.0).Deck);

        // Beside a deck there is nothing, and under it a wheel on the grass is on the grass.
        RoadSurface surface = new([valley]);
        Assert.False(surface.TryHeightOver(world.Dir(50.0, 5.5) * (world.RadiusM + 6.0), out _));
        Assert.False(surface.TryHeightOver(world.Dir(50.0, 0.0) * (world.RadiusM + 0.3), out _));
        Assert.True(surface.TryLocate(world.Dir(190.0, 6.0) * (world.RadiusM + 1.2), null, out _, out double outM));
        Near.Equal(1.0, outM, 1e-6);
    }

    [Fact]
    public void WhereABendIsTighterThanItsRoadCanTakeTheRibbonSaysWhichRoadAndHowTight()
    {
        TrackWorld world = TrackWorld.Earth();
        Dictionary<string, int> tight = [];
        foreach (string name in ExtremeCircuits.Names)
        {
            int found = 0;
            foreach (RoadRibbon ribbon in Lay(world, ExtremeCircuits.Of(name, world)))
            {
                foreach (RoadLine.Tight t in ribbon.TooTight())
                {
                    found++;
                    Assert.True(t.LeastRadiusM < t.NeededM);
                    RoadRibbon.Span on = ribbon.Spans[ribbon.Profile.Road(Math.Min(t.FromS, ribbon.LengthM - 1e-6))];
                    Assert.InRange(t.FromS, on.FromS - 1e-6, on.ToS + 1e-6);
                }
            }
            tight[name] = found;
        }

        // Of the circuits that are lapped only the kinks are too tight, and those have no radius at all.
        Assert.Equal(5, tight["Kinks"]);
        foreach (string name in ExtremeCircuits.Names.Where(n => n != "Kinks")) Assert.True(tight[name] == 0, $"{tight[name]} on {name}");

        // The same bend is fine under a narrow road and too tight under a wide one.
        Circuit bend = Through(world, 6.0, (0, 0, 0), (60, 0, 0), (60, 60, 0));
        Assert.Empty(Assert.Single(Lay(world, bend)).TooTight());
        List<RoadLine.Tight> wide = Assert.Single(Lay(world, bend with { WidthM = 30.0 })).TooTight();
        Assert.NotEmpty(wide);
        Assert.All(wide, t =>
        {
            Near.Equal(RoadRibbon.RadiusFloor * (15.0 + RoadRibbon.VergeM), t.NeededM, 1e-9);
            Assert.InRange(t.LeastRadiusM, 10.0, t.NeededM);
            Assert.InRange(t.FromS, 40.0, 80.0);
        });
    }
}
