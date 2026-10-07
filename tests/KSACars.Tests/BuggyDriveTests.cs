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

    private static Rig F2004Rig(double dt) =>
        new(dt, BuggyProfile.F2004, Rig.F2004Com) { RigMass = 605.0, Box = new double3(0.5, 5.6, 2.0) };

    [Theory]
    [InlineData(1.0 / 60.0)]
    [InlineData(1.0 / 30.0)]
    public void TheF2004SettlesOnItsShortSpringsAndPullsAway(double dt)
    {
        Rig rig = F2004Rig(dt);
        rig.Position = Rig.F2004Com + new double3(0.05, 0, 0);
        for (int i = 0; i < (int)(4.0 / dt); i++) rig.Step(default);
        Assert.True(Math.Abs(rig.Position.X - Rig.F2004Com.X) < 0.01, $"rests {rig.Position.X - Rig.F2004Com.X:F3} m off");
        Assert.True(Vec.Len(rig.Velocity) < 0.05, $"still moving at {Vec.Len(rig.Velocity):F2} m/s");

        for (int i = 0; i < (int)(5.0 / dt); i++) rig.Step(new DriveInput(1.0, 0.0));
        Assert.True(rig.Velocity.Y > 30.0, $"{rig.Velocity.Y:F1} m/s after five seconds");
        Assert.True((rig.Attitude * new double3(1, 0, 0)).X > 0.99, "tipped");
    }

    [Theory]
    [InlineData(1.0 / 60.0)]
    [InlineData(1.0 / 40.0)]
    public void TheF2004ShrugsOffAYawKick(double dt)
    {
        Rig rig = F2004Rig(dt);
        rig.Settle();
        rig.Spin += new double3(0.2, 0, 0);
        double yaw = 0.0;
        for (double t = 0.0; t < 3.0; t += dt)
        {
            rig.Step(new DriveInput(1.0, 0.0));
            if (t >= 2.0) yaw = Math.Max(yaw, Math.Abs(rig.Spin.X));
        }
        Assert.True(yaw < 0.01, $"still yawing at up to {yaw * 180 / Math.PI:F1} deg/s in the third second");
        Assert.True(rig.Velocity.Y > 27.0, $"{rig.Velocity.Y:F1} m/s after three seconds");
    }

    [Fact]
    public void TheF2004TurnsAndBrakesWithoutRollingOver()
    {
        Rig rig = F2004Rig(1.0 / 60.0);
        rig.Settle();
        for (int i = 0; i < 240; i++) rig.Step(new DriveInput(1.0, 0.0));
        for (int i = 0; i < 120; i++) rig.Step(new DriveInput(0.4, 1.0));
        double3 heading = rig.Attitude * new double3(0, 1, 0);
        Assert.True(heading.Z > 0.2, $"heading {heading}");
        Assert.True((rig.Attitude * new double3(1, 0, 0)).X > 0.98, "rolled");

        double before = Vec.Len(rig.Velocity);
        for (int i = 0; i < 180; i++) rig.Step(new DriveInput(-1.0, 0.0));
        Assert.True(Vec.Len(rig.Velocity) < 0.5 * before, $"from {before:F1} to {Vec.Len(rig.Velocity):F1} m/s under three seconds of brake");
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

    // Over a crest a car is off all four wheels for a step or two, and KSA's drag coming back there is a
    // two-g jab at speed. Flying, or with a rocket lit, that drag is the only one the car has.
    [Fact]
    public void TheGamesDragStaysOffOverACrestAndComesBackInFlight()
    {
        Assert.True(BuggyDrive.ShedsGameDrag(0.0, burning: false));
        Assert.True(BuggyDrive.ShedsGameDrag(0.1, burning: false));
        Assert.False(BuggyDrive.ShedsGameDrag(BuggyDrive.FlightSeconds, burning: false));
        Assert.False(BuggyDrive.ShedsGameDrag(0.0, burning: true));
    }

    // A held key is full lock, and a lock that asks the tyres for all their grip sideways leaves the
    // drive none: the car crawls up to speed in a turn and falls back to a crawl in a fast one.
    [Theory]
    [InlineData(1.0 / 60.0)]
    [InlineData(0.021)]
    [InlineData(1.0 / 30.0)]
    public void TheF2004KeepsItsDriveWithTheLockHeld(double dt)
    {
        Rig slow = F2004Rig(dt);
        slow.LeftGroundRise = 0.0005;
        slow.Settle();
        slow.Velocity = new double3(0, 10.0, 0);
        for (double t = 0.0; t < 4.0; t += dt) slow.Step(new DriveInput(1.0, 1.0));
        Assert.True(Vec.Len(slow.Velocity) > 20.0, $"only {Vec.Len(slow.Velocity):F1} m/s after four seconds of throttle from 10 in a turn");

        Rig fast = F2004Rig(dt);
        fast.LeftGroundRise = 0.0005;
        fast.Settle();
        fast.Velocity = new double3(0, 80.0, 0);
        double slip = 0.0;
        for (double t = 0.0; t < 12.0; t += dt)
        {
            fast.Step(new DriveInput(1.0, 1.0));
            slip = Math.Max(slip, fast.SlipDeg());
        }
        Assert.True(Vec.Len(fast.Velocity) > 75.0, $"fell from 80 to {Vec.Len(fast.Velocity):F1} m/s holding the lock");
        Assert.True(slip < 8.0, $"slid {slip:F0} deg off its heading");
    }

    // On the tyres alone below the speed the wings start to count, and on both above it.
    [Theory]
    [InlineData(100.0, 1.6, 2.4)]
    [InlineData(200.0, 2.2, 3.3)]
    [InlineData(300.0, 3.3, 4.5)]
    public void TheF2004CornersHarderTheFasterItGoes(double kmh, double least, double most)
    {
        foreach (double dt in new[] { 1.0 / 60.0, 0.021, 1.0 / 30.0 })
        {
            Rig rig = F2004Rig(dt);
            rig.LeftGroundRise = 0.0005;
            rig.Settle();
            rig.Velocity = new double3(0, kmh / 3.6, 0);
            double g = 0.0, lean = 0.0, slip = 0.0;
            int n = 0;
            for (double t = 0.0; t < 3.0; t += dt)
            {
                rig.Step(new DriveInput(0.5, 1.0));
                lean = Math.Max(lean, rig.RollDeg());
                slip = Math.Max(slip, rig.SlipDeg());
                if (t > 1.0) { g += Math.Abs(rig.Spin.X) * Vec.Len(rig.Velocity) / G; n++; }
            }
            Assert.InRange(g / n, least, most);
            Assert.True(lean < 3.0, $"rolled to {lean:F1} deg from {kmh} km/h at {dt * 1000:F0} ms");
            Assert.True(slip < 5.0, $"slid {slip:F1} deg off its heading from {kmh} km/h at {dt * 1000:F0} ms");
        }
    }

    // A close-ratio box drops the revs by the step between two gears, at once. Swept across the whole
    // range and slewed at a road car's rate, a change up is a second-long slide to half the revs.
    [Fact]
    public void TheF2004ChangesUpWithAStepInItsRevsAndNotASlide()
    {
        const double dt = 1.0 / 60.0;
        Rig rig = F2004Rig(dt);
        rig.Settle();
        int gear = 0, changes = 0;
        double before = 0.0, sinceChange = double.NaN;
        for (double t = 0.0; t < 14.0; t += dt)
        {
            double rpm = rig.Drive.Rpm;
            rig.Step(new DriveInput(1.0, 0.0));
            if (rig.Drive.Gear > gear) { gear = rig.Drive.Gear; before = rpm; sinceChange = 0.0; changes++; }
            if (double.IsNaN(sinceChange)) continue;
            sinceChange += dt;
            if (sinceChange < 0.15) continue;
            double kept = rig.Drive.Rpm / before;
            Assert.True(kept is > 0.55 and < 0.93, $"into gear {gear + 1} the revs are at {kept:P0} of what they were, 0.15 s on");
            sinceChange = double.NaN;
        }
        Assert.True(changes >= 5, $"only {changes} changes up in fourteen seconds");
        Assert.True(rig.Drive.Rpm > 0.9 * BuggyProfile.F2004.RedlineRpm, $"{rig.Drive.Rpm:F0} rpm flat out in top");
    }

    private static Rig RigFor(BuggyProfile p, double dt) =>
        p == BuggyProfile.F2004 ? F2004Rig(dt)
        : p == BuggyProfile.Eldorado ? new Rig(dt, p, Rig.EldoradoCom) { RigMass = 2336.0 }
        : new Rig(dt, p);

    public static IEnumerable<object[]> EveryCarAtEveryStep() =>
        from car in Enumerable.Range(0, BuggyProfile.All.Length)
        from dt in new[] { 1.0 / 60.0, 0.019, 0.021, 0.023, 1.0 / 40.0, 1.0 / 30.0, 0.05, 0.1 }
        select new object[] { car, dt };

    // The springs are an impulse a step, and past a step that depends on the car the roll mode
    // overshoots: the car rocks left and right every step, the unloaded tyre drives nothing and half
    // the drive is gone. It needs a seed, and no ground is level to half a millimetre. A sample can
    // land on a zero of the rocking, so this takes the worst of a whole second.
    [Theory]
    [MemberData(nameof(EveryCarAtEveryStep))]
    public void NoCarRocksOnItsSpringsOrLosesItsDriveAtAnyStep(int car, double dt)
    {
        BuggyProfile p = BuggyProfile.All[car];
        double atSixty = SpeedThreeSecondsAfterAKick(RigFor(p, 1.0 / 60.0), out _, out _, out _);
        double speed = SpeedThreeSecondsAfterAKick(RigFor(p, dt), out double yaw, out double roll, out double flip);

        Assert.True(yaw < 0.01, $"{p.DisplayName} still yaws at up to {yaw * 180 / Math.PI:F2} deg/s");
        Assert.True(roll < 0.01, $"{p.DisplayName} rocks at up to {roll * 180 / Math.PI:F2} deg/s");
        Assert.True(flip < 0.0005, $"{p.DisplayName}'s rear wheels swap {flip * 1000:F2} mm of travel a step");
        Assert.True(Math.Abs(speed - atSixty) < 0.06 * atSixty, $"{p.DisplayName} reaches {speed:F1} m/s, and {atSixty:F1} at 60 Hz");
    }

    // Full throttle from rest after a yaw kick, on ground half a millimetre higher under the left
    // wheels. The speed is three seconds in; the rest is the worst of the tenth second.
    private static double SpeedThreeSecondsAfterAKick(Rig rig, out double yaw, out double roll, out double flip)
    {
        rig.LeftGroundRise = 0.0005;
        rig.Settle();
        rig.Spin += new double3(0.2, 0, 0);
        yaw = roll = flip = 0.0;
        double speed = 0.0, last = 0.0;
        for (double t = 0.0; t < 10.0; t += rig.StepSeconds)
        {
            rig.Step(new DriveInput(1.0, 0.0));
            if (t < 3.0) speed = rig.Velocity.Y;
            double across = rig.Drive.Travel[2] - rig.Drive.Travel[3];
            if (t >= 9.0)
            {
                yaw = Math.Max(yaw, Math.Abs(rig.Spin.X));
                roll = Math.Max(roll, Math.Abs(rig.Spin.Y));
                flip = Math.Max(flip, Math.Abs(across - last));
            }
            last = across;
        }
        return speed;
    }

    [Theory]
    [InlineData(1.0 / 60.0)]
    [InlineData(0.021)]
    [InlineData(1.0 / 40.0)]
    [InlineData(1.0 / 30.0)]
    public void TheF2004ReachesItsSpeedsOnTimeWhateverTheStep(double dt)
    {
        Rig rig = F2004Rig(dt);
        rig.LeftGroundRise = 0.0005;
        rig.Settle();

        double to100 = double.NaN, to200 = double.NaN, to300 = double.NaN, slowestOfTheLastFive = double.PositiveInfinity;
        for (double t = 0.0; t < 40.0; t += dt)
        {
            rig.Step(new DriveInput(1.0, 0.0));
            double v = rig.Velocity.Y;
            if (double.IsNaN(to100) && v >= 100.0 / 3.6) to100 = t + dt;
            if (double.IsNaN(to200) && v >= 200.0 / 3.6) to200 = t + dt;
            if (double.IsNaN(to300) && v >= 300.0 / 3.6) to300 = t + dt;
            if (t >= 35.0) slowestOfTheLastFive = Math.Min(slowestOfTheLastFive, v);
        }

        Assert.InRange(to100, 2.1, 2.6);
        Assert.InRange(to200, 4.9, 5.5);
        Assert.InRange(to300, 9.4, 10.4);
        Assert.InRange(slowestOfTheLastFive, 93.0, 97.0);
        Assert.InRange(rig.Velocity.Y, 93.0, 97.0);
    }

    // At 90 m/s the launch pull is more than the engine has, so what is left after the air and the
    // tyres' rolling is its power.
    [Fact]
    public void TheF2004PullsWithItsPowerOverItsSpeed()
    {
        BuggyProfile p = BuggyProfile.F2004;
        Rig rig = F2004Rig(1.0 / 60.0);
        rig.Settle();
        rig.Velocity = new double3(0, 90.0, 0);
        for (int i = 0; i < 30; i++) rig.Step(new DriveInput(1.0, 0.0));
        double from = rig.Velocity.Y;
        for (int i = 0; i < 30; i++) rig.Step(new DriveInput(1.0, 0.0));
        double gained = (rig.Velocity.Y - from) / 0.5;

        double v = (from + rig.Velocity.Y) / 2.0;
        double drag = 0.5 * 1.225 * p.DragAreaM2 * v * v;
        double expected = (((p.PowerW / v) - drag) / 605.0) - (p.RollingResistance * G);
        Assert.InRange(gained, 0.9 * expected, expected);
    }

    // With no air there is nothing but the limiter to stop it, and a limiter that cuts too sharply
    // for the step hunts.
    [Theory]
    [InlineData(1.0 / 60.0)]
    [InlineData(0.05)]
    [InlineData(0.1)]
    public void InAVacuumTheF2004HoldsItsLimiterWithoutHunting(double dt)
    {
        Rig rig = F2004Rig(dt);
        rig.Air = 0.0;
        rig.Settle();
        double slowest = double.PositiveInfinity, fastest = 0.0;
        for (double t = 0.0; t < 40.0; t += dt)
        {
            rig.Step(new DriveInput(1.0, 0.0));
            if (t < 35.0) continue;
            slowest = Math.Min(slowest, rig.Velocity.Y);
            fastest = Math.Max(fastest, rig.Velocity.Y);
        }

        Assert.InRange(fastest, 0.97 * BuggyProfile.F2004.TopSpeed, BuggyProfile.F2004.TopSpeed);
        Assert.True(fastest - slowest < 0.05, $"hunts between {slowest:F2} and {fastest:F2} m/s");
    }

    // The wings are nothing without air, to the last bit: what they add is added only where they press.
    [Fact]
    public void WithoutAirTheWingsChangeNothing()
    {
        Rig winged = F2004Rig(1.0 / 60.0);
        Rig bare = new(1.0 / 60.0, BuggyProfile.F2004 with { DownforceAreaM2 = 0.0 }, Rig.F2004Com) { RigMass = 605.0, Box = new double3(0.5, 5.6, 2.0) };
        foreach (Rig rig in new[] { winged, bare })
        {
            rig.Air = 0.0;
            rig.LeftGroundRise = 0.0005;
            rig.Settle();
            for (int i = 0; i < 1200; i++) rig.Step(new DriveInput(i < 500 ? 1.0 : i < 800 ? 0.4 : -1.0, i < 300 ? 0.0 : i < 700 ? 1.0 : -1.0));
        }

        Assert.Equal(bare.Position, winged.Position);
        Assert.Equal(bare.Velocity, winged.Velocity);
        Assert.Equal(bare.Spin, winged.Spin);
    }

    [Theory]
    [InlineData(1.0 / 60.0)]
    [InlineData(1.0 / 30.0)]
    public void TheF2004StopsFromThreeHundredInAHundredAndThirtyMetresAndStraight(double dt)
    {
        Rig rig = F2004Rig(dt);
        rig.LeftGroundRise = 0.0005;
        rig.Settle();
        rig.Velocity = new double3(0, 300.0 / 3.6, 0);
        double from = rig.Position.Y, yaw = 0.0, highestHub = 0.0;
        for (double t = 0.0; t < 10.0 && rig.Velocity.Y > 1.0; t += dt)
        {
            rig.Step(new DriveInput(-1.0, 0.0));
            yaw = Math.Max(yaw, Math.Abs(rig.Spin.X));
            highestHub = Math.Max(highestHub, rig.HighestHub());
        }

        BuggyProfile p = BuggyProfile.F2004;
        Assert.True(rig.Velocity.Y <= 1.0, $"still at {rig.Velocity.Y:F1} m/s after ten seconds");
        Assert.InRange(rig.Position.Y - from, 100.0, 130.0);
        Assert.True(yaw < 0.02, $"yaws at up to {yaw * 180 / Math.PI:F1} deg/s");
        Assert.True(highestHub < p.Corners[0].Radius + p.DroopTravel, $"a hub {highestHub:F3} m up: the brakes lift the rear off the ground");
    }

    // Five g of brake through the tyres' contact pitches a 605 kg car until its rear wheels carry
    // nothing, and a rear with no load has no side grip: turning in under brakes then swaps the ends.
    [Theory]
    [InlineData(90.0, 1.0 / 60.0)]
    [InlineData(90.0, 1.0 / 30.0)]
    [InlineData(60.0, 1.0 / 60.0)]
    [InlineData(60.0, 0.021)]
    public void TheF2004BrakesIntoATurnWithoutSwappingEnds(double speed, double dt)
    {
        Rig rig = F2004Rig(dt);
        rig.LeftGroundRise = 0.0005;
        rig.Settle();
        rig.Velocity = new double3(0, speed, 0);
        double slip = 0.0, lean = 0.0, highestHub = 0.0, t = 0.0;
        for (; t < 15.0 && Vec.Len(rig.Velocity) > 5.0; t += dt)
        {
            rig.Step(new DriveInput(-1.0, 1.0));
            slip = Math.Max(slip, rig.SlipDeg());
            lean = Math.Max(lean, rig.RollDeg());
            highestHub = Math.Max(highestHub, rig.HighestHub());
        }

        BuggyProfile p = BuggyProfile.F2004;
        Assert.True(t < 15.0, $"still at {Vec.Len(rig.Velocity):F1} m/s after fifteen seconds of brake");
        Assert.True(slip < 15.0, $"slid {slip:F0} deg off its heading");
        Assert.True(lean < 3.0, $"rolled to {lean:F1} deg");
        Assert.True(highestHub < p.Corners[0].Radius + p.DroopTravel + 0.02, $"a hub {highestHub:F3} m up");
    }

    // The wings' grip is sideways too, and the load that gives it is what keeps it from tipping the car.
    [Theory]
    [InlineData(1.0 / 60.0)]
    [InlineData(1.0 / 30.0)]
    public void TheF2004CornersOnItsWingsWithoutLeaningOnThem(double dt)
    {
        BuggyProfile p = BuggyProfile.F2004;
        Rig rig = F2004Rig(dt);
        rig.LeftGroundRise = 0.0005;
        rig.Settle();
        rig.Velocity = new double3(0, 85.0, 0);

        double lean = 0.0, slip = 0.0, highestHub = 0.0, sideways = 0.0;
        int held = 0;
        for (double t = 0.0; t < 5.0; t += dt)
        {
            rig.Step(new DriveInput(1.0, t < 1.5 ? 1.0 : -1.0));
            lean = Math.Max(lean, rig.RollDeg());
            slip = Math.Max(slip, rig.SlipDeg());
            highestHub = Math.Max(highestHub, rig.HighestHub());
            if (t >= 0.5 && t < 1.5) { sideways += Math.Abs(rig.Spin.X) * Vec.Len(rig.Velocity); held++; }
        }

        Assert.True(sideways / held > 2.5 * G, $"corners at {sideways / held / G:F1} g at 85 m/s");
        Assert.True(lean < 3.0, $"rolled to {lean:F1} deg");
        Assert.True(slip < 10.0, $"slid {slip:F0} deg off its heading");
        Assert.True(highestHub < p.Corners[0].Radius + p.DroopTravel + 0.02, $"a hub {highestHub:F3} m up");
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
        public double Air = 1.225;
        // How much higher the ground stands under the left wheels than under the right.
        public double LeftGroundRise;
        public double StepSeconds => dt;
        // A solid cuboid's sides along X, Y and Z, for a car whose mass is declared as one.
        public double3? Box;
        private double3 Inertia => Box is { } b
            ? new double3((b.Y * b.Y) + (b.Z * b.Z), (b.X * b.X) + (b.Z * b.Z), (b.X * b.X) + (b.Y * b.Y)) * (RigMass / 12.0)
            : RigMass > 1000
            ? new double3((5.6 * 5.6) + (2.0 * 2.0), (1.0 * 1.0) + (2.0 * 2.0), (1.0 * 1.0) + (5.6 * 5.6)) * (RigMass / 12.0)
            : new double3(1, 1, 1) * (0.4 * RigMass * 1.1 * 1.1);

        // Where KSACarsGameData.xml puts the centre of mass.
        public static readonly double3 Com = new(0.60, -0.25, 0.0);
        public static readonly double3 EldoradoCom = new(0.55, 0.25, 0.0);
        public static readonly double3 F2004Com = new(0.30, -0.15, 0.0);
        private double3[] Hubs => _hubs ??= _p.Corners.Select(c => c.Hub - _com).ToArray();
        private double3[]? _hubs;

        public double HighestHub() => Hubs.Max(h => (Position + (Attitude * h)).X);

        public double RollDeg() => Math.Abs(Math.Asin(Math.Clamp((Attitude * new double3(0, 0, 1)).X, -1.0, 1.0))) * 180.0 / Math.PI;

        // How far the car's heading is off the way it is going, once it is going anywhere.
        public double SlipDeg()
        {
            double3 v = doubleQuat.Conjugate(Attitude) * Velocity;
            return Math.Abs(v.Y) < 2.0 ? 0.0 : Math.Abs(Math.Atan2(v.Z, Math.Abs(v.Y))) * 180.0 / Math.PI;
        }

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
                double ground = Hubs[i].Z > 0.0 ? LeftGroundRise : 0.0;
                contacts[i] = new WheelContact(true, world.X - ground, upBody, velBody + Vec.Cross(Spin, Hubs[i]));
            }

            DriveImpulse j = Drive.Step(input, contacts, Hubs, new double3(1, 0, 0), new double3(0, 1, 0),
                                        RigMass, Gravity, Air, dt);
            Velocity += Attitude * (j.Linear / RigMass);
            Spin += new double3(j.Angular.X / Inertia.X, j.Angular.Y / Inertia.Y, j.Angular.Z / Inertia.Z);

            Velocity += new double3(-Gravity * dt, 0, 0);
            Position += Velocity * dt;
            double rate = Vec.Len(Spin);
            if (rate > 0.0) Attitude *= doubleQuat.CreateFromAxisAngle(Spin / rate, rate * dt);
        }
    }
}
