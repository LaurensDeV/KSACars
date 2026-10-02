using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class BuggyDriveTests
{
    private static readonly BuggyProfile P = BuggyProfile.Manx;
    private const double G = 9.81;
    private const double Mass = 650.0;

    [Theory]
    [InlineData(-0.14)]
    [InlineData(-0.05)]
    [InlineData(0.0)]
    [InlineData(0.08)]
    [InlineData(0.14)]
    public void TheArmPutsTheHubAtItsTravel(double travel)
    {
        foreach (BuggyCorner c in P.Corners)
        {
            double3 hub = BuggyDrive.OnArm(c, c.Hub, BuggyDrive.ArmAngle(c, travel));
            Assert.Equal(c.Hub.X + travel, hub.X, 9);
            Assert.Equal(c.Hub.Z, hub.Z, 9);
        }
    }

    [Theory]
    [InlineData(-0.14)]
    [InlineData(0.0)]
    [InlineData(0.14)]
    public void TheCoilsBottomEyeStaysOnTheArm(double travel)
    {
        foreach (BuggyCorner c in P.Corners)
        {
            double angle = BuggyDrive.ArmAngle(c, travel);
            (doubleQuat rotation, double3 scale) = BuggyDrive.CoilPose(c, angle);

            double3 rest = c.CoilBottom - c.CoilTop;
            double3 drawn = c.CoilTop + (rotation * new double3(rest.X * scale.X, rest.Y * scale.Y, rest.Z * scale.Z));
            double3 eye = BuggyDrive.OnArm(c, c.CoilBottom, angle);

            Assert.True(Vec.Len(drawn - eye) < 1e-6, $"{c.Key} at {travel}: {Vec.Len(drawn - eye)} m off");
        }
    }

    [Fact]
    public void ACompressedCoilIsShorter()
    {
        BuggyCorner c = P.Corners[0];
        (_, double3 bumped) = BuggyDrive.CoilPose(c, BuggyDrive.ArmAngle(c, 0.12));
        (_, double3 drooped) = BuggyDrive.CoilPose(c, BuggyDrive.ArmAngle(c, -0.12));
        Assert.True(bumped.X < 1.0 && drooped.X > 1.0, $"bump {bumped.X}, droop {drooped.X}");
    }

    [Fact]
    public void AWheelRollingForwardTurnsItsTopForwardAndSteersLeftToPlusZ()
    {
        double3 top = BuggyDrive.WheelRotation(0.1, 0.0) * new double3(1, 0, 0);
        Assert.True(top.Y > 0.0);

        double3 heading = BuggyDrive.WheelRotation(0.0, 0.3) * new double3(0, 1, 0);
        Assert.True(heading.Z > 0.0);
    }

    [Theory]
    [InlineData(1.0 / 60.0)]
    [InlineData(1.0 / 30.0)]
    public void DroppedItBouncesThenSettlesOnItsSprings(double dt)
    {
        Rig rig = new(dt);
        rig.Position = Rig.Com + new double3(0.25, 0, 0);

        double lowest = double.PositiveInfinity, highestAfterLowest = double.NegativeInfinity;
        for (int i = 0; i < (int)(8.0 / dt); i++)
        {
            rig.Step(default);
            if (rig.Position.X < lowest) { lowest = rig.Position.X; highestAfterLowest = double.NegativeInfinity; }
            highestAfterLowest = Math.Max(highestAfterLowest, rig.Position.X);
        }

        // Springy: from the bottom of the landing it comes back up past where it will end.
        Assert.True(highestAfterLowest - lowest > 0.03, $"rebound only {highestAfterLowest - lowest:F3} m");
        Assert.True(Math.Abs(rig.Position.X - Rig.Com.X) < 0.02, $"rests {rig.Position.X - Rig.Com.X:F3} m off its rest height");
        Assert.True(Vec.Len(rig.Velocity) < 0.01, $"still moving at {Vec.Len(rig.Velocity):F3} m/s");
        Assert.True(Vec.Len(rig.Spin) < 0.01, $"still turning at {Vec.Len(rig.Spin):F3} rad/s");
    }

    [Theory]
    [InlineData(1.0 / 60.0)]
    [InlineData(1.0 / 30.0)]
    public void FullThrottleTakesItToSpeedStraightAndUpright(double dt)
    {
        Rig rig = new(dt);
        rig.Settle();
        for (int i = 0; i < (int)(10.0 / dt); i++) rig.Step(new DriveInput(1.0, 0.0));

        Assert.True(rig.Velocity.Y > 20.0 && rig.Velocity.Y < P.TopSpeed + 0.5, $"{rig.Velocity.Y:F1} m/s");
        Assert.True(Math.Abs(rig.Velocity.Z) < 0.2, $"drifting sideways at {rig.Velocity.Z:F2} m/s");
        Assert.True((rig.Attitude * new double3(1, 0, 0)).X > 0.99, "tipped");
        Assert.InRange(rig.Drive.Gear, 2, 3);
    }

    [Fact]
    public void SteeringLeftTurnsItLeftWithoutRollingIt()
    {
        Rig rig = new(1.0 / 60.0);
        rig.Settle();
        for (int i = 0; i < 180; i++) rig.Step(new DriveInput(1.0, 0.0));
        for (int i = 0; i < 90; i++) rig.Step(new DriveInput(0.3, 1.0));

        double3 heading = rig.Attitude * new double3(0, 1, 0);
        Assert.True(heading.Z > 0.2, $"heading {heading}");
        Assert.True((rig.Attitude * new double3(1, 0, 0)).X > 0.9, "rolled over");
    }

    [Theory]
    [InlineData(1.0 / 60.0)]
    [InlineData(1.0 / 30.0)]
    public void TheEldoradoSettlesFloatilyAndPullsAway(double dt)
    {
        Rig rig = new(dt, BuggyProfile.Eldorado, Rig.EldoradoCom) { RigMass = 2336.0 };
        rig.Position = Rig.EldoradoCom + new double3(0.2, 0, 0);
        double lowest = double.PositiveInfinity, rebound = double.NegativeInfinity;
        for (int i = 0; i < (int)(8.0 / dt); i++)
        {
            rig.Step(default);
            if (rig.Position.X < lowest) { lowest = rig.Position.X; rebound = double.NegativeInfinity; }
            rebound = Math.Max(rebound, rig.Position.X);
        }
        Assert.True(rebound - lowest > 0.03, $"rebound only {rebound - lowest:F3} m");
        Assert.True(Math.Abs(rig.Position.X - Rig.EldoradoCom.X) < 0.03, $"rests {rig.Position.X - Rig.EldoradoCom.X:F3} m off");

        for (int i = 0; i < (int)(10.0 / dt); i++) rig.Step(new DriveInput(1.0, 0.0));
        Assert.True(rig.Velocity.Y > 20.0, $"{rig.Velocity.Y:F1} m/s after ten seconds");
        Assert.True((rig.Attitude * new double3(1, 0, 0)).X > 0.99, "tipped");
    }

    // The engine never hands a parked car a yaw of exactly zero. A side grip that removes more yaw in a
    // step than there was flips it every step instead, and the slip that rocking puts under the front
    // hubs spends the friction circle the drive needed: in game, 1 m/s^2 where 4 was asked for.
    [Theory]
    [InlineData(1.0 / 60.0)]
    [InlineData(1.0 / 40.0)]
    public void AYawKickDiesAwayRatherThanRockingTheDriveAway(double dt)
    {
        Rig buggy = new(dt);
        buggy.Settle();
        buggy.Spin += new double3(0.2, 0, 0);
        for (int i = 0; i < (int)(3.0 / dt); i++) buggy.Step(new DriveInput(1.0, 0.0));
        Assert.True(Math.Abs(buggy.Spin.X) < 0.01, $"the buggy still yaws at {buggy.Spin.X * 180 / Math.PI:F1} deg/s");

        Rig rig = new(dt, BuggyProfile.Eldorado, Rig.EldoradoCom) { RigMass = 2336.0 };
        rig.Settle();
        rig.Spin += new double3(0.2, 0, 0);
        for (int i = 0; i < (int)(3.0 / dt); i++) rig.Step(new DriveInput(1.0, 0.0));
        Assert.True(Math.Abs(rig.Spin.X) < 0.01, $"still yawing at {rig.Spin.X * 180 / Math.PI:F1} deg/s");
        Assert.True(rig.Velocity.Y > 8.0, $"{rig.Velocity.Y:F1} m/s after three seconds");
    }

    // A lock that is not held to the grip asks this for three times the grip there is, and the car
    // scrubs from 15 m/s to a standstill.
    [Fact]
    public void AFullLockFlickAtSpeedTurnsRatherThanScrubbingToAStop()
    {
        Rig rig = new(1.0 / 60.0);
        rig.Settle();
        for (int i = 0; i < 300; i++) rig.Step(new DriveInput(1.0, 0.0));
        double before = Vec.Len(rig.Velocity);
        for (int i = 0; i < 240; i++) rig.Step(new DriveInput(0.6, 1.0));

        double3 heading = rig.Attitude * new double3(0, 1, 0);
        double turned = Math.Atan2(heading.Z, heading.Y) * 180.0 / Math.PI;
        Assert.True(before > 13.0, $"only reached {before:F1} m/s");
        Assert.True(Vec.Len(rig.Velocity) > 0.6 * before, $"scrubbed from {before:F1} to {Vec.Len(rig.Velocity):F1} m/s");
        Assert.True(Math.Abs(turned) > 60.0, $"turned only {turned:F0} deg");
    }

    // Past about 17 degrees of roll the outside tyres' grip, at a centre of mass 0.55 m up, tips the car
    // over them, and a reversal swings twice the lean of a steady turn.
    [Theory]
    [InlineData(30.0, 1.0 / 60.0, 1.0 / 60.0)]
    [InlineData(45.0, 0.019, 0.025)]
    [InlineData(70.0, 1.0 / 40.0, 1.0 / 40.0)]
    public void TheEldoradoLeansThroughASteeringReversalAtSpeedWithoutLiftingAWheel(double speed, double dtA, double dtB)
    {
        BuggyProfile p = BuggyProfile.Eldorado;
        Rig rig = new(dtA, p, Rig.EldoradoCom) { RigMass = 2336.0 };
        rig.Settle();
        rig.Pattern = [dtA, dtB];
        rig.Velocity = new double3(0, speed, 0);

        double lean = 0.0, steady = 0.0, highestHub = 0.0;
        for (double t = 0.0; t < 5.0; t += (dtA + dtB) / 2.0)
        {
            rig.Step(new DriveInput(1.0, t < 1.5 ? 1.0 : -1.0));
            double roll = Math.Abs(Math.Asin((rig.Attitude * new double3(0, 0, 1)).X)) * 180.0 / Math.PI;
            lean = Math.Max(lean, roll);
            if (t < 1.5) steady = Math.Max(steady, roll);
            foreach (BuggyCorner c in p.Corners)
            {
                highestHub = Math.Max(highestHub, (rig.Position + (rig.Attitude * (c.Hub - Rig.EldoradoCom))).X);
            }
        }

        Assert.True(steady > 4.0, $"leans only {steady:F1} deg in the turn");
        Assert.True(lean < 10.0, $"rolled to {lean:F1} deg");
        Assert.True(highestHub < p.Corners[0].Radius + p.DroopTravel + 0.02, $"a hub {highestHub:F2} m up");
    }

    [Fact]
    public void BrakingFromSpeedStopsIt()
    {
        Rig rig = new(1.0 / 60.0);
        rig.Settle();
        for (int i = 0; i < 240; i++) rig.Step(new DriveInput(1.0, 0.0));
        for (int i = 0; i < 360; i++) rig.Step(new DriveInput(-1.0, 0.0));

        Assert.True(rig.Velocity.Y < 0.6, $"still at {rig.Velocity.Y:F1} m/s after six seconds of brake");
    }

    // A rigid box on flat ground, +X up, integrated the way the engine does: the drive's impulse at the
    // start of the frame, then gravity and the motion over it.
    internal sealed class Rig(double dt, BuggyProfile? profile = null, double3? com = null)
    {
        private readonly BuggyProfile _p = profile ?? P;
        private readonly double3 _com = com ?? Com;
        // Set to alternate the step the way KSA's does on a 120 Hz display at 60 fps.
        public double[]? Pattern;
        private int _tick;
        private double Dt => Pattern is { } p ? p[_tick++ % p.Length] : dt;

        public BuggyDrive Drive { get; } = new(profile ?? P);
        // The centre of mass, which rests Com.X over the ground.
        public double3 Position = com ?? Com;
        public double3 Velocity = Vec.Zero;
        public doubleQuat Attitude = doubleQuat.Identity;
        public double3 Spin = Vec.Zero;

        // What the engine gives each car, from KSACarsGameData.xml: the buggy's solid sphere, 1.1 m in
        // radius, and the Eldorado's solid cuboid, 1.0 m tall, 5.6 m long and 2.0 m wide.
        public double RigMass = Mass;
        public double Gravity = G;
        private double3 Inertia => RigMass > 1000
            ? new double3((5.6 * 5.6) + (2.0 * 2.0), (1.0 * 1.0) + (2.0 * 2.0), (1.0 * 1.0) + (5.6 * 5.6)) * (RigMass / 12.0)
            : new double3(1, 1, 1) * (0.4 * RigMass * 1.1 * 1.1);

        // Where KSACarsGameData.xml puts the centre of mass.
        public static readonly double3 Com = new(0.60, -0.25, 0.0);
        public static readonly double3 EldoradoCom = new(0.55, 0.25, 0.0);
        private double3[] Hubs => _hubs ??= _p.Corners.Select(c => c.Hub - _com).ToArray();
        private double3[]? _hubs;

        public void Settle()
        {
            for (int i = 0; i < (int)(6.0 / dt); i++) Step(default);
        }

        public void Step(DriveInput input)
        {
            double dt = Dt;
            doubleQuat toBody = doubleQuat.Conjugate(Attitude);
            double3 comWorld = Position;
            double3 upBody = toBody * new double3(1, 0, 0);
            double3 velBody = toBody * Velocity;

            WheelContact[] contacts = new WheelContact[Hubs.Length];
            for (int i = 0; i < Hubs.Length; i++)
            {
                double3 world = comWorld + (Attitude * Hubs[i]);
                contacts[i] = new WheelContact(true, world.X, upBody, velBody + Vec.Cross(Spin, Hubs[i]));
            }

            DriveImpulse j = Drive.Step(input, contacts, Hubs, new double3(1, 0, 0), new double3(0, 1, 0),
                                        RigMass, Gravity, 1.225, dt);
            Velocity += Attitude * (j.Linear / RigMass);
            Spin += new double3(j.Angular.X / Inertia.X, j.Angular.Y / Inertia.Y, j.Angular.Z / Inertia.Z);

            Velocity += new double3(-Gravity * dt, 0, 0);
            Position += Velocity * dt;
            double rate = Vec.Len(Spin);
            if (rate > 0.0) Attitude *= doubleQuat.CreateFromAxisAngle(Spin / rate, rate * dt);
        }
    }
}
