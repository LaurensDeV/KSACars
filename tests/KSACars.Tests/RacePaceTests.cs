using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class RacePaceTests
{
    private const double Cap = 1.0 / 30.0;

    // A host drawing 60 frames a second and a guest drawing 20, raced for two minutes of the wall: what the guest was behind at each moment.
    private static List<double> Race(bool holdTheRace, bool makeUpFrames, double guestFrameSeconds = 0.05)
    {
        Random noise = new(7);
        SharedPace shared = new();
        PaceMeter guestPace = new(), hostPace = new();
        double wall = 0.0, hostU = 1000.0, guestU = 1000.0, pace = 1.0, sendNext = 0.0;
        List<double> behind = [];
        const double dt = 0.05, hostFrame = 1.0 / 60.0;
        for (int i = 0; i < 2400; i++)
        {
            wall += dt;
            // A world goes at the speed asked times what of each frame KSA steps.
            double hostAsked = makeUpFrames ? ClockLock.Asked(pace, hostFrame, Cap) : pace;
            hostU += hostAsked * (Math.Min(hostFrame, Cap) / hostFrame) * dt;
            double gap = hostU - guestU;
            double wanted = ClockLock.Speed(gap, pace);
            double guestAsked = makeUpFrames ? ClockLock.Asked(wanted, guestFrameSeconds, Cap) : wanted;
            guestU += guestAsked * (Math.Min(guestFrameSeconds, Cap) / guestFrameSeconds) * dt;
            behind.Add(hostU - guestU);

            hostPace.Add(wall, hostU);
            if (wall >= sendNext)
            {
                // A state 30 times a second, arriving over a relay that holds each up by 150 to 210 ms.
                guestPace.Add(wall + 0.15 + (noise.NextDouble() * 0.06), guestU);
                sendNext += 1.0 / 30.0;
            }
            if (holdTheRace) pace = shared.Update(dt, hostPace.Rate, [(guestPace.Rate, hostU - guestU)]);
        }
        return behind;
    }

    [Fact]
    public void AGuestDrawingTwentyFramesASecondKeepsUpWhenTheRaceIsHeldToItsPace()
    {
        List<double> held = Race(holdTheRace: true, makeUpFrames: true);
        Assert.True(held.Skip(800).Max() < 0.8, $"held to the guest's pace, it was at worst {held.Skip(800).Max():F2} s behind: {string.Join(" ", held.Where((_, i) => i % 40 == 0).Select(b => b.ToString("F2")))}");

        // As the race was run: the guest's speed no more than a tenth over the host's, its slow frames losing the rest.
        List<double> before = Race(holdTheRace: false, makeUpFrames: false);
        Assert.True(before[^1] > 30.0, $"run as before it was {before[^1]:F1} s behind");

        // The host slowing down is not enough by itself: a world that loses a third of each frame never makes up a pace it is asked a tenth over.
        List<double> heldOnly = Race(holdTheRace: true, makeUpFrames: false);
        Assert.True(heldOnly[^1] > 5.0, $"held without the frames made up it was {heldOnly[^1]:F1} s behind");
    }

    [Fact]
    public void ARaceIsNotHeldBackWhileEveryGuestKeepsUp()
    {
        List<double> both = Race(holdTheRace: true, makeUpFrames: true, guestFrameSeconds: 1.0 / 45.0);
        Assert.True(both.Max() < 0.2, $"a guest at 45 frames a second was {both.Max():F2} s behind");

        SharedPace shared = new();
        for (int i = 0; i < 2000; i++) shared.Update(0.05, 1.0, [(0.999, 0.02), (1.001, -0.01)]);
        Assert.Equal(1.0, shared.Pace);
    }

    [Fact]
    public void AHeldRaceGoesBackToFullPaceOnceTheSlowGuestKeepsUpAgain()
    {
        SharedPace shared = new();
        for (int i = 0; i < 100; i++) shared.Update(0.05, 1.0, [(0.7, 2.0)]);
        Assert.InRange(shared.Pace, 0.66, 0.70);
        // Now its frames are quick again and it keeps up at whatever it is held to.
        double seconds = 0.0;
        while (shared.Pace < 1.0 && seconds < 120.0)
        {
            shared.Update(0.05, shared.Pace, [(shared.Pace, 0.05)]);
            seconds += 0.05;
        }
        Assert.Equal(1.0, shared.Pace);
        Assert.InRange(seconds, 25.0, 35.0);
        // Held no lower than its floor, however slow a guest.
        for (int i = 0; i < 400; i++) shared.Update(0.05, shared.Pace, [(0.2, 9.0)]);
        Assert.Equal(SharedPace.Floor, shared.Pace);
    }

    [Fact]
    public void PaceIsMeasuredThroughJitterAndLateArrivals()
    {
        Random noise = new(3);
        PaceMeter meter = new();
        Assert.Null(meter.Rate);
        for (int i = 0; i < 200; i++)
        {
            double sent = i / 30.0;
            meter.Add(sent + 0.2 + (noise.NextDouble() * 0.08), 500.0 + (0.7 * sent));
        }
        Assert.InRange(meter.Rate!.Value, 0.69, 0.71);
        // A clock set back far, as by a world loaded, starts the measuring again.
        meter.Add(10.0, 10.0);
        Assert.Null(meter.Rate);
    }

    [Fact]
    public void ACarFromAWorldRunningSlowerIsDrawnWhereItIs()
    {
        // The sender's world runs at 0.7 of this one's; the car goes 30 m every second of this world's clock.
        RemoteTrack track = new();
        Random noise = new(5);
        double worst = 0.0;
        for (int i = 0; i < 600; i++)
        {
            double t = i / 30.0;
            NetWire.State state = new(2, (uint)(i + 1), 2000.0 + (0.7 * t), new RacePose(30.0 * t, 5000.0, default, doubleQuat.Identity, new double3(30, 0, 0), default),
                                      1f, 0f, 0f, 3, false);
            track.Add(state, t + 0.05 + (noise.NextDouble() * 0.03));
            if (t < 6.0) continue;
            double now = t + 0.08;
            RacePose drawn = track.At(now)!.Value.Pose;
            worst = Math.Max(worst, Math.Abs(drawn.S - (30.0 * (now - 0.05 - RemoteTrack.DelaySeconds))));
        }
        Assert.True(worst < 2.0, $"drawn up to {worst:F1} m from where it was");
    }

    [Fact]
    public void HowFarBehindAGuestIsComesWithItsStateAndAnOldStateHasNone()
    {
        NetWire.State state = new(3, 9, 12.5, new RacePose(1.0, 100.0, default, doubleQuat.Identity, default, default), 1f, 0f, 0f, 2, true, 0.75f);
        byte[] buffer = new byte[NetWire.BufferBytes];
        int n = NetWire.Write(state, buffer);
        Assert.Equal(0.75f, Assert.IsType<NetWire.State>(NetWire.Read(buffer.AsSpan(0, n), out _)).Behind);
        // As a game that does not say it sends it: four bytes fewer.
        Assert.True(float.IsNaN(Assert.IsType<NetWire.State>(NetWire.Read(buffer.AsSpan(0, n - 4), out _)).Behind));
    }

    [Fact]
    public void AFrameTooSlowForKsasStepIsMadeUpByTheSpeedAndNoFurther()
    {
        Assert.Equal(1.0, ClockLock.Asked(1.0, 1.0 / 60.0, Cap), 9);
        Assert.Equal(0.8 * 1.2, ClockLock.Asked(0.8, 0.04, Cap), 9);
        Assert.Equal(ClockLock.MostSpeed, ClockLock.Asked(1.0, 0.05, Cap), 9);
        Assert.Equal(0.0, ClockLock.Asked(0.0, 0.05, Cap), 9);
    }
}
