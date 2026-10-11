using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppLifecycle;

namespace MessagePurify.Windows.IntegrationProbe;

public sealed class NotificationProbe : IDisposable
{
    public const string SyntheticAccount = "demo-account";
    private bool registered;

    public static bool IsElevated() => ProcessIntegrity.IsElevated();

    public bool Register(ProbeReport report, Action<string> onInvoked, bool coldActivation = false)
    {
        if (report.Elevated) { report.NotificationStatus = "blockedElevated"; return false; }
        if (!AppNotificationManager.IsSupported()) { report.NotificationStatus = "unsupported"; return false; }
        AppNotificationManager.Default.NotificationInvoked += (_, arguments) => onInvoked(arguments.Argument);
        report.NotificationStatus = "registering";
        AppNotificationManager.Default.Register();
        registered = true;
        report.NotificationStatus = "readingSetting";
        report.NotificationSetting = AppNotificationManager.Default.Setting.ToString();
        report.NotificationStatus = "registered";
        if (coldActivation)
        {
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            if (activation.Kind == ExtendedActivationKind.AppNotification && activation.Data is AppNotificationActivatedEventArgs launch)
                onInvoked(launch.Argument);
        }
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
        NotificationProtocol.Register();
        var notification = new AppNotification(NotificationProtocol.BuildPayload(
            new(SyntheticAccount, "demo-item", probeId)))
        {
            Group = "integration-demo-account", Tag = "probe-" + mode
        };
        report.NotificationActivationTransport = "protocol";
        report.NotificationProbeId = probeId;
        report.NotificationStatus = "submitting";
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
