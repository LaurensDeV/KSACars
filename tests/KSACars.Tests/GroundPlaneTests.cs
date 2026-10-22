using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class GroundPlaneTests
{
    private static readonly double3 Up = new(1, 0, 0);
    private static readonly double3[] Hubs = [new(0, 1.6, 0.8), new(0, 1.6, -0.8), new(0, -1.5, 0.8), new(0, -1.5, -0.8)];

    // The ground falls away ahead at this slope; each hub a third of a metre above it, straight up.
    private static WheelContact[] OnSlope(double slope, double3 velocity)
    {
        WheelContact[] contacts = new WheelContact[4];
        for (int i = 0; i < 4; i++) contacts[i] = new WheelContact(true, 0.33 + (slope * Hubs[i].Y), Up, velocity);
        return contacts;
    }

    [Fact]
    public void ACarRunningDownASlopeIsNotClosingOnTheGround()
    {
        const double slope = 0.10, speed = 60.0;
        double3 downhill = Vec.Unit(new double3(-slope, 1.0, 0.0)) * speed;
        WheelContact[] contacts = OnSlope(slope, downhill);

        Assert.True(-Vec.Dot(contacts[0].HubVelocity, contacts[0].GroundUp) > 5.0, "straight up, the damper sees 6 m/s of closing");

        GroundPlane.Tilt(contacts, Hubs);

        Assert.All(contacts, c => Assert.True(Math.Abs(Vec.Dot(c.HubVelocity, c.GroundUp)) < 1e-9));
        Assert.True(Vec.Len(contacts[0].GroundUp - Vec.Unit(new double3(1.0, slope, 0.0))) < 1e-9);
        Assert.Equal(0.33 * Math.Cos(Math.Atan(slope)), contacts[2].HubHeight - (slope * Hubs[2].Y * Math.Cos(Math.Atan(slope))), 9);
    }

    [Fact]
    public void LevelGroundIsLeftAsItIsAndSoIsACarWithAWheelOverNothing()
    {
        WheelContact[] level = OnSlope(0.0, default);
        GroundPlane.Tilt(level, Hubs);
        Assert.All(level, c => Assert.True(Vec.Len(c.GroundUp - Up) < 1e-12));
        Assert.Equal(0.33, level[1].HubHeight, 12);

        WheelContact[] hung = OnSlope(0.05, default);
        hung[3] = default;
        GroundPlane.Tilt(hung, Hubs);
        Assert.True(Vec.Len(hung[0].GroundUp - Up) < 1e-12);

        WheelContact[] wall = OnSlope(2.5, default);
        GroundPlane.Tilt(wall, Hubs);
        Assert.True(Vec.Len(wall[0].GroundUp - Up) < 1e-12);

        // A road is laid as steep as one in one and a bit over, and that is still a road.
        WheelContact[] steep = OnSlope(1.2, default);
        GroundPlane.Tilt(steep, Hubs);
        Assert.True(Vec.Dot(steep[0].GroundUp, Up) < 0.7);
    }
}
