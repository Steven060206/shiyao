namespace MessagePurify.Windows.IntegrationProbe;

public sealed record ProbeOptions(bool Bridge, bool InteractiveDesktop, string? Notification,
    bool ExitAfterSend, bool ColdActivation, int WaitSeconds, string? ReportPath, bool Help)
{
    public static ProbeOptions Parse(string[] arguments)
    {
        bool bridge = false, desktop = false, exit = false, cold = false, help = false;
        string? notification = null, report = null;
        int wait = 20;
        for (var index = 0; index < arguments.Length; index++)
        {
            string TakeValue()
            {
                if (++index >= arguments.Length || string.IsNullOrWhiteSpace(arguments[index]) || arguments[index].StartsWith("--"))
                    throw new ArgumentException("Missing option value.");
                return arguments[index];
            }
            switch (arguments[index])
            {
                case "--bridge": bridge = true; break;
                case "--interactive-desktop": desktop = true; break;
                case "--notification":
                    notification = TakeValue();
                    if (notification is not ("silent" or "normal")) throw new ArgumentException("Invalid notification mode.");
                    break;
                case "--exit-after-send": exit = true; break;
                case "--wait-seconds":
                    if (!int.TryParse(TakeValue(), out wait) || wait is < 1 or > 600) throw new ArgumentException("Wait must be 1..600 seconds.");
                    break;
                case "--report": report = TakeValue(); break;
                case "--help": help = true; break;
                default:
                    if (arguments[index].StartsWith("----AppNotificationActivated:", StringComparison.Ordinal)) cold = true;
                    else throw new ArgumentException("Unknown option.");
                    break;
            }
        }
        if (exit && notification is null) throw new ArgumentException("Exit after send requires a notification.");
        if (!bridge && !desktop && notification is null && !cold && !help) bridge = true;
        return new(bridge, desktop, notification, exit, cold, wait, report, help);
    }
}
