using Xunit;

namespace KSACars.Tests;

public class ChaseViewTests
{
    // KSA springs its camera towards the view and is fed the view's own rate of change, so an angle the
    // chase writes over a drag throws the camera round at the speed of the mouse. While the view is
    // dragged the chase writes neither angle; this is what brings it back behind the car after.
    [Fact]
    public void AViewLeftOffTheCarComesBackBehindIt()
    {
        const double dt = 1.0 / 60.0, heading = 0.7;
        double azimuth = heading + 2.0;
        for (double t = 0.0; t < 1.0; t += dt) azimuth = ChaseView.Follow(azimuth, heading, 0.0, dt);
        Assert.True(Math.Abs(azimuth - heading) < 0.09, $"{(azimuth - heading) * 180.0 / Math.PI:F1} deg off the car's heading a second on");

        for (double t = 0.0; t < 1.0; t += dt) azimuth = ChaseView.Follow(azimuth, heading, 0.0, dt);
        Assert.True(Math.Abs(azimuth - heading) < 0.01, $"{(azimuth - heading) * 180.0 / Math.PI:F1} deg off it two seconds on");
    }

    // A drag goes anywhere KSA's own camera does, where the stick is held to what the chase is tilted to.
    [Fact]
    public void ADraggedTiltReachesFurtherThanTheSticks()
    {
        Assert.True(ChaseView.HighestDraggedRad < ChaseView.HighestRad, "a drag cannot be raised past the stick");
        Assert.True(ChaseView.LowestDraggedRad > ChaseView.LowestRad, "a drag cannot be lowered past the stick");
        Assert.Equal(ChaseView.LowestRad, ChaseView.Tilt(0.9, 0.0, 1.0 / 60.0), 9);
    }
}
