using Xunit;

namespace KSACars.Tests;

/// <summary>
/// What the bridge reads out of a command file. A refusal here is a reply the agent can read,
/// where a crash in the game's frame loop would be the game.
/// </summary>
public class BridgeCommandTests
{
    [Fact]
    public void ACommandCarriesItsIdNameAndArguments()
    {
        Assert.True(BridgeCommand.TryParse("""{"id":"7","cmd":"Drive","throttle":1,"seconds":"6"}""",
                                           out BridgeCommand? c, out _));

        Assert.Equal("7", c!.Id);
        Assert.Equal("drive", c.Name);
        Assert.Equal(1.0, c.Number("throttle", 0.0));
        Assert.Equal(6.0, c.Number("seconds", 0.0));
        Assert.Equal(-1.0, c.Number("steer", -1.0));
    }

    [Theory]
    [InlineData("""{"cmd":"status"}""")]
    [InlineData("""{"id":"a/b","cmd":"status"}""")]
    [InlineData("""{"id":"1"}""")]
    [InlineData("""[1,2]""")]
    [InlineData("""not json""")]
    public void AnUnreadableCommandIsRefusedWithAReason(string text)
    {
        Assert.False(BridgeCommand.TryParse(text, out _, out string trouble));
        Assert.NotEmpty(trouble);
    }
}
