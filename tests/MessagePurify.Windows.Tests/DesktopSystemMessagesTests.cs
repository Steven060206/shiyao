using MessagePurify.Windows.App;

namespace MessagePurify.Windows.Tests;

public sealed class DesktopSystemMessagesTests
{
    [Theory]
    [InlineData(0x7e, 0)]
    [InlineData(0x1a, 0)]
    [InlineData(0x2e0, 0)]
    [InlineData(0x218, 7)]
    [InlineData(0x218, 18)]
    [InlineData(0xc123, 0)]
    public void DisplayAndResumeRequestCurrentCards(int message, int parameter) =>
        Assert.True(DesktopSystemMessages.ShouldRefresh(message, parameter, 0xc123));

    [Theory]
    [InlineData(0x218, 4)]
    [InlineData(0x218, 0)]
    [InlineData(0xf, 0)]
    [InlineData(0, 0)]
    public void OtherMessagesDoNotRefresh(int message, int parameter) =>
        Assert.False(DesktopSystemMessages.ShouldRefresh(message, parameter, 0));
}
