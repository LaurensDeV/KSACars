using Xunit;

namespace KSACars.Tests;

// Within a tolerance, which rounding both to so many places is not: two values a hair apart can round apart.
internal static class Near
{
    public static void Equal(double expected, double actual, double within) =>
        Assert.True(Math.Abs(expected - actual) <= within, $"{actual} is not within {within} of {expected}");
}

public class MonotoneCurveTests
{
    [Fact]
    public void ThroughPointsInALineItIsThatLine()
    {
        MonotoneCurve curve = new([0.0, 100.0, 130.0, 400.0, 410.0], [2.0, 7.0, 8.5, 22.0, 22.5]);

        for (double x = 0.0; x <= 410.0; x += 0.37)
        {
            curve.At(x, out double y, out double slope, out double bend);
            Assert.Equal(2.0 + (0.05 * x), y, 10);
            Assert.Equal(0.05, slope, 12);
            Assert.Equal(0.0, bend, 12);
        }
    }

    [Fact]
    public void ItNeverLeavesTheSpanOfTheTwoPointsItIsBetweenAndItsSlopeHasNoStep()
    {
        Random random = new(11);
        for (int run = 0; run < 200; run++)
        {
            int count = random.Next(2, 9);
            double[] x = new double[count], y = new double[count];
            for (int i = 0; i < count; i++)
            {
                x[i] = (i == 0 ? 0.0 : x[i - 1]) + 1.0 + (200.0 * random.NextDouble());
                y[i] = random.Next(4) == 0 && i > 0 ? y[i - 1] : 40.0 * random.NextDouble();
            }
            bool round = run % 3 == 0;
            MonotoneCurve curve = new(x, y, round ? x[^1] - x[0] + 50.0 : 0.0);

            for (int i = 0; i + 1 < count; i++)
            {
                double low = Math.Min(y[i], y[i + 1]), high = Math.Max(y[i], y[i + 1]);
                for (int k = 0; k <= 50; k++)
                {
                    double at = curve.At(x[i] + ((x[i + 1] - x[i]) * k / 50.0));
                    Assert.InRange(at, low - 1e-9, high + 1e-9);
                }
            }
            for (int i = round ? 0 : 1; i < count - (round ? 0 : 1); i++)
            {
                curve.At(x[i] - 1e-7, out double before, out double slopeBefore, out _);
                curve.At(x[i] + 1e-7, out double after, out double slopeAfter, out _);
                Near.Equal(y[i], before, 1e-4);
                Near.Equal(y[i], after, 1e-4);
                Assert.True(Math.Abs(slopeAfter - slopeBefore) < 1e-5, $"the slope steps by {slopeAfter - slopeBefore} at point {i}");
            }
        }
    }

    [Fact]
    public void ItsSlopeAndItsBendAreItsOwn()
    {
        MonotoneCurve curve = new([0.0, 60.0, 100.0, 260.0], [0.0, 9.0, 4.0, 4.5], 300.0);
        const double step = 1e-4;
        for (double x = -40.0; x < 700.0; x += 3.3)
        {
            curve.At(x, out _, out double slope, out double bend);
            curve.At(x + step, out _, out double slopeOn, out _);
            curve.At(x - step, out _, out double slopeBack, out _);

            // Not across a point, where the bend steps.
            double within = ((x % 300.0) + 300.0) % 300.0;
            if (new[] { 0.0, 60.0, 100.0, 260.0, 300.0 }.Any(k => Math.Abs(within - k) < 0.01)) continue;
            Near.Equal((curve.At(x + step) - curve.At(x - step)) / (2.0 * step), slope, 1e-6);
            Near.Equal((slopeOn - slopeBack) / (2.0 * step), bend, 1e-6);
        }
        Near.Equal(curve.At(17.0), curve.At(317.0), 1e-9);
        Near.Equal(curve.At(283.0), curve.At(-17.0), 1e-9);
    }
}

public class RoadProfileTests
{
    private const double Speed = 80.0;

    private static RoadProfile Climb(params double[] heights)
    {
        double[] knot = [.. heights.Select((_, i) => 100.0 * i)];
        return new RoadProfile(knot, heights, [.. heights.Select(_ => 0.0)], [.. heights.Select(_ => 10.0)], false, 0.07);
    }

    // What a point driven along the centre line at a steady speed over the ground feels: how fast it
    // climbs, each centimetre along, and the greatest change of that between two.
    private const double Each = 0.01;

    private static (double MostAccel, double MostStep) Driven(RoadProfile profile)
    {
        double mostAccel = 0.0, mostStep = 0.0, last = double.NaN;
        for (double s = 0.0; s <= profile.LengthM; s += Each)
        {
            profile.Height(s, out _, out double slope, out double bend);
            mostAccel = Math.Max(mostAccel, Math.Abs(Speed * Speed * bend));
            if (!double.IsNaN(last)) mostStep = Math.Max(mostStep, Math.Abs((slope - last) * Speed));
            last = slope;
        }
        return (mostAccel, mostStep);
    }

    [Fact]
    public void ASteadyClimbThroughSeveralPointsIsARampWithNothingToFeel()
    {
        RoadProfile ramp = Climb(0.0, 5.0, 10.0, 15.0, 20.0, 25.0);

        (double accel, double step) = Driven(ramp);
        Assert.True(accel < 1e-9, $"{accel} m/s2 on a steady climb");
        Assert.True(step < 1e-9);
        for (int road = 0; road < ramp.Roads; road++)
        {
            (double grade, double radius) = ramp.Steepness(road);
            Assert.Equal(0.05, grade, 12);
            Assert.True(radius > 1e9);
        }

        // Eased level at each point, as a road once was, the same climb is this at its worst.
        Assert.Equal(19.2, Speed * Speed * 6.0 * 5.0 / (100.0 * 100.0), 9);
    }

    [Fact]
    public void OverACrestTheClimbBendsNoHarderThanItSaysAndNeverSteps()
    {
        RoadProfile crest = Climb(0.0, 4.0, 9.0, 9.5, 3.0, 0.0, 0.0);

        (double accel, double step) = Driven(crest);
        double mostBend = 0.0, steepest = 0.0, least = double.PositiveInfinity;
        for (int road = 0; road < crest.Roads; road++)
        {
            (double grade, double radius) = crest.Steepness(road, 0.01);
            steepest = Math.Max(steepest, grade);
            least = Math.Min(least, radius);
        }
        for (double s = 0.0; s <= crest.LengthM; s += 0.01)
        {
            crest.Height(s, out double height, out _, out double bend);
            mostBend = Math.Max(mostBend, Math.Abs(bend));
            Assert.InRange(height, 0.07 - 1e-9, 9.57 + 1e-9);
        }

        Assert.True(accel <= (Speed * Speed * mostBend) + 1e-9);
        Assert.InRange(accel, 1.0, 30.0);

        // A centimetre at this speed is an eighth of a millisecond, and the rate of climb changes in
        // it by no more than the bend gives in that time: there is no step in it at any point.
        Assert.True(step <= (1.01 * accel * Each / Speed) + 1e-9, $"the climb steps by {step} m/s where {accel * Each / Speed} is the most the bend gives");
        Assert.InRange(steepest, 0.065, 0.12);
        Assert.InRange(least, Speed * Speed / 30.0, Speed * Speed / 1.0);
    }

    [Fact]
    public void APointBelowTheGroundIsLaidOnIt()
    {
        RoadProfile dug = Climb(0.0, -6.0, 0.0);
        for (double s = 0.0; s <= 200.0; s += 1.0)
        {
            dug.Height(s, out double height, out _, out _);
            Assert.Equal(0.07, height, 12);
        }
    }

    [Fact]
    public void TheBankAndTheWidthEaseFromOnePointToTheNextWithNoStep()
    {
        RoadProfile road = new([0.0, 120.0, 200.0, 420.0], [0.0, 0.0, 0.0, 0.0], [0.0, 8.0, 8.0, -3.0], [10.0, 10.0, 6.0, 14.0], false, 0.07);
        const double step = 1e-5;

        road.Bank(0.0, out double tan, out _);
        Assert.Equal(0.0, tan, 12);
        road.Bank(160.0, out tan, out double rate);
        Assert.Equal(Math.Tan(8.0 * Math.PI / 180.0), tan, 12);
        Assert.Equal(0.0, rate, 12);
        road.HalfWidth(200.0, out double half, out _);
        Assert.Equal(3.0, half, 12);

        for (double s = 1.0; s < 419.0; s += 0.7)
        {
            road.Bank(s - step, out double tanBack, out _);
            road.Bank(s, out tan, out rate);
            road.Bank(s + step, out double tanOn, out _);
            Assert.InRange(tan, Math.Tan(-3.0 * Math.PI / 180.0) - 1e-12, Math.Tan(8.0 * Math.PI / 180.0) + 1e-12);
            Near.Equal((tanOn - tanBack) / (2.0 * step), rate, 1e-7);

            road.HalfWidth(s - step, out double halfBack, out _);
            road.HalfWidth(s, out half, out double widening);
            road.HalfWidth(s + step, out double halfOn, out _);
            Assert.InRange(half, 3.0, 7.0);
            Near.Equal((halfOn - halfBack) / (2.0 * step), widening, 1e-7);
        }
    }

    [Fact]
    public void AnEndThatMeetsOtherRoadsIsSunkAndComesUpOverTwoWidthsWithNoStepInItsSlope()
    {
        RoadProfile side = new([0.0, 200.0], [0.0, 0.0], [0.0, 0.0], [10.0, 10.0], false, 0.07, 0.012, 0.02);

        side.Above(0.0, out double above, out double slope, out _);
        Assert.Equal(0.058, above, 12);
        Assert.Equal(0.0, slope, 12);
        side.Above(200.0, out above, out _, out _);
        Assert.Equal(0.05, above, 12);
        side.Above(100.0, out above, out _, out _);
        Assert.Equal(0.07, above, 12);

        double last = 0.0;
        for (double s = 0.0; s <= 200.0; s += 0.01)
        {
            side.Above(s, out _, out slope, out _);
            Assert.True(Math.Abs(slope - last) < 4e-6, $"the slope steps by {slope - last} at {s} m");
            last = slope;
        }
    }
}

public class RoadGroundTests
{
    private const double Spacing = 2.0;

    private static double Between(double[] ground, double s, bool closed)
    {
        int i = (int)Math.Floor(s / Spacing);
        double t = (s / Spacing) - i;
        double a = ground[closed ? i % ground.Length : Math.Min(i, ground.Length - 1)];
        double b = ground[closed ? (i + 1) % ground.Length : Math.Min(i + 1, ground.Length - 1)];
        return a + ((b - a) * t);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverAnyGroundItIsNeverUnderItAndItsSlopeNeverSteps(bool closed)
    {
        Random random = new(5);
        for (int run = 0; run < 60; run++)
        {
            int n = random.Next(3, 400);
            double window = new[] { 0.0, 6.0, 30.0, 90.0 }[run % 4];
            double[] ground = new double[n];
            double rough = random.NextDouble() * 2.0, tilt = (random.NextDouble() - 0.5) * 0.4;
            for (int i = 0; i < n; i++)
            {
                ground[i] = (tilt * i * Spacing * (closed ? 0.0 : 1.0)) + (8.0 * Math.Sin(i * 0.07 * (1 + (run % 5)))) + (rough * random.NextDouble())
                          + (random.Next(40) == 0 ? 5.0 : 0.0);
            }

            RoadGround smooth = RoadGround.Smooth(ground, Spacing, window, closed);
            double length = (closed ? n : n - 1) * Spacing;
            for (double s = 0.0; s <= length; s += 0.05)
            {
                Assert.True(smooth.At(s) >= Between(ground, s, closed) - 1e-9, $"buried by {Between(ground, s, closed) - smooth.At(s)} m at {s} m, run {run}");
            }
            for (int i = closed ? 0 : 1; i < n - (closed ? 0 : 1); i++)
            {
                smooth.At((i * Spacing) - 1e-7, out _, out double before, out _);
                smooth.At((i * Spacing) + 1e-7, out _, out double after, out _);
                Assert.True(Math.Abs(after - before) < 1e-5, $"the slope steps by {after - before} at sample {i}, run {run}");
            }
        }
    }

    [Fact]
    public void LevelGroundAndASteadySlopeComeBackAsTheyAre()
    {
        double[] level = [.. Enumerable.Repeat(31.5, 200)];
        RoadGround flat = RoadGround.Smooth(level, Spacing, 30.0, false);
        for (double s = 0.0; s < 398.0; s += 1.3) Assert.Equal(31.5, flat.At(s), 12);

        double[] slope = [.. Enumerable.Range(0, 200).Select(i => 0.05 * i * Spacing)];
        RoadGround ramp = RoadGround.Smooth(slope, Spacing, 30.0, false);
        for (double s = 0.0; s <= 398.0; s += 1.3)
        {
            ramp.At(s, out double height, out double climb, out double bend);
            Near.Equal(0.05 * s, height, 1e-9);
            Near.Equal(0.05, climb, 1e-9);
            Near.Equal(0.0, bend, 1e-9);
        }
    }

    // A rock a metre high and one sample long in level ground: the road goes over it, coming up and
    // going down over the smoothing's width either side, and never as sharply as the rock.
    [Fact]
    public void ABumpIsFilledOverAndTheRoadRisesToItGently()
    {
        double[] ground = new double[200];
        ground[100] = 1.0;
        RoadGround smooth = RoadGround.Smooth(ground, Spacing, 30.0, false);

        Assert.True(smooth.At(200.0) >= 1.0);
        Near.Equal(0.0, smooth.At(200.0 - 70.0), 1e-9);
        Near.Equal(0.0, smooth.At(200.0 + 70.0), 1e-9);

        double mostBend = 0.0, steepest = 0.0, highest = 0.0;
        for (double s = 0.0; s < 398.0; s += 0.05)
        {
            smooth.At(s, out double height, out double slope, out double bend);
            (mostBend, steepest, highest) = (Math.Max(mostBend, Math.Abs(bend)), Math.Max(steepest, Math.Abs(slope)), Math.Max(highest, height));
        }
        Assert.InRange(highest, 1.0, 1.3);
        Assert.True(steepest < 0.1, $"as steep as {steepest}");

        // The rock itself bends the ground by a metre over two metres each way, 0.5 a metre.
        Assert.True(mostBend < 0.02, $"bends by {mostBend} a metre");
    }
}

public class CircuitFormatTests
{
    private static double Deg(double metres) => metres / 6_371_000.0 * 180.0 / Math.PI;

    [Fact]
    public void WhatIsSetAtARoadsEndsAndAtAPointComesBackFromTheFile()
    {
        Circuit c = new Circuit { Name = "Banked" }
            .AddNode(0.0, 0.0, out int a).Extend(a, 0.0, Deg(300), out int b).Extend(b, Deg(200), Deg(300), out int d)
            .SetBank(b, a, 9.0).SetBank(b, d, -4.0).SetEndWidth(a, b, 14.0).SetEndWidth(d, b, 7.0)
            .SetJunctionRadius(b, 12.0).SetGroundSmooth(45.0).SetHeight(d, 3.0);

        string json = c.ToJson();
        Assert.Contains("\"version\": 2", json);
        Assert.Contains("\"to_bank_deg\": 9", json);
        Assert.Contains("\"ground_smooth_m\": 45", json);
        Assert.Contains("\"junction_radius_m\": 12", json);

        Circuit? back = Circuit.FromJson(json, out string why, out int dropped);
        Assert.NotNull(back);
        Assert.Equal("", why);
        Assert.Equal(0, dropped);
        Assert.Equal(json, back.ToJson());
        Assert.Equal(9.0, Circuit.EndBankDeg(back.Roads[0], b));
        Assert.Null(Circuit.EndBankDeg(back.Roads[0], a));
        Assert.Equal(-4.0, Circuit.EndBankDeg(back.Roads[1], b));
        Assert.Equal(14.0, Circuit.EndWidth(back.Roads[0], a));
        Assert.Null(Circuit.EndWidth(back.Roads[0], b));
        Assert.Equal(7.0, Circuit.EndWidth(back.Roads[1], d));
        Assert.Equal(12.0, back.Find(b)!.JunctionRadiusM);
        Assert.Equal(45.0, back.GroundSmoothM);
    }

    [Fact]
    public void AFileOfTheFirstVersionLoadsWithEverythingItDoesNotSayAtItsDefault()
    {
        Circuit? read = Circuit.FromJson("""
            {"version": 1, "name": "old", "body": "Earth", "width_m": 12,
             "nodes": [{"id": 1, "lat_deg": 0, "lon_deg": 0, "height_m": -3}, {"id": 2, "lat_deg": 0, "lon_deg": 0.01, "corner": 0.5, "height_m": 4}],
             "roads": [{"from": 1, "to": 2, "width_m": 8}]}
            """, out string why, out int dropped);

        Assert.NotNull(read);
        Assert.Equal("", why);
        Assert.Equal(0, dropped);
        Assert.Equal(Circuit.CurrentVersion, read.Version);
        Assert.Equal(Circuit.DefaultGroundSmoothM, read.GroundSmoothM);
        Assert.Null(read.Nodes[0].JunctionRadiusM);
        Assert.Equal(-3.0, read.Nodes[0].HeightM);
        Assert.Equal(4.0, read.Nodes[1].HeightM);
        Circuit.Road road = Assert.Single(read.Roads);
        Assert.Equal(8.0, read.WidthOf(road));
        Assert.Null(road.FromBankDeg);
        Assert.Null(road.ToBankDeg);
        Assert.Null(road.FromWidthM);
        Assert.Null(road.ToWidthM);
    }

    [Fact]
    public void WhatAFileSaysIsHeldToWhatARoadCanBe()
    {
        Circuit? read = Circuit.FromJson("""
            {"version": 2, "ground_smooth_m": 9000,
             "nodes": [{"id": 1, "lat_deg": 0, "lon_deg": 0, "junction_radius_m": -5}, {"id": 2, "lat_deg": 0, "lon_deg": 0.01}],
             "roads": [{"from": 1, "to": 2, "from_bank_deg": 80, "to_width_m": 0.1}]}
            """, out _, out _);

        Assert.NotNull(read);
        Assert.Equal(Circuit.MaxGroundSmoothM, read.GroundSmoothM);
        Assert.Equal(0.0, read.Nodes[0].JunctionRadiusM);
        Assert.Equal(Circuit.MaxBankDeg, read.Roads[0].FromBankDeg);
        Assert.Equal(Circuit.MinWidthM, read.Roads[0].ToWidthM);
    }

    [Fact]
    public void APointIsSetNoLowerThanTheGroundAndASplitRoadKeepsWhatItsFarEndsHad()
    {
        Circuit c = new Circuit().AddNode(0.0, 0.0, out int a).Extend(a, 0.0, Deg(300), out int b)
            .SetHeight(a, -4.0).SetBank(a, b, 5.0).SetBank(b, a, 7.0).SetEndWidth(b, a, 6.0);
        Assert.Equal(0.0, c.Find(a)!.HeightM);

        Circuit split = c.Split(a, b, 0.0, Deg(150), out int mid);
        Circuit.Road first = split.Roads.Single(r => r.Joins(a, mid)), second = split.Roads.Single(r => r.Joins(mid, b));
        Assert.Equal(5.0, Circuit.EndBankDeg(first, a));
        Assert.Null(Circuit.EndBankDeg(first, mid));
        Assert.Null(Circuit.EndBankDeg(second, mid));
        Assert.Equal(7.0, Circuit.EndBankDeg(second, b));
        Assert.Equal(6.0, Circuit.EndWidth(second, b));
    }
}
