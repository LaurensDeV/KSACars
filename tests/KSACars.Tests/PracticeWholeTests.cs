using Xunit;

namespace KSACars.Tests;

// Apart from PracticeTests so the two are run side by side: this one is half a minute.
public class PracticeWholeTests
{
    [Fact]
    public void AllOfAPracticeHasTheF2004RoundClubMotorsportsAsQuickAsItIsDrivenByHand() =>
        PracticeTests.PractisesClub("F2004", Practice.MostDrives, 76.9);
}
