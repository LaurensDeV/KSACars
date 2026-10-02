using Brutal.Numerics;

namespace KSACars;

/// <summary>What one wheel's hub, at its rest point on the car, sees of the ground this step.</summary>
/// <param name="HubHeight">The rest point's height above the terrain, measured along the ground's up.</param>
/// <param name="GroundUp">The ground's up, in the car's body frame.</param>
/// <param name="HubVelocity">The rest point's velocity over the ground, in the car's body frame.</param>
public readonly record struct WheelContact(bool Valid, double HubHeight, double3 GroundUp, double3 HubVelocity);

/// <param name="Throttle">-1 full reverse or brake, +1 full forward.</param>
/// <param name="Steer">-1 full right, +1 full left.</param>
public readonly record struct DriveInput(double Throttle, double Steer);

/// <summary>What a step asks of the craft: an impulse through its centre of mass and one about it, body frame.</summary>
public readonly record struct DriveImpulse(double3 Linear, double3 Angular);

/// <summary>
/// A buggy's springs, tyres and engine, stepped once a frame against the ground under each hub.
///
/// <para>The engine integrates the craft; this only says how hard the ground pushes back. Each wheel
/// is a spring-damper along the car's up, carrying its share of the static weight at rest, and a tyre
/// whose drive, brake and side grip share one friction circle of <c>grip x load</c>. Side grip is asked
/// to cancel the wheel's sideways speed within the step and no more, which is what keeps an explicit
/// per-frame impulse from ringing.</para>
///
/// <para>Body frame throughout, with the origin at the centre of mass, because that is the frame the
/// impulse is written in.</para>
/// </summary>
public sealed class BuggyDrive(BuggyProfile profile)
{
    // Share of a wheel's own slip cancelled per step. Four wheels each cancelling all of theirs
    // couple through the yaw inertia and overshoot, so each asks for less than the whole.
    private const double Settle = 0.7;

    // A spring never pushes harder than this many times its share of the weight. A craft can be
    // placed with its wheels well into the ground, and the spring alone would throw it into the air.
    private const double MaxLoadFactor = 4.0;

    public BuggyProfile Profile { get; } = profile;

    /// <summary>Front-wheel angle, radians, left positive.</summary>
    public double SteerAngle { get; private set; }

    /// <summary>Each wheel's travel from rest, metres, up positive.</summary>
    public double[] Travel { get; } = new double[profile.Corners.Length];

    /// <summary>Each wheel's roll angle about its axle, radians.</summary>
    public double[] Spin { get; } = new double[profile.Corners.Length];

    public bool[] Grounded { get; } = new bool[profile.Corners.Length];

    public double Rpm { get; private set; } = profile.IdleRpm;

    /// <summary>How hard the engine is working, 0 to 1, for the sound.</summary>
    public double Load { get; private set; }

    public double ForwardSpeed { get; private set; }

    public int Gear { get; private set; }

    /// <summary>
    /// Advances one step and returns the impulse the ground and the engine put into the car.
    /// </summary>
    /// <param name="hubs">Each corner's hub at rest, relative to the centre of mass, body frame.</param>
    /// <param name="up">The car's own up, body frame.</param>
    /// <param name="forward">The car's own forward, body frame.</param>
    public DriveImpulse Step(DriveInput input, ReadOnlySpan<WheelContact> contacts, ReadOnlySpan<double3> hubs,
                             double3 up, double3 forward, double mass, double gravity, double airDensity, double dt)
    {
        int n = Profile.Corners.Length;
        if (!(dt > 0.0) || !(mass > 0.0) || contacts.Length != n || hubs.Length != n) return default;

        double3 left = Vec.Cross(up, forward);

        double speed = 0.0;
        int grounded = 0;
        for (int i = 0; i < n; i++)
        {
            if (!contacts[i].Valid) continue;
            speed += Vec.Dot(contacts[i].HubVelocity, forward);
            grounded++;
        }
        ForwardSpeed = grounded > 0 ? speed / grounded : ForwardSpeed;

        SteerToward(input.Steer, gravity, dt);

        Span<double> share = stackalloc double[n];
        StaticShares(hubs, forward, share);

        double omega = 2.0 * Math.PI * Profile.SpringHz;
        double3 linear = Vec.Zero;
        double3 angular = Vec.Zero;

        double drive = DriveForce(input.Throttle, mass, gravity);
        int drivenOnGround = 0;
        for (int i = 0; i < n; i++)
        {
            if (Profile.Corners[i].Driven && Touching(i, contacts[i], up)) drivenOnGround++;
        }

        // How far each wheel is pushed up, from the ground below its hub along the car's own up; an
        // axle's anti-roll bar needs both of its wheels' before either spring can be worked out.
        Span<double> compressions = stackalloc double[n];
        Span<double> closings = stackalloc double[n];
        for (int i = 0; i < n; i++)
        {
            WheelContact c = contacts[i];
            double tilt = c.Valid ? Vec.Dot(up, c.GroundUp) : 0.0;
            double reach = tilt > 0.2 ? c.HubHeight / tilt : double.PositiveInfinity;
            compressions[i] = c.Valid ? Profile.Corners[i].Radius - reach : double.NegativeInfinity;
            closings[i] = c.Valid ? -Vec.Dot(c.HubVelocity, c.GroundUp) / Math.Max(tilt, 0.2) : 0.0;
        }

        for (int i = 0; i < n; i++)
        {
            BuggyCorner corner = Profile.Corners[i];
            WheelContact c = contacts[i];
            double m = mass * share[i];
            double k = m * omega * omega;
            double damping = 2.0 * Profile.DampingRatio * m * omega;

            double tilt = c.Valid ? Vec.Dot(up, c.GroundUp) : 0.0;
            double compression = compressions[i];

            if (!c.Valid || compression < -Profile.DroopTravel)
            {
                Grounded[i] = false;
                Travel[i] = MoveToward(Travel[i], -Profile.DroopTravel, 1.5 * dt);
                Spin[i] += WheelSpinRate(i, corner, drivenOnGround, input.Throttle) * dt;
                continue;
            }

            Grounded[i] = true;
            Travel[i] = Math.Min(compression, Profile.BumpTravel);

            double closing = closings[i];
            double spring = (m * gravity) + (k * compression) + (damping * closing);
            int partner = AxlePartner(i);
            if (partner >= 0)
            {
                double other = Math.Clamp(compressions[partner], -Profile.DroopTravel, Profile.BumpTravel);
                spring += Profile.AntiRoll * k * (Math.Min(compression, Profile.BumpTravel) - other);
                // Half the difference is this wheel's speed in a pure roll, and nothing in a bounce.
                spring += Profile.RollDamping * m * omega * (closing - closings[partner]);
            }
            if (compression > Profile.BumpTravel) spring += Profile.BumpStopFactor * k * (compression - Profile.BumpTravel);
            double load = Math.Clamp(spring, 0.0, MaxLoadFactor * m * gravity);

            double3 normal = c.GroundUp;
            double3 heading = corner.Steers
                ? (doubleQuat.CreateFromAxisAngle(up, SteerAngle) * forward)
                : forward;
            double3 along = Vec.Unit(Vec.RejectFrom(heading, normal));
            double3 side = Vec.Cross(normal, along);

            double vAlong = Vec.Dot(c.HubVelocity, along);
            double vSide = Vec.Dot(c.HubVelocity, side);

            double grip = corner.Steers ? Profile.FrontGrip : Profile.RearGrip;
            double limit = grip * load;

            double fAlong = LongitudinalForce(corner, input.Throttle, drive, drivenOnGround, vAlong, m, load, dt);
            double fSide = -vSide * m * Settle / dt;

            // Holding the line comes first and pushing gets what is left: a tyre that spends its grip on
            // drive before cornering lets go of the tail the moment the throttle opens in a turn.
            fSide = Math.Clamp(fSide, -limit, limit);
            double spare = Math.Sqrt(Math.Max((limit * limit) - (fSide * fSide), 0.0));
            fAlong = Math.Clamp(fAlong, -spare, spare);

            double3 force = (normal * load) + (along * fAlong) + (side * fSide);
            double3 contact = hubs[i] - (up * (corner.Radius - Travel[i]));

            linear += force * dt;
            angular += Vec.Cross(contact, force) * dt;
            angular += Vec.Cross(up * Profile.RollCentreHeight, side * fSide) * dt;

            Spin[i] += vAlong / corner.Radius * dt;
        }

        // Air, at the centre of mass: it is small against the tyres and only there to cap the top speed.
        double3 air = Vec.Zero;
        if (grounded > 0)
        {
            double3 v = contacts[0].HubVelocity;
            double s = Vec.Len(v);
            if (s > 0.0) air = -v * (0.5 * airDensity * Profile.DragAreaM2 * s);
        }
        linear += air * dt;

        UpdateEngine(input.Throttle, drivenOnGround > 0, dt);

        return new DriveImpulse(linear, angular);
    }

    // The wheel on the other side of the same axle, or -1.
    private int AxlePartner(int i)
    {
        BuggyCorner me = Profile.Corners[i];
        for (int j = 0; j < Profile.Corners.Length; j++)
        {
            BuggyCorner c = Profile.Corners[j];
            if (j != i && c.Steers == me.Steers && Math.Abs(c.Hub.Y - me.Hub.Y) < 0.05) return j;
        }
        return -1;
    }

    private static bool Touching(int i, WheelContact c, double3 up) => c.Valid && Vec.Dot(up, c.GroundUp) > 0.2;

    // The lock is the angle whose turn the front tyres can just hold at this speed, v^2 / R = grip g
    // with R = wheelbase / tan(angle), so a flick at speed turns the car rather than scrubbing it to a stop.
    private void SteerToward(double steer, double gravity, double dt)
    {
        double lockRad = Profile.MaxSteerDeg * Math.PI / 180.0;
        double v2 = ForwardSpeed * ForwardSpeed;
        if (v2 > 1.0)
        {
            double held = Math.Atan(Wheelbase() * Profile.FrontGrip * gravity * Profile.SteerOverGrip / v2);
            lockRad = Math.Min(lockRad, Math.Max(held, 3.0 * Math.PI / 180.0));
        }
        double wanted = Math.Clamp(steer, -1.0, 1.0) * lockRad;
        SteerAngle = MoveToward(SteerAngle, wanted, Profile.SteerRateDegPerSec * Math.PI / 180.0 * dt);
    }

    private double Wheelbase()
    {
        double front = 0.0, rear = 0.0;
        int nFront = 0, nRear = 0;
        foreach (BuggyCorner c in Profile.Corners)
        {
            if (c.Steers) { front += c.Hub.Y; nFront++; }
            else { rear += c.Hub.Y; nRear++; }
        }
        return nFront > 0 && nRear > 0 ? Math.Abs((front / nFront) - (rear / nRear)) : 2.0;
    }

    // Each wheel's share of the car's weight at rest, from where the centre of mass sits between the axles.
    private void StaticShares(ReadOnlySpan<double3> hubs, double3 forward, Span<double> share)
    {
        double front = 0.0, rear = 0.0;
        int nFront = 0, nRear = 0;
        for (int i = 0; i < hubs.Length; i++)
        {
            double y = Vec.Dot(hubs[i], forward);
            if (Profile.Corners[i].Steers) { front += y; nFront++; }
            else { rear += y; nRear++; }
        }
        if (nFront == 0 || nRear == 0)
        {
            share.Fill(1.0 / hubs.Length);
            return;
        }
        front /= nFront;
        rear /= nRear;
        double frontShare = Math.Clamp(-rear / (front - rear), 0.1, 0.9);
        for (int i = 0; i < hubs.Length; i++)
        {
            share[i] = Profile.Corners[i].Steers ? frontShare / nFront : (1.0 - frontShare) / nRear;
        }
    }

    // The engine's pull at the driven wheels together: strongest from rest and gone at the top speed.
    private double DriveForce(double throttle, double mass, double gravity)
    {
        double launch = Profile.LaunchAccelG * gravity * mass;
        if (throttle > 0.0 && ForwardSpeed > -0.5)
        {
            return throttle * launch * Math.Clamp(1.0 - (ForwardSpeed / Profile.TopSpeed), 0.0, 1.0);
        }
        if (throttle < 0.0 && ForwardSpeed < 0.5)
        {
            return throttle * launch * 0.6 * Math.Clamp(1.0 + (ForwardSpeed / Profile.ReverseTopSpeed), 0.0, 1.0);
        }
        return 0.0;
    }

    private double LongitudinalForce(BuggyCorner corner, double throttle, double drive, int drivenOnGround,
                                     double vAlong, double m, double load, double dt)
    {
        // Pressing against the way the car is rolling is braking, on all four.
        bool braking = (throttle > 0.0 && ForwardSpeed < -0.5) || (throttle < 0.0 && ForwardSpeed > 0.5);
        if (braking)
        {
            return -Math.Sign(vAlong) * Math.Min(Profile.BrakeG * load, Math.Abs(vAlong) * m / dt);
        }

        if (corner.Driven && drive != 0.0 && drivenOnGround > 0) return drive / drivenOnGround;

        // Coasting: rolling resistance, and nothing at all while it is standing, so it parks on a slope.
        if (throttle == 0.0 && Math.Abs(ForwardSpeed) < 1.0)
        {
            return -vAlong * m * Settle / dt;
        }
        return -Math.Sign(vAlong) * Math.Min(Profile.RollingResistance * load, Math.Abs(vAlong) * m / dt);
    }

    // A wheel in the air still turns: a driven one spins up under power, a free one keeps rolling.
    private double WheelSpinRate(int i, BuggyCorner corner, int drivenOnGround, double throttle)
    {
        if (corner.Driven && throttle != 0.0 && drivenOnGround == 0) return throttle * 40.0;
        return ForwardSpeed / corner.Radius;
    }

    private void UpdateEngine(double throttle, bool driven, double dt)
    {
        double speed = Math.Abs(ForwardSpeed);
        double[] tops = Profile.GearTopSpeeds;
        int gear = 0;
        while (gear < tops.Length - 1 && speed > tops[gear] * 0.95) gear++;
        Gear = gear;

        double lower = gear == 0 ? 0.0 : tops[gear - 1] * 0.95;
        double fraction = Math.Clamp((speed - (lower * 0.6)) / (tops[gear] - (lower * 0.6)), 0.0, 1.0);
        double fromWheels = Profile.IdleRpm + ((Profile.RedlineRpm - Profile.IdleRpm) * fraction);

        // Revved in the air or with the wheels spinning: the engine runs free of the road.
        double free = Profile.IdleRpm + ((Profile.RedlineRpm - Profile.IdleRpm) * 0.8 * Math.Abs(throttle));
        double wanted = driven ? Math.Max(fromWheels, Profile.IdleRpm + (Math.Abs(throttle) * 600.0)) : free;

        double rate = wanted > Rpm ? 5000.0 : 3500.0;
        Rpm = MoveToward(Rpm, wanted, rate * dt);
        Load = MoveToward(Load, Math.Abs(throttle), 4.0 * dt);
    }

    private static double MoveToward(double from, double to, double step)
        => from < to ? Math.Min(from + step, to) : Math.Max(from - step, to);

    // --- poses, for drawing ---

    /// <summary>The trailing arm's turn about its bushing, part-frame +Z, that puts the hub at its travel.</summary>
    public static double ArmAngle(BuggyCorner corner, double travel)
    {
        double3 d = corner.Hub - corner.ArmPivot;
        double length = Math.Sqrt((d.X * d.X) + (d.Y * d.Y));
        if (!(length > 0.0)) return 0.0;

        double rest = Math.Atan2(d.Y, d.X);
        double x = Math.Clamp((d.X + travel) / length, -1.0, 1.0);
        double moved = Math.Acos(x) * (d.Y < 0.0 ? -1.0 : 1.0);
        return moved - rest;
    }

    /// <summary>Where a point riding on the arm is when the arm has turned by <paramref name="angle"/>.</summary>
    public static double3 OnArm(BuggyCorner corner, double3 point, double angle)
        => corner.ArmPivot + (doubleQuat.CreateFromAxisAngle(new double3(0, 0, 1), angle) * (point - corner.ArmPivot));

    /// <summary>
    /// The coil-over's rotation and its squash along its own mesh X, so its bottom eye lands on the arm's.
    /// The mesh was exported at rest, so both are relative to that pose.
    /// </summary>
    public static (doubleQuat Rotation, double3 Scale) CoilPose(BuggyCorner corner, double armAngle)
    {
        double3 rest = corner.CoilBottom - corner.CoilTop;
        double3 now = OnArm(corner, corner.CoilBottom, armAngle) - corner.CoilTop;
        double restLength = Vec.Len(rest);
        double length = Vec.Len(now);
        if (!(restLength > 0.0) || !(Math.Abs(rest.X) > 1e-6)) return (doubleQuat.Identity, new double3(1, 1, 1));

        // Scaled along the mesh's X only, the coil's bottom moves to (s*x, y, z); pick s for the length wanted.
        double across = (rest.Y * rest.Y) + (rest.Z * rest.Z);
        double s = Math.Sqrt(Math.Max((length * length) - across, 1e-6)) / Math.Abs(rest.X);
        double3 squashed = new(rest.X * s, rest.Y, rest.Z);
        return (Vec.RotationFromTo(squashed, now), new double3(s, 1, 1));
    }

    /// <summary>A wheel's rotation: rolled about its axle, then steered about the car's up.</summary>
    public static doubleQuat WheelRotation(double spin, double steer)
        => doubleQuat.CreateFromAxisAngle(new double3(1, 0, 0), steer)
           * doubleQuat.CreateFromAxisAngle(new double3(0, 0, 1), spin);
}
