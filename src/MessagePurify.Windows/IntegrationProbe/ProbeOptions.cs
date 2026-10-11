namespace MessagePurify.Windows.IntegrationProbe;

public sealed record ProbeOptions(bool Bridge, bool InteractiveDesktop, string? Notification,
    bool ExitAfterSend, bool ColdActivation, int WaitSeconds, string? ReportPath, bool Help)
{
    public string? ProtocolActivation { get; init; }

    public static ProbeOptions Parse(string[] arguments)
    {
        bool bridge = false, desktop = false, exit = false, cold = false, embedding = false, help = false;
        string? notification = null, report = null, protocol = null;
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
                case "--open-notification":
                    protocol = TakeValue();
                    if (!NotificationProtocol.TryParse(protocol, out _)) throw new ArgumentException("Invalid notification URI.");
                    break;
                case "--help": help = true; break;
                case "-Embedding": embedding = true; break;
                default:
                    if (arguments[index].StartsWith("----AppNotificationActivated:", StringComparison.Ordinal)) cold = true;
                    else throw new ArgumentException("Unknown option.");
                    break;
            }
        }
        if (exit && notification is null) throw new ArgumentException("Exit after send requires a notification.");
        if (embedding && !cold) throw new ArgumentException("COM embedding requires notification activation.");
        if (protocol is not null && (bridge || desktop || notification is not null || cold || exit))
            throw new ArgumentException("Notification URI activation only opens details.");
        if (!bridge && !desktop && notification is null && !cold && protocol is null && !help) bridge = true;
        return new(bridge, desktop, notification, exit, cold, wait, report, help) { ProtocolActivation = protocol };
    }
}
