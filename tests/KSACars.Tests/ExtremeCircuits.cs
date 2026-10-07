namespace KSACars.Tests;

// The circuits tools/roads/extreme-circuits.py writes into the game's library, point for point: what is
// lapped in game is what is lapped here. A change to either has to be made in both. The script puts
// each near -24, -62 on Earth; here the same metres east and north are laid where the equator meets the
// prime meridian, which is where a TrackWorld's own metres are.
internal static class ExtremeCircuits
{
    public static readonly string[] Names = ["Hairpins", "Spiral", "Coaster", "Eight", "Speedway", "Chicane", "Kinks", "Grid"];

    private sealed class Course(double width = 10.0, bool closed = false)
    {
        private readonly List<(double East, double North, double Height, double Corner)> _points = [];
        private readonly List<(int A, int B)> _roads = [];

        public int Point(double east, double north, double height = 0.0, double corner = 1.0, bool join = true)
        {
            _points.Add((east, north, height, corner));
            if (join && _points.Count > 1) _roads.Add((_points.Count - 1, _points.Count));
            return _points.Count;
        }

        public void Road(int a, int b) => _roads.Add((a, b));

        public Circuit On(TrackWorld world, string name)
        {
            Circuit circuit = new() { Name = name, WidthM = width };
            foreach ((double east, double north, double height, double corner) in _points)
            {
                circuit = circuit.AddNode(world.Deg(north), world.Deg(east), out int id).SetHeight(id, height).SetCorner(id, corner);
            }
            foreach ((int a, int b) in _roads) circuit = circuit.Connect(a, b);
            return closed ? circuit.Connect(_points.Count, 1) : circuit;
        }
    }

    public static Circuit Of(string name, TrackWorld world) => (name switch
    {
        "Hairpins" => Hairpins(),
        "Spiral" => Spiral(),
        "Coaster" => Coaster(),
        "Eight" => Eight(),
        "Speedway" => Speedway(),
        "Chicane" => Chicane(),
        "Kinks" => Kinks(),
        "Grid" => Grid(),
        _ => throw new ArgumentException($"no circuit called {name}"),
    }).On(world, "X " + name);

    private static Course Hairpins()
    {
        Course c = new();
        for (int k = 0; k < 6; k++)
        {
            double y = 24.0 * k;
            c.Point(k % 2 == 0 ? 0.0 : 120.0, y);
            c.Point(k % 2 == 0 ? 120.0 : 0.0, y);
        }
        return c;
    }

    private static Course Spiral()
    {
        Course c = new();
        const int turns = 4;
        const double perTurn = 7.0, radius = 25.0;
        c.Point(-80.0, -radius);
        for (int i = 0; i <= turns * 8; i++)
        {
            double a = (-Math.PI / 2.0) + (i * Math.PI / 4.0);
            c.Point(radius * Math.Cos(a), radius * Math.Sin(a), perTurn * i / 8.0);
        }
        c.Point(120.0, -radius, turns * perTurn);
        c.Point(420.0, -radius, 0.0);
        c.Point(520.0, -radius, 0.0);
        return c;
    }

    private static Course Coaster()
    {
        Course c = new();
        (double, double, double)[] points =
        [
            (0, 0, 0), (80, 0, 0), (160, 0, 15), (240, 0, 0), (300, 10, 6), (360, 30, 0), (460, 40, 25),
            (560, 30, 0), (620, 0, 10), (700, -20, 2), (780, -20, 12), (880, 0, 0), (980, 0, 0),
        ];
        foreach ((double e, double n, double h) in points) c.Point(e, n, h);
        return c;
    }

    private static Course Eight()
    {
        Course c = new(closed: true);
        const double r = 60.0;
        for (int i = 0; i < 8; i++)
        {
            double a = Math.PI + (i * Math.PI / 4.0);
            c.Point(r + (r * Math.Cos(a)), r * Math.Sin(a), i == 0 ? 6.0 : Math.Max(0.0, 6.0 - (3.0 * Math.Min(i, 8 - i))));
        }
        for (int i = 1; i < 8; i++)
        {
            double a = -i * Math.PI / 4.0;
            c.Point(-r + (r * Math.Cos(a)), r * Math.Sin(a));
        }
        return c;
    }

    private static Course Speedway()
    {
        Course c = new(width: 14.0, closed: true);
        const double r = 180.0, half = 500.0;
        c.Point(-half, -r);
        c.Point(half, -r);
        for (int i = 1; i < 8; i++)
        {
            double a = (-Math.PI / 2.0) + (i * Math.PI / 8.0);
            c.Point(half + (r * Math.Cos(a)), r * Math.Sin(a));
        }
        c.Point(half, r);
        c.Point(-half, r);
        for (int i = 1; i < 8; i++)
        {
            double a = (Math.PI / 2.0) + (i * Math.PI / 8.0);
            c.Point(-half + (r * Math.Cos(a)), r * Math.Sin(a));
        }
        return c;
    }

    private static Course Chicane()
    {
        Course c = new(width: 6.0);
        c.Point(-60.0, 0.0);
        for (int k = 0; k < 11; k++) c.Point(40.0 * k, k is > 0 and < 10 ? 15.0 * (k % 2 == 1 ? 1.0 : -1.0) : 0.0);
        c.Point(460.0, 0.0);
        return c;
    }

    private static Course Kinks()
    {
        Course c = new();
        double x = 0.0, y = 0.0, heading = 0.0;
        c.Point(x, y, corner: 0.0);
        foreach (double turn in new[] { 0.0, 30.0, -60.0, 90.0, -120.0, 60.0, 0.0 })
        {
            heading += turn * Math.PI / 180.0;
            x += 90.0 * Math.Cos(heading);
            y += 90.0 * Math.Sin(heading);
            c.Point(x, y, corner: 0.0);
        }
        return c;
    }

    private static Course Grid()
    {
        Course c = new();
        int[,] ids = new int[3, 3];
        for (int j = 0; j < 3; j++)
        {
            for (int i = 0; i < 3; i++) ids[i, j] = c.Point(100.0 * i, 100.0 * j, i == 1 && j == 1 ? 5.0 : 0.0, join: false);
        }
        for (int j = 0; j < 3; j++)
        {
            for (int i = 0; i < 3; i++)
            {
                if (i < 2) c.Road(ids[i, j], ids[i + 1, j]);
                if (j < 2) c.Road(ids[i, j], ids[i, j + 1]);
            }
        }
        return c;
    }
}
