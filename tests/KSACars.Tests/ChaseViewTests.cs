using Xunit;

namespace KSACars.Tests;

public class ChaseViewTests
{
    // KSA springs its camera towards the view and is fed the view's own rate of change, so a drag that
    // the chase wrote over every frame threw the camera round at the speed of the mouse. A drag is the
    // chase's own look-round instead: it holds while the button is down and comes back after.
    [Fact]
    public void AMouseDragLooksRoundTheCarAndIsNotThrownAway()
    {
        const double dt = 1.0 / 60.0;
        double look = 0.0;
        for (int i = 0; i < 10; i++) look = ChaseView.Looked(look, 0.05, held: true, dt);
        Assert.Equal(0.5, look, 9);

        // Let go, and the view comes back behind the car: within a couple of degrees after three seconds.
        for (double t = 0.0; t < 3.0; t += dt) look = ChaseView.Looked(look, 0.0, held: false, dt);
        Assert.True(Math.Abs(look) < 0.035, $"{look * 180.0 / Math.PI:F1} deg off the car's heading three seconds after the drag");
    }

    // A drag past the look-round's own limit stops the view there rather than pulling it against what is
    // written: the camera takes only as much of a drag as the clamp leaves.
    [Fact]
    public void ADragPastFullLockIsTakenNoFurther()
    {
        double look = 0.0;
        for (int i = 0; i < 200; i++) look = ChaseView.Looked(look, 0.05, held: true, 1.0 / 60.0);
        Assert.Equal(ChaseView.LookRoundRad, look, 9);
        Assert.Equal(look, ChaseView.Looked(look, 0.05, held: true, 1.0 / 60.0), 9);
    }
}
