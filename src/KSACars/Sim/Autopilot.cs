using Brutal.Numerics;

namespace KSACars;

/// <summary>Why a lap is over, or that it is not.</summary>
public enum LapEnd
{
    Running,

    /// <summary>The end of the route, or the laps asked for.</summary>
    Finished,

    /// <summary>Every wheel off the asphalt for <see cref="Autopilot.OffRoadSeconds"/>.</summary>
    OffRoad,
    Flipped,

    /// <summary>No metre of progress in <see cref="Autopilot.StuckSeconds"/>.</summary>
    Stuck,
    TimedOut,

    /// <summary>The driver itself threw; <see cref="Autopilot.Why"/> has what.</summary>
    Failed,

    /// <summary>Told to, from outside.</summary>
    Stopped,

    /// <summary>The car is no longer being stepped.</summary>
    NotDriven,

    /// <summary>The roads were laid again under it.</summary>
    RoadsRelaid,
}

/// <summary>
/// What a lap came to. Distances in metres, speeds in m/s, times in simulated seconds. A hub's height
/// is over what is under it, less its tyre's radius: nothing with the tyre just touching, negative
/// with the spring pushed up.
/// </summary>
public struct LapSummary
{
    public int Laps, Steps, BrakeToggles, Fumbles;
    public double Seconds, DistanceM, ProgressM, LastLapSeconds;
    public double MaxCrossM, MeanCrossM, MaxSpeed;
    public double AirSeconds, LongestFlightSeconds, OffAsphaltSeconds;

    /// <summary>Seconds with the hull on something, where the caller knows: only the tyres are meant to be.</summary>
    public double HullSeconds;
    public double MinHubM, MaxHubM, MaxRollDeg, MaxPitchDeg, LongestStepSeconds;
}

/// <summary>One step of a lap, as it began.</summary>
public struct LapRow
{
    public double T, Dt, S, Cross, Speed, Wanted, Throttle, Steer, RollDeg, PitchDeg;
    public float Hub0, Hub1, Hub2, Hub3;

    /// <summary>A bit a wheel: set in the one where its tyre is on something, in the other where it is not over asphalt.</summary>
    public int Grounded, OffAsphalt;
    public bool HullDown;
    public double3 Position;

    public const string Header = "t,dt,s,cross,speed,wanted,throttle,steer,hub0,hub1,hub2,hub3,grounded,off_asphalt,roll_deg,pitch_deg,x,y,z,hull";

    public readonly void AppendTo(System.Text.StringBuilder to)
    {
        System.Globalization.CultureInfo plain = System.Globalization.CultureInfo.InvariantCulture;
        to.Append(plain, $"{T:F4},{Dt:F5},{S:F3},{Cross:F3},{Speed:F3},{Wanted:F2},{Throttle:F3},{Steer:F4},");
        to.Append(plain, $"{Hub0:F4},{Hub1:F4},{Hub2:F4},{Hub3:F4},{Grounded},{OffAsphalt},{RollDeg:F2},{PitchDeg:F2},");
        to.Append(plain, $"{Position.X:F3},{Position.Y:F3},{Position.Z:F3},{(HullDown ? 1 : 0)}").Append('\n');
    }
}

/// <summary>
/// A driver that follows a <see cref="Route"/>: each step it reads where the car is and answers the
/// throttle and steering for it, and it ends itself, with a reason, when the route is done or the
/// run has gone wrong.
///
/// <para>It steers by pure pursuit from the rear axle, aiming at the route a look-ahead further on,
/// and asks for a front-wheel angle, which it turns into a steer input through
/// <see cref="BuggyDrive.SteerLock"/>: the input is a share of a lock that shrinks with speed.</para>
///
/// <para>It holds the least of the speed asked for and what the road allows, braked for in time. A
/// bend allows what <see cref="GripShare"/> of the lesser of the front tyres' grip and what full lock
/// asks of them holds, on the weight and the wings together, and no more than <see cref="TipShare"/>
/// of what would lift the inside wheels. A crest allows what leaves <see cref="CrestShare"/> of the
/// weight to be thrown off, and a dip what pushes the springs <see cref="DipShare"/> of the way to
/// their stops, unless it <see cref="Jumps"/>. The brake is all or nothing, so it is worked with a gap
/// between on and off, and not below <see cref="LeastBrakeMs"/>, where a negative throttle is
/// reverse, until the end of a road, where the car is braked to a stand and the lap is over.</para>
///
/// <para>Everything is in the body's own frame, where the ground does not move. Nothing here keeps
/// time or state outside itself, so the same run is the same run twice.</para>
/// </summary>
internal sealed class Autopilot
{
    public const double OffRoadSeconds = 1.0, FlippedSeconds = 1.0, StuckSeconds = 5.0;

    /// <summary>The share of the cornering a car has that a bend is taken at.</summary>
    public const double GripShare = 0.85;

    /// <summary>The share of the brakes a bend is braked for with, the rest being spare.</summary>
    public const double BrakeShare = 0.7;

    /// <summary>The share of the sideways pull that would lift a car's inside wheels that a bend is taken at.</summary>
    public const double TipShare = 0.6;

    /// <summary>The share of its weight a car is let throw off over a crest.</summary>
    public const double CrestShare = 0.6;

    /// <summary>The share of its springs' way to their stops a dip is let push them, over where they ride.</summary>
    public const double DipShare = 0.35;

    /// <summary>What a car's tightest turn is taken as being wider than, for the line through a kink.</summary>
    public const double TurnMargin = 1.25;

    /// <summary>Seconds of travel the steering aims ahead by.</summary>
    public const double LookAheadSeconds = 0.4;

    public const double LeastLookAheadM = 4.0, MostLookAheadM = 60.0;

    /// <summary>Below this a negative throttle is not asked for, short of the stop at a road's end.</summary>
    public const double LeastBrakeMs = 1.5;

    // The brake comes on this far over the speed wanted, or this share of it if that is more, and goes
    // off this far over it.
    private const double BrakeOnMs = 0.3, BrakeOnShare = 0.04, BrakeOffMs = 0.1;

    // The speed wanted is read this far on, in seconds, so the brake's gap is not all above it: a car
    // that goes into a bend over what the bend allows has no grip left to brake with.
    private const double BrakeLeadSeconds = 0.1;

    // Full throttle from this far under the speed wanted.
    private const double FullThrottleUnderMs = 2.0;

    // The speed held off the road or pointing away from the route, and the least any bend is taken at.
    private const double RecoverMs = 5.0, CrawlMs = 2.0;

    // The speed the end of a road is come up to, over the last few metres; how far short of the end the
    // front wheels are when it is braked to a stop; and the speed at which it is let go, to stand. The
    // first is well over the metre a second under which a car with its throttle shut is held by its
    // tyres as a parked one is, which a car still on its way must not be.
    private const double ArriveMs = 2.0, ArriveOverM = 5.0, ShortOfEndM = 2.5, StoppedMs = 0.7;

    // The end is braked for no harder than this, g, over the last of the road: a soft car stopped hard
    // has its nose down on the road when it gets there.
    private const double GentleBrakeG = 0.3, ArriveBrakeOverM = 40.0;

    // The share of what a dip is let push the springs past which it is braked through no harder than that either.
    private const double DipBrakeShare = 0.3;

    private readonly BuggyProfile _profile;
    private readonly Route _route;
    private readonly RoadSurface? _road;
    private readonly double _mass, _gravity, _air, _wheelbase, _stopAt, _timeout, _inside;
    private readonly int _lapsWanted;
    private readonly LapRow[]? _rows;
    private double[]? _limit;
    private (double HalfTrackM, double ComHeightM)? _stance;

    private LapSummary _summary;
    private int _index = -1;
    private double _s, _cross, _side;
    private bool _braking;
    private double _crossSum, _flight, _offFor, _flippedFor, _markProgress, _markTime, _lapStarted;

    /// <param name="road">The roads, to tell asphalt from shoulder; with none, no wheel is ever counted off it.</param>
    /// <param name="gravity">What presses the tyres, m/s2, without the wings.</param>
    /// <param name="cruiseMs">The speed to hold where no bend asks for less; nothing or less is the car's own top speed.</param>
    /// <param name="rows">Filled a step at a time until it is full, or null to keep none.</param>
    public Autopilot(BuggyProfile profile, Route route, RoadSurface? road, double massKg, double gravity, double airDensity,
                     int laps = 1, double cruiseMs = 0.0, double timeoutSeconds = 600.0, LapRow[]? rows = null)
    {
        _profile = profile;
        _route = route;
        _road = road;
        _mass = massKg;
        _gravity = gravity;
        _air = airDensity;
        _lapsWanted = Math.Max(laps, 1);
        _timeout = timeoutSeconds;
        _rows = rows;
        _wheelbase = BuggyDrive.Wheelbase(profile);
        _inside = RaceInside(profile);
        CruiseMs = cruiseMs > 0.0 ? Math.Min(cruiseMs, profile.TopSpeed) : profile.TopSpeed;

        _stopAt = route.Closed ? double.PositiveInfinity : Math.Max(route.LengthM - _wheelbase - ShortOfEndM, 0.0);
        _summary.MinHubM = double.PositiveInfinity;
        _summary.MaxHubM = double.NegativeInfinity;
    }

    public double CruiseMs { get; }

    /// <summary>
    /// The driver's own settings, each at what the constant of its name says unless a lap is given
    /// another: what a search for a faster lap turns.
    /// </summary>
    public sealed record Tuning
    {
        public double GripShare { get; init; } = Autopilot.GripShare;
        public double BrakeShare { get; init; } = Autopilot.BrakeShare;
        public double TipShare { get; init; } = Autopilot.TipShare;
        public double CrestShare { get; init; } = Autopilot.CrestShare;
        public double DipShare { get; init; } = Autopilot.DipShare;
        public double LookAheadSeconds { get; init; } = Autopilot.LookAheadSeconds;
        public double LeastLookAheadM { get; init; } = Autopilot.LeastLookAheadM;
        public double MostLookAheadM { get; init; } = Autopilot.MostLookAheadM;
        public double BrakeOnMs { get; init; } = Autopilot.BrakeOnMs;
        public double BrakeOnShare { get; init; } = Autopilot.BrakeOnShare;
        public double BrakeOffMs { get; init; } = Autopilot.BrakeOffMs;
        public double BrakeLeadSeconds { get; init; } = Autopilot.BrakeLeadSeconds;
        public double FullThrottleUnderMs { get; init; } = Autopilot.FullThrottleUnderMs;
        public double DipBrakeShare { get; init; } = Autopilot.DipBrakeShare;

        /// <summary>How far over the speed wanted the brake is pressed all the way, m/s, and the least it is pressed while on; no band, and it is all or nothing.</summary>
        public double BrakeBandMs { get; init; }

        public double LeastBrake { get; init; } = 0.1;

        /// <summary>
        /// How much of the car the driver's plan knows. 0, a stop is planned at a share of the brakes'
        /// own g, taken from what the bend leaves of its share of the grip, and the pedals are full on
        /// or off either side of the speed wanted. 1, a stop is planned on what presses the tyres,
        /// wings and all, and on the tyres' own circle, with the air's drag counted: a car with wings
        /// stops several times harder at speed than its brakes' g says, and one in a bend still has
        /// brakes. 2, and the pedals are pressed as far as gives the acceleration the plan asks.
        /// </summary>
        public double Model { get; init; }

        /// <summary>With <see cref="Model"/> 1, the share of the tyres' grip a stop in a bend is planned on.</summary>
        public double TyreShare { get; init; } = 0.9;

        /// <summary>With <see cref="Model"/> 2, how soon the car is to be at the speed wanted, s: the pedals' gain.</summary>
        public double PedalSeconds { get; init; } = 0.12;

        /// <summary>What a bend's tightness is averaged over, m: a line's bend spikes for a few metres where a road's width changes, and the car does not turn that. Nothing, and each metre's own.</summary>
        public double CurveOverM { get; init; }

        /// <summary>
        /// What the speed each bend allows is multiplied by, at as many places evenly round the route as
        /// there are numbers here and between them in proportion: a lap learnt corner by corner, where
        /// one share of the grip for all of it is held to what its hardest corner bears. None, and it is 1.
        /// </summary>
        public IReadOnlyList<double> Pace { get; init; } = [];

        public double PaceAt(double share)
        {
            if (Pace.Count == 0) return 1.0;
            double at = (share - Math.Floor(share)) * Pace.Count;
            int i = (int)at % Pace.Count;
            return Pace[i] + ((Pace[(i + 1) % Pace.Count] - Pace[i]) * (at - Math.Floor(at)));
        }

        /// <summary>
        /// These settings a share of the way to <paramref name="other"/>'s, each on its own: nothing of
        /// the way is these and all of it the other's. A pace either has none of is 1.
        /// </summary>
        public Tuning Toward(Tuning other, double share)
        {
            double t = Math.Clamp(share, 0.0, 1.0);
            double Mix(double a, double b) => a + ((b - a) * t);
            int places = Math.Max(Pace.Count, other.Pace.Count);
            double[] pace = new double[places];
            for (int i = 0; i < places; i++)
            {
                double at = (i + 0.0) / places;
                pace[i] = Mix(PaceAt(at), other.PaceAt(at));
            }
            return new Tuning
            {
                GripShare = Mix(GripShare, other.GripShare), BrakeShare = Mix(BrakeShare, other.BrakeShare), TipShare = Mix(TipShare, other.TipShare),
                CrestShare = Mix(CrestShare, other.CrestShare), DipShare = Mix(DipShare, other.DipShare),
                LookAheadSeconds = Mix(LookAheadSeconds, other.LookAheadSeconds), LeastLookAheadM = Mix(LeastLookAheadM, other.LeastLookAheadM),
                MostLookAheadM = Mix(MostLookAheadM, other.MostLookAheadM), BrakeOnMs = Mix(BrakeOnMs, other.BrakeOnMs),
                BrakeOnShare = Mix(BrakeOnShare, other.BrakeOnShare), BrakeOffMs = Mix(BrakeOffMs, other.BrakeOffMs),
                BrakeLeadSeconds = Mix(BrakeLeadSeconds, other.BrakeLeadSeconds), FullThrottleUnderMs = Mix(FullThrottleUnderMs, other.FullThrottleUnderMs),
                DipBrakeShare = Mix(DipBrakeShare, other.DipBrakeShare), BrakeBandMs = Mix(BrakeBandMs, other.BrakeBandMs),
                LeastBrake = Mix(LeastBrake, other.LeastBrake), Pace = pace,
                Model = Math.Max(Model, other.Model), TyreShare = Mix(TyreShare, other.TyreShare), PedalSeconds = Mix(PedalSeconds, other.PedalSeconds), CurveOverM = Mix(CurveOverM, other.CurveOverM),
            };
        }

        /// <summary>These settings as <see cref="With"/> reads them, every one of them, so that a lap that is kept is the lap that was driven.</summary>
        public string Written()
        {
            string N(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            string all = $"grip_share={N(GripShare)},brake_share={N(BrakeShare)},tip_share={N(TipShare)},crest_share={N(CrestShare)},dip_share={N(DipShare)},"
                       + $"look_ahead_seconds={N(LookAheadSeconds)},least_look_ahead_m={N(LeastLookAheadM)},most_look_ahead_m={N(MostLookAheadM)},"
                       + $"brake_on_ms={N(BrakeOnMs)},brake_on_share={N(BrakeOnShare)},brake_off_ms={N(BrakeOffMs)},brake_lead_seconds={N(BrakeLeadSeconds)},"
                       + $"full_throttle_under_ms={N(FullThrottleUnderMs)},dip_brake_share={N(DipBrakeShare)},brake_band_ms={N(BrakeBandMs)},least_brake={N(LeastBrake)},"
                       + $"model={N(Model)},tyre_share={N(TyreShare)},pedal_seconds={N(PedalSeconds)},curve_over_m={N(CurveOverM)}";
            return Pace.Count == 0 ? all : all + ",pace=" + string.Join(';', Pace.Select(N));
        }

        /// <summary>The settings named in "grip_share=0.95,brake_lead_seconds=0.05", the rest as they are here; null with the name that is not one.</summary>
        public Tuning? With(string settings, out string why)
        {
            why = "";
            Tuning tuned = this;
            foreach (string setting in settings.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                string[] sides = setting.Split('=');
                if (sides.Length == 2 && sides[0].Trim().ToLowerInvariant() == "pace")
                {
                    List<double> pace = [];
                    foreach (string one in sides[1].Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!double.TryParse(one, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double p) || !(p > 0.0))
                        {
                            why = $"pace: '{one}' is not a number over nothing";
                            return null;
                        }
                        pace.Add(p);
                    }
                    tuned = tuned with { Pace = pace };
                    continue;
                }
                if (sides.Length != 2 || !double.TryParse(sides[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v))
                {
                    why = $"'{setting}' is not name=number";
                    return null;
                }
                switch (sides[0].Trim().Replace("_", "").ToLowerInvariant())
                {
                    case "gripshare": tuned = tuned with { GripShare = v }; break;
                    case "brakeshare": tuned = tuned with { BrakeShare = v }; break;
                    case "tipshare": tuned = tuned with { TipShare = v }; break;
                    case "crestshare": tuned = tuned with { CrestShare = v }; break;
                    case "dipshare": tuned = tuned with { DipShare = v }; break;
                    case "lookaheadseconds": tuned = tuned with { LookAheadSeconds = v }; break;
                    case "leastlookaheadm": tuned = tuned with { LeastLookAheadM = v }; break;
                    case "mostlookaheadm": tuned = tuned with { MostLookAheadM = v }; break;
                    case "brakeonms": tuned = tuned with { BrakeOnMs = v }; break;
                    case "brakeonshare": tuned = tuned with { BrakeOnShare = v }; break;
                    case "brakeoffms": tuned = tuned with { BrakeOffMs = v }; break;
                    case "brakeleadseconds": tuned = tuned with { BrakeLeadSeconds = v }; break;
                    case "fullthrottleunderms": tuned = tuned with { FullThrottleUnderMs = v }; break;
                    case "dipbrakeshare": tuned = tuned with { DipBrakeShare = v }; break;
                    case "brakebandms": tuned = tuned with { BrakeBandMs = v }; break;
                    case "leastbrake": tuned = tuned with { LeastBrake = v }; break;
                    case "model": tuned = tuned with { Model = v }; break;
                    case "tyreshare": tuned = tuned with { TyreShare = v }; break;
                    case "pedalseconds": tuned = tuned with { PedalSeconds = v }; break;
                    case "curveoverm": tuned = tuned with { CurveOverM = v }; break;
                    default:
                        why = $"'{sides[0]}' is not one of the driver's settings";
                        return null;
                }
            }
            return tuned;
        }
    }

    public Tuning Tune { get; init; } = new();

    /// <summary>
    /// A driver who is not the same every lap, for a seed: every bend of a lap slower by a share drawn
    /// for that lap, half a normal of <paramref name="LapSpread"/> and never quicker, and with a chance
    /// of <paramref name="ExitChance"/> a lap the throttle eased out of one bend.
    /// </summary>
    public sealed record Human(int Seed, double LapSpread, double ExitChance);

    /// <summary>The driver as a person, or null for one who drives every lap alike.</summary>
    public Human? Person { get; init; }

    /// <summary>The throttle a fluffed exit is held to, and the least and most seconds it lasts.</summary>
    public const double FumbleThrottle = 0.4, FumbleLeastSeconds = 0.5, FumbleMostSeconds = 1.2;

    private double _lapPace = 1.0, _fumbleAt = double.PositiveInfinity, _fumbleUntil = double.NegativeInfinity, _fumbleSeconds;
    private int _fumbleLap = -1;

    // What this lap is to the person driving it: drawn from its seed and the lap's number, so the same driver has the same laps.
    private void Roll()
    {
        if (Person is not { } person) return;
        int lap = _summary.Laps;
        Random dice = new(unchecked((person.Seed * 486187739) + (lap * 7919) + 17));
        double normal = Math.Sqrt(-2.0 * Math.Log(1.0 - dice.NextDouble())) * Math.Cos(2.0 * Math.PI * dice.NextDouble());
        _lapPace = Math.Clamp(1.0 - (person.LapSpread * Math.Abs(normal)), 1.0 - (3.0 * person.LapSpread), 1.0);
        (_fumbleAt, _fumbleLap) = dice.NextDouble() < person.ExitChance ? (dice.NextDouble() * _route.LengthM, lap) : (double.PositiveInfinity, -1);
        _fumbleSeconds = FumbleLeastSeconds + ((FumbleMostSeconds - FumbleLeastSeconds) * dice.NextDouble());
    }

    /// <summary>How hard the car brakes at a speed, m/s2, all its brakes on the weight and the wings: what a car ahead is braked for with.</summary>
    public double BrakeAt(double speedMs) => _profile.BrakeG * (_gravity + BuggyDrive.WingLoad(_profile, speedMs, _air, _mass));

    /// <summary>Whether crests and dips are taken at whatever the bends allow, to see what a car does in the air.</summary>
    public bool Jumps { get; init; }

    /// <summary>
    /// How hard the lap is driven, from 0 for the shares above, which leave room for a road that is
    /// not what it was taken to be, to 1 for nearly all the car has: a lap for the watch.
    /// </summary>
    public double Push { get; init; }

    /// <summary>Stretches of the route, in metres along it, where a crest or a dip is not slowed for: a jump that is meant.</summary>
    public IReadOnlyList<(double From, double To)> JumpZones { get; init; } = [];

    /// <summary>
    /// How long every wheel may be off the asphalt before the lap ends for it. Off the road the driver
    /// makes for its line at a crawl, so one given longer drives itself back on.
    /// </summary>
    public double OffRoadFor { get; init; } = OffRoadSeconds;

    /// <summary>
    /// How far on along the route from this car a place is, and how far to its left, looking no
    /// further than <paramref name="reachM"/>: what says a car round a bend is in the way, where a
    /// straight line from one to the other says it is off to one side. False before the first step.
    /// </summary>
    public bool Along(double3 point, double reachM, out double aheadM, out double sideM)
    {
        (aheadM, sideM) = (0.0, 0.0);
        int at = _index;
        if (at < 0) return false;
        _route.Locate(point, ref at, reachM, out double s, out double cross);
        aheadM = s - _s;
        if (_route.Closed && aheadM < -0.5 * _route.LengthM) aheadM += _route.LengthM;
        sideM = cross - _cross;
        return true;
    }

    /// <summary>Told that the car is standing because it is held back, so that it is not taken for stuck.</summary>
    public void Waiting() => _markTime = _summary.Seconds;

    /// <summary>The most the driver goes at whatever the road allows, for a car in its way. Set from outside while it drives.</summary>
    public double CapMs { get; set; } = double.PositiveInfinity;

    /// <summary>
    /// How far to the left of its route the driver is to run, m, to go round a car. Set from outside
    /// while it drives; the driver moves across no faster than <see cref="SideRateMs"/> and no
    /// further than leaves <see cref="RaceInside"/> of asphalt outside it.
    /// </summary>
    public double SideM { get; set; }

    /// <summary>How fast the line is moved across the road, m/s, and the most of the car's speed that is.</summary>
    public const double SideRateMs = 2.0, SideSlope = 0.08, SideSlowMs = 12.0, SideSlopeSlow = 0.35;

    /// <summary>How far to the left of its route the driver is aiming now.</summary>
    public double SideNowM => _side;

    /// <summary>How far to the left of its route the car is.</summary>
    public double CrossM => _cross;

    /// <summary>The speed the driver would hold now with no car in its way, m/s.</summary>
    public double FreeMs { get; private set; }

    // The plan's speeds are for the route's own bends, and a line beside it is tighter on one side
    // and joined by a swerve: a bend is taken slower by twice the share the offset is of its radius.
    private const double SideBendShare = 2.0, SideLeastShare = 0.5;

    /// <summary>How far on a stretch with no bend in it is looked for.</summary>
    public const double StraightLookM = 600.0;

    /// <summary>
    /// The road ahead for a car that would leave its line: how far to the left and to the right of
    /// the route its middle may go over the next <paramref name="overM"/>, and how far on the route
    /// first bends by more than <paramref name="straightPerM"/>. False before the first step.
    /// </summary>
    public bool RoadAhead(double overM, double straightPerM, out double leftM, out double rightM, out double straightM)
    {
        (leftM, rightM, straightM) = (0.0, 0.0, 0.0);
        int at = _index;
        if (at < 0) return false;
        ReadOnlySpan<Route.Sample> samples = _route.Samples;
        (leftM, rightM) = (double.PositiveInfinity, double.PositiveInfinity);
        bool bent = false;
        double gone = 0.0;
        for (int i = at, guard = 0; guard < samples.Length && gone <= StraightLookM && (!bent || gone <= overM); guard++)
        {
            Route.Sample s = samples[i];
            if (gone <= overM)
            {
                (double lo, double hi) = Room(s);
                (leftM, rightM) = (Math.Min(leftM, hi), Math.Min(rightM, -lo));
            }
            bent |= Math.Abs(s.Curvature) > straightPerM || s.LoopM > 0.0;
            if (!bent) straightM = gone;
            int next = i + 1 < samples.Length ? i + 1 : 0;
            if (next == 0 && !_route.Closed) break;
            gone += Vec.Len(samples[next].At - s.At);
            i = next;
        }
        return true;
    }

    // How far right and left of the route a car's middle may be at a sample; the route itself always may.
    private (double Lo, double Hi) Room(in Route.Sample s) =>
        (Math.Min(s.CentreM - s.HalfWidth + _inside, 0.0), Math.Max(s.CentreM + s.HalfWidth - _inside, 0.0));

    private double Pushed(double share, double most) => share + ((most - share) * Math.Clamp(Push, 0.0, 1.0));

    private bool JumpsAt(double s) => Jumps || JumpZones.Any(z => s >= z.From && s <= z.To);
    public LapEnd End { get; private set; }

    /// <summary>What went wrong, where the reason alone does not say.</summary>
    public string Why { get; private set; } = "";

    public int RowCount { get; private set; }

    public LapSummary Summary
    {
        get
        {
            LapSummary s = _summary;
            s.MeanCrossM = s.Seconds > 0.0 ? _crossSum / s.Seconds : 0.0;
            return s;
        }
    }

    /// <summary>The speed the route allows at each of its samples, m/s; nothing before the first step, which is where the car is weighed up.</summary>
    public ReadOnlySpan<double> Limit => _limit;

    /// <summary>
    /// <see cref="Limit"/> without a step driven, the car's track taken off its profile and nothing held
    /// back for tipping: for a line drawn for somebody else's driving.
    /// </summary>
    public ReadOnlySpan<double> Plan() => _limit ??= Limits(_profile.Corners.Max(c => Math.Abs(c.Hub.Z)), 0.0);

    /// <summary>How far inside a road's edge a car's racing line keeps its middle: half its track, a tyre and what the steering wanders by.</summary>
    public static double RaceInside(BuggyProfile profile) => profile.Corners.Max(c => Math.Abs(c.Hub.Z)) + RaceMarginM;

    public const double RaceMarginM = 0.9;

    /// <summary>The tightest turn a car is asked to make, m: what a route for it is drawn with.</summary>
    public static double TurnRadius(BuggyProfile profile) =>
        TurnMargin * BuggyDrive.Wheelbase(profile) / Math.Tan(Math.Max(profile.MaxSteerDeg, 1.0) * Math.PI / 180.0);

    /// <summary>Ends the lap from outside; one already over keeps the reason it has.</summary>
    public void Finish(LapEnd reason, string why = "")
    {
        if (End != LapEnd.Running) return;
        End = reason;
        Why = why;
    }

    /// <summary>
    /// The speed a loop is gone into at: by <see cref="LoopPace"/> over the least that still presses a
    /// car to the road at its top, which is that of a fall from 0.62 of the loop's length.
    /// </summary>
    public static double LoopSpeed(double loopM, double gravity) => LoopPace * Math.Sqrt(0.62 * gravity * loopM);

    public const double LoopPace = 1.12;

    private const int LoopNearSamples = 6;

    // What the road allows at each sample, and then back along the route what the brakes can get down
    // to it from. A bend: v^2 kappa = share x grip x what presses the tyres, which is g cos(slope), the
    // wings' press at v and, over a crest, less what the crest throws off.
    private double[] Limits(double halfTrackM, double comHeightM)
    {
        ReadOnlySpan<Route.Sample> samples = _route.Samples;
        double[] limit = new double[samples.Length];
        double grip = Pushed(Tune.GripShare, 1.0) * Math.Min(_profile.FrontGrip, _profile.FrontGrip * _profile.SteerOverGrip);
        if (comHeightM > 0.0) grip = Math.Min(grip, Pushed(Tune.TipShare, 0.85) * halfTrackM / comHeightM);
        double crestShare = Pushed(Tune.CrestShare, 0.92);
        double wings = _mass > 0.0 ? 0.5 * _air * _profile.DownforceAreaM2 / _mass : 0.0;

        // What a spring carries over its share of the weight at a compression is that times its rate.
        double spring = 2.0 * Math.PI * _profile.SpringHz;
        double dip = Pushed(Tune.DipShare, 0.8) * _profile.BumpTravel * spring * spring;
        double[] bend = new double[limit.Length];
        {
            int half = (int)Math.Round(0.5 * Tune.CurveOverM / Route.SpacingM);
            for (int i = 0; i < bend.Length; i++)
            {
                double sum = 0.0;
                int n = 0;
                for (int k = -half; k <= half; k++)
                {
                    int j = i + k;
                    if (_route.Closed) j = ((j % bend.Length) + bend.Length) % bend.Length;
                    else if (j < 0 || j >= bend.Length) continue;
                    sum += samples[j].Curvature;
                    n++;
                }
                bend[i] = n > 0 ? sum / n : samples[i].Curvature;
            }
        }
        double tyres = Tune.TyreShare * Math.Min(_profile.FrontGrip, _profile.RearGrip);
        double drag = _mass > 0.0 ? 0.5 * _air * _profile.DragAreaM2 / _mass : 0.0;
        for (int i = 0; i < limit.Length; i++)
        {
            Route.Sample s = samples[i] with { Curvature = bend[i] };
            double level = 1.0 / Math.Sqrt(1.0 + (s.Slope * s.Slope));
            bool jumps = JumpsAt(s.S);
            double crest = jumps ? 0.0 : Math.Max(-s.Vertical, 0.0);
            double held = Banked(grip, s);
            double allowed = Math.Min(CruiseMs, Under(held * _gravity * level, Math.Abs(s.Curvature) + (held * (crest - wings))));

            // Round a bend that leans into it the turn presses the car into the road as a dip does.
            double inward = -s.Bank * Math.Sign(s.Curvature);
            if (inward > 0.0)
            {
                // And never slower for the lean than the same bend would be taken level.
                double pressed = Under(dip, Math.Abs(s.Curvature) * inward / Math.Sqrt(1.0 + (inward * inward)));
                double flat = Under(grip * _gravity * level, Math.Abs(s.Curvature) + (grip * (crest - wings)));
                allowed = Math.Min(allowed, Math.Max(pressed, flat));
            }
            if (!jumps)
            {
                allowed = Math.Min(allowed, Under(crestShare * _gravity * level, crest - (crestShare * wings)));
                allowed = Math.Min(allowed, Under(dip, Math.Max(s.Vertical, 0.0)));
            }
            // A loop is gone into at what carries the car over its top and no faster: it presses by the square of the speed.
            if (s.LoopM > 0.0) allowed = Math.Min(CruiseMs, LoopSpeed(s.LoopM, _gravity));
            else if (allowed < CruiseMs) allowed = Math.Min(allowed * Tune.PaceAt(s.S / _route.LengthM) * _lapPace, CruiseMs);
            limit[i] = s.S >= _stopAt - ArriveOverM ? ArriveMs : Math.Max(allowed, CrawlMs);
        }

        // A tyre holds its line before it brakes, so what a bend leaves of the grip is all the brakes
        // have: into a bend the braking is done before the turn is. The car is taken to be the brake's
        // gap over the speed it is braked to. Twice round a closed route, so the bend after the line
        // slows the straight before it.
        double brakes = Pushed(Tune.BrakeShare, 0.92) * _profile.BrakeG * _gravity;
        int last = limit.Length - 1;
        for (int pass = 0; pass < (_route.Closed ? 2 : 1); pass++)
        {
            for (int i = _route.Closed ? last : last - 1; i >= 0; i--)
            {
                int next = (i + 1) % limit.Length;
                Route.Sample s = samples[i] with { Curvature = bend[i] };
                double piece = Vec.Len(samples[next].At - s.At);
                double level = 1.0 / Math.Sqrt(1.0 + (s.Slope * s.Slope));
                bool jumps = JumpsAt(s.S);
                double crest = jumps ? 0.0 : Math.Max(-s.Vertical, 0.0);
                // Through a dip too: the brakes' dive and the dip push the same front springs.
                bool gently = s.S >= _stopAt - ArriveBrakeOverM || (!jumps && s.Vertical * limit[next] * limit[next] > Tune.DipBrakeShare * dip);
                double most = gently ? Math.Min(brakes, GentleBrakeG * _gravity) : brakes;
                double from = limit[next];
                for (int again = 0; again < 2; again++)
                {
                    double over = from * (1.0 + Tune.BrakeOnShare);
                    double held = Banked(grip, s) * Math.Max((_gravity * level) + ((wings - crest) * over * over), 0.0);
                    double turning = over * over * Math.Abs(s.Curvature);
                    double spare = Math.Sqrt(Math.Max((held * held) - (turning * turning), 0.0));

                    // Gravity along the road is with the brakes up a hill and against them down one.
                    double slowing = Math.Max(Math.Min(most, spare) + (_gravity * s.Slope * level), 0.0);
                    if (Tune.Model >= 1.0)
                    {
                        // The brakes hold by what presses the tyres, wings and all, on the tyres' own circle, and the air slows the car too.
                        double press = Math.Max((_gravity * level) + ((wings - crest) * over * over), 0.0);
                        double circle = Banked(tyres, s) * press;
                        double left = Math.Sqrt(Math.Max((circle * circle) - (turning * turning), 0.0));
                        double pedal = Pushed(Tune.BrakeShare, 0.92) * _profile.BrakeG * press;
                        if (gently) pedal = Math.Min(pedal, GentleBrakeG * _gravity);
                        slowing = Math.Max(Math.Min(pedal, left) + (drag * over * over) + (_gravity * s.Slope * level), 0.0);
                    }
                    from = Math.Sqrt((limit[next] * limit[next]) + (2.0 * slowing * piece));
                }
                limit[i] = Math.Min(limit[i], from);
            }
        }
        return limit;
    }

    // What a bend leaves of the grip on a road that leans: less where it leans out of the bend, which
    // the car's weight slides down, and more where it leans into it.
    private static double Banked(double grip, in Route.Sample s)
    {
        double outward = s.Bank * Math.Sign(s.Curvature);
        return Math.Max((grip - outward) / Math.Max(1.0 + (grip * outward), 0.5), 0.05);
    }

    // The speed at which v^2 x per = most; any speed where nothing is asked.
    private static double Under(double most, double per) => per > 1e-9 ? Math.Sqrt(most / per) : double.PositiveInfinity;

    // Half the narrower axle's track, and how high the centre of mass rides with the tyres just touching.
    private (double HalfTrackM, double ComHeightM) Stance(ReadOnlySpan<double3> hubs, double3 up, double3 forward)
    {
        BuggyCorner[] corners = _profile.Corners;
        double3 left = Vec.Cross(up, forward);
        double front = 0.0, rear = 0.0, height = 0.0;
        int fronts = 0, rears = 0, n = Math.Min(corners.Length, hubs.Length);
        for (int i = 0; i < n; i++)
        {
            height += (corners[i].Radius - Vec.Dot(hubs[i], up)) / n;
            if (corners[i].Steers) { front += Math.Abs(Vec.Dot(hubs[i], left)); fronts++; }
            else { rear += Math.Abs(Vec.Dot(hubs[i], left)); rears++; }
        }
        if (fronts == 0 || rears == 0) return (0.0, 0.0);
        return (Math.Min(front / fronts, rear / rears), height);
    }

    /// <summary>
    /// One step: the throttle and steering for it, or neither once the lap is over. Asked after the
    /// ground under the wheels is read and before the drive is stepped.
    /// </summary>
    /// <param name="positionCcf">The centre of mass, from the body's centre, in the body's own frame.</param>
    /// <param name="velocityCcf">Its velocity over the ground, in that frame.</param>
    /// <param name="up">The car's own up, in its body frame, and <paramref name="forward"/> its forward.</param>
    /// <param name="hubs">Each corner's hub at rest, relative to the centre of mass, body frame.</param>
    /// <param name="hubHeights">Each hub's height over what is under it.</param>
    /// <param name="hullDown">Whether anything of the car but its tyres is on something, where that is known.</param>
    public DriveInput Step(double3 positionCcf, doubleQuat body2Ccf, double3 velocityCcf, double3 up, double3 forward,
                           ReadOnlySpan<double3> hubs, ReadOnlySpan<double> hubHeights, double dt, bool hullDown = false)
    {
        if (End != LapEnd.Running || !(dt > 0.0)) return default;
        try
        {
            return Drive(positionCcf, body2Ccf, velocityCcf, up, forward, hubs, hubHeights, dt, hullDown);
        }
        catch (Exception e)
        {
            Finish(LapEnd.Failed, e.Message);
            return default;
        }
    }

    private DriveInput Drive(double3 position, doubleQuat body2Ccf, double3 velocity, double3 up, double3 forward,
                             ReadOnlySpan<double3> hubs, ReadOnlySpan<double> hubHeights, double dt, bool hullDown)
    {
        BuggyCorner[] corners = _profile.Corners;
        _stance ??= Stance(hubs, up, forward);
        if (_limit is not { } limit)
        {
            if (Person is not null) Roll();
            _limit = limit = Limits(_stance.Value.HalfTrackM, _stance.Value.ComHeightM);
        }
        double3 radial = Vec.Unit(position);
        double3 ahead = body2Ccf * forward, top = body2Ccf * up;
        double3 left = Vec.Cross(top, ahead);
        double speed = Vec.Len(velocity), rolling = Vec.Dot(velocity, ahead);

        double3 axle = Vec.Zero;
        int rears = 0;
        for (int i = 0; i < corners.Length && i < hubs.Length; i++)
        {
            if (corners[i].Steers) continue;
            axle += hubs[i];
            rears++;
        }
        double3 rear = position + (body2Ccf * (rears > 0 ? axle / rears : Vec.Zero));

        if (_index < 0)
        {
            _index = _route.Nearest(rear);
            _route.Locate(rear, ref _index, 0.0, out double from, out _);
            _markProgress = from;
        }
        // Looked for as far on as three steps at this speed and ten metres, and no further: see Route.
        if (_route.Locate(rear, ref _index, 10.0 + (3.0 * Math.Abs(rolling) * dt), out double s, out double cross))
        {
            _summary.Laps++;
            _summary.LastLapSeconds = _summary.Seconds - _lapStarted;
            _lapStarted = _summary.Seconds;
            // A lap of its own: the bends are weighed up again for it.
            if (Person is not null)
            {
                Roll();
                _limit = limit = Limits(_stance.Value.HalfTrackM, _stance.Value.ComHeightM);
            }
        }
        (_s, _cross) = (s, cross);
        Route.Sample here = _route.Samples[_index];
        double progress = (_summary.Laps * _route.LengthM) + s;

        double reach = Math.Clamp(Tune.LookAheadSeconds * rolling, Math.Max(1.5 * _wheelbase, Tune.LeastLookAheadM), Tune.MostLookAheadM);
        reach = Math.Max(reach, 3.0 * rolling * dt);
        double3 aim = _route.Ahead(_index, s, reach);
        double asked = SideM;
        if (asked != 0.0 || _side != 0.0)
        {
            double on = s + reach;
            if (_route.Closed && on >= _route.LengthM) on -= _route.LengthM;
            Route.Sample there = _route.Samples[_route.IndexAt(on)];
            // At a creep a car can steer across far more steeply than at speed, which is what gets it out from behind one that is standing.
            double move = Math.Min(SideRateMs, (Math.Abs(rolling) < SideSlowMs ? SideSlopeSlow : SideSlope) * Math.Abs(rolling)) * dt;
            (double lo, double hi) = Room(there);
            _side = there.LoopM > 0.0 ? 0.0 : Math.Clamp(_side + Math.Clamp(asked - _side, -move, move), lo, hi);
            aim += there.Across * _side;
        }
        double3 to = Vec.RejectFrom(aim - rear, radial);
        double3 nose = Vec.Unit(Vec.RejectFrom(ahead, radial));
        double range = Vec.Len(to);
        double bearing = range > 0.0 ? Math.Atan2(Vec.Dot(Vec.Cross(nose, to), radial), Vec.Dot(nose, to)) : 0.0;
        double angle = range > 0.0 ? Math.Atan2(2.0 * _wheelbase * Math.Sin(bearing), range) : 0.0;
        double lockRad = BuggyDrive.SteerLock(_profile, rolling, _gravity, BuggyDrive.WingLoad(_profile, rolling, _air, _mass));
        double steer = lockRad > 0.0 ? Math.Clamp(angle / lockRad, -1.0, 1.0) : 0.0;

        // In the air the wheels steer nothing, and what the line asks for grows until they come down
        // at full lock: they are held straight to land on.
        if (_flight > 0.0) steer = 0.0;

        // The least the route allows between here and where the next step ends, and a little beyond.
        // Between two samples it is what a steady braking from one to the other passes through: taken
        // a sample at a time the last few metres of a stop are steps of a metre a second and more,
        // each of them the brake held on until the nose is down.
        double wanted = Between(limit, _index, s - here.S);
        double toGo = (s - here.S) + (Math.Abs(rolling) * (dt + Tune.BrakeLeadSeconds));
        for (int i = _index, guard = 0; guard < limit.Length; guard++)
        {
            int next = i + 1 < limit.Length ? i + 1 : _route.Closed ? 0 : i;
            double piece = Vec.Len(_route.Samples[next].At - _route.Samples[i].At);
            if (toGo <= piece || next == i)
            {
                wanted = Math.Min(wanted, Between(limit, i, toGo));
                break;
            }
            toGo -= piece;
            wanted = Math.Min(wanted, limit[next]);
            i = next;
        }
        if (_side != 0.0)
        {
            double bend = 0.0, gone = 0.0;
            for (int i = _index, guard = 0; guard < limit.Length && gone <= reach; guard++)
            {
                bend = Math.Max(bend, Math.Abs(_route.Samples[i].Curvature));
                int next = i + 1 < limit.Length ? i + 1 : _route.Closed ? 0 : i;
                if (next == i) break;
                gone += Vec.Len(_route.Samples[next].At - _route.Samples[i].At);
                i = next;
            }
            wanted *= Math.Clamp(1.0 - (SideBendShare * Math.Abs(_side) * bend), SideLeastShare, 1.0);
        }
        FreeMs = wanted;
        wanted = Math.Min(wanted, CapMs);

        // Off the road it comes back slowly and does not charge across the grass; turned away from the
        // route it is turning round.
        // On a loop the car is steered by the loop, and which way it faces against the horizon says nothing.
        bool looping = here.LoopM > 0.0;
        bool wide = Math.Abs(cross - here.CentreM) > here.HalfWidth;
        if (!looping && (wide || Math.Abs(bearing) > Math.PI / 4.0)) wanted = Math.Min(wanted, RecoverMs);

        bool arrived = !_route.Closed && s >= _stopAt;
        bool stopped = arrived && rolling <= StoppedMs;
        if (arrived)
        {
            // Every other step, which is half the brakes: all of them put its nose down as it stops.
            if (_braking || !stopped) Brake(!_braking);
        }
        else if (_braking)
        {
            if (rolling <= wanted + Tune.BrakeOffMs || rolling < LeastBrakeMs) Brake(false);
        }
        else if (rolling > wanted + Math.Max(Tune.BrakeOnMs, Tune.BrakeOnShare * wanted) && rolling > LeastBrakeMs)
        {
            Brake(true);
        }
        // With a band, the brake is pressed by how far over the speed wanted the car is, and all of it only that far over.
        double pressed = Tune.BrakeBandMs > 0.0 ? Math.Clamp((rolling - wanted) / Tune.BrakeBandMs, Tune.LeastBrake, 1.0) : 1.0;
        double throttle = _braking ? -pressed : Math.Clamp((wanted - rolling) / Tune.FullThrottleUnderMs, 0.0, 1.0);
        if (Tune.Model >= 2.0 && !arrived)
        {
            // The acceleration that has the car at the speed wanted a moment on, and the pedal that gives it.
            double level = 1.0 / Math.Sqrt(1.0 + (here.Slope * here.Slope));
            double wingsNow = BuggyDrive.WingLoad(_profile, rolling, _air, _mass);
            double press = Math.Max((_gravity * level) + wingsNow, 0.1);
            double dragNow = _mass > 0.0 ? 0.5 * _air * _profile.DragAreaM2 * rolling * rolling / _mass : 0.0;
            double resist = dragNow + (_profile.RollingResistance * press) + (_gravity * here.Slope * level * Vec.Dot(nose, here.Tangent));
            double need = ((wanted - rolling) / Math.Max(Tune.PedalSeconds, dt)) + resist;
            double pull = _profile.PowerW > 0.0 ? Math.Min(_profile.LaunchAccelG * _gravity, _profile.PowerW / (_mass * Math.Max(rolling, 1.0))) : _profile.LaunchAccelG * _gravity * Math.Max(1.0 - (rolling / _profile.TopSpeed), 0.05);
            if (need >= 0.0 || rolling < LeastBrakeMs)
            {
                if (_braking) Brake(false);
                throttle = Math.Clamp(need / Math.Max(pull, 0.1), 0.0, 1.0);
            }
            else
            {
                double pedal = -need / (_profile.BrakeG * press);
                // Under a little of the pedal the air and the tyres' rolling are left to it.
                if (pedal < 0.03) { if (_braking) Brake(false); throttle = 0.0; }
                else { if (!_braking) Brake(true); throttle = -Math.Clamp(pedal, 0.0, 1.0); }
            }
        }
        // A fluffed exit: the next time after its place the throttle is wanted wide open, it is not, for a moment.
        if (s >= _fumbleAt && _fumbleLap == _summary.Laps && throttle > 0.9 && !_braking)
        {
            (_fumbleUntil, _fumbleAt) = (_summary.Seconds + _fumbleSeconds, double.PositiveInfinity);
            _summary.Fumbles++;
        }
        if (_summary.Seconds < _fumbleUntil && throttle > FumbleThrottle) throttle = FumbleThrottle;
        // Nor is it braked there: the speed it has is what holds it on.
        if (looping) throttle = Math.Clamp((wanted - rolling) / Tune.FullThrottleUnderMs, 0.0, 1.0);

        // A car's length either side of a loop some of its wheels are on it, which is no road's asphalt and leans as no road does.
        bool byLoop = looping;
        for (int k = -LoopNearSamples; k <= LoopNearSamples && !byLoop; k++)
        {
            int near = _index + k;
            if (_route.Closed) near = ((near % limit.Length) + limit.Length) % limit.Length;
            byLoop = near >= 0 && near < limit.Length && _route.Samples[near].LoopM > 0.0;
        }

        int grounded = 0, onGround = 0, offAsphalt = 0, off = 0;
        for (int i = 0; i < corners.Length && i < hubs.Length && i < hubHeights.Length; i++)
        {
            // Straight up a hub on a slope or a bank is further from the road than its tyre is.
            double lean = Vec.Dot(top, radial);
            double over = (hubHeights[i] * (lean > 0.5 && !byLoop ? lean : 1.0)) - corners[i].Radius;
            _summary.MinHubM = Math.Min(_summary.MinHubM, over);
            _summary.MaxHubM = Math.Max(_summary.MaxHubM, over);
            if (over <= _profile.DroopTravel)
            {
                onGround++;
                grounded |= 1 << i;
            }
            if (!byLoop && _road is not null && (!_road.TryLocate(position + (body2Ccf * hubs[i]), null, out _, out double outM) || outM > 0.02))
            {
                offAsphalt++;
                off |= 1 << i;
            }
        }

        // Against the road's own slope, which is level across and climbs along the route.
        double facing = Vec.Dot(nose, here.Tangent);
        double pitch = Deg(Math.Asin(Math.Clamp(Vec.Dot(ahead, radial), -1.0, 1.0)) - Math.Atan(here.Slope * facing));
        double roll = Deg(Math.Asin(Math.Clamp(Vec.Dot(left, radial), -1.0, 1.0))
                          - Math.Atan(here.Slope * Vec.Dot(Vec.Cross(radial, nose), here.Tangent)));

        if (looping) (pitch, roll) = (0.0, 0.0);

        // Against the asphalt's own face where the car is over it, which a banked road leans.
        if (_road is not null && _road.TryLocate(position, null, out _, out _, out double3? face) && face is { } faces)
        {
            pitch = Deg(Math.Asin(Math.Clamp(Vec.Dot(ahead, faces), -1.0, 1.0)));
            roll = Deg(Math.Asin(Math.Clamp(Vec.Dot(left, faces), -1.0, 1.0)));
        }

        if (_rows is { } rows && RowCount < rows.Length)
        {
            rows[RowCount++] = new LapRow
            {
                T = _summary.Seconds, Dt = dt, S = progress, Cross = cross, Speed = speed, Wanted = wanted, Throttle = throttle, Steer = steer,
                RollDeg = roll, PitchDeg = pitch, Grounded = grounded, OffAsphalt = off, HullDown = hullDown, Position = position,
                Hub0 = Hub(hubHeights, 0), Hub1 = Hub(hubHeights, 1), Hub2 = Hub(hubHeights, 2), Hub3 = Hub(hubHeights, 3),
            };
        }

        _summary.Steps++;
        _summary.Seconds += dt;
        _summary.DistanceM += speed * dt;
        _summary.ProgressM = Math.Max(_summary.ProgressM, progress);
        _summary.LongestStepSeconds = Math.Max(_summary.LongestStepSeconds, dt);
        // From the line it is asked to be on.
        _summary.MaxCrossM = Math.Max(_summary.MaxCrossM, Math.Abs(cross - _side));
        _crossSum += Math.Abs(cross - _side) * dt;
        _summary.MaxSpeed = Math.Max(_summary.MaxSpeed, speed);
        _summary.MaxRollDeg = Math.Max(_summary.MaxRollDeg, Math.Abs(roll));
        _summary.MaxPitchDeg = Math.Max(_summary.MaxPitchDeg, Math.Abs(pitch));
        if (offAsphalt > 0) _summary.OffAsphaltSeconds += dt;
        if (hullDown) _summary.HullSeconds += dt;
        _flight = onGround == 0 ? _flight + dt : 0.0;
        if (onGround == 0) _summary.AirSeconds += dt;
        _summary.LongestFlightSeconds = Math.Max(_summary.LongestFlightSeconds, _flight);

        _offFor = _road is not null && offAsphalt >= corners.Length ? _offFor + dt : 0.0;
        _flippedFor = !looping && Vec.Dot(top, radial) < 0.3 ? _flippedFor + dt : 0.0;
        if (progress > _markProgress + 1.0) (_markProgress, _markTime) = (progress, _summary.Seconds);

        if (stopped || (_route.Closed && _summary.Laps >= _lapsWanted)) Finish(LapEnd.Finished);
        else if (_flippedFor >= FlippedSeconds) Finish(LapEnd.Flipped);
        else if (_offFor >= OffRoadFor) Finish(LapEnd.OffRoad);
        else if (_summary.Seconds - _markTime >= StuckSeconds) Finish(LapEnd.Stuck);
        else if (_summary.Seconds >= _timeout) Finish(LapEnd.TimedOut);

        return End == LapEnd.Running ? new DriveInput(throttle, steer) : default;
    }

    private void Brake(bool on)
    {
        _braking = on;
        _summary.BrakeToggles++;
    }

    // What is allowed a distance on from a sample, towards the next.
    private double Between(double[] limit, int i, double onM)
    {
        int next = i + 1 < limit.Length ? i + 1 : _route.Closed ? 0 : i;
        double piece = Vec.Len(_route.Samples[next].At - _route.Samples[i].At);
        if (!(piece > 0.0) || !double.IsFinite(limit[i]) || !double.IsFinite(limit[next])) return limit[i];
        double share = Math.Clamp(onM / piece, 0.0, 1.0);
        return Math.Sqrt((limit[i] * limit[i]) + (((limit[next] * limit[next]) - (limit[i] * limit[i])) * share));
    }

    private static float Hub(ReadOnlySpan<double> heights, int i) => i < heights.Length ? (float)heights[i] : float.NaN;

    private static double Deg(double rad) => rad * 180.0 / Math.PI;
}
