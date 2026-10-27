using Xunit;

namespace KSACars.Tests;

public class ChaseViewTests
{
    private const double Dt = 1.0 / 60.0;

    // KSA springs its camera towards the view and is fed the view's own rate of change, so an angle the
    // chase writes over a drag throws the camera round at the speed of the mouse. While the view is
    // dragged the chase writes neither angle; this is what brings it back behind the car after, and a
    // view left half a turn round has to come back at a walk and not a whip.
    [Fact]
    public void AViewLeftOffTheCarComesBackBehindItAtAWalk()
    {
        const double heading = 0.7;
        double azimuth = heading + 2.6, worst = 0.0;
        for (double t = 0.0; t < 3.0; t += Dt)
        {
            double was = azimuth;
            azimuth = ChaseView.Follow(azimuth, heading, 0.0, Dt);
            worst = Math.Max(worst, Math.Abs(azimuth - was) / Dt);
        }

        Assert.True(worst <= ChaseView.MostTurnRadPerSec + 1e-9, $"came round at {worst * 180.0 / Math.PI:F0} deg/s");
        Assert.True(Math.Abs(azimuth - heading) < 0.02, $"{(azimuth - heading) * 180.0 / Math.PI:F1} deg off the car's heading three seconds on");
    }

    // A view a turn and more round, which is where KSA's camera leaves a long drag, comes back the few
    // degrees it really is off the car and not the turn its number says.
    [Fact]
    public void AViewAWholeTurnRoundComesBackTheShortWay()
    {
        double azimuth = (2.0 * Math.PI) + 0.3;
        double moved = ChaseView.Follow(azimuth, 0.1, 0.0, Dt) - azimuth;
        Assert.True(moved < 0.0 && Math.Abs(moved) < 0.05, $"moved {moved * 180.0 / Math.PI:F2} deg in a frame");
    }

    // A drag goes anywhere KSA's own camera does, where the stick is held to what the chase is tilted to.
    [Fact]
    public void ADraggedTiltReachesFurtherThanTheSticks()
    {
        Assert.True(ChaseView.HighestDraggedRad < ChaseView.HighestRad, "a drag cannot be raised past the stick");
        Assert.True(ChaseView.LowestDraggedRad > ChaseView.LowestRad, "a drag cannot be lowered past the stick");
        Assert.Equal(ChaseView.LowestRad, ChaseView.Tilt(0.9, 0.0, Dt), 9);
    }
}
