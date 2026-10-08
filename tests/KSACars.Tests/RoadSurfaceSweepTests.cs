using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

/// <summary>
/// Roads laid as the game lays them, over a range of shapes, and asked what a wheel would ask: the
/// geometry here is worked in the plan, east and north of a place on the equator, from points half a
/// metre apart along each road's centre line, so none of it is the surface's own arithmetic across
/// the road or its way of finding a place on it.
/// </summary>
public class RoadSurfaceSweepTests
{
    private const double R = 1_737_400.0, Hub = 0.33, Lift = 0.07;

    private static double3 DirOf(double latDeg, double lonDeg)
    {
        double lat = latDeg * Math.PI / 180.0, lon = lonDeg * Math.PI / 180.0;
        return new double3(Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));
    }

    private static double Deg(double metres) => metres / R * 180.0 / Math.PI;

    // A place in the plan, and how far it is from the body's centre.
    private readonly record struct P(double E, double N, double Rad);

    private static P Plan(double3 p) => new(Math.Atan2(p.Y, p.X) * R, Math.Asin(p.Z / Vec.Len(p)) * R, Vec.Len(p));

    private static P[] Plan(double3[] line) => [.. line.Select(Plan)];

    private static double3 At(double e, double n, double rad) => DirOf(Deg(n), Deg(e)) * rad;

    // A laid road's centre line, closely enough that the straight line between two points is the road's own to a centimetre.
    private const double CentreEveryM = 0.5;

    private static P[] Centre(RoadLaying.Strip strip)
    {
        RoadRibbon ribbon = strip.Ribbon!;
        List<P> line = [];
        for (int arc = 0; arc < ribbon.Line.Count; arc++)
        {
            double from = ribbon.Line.StartOf(arc), length = ribbon.Line.StartOf(arc + 1) - from;
            int steps = Math.Max(1, (int)Math.Ceiling(length / CentreEveryM));
            for (int i = line.Count == 0 ? 0 : 1; i <= steps; i++)
            {
                RoadRibbon.Section section = ribbon.At(arc, from + (length * i / steps));
                line.Add(Plan(ribbon.Point(section, 0.0, section.Height)));
            }
        }
        return [.. line];
    }

    private static double Between(P a, P b) => Math.Sqrt(((a.E - b.E) * (a.E - b.E)) + ((a.N - b.N) * (a.N - b.N)));

    private sealed record Laid(List<RoadLaying.Strip> Strips, RoadSurface Surface);

    private static Laid Lay(Circuit circuit, double spacingM, Func<double3, double>? ground = null)
    {
        List<RoadLaying.Strip> strips = RoadLaying.Lay(circuit, DirOf, R, ground ?? (_ => 0.0), Lift, spacingM);
        return new Laid(strips, RoadLaying.Surface(strips));
    }

    // A road west to east through the origin that turns left there by so many degrees.
    private static Circuit Bend(double legM, double turnDeg, double corner, double widthM, double h0, double h1, double h2)
    {
        double turn = turnDeg * Math.PI / 180.0;
        return new Circuit { WidthM = widthM }.AddNode(0, Deg(-legM), out int a).Extend(a, 0, 0, out int b)
            .Extend(b, Deg(legM * Math.Sin(turn)), Deg(legM * Math.Cos(turn)), out int c)
            .SetCorner(a, corner).SetCorner(b, corner).SetCorner(c, corner)
            .SetHeight(a, h0).SetHeight(b, h1).SetHeight(c, h2);
    }

    // The nearest place on a line, seen from above: how far it is, how high the line is there, and
    // whether it is the line's end with the point out beyond it. Within its last piece counts as
    // beyond: a road ends square to its own heading there, which across a wide road is a hand's
    // breadth from square to the line between two points.
    private static (double Metres, double Rad, bool PastEnd) Nearest(P[] line, double e, double n)
    {
        (double best, double rad, bool pastEnd) = (double.PositiveInfinity, 0.0, false);
        for (int i = 1; i < line.Length; i++)
        {
            P a = line[i - 1], b = line[i];
            double de = b.E - a.E, dn = b.N - a.N, len2 = (de * de) + (dn * dn);
            double along = len2 > 0.0 ? (((e - a.E) * de) + ((n - a.N) * dn)) / len2 : 0.0, t = Math.Clamp(along, 0.0, 1.0);
            double fe = a.E + (de * t) - e, fn = a.N + (dn * t) - n, d = Math.Sqrt((fe * fe) + (fn * fn));
            if (d < best) (best, rad, pastEnd) = (d, a.Rad + ((b.Rad - a.Rad) * t), (i == 1 && along < 1.0) || (i == line.Length - 1 && along > 0.0));
        }
        return (best, rad, pastEnd);
    }

    // The tightest circle through three points in a row.
    private static double TightestRadius(P[] line)
    {
        double least = double.PositiveInfinity;
        for (int i = 2; i < line.Length; i++)
        {
            double a = Between(line[i - 2], line[i - 1]), b = Between(line[i - 1], line[i]), c = Between(line[i - 2], line[i]);
            double cross = Math.Abs(((line[i - 1].E - line[i - 2].E) * (line[i].N - line[i - 2].N))
                                  - ((line[i - 1].N - line[i - 2].N) * (line[i].E - line[i - 2].E)));
            if (cross > 1e-9) least = Math.Min(least, a * b * c / (2.0 * cross));
        }
        return least;
    }

    private static double SteepestGradient(IEnumerable<RoadLaying.Strip> strips)
    {
        double steepest = 0.0;
        foreach (RoadLaying.Strip strip in strips)
        {
            P[] line = Plan(strip.Line);
            for (int i = 1; i < line.Length; i++)
            {
                double run = Between(line[i - 1], line[i]);
                if (run > 1e-6) steepest = Math.Max(steepest, Math.Abs(line[i].Rad - line[i - 1].Rad) / run);
            }
        }
        return steepest;
    }

    // ---- a. every point of a road is over it ------------------------------------------------------

    private sealed record Coverage(int Points, int Missed, int MissedRemembered, int Differed, int NotLevel, double WorstLevel, string FirstMiss,
                                   string FirstDiffered, string FirstNotLevel);

    // Points round every point of the line and beside the middle of every piece, all within the
    // half width less 5 cm of the line, a hub's height above the line's nearest point.
    private static readonly Lazy<Coverage> Swept = new(() =>
    {
        int points = 0, missed = 0, missedRemembered = 0, differed = 0, notLevel = 0;
        double worstLevel = 0.0;
        string firstMiss = "", firstDiffered = "", firstNotLevel = "";
        (double, double, double)[] heights = [(0, 0, 0), (8, 8, 8), (0, 3, 6)];

        foreach (double leg in new[] { 30.0, 40.0, 100.0 })
        foreach (double turn in new[] { 30.0, 90.0, 150.0 })
        foreach (double corner in new[] { 0.0, 0.3, 1.0, 1.5 })
        foreach (double spacing in new[] { 2.0, 6.0 })
        foreach (double width in new[] { 6.0, 12.0, 40.0 })
        foreach ((double h0, double h1, double h2) in heights)
        {
            Laid laid = Lay(Bend(leg, turn, corner, width, h0, h1, h2), spacing);
            P[] line = Centre(Assert.Single(laid.Strips));
            double reach = (0.5 * width) - 0.05;
            bool level = h0 == h2;
            string shape = $"legs {leg} m, turn {turn} deg, corner {corner}, spacing {spacing} m, width {width} m, heights {h0}/{h1}/{h2}";

            void Ask(double e, double n)
            {
                (double off, double rad, bool pastEnd) = Nearest(line, e, n);
                if (off > reach + 1e-6 || pastEnd) return;
                double3 at = At(e, n, rad + Hub);
                points++;
                bool fresh = laid.Surface.TryHeightOver(at, out double over);
                bool remembered = laid.Surface.TryHeightOver(at, Hub, out double still);
                string where = $"{shape}: {e:F2} m east, {n:F2} m north, {off:F2} m off the line";
                if (!fresh && missed++ == 0) firstMiss = where;
                if (!remembered) missedRemembered++;
                if (fresh && remembered && Math.Abs(over - still) > 1e-6 && differed++ == 0) firstDiffered = $"{where}: {over:F3} fresh, {still:F3} remembered";
                if (fresh && level && Math.Abs(over - Hub) > 0.02)
                {
                    if (notLevel++ == 0) firstNotLevel = $"{where}: {over:F3} m over it";
                    worstLevel = Math.Max(worstLevel, Math.Abs(over - Hub));
                }
            }

            for (int i = 0; i < line.Length; i += (int)(spacing / CentreEveryM))
            {
                for (int k = 0; k < 8; k++)
                {
                    double angle = (k * Math.PI / 4.0) + 0.1, out_ = k % 2 == 0 ? reach : 0.6 * reach;
                    Ask(line[i].E + (out_ * Math.Cos(angle)), line[i].N + (out_ * Math.Sin(angle)));
                }
                if (i == 0) continue;
                double de = line[i].E - line[i - 1].E, dn = line[i].N - line[i - 1].N, len = Math.Sqrt((de * de) + (dn * dn));
                if (len < 1e-6) continue;
                double me = 0.5 * (line[i].E + line[i - 1].E), mn = 0.5 * (line[i].N + line[i - 1].N);
                Ask(me - (dn / len * reach), mn + (de / len * reach));
                Ask(me + (dn / len * reach), mn - (de / len * reach));
            }
        }
        return new Coverage(points, missed, missedRemembered, differed, notLevel, worstLevel, firstMiss, firstDiffered, firstNotLevel);
    });

    [Fact]
    public void EveryPointWithinTheWidthOfALaidLineIsOverARoad()
    {
        Coverage c = Swept.Value;
        Assert.True(c.Points > 50_000, $"only {c.Points} points asked");
        Assert.True(c.Missed == 0, $"{c.Missed} of {c.Points} points found no road, the first at {c.FirstMiss}");
        Assert.True(c.MissedRemembered == 0, $"{c.MissedRemembered} of {c.Points} points found no road for a wheel that was on one");
    }

    [Fact]
    public void OnARoadByItselfAWheelThatWasOnItIsToldWhatOneNewToItIs()
    {
        Coverage c = Swept.Value;
        Assert.True(c.Differed == 0, $"{c.Differed} of {c.Points} points answered differently, the first at {c.FirstDiffered}");
    }

    [Fact]
    public void ALevelRoadIsLevelRoundEveryBend()
    {
        Coverage c = Swept.Value;
        Assert.True(c.NotLevel == 0, $"{c.NotLevel} points over a level road were more than 2 cm out, the worst by {c.WorstLevel:F3} m, the first at {c.FirstNotLevel}");
    }

    // ---- b. no step in a road walked along or across ----------------------------------------------

    // The line moved sideways by so much, each corner of it on the line that halves the turn there:
    // the edge of the road, or a wheel's track along it.
    private static P[] Beside(P[] line, double offset)
    {
        P[] beside = new P[line.Length];
        for (int i = 0; i < line.Length; i++)
        {
            (double e0, double n0) = Heading(line, Math.Max(i - 1, 0));
            (double e1, double n1) = Heading(line, Math.Min(i, line.Length - 2));
            double be = e0 + e1, bn = n0 + n1, bl = Math.Sqrt((be * be) + (bn * bn));
            (be, bn) = (be / bl, bn / bl);
            double cos = (be * e1) + (bn * n1);
            beside[i] = new P(line[i].E - (bn * offset / cos), line[i].N + (be * offset / cos), line[i].Rad);
        }
        return beside;
    }

    private static (double E, double N) Heading(P[] line, int piece)
    {
        double de = line[piece + 1].E - line[piece].E, dn = line[piece + 1].N - line[piece].N, len = Math.Sqrt((de * de) + (dn * dn));
        return (de / len, dn / len);
    }

    [Fact]
    public void AClimbingBendHasNoStepInItWalkedAlongItsEdgesOrAcrossIt()
    {
        int walked = 0, samples = 0, lost = 0, stepped = 0;
        double worst = 0.0;
        string firstLost = "", worstStep = "";

        // No sharper than 120 degrees: past that the two legs lie within a width of each other at
        // different heights, and the edge of the higher one is a step that is really there.
        foreach (double leg in new[] { 30.0, 60.0 })
        foreach (double grade in new[] { 0.15, 0.30 })
        foreach (bool slopedGround in new[] { false, true })
        foreach (double turn in new[] { 30.0, 90.0, 120.0 })
        foreach (double corner in new[] { 1.0, 1.5 })
        foreach (double spacing in new[] { 2.0, 6.0 })
        foreach (double width in new[] { 4.0, 8.0 })
        {
            // Either the road climbs from point to point over level ground, or it lies on a hillside
            // that rises the way the bend's middle faces.
            double half = turn * Math.PI / 360.0;
            Func<double3, double>? ground = slopedGround
                ? dir => grade * R * ((dir.Y * Math.Cos(half)) + (dir.Z * Math.Sin(half)))
                : null;
            double rise = slopedGround ? 0.0 : leg * grade;
            Laid laid = Lay(Bend(leg, turn, corner, width, 0.0, rise, 2.0 * rise), spacing, ground);
            P[] line = Centre(Assert.Single(laid.Strips));
            double halfWidth = 0.5 * width;

            // Tighter than this the inside edge folds over itself and has no one height.
            if (TightestRadius(line) < 1.25 * halfWidth) continue;
            walked++;
            string shape = $"legs {leg} m at {grade:P0}{(slopedGround ? " of hillside" : "")}, turn {turn} deg, corner {corner}, spacing {spacing} m, width {width} m";

            foreach (double offset in new[] { -(halfWidth - 0.1), -0.5 * halfWidth, 0.0, 0.5 * halfWidth, halfWidth - 0.1 })
            {
                P[] track = Beside(line, offset);
                double[] gradient = new double[track.Length - 1];
                for (int i = 0; i < gradient.Length; i++) gradient[i] = Math.Abs(track[i + 1].Rad - track[i].Rad) / Between(track[i], track[i + 1]);

                foreach (double stride in new[] { 0.05, 0.31, 1.0 })
                {
                    if (stride < 0.1 && (Math.Abs(offset) < halfWidth - 0.2 || leg > 30.0)) continue;
                    double? before = null;
                    double done = 0.0;
                    // Not the first metre or the last: the road ends square to its own heading, not to this line's.
                    for (int i = 2; i < track.Length - 3; i++)
                    {
                        double length = Between(track[i], track[i + 1]);
                        // The steepest of the pieces a stride can reach back over.
                        double local = 0.0;
                        for (int k = Math.Max(i - 3, 0); k <= Math.Min(i + 3, gradient.Length - 1); k++) local = Math.Max(local, gradient[k]);
                        for (; done <= length; done += stride)
                        {
                            double t = done / length;
                            double rad = track[i].Rad + ((track[i + 1].Rad - track[i].Rad) * t) + Hub;
                            double3 at = At(track[i].E + ((track[i + 1].E - track[i].E) * t), track[i].N + ((track[i + 1].N - track[i].N) * t), rad);
                            samples++;
                            if (!laid.Surface.TryHeightOver(at, out double over))
                            {
                                if (lost++ == 0) firstLost = $"{shape}: {offset:F2} m off the line, piece {i}";
                                before = null;
                                continue;
                            }
                            double surface = rad - over;
                            if (before is { } was && Math.Abs(surface - was) > (local * stride) + 0.02)
                            {
                                stepped++;
                                double by = Math.Abs(surface - was) - (local * stride);
                                if (by > worst) (worst, worstStep) = (by, $"{shape}: {offset:F2} m off the line, piece {i}, stride {stride} m");
                            }
                            before = surface;
                        }
                        done -= length;
                    }
                }
            }

            // Straight across the middle of each piece, where the road is level.
            for (int i = 0; i < line.Length - 1; i += 12)
            {
                (double he, double hn) = Heading(line, i);
                double me = 0.5 * (line[i].E + line[i + 1].E), mn = 0.5 * (line[i].N + line[i + 1].N), rad = (0.5 * (line[i].Rad + line[i + 1].Rad)) + Hub;
                double? before = null;
                for (double across = -(halfWidth - 0.1); across <= halfWidth - 0.1; across += 0.05)
                {
                    samples++;
                    if (!laid.Surface.TryHeightOver(At(me - (hn * across), mn + (he * across), rad), out double over))
                    {
                        if (lost++ == 0) firstLost = $"{shape}: {across:F2} m across piece {i}";
                        before = null;
                        continue;
                    }
                    if (before is { } was && Math.Abs(over - was) > 0.02)
                    {
                        stepped++;
                        if (Math.Abs(over - was) > worst) (worst, worstStep) = (Math.Abs(over - was), $"{shape}: {across:F2} m across piece {i}");
                    }
                    before = over;
                }
            }
        }

        Assert.True(walked >= 40, $"only {walked} bends were gentle enough to walk");
        Assert.True(lost == 0 && stepped == 0,
            $"of {samples} samples {lost} found no road, the first at {firstLost}; and {stepped} stepped, the worst {worst:F3} m more than its slope, at {worstStep}");
    }

    // ---- c. junctions ------------------------------------------------------------------------------

    // A wheel rolled along a path, its hub kept its height above whatever it was last told is under
    // it, and remembering that as a car does. Answers every surface it was told and where.
    private static List<(double E, double N, double Surface)> Roll(Laid laid, IReadOnlyList<(double E, double N)> path, double startRad,
                                                                   out int lost)
    {
        List<(double, double, double)> told = [];
        lost = 0;
        double surface = startRad;
        double? last = null;
        foreach ((double e, double n) in path)
        {
            double rad = surface + Hub;
            if (!laid.Surface.TryHeightOver(At(e, n, rad), last, out double over))
            {
                lost++;
                last = null;
                continue;
            }
            surface = rad - over;
            last = Hub;
            told.Add((e, n, surface));
        }
        return told;
    }

    private static List<(double E, double N)> Path(double stride, params (double E, double N)[] through)
    {
        List<(double, double)> path = [];
        for (int i = 1; i < through.Length; i++)
        {
            double de = through[i].E - through[i - 1].E, dn = through[i].N - through[i - 1].N, length = Math.Sqrt((de * de) + (dn * dn));
            for (double done = 0.0; done < length; done += stride) path.Add((through[i - 1].E + (de * done / length), through[i - 1].N + (dn * done / length)));
        }
        return path;
    }

    [Theory]
    [InlineData(100.0, 3.0)]
    [InlineData(100.0, 8.0)]
    [InlineData(60.0, 6.0)]
    public void AWheelLeavingAJunctionUpAClimbingSideRoadIsOnTheSideRoadAllTheWay(double sideM, double riseM)
    {
        foreach (double spacing in new[] { 2.0, 6.0 })
        foreach (double stride in new[] { 0.05, 0.3, 1.0 })
        {
            Circuit c = new Circuit { WidthM = 10.0 }.AddNode(0, 0, out int mid)
                .Extend(mid, 0, Deg(-100), out _).Extend(mid, 0, Deg(100), out _).Extend(mid, Deg(sideM), 0, out int far)
                .SetHeight(far, riseM);
            Laid laid = Lay(c, spacing);
            // Each half of the through road and the side road, every one of them stopping at the junction's mouth.
            Assert.Equal(3, laid.Strips.Count);
            P[] side = Centre(laid.Strips.Single(s => Plan(s.Line).Max(p => p.N) > 20.0));
            double steepest = SteepestGradient(laid.Strips);

            List<(double E, double N, double Surface)> told = Roll(laid, Path(stride, (-20.0, 0.0), (0.0, 0.0), (0.0, sideM - 1.0)), R + Lift, out int lost);

            Assert.True(lost == 0, $"{lost} samples found no road at spacing {spacing} m, stride {stride} m");
            (double step, double stepAt, double under, double underAt) = (0.0, 0.0, 0.0, 0.0);
            for (int i = 1; i < told.Count; i++)
            {
                double more = Math.Abs(told[i].Surface - told[i - 1].Surface) - (steepest * stride);
                if (more > step) (step, stepAt) = (more, told[i].N);
                double below = Nearest(side, told[i].E, told[i].N).Rad - told[i].Surface;
                if (told[i].N > 0.0 && below > under) (under, underAt) = (below, told[i].N);
            }
            Assert.True(step <= 0.03, $"a step {step:F3} m more than the slope, {stepAt:F2} m up the side road, at spacing {spacing} m, stride {stride} m");
            Assert.True(under <= 0.02, $"{under:F3} m under the side road, {underAt:F2} m up it, at spacing {spacing} m, stride {stride} m");
        }
    }

    [Fact]
    public void AWheelTurningOffARaisedSideRoadOntoTheRoadItMeetsIsLevelAllTheWay()
    {
        foreach (double spacing in new[] { 2.0, 6.0 })
        foreach (double stride in new[] { 0.05, 0.3, 1.0 })
        {
            Circuit c = new Circuit { WidthM = 10.0 }.AddNode(0, 0, out int mid)
                .Extend(mid, 0, Deg(-100), out int west).Extend(mid, 0, Deg(100), out int east).Extend(mid, Deg(-100), 0, out int south)
                .SetHeight(mid, 8.0).SetHeight(west, 8.0).SetHeight(east, 8.0).SetHeight(south, 8.0);
            Laid laid = Lay(c, spacing);

            // Up the side road, round a quarter circle onto the through road, and along it.
            List<(double E, double N)> corner = [(0.0, -40.0)];
            for (int k = 0; k <= 30; k++)
            {
                double angle = k * Math.PI / 60.0;
                corner.Add((10.0 - (10.0 * Math.Cos(angle)), -10.0 + (10.0 * Math.Sin(angle))));
            }
            corner.Add((40.0, 0.0));

            List<(double E, double N, double Surface)> told = Roll(laid, Path(stride, [.. corner]), R + Lift + 8.0, out int lost);

            Assert.True(lost == 0, $"{lost} samples found no road at spacing {spacing} m, stride {stride} m");
            Assert.All(told, t => Assert.True(Math.Abs(t.Surface - (R + Lift + 8.0)) < 0.03,
                $"{t.Surface - (R + Lift + 8.0):F3} m off the deck's height at {t.E:F2} m east, {t.N:F2} m north"));
        }
    }

    // ---- d. bridges --------------------------------------------------------------------------------

    [Fact]
    public void AWheelLeavingTheSideOfARoadUnderADeckIsNotPutInTheDeck()
    {
        foreach (double spacing in new[] { 2.0, 6.0 })
        {
            // A road along the equator, and a deck 12 m up crossing it from south to north.
            Circuit c = new Circuit { WidthM = 10.0 }.AddNode(0, Deg(-100), out int west).Extend(west, 0, Deg(100), out _)
                .AddNode(Deg(-100), 0, out int south).Extend(south, Deg(100), 0, out int north)
                .SetHeight(south, 12.0).SetHeight(north, 12.0);
            Laid laid = Lay(c, spacing);
            Assert.Equal(2, laid.Strips.Count);

            double? last = null;
            for (double n = 0.0; n < 30.0; n += 0.1)
            {
                bool found = laid.Surface.TryHeightOver(At(1.0, n, R + Lift + Hub), last, out double over);
                Assert.True(!found || over > -1.0, $"{-over:F2} m inside the deck, {n:F1} m from the road's middle, at spacing {spacing} m");
                last = found ? over : null;
            }
        }
    }

    [Fact]
    public void AWheelInTheAirOverARoadIsOverADeckItComesInAbove()
    {
        Circuit c = new Circuit { WidthM = 10.0 }.AddNode(0, Deg(-100), out int west).Extend(west, 0, Deg(100), out _)
            .AddNode(Deg(-100), 0, out int south).Extend(south, Deg(100), 0, out int north)
            .SetHeight(south, 12.0).SetHeight(north, 12.0);
        Laid laid = Lay(c, 2.0);

        // Along the lower road 20 m up, which is 8 m over the deck where the two cross.
        double? last = null;
        for (double e = -30.25; e <= 30.0; e += 0.5)
        {
            Assert.True(laid.Surface.TryHeightOver(At(e, 0.0, R + Lift + 20.0), last, out double over));
            Assert.Equal(Math.Abs(e) < 5.0 ? 8.0 : 20.0, over, 2);
            last = over;
        }
    }

    [Fact]
    public void AWheelRoundABendOnADeckStaysOnTheDeckOverTheRoadBeneathIt()
    {
        foreach (double spacing in new[] { 2.0, 6.0 })
        {
            // The deck comes up from the south, turns over the road and goes back south.
            Circuit c = new Circuit { WidthM = 10.0 }.AddNode(0, Deg(-150), out int west).Extend(west, 0, Deg(150), out _)
                .AddNode(Deg(-70), Deg(-55), out int a).Extend(a, Deg(18), 0, out int b).Extend(b, Deg(-70), Deg(55), out int d)
                .SetHeight(a, 12.0).SetHeight(b, 12.0).SetHeight(d, 12.0);
            Laid laid = Lay(c, spacing);
            P[] deck = Centre(laid.Strips.Single(s => s.AboveGroundM[0] > 6.0));
            Assert.InRange(TightestRadius(deck), 20.0, 40.0);

            foreach (double offset in new[] { -4.8, -2.5, 0.0, 2.5, 4.8 })
            {
                // Not the first metre or the last: the deck ends square to its own heading, not to this line's.
                P[] track = Beside(deck, offset)[2..^2];
                List<(double E, double N)> path = Path(0.2, [.. track.Select(p => (p.E, p.N))]);
                List<(double E, double N, double Surface)> told = Roll(laid, path, R + Lift + 12.0, out int lost);

                Assert.True(lost == 0, $"{lost} of {path.Count} samples found no road, {offset} m off the deck's middle, at spacing {spacing} m");
                int fell = told.Count(t => Math.Abs(t.Surface - (R + Lift + 12.0)) > 0.05);
                Assert.True(fell == 0, $"{fell} of {told.Count} samples were not on the deck, {offset} m off its middle, at spacing {spacing} m");
            }
        }
    }

    // ---- e. shapes that are not roads --------------------------------------------------------------

    public static TheoryData<string, double[][], bool> Degenerate()
    {
        double[][] Circle(double radius, int points, bool closed)
        {
            List<double[]> ring = [];
            for (int i = 0; i < points; i++) ring.Add([radius * Math.Cos(i * 2.0 * Math.PI / points), radius * Math.Sin(i * 2.0 * Math.PI / points), 0.0]);
            if (closed) ring.Add(ring[0]);
            return [.. ring];
        }

        return new TheoryData<string, double[][], bool>
        {
            { "nothing", [], false },
            { "one point", [[0, 0, 0]], false },
            { "two points in one place", [[0, 0, 0], [0, 0, 0]], false },
            { "a point laid twice in a road", [[-10, 0, 0], [0, 0, 0], [0, 0, 0], [10, 0, 0]], false },
            { "a piece a millimetre long", [[-10, 0, 0], [0, 0, 0], [0.001, 0, 0], [10, 3, 0]], false },
            { "a piece straight up", [[-10, 0, 0], [0, 0, 0], [0, 0, 5], [10, 0, 5]], false },
            { "a road back along itself", [[-10, 0, 0], [10, 0, 0], [-10, 0, 0]], false },
            { "a turn tighter than the road is wide", Circle(2.0, 9, false), false },
            { "a ring tighter than the road is wide", Circle(2.0, 12, true), true },
            { "a ring", Circle(40.0, 60, true), true },
            { "a ring of two points", [[0, 0, 0], [10, 0, 0], [0, 0, 0]], true },
        };
    }

    [Theory]
    [MemberData(nameof(Degenerate))]
    public void AShapeThatIsNoRoadAnswersANumberOrNothing(string shape, double[][] points, bool closed)
    {
        double3[] line = [.. points.Select(p => At(p[0], p[1], R + p[2]))];
        RoadSurface surface = new([(line, 5.0, closed)]);
        RoadSurface low = new([(line, 5.0, closed, (double[]?)[.. line.Select(_ => 0.1)])]);

        for (double e = -50.0; e <= 50.0; e += 3.1)
        {
            for (double n = -50.0; n <= 50.0; n += 2.3)
            {
                foreach (double up in new[] { -3.0, 0.3, 4.0 })
                {
                    foreach (double? last in new double?[] { null, 0.3, -4.0 })
                    {
                        foreach (RoadSurface asked in new[] { surface, low })
                        {
                            bool found = asked.TryHeightOver(At(e, n, R + up), last, out double over);
                            Assert.True(!found || double.IsFinite(over), $"{shape}: {over} at {e:F1} m east, {n:F1} m north");
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void ARingIsARoadAllTheWayRoundWithNoGapWhereItCloses()
    {
        List<double3> ring = [];
        for (int i = 0; i <= 40; i++) ring.Add(At(40.0 * Math.Cos(i * Math.PI / 20.0), 40.0 * Math.Sin(i * Math.PI / 20.0), R));
        ring[^1] = ring[0];
        RoadSurface surface = new([([.. ring], 5.0, true)]);

        for (double angle = -0.5; angle < 0.5; angle += 0.003)
        {
            foreach (double radius in new[] { 35.1, 40.0, 44.9 })
            {
                Assert.True(surface.TryHeightOver(At(radius * Math.Cos(angle), radius * Math.Sin(angle), R + Hub), out double over),
                    $"no road {radius} m out at {angle:F3} rad");
                Assert.True(Math.Abs(over - Hub) < 0.02, $"{over:F3} m over the ring {radius} m out at {angle:F3} rad");
            }
        }
    }
}
