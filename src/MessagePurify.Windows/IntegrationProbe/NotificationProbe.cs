using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace MessagePurify.Windows.IntegrationProbe;

public sealed class NotificationProbe : IDisposable
{
    public const string SyntheticAccount = "demo-account";
    private bool registered;

    public static bool IsElevated() => ProcessIntegrity.IsElevated();

    public bool Register(ProbeReport report, Action<string> onInvoked)
    {
        if (report.Elevated) { report.NotificationStatus = "blockedElevated"; return false; }
        if (!AppNotificationManager.IsSupported()) { report.NotificationStatus = "unsupported"; return false; }
        AppNotificationManager.Default.NotificationInvoked += (_, arguments) => onInvoked(arguments.Argument);
        AppNotificationManager.Default.Register();
        registered = true;
        report.NotificationSetting = AppNotificationManager.Default.Setting.ToString();
        report.NotificationStatus = "registered";
        return true;
    }

    public void Send(string mode, ProbeReport report)
    {
        report.NotificationMode = mode;
        if (!registered) return;
        if (AppNotificationManager.Default.Setting != AppNotificationSetting.Enabled)
        {
            report.NotificationStatus = "blockedBySettings";
            return;
        }
        var probeId = Guid.NewGuid().ToString("N");
        var builder = new AppNotificationBuilder()
            .AddArgument("action", "view")
            .AddArgument("account", SyntheticAccount)
            .AddArgument("item", "demo-item")
            .AddArgument("probe", probeId)
            .SetGroup("integration-demo-account")
            .SetTag("probe-" + mode)
            .AddText("消息提纯：集成测试提醒")
            .AddText("合成内容。点击验证账号与事项定位。");
        if (mode == "silent") builder.MuteAudio();
        var notification = builder.BuildNotification();
        AppNotificationManager.Default.Show(notification);
        report.NotificationId = notification.Id;
        report.NotificationStatus = notification.Id == 0 ? "submissionUnknown" : "submitted";
    }

    public void Dispose()
    {
        if (!registered) return;
        AppNotificationManager.Default.Unregister();
        registered = false;
    }
}
