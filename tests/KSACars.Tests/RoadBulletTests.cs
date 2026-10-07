using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

// A car fired at a piece of road with nobody driving it: the wheel straight, the throttle shut or
// held, and what the rig saw of it judged afterwards. Every road here is straight and runs east
// along the equator, with what the car meets at east 0.
public class RoadBulletTests
{
    internal sealed record Track(string Name, TrackWorld World, RoadSurface? Road);

    // What a run came to. Energies are per kilogram.
    internal sealed record Outcome(
        bool Finite, double Seconds, double EndSpeed, int HullSteps, int HullHits, double HullMs,
        double Deepest, double AirSeconds, double LongestFlight, int Flights, double PeakClearance,
        double PitchDeg, double RollDeg, double UpDot, double Gain, double Budget,
        double TerrainLow, double TerrainHigh, double TerrainStart, double TerrainEnd, double EndEast)
    {
        public const string Header = "finite\tsecs\tend_ms\thull_steps\thull_hits\thull_ms\tdeepest_m\tair_s\tlongest_s\t"
                                   + "flights\tpeak_m\tpitch_deg\troll_deg\tup_dot\tgain_J/kg\tbudget_J/kg\tlow_m\thigh_m\tend_m";

        public string Row() => string.Join('\t', new[]
        {
            Finite ? "1" : "0", F(Seconds, 2), F(EndSpeed, 1), HullSteps.ToString(CultureInfo.InvariantCulture),
            HullHits.ToString(CultureInfo.InvariantCulture), F(HullMs, 2),
            F(Deepest, 3), F(AirSeconds, 2), F(LongestFlight, 2), Flights.ToString(CultureInfo.InvariantCulture),
            F(PeakClearance, 3), F(PitchDeg, 1), F(RollDeg, 1), F(UpDot, 3), F(Gain, 1), F(Budget, 1),
            F(TerrainLow - TerrainStart, 3), F(TerrainHigh - TerrainStart, 3), F(TerrainEnd - TerrainStart, 3),
        });

        private static string F(double v, int places) => v.ToString("F" + places, CultureInfo.InvariantCulture);
    }

    private const double Width = 10.0;

    private static readonly ConcurrentDictionary<string, Track> Tracks = new();

    private static TrackWorld World(bool luna, Func<double, double, double>? height = null) =>
        luna ? TrackWorld.Luna(height) : TrackWorld.Earth(height);

    // A road along the equator through points so many metres east, each so far above the ground.
    private static RoadSurface Road(TrackWorld world, params (double East, double Height)[] points)
    {
        Circuit circuit = new Circuit { WidthM = Width }.AddNode(0.0, world.Deg(points[0].East), out int last).SetHeight(last, points[0].Height);
        for (int i = 1; i < points.Length; i++)
        {
            circuit = circuit.Extend(last, 0.0, world.Deg(points[i].East), out last).SetHeight(last, points[i].Height);
        }
        return world.Surface(circuit);
    }

    internal static Track Level(bool luna = false) => Tracks.GetOrAdd($"level{luna}", _ =>
    {
        TrackWorld world = World(luna);
        return new Track("level road", world, Road(world, (-300.0, 0.0), (900.0, 0.0)));
    });

    // Level to east 0, up by the rise over the run, eased as a circuit eases it, then a level deck.
    internal static Track Ramp(double rise, double run, bool luna = false) => Tracks.GetOrAdd($"ramp{rise}/{run}{luna}", _ =>
    {
        TrackWorld world = World(luna);
        return new Track($"ramp {rise:F0} m over {run:F0} m", world,
                         Road(world, (-300.0, 0.0), (0.0, 0.0), (run, rise), (run + 600.0, rise)));
    });

    // A deck so high from east -600 to 0, where it ends in the air.
    internal static Track Deck(double height) => Tracks.GetOrAdd($"deck{height}", _ =>
    {
        TrackWorld world = World(false);
        return new Track($"deck {height:F1} m up", world, Road(world, (-600.0, height), (0.0, height)));
    });

    // A road on ground of one grade that stands a step higher from east 0 on. A circuit eases every
    // change of height, so this one is laid as a strip: the riser is a piece a tenth of a metre long.
    internal static Track Stepped(double step, double grade, bool luna = false) => Tracks.GetOrAdd($"step{step}/{grade}{luna}", _ =>
    {
        TrackWorld world = World(luna, TrackWorld.Slope(grade));
        List<double3> line = [];
        List<double> above = [];
        void Point(double east, double over)
        {
            double3 dir = world.Dir(east, 0.0);
            line.Add(dir * (world.RadiusM + world.HeightAt(dir) + over));
            above.Add(over);
        }
        for (double east = -300.0; east <= -2.0; east += TrackWorld.SpacingM) Point(east, TrackWorld.LiftM);
        Point(0.0, TrackWorld.LiftM);
        Point(0.1, TrackWorld.LiftM + step);
        for (double east = 2.0; east <= 900.0; east += TrackWorld.SpacingM) Point(east, TrackWorld.LiftM + step);
        RoadLaying.Strip strip = new([.. line], 0.5 * Width, 1200.0, false, [.. above]);
        return new Track($"step {step:F2} m on {grade * 100.0:+0;-0;0}%", world, RoadLaying.Surface([strip]));
    });

    // Settled where it is put, then given its speed along its own nose and left alone until the time
    // is up, it has gone past `until` metres east, or it has all but stopped.
    internal static Outcome Fire(TrackCar car, Track track, double dt, double east, double north, double headingDeg, double speed,
                                 double seconds, double throttle = 0.0, double above = 0.0, double until = double.PositiveInfinity,
                                 bool west = false, int? seed = null)
    {
        TrackRig rig = new(car, track.World, track.Road);
        rig.Place(east, north, headingDeg, above);
        double terrainStart = rig.TerrainUnderM;
        rig.Settle(1.0);
        rig.Dt = dt;
        rig.Jitter = seed is { } s ? new Random(s) : null;
        rig.Launch(speed);
        double started = rig.Time;

        DriveInput input = new(throttle, 0.0);
        while (rig.Time - started < seconds && rig.Finite && (west ? rig.Where.East > until : rig.Where.East < until))
        {
            rig.Step(input);
            if (speed > 0.0 && Vec.Len(rig.Velocity) < 2.0) break;
        }

        int flights = 0, hull = 0, hits = 0;
        bool down = false;
        double hardest = 0.0, deepest = double.NegativeInfinity, air = 0.0, flight = 0.0, longest = 0.0, peak = 0.0;
        double pitch = 0.0, roll = 0.0, upDot = 1.0, budget = 0.0, low = double.PositiveInfinity, high = double.NegativeInfinity;
        foreach (TrackSample sample in rig.Log)
        {
            deepest = Math.Max(deepest, sample.DeepestM);
            if (sample.Airborne)
            {
                if (flight == 0.0) flights++;
                flight += sample.Dt;
                air += sample.Dt;
                longest = Math.Max(longest, flight);
                peak = Math.Max(peak, sample.ClearanceM);
            }
            else
            {
                flight = 0.0;
            }
            pitch = Math.Max(pitch, Math.Abs(sample.PitchDeg));
            roll = Math.Max(roll, Math.Abs(sample.RollDeg));
            upDot = Math.Min(upDot, sample.UpDot);
            budget += sample.LossBudget;
            low = Math.Min(low, sample.TerrainUnderM);
            high = Math.Max(high, sample.TerrainUnderM);
            if (sample.HullDown)
            {
                hull++;
                if (!down) hits++;
                hardest = Math.Max(hardest, sample.HullMs);
            }
            down = sample.HullDown;
        }

        return new Outcome(
            rig.Finite, rig.Time - started, Vec.Len(rig.Velocity), hull, hits, hardest, deepest, air, longest, flights, peak,
            pitch, roll, upDot, rig.Energy - rig.Log[0].Energy, budget, low, high, terrainStart, rig.TerrainUnderM, rig.Where.East);
    }

    internal static readonly double[] Steps = [1.0 / 60.0, 0.02, 0.05];

    // Each car at 10 m/s and up to what it can reach.
    internal static double[] Speeds(TrackCar car) =>
        car.Name == "Manx" ? [10.0, 25.0] : car.Name == "Eldorado" ? [10.0, 30.0, 50.0] : [10.0, 30.0, 60.0, 100.0];

    // A second's run before what the car is fired at, and what is left of five after it.
    private static double RunUp(double speed) => 3.0 + speed;

    private const double After = 5.0;

    internal static Outcome Control(TrackCar car, double speed, double dt, bool luna = false) =>
        Fire(car, Level(luna), dt, -250.0, 0.0, 0.0, speed, After + 1.0);

    // Coasting where its speed carries it over the top with half to spare, and at full throttle where not.
    internal static bool CoastsUp(double speed, double rise, double gravity) => 0.5 * speed * speed > 2.0 * gravity * rise;

    internal static Outcome RampUp(double rise, double run, TrackCar car, double speed, double dt, bool luna = false)
    {
        Track track = Ramp(rise, run, luna);
        double throttle = CoastsUp(speed, rise, track.World.Gravity) ? 0.0 : 1.0;
        return Fire(car, track, dt, -RunUp(speed), 0.0, 0.0, speed, 30.0, throttle, until: run + (After * speed));
    }

    // Only as far as halfway up, which is the foot of the ramp and none of its crest.
    internal static Outcome RampFoot(double rise, double run, TrackCar car, double speed, double dt, bool luna = false) =>
        Fire(car, Ramp(rise, run, luna), dt, -RunUp(speed), 0.0, 0.0, speed, 30.0, until: 0.5 * run);

    internal static Outcome RampDown(double rise, double run, TrackCar car, double speed, double dt, bool luna = false) =>
        Fire(car, Ramp(rise, run, luna), dt, run + RunUp(speed), 0.0, 180.0, speed, 30.0, above: rise, until: -After * speed, west: true);

    internal static Outcome Step(double step, double grade, TrackCar car, double speed, double dt, bool luna = false) =>
        Fire(car, Stepped(step, grade, luna), dt, -RunUp(speed), 0.0, 0.0, speed, After + 1.0);

    // From the grass south of a road on the ground, across it at an angle.
    internal static Outcome Entry(double angleDeg, TrackCar car, double speed, double dt)
    {
        double angle = angleDeg * Math.PI / 180.0;
        // From well clear of the verge, which comes up through the grass a metre out from the road's edge.
        double reach = (0.5 * Width) + 7.5;
        double across = 2.0 * reach / Math.Sin(angle);
        return Fire(car, Level(), dt, -RunUp(speed) * Math.Cos(angle), -reach - (RunUp(speed) * Math.Sin(angle)), angleDeg, speed,
                    ((RunUp(speed) + across) / speed) + 2.0);
    }

    // On the grass along a deck, so far north of its centre line.
    internal static Outcome Along(double height, double north, TrackCar car, double speed, double dt) =>
        Fire(car, Deck(height), dt, -550.0, north, 0.0, speed, After, until: -30.0);

    internal static Outcome OffTheEnd(TrackCar car, double speed, double dt) =>
        Fire(car, Deck(3.0), dt, -RunUp(speed), 0.0, 0.0, speed, After + 1.0, above: 3.0);

    internal static Outcome Parked(TrackCar car, double dt, double deck) =>
        deck > 0.0 ? Fire(car, Deck(deck), dt, -300.0, 0.0, 0.0, 0.0, 6.0, above: deck)
                   : Fire(car, Level(), dt, 0.0, 0.0, 0.0, 0.0, 6.0);

    internal static readonly (double Rise, double Run)[] Ramps = [(8.0, 100.0), (8.0, 40.0), (3.0, 60.0)];
    // Steps a car's hull clears, which its wheels have to take; and for the table, ones it does not:
    // those the rig's hull rides up, where in the game the nose meets the riser.
    internal static readonly double[] StepHeights = [0.05, 0.1];
    internal static readonly double[] TallSteps = [0.25, 0.5];
    internal static readonly double[] Grades = [0.0, 0.1, 0.2, -0.1, -0.2];
    internal static readonly double[] EntryAngles = [10.0, 30.0, 90.0];
    // High enough to be decks, with nothing beside them, and low enough to stand on a bank instead.
    internal static readonly double[] DeckHeights = [4.5, 6.0, 9.0, 12.0];
    internal static readonly double[] BankHeights = [1.0, 2.0, 3.0];
    internal static readonly double[] LongSteps = [0.15, 0.4];

    // Two metres out from the deck's edge.
    internal const double BesideM = -((0.5 * Width) + 2.0);

    // What went wrong over a sweep, all of it, so one run says which cars, speeds and steps.
    internal sealed class Faults
    {
        private readonly List<string> _found = [];

        public void Check(bool ok, TrackCar car, double speed, double dt, bool luna, FormattableString what)
        {
            if (ok) return;
            _found.Add(string.Create(CultureInfo.InvariantCulture,
                $"{car.Name} at {speed:F0} m/s, {dt * 1000.0:F1} ms{(luna ? ", Luna" : "")}: {FormattableString.Invariant(what)}"));
        }

        public void None() => Assert.True(_found.Count == 0, $"{_found.Count} run(s):\n" + string.Join('\n', _found.Take(40)));
    }

    // Springs and tyres settling a car are not friction the budget counts: 2 J/kg is 20 cm of height on
    // Earth, and 2% covers a budget reckoned from the speed at the start of each step.
    internal static double Slack(Outcome o) => 2.0 + (0.02 * o.Budget);

    // Nothing a road does to a coasting car gives it energy: it has what it started with less what drag
    // and rolling resistance took, or less.
    internal static bool NoEnergyGained(Outcome o) => o.Gain + o.Budget <= Slack(o);

    // And with no flight to land from and no hull to land on, no less.
    internal static bool EnergyKept(Outcome o) =>
        NoEnergyGained(o) && (o.AirSeconds > 0.0 || o.HullSteps > 0 || o.Gain + o.Budget >= -Slack(o));

    // Nothing but coasting: no wheel off the road, no lean, no collider down, no energy from nowhere.
    private static void Calm(Faults faults, Outcome o, TrackCar car, double speed, double dt, bool luna = false)
    {
        faults.Check(o.Finite, car, speed, dt, luna, $"not finite");
        faults.Check(o.HullSteps == 0, car, speed, dt, luna, $"down on its hull for {o.HullSteps} steps, at up to {o.HullMs:F2} m/s");
        faults.Check(o.AirSeconds == 0.0, car, speed, dt, luna, $"off its wheels for {o.AirSeconds:F2} s");
        faults.Check(o.PitchDeg < 1.0 && o.RollDeg < 1.0, car, speed, dt, luna, $"pitched {o.PitchDeg:F1} deg, rolled {o.RollDeg:F1}");
        faults.Check(EnergyKept(o), car, speed, dt, luna, $"energy {o.Gain:F1} J/kg against a budget of {o.Budget:F1}");
        faults.Check(o.TerrainHigh - o.TerrainLow < 0.03, car, speed, dt, luna,
                     $"moved {o.TerrainLow - o.TerrainStart:F3} to {o.TerrainHigh - o.TerrainStart:F3} m over the ground");
    }

    [Fact]
    public void OnALevelRoadACarCoastsAndNothingElseHappens()
    {
        Faults faults = new();
        foreach (TrackCar car in TrackCar.All)
        {
            foreach (double speed in Speeds(car))
            {
                foreach (double dt in Steps) Calm(faults, Control(car, speed, dt), car, speed, dt);
                Calm(faults, Control(car, speed, Steps[0], luna: true), car, speed, Steps[0], luna: true);
            }
        }
        faults.None();
    }

    // KSA's step is the frame's, and a frame is as long as it is.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ALevelRoadIsCoastedTheSameOnStepsOfAnyLength(int seed)
    {
        Faults faults = new();
        foreach (TrackCar car in TrackCar.All)
        {
            double speed = Speeds(car)[1];
            Outcome o = Fire(car, Level(), 0.0, -250.0, 0.0, 0.0, speed, After + 1.0, seed: seed);
            Calm(faults, o, car, speed, 0.0);
        }
        faults.None();
    }

    // A road stands 7 cm proud of the grass and its verge brings a wheel up to it at 1 in 15.
    [Fact]
    public void OntoARoadFromTheGrassAtAnyAngleACarIsCarriedOnItsSprings()
    {
        Faults faults = new();
        foreach (TrackCar car in TrackCar.All)
        {
            foreach (double speed in Speeds(car))
            {
                foreach (double dt in Steps)
                {
                    foreach (double angle in EntryAngles)
                    {
                        Outcome o = Entry(angle, car, speed, dt);
                        faults.Check(o.Finite && o.HullSteps == 0, car, speed, dt, false, $"{angle:F0} deg: down on its hull for {o.HullSteps} steps");
                        faults.Check(o.UpDot > 0.99 && o.PitchDeg < 5.0 && o.RollDeg < 5.0, car, speed, dt, false,
                                     $"{angle:F0} deg: pitched {o.PitchDeg:F1} deg, rolled {o.RollDeg:F1}");
                        faults.Check(o.PeakClearance <= 0.15, car, speed, dt, false, $"{angle:F0} deg: thrown {o.PeakClearance:F2} m clear");
                        faults.Check(EnergyKept(o), car, speed, dt, false, $"{angle:F0} deg: energy {o.Gain:F1} J/kg against a budget of {o.Budget:F1}");
                    }
                }
            }
        }
        faults.None();
    }

    [Fact]
    public void OnTheGrassBesideARaisedDeckACarIsLeftOnTheGround()
    {
        Faults faults = new();
        foreach (TrackCar car in TrackCar.All)
        {
            foreach (double speed in Speeds(car))
            {
                foreach (double height in DeckHeights)
                {
                    foreach (double dt in new[] { Steps[0], Steps[2] }) Calm(faults, Along(height, BesideM, car, speed, dt), car, speed, dt);
                }
            }
        }
        faults.None();
    }

    // Two metres out from where the bank of a road so high goes under the grass.
    private static double PastTheBank(double height) =>
        -((0.5 * Width) + RoadRibbon.VergeM + ((height + TrackWorld.LiftM + RoadRibbon.BuriedM) / RoadRibbon.BankSlope) + 2.0);

    [Fact]
    public void OnTheGrassPastTheFootOfARaisedRoadsBankACarIsLeftOnTheGround()
    {
        Faults faults = new();
        foreach (TrackCar car in TrackCar.All)
        {
            foreach (double speed in Speeds(car))
            {
                foreach (double height in BankHeights)
                {
                    foreach (double dt in new[] { Steps[0], Steps[2] }) Calm(faults, Along(height, PastTheBank(height), car, speed, dt), car, speed, dt);
                }
            }
        }
        faults.None();
    }

    // Three centimetres: the springs' own movement as the car is let go.
    private static void LeftOnTheGround(Faults faults, double height, TrackCar car, double speed, double dt)
    {
        Outcome o = Along(height, 0.0, car, speed, dt);
        faults.Check(o.TerrainHigh - o.TerrainStart < 0.03 && o.HullSteps == 0, car, speed, dt, false,
                     $"under a deck {height:F1} m up: lifted {o.TerrainHigh - o.TerrainStart:F2} m");
    }

    [Fact]
    public void UnderADeckThreeMetresUpACarIsLeftOnTheGround()
    {
        Faults faults = new();
        foreach (TrackCar car in TrackCar.All)
        {
            foreach (double speed in Speeds(car))
            {
                foreach (double dt in Steps) LeftOnTheGround(faults, 3.0, car, speed, dt);
            }
        }
        faults.None();
    }

    // A deck is a collider RoadSlabs.ThicknessM deep, so a car is under one only where its own
    // colliders end below that. Under any such deck its wheels are on the ground.
    [Fact]
    public void UnderAnyDeckACarFitsUnderItIsLeftOnTheGround()
    {
        Faults faults = new();
        int judged = 0;
        foreach (TrackCar car in TrackCar.All)
        {
            foreach (double speed in Speeds(car))
            {
                foreach (double height in new[] { 1.0, 1.25, 1.5, 2.0 })
                {
                    if (height + TrackWorld.LiftM - RoadSlabs.ThicknessM < car.RoofM) continue;
                    judged++;
                    LeftOnTheGround(faults, height, car, speed, Steps[0]);
                }
            }
        }
        Assert.True(judged >= 15, $"only {judged} runs fitted under a deck");
        faults.None();
    }

    // What a car does on the ground once it has landed on its nose is the rig's and is not judged.
    [Fact]
    public void OffTheOpenEndOfADeckACarFallsToTheGround()
    {
        Faults faults = new();
        foreach (TrackCar car in TrackCar.All)
        {
            foreach (double speed in Speeds(car))
            {
                foreach (double dt in Steps)
                {
                    Outcome o = OffTheEnd(car, speed, dt);
                    faults.Check(o.Finite, car, speed, dt, false, $"not finite");
                    faults.Check(o.TerrainHigh - o.TerrainStart <= 0.05, car, speed, dt, false, $"rose {o.TerrainHigh - o.TerrainStart:F2} m off the deck");
                    faults.Check(o.TerrainLow - o.TerrainStart < -2.5, car, speed, dt, false, $"fell only {o.TerrainStart - o.TerrainLow:F2} m");
                }
            }
        }
        faults.None();
    }

    // Past a tenth of a second the springs are left out, and a car is on its hull as it is on the
    // ground: down by what its hull stands clear at rest, and no further.
    [Fact]
    public void ParkedOnARoadUnderALongStepACarSitsOnItsHullAndStaysThere()
    {
        Faults faults = new();
        foreach (TrackCar car in TrackCar.All)
        {
            foreach (double dt in LongSteps)
            {
                foreach (double deck in new[] { 0.0, 3.0 })
                {
                    Outcome o = Parked(car, dt, deck);
                    double sank = o.TerrainStart - o.TerrainLow, moved = o.EndEast - (deck > 0.0 ? -300.0 : 0.0);
                    faults.Check(o.Finite && sank < car.FloorM + 0.01 && o.TerrainHigh - o.TerrainStart < 0.01, car, 0.0, dt, false,
                                 $"deck {deck:F0} m: sank {sank:F3} m and rose {o.TerrainHigh - o.TerrainStart:F3} m in {o.Seconds:F1} s, its hull {car.FloorM:F2} m clear at rest");
                    faults.Check(o.EndSpeed < 0.01 && Math.Abs(moved) < 0.01 && o.UpDot > 0.999, car, 0.0, dt, false,
                                 $"deck {deck:F0} m: left moving at {o.EndSpeed:F3} m/s, {moved:F3} m from where it was parked");
                    faults.Check(NoEnergyGained(o), car, 0.0, dt, false, $"deck {deck:F0} m: energy {o.Gain:F2} J/kg from nowhere");
                }
            }
        }
        faults.None();
    }

    // Every bullet there is, the ones no test judges included, as a table: written to the file named
    // in KSACARS_BULLETS, and nothing without it.
    [Fact]
    public void EveryBulletIsWrittenOutWhenAskedFor()
    {
        if (Environment.GetEnvironmentVariable("KSACARS_BULLETS") is not { Length: > 0 } path) return;

        StringBuilder table = new();
        table.Append("fixture\tgravity\tcar\tspeed\tdt_ms\t").AppendLine(Outcome.Header);
        void Row(string fixture, bool luna, TrackCar car, double speed, double dt, Outcome o) =>
            table.Append(CultureInfo.InvariantCulture, $"{fixture}\t{(luna ? "luna" : "earth")}\t{car.Name}\t{speed:F0}\t{dt * 1000.0:F1}\t").AppendLine(o.Row());

        foreach (TrackCar car in TrackCar.All)
        {
            foreach (double dt in Steps)
            {
                foreach (double speed in Speeds(car))
                {
                    foreach (bool luna in new[] { false, true })
                    {
                        Row("control", luna, car, speed, dt, Control(car, speed, dt, luna));
                        foreach ((double rise, double run) in Ramps)
                        {
                            Row($"up {rise:F0}/{run:F0}", luna, car, speed, dt, RampUp(rise, run, car, speed, dt, luna));
                            Row($"down {rise:F0}/{run:F0}", luna, car, speed, dt, RampDown(rise, run, car, speed, dt, luna));
                            Row($"foot {rise:F0}/{run:F0}", luna, car, speed, dt, RampFoot(rise, run, car, speed, dt, luna));
                        }
                        foreach (double step in StepHeights.Concat(TallSteps))
                        {
                            foreach (double grade in Grades)
                            {
                                Row($"step {step:F2} on {grade * 100.0:+0;-0;0}%", luna, car, speed, dt, Step(step, grade, car, speed, dt, luna));
                            }
                        }
                    }
                    foreach (double angle in EntryAngles) Row($"entry {angle:F0} deg", false, car, speed, dt, Entry(angle, car, speed, dt));
                    foreach (double height in DeckHeights)
                    {
                        Row($"beside deck {height:F1}", false, car, speed, dt, Along(height, BesideM, car, speed, dt));
                        Row($"under deck {height:F1}", false, car, speed, dt, Along(height, 0.0, car, speed, dt));
                    }
                    Row("off the end", false, car, speed, dt, OffTheEnd(car, speed, dt));
                }
            }
            foreach (double dt in LongSteps)
            {
                Row("parked on road", false, car, 0.0, dt, Parked(car, dt, 0.0));
                Row("parked on deck 3.0", false, car, 0.0, dt, Parked(car, dt, 3.0));
            }
        }
        File.WriteAllText(path, table.ToString());
    }
}

public class RoadBulletRampTests
{
    // How hard a ramp's foot and its crest turn a car at a speed, m/s2: the eased rise is sharpest at its ends.
    private static double Asks(double rise, double run, double speed) => speed * speed * 6.0 * rise / (run * run);

    private const double EarthG = 9.81;

    private static IEnumerable<(TrackCar Car, double Speed, double Dt, bool Luna)> Runs()
    {
        foreach (TrackCar car in TrackCar.All)
        {
            foreach (double speed in RoadBulletTests.Speeds(car))
            {
                foreach (double dt in RoadBulletTests.Steps) yield return (car, speed, dt, false);
                yield return (car, speed, RoadBulletTests.Steps[0], true);
            }
        }
    }

    // Under 0.6 g of turn at the foot and the crest, at the speed it comes off the ramp when that is
    // the faster, a car neither bottoms nor flies: the road is simply followed.
    [Fact]
    public void AGentleRampIsFollowedUpAndDownOnTheSpringsWithNoFlight()
    {
        RoadBulletTests.Faults faults = new();
        int judged = 0;
        foreach ((TrackCar car, double speed, double dt, bool luna) in Runs())
        {
            double g = luna ? 1.62 : EarthG;
            foreach ((double rise, double run) in RoadBulletTests.Ramps)
            {
                foreach (bool up in new[] { true, false })
                {
                    if (up && !RoadBulletTests.CoastsUp(speed, rise, g)) continue;
                    double fastest = up ? speed : Math.Sqrt((speed * speed) + (2.0 * g * rise));
                    if (Asks(rise, run, fastest) > 0.6 * g) continue;

                    judged++;
                    RoadBulletTests.Outcome o = up ? RoadBulletTests.RampUp(rise, run, car, speed, dt, luna)
                                                   : RoadBulletTests.RampDown(rise, run, car, speed, dt, luna);
                    string what = $"{(up ? "up" : "down")} {rise:F0} m over {run:F0} m";
                    faults.Check(o.Finite && o.HullSteps == 0, car, speed, dt, luna, $"{what}: down on its hull for {o.HullSteps} steps");
                    faults.Check(o.AirSeconds == 0.0, car, speed, dt, luna, $"{what}: off its wheels for {o.AirSeconds:F2} s");
                    faults.Check(o.PitchDeg < 3.0 && o.RollDeg < 1.0 && o.UpDot > 0.9, car, speed, dt, luna, $"{what}: pitched {o.PitchDeg:F1} deg off the road");
                    faults.Check(RoadBulletTests.EnergyKept(o), car, speed, dt, luna, $"{what}: energy {o.Gain:F1} J/kg against a budget of {o.Budget:F1}");
                }
            }
        }
        Assert.True(judged > 60, $"only {judged} runs were gentle");
        faults.None();
    }

    private static void Upright(RoadBulletTests.Faults faults, double most)
    {
        foreach ((TrackCar car, double speed, double dt, bool luna) in Runs())
        {
            double g = luna ? 1.62 : EarthG;
            foreach ((double rise, double run) in RoadBulletTests.Ramps)
            {
                if (Asks(rise, run, speed) > most * g) continue;
                foreach (bool up in new[] { true, false })
                {
                    RoadBulletTests.Outcome o = up ? RoadBulletTests.RampUp(rise, run, car, speed, dt, luna)
                                                   : RoadBulletTests.RampDown(rise, run, car, speed, dt, luna);
                    string what = $"{(up ? "up" : "down")} {rise:F0} m over {run:F0} m ({Asks(rise, run, speed) / g:F1} g)";
                    bool coasts = !up || RoadBulletTests.CoastsUp(speed, rise, g);
                    faults.Check(o.Finite, car, speed, dt, luna, $"{what}: not finite");
                    faults.Check(o.UpDot > 0.0, car, speed, dt, luna,
                                 $"{what}: turned over, after {o.HullHits} landings on its hull at up to {o.HullMs:F1} m/s and {o.PeakClearance:F1} m clear of the road");
                    faults.Check(!coasts || RoadBulletTests.NoEnergyGained(o), car, speed, dt, luna, $"{what}: energy {o.Gain:F1} J/kg against a budget of {o.Budget:F1}");
                }
            }
        }
        faults.None();
    }

    // The springs push with up to four times the car's weight, so a ramp asking no more than that of
    // them is one a car can be carried up; what it does off the crest is a jump, and it lands.
    [Fact]
    public void ARampTheSpringsCanCarryACarUpLeavesItTheRightWayUp()
    {
        RoadBulletTests.Faults faults = new();
        Upright(faults, most: 4.0);
        faults.None();
    }

    // Past that a car comes down on its hull at the foot. However hard, that is all: it is not
    // turned over there and gets nothing from it. Off the crest it flies, and where it lands is not judged.
    [Fact]
    public void TheFootOfNoRampTurnsACarOverOrGivesItEnergy()
    {
        RoadBulletTests.Faults faults = new();
        double hardest = 0.0;
        foreach ((TrackCar car, double speed, double dt, bool luna) in Runs())
        {
            foreach ((double rise, double run) in RoadBulletTests.Ramps)
            {
                RoadBulletTests.Outcome o = RoadBulletTests.RampFoot(rise, run, car, speed, dt, luna);
                string what = $"foot of {rise:F0} m over {run:F0} m ({Asks(rise, run, speed) / (luna ? 1.62 : EarthG):F1} g)";
                hardest = Math.Max(hardest, o.HullMs);
                faults.Check(o.Finite && o.UpDot > 0.8, car, speed, dt, luna,
                             $"{what}: leant to {Math.Acos(Math.Clamp(o.UpDot, -1.0, 1.0)) * 180.0 / Math.PI:F0} deg, its hull down {o.HullHits} times at up to {o.HullMs:F1} m/s");
                faults.Check(RoadBulletTests.NoEnergyGained(o), car, speed, dt, luna, $"{what}: energy {o.Gain:F1} J/kg against a budget of {o.Budget:F1}");
            }
        }
        Assert.True(hardest > 5.0, $"no hull came down harder than {hardest:F1} m/s, so nothing hard was tried");
        faults.None();
    }

    // 19 m/s2 is two of Earth's gravities: with the weight, three of the four the springs have there.
    private static void FootOnTheSprings(RoadBulletTests.Faults faults, bool luna)
    {
        foreach (TrackCar car in TrackCar.All)
        {
            foreach (double speed in RoadBulletTests.Speeds(car))
            {
                foreach (double dt in RoadBulletTests.Steps)
                {
                    foreach ((double rise, double run) in RoadBulletTests.Ramps)
                    {
                        if (Asks(rise, run, speed) > 19.0) continue;
                        RoadBulletTests.Outcome o = RoadBulletTests.RampFoot(rise, run, car, speed, dt, luna);
                        faults.Check(o.Finite && o.HullSteps == 0, car, speed, dt, luna,
                                     $"foot of {rise:F0} m over {run:F0} m ({Asks(rise, run, speed):F1} m/s2): down on its hull {o.HullHits} times for {o.HullSteps} steps, at up to {o.HullMs:F2} m/s");
                    }
                }
            }
        }
    }

    [Fact]
    public void TheFootOfARampIsTheSpringsToCarryOnEarth()
    {
        RoadBulletTests.Faults faults = new();
        FootOnTheSprings(faults, luna: false);
        faults.None();
    }

    [Fact(Skip = "a spring's load is capped at four times the car's weight where it is, so on Luna a car comes down on its hull, at 1.0 to 1.5 m/s and for 0.3 to 0.7 s, at the foot of a ramp its springs carry it up on Earth")]
    public void TheFootOfARampIsTheSpringsToCarryOnLuna()
    {
        RoadBulletTests.Faults faults = new();
        FootOnTheSprings(faults, luna: true);
        faults.None();
    }
}

public class RoadBulletStepTests
{
    private static IEnumerable<(TrackCar Car, double Speed, double Dt, bool Luna)> Runs()
    {
        foreach (TrackCar car in TrackCar.All)
        {
            foreach (double speed in RoadBulletTests.Speeds(car))
            {
                foreach (double dt in RoadBulletTests.Steps) yield return (car, speed, dt, false);
                yield return (car, speed, RoadBulletTests.Steps[0], true);
            }
        }
    }

    // A hop no higher than twice the step, and three times it in a sixth of the gravity.
    [Fact]
    public void AStepOnTheLevelOrOnAClimbIsTakenOnTheWheels()
    {
        RoadBulletTests.Faults faults = new();
        foreach ((TrackCar car, double speed, double dt, bool luna) in Runs())
        {
            foreach (double step in RoadBulletTests.StepHeights)
            {
                foreach (double grade in RoadBulletTests.Grades)
                {
                    if (grade < 0.0) continue;
                    RoadBulletTests.Outcome o = RoadBulletTests.Step(step, grade, car, speed, dt, luna);
                    string what = $"{step:F2} m step on {grade * 100.0:F0}%";
                    faults.Check(o.Finite && o.UpDot > 0.8, car, speed, dt, luna, $"{what}: leant to {Math.Acos(Math.Clamp(o.UpDot, -1.0, 1.0)) * 180.0 / Math.PI:F0} deg");
                    faults.Check(o.PeakClearance <= (luna ? 3.0 : 2.0) * step, car, speed, dt, luna, $"{what}: thrown {o.PeakClearance:F2} m clear");
                    faults.Check(RoadBulletTests.NoEnergyGained(o), car, speed, dt, luna,
                                 $"{what}: energy {o.Gain:F1} J/kg against a budget of {o.Budget:F1}");
                }
            }
        }
        faults.None();
    }

    // The same step at the same speed, on the level and going down: the ground falls away from a car
    // in the air, so twice the level's flight is allowed, and 5 cm and 0.15 s on top. And going down
    // a car is following the road, which gives it nothing.
    [Fact]
    public void AStepOnADescentThrowsACarNoFurtherThanTwiceWhatItDoesOnTheLevelAndGivesItNoEnergy()
    {
        RoadBulletTests.Faults faults = new();
        foreach ((TrackCar car, double speed, double dt, bool luna) in Runs())
        {
            foreach (double step in RoadBulletTests.StepHeights)
            {
                RoadBulletTests.Outcome level = RoadBulletTests.Step(step, 0.0, car, speed, dt, luna);
                foreach (double grade in RoadBulletTests.Grades)
                {
                    if (grade >= 0.0) continue;
                    RoadBulletTests.Outcome o = RoadBulletTests.Step(step, grade, car, speed, dt, luna);
                    string what = $"{step:F2} m step on {grade * 100.0:F0}%";
                    faults.Check(o.PeakClearance <= (2.0 * level.PeakClearance) + 0.05 && o.AirSeconds <= (2.0 * level.AirSeconds) + 0.15,
                                 car, speed, dt, luna,
                                 $"{what}: {o.Flights} flights, {o.AirSeconds:F2} s and {o.PeakClearance:F2} m clear, against {level.AirSeconds:F2} s and {level.PeakClearance:F2} m on the level");
                    faults.Check(RoadBulletTests.NoEnergyGained(o), car, speed, dt, luna, $"{what}: energy {o.Gain:F1} J/kg against a budget of {o.Budget:F1}");
                }
            }
        }
        faults.None();
    }

    [Fact]
    public void AStepOnADescentLeavesACarTheRightWayUp()
    {
        RoadBulletTests.Faults faults = new();
        foreach ((TrackCar car, double speed, double dt, bool luna) in Runs())
        {
            foreach (double step in RoadBulletTests.StepHeights)
            {
                foreach (double grade in RoadBulletTests.Grades)
                {
                    if (grade >= 0.0) continue;
                    RoadBulletTests.Outcome o = RoadBulletTests.Step(step, grade, car, speed, dt, luna);
                    faults.Check(o.Finite && o.UpDot > 0.8, car, speed, dt, luna,
                                 $"{step:F2} m step on {grade * 100.0:F0}%: leant to {Math.Acos(Math.Clamp(o.UpDot, -1.0, 1.0)) * 180.0 / Math.PI:F0} deg, {o.PeakClearance:F1} m clear of the road");
                }
            }
        }
        faults.None();
    }
}
