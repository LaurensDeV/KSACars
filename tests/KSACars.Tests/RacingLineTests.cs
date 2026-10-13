using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class RacingLineTests
{
    private static AutopilotTests.Track Club()
    {
        DirectoryInfo? at = new(AppContext.BaseDirectory);
        while (at is not null && !Directory.Exists(Path.Combine(at.FullName, "src", "KSACars", "Circuits"))) at = at.Parent;
        string file = File.ReadAllText(Path.Combine(at!.FullName, "src", "KSACars", "Circuits", "Club Motorsports.json"));
        Circuit circuit = Circuit.FromJson(file, out string bad, out _) ?? throw new InvalidOperationException(bad);
        TrackWorld world = TrackWorld.Earth();
        return new AutopilotTests.Track(world, circuit, world.Surface(circuit));
    }

    private static Route Raced(AutopilotTests.Track track, TrackCar car, IReadOnlyList<double>? nudgesM = null) =>
        Route.Of(track.Circuit, TrackWorld.DirOf, track.World.RadiusM, track.World.HeightAt, TrackWorld.LiftM, TrackWorld.SpacingM, null, 0.0,
                 out string why, Autopilot.TurnRadius(car.Profile), Autopilot.RaceInside(car.Profile), nudgesM) ?? throw new InvalidOperationException(why);

    // How far past the room it is given a route is, at its worst.
    private static double Past(Route route, double insideM)
    {
        double past = double.NegativeInfinity;
        foreach (Route.Sample s in route.Samples) past = Math.Max(past, Math.Abs(s.CentreM) - Math.Max(s.HalfWidth - insideM, 0.0));
        return past;
    }

    [Theory]
    [InlineData("Manx")]
    [InlineData("Eldorado")]
    [InlineData("F2004")]
    public void RoundACourseWhoseAsphaltWidensToOneSideTheLineHasNoKinkAndUsesTheRoad(string name)
    {
        TrackCar car = TrackCar.Of(name);
        Route route = Raced(Club(), car);
        int n = route.Count, atEdge = 0;
        double inside = Autopilot.RaceInside(car.Profile), tightest = 0.0;
        double Bend(int i) => Math.Abs(route.Samples[((i % n) + n) % n].Curvature);
        for (int i = 0; i < n; i++)
        {
            Route.Sample s = route.Samples[i];
            tightest = Math.Max(tightest, Bend(i));
            if (Math.Abs(Math.Abs(s.CentreM) - (s.HalfWidth - inside)) < 0.3) atEdge++;

            // a kink of the line's own: a bend tighter than 50 m that is half as tight again 10 m either side of it
            bool peak = Bend(i) > 0.02;
            for (int k = -5; k <= 5 && peak; k++) peak = Bend(i + k) <= Bend(i);
            Assert.False(peak && Bend(i) > 1.4 * Math.Max(Bend(i - 10), Bend(i + 10)),
                         $"a kink of {1.0 / Bend(i):F1} m at {s.S:F0} m between {1.0 / Bend(i - 10):F1} m and {1.0 / Bend(i + 10):F1} m");
        }
        // the course's own tightest is the hairpin at 1.2 km, of 27 m with the road's width used
        Assert.True(tightest < 1.0 / 25.0, $"its tightest bend is {1.0 / tightest:F1} m");
        Assert.True(atEdge > 0.1 * n, $"{100.0 * atEdge / n:F0}% of it is at an edge");
        Assert.True(Past(route, inside) < 1e-6, $"{Past(route, inside):F3} m past the room it has");

        // and where the lap is said to start it is as it is anywhere
        Assert.InRange(route.Samples[0].CentreM - route.Samples[n - 1].CentreM, -0.3, 0.3);
    }

    [Theory]
    [InlineData(0.4)]
    [InlineData(2.5)]
    public void ALineNudgedToEitherSideIsStillOnTheRoad(double byM)
    {
        AutopilotTests.Track club = Club();
        TrackCar car = TrackCar.F2004;
        double[] nudges = new double[Rival.Places];
        for (int k = 0; k < nudges.Length; k++) nudges[k] = k % 2 == 0 ? byM : -byM;
        Route plain = Raced(club, car), nudged = Raced(club, car, nudges);
        Assert.True(Past(nudged, Autopilot.RaceInside(car.Profile)) < 1e-6);

        double moved = 0.0;
        for (int i = 0; i < plain.Count; i++) moved = Math.Max(moved, Math.Abs(nudged.Samples[i].CentreM - plain.Samples[i].CentreM));
        Assert.InRange(moved, 0.9 * byM, byM + 1e-6);

        foreach (string circuit in ExtremeCircuits.Names)
        {
            AutopilotTests.Track track = ExtremeLapTests.Laid(circuit);
            Assert.True(Raced(track, car, nudges).Count > 0);
        }
    }

    // A stadium of two 80 m straights and two half circles of 30 m, a point a metre, from the body's centre.
    private static double3[] Stadium(TrackWorld world, int from)
    {
        const double straight = 80.0, radius = 30.0;
        double lap = (2.0 * straight) + (2.0 * Math.PI * radius);
        int n = (int)Math.Round(lap);
        double3[] points = new double3[n];
        for (int i = 0; i < n; i++)
        {
            double s = lap * ((i + from) % n) / n, east, north;
            if (s < straight) (east, north) = (s, -radius);
            else if (s < straight + (Math.PI * radius))
            {
                double a = (s - straight) / radius;
                (east, north) = (straight + (radius * Math.Sin(a)), -radius * Math.Cos(a));
            }
            else if (s < (2.0 * straight) + (Math.PI * radius)) (east, north) = (straight - (s - straight - (Math.PI * radius)), radius);
            else
            {
                double a = (s - (2.0 * straight) - (Math.PI * radius)) / radius;
                (east, north) = (-radius * Math.Sin(a), radius * Math.Cos(a));
            }
            points[i] = world.Dir(east, north) * world.RadiusM;
        }
        return points;
    }

    [Fact]
    public void RoundALapTheLineIsTheSameWhereverTheLapIsSaidToStart()
    {
        TrackWorld world = TrackWorld.Earth();
        double3[] lap = Stadium(world, 0);
        double[] half = new double[lap.Length];
        Array.Fill(half, 6.0);
        double[] line = Route.RacingLine(lap, half, true, 1.5);
        foreach (int from in new[] { 1, 37, lap.Length / 2, lap.Length - 2 })
        {
            double[] turned = Route.RacingLine(Stadium(world, from), half, true, 1.5);
            for (int i = 0; i < lap.Length; i++) Assert.InRange(turned[i] - line[(i + from) % lap.Length], -0.05, 0.05);
        }
    }

    [Fact]
    public void ThroughABendTheLineIsOutAtTheEdgeAcrossTheInsideAndOutAgain()
    {
        TrackWorld world = TrackWorld.Earth();
        double3[] lap = Stadium(world, 0);
        double[] half = new double[lap.Length];
        Array.Fill(half, 6.0);
        double[] left = Route.RacingLine(lap, half, true, 1.5);

        // the bends are to the left: inside in the middle of each, and outside where the straights between them are half gone
        int n = lap.Length, apex = (int)Math.Round(80.0 + (0.5 * Math.PI * 30.0));
        Assert.InRange(left[apex], 4.4, 4.5 + 1e-9);
        Assert.InRange(left[40], -4.5 - 1e-9, -4.0);
        Assert.All(left, x => Assert.InRange(x, -4.5 - 1e-9, 4.5 + 1e-9));

        // at its tightest it is along the inside edge, of 25.5 m, and for all of a lap it turns less than the
        // 0.182 of the widest arcs that fit the bends: it has the straights to turn in too
        double tightest = 0.0, bent = 0.0;
        for (int i = 0; i < n; i++)
        {
            double3 Point(int k)
            {
                int j = ((k % n) + n) % n;
                double3 up = Vec.Unit(lap[j]);
                return lap[j] + (Vec.Cross(up, Vec.Unit(lap[(j + 1) % n] - lap[(j + n - 1) % n])) * left[j]);
            }
            double3 a = Point(i - 3), b = Point(i), c = Point(i + 3);
            double over = 0.5 * (Vec.Len(b - a) + Vec.Len(c - b)), bend = Vec.AngleBetween(b - a, c - b) / over;
            tightest = Math.Max(tightest, bend);
            bent += bend * bend * over / 3.0;
        }
        Assert.InRange(1.0 / tightest, 25.0, 30.0);
        Assert.True(bent < 0.175, $"its bend squared, over the lap, is {bent:F4}");
    }
}
