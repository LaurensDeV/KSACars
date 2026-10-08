using System.Globalization;
using System.Text;
using Xunit;

namespace KSACars.Tests;

// Many ways of driving one circuit file tried at once, for a search after a faster lap:
// tools/roads/personal-best.py writes the candidates and reads the times. A candidate is only as fast
// as its slowest clean flying lap over several kinds of step, since the game's are not the rig's.
public class LapSearchTests
{
    private static readonly double[][] Steps = [ExtremeLapTests.GameSteps, [1.0 / 60.0], [0.020], [0.025]];

    // And with the tyres holding this much less than the driver takes them to, at two kinds of step: a
    // lap that is only clean with all the grip there is, is not one the game gives twice.
    private const double LessGrip = 0.94;

    // The fifth part of a candidate, where it has one: metres to the left at places round the lap, "0.2;-0.5;0".
    private static List<double>? Nudges(string[] parts) => parts.Length > 4 && parts[4].Trim().Length > 0
        ? [.. parts[4].Split(';').Select(n => double.Parse(n, CultureInfo.InvariantCulture))]
        : null;

    [Fact]
    public void EveryCandidateIsLappedAndTimed()
    {
        if (Environment.GetEnvironmentVariable("KSACARS_CANDIDATES") is not { Length: > 0 } file) return;
        string circuitFile = Environment.GetEnvironmentVariable("KSACARS_CIRCUIT")!;
        Circuit circuit = Circuit.FromJson(File.ReadAllText(circuitFile), out string why, out _) ?? throw new InvalidOperationException(why);
        TrackCar car = TrackCar.Of(Environment.GetEnvironmentVariable("KSACARS_CAR") ?? "F2004");
        TrackWorld world = TrackWorld.Earth();
        AutopilotTests.Track track = new(world, circuit, world.Surface(circuit));

        string[] lines = [.. File.ReadAllLines(file).Where(l => l.Trim().Length > 0)];
        string[] results = new string[lines.Length];
        Parallel.For(0, lines.Length, i =>
        {
            // name | settings | metres kept inside each edge | push | the line's nudges
            string[] parts = lines[i].Split('|');
            try
            {
                Autopilot.Tuning tune = new Autopilot.Tuning().With(parts[1], out string bad) ?? throw new InvalidOperationException(bad);
                double inside = double.Parse(parts[2], CultureInfo.InvariantCulture), push = double.Parse(parts[3], CultureInfo.InvariantCulture);
                Route route = Route.Of(circuit, TrackWorld.DirOf, world.RadiusM, world.HeightAt, TrackWorld.LiftM, TrackWorld.SpacingM, null, 0.0,
                                       out string noRoute, Autopilot.TurnRadius(car.Profile), inside, Nudges(parts)) ?? throw new InvalidOperationException(noRoute);
                double worst = 0.0, sum = 0.0;
                string fault = "";
                foreach (double[] steps in Steps)
                {
                    AutopilotTests.Lap lap = AutopilotTests.Drive(car, track, route, steps[0], laps: 2, timeout: 400.0,
                                                                  pattern: steps.Length > 1 ? steps : null, push: push, tune: tune);
                    LapSummary s = lap.Summary;
                    if (lap.End != LapEnd.Finished) fault = lap.End.ToString();
                    else if (s.OffAsphaltSeconds > 0.0) fault = "off the asphalt";
                    else if (lap.HullSteps > 0) fault = "hull down";
                    if (fault.Length > 0) break;
                    worst = Math.Max(worst, s.LastLapSeconds);
                    sum += s.LastLapSeconds;
                }
                TrackCar slippery = car with { Profile = car.Profile with { FrontGrip = car.Profile.FrontGrip * LessGrip, RearGrip = car.Profile.RearGrip * LessGrip } };
                Autopilot.Tuning asBefore = tune with { GripShare = tune.GripShare / LessGrip };
                foreach (double[] steps in new[] { ExtremeLapTests.GameSteps, [0.025] })
                {
                    if (fault.Length > 0) break;
                    AutopilotTests.Lap lap = AutopilotTests.Drive(slippery, track, route, steps[0], laps: 2, timeout: 400.0,
                                                                  pattern: steps.Length > 1 ? steps : null, push: push, tune: asBefore);
                    if (lap.End != LapEnd.Finished || lap.Summary.OffAsphaltSeconds > 0.0 || lap.HullSteps > 0) fault = "not clean on less grip";
                }
                results[i] = fault.Length > 0 ? $"{parts[0].Trim()}|fault|{fault}" : string.Create(CultureInfo.InvariantCulture, $"{parts[0].Trim()}|{worst:F3}|{sum / Steps.Length:F3}");
            }
            catch (Exception e)
            {
                results[i] = $"{parts[0].Trim()}|fault|{e.Message.Split('\n')[0]}";
            }
        });
        File.WriteAllLines(file + ".out", results, new UTF8Encoding(false));
    }
}
