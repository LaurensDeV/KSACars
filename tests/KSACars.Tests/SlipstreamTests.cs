using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class SlipstreamTests
{
    private static readonly double3 Here = new(6_371_000.0, 0.0, 0.0), East = new(0, 1, 0), North = new(0, 0, 1);

    [Fact]
    public void ACarRightBehindAnotherIsSparedMostAndTheWakeFadesAwayAndToTheSide()
    {
        double close = Slipstream.Tow(Here, East, Here + (East * 5.0), East);
        double far = Slipstream.Tow(Here, East, Here + (East * 40.0), East);
        double aside = Slipstream.Tow(Here, East, Here + (East * 5.0) + (North * 1.2), East);
        Assert.InRange(close, 0.8 * Slipstream.MostShare, Slipstream.MostShare);
        Assert.True(far > 0.0 && far < close && aside < close, $"{close:F2} close, {far:F2} far and {aside:F2} aside");

        Assert.Equal(0.0, Slipstream.Tow(Here, East, Here + (East * 60.0), East));
        Assert.Equal(0.0, Slipstream.Tow(Here, East, Here + (East * 5.0) + (North * 2.0), East));
        Assert.Equal(0.0, Slipstream.Tow(Here, East, Here - (East * 5.0), East));
        // nor from a car crossing or coming the other way
        Assert.Equal(0.0, Slipstream.Tow(Here, East, Here + (East * 5.0), North));
        Assert.Equal(0.0, Slipstream.Tow(Here, East, Here + (East * 5.0), -East));
    }

    [Fact]
    public void InTheWakeACarGoesFasterDownAStraight()
    {
        foreach (BuggyProfile car in BuggyProfile.All)
        {
            double Flat(double tow)
            {
                BuggyDriveTests.Rig rig = BuggyDriveTests.RigFor(car, 1.0 / 60.0);
                rig.Settle();
                rig.Drive.Tow = tow;
                for (int i = 0; i < 20 * 60; i++) rig.Step(new DriveInput(1.0, 0.0));
                return Vec.Len(rig.Velocity);
            }
            double free = Flat(0.0), towed = Flat(Slipstream.MostShare);
            Assert.True(towed > free + 0.3, $"{car.DisplayName}: {towed:F1} m/s towed and {free:F1} m/s not");
        }
    }
}
