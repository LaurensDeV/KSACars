using System.Text;
using Xunit;

namespace KSACars.Tests;

// Every car round circuit files as the game reads them, which is how a circuit made for the game is
// driven here first: tools/roads/rig-laps.sh. Nothing is asked of a lap, because a road no car can
// climb is a fair thing to draw; the table is what is read.
public class CircuitFileLapTests
{
    [Fact]
    public void EveryCarLapsEveryCircuitFileItIsPointedAt()
    {
        if (Environment.GetEnvironmentVariable("KSACARS_CIRCUITS") is not { Length: > 0 } folder) return;
        string prefix = Environment.GetEnvironmentVariable("KSACARS_ONLY") ?? "";
        string traced = Environment.GetEnvironmentVariable("KSACARS_TRACE") ?? "";
        string? into = Environment.GetEnvironmentVariable("KSACARS_LAPS_OUT");

        StringBuilder table = new();
        foreach (string file in Directory.GetFiles(folder, "*.json").Order())
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (Circuit.FromJson(File.ReadAllText(file), out string why, out _) is not { } circuit)
            {
                table.AppendLine($"{name,-12} not read: {why}");
                continue;
            }

            TrackWorld world = TrackWorld.Earth();
            AutopilotTests.Track track = new(world, circuit, world.Surface(circuit));
            foreach (TrackCar car in TrackCar.All)
            {
                Route? route = Route.Of(circuit, TrackWorld.DirOf, world.RadiusM, world.HeightAt, TrackWorld.LiftM, TrackWorld.SpacingM,
                                        null, 0.0, out why, Autopilot.TurnRadius(car.Profile));
                if (route is null)
                {
                    table.AppendLine($"{name,-12} {car.Name,-9} no route: {why}");
                    continue;
                }

                LapRow[]? rows = traced == car.Name ? new LapRow[400_000] : null;
                AutopilotTests.Lap lap = AutopilotTests.Drive(car, track, route, 1.0 / 60.0, timeout: 900.0, pattern: ExtremeLapTests.GameSteps, rows: rows);
                LapSummary s = lap.Summary;
                table.AppendLine($"{name,-12} {car.Name,-9} {lap.End,-9} {s.ProgressM,6:F0}/{route.LengthM,6:F0} m {s.Seconds,6:F1} s  "
                                 + $"vmax {s.MaxSpeed,5:F1}  cross {s.MaxCrossM,5:F2}  off {s.OffAsphaltSeconds,5:F2}  flight {s.LongestFlightSeconds,5:F2}  "
                                 + $"hub {s.MinHubM:F2}..{s.MaxHubM:F2}  roll {s.MaxRollDeg:F1}  pitch {s.MaxPitchDeg:F1}  hull {lap.HullSteps}");

                if (rows is not null && into is not null)
                {
                    StringBuilder steps = new(LapRow.Header + "\n");
                    for (int i = 0; i < lap.Pilot.RowCount; i++)
                    {
                        rows[i].AppendTo(steps);
                        steps.Append('\n');
                    }
                    File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(into))!, $"{name}-{car.Name}.csv"), steps.ToString());
                }
            }
        }
        if (into is not null) File.WriteAllText(into, table.ToString());
    }
}
