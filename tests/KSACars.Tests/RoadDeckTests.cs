using Brutal.Numerics;
using Xunit;
using Xunit.Abstractions;

namespace KSACars.Tests;

/// <summary>
/// The solid the physics is given for a road, against the line it was built from and against the
/// surface a wheel is told over the same line.
/// </summary>
public class RoadDeckTests(ITestOutputHelper output)
{
    private const double R = 1_737_400.0, Depth = RoadSlabs.ThicknessM;

    private static double3 At(double e, double n, double rad)
    {
        double lat = n / R, lon = e / R;
        return new double3(Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat)) * rad;
    }

    private sealed record Shape(string Name, double3[] Line, double HalfWidth, bool Closed);

    private static Shape Straight(double lengthM, double climb = 0.0)
    {
        List<double3> line = [];
        for (double e = 0.0; e <= lengthM; e += 2.0) line.Add(At(e, 0.0, R + (climb * e)));
        return new Shape($"a {lengthM} m straight climbing {climb}", [.. line], 4.0, false);
    }

    // An arc turning left from due east, with a point every stepM, climbing by so much a metre along.
    private static Shape Arc(double radiusM, double turnDeg, double halfWidth, double climb, double stepM = 2.0)
    {
        List<double3> line = [];
        int steps = (int)Math.Round(radiusM * turnDeg * Math.PI / 180.0 / stepM);
        for (int i = 0; i <= steps; i++)
        {
            double a = i * stepM / radiusM;
            line.Add(At(radiusM * Math.Sin(a), radiusM * (1.0 - Math.Cos(a)), R + (climb * i * stepM)));
        }
        return new Shape($"a {radiusM} m bend of {turnDeg} deg, {2.0 * halfWidth} m wide, climbing {climb}", [.. line], halfWidth, false);
    }

    private static Shape Ring(double radiusM, int points, double halfWidth, double swellM = 0.0)
    {
        List<double3> line = [];
        for (int i = 0; i < points; i++)
        {
            double a = i * 2.0 * Math.PI / points;
            line.Add(At(radiusM * Math.Cos(a), radiusM * Math.Sin(a), R + (swellM * Math.Sin(3.0 * a))));
        }
        line.Add(line[0]);
        return new Shape($"a ring of {radiusM} m", [.. line], halfWidth, true);
    }

    // A circuit's roads as the game lays them, over ground that rolls.
    private static IEnumerable<Shape> Laid()
    {
        static double Deg(double metres) => metres / R * 180.0 / Math.PI;
        static double3 DirOf(double latDeg, double lonDeg) => At(lonDeg * Math.PI / 180.0 * R, latDeg * Math.PI / 180.0 * R, 1.0);

        Circuit circuit = new Circuit { WidthM = 8.0 }.AddNode(0, Deg(-300.0), out int a).Extend(a, 0, 0, out int b)
            .Extend(b, Deg(250.0), Deg(120.0), out int c).Extend(c, Deg(260.0), Deg(-200.0), out _)
            .SetHeight(b, 6.0).SetHeight(c, 2.0);
        foreach (RoadLaying.Strip strip in RoadLaying.Lay(circuit, DirOf, R, dir => 4.0 * Math.Sin(dir.Y * R / 70.0), 0.07, 2.0))
        {
            yield return new Shape($"a laid road of {strip.LengthM:F0} m", strip.Line, strip.HalfWidth, strip.Closed);
        }
    }

    private static IEnumerable<Shape> Shapes() =>
    [
        Straight(700.0), Straight(90.0, 0.08), Arc(180.0, 120.0, 4.0, 0.0), Arc(30.0, 200.0, 4.0, 0.05), Arc(5.0, 170.0, 3.0, 0.125),
        Ring(40.0, 40, 5.0), Ring(400.0, 1200, 4.0, 5.0), .. Laid(),
    ];

    private static RoadDeck.Deck Build(Shape shape) => RoadDeck.Build(shape.Line, shape.HalfWidth, shape.Closed, Depth);

    private static double3 Front(RoadDeck.Deck deck, RoadDeck.Chunk chunk, int t)
    {
        double3 a = deck.Corners[chunk.Triangles[3 * t]], b = deck.Corners[chunk.Triangles[(3 * t) + 1]], c = deck.Corners[chunk.Triangles[(3 * t) + 2]];
        return Vec.Cross(c - a, b - a);
    }

    private static double3 Middle(RoadDeck.Deck deck, int section)
    {
        double3 sum = default;
        for (int k = 0; k < 4; k++) sum += deck.Corners[(4 * section) + k] * 0.25;
        return sum;
    }

    // Which way is out of the deck for a triangle, from which corners of which sections it joins
    // alone: nothing here is the order the deck listed them in.
    private static double3 Outward(RoadDeck.Deck deck, RoadDeck.Chunk chunk, int t, Dictionary<int, int> neighbour)
    {
        int[] at = [chunk.Triangles[3 * t], chunk.Triangles[(3 * t) + 1], chunk.Triangles[(3 * t) + 2]];
        int section = at[0] / 4;
        if (at.All(i => i / 4 == section)) return Middle(deck, section) - Middle(deck, neighbour[section]);

        double3 up = Vec.Unit(deck.Corners[at[0]]), left = deck.Corners[4 * section] - deck.Corners[(4 * section) + 1];
        if (at.All(i => i % 4 < 2)) return up;
        if (at.All(i => i % 4 >= 2)) return up * -1.0;
        if (at.All(i => i % 2 == 0)) return left;
        Assert.True(at.All(i => i % 2 == 1), "a triangle that is no face of the deck");
        return left * -1.0;
    }

    [Fact]
    public void EveryTriangleIsSolidFromOutsideTheDeck()
    {
        int seen = 0;
        foreach (Shape shape in Shapes())
        {
            RoadDeck.Deck deck = Build(shape);
            foreach (RoadDeck.Chunk chunk in deck.Chunks)
            {
                // A cap is on one section, and out of it is away from the section the chunk joins it to.
                Dictionary<int, int> neighbour = [];
                for (int t = 0; t < chunk.Triangles.Length / 3; t++)
                {
                    int[] sections = [.. chunk.Triangles.Skip(3 * t).Take(3).Select(i => i / 4).Distinct()];
                    if (sections.Length == 2) (neighbour[sections[0]], neighbour[sections[1]]) = (sections[1], sections[0]);
                }

                double volume = 0.0;
                for (int t = 0; t < chunk.Triangles.Length / 3; t++)
                {
                    double3 front = Front(deck, chunk, t), outward = Outward(deck, chunk, t, neighbour);
                    double cos = Vec.Dot(Vec.Unit(front), Vec.Unit(outward));
                    Assert.True(cos > 0.5, $"triangle {t} of {shape.Name} faces {cos:F2} of the way out");

                    double3 a = deck.Corners[chunk.Triangles[3 * t]] - chunk.Origin;
                    volume += Vec.Dot(a, front) / 6.0;
                    seen++;
                }
                Assert.True(volume > 0.0, $"a chunk of {shape.Name} is inside out: {volume:F1} m3");
            }
        }
        Assert.True(seen > 20_000, $"only {seen} triangles");
    }

    [Fact]
    public void EveryChunkIsClosed()
    {
        foreach (Shape shape in Shapes())
        {
            RoadDeck.Deck deck = Build(shape);
            Assert.NotEmpty(deck.Chunks);
            foreach (RoadDeck.Chunk chunk in deck.Chunks)
            {
                Dictionary<(int, int), int> edges = [];
                for (int t = 0; t < chunk.Triangles.Length; t += 3)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        (int, int) edge = (chunk.Triangles[t + k], chunk.Triangles[t + ((k + 1) % 3)]);
                        edges[edge] = edges.GetValueOrDefault(edge) + 1;
                    }
                }

                // Each edge once each way: two triangles on it, and the two wound alike.
                foreach (((int from, int to), int times) in edges)
                {
                    Assert.True(times == 1 && edges.GetValueOrDefault((to, from)) == 1, $"an edge of {shape.Name} is not shared by two triangles wound alike");
                }
            }
        }
    }

    [Fact]
    public void ChunksCoverEveryStretchOnceAndMeetOnTheSameCorners()
    {
        foreach (Shape shape in Shapes())
        {
            RoadDeck.Deck deck = Build(shape);
            int stretches = shape.Closed ? deck.Sections : deck.Sections - 1;
            int[] covered = new int[stretches];
            List<HashSet<int>> own = [];
            foreach (RoadDeck.Chunk chunk in deck.Chunks)
            {
                HashSet<int> sections = [], tops = [];
                for (int t = 0; t < chunk.Triangles.Length; t += 3)
                {
                    int[] at = [chunk.Triangles[t], chunk.Triangles[t + 1], chunk.Triangles[t + 2]];
                    if (at.Any(i => i >= 4 * deck.Sections)) continue;
                    foreach (int i in at) sections.Add(i / 4);

                    // The top's triangle with two corners on the earlier section stands for the stretch.
                    int first = at.Min() / 4, last = at.Max() / 4;
                    bool wraps = first == 0 && last == deck.Sections - 1 && deck.Sections > 2 && shape.Closed;
                    int start = wraps ? last : first;
                    if (first != last && at.All(i => i % 4 < 2) && at.Count(i => i / 4 == start) == 2) covered[start]++;
                }
                own.Add(sections);
                Assert.True(chunk.RadiusM < RoadDeck.ChunkM, $"a chunk of {shape.Name} is {2.0 * chunk.RadiusM:F0} m across");
                foreach (int corner in chunk.Triangles) Assert.True(Vec.Len(deck.Corners[corner] - chunk.Origin) <= chunk.RadiusM + 1e-6);
            }

            Assert.True(covered.All(c => c == 1), $"{shape.Name}: a stretch in {covered.Min()} or {covered.Max()} chunks");
            for (int k = 0; k + 1 < own.Count; k++)
            {
                Assert.True(own[k].Intersect(own[k + 1]).Count() == 1, $"{shape.Name}: chunks {k} and {k + 1} do not meet on one section");
            }
            if (shape.Closed && own.Count > 2) Assert.Single(own[^1].Intersect(own[0]));
        }
    }

    [Fact]
    public void NoTriangleIsALineInSinglePrecision()
    {
        List<double3> line = [];
        for (double e = 0.0; e <= 300.0; e += 2.0)
        {
            line.Add(At(e, 0.0, R));
            if (e % 20.0 == 0.0) line.AddRange([At(e, 0.0, R), At(e + 1e-4, 0.0, R + 0.3), At(e + 0.004, 0.0, R)]);
        }
        line.Add(line[^1]);

        double least = double.PositiveInfinity;
        foreach (Shape shape in Shapes().Append(new Shape("a line that repeats its points", [.. line], 4.0, false)))
        {
            RoadDeck.Deck deck = Build(shape);
            Assert.All(deck.Corners, c => Assert.True(Vec.IsFinite(c)));
            foreach (RoadDeck.Chunk chunk in deck.Chunks)
            {
                for (int t = 0; t < chunk.Triangles.Length; t += 3)
                {
                    float3 a = Local(deck, chunk, t), b = Local(deck, chunk, t + 1), c = Local(deck, chunk, t + 2);
                    double area = 0.5 * float3.Cross(b - a, c - a).Length();
                    least = Math.Min(least, area);
                    Assert.True(area > 1e-3, $"a triangle of {area:E1} m2 on {shape.Name}");
                }
            }
        }
        output.WriteLine($"the smallest triangle is {least:F4} m2");
    }

    private static float3 Local(RoadDeck.Deck deck, RoadDeck.Chunk chunk, int corner)
    {
        double3 at = deck.Corners[chunk.Triangles[corner]] - chunk.Origin;
        return new float3((float)at.X, (float)at.Y, (float)at.Z);
    }

    // How far out from the body's centre the deck's highest face is on the line through a point, in
    // the single precision the physics has it in.
    private static double? TopOver(RoadDeck.Deck deck, double3 at)
    {
        double3 up = Vec.Unit(at);
        double? best = null;
        foreach (RoadDeck.Chunk chunk in deck.Chunks)
        {
            if (Vec.Len(at - chunk.Origin) > chunk.RadiusM + 2.0) continue;
            for (int t = 0; t < chunk.Triangles.Length; t += 3)
            {
                float3 fa = Local(deck, chunk, t), fb = Local(deck, chunk, t + 1), fc = Local(deck, chunk, t + 2);
                double3 a = chunk.Origin + new double3(fa.X, fa.Y, fa.Z), b = chunk.Origin + new double3(fb.X, fb.Y, fb.Z), c = chunk.Origin + new double3(fc.X, fc.Y, fc.Z);
                double3 normal = Vec.Cross(b - a, c - a);
                double facing = Vec.Dot(normal, up);
                if (Math.Abs(facing) < 1e-9) continue;

                double3 hit = up * (Vec.Dot(normal, a) / facing);
                if (Vec.Dot(Vec.Cross(b - a, hit - a), normal) < 0.0 || Vec.Dot(Vec.Cross(c - b, hit - b), normal) < 0.0
                    || Vec.Dot(Vec.Cross(a - c, hit - c), normal) < 0.0) continue;
                double rad = Vec.Len(hit);
                if (best is null || rad > best) best = rad;
            }
        }
        return best;
    }

    // The most the deck's top is above and below the surface a wheel is told, over places up to
    // nine tenths of the half width either side of the line.
    private static (double Above, double Below, int Places) AgainstTheWheels(Shape shape)
    {
        RoadDeck.Deck deck = Build(shape);
        RoadSurface surface = new([(shape.Line, shape.HalfWidth, shape.Closed)]);
        (double above, double below, int places) = (0.0, 0.0, 0);
        for (int i = 0; i + 1 < shape.Line.Length; i++)
        {
            double3 a = shape.Line[i], b = shape.Line[i + 1];
            if (Vec.Len(b - a) < 0.5) continue;
            double3 across = Vec.Unit(Vec.Cross(a + b, b - a));
            foreach (double along in new[] { 0.03, 0.31, 0.5, 0.77, 0.97 })
            {
                foreach (double side in new[] { -0.9, -0.45, 0.0, 0.3, 0.9 })
                {
                    double3 at = a + ((b - a) * along) + (across * (side * shape.HalfWidth));
                    Assert.True(surface.TryHeightOver(at + (Vec.Unit(at) * 0.2), out double over), $"no road under a place on {shape.Name}");
                    double? top = TopOver(deck, at);
                    Assert.True(top is not null, $"no deck under a place on {shape.Name}, stretch {i}, {side} across");

                    double off = top.Value - (Vec.Len(at) + 0.2 - over);
                    above = Math.Max(above, off);
                    below = Math.Min(below, off);
                    places++;
                }
            }
        }
        return (above, below, places);
    }

    [Fact]
    public void TheTopIsTheSurfaceAWheelIsToldOnStraightsAndLevelBends()
    {
        foreach (Shape shape in new[] { Straight(700.0), Straight(90.0, 0.08), Arc(180.0, 120.0, 4.0, 0.0), Arc(30.0, 200.0, 4.0, 0.0), Ring(40.0, 40, 5.0), Ring(400.0, 1200, 4.0) })
        {
            (double above, double below, int places) = AgainstTheWheels(shape);
            output.WriteLine($"{shape.Name}: {above * 1000.0:F3} mm above and {-below * 1000.0:F3} mm below over {places} places");
            Assert.True(places > 400 && above < 0.001 && below > -0.001, $"{shape.Name}: the deck's top is {above * 1000.0:F2} mm above and {-below * 1000.0:F2} mm below the wheels' surface");
        }
    }

    [Theory]
    [InlineData(180.0, 60.0, 4.0, 0.05, 0.0015)]
    [InlineData(30.0, 200.0, 4.0, 0.05, 0.008)]
    [InlineData(5.0, 170.0, 3.0, 0.125, 0.17)]
    public void RoundAClimbingBendTheTopPartsFromItByTheGradientAndTheTurn(double radiusM, double turnDeg, double halfWidth, double climb, double mostM)
    {
        Shape shape = Arc(radiusM, turnDeg, halfWidth, climb);
        (double above, double below, _) = AgainstTheWheels(shape);
        output.WriteLine($"{shape.Name}: {above * 1000.0:F1} mm above and {-below * 1000.0:F1} mm below");

        double furthest = Math.Max(above, -below), reckoned = climb * halfWidth * Math.Tan(1.0 / radiusM);
        Assert.True(furthest < mostM, $"{furthest * 1000.0:F1} mm off on {shape.Name}");
        Assert.True(furthest < 1.2 * reckoned, $"{furthest * 1000.0:F1} mm off on {shape.Name}, where the gradient and the turn give {reckoned * 1000.0:F1}");
    }

    [Fact]
    public void ATongueIsInsideTheRoadItRunsOnInto()
    {
        Shape shape = Ring(400.0, 1200, 4.0, 5.0);
        RoadDeck.Deck deck = Build(shape);
        RoadSurface surface = new([(shape.Line, shape.HalfWidth, shape.Closed)]);
        Assert.True(deck.Corners.Length > 4 * deck.Sections);

        double deepest = 0.0;
        for (int i = 4 * deck.Sections; i < deck.Corners.Length; i++)
        {
            Assert.True(surface.TryLocate(deck.Corners[i], null, out double over, out double outM), "a tongue's corner with no road over it");
            Assert.True(outM <= 0.0 && over < -0.004 && over > -Depth + 0.004, $"a tongue's corner {-over:F3} m under the road's top, {outM:F2} m out past its edge");
            if (i % 4 < 2) deepest = Math.Min(deepest, over);
        }
        Assert.InRange(-deepest, 0.09, 0.11);
    }

    [Fact]
    public void ARingClosesWithNoCapAndAnOpenRoadHasTwo()
    {
        RoadDeck.Deck ring = Build(Ring(40.0, 40, 5.0));
        Assert.Single(ring.Chunks);
        Assert.Equal(40, ring.Sections);
        Assert.Equal(40 * 8, ring.TriangleCount);
        Assert.Equal(4 * 40, ring.Corners.Length);

        RoadDeck.Deck open = Build(Straight(90.0));
        Assert.Single(open.Chunks);
        Assert.Equal((45 * 8) + 4, open.TriangleCount);

        // The ring's top is there over the place it closes at.
        for (double angle = -0.2; angle < 0.2; angle += 0.01)
        {
            double? top = TopOver(ring, At(40.0 * Math.Cos(angle), 40.0 * Math.Sin(angle), R));
            Assert.True(top is not null && Math.Abs(top.Value - R) < 0.001, $"no top at {angle:F2} rad round the ring");
        }
    }

    [Fact]
    public void AKinkIsWidenedNoFurtherThanTheCapAndStaysClosed()
    {
        double3[] line = [At(-40.0, 0.0, R), At(-20.0, 0.0, R), At(0.0, 0.0, R), At(-17.0, 10.0, R), At(-34.0, 20.0, R)];
        RoadDeck.Deck deck = RoadDeck.Build(line, 3.0, false, Depth);
        Assert.Equal(5, deck.Sections);
        Assert.Equal(2.0 * 3.0 * RoadDeck.MitreCap, Vec.Len(deck.Corners[8] - deck.Corners[9]), 6);
        Assert.Equal(6.0, Vec.Len(deck.Corners[0] - deck.Corners[1]), 6);
        Assert.Equal(Depth, Vec.Len(deck.Corners[8] - deck.Corners[10]), 6);
    }

    [Fact]
    public void AFiveKilometreCircuitIsAFewDozenMeshes()
    {
        RoadDeck.Deck deck = Build(Ring(5000.0 / (2.0 * Math.PI), 2500, 4.0));
        output.WriteLine($"5 km: {deck.Sections} sections, {deck.Chunks.Length} chunks, {deck.TriangleCount} triangles, {deck.Chunks.Max(c => c.Triangles.Length / 3)} in the biggest chunk");
        Assert.Equal(2500, deck.Sections);
        Assert.Equal(25, deck.Chunks.Length);
        Assert.Equal(8 * 2500, deck.TriangleCount - deck.Chunks.Sum(c => c.Triangles.Count(i => i >= 4 * deck.Sections) > 0 ? TongueTriangles(deck, c) : 0));
        Assert.InRange(deck.TriangleCount, 24_000, 25_000);
    }

    private static int TongueTriangles(RoadDeck.Deck deck, RoadDeck.Chunk chunk)
    {
        int count = 0;
        for (int t = 0; t < chunk.Triangles.Length; t += 3)
        {
            if (chunk.Triangles.Skip(t).Take(3).Any(i => i >= 4 * deck.Sections)) count++;
        }
        return count;
    }
}
