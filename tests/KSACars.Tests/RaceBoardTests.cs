using Brutal.Numerics;
using Xunit;

namespace KSACars.Tests;

public class RaceBoardTests
{
    private const double Length = 1000.0, Line = 9.5;

    // Driven from one place along the route to another, a few metres a look, on the road.
    private static void Drive(RaceBoard.Tally car, double from, double to, bool onRoad = true)
    {
        for (double s = from; s < to; s += 4.0) car.See(s % Length, onRoad);
        car.See(to % Length, onRoad);
    }

    [Fact]
    public void ALapIsEveryCheckpointInOrderAndTheLineAgain()
    {
        Assert.Equal(4, RaceBoard.Gates(Length));
        Assert.Equal(12, RaceBoard.Gates(3565.0));

        RaceBoard.Tally car = new(Length, Line);
        car.See(Length - 11.0, true);
        Assert.Equal(1, car.Lap);
        Assert.Equal(-20.5, car.ProgressM, 6);
        Assert.Equal(Line, car.NextGateM, 9);

        // over the line, which starts the first lap, and round to just short of it again
        Drive(car, Length - 11.0, Length + Line - 1.0 + Length);
        Assert.Equal(1, car.Lap);
        Assert.Equal(4, car.Passed);
        Assert.False(car.Missed);

        Drive(car, Line - 1.0, Line + 2.0);
        Assert.Equal(2, car.Lap);
        Assert.Equal(Length + 2.0, car.ProgressM, 6);
    }

    [Fact]
    public void ACarThatCutsAcrossHasMissedACheckpointUntilItGoesBackForIt()
    {
        RaceBoard.Tally honest = new(Length, Line), cutter = new(Length, Line);
        Drive(honest, 0.0, 200.0);
        Drive(cutter, 0.0, 200.0);

        // found again 400 m on, past the checkpoint at 259.5 m without having been driven over it
        cutter.See(600.0, true);
        Drive(cutter, 600.0, 700.0);
        Drive(honest, 200.0, 300.0);
        Assert.True(cutter.Missed);
        Assert.Equal(1, cutter.Passed);
        Assert.Equal(250.0, cutter.ProgressM, 6);
        Assert.True(honest.ProgressM > cutter.ProgressM, "the car that cut across is ahead of the one that did not");

        // round to the line it goes, and is still on its first lap
        Drive(cutter, 700.0, Length + Line + 5.0);
        Assert.Equal(1, cutter.Lap);

        // back before the one it missed and over it, and it counts again
        cutter.See(240.0, true);
        Drive(cutter, 240.0, 300.0);
        Assert.Equal(2, cutter.Passed);
        Assert.False(cutter.Missed);
    }

    [Fact]
    public void ACheckpointIsNotGoneThroughOffTheRoadNorBackwards()
    {
        RaceBoard.Tally car = new(Length, Line);
        Drive(car, 0.0, 200.0);
        Drive(car, 200.0, 300.0, onRoad: false);
        Assert.Equal(1, car.Passed);

        RaceBoard.Tally back = new(Length, Line);
        back.See(20.0, true);
        back.See(12.0, true);
        back.See(4.0, true);
        Assert.Equal(0, back.Passed);
        Assert.Equal(1, back.Lap);
    }

    // Driven on at a steady speed from where and when it is, a look every tenth of a second; where and when it then is.
    private static (double S, double T) Timed(RaceBoard.Tally car, double s, double t, double metres, double speed)
    {
        for (double gone = 0.0; gone < metres - 1e-9; )
        {
            double step = Math.Min(speed * 0.1, metres - gone);
            (gone, s, t) = (gone + step, s + step, t + (step / speed));
            car.See(s % Length, true, t);
        }
        return (s, t);
    }

    [Fact]
    public void ALapIsTimedFromLineToLineAndEachCheckpointAgainstTheBestLap()
    {
        RaceBoard.Tally car = new(Length, Line);
        car.See(0.0, true, 100.0);
        Assert.Null(car.LapSoFar(100.0));

        // over the line at 10 m/s, 0.95 s after the off, and a lap of 100 s
        (double s, double t) = Timed(car, 0.0, 100.0, Line + 500.0, 10.0);
        Assert.Equal(50.0, car.LapSoFar(t)!.Value, 6);
        Assert.Null(car.Split);
        (s, t) = Timed(car, s, t, 500.0, 10.0);
        Assert.Equal(100.0, car.LastLapS!.Value, 6);
        Assert.Equal(100.0, car.BestLapS!.Value, 6);
        Assert.Null(car.Split);

        // the next at 12.5 m/s: 5 s up at the first checkpoint, 250 m on, and 20 s up at the line
        (s, t) = Timed(car, s, t, 260.0, 12.5);
        Assert.Equal(-5.0, car.Split!.Value.DeltaS, 6);
        (s, t) = Timed(car, s, t, 740.0, 12.5);
        Assert.Equal(-20.0, car.Split!.Value.DeltaS, 6);
        Assert.Equal(80.0, car.LastLapS!.Value, 6);
        Assert.Equal(80.0, car.BestLapS!.Value, 6);

        // and a slow one is measured against that, and leaves the best as it was
        (s, t) = Timed(car, s, t, 260.0, 10.0);
        Assert.Equal(5.0, car.Split!.Value.DeltaS, 6);
        Timed(car, s, t, 740.0, 10.0);
        Assert.Equal(100.0, car.LastLapS!.Value, 6);
        Assert.Equal(80.0, car.BestLapS!.Value, 6);
    }

    [Fact]
    public void TimesAreWrittenAsALapAndASplitAre()
    {
        Assert.Equal("1:26.540", RaceBoard.Clock(86.54));
        Assert.Equal("0:09.005", RaceBoard.Clock(9.005));
        Assert.Equal("-0.312", RaceBoard.Gap(-0.312));
        Assert.Equal("+1.045", RaceBoard.Gap(1.045));
    }

    [Fact]
    public void TheOrderIsByHowFarEachHasGone()
    {
        double[] totals = [2500.0, 2600.0, -3.0, 2600.0];
        Assert.Equal(3, RaceBoard.Position(totals, 0));
        Assert.Equal(1, RaceBoard.Position(totals, 1));
        Assert.Equal(4, RaceBoard.Position(totals, 2));
        Assert.Equal(1, RaceBoard.Position(totals, 3));
    }

    [Fact]
    public void TheMapHasWhatIsAheadOfTheCarAtTheTopAndWhatIsToItsRightOnTheRight()
    {
        double3 centre = new(6_371_000.0, 0.0, 0.0), north = new(0, 0, 1), east = new(0, 1, 0);

        // facing north: 50 m north is up the picture, 30 m east to the right
        (double right, double down) = RaceBoard.OnMap(centre + (north * 50.0) + (east * 30.0), centre, north);
        Assert.Equal(30.0, right, 6);
        Assert.Equal(-50.0, down, 6);

        // turned to face east, the same place is 30 m up the picture and 50 m to the left
        (right, down) = RaceBoard.OnMap(centre + (north * 50.0) + (east * 30.0), centre, east);
        Assert.Equal(-50.0, right, 6);
        Assert.Equal(-30.0, down, 6);
    }
}
