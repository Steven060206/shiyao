namespace MessagePurify.Windows.App;

public static class DesktopSystemMessages
{
    public static bool ShouldRefresh(int message, nint parameter, uint taskbarCreatedMessage) =>
        message is 0x7e or 0x1a or 0x2e0
        || message == 0x218 && parameter is 7 or 18
        || taskbarCreatedMessage != 0 && (uint)message == taskbarCreatedMessage;
}
