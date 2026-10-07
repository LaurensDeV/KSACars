using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

// A car as the engine has it, from KSACarsGameData.xml: its mass, where its centre of mass is, its
// inertia about that, and how far above the ground its lowest collider ends and its highest one tops
// out with the car at rest.
internal sealed record TrackCar(string Name, BuggyProfile Profile, double3 Com, double MassKg, double3 Inertia, double FloorM, double RoofM)
{
    public static readonly TrackCar Manx = new(
        "Manx", BuggyProfile.Manx, BuggyDriveTests.Rig.Com, 650.0, new double3(1, 1, 1) * (0.4 * 650.0 * 1.1 * 1.1), 0.18, 1.10);

    public static readonly TrackCar Eldorado = new(
        "Eldorado", BuggyProfile.Eldorado, BuggyDriveTests.Rig.EldoradoCom, 2336.0, Cuboid(2336.0, 1.0, 5.6, 2.0), 0.25, 1.00);

    public static readonly TrackCar F2004 = new(
        "F2004", BuggyProfile.F2004, BuggyDriveTests.Rig.F2004Com, 605.0, Cuboid(605.0, 0.5, 5.6, 2.0), 0.14, 0.70);

    public static readonly TrackCar[] All = [Manx, Eldorado, F2004];

    public static TrackCar Of(string name) => All.First(c => c.Name == name);

    private static double3 Cuboid(double mass, double x, double y, double z) =>
        new double3((y * y) + (z * z), (x * x) + (z * z), (x * x) + (y * y)) * (mass / 12.0);
}

// A sphere with ground of the test's own on it, in the sphere's own frame. Everything is placed by
// metres east and north of where the equator meets the prime meridian, where up is +X, east +Y and
// north +Z: a car facing east there has the part frame's axes for the world's.
internal sealed class TrackWorld(double radiusM, double gravity, double air, Func<double, double, double> height) : ITerrainHeights
{
    public double RadiusM { get; } = radiusM;
    public double Gravity { get; } = gravity;
    public double Air { get; } = air;

    public static TrackWorld Earth(Func<double, double, double>? height = null) => new(6_371_000.0, 9.81, 1.225, height ?? Flat);

    public static TrackWorld Luna(Func<double, double, double>? height = null) => new(1_737_400.0, 1.62, 0.0, height ?? Flat);

    public static readonly Func<double, double, double> Flat = (_, _) => 0.0;

    // Rising to the east, so a car heading east climbs it.
    public static Func<double, double, double> Slope(double grade) => (e, _) => grade * e;

    // Rising to the north, across a car heading east.
    public static Func<double, double, double> SideSlope(double grade) => (_, n) => grade * n;

    public static Func<double, double, double> Sinusoid(double amplitudeM, double wavelengthM) =>
        (e, _) => amplitudeM * Math.Sin(2.0 * Math.PI * e / wavelengthM);

    public static Func<double, double, double> Step(double heightM, double atEastM) => (e, _) => e >= atEastM ? heightM : 0.0;

    public static double3 DirOf(double latDeg, double lonDeg)
    {
        double lat = latDeg * Math.PI / 180.0, lon = lonDeg * Math.PI / 180.0;
        return new double3(Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));
    }

    // Degrees of latitude, or of longitude, that are so many metres.
    public double Deg(double metres) => metres / RadiusM * 180.0 / Math.PI;

    public double3 Dir(double eastM, double northM) => DirOf(Deg(northM), Deg(eastM));

    public (double East, double North) Flatten(double3 at)
    {
        double3 d = Vec.Unit(at);
        return (RadiusM * Math.Atan2(d.Y, d.X), RadiusM * Math.Asin(Math.Clamp(d.Z, -1.0, 1.0)));
    }

    public double HeightAt(double3 dir)
    {
        (double east, double north) = Flatten(dir);
        return height(east, north);
    }

    public bool TryHeight(double3 dirFromCentre, out double metres)
    {
        metres = HeightAt(dirFromCentre);
        return double.IsFinite(metres);
    }

    // As the game lays them: RoadEditor's lift, and its fine spacing.
    public const double LiftM = 0.07, SpacingM = 2.0;

    public List<RoadLaying.Strip> Lay(Circuit circuit) => RoadLaying.Lay(circuit, DirOf, RadiusM, HeightAt, LiftM, SpacingM);

    public RoadSurface Surface(Circuit circuit) => RoadLaying.Surface(Lay(circuit));
}

// What one step saw and did: all of it as the step began, but for the hull, which is met as it ends.
// HullMs is the speed into the surface the hull was stopped from, or nothing with the hull clear.
internal readonly record struct TrackSample(
    double Time, double Dt, double East, double North, double Speed, double TerrainUnderM, double Energy, double LossBudget,
    int Grounded, int OnRoad, double ClearanceM, double DeepestM,
    double PitchDeg, double RollDeg, double UpDot, bool HullDown, double HullMs)
{
    public bool Airborne => Grounded == 0;
}

// A free rigid body on a TrackWorld, stepped as Buggies steps a car and integrated the way the flat
// Rig is: what is under the wheels, the drive's impulse, then gravity and the motion over the step.
//
// The hull is the least that makes a hard landing mean anything: a point under each hub, as low as
// the car's lowest collider, that is not let into the ground or a road and keeps none of its speed
// into it. That is a stop and nothing else. It does not rub, so a car down on
// its hull slides on where the game's would scrape to a halt; it is four points, so a crest under the
// belly, a nose into a riser and a roof on the ground meet nothing; and a road is its top face alone,
// with no edge to run into. A run with HullDown in it says the hull came down and how hard, and what
// the car did afterwards is the rig's and not the game's.
internal sealed class TrackRig
{
    public const double LeastStep = 0.004, MostStep = 0.1;

    private readonly TrackCar _car;
    private readonly TrackWorld _world;
    private readonly RoadSurface? _road;
    private readonly double3[] _hubs;
    private readonly double?[] _roadOver;
    private readonly WheelContact[] _contacts;
    private readonly double[] _hubHeights;
    private readonly double3[] _hull;
    private readonly double?[] _hullOver;
    private readonly WheelContact[] _hullContacts;
    private readonly double[] _hullHeights;
    private int _tick;

    public TrackRig(TrackCar car, TrackWorld world, RoadSurface? road = null)
    {
        _car = car;
        _world = world;
        _road = road;
        Drive = new BuggyDrive(car.Profile);
        _hubs = [.. car.Profile.Corners.Select(c => c.Hub - car.Com)];
        _roadOver = new double?[_hubs.Length];
        _contacts = new WheelContact[_hubs.Length];
        _hubHeights = new double[_hubs.Length];
        _hull = [.. _hubs.Select(h => new double3(car.FloorM - car.Com.X, h.Y, h.Z))];
        _hullOver = new double?[_hubs.Length];
        _hullContacts = new WheelContact[_hubs.Length];
        _hullHeights = new double[_hubs.Length];
        Place(0.0, 0.0, 0.0);
    }

    public BuggyDrive Drive { get; }
    public double3 Position, Velocity, Spin;
    public doubleQuat Attitude = doubleQuat.Identity;
    public double Time { get; private set; }

    // The step: one length, or a sequence gone round, or drawn anew each step between the least and the most.
    public double Dt = 1.0 / 60.0;
    public double[]? Pattern;
    public Random? Jitter;

    public List<TrackSample> Log { get; } = [];

    public double3 Up => Vec.Unit(Position);
    public double3 Forward => Attitude * new double3(0, 1, 0);
    public double TerrainUnderM => Vec.Len(Position) - _world.RadiusM - _world.HeightAt(Up);
    public (double East, double North) Where => _world.Flatten(Position);
    public bool Finite => Vec.IsFinite(Position) && Vec.IsFinite(Velocity) && Vec.IsFinite(Spin);

    // Per kilogram: the speed's, the turning's and the height's.
    public double Energy
    {
        get
        {
            double3 i = _car.Inertia;
            double turning = 0.5 * ((i.X * Spin.X * Spin.X) + (i.Y * Spin.Y * Spin.Y) + (i.Z * Spin.Z * Spin.Z)) / _car.MassKg;
            return (0.5 * Vec.Len2(Velocity)) + turning + (_world.Gravity * Vec.Len(Position));
        }
    }

    // Sets the car on its wheels, standing still, facing so many degrees north of east, on the ground
    // there or on a road whose top is within a metre of aboveGroundM over it: under a deck is not on it.
    public void Place(double eastM, double northM, double headingDeg, double aboveGroundM = 0.0)
    {
        double3 dir = _world.Dir(eastM, northM);
        doubleQuat level = doubleQuat.CreateFromAxisAngle(new double3(0, 0, 1), eastM / _world.RadiusM)
                         * doubleQuat.CreateFromAxisAngle(new double3(0, 1, 0), -northM / _world.RadiusM)
                         * doubleQuat.CreateFromAxisAngle(new double3(1, 0, 0), headingDeg * Math.PI / 180.0);
        double3 ahead = level * new double3(0, 1, 0), left = level * new double3(0, 0, 1);

        bool onRoad = false;
        double Top(double3 offset)
        {
            double3 d = Vec.Unit((dir * _world.RadiusM) + offset);
            double ground = _world.HeightAt(d);
            double3 probe = d * (_world.RadiusM + ground + aboveGroundM + 1.0);
            if (_road is null || !_road.TryHeightOver(probe, out double over) || over is < 0.0 or > 2.0) return ground;
            onRoad = true;
            return Math.Max(ground + aboveGroundM + 1.0 - over, ground);
        }

        double pitch = Math.Atan((Top(ahead) - Top(-ahead)) / 2.0), roll = Math.Atan((Top(left) - Top(-left)) / 2.0);
        Attitude = level * doubleQuat.CreateFromAxisAngle(new double3(0, 0, 1), -pitch)
                         * doubleQuat.CreateFromAxisAngle(new double3(0, 1, 0), roll);
        Velocity = Spin = Vec.Zero;

        // From clear of the surface, down by what the wheels have to spare between them.
        Position = dir * (_world.RadiusM + Top(Vec.Zero) + _car.Com.X + 0.5);
        Array.Clear(_roadOver);
        Read(onRoad ? _road : null);
        double spare = 0.0;
        for (int i = 0; i < _hubs.Length; i++) spare += (_contacts[i].HubHeight - _car.Profile.Corners[i].Radius) / _hubs.Length;
        Position -= (Attitude * _contacts[0].GroundUp) * spare;
        Array.Clear(_roadOver);
        Array.Clear(_hullOver);
    }

    public void Launch(double speed) => Velocity = Forward * speed;

    public void Settle(double seconds = 3.0)
    {
        for (double t = 0.0; t < seconds; t += Dt) Step(default);
        Log.Clear();
    }

    private void Read(RoadSurface? road) => WheelGround.Read(
        Position, Attitude, doubleQuat.Conjugate(Attitude) * Velocity, Spin, _hubs, _world.RadiusM,
        _world, null, road, _roadOver, _contacts, _hubHeights);

    // Asked as the wheels are, so a hull point is over the same roads a wheel there would be, and
    // answered along the one plane through what is under all four: on a ramp, square to the ramp.
    // Each point is slowed to what brings it to the surface by the end of the step and no further, as
    // a physics engine holds a contact it sees coming, so a car at rest on its hull does not sink a
    // step of gravity and get put back. One already under, the surface having risen to it, is put on it.
    private bool Hull(double dt, out double hardest)
    {
        double3 velocity = doubleQuat.Conjugate(Attitude) * Velocity;
        WheelGround.Read(Position, Attitude, velocity, Spin, _hull, _world.RadiusM, _world, null, _road, _hullOver, _hullContacts, _hullHeights);

        double3 normal = _hullContacts[0].GroundUp, inertia = _car.Inertia;
        double sunk = 0.0;
        foreach (WheelContact c in _hullContacts) sunk = Math.Max(sunk, -c.HubHeight);

        // Each point's push so far: one may be eased off as another takes the weight, and none pulls.
        Span<double> pushed = stackalloc double[_hull.Length];
        bool down = false;
        hardest = 0.0;
        for (int pass = 0; pass < 64; pass++)
        {
            double moved = 0.0;
            for (int i = 0; i < _hull.Length; i++)
            {
                double excess = -Vec.Dot(velocity + Vec.Cross(Spin, _hull[i]), normal) - (Math.Max(_hullContacts[i].HubHeight, 0.0) / dt);
                if (pass == 0) hardest = Math.Max(hardest, excess);

                double3 arm = Vec.Cross(_hull[i], normal);
                double3 turn = new(arm.X / inertia.X, arm.Y / inertia.Y, arm.Z / inertia.Z);
                double push = Math.Max(pushed[i] + (excess / ((1.0 / _car.MassKg) + Vec.Dot(normal, Vec.Cross(turn, _hull[i])))), 0.0) - pushed[i];
                if (push == 0.0) continue;

                pushed[i] += push;
                velocity += normal * (push / _car.MassKg);
                Spin += turn * push;
                moved = Math.Max(moved, Math.Abs(push) / _car.MassKg);
                down = true;
            }
            if (moved < 1e-9) break;
        }
        if (!down && !(sunk > 0.0)) return false;

        Velocity = Attitude * velocity;
        if (sunk > 0.0)
        {
            Position += (Attitude * normal) * sunk;
            for (int i = 0; i < _hullOver.Length; i++)
            {
                if (_hullOver[i] is { } over) _hullOver[i] = over + sunk;
            }
        }
        return true;
    }

    private double NextDt() =>
        Jitter is { } random ? LeastStep + ((MostStep - LeastStep) * random.NextDouble())
        : Pattern is { } pattern ? pattern[_tick++ % pattern.Length]
        : Dt;

    public TrackSample Step(DriveInput input)
    {
        double dt = NextDt();
        BuggyProfile p = _car.Profile;
        double3 up = new(1, 0, 0), forward = new(0, 1, 0);
        double speed = Vec.Len(Velocity), energy = Energy, terrainUnder = TerrainUnderM;
        (double east, double north) = Where;

        Read(_road);

        double clearance = double.PositiveInfinity, deepest = double.NegativeInfinity;
        int onRoad = 0;
        for (int i = 0; i < _hubs.Length; i++)
        {
            if (_roadOver[i] is not null) onRoad++;
            clearance = Math.Min(clearance, _hubHeights[i] - p.Corners[i].Radius);
            deepest = Math.Max(deepest, p.Corners[i].Radius - p.BumpTravel - _hubHeights[i]);
        }
        double3 plane = _contacts[0].GroundUp;
        double upDot = Vec.Dot(Attitude * up, Up);

        // As Buggies does: past a tenth of a second the springs are left out.
        bool warped = dt > 0.1;
        DriveImpulse j = warped
            ? default
            : Drive.Step(input, _contacts, _hubs, up, forward, _car.MassKg, _world.Gravity, _world.Air, dt);

        int grounded = warped ? 0 : Drive.Grounded.Count(g => g);
        double wings = 0.5 * _world.Air * p.DownforceAreaM2 * speed * speed;
        double lost = grounded == 0 ? 0.0
            : ((0.5 * _world.Air * p.DragAreaM2 * speed * speed * speed)
               + (p.RollingResistance * ((_car.MassKg * _world.Gravity) + wings) * speed)) * dt / _car.MassKg;

        Velocity += Attitude * (j.Linear / _car.MassKg);
        Spin += new double3(j.Angular.X / _car.Inertia.X, j.Angular.Y / _car.Inertia.Y, j.Angular.Z / _car.Inertia.Z);

        Velocity -= Up * (_world.Gravity * dt);
        bool hullDown = Hull(dt, out double hardest);
        Position += Velocity * dt;
        double rate = Vec.Len(Spin);
        if (rate > 0.0) Attitude *= doubleQuat.CreateFromAxisAngle(Spin / rate, rate * dt);

        TrackSample sample = new(
            Time, dt, east, north, speed, terrainUnder, energy, lost, grounded, onRoad, clearance, deepest,
            Math.Asin(Math.Clamp(plane.Y, -1.0, 1.0)) * 180.0 / Math.PI,
            Math.Asin(Math.Clamp(plane.Z, -1.0, 1.0)) * 180.0 / Math.PI,
            upDot, hullDown, hardest);
        Log.Add(sample);
        Time += dt;
        return sample;
    }
}

public class TrackRigTests
{
    private static BuggyDriveTests.Rig FlatRig(TrackCar car, double dt) =>
        new(dt, car.Profile, car.Com) { RigMass = car.MassKg, Box = car.Name == "F2004" ? new double3(0.5, 5.6, 2.0) : null };

    // The flat Rig is what every car was tuned against, so on a sphere with nothing on it this one has
    // to be that rig: a planet's curve under a wheelbase is a fraction of a micron.
    [Theory]
    [InlineData("Manx", 1.0 / 60.0)]
    [InlineData("Manx", 1.0 / 30.0)]
    [InlineData("Eldorado", 1.0 / 60.0)]
    [InlineData("Eldorado", 1.0 / 30.0)]
    [InlineData("F2004", 1.0 / 60.0)]
    [InlineData("F2004", 1.0 / 30.0)]
    public void OnABareSphereItSettlesAndPullsAwayAsTheFlatRigDoes(string name, double dt)
    {
        TrackCar car = TrackCar.Of(name);
        BuggyDriveTests.Rig flat = FlatRig(car, dt);
        flat.Position = car.Com + new double3(0.05, 0, 0);
        TrackRig rig = new(car, TrackWorld.Earth()) { Dt = dt };
        rig.Position += rig.Up * 0.05;

        for (double t = 0.0; t < 6.0; t += dt)
        {
            flat.Step(default);
            rig.Step(default);
        }
        double rests = rig.TerrainUnderM - flat.Position.X;
        Assert.True(Math.Abs(rests) < 1e-5, $"rests {rests * 1e6:F1} microns from where the flat rig does");
        Assert.True(Vec.Len(rig.Velocity) < 0.01, $"still moving at {Vec.Len(rig.Velocity):F3} m/s");

        for (double t = 0.0; t < 5.0; t += dt)
        {
            flat.Step(new DriveInput(1.0, 0.0));
            rig.Step(new DriveInput(1.0, 0.0));
        }
        double speed = Vec.Dot(rig.Velocity, rig.Forward);
        Assert.True(Math.Abs(speed - flat.Velocity.Y) < 0.001 * flat.Velocity.Y,
                    $"{speed:F3} m/s after five seconds against the flat rig's {flat.Velocity.Y:F3}");
        double gone = flat.Position.Y - car.Com.Y;
        Assert.True(Math.Abs(rig.Where.East - gone) < 0.001 * gone, $"{rig.Where.East:F2} m gone against the flat rig's {gone:F2}");
        Assert.True(rig.Log[^1].UpDot > 0.999, "tipped");
        Assert.DoesNotContain(rig.Log, s => s.HullDown);
    }

    [Theory]
    [InlineData("Manx")]
    [InlineData("Eldorado")]
    [InlineData("F2004")]
    public void ItIsPlacedStandingOnASlopeASideSlopeAndADeckAndStaysThere(string name)
    {
        TrackCar car = TrackCar.Of(name);
        TrackWorld deckWorld = TrackWorld.Earth();
        Circuit deck = new Circuit().AddNode(0.0, deckWorld.Deg(-100.0), out int a).Extend(a, 0.0, deckWorld.Deg(100.0), out int b)
            .SetHeight(a, 3.0).SetHeight(b, 3.0);

        (TrackWorld World, RoadSurface? Road, double Above, double Heading, string What)[] places =
        [
            (TrackWorld.Earth(TrackWorld.Slope(0.2)), null, 0.0, 0.0, "facing up a 20% slope"),
            (TrackWorld.Earth(TrackWorld.Slope(0.2)), null, 0.0, 180.0, "facing down a 20% slope"),
            (TrackWorld.Earth(TrackWorld.SideSlope(0.15)), null, 0.0, 0.0, "across a 15% slope"),
            (TrackWorld.Luna(TrackWorld.Slope(-0.1)), null, 0.0, 30.0, "on a 10% slope on Luna"),
            (deckWorld, deckWorld.Surface(deck), 3.0, 0.0, "on a deck 3 m up"),
        ];
        foreach ((TrackWorld world, RoadSurface? road, double above, double heading, string what) in places)
        {
            TrackRig rig = new(car, world, road);
            rig.Place(10.0, 0.0, heading, above);
            (double east, double north) = rig.Where;
            for (double t = 0.0; t < 3.0; t += rig.Dt) rig.Step(default);

            TrackSample last = rig.Log[^1];
            // A standing tyre gives back 0.7 of its speed a step, so on a slope the car creeps at what
            // gravity adds in a step over that: 5 cm/s on 20% at 60 Hz.
            Assert.True(Math.Abs(rig.Where.East - east) + Math.Abs(rig.Where.North - north) < 0.4,
                        $"{what}: crept {rig.Where.East - east:F3} m east and {rig.Where.North - north:F3} m north");
            Assert.True(Vec.Len(rig.Velocity) < 0.15, $"{what}: still moving at {Vec.Len(rig.Velocity):F3} m/s");
            Assert.True(Math.Abs(last.PitchDeg) < 2.0 && Math.Abs(last.RollDeg) < 2.0,
                        $"{what}: {last.PitchDeg:F2} deg of pitch and {last.RollDeg:F2} of roll off the ground under it");
            Assert.Equal(4, last.Grounded);
            Assert.Equal(road is null ? 0 : 4, last.OnRoad);
            Assert.DoesNotContain(rig.Log, s => s.HullDown);
        }
    }

    [Fact]
    public void OverASinusoidAndAStepInTheGroundItStaysOnItsWheels()
    {
        foreach (TrackCar car in TrackCar.All)
        {
            TrackRig waves = new(car, TrackWorld.Earth(TrackWorld.Sinusoid(0.3, 40.0)));
            waves.Launch(15.0);
            for (double t = 0.0; t < 8.0; t += waves.Dt) waves.Step(default);
            Assert.True(waves.Finite && waves.Log.Min(s => s.UpDot) > 0.9, $"{car.Name} over the waves");
            Assert.True(waves.Where.East > 80.0, $"{car.Name} got {waves.Where.East:F0} m over the waves");

            TrackRig kerb = new(car, TrackWorld.Earth(TrackWorld.Step(-0.1, 20.0)));
            kerb.Launch(10.0);
            for (double t = 0.0; t < 5.0; t += kerb.Dt) kerb.Step(default);
            Assert.True(kerb.Finite && kerb.Log.Min(s => s.UpDot) > 0.9, $"{car.Name} off the kerb");
            Assert.True(Math.Abs(kerb.TerrainUnderM - car.Com.X) < 0.03, $"{car.Name} rests {kerb.TerrainUnderM:F3} m up after the kerb");
        }
    }

    // A run that went wrong on a frame-time hitch is only a bug report if it can be run again.
    [Fact]
    public void ARunOnRandomStepsIsTheSameRunWhenItsStepsArePlayedBack()
    {
        TrackCar car = TrackCar.Eldorado;
        TrackWorld world = TrackWorld.Earth(TrackWorld.Sinusoid(0.1, 60.0));
        TrackRig random = new(car, world) { Jitter = new Random(20261007) };
        random.Launch(20.0);
        for (int i = 0; i < 400; i++) random.Step(new DriveInput(0.5, 0.1));

        Assert.InRange(random.Log.Min(s => s.Dt), TrackRig.LeastStep, 0.01);
        Assert.InRange(random.Log.Max(s => s.Dt), 0.09, TrackRig.MostStep);
        Assert.True(random.Finite && random.Log.Min(s => s.UpDot) > 0.9, "the random steps upset it");

        TrackRig replay = new(car, world) { Pattern = [.. random.Log.Select(s => s.Dt)] };
        replay.Launch(20.0);
        for (int i = 0; i < 400; i++) replay.Step(new DriveInput(0.5, 0.1));

        Assert.Equal(random.Position, replay.Position);
        Assert.Equal(random.Velocity, replay.Velocity);
        Assert.Equal(random.Spin, replay.Spin);
    }
}
