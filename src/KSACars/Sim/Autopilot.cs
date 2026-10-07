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
    public double MinHubM, MaxHubM, MaxRollDeg, MaxPitchDeg, LongestStepSeconds;
}

/// <summary>One step of a lap, as it began.</summary>
public struct LapRow
{
    public double T, Dt, S, Cross, Speed, Wanted, Throttle, Steer, RollDeg, PitchDeg;
    public float Hub0, Hub1, Hub2, Hub3;

    /// <summary>A bit a wheel: set in the one where its tyre is on something, in the other where it is not over asphalt.</summary>
    public int Grounded, OffAsphalt;
    public double3 Position;

    public const string Header = "t,dt,s,cross,speed,wanted,throttle,steer,hub0,hub1,hub2,hub3,grounded,off_asphalt,roll_deg,pitch_deg,x,y,z";

    public readonly void AppendTo(System.Text.StringBuilder to)
    {
        System.Globalization.CultureInfo plain = System.Globalization.CultureInfo.InvariantCulture;
        to.Append(plain, $"{T:F4},{Dt:F5},{S:F3},{Cross:F3},{Speed:F3},{Wanted:F2},{Throttle:F3},{Steer:F4},");
        to.Append(plain, $"{Hub0:F4},{Hub1:F4},{Hub2:F4},{Hub3:F4},{Grounded},{OffAsphalt},{RollDeg:F2},{PitchDeg:F2},");
        to.Append(plain, $"{Position.X:F3},{Position.Y:F3},{Position.Z:F3}").Append('\n');
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
/// <para>It holds the least of the speed asked for and what each bend allows, braked for in time. A
/// bend allows what <see cref="GripShare"/> of the lesser of the front tyres' grip and what full lock
/// asks of them holds, on the weight and the wings together. The brake is all or nothing, so it is
/// worked with a gap between on and off, and never below <see cref="LeastBrakeMs"/>, where a negative
/// throttle is reverse.</para>
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

    /// <summary>Seconds of travel the steering aims ahead by.</summary>
    public const double LookAheadSeconds = 0.4;

    public const double LeastLookAheadM = 4.0, MostLookAheadM = 60.0;

    /// <summary>Below this a negative throttle is not asked for.</summary>
    public const double LeastBrakeMs = 1.5;

    // The brake comes on this far over the speed wanted, or this share of it if that is more, and goes
    // off this far over it.
    private const double BrakeOnMs = 0.3, BrakeOnShare = 0.04, BrakeOffMs = 0.1;

    // Full throttle from this far under the speed wanted.
    private const double FullThrottleUnderMs = 2.0;

    // The speed held off the road or pointing away from the route, and the least any bend is taken at.
    private const double RecoverMs = 5.0, CrawlMs = 2.0;

    // The speed the end of a road is come up to, over the last few metres; and how far short of the end
    // the front wheels are when the lap is over. Under a metre a second a car with its throttle shut
    // stands still, and from this speed it rolls a metre and a half getting there.
    private const double ArriveMs = 1.2, ArriveOverM = 5.0, ShortOfEndM = 2.5;

    private readonly BuggyProfile _profile;
    private readonly Route _route;
    private readonly RoadSurface? _road;
    private readonly double _mass, _gravity, _air, _wheelbase, _stopAt, _timeout;
    private readonly int _lapsWanted;
    private readonly double[] _limit;
    private readonly LapRow[]? _rows;

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
        _limit = Limits();
        _summary.MinHubM = double.PositiveInfinity;
        _summary.MaxHubM = double.NegativeInfinity;
    }

    public double CruiseMs { get; }
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

    /// <summary>The speed the route allows at each of its samples, m/s.</summary>
    public ReadOnlySpan<double> Limit => _limit;

    /// <summary>Ends the lap from outside; one already over keeps the reason it has.</summary>
    public void Finish(LapEnd reason, string why = "")
    {
        if (End != LapEnd.Running) return;
        End = reason;
        Why = why;
    }

    // What each bend allows, v^2 kappa = share x grip x (g cos(slope) + the wings' press at v), and then
    // back along the route what the brakes can get down to it from.
    private double[] Limits()
    {
        ReadOnlySpan<Route.Sample> samples = _route.Samples;
        double[] limit = new double[samples.Length];
        double grip = GripShare * Math.Min(_profile.FrontGrip, _profile.FrontGrip * _profile.SteerOverGrip);
        double wings = _mass > 0.0 ? 0.5 * _air * _profile.DownforceAreaM2 / _mass : 0.0;
        for (int i = 0; i < limit.Length; i++)
        {
            Route.Sample s = samples[i];
            double level = 1.0 / Math.Sqrt(1.0 + (s.Slope * s.Slope));
            double short_ = Math.Abs(s.Curvature) - (grip * wings);
            double bend = short_ > 1e-9 ? Math.Sqrt(grip * _gravity * level / short_) : double.PositiveInfinity;
            limit[i] = s.S >= _stopAt - ArriveOverM ? ArriveMs : Math.Max(Math.Min(CruiseMs, bend), CrawlMs);
        }

        // A tyre holds its line before it brakes, so what a bend leaves of the grip is all the brakes
        // have: into a bend the braking is done before the turn is. Twice round a closed route, so
        // the bend after the line slows the straight before it.
        double brakes = BrakeShare * _profile.BrakeG * _gravity;
        int last = limit.Length - 1;
        for (int pass = 0; pass < (_route.Closed ? 2 : 1); pass++)
        {
            for (int i = _route.Closed ? last : last - 1; i >= 0; i--)
            {
                int next = (i + 1) % limit.Length;
                double piece = Vec.Len(samples[next].At - samples[i].At);
                double from = limit[next];
                for (int again = 0; again < 2; again++)
                {
                    double held = grip * (_gravity + (wings * from * from));
                    double turning = from * from * Math.Abs(samples[i].Curvature);
                    double spare = Math.Sqrt(Math.Max((held * held) - (turning * turning), 0.0));
                    from = Math.Sqrt((limit[next] * limit[next]) + (2.0 * Math.Min(brakes, spare) * piece));
                }
                limit[i] = Math.Min(limit[i], from);
            }
        }
        return limit;
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
    public DriveInput Step(double3 positionCcf, doubleQuat body2Ccf, double3 velocityCcf, double3 up, double3 forward,
                           ReadOnlySpan<double3> hubs, ReadOnlySpan<double> hubHeights, double dt)
    {
        if (End != LapEnd.Running || !(dt > 0.0)) return default;
        try
        {
            return Drive(positionCcf, body2Ccf, velocityCcf, up, forward, hubs, hubHeights, dt);
        }
        catch (Exception e)
        {
            Finish(LapEnd.Failed, e.Message);
            return default;
        }
    }

    private DriveInput Drive(double3 position, doubleQuat body2Ccf, double3 velocity, double3 up, double3 forward,
                             ReadOnlySpan<double3> hubs, ReadOnlySpan<double> hubHeights, double dt)
    {
        BuggyCorner[] corners = _profile.Corners;
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

        // The least the route allows between here and where the next step ends.
        double wanted = _limit[_index];
        double covered = here.S - s;
        for (int i = _index, guard = 0; covered < Math.Abs(rolling) * dt && guard < _limit.Length; guard++)
        {
            int next = i + 1;
            if (next >= _limit.Length)
            {
                if (!_route.Closed) break;
                next = 0;
            }
            covered += Vec.Len(_route.Samples[next].At - _route.Samples[i].At);
            wanted = Math.Min(wanted, _limit[next]);
            i = next;
        }

        // Off the road it comes back slowly and does not charge across the grass; turned away from the
        // route it is turning round.
        bool wide = Math.Abs(cross - here.CentreM) > here.HalfWidth;
        if (wide || Math.Abs(bearing) > Math.PI / 4.0) wanted = Math.Min(wanted, RecoverMs);

        bool arrived = !_route.Closed && s >= _stopAt;
        if (_braking)
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
            double over = hubHeights[i] - corners[i].Radius;
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

        if (_rows is { } rows && RowCount < rows.Length)
        {
            rows[RowCount++] = new LapRow
            {
                T = _summary.Seconds, Dt = dt, S = progress, Cross = cross, Speed = speed, Wanted = wanted, Throttle = throttle, Steer = steer,
                RollDeg = roll, PitchDeg = pitch, Grounded = grounded, OffAsphalt = off, Position = position,
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
        _flight = onGround == 0 ? _flight + dt : 0.0;
        if (onGround == 0) _summary.AirSeconds += dt;
        _summary.LongestFlightSeconds = Math.Max(_summary.LongestFlightSeconds, _flight);

        _offFor = _road is not null && offAsphalt >= corners.Length ? _offFor + dt : 0.0;
        _flippedFor = Vec.Dot(top, radial) < 0.3 ? _flippedFor + dt : 0.0;
        if (progress > _markProgress + 1.0) (_markProgress, _markTime) = (progress, _summary.Seconds);

        if (arrived || (_route.Closed && _summary.Laps >= _lapsWanted)) Finish(LapEnd.Finished);
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

    private static float Hub(ReadOnlySpan<double> heights, int i) => i < heights.Length ? (float)heights[i] : float.NaN;

    private static double Deg(double rad) => rad * 180.0 / Math.PI;
}
