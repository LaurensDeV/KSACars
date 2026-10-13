using Xunit;

namespace KSACars.Tests;

// Apart from PracticeTests so the two are run side by side: between them they are most of a minute.
public class PracticeCircuitTests
{
    // Enough laps for the quickest pace everywhere and a start on the driver's settings.
    private const int Laps = 40;

    public static TheoryData<string> Circuits() => [.. ExtremeCircuits.Names];

    [Theory]
    [MemberData(nameof(Circuits))]
    public void OnAnyCircuitWhatPractiseFindsIsNoSlowerThanRacingBlind(string circuit)
    {
        AutopilotTests.Track track = ExtremeLapTests.Laid(circuit);
        foreach (TrackCar car in TrackCar.All)
        {
            if (PracticeTests.Practised(track, car, out _, Laps) is not { } found) continue;
            Assert.True(found.Seconds <= found.BlindSeconds + 0.01, $"{car.Name}: {found.Seconds:F1} s practised and {found.BlindSeconds:F1} s blind");
        }
    }
}
