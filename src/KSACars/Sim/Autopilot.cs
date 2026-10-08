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
    public int Laps, Steps, BrakeToggles;
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
    private readonly double _mass, _gravity, _air, _wheelbase, _stopAt, _timeout;
    private readonly int _lapsWanted;
    private readonly LapRow[]? _rows;
    private double[]? _limit;

    private LapSummary _summary;
    private int _index = -1;
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
        CruiseMs = cruiseMs > 0.0 ? Math.Min(cruiseMs, profile.TopSpeed) : profile.TopSpeed;

        _stopAt = route.Closed ? double.PositiveInfinity : Math.Max(route.LengthM - _wheelbase - ShortOfEndM, 0.0);
        _summary.MinHubM = double.PositiveInfinity;
        _summary.MaxHubM = double.NegativeInfinity;
    }

    public double CruiseMs { get; }

    /// <summary>Whether crests and dips are taken at whatever the bends allow, to see what a car does in the air.</summary>
    public bool Jumps { get; init; }
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

    // What the road allows at each sample, and then back along the route what the brakes can get down
    // to it from. A bend: v^2 kappa = share x grip x what presses the tyres, which is g cos(slope), the
    // wings' press at v and, over a crest, less what the crest throws off.
    private double[] Limits(double halfTrackM, double comHeightM)
    {
        ReadOnlySpan<Route.Sample> samples = _route.Samples;
        double[] limit = new double[samples.Length];
        double grip = GripShare * Math.Min(_profile.FrontGrip, _profile.FrontGrip * _profile.SteerOverGrip);
        if (comHeightM > 0.0) grip = Math.Min(grip, TipShare * halfTrackM / comHeightM);
        double wings = _mass > 0.0 ? 0.5 * _air * _profile.DownforceAreaM2 / _mass : 0.0;

        // What a spring carries over its share of the weight at a compression is that times its rate.
        double spring = 2.0 * Math.PI * _profile.SpringHz;
        double dip = DipShare * _profile.BumpTravel * spring * spring;
        for (int i = 0; i < limit.Length; i++)
        {
            Route.Sample s = samples[i];
            double level = 1.0 / Math.Sqrt(1.0 + (s.Slope * s.Slope));
            double crest = Jumps ? 0.0 : Math.Max(-s.Vertical, 0.0);
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
            if (!Jumps)
            {
                allowed = Math.Min(allowed, Under(CrestShare * _gravity * level, crest - (CrestShare * wings)));
                allowed = Math.Min(allowed, Under(dip, Math.Max(s.Vertical, 0.0)));
            }
            limit[i] = s.S >= _stopAt - ArriveOverM ? ArriveMs : Math.Max(allowed, CrawlMs);
        }

        // A tyre holds its line before it brakes, so what a bend leaves of the grip is all the brakes
        // have: into a bend the braking is done before the turn is. The car is taken to be the brake's
        // gap over the speed it is braked to. Twice round a closed route, so the bend after the line
        // slows the straight before it.
        double brakes = BrakeShare * _profile.BrakeG * _gravity;
        int last = limit.Length - 1;
        for (int pass = 0; pass < (_route.Closed ? 2 : 1); pass++)
        {
            for (int i = _route.Closed ? last : last - 1; i >= 0; i--)
            {
                int next = (i + 1) % limit.Length;
                Route.Sample s = samples[i];
                double piece = Vec.Len(samples[next].At - s.At);
                double level = 1.0 / Math.Sqrt(1.0 + (s.Slope * s.Slope));
                double crest = Jumps ? 0.0 : Math.Max(-s.Vertical, 0.0);
                // Through a dip too: the brakes' dive and the dip push the same front springs.
                bool gently = s.S >= _stopAt - ArriveBrakeOverM || (!Jumps && s.Vertical * limit[next] * limit[next] > DipBrakeShare * dip);
                double most = gently ? Math.Min(brakes, GentleBrakeG * _gravity) : brakes;
                double from = limit[next];
                for (int again = 0; again < 2; again++)
                {
                    double over = from * (1.0 + BrakeOnShare);
                    double held = Banked(grip, s) * Math.Max((_gravity * level) + ((wings - crest) * over * over), 0.0);
                    double turning = over * over * Math.Abs(s.Curvature);
                    double spare = Math.Sqrt(Math.Max((held * held) - (turning * turning), 0.0));

                    // Gravity along the road is with the brakes up a hill and against them down one.
                    double slowing = Math.Max(Math.Min(most, spare) + (_gravity * s.Slope * level), 0.0);
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
        if (_limit is not { } limit)
        {
            (double halfTrack, double comHeight) = Stance(hubs, up, forward);
            _limit = limit = Limits(halfTrack, comHeight);
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
        }
        Route.Sample here = _route.Samples[_index];
        double progress = (_summary.Laps * _route.LengthM) + s;

        double reach = Math.Clamp(LookAheadSeconds * rolling, Math.Max(1.5 * _wheelbase, LeastLookAheadM), MostLookAheadM);
        reach = Math.Max(reach, 3.0 * rolling * dt);
        double3 to = Vec.RejectFrom(_route.Ahead(_index, s, reach) - rear, radial);
        double3 nose = Vec.Unit(Vec.RejectFrom(ahead, radial));
        double range = Vec.Len(to);
        double bearing = range > 0.0 ? Math.Atan2(Vec.Dot(Vec.Cross(nose, to), radial), Vec.Dot(nose, to)) : 0.0;
        double angle = range > 0.0 ? Math.Atan2(2.0 * _wheelbase * Math.Sin(bearing), range) : 0.0;
        double lockRad = BuggyDrive.SteerLock(_profile, rolling, _gravity, BuggyDrive.WingLoad(_profile, rolling, _air, _mass));
        double steer = lockRad > 0.0 ? Math.Clamp(angle / lockRad, -1.0, 1.0) : 0.0;

        // The least the route allows between here and where the next step ends, and a little beyond.
        // Between two samples it is what a steady braking from one to the other passes through: taken
        // a sample at a time the last few metres of a stop are steps of a metre a second and more,
        // each of them the brake held on until the nose is down.
        double wanted = Between(limit, _index, s - here.S);
        double toGo = (s - here.S) + (Math.Abs(rolling) * (dt + BrakeLeadSeconds));
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

        // Off the road it comes back slowly and does not charge across the grass; turned away from the
        // route it is turning round.
        bool wide = Math.Abs(cross - here.CentreM) > here.HalfWidth;
        if (wide || Math.Abs(bearing) > Math.PI / 4.0) wanted = Math.Min(wanted, RecoverMs);

        bool arrived = !_route.Closed && s >= _stopAt;
        bool stopped = arrived && rolling <= StoppedMs;
        if (arrived)
        {
            // Every other step, which is half the brakes: all of them put its nose down as it stops.
            if (_braking || !stopped) Brake(!_braking);
        }
        else if (_braking)
        {
            if (rolling <= wanted + BrakeOffMs || rolling < LeastBrakeMs) Brake(false);
        }
        else if (rolling > wanted + Math.Max(BrakeOnMs, BrakeOnShare * wanted) && rolling > LeastBrakeMs)
        {
            Brake(true);
        }
        double throttle = _braking ? -1.0 : Math.Clamp((wanted - rolling) / FullThrottleUnderMs, 0.0, 1.0);

        int grounded = 0, onGround = 0, offAsphalt = 0, off = 0;
        for (int i = 0; i < corners.Length && i < hubs.Length && i < hubHeights.Length; i++)
        {
            // Straight up a hub on a slope or a bank is further from the road than its tyre is.
            double lean = Vec.Dot(top, radial);
            double over = (hubHeights[i] * (lean > 0.5 ? lean : 1.0)) - corners[i].Radius;
            _summary.MinHubM = Math.Min(_summary.MinHubM, over);
            _summary.MaxHubM = Math.Max(_summary.MaxHubM, over);
            if (over <= _profile.DroopTravel)
            {
                onGround++;
                grounded |= 1 << i;
            }
            if (_road is not null && (!_road.TryLocate(position + (body2Ccf * hubs[i]), null, out _, out double outM) || outM > 0.02))
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
        _summary.MaxCrossM = Math.Max(_summary.MaxCrossM, Math.Abs(cross));
        _crossSum += Math.Abs(cross) * dt;
        _summary.MaxSpeed = Math.Max(_summary.MaxSpeed, speed);
        _summary.MaxRollDeg = Math.Max(_summary.MaxRollDeg, Math.Abs(roll));
        _summary.MaxPitchDeg = Math.Max(_summary.MaxPitchDeg, Math.Abs(pitch));
        if (offAsphalt > 0) _summary.OffAsphaltSeconds += dt;
        if (hullDown) _summary.HullSeconds += dt;
        _flight = onGround == 0 ? _flight + dt : 0.0;
        if (onGround == 0) _summary.AirSeconds += dt;
        _summary.LongestFlightSeconds = Math.Max(_summary.LongestFlightSeconds, _flight);

        _offFor = _road is not null && offAsphalt >= corners.Length ? _offFor + dt : 0.0;
        _flippedFor = Vec.Dot(top, radial) < 0.3 ? _flippedFor + dt : 0.0;
        if (progress > _markProgress + 1.0) (_markProgress, _markTime) = (progress, _summary.Seconds);

        if (stopped || (_route.Closed && _summary.Laps >= _lapsWanted)) Finish(LapEnd.Finished);
        else if (_flippedFor >= FlippedSeconds) Finish(LapEnd.Flipped);
        else if (_offFor >= OffRoadSeconds) Finish(LapEnd.OffRoad);
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
