using System.Xml.Linq;
using Microsoft.Win32;

namespace MessagePurify.Windows.IntegrationProbe;

public static class NotificationProtocol
{
    public const string Scheme = "messagepurify-probe";

    public static string ToUri(NotificationRoute route) => Scheme + "://view/?" + route.ToArguments();

    public static bool TryParse(string value, out NotificationRoute? route)
    {
        route = null;
        return value.Length <= 1100 && !value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '"' or '\\') &&
            Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Scheme == Scheme && uri.Host == "view" && uri.AbsolutePath == "/" &&
            uri.UserInfo.Length == 0 && uri.Port == -1 && uri.Fragment.Length == 0 &&
            NotificationRoute.TryParse(uri.Query.TrimStart('?'), out route);
    }

    public static string BuildPayload(NotificationRoute route)
    {
        var toast = new XElement("toast", new XAttribute("activationType", "protocol"),
            new XAttribute("launch", ToUri(route)),
            new XElement("visual", new XElement("binding", new XAttribute("template", "ToastGeneric"),
                new XElement("text", "消息提纯：集成测试提醒"),
                new XElement("text", "点击打开测试事项。仅在本机显示。"))));
        toast.Add(new XElement("audio", new XAttribute("silent", "true")));
        return toast.ToString(SaveOptions.DisableFormatting);
    }

    public static string OpenCommand(string executable) => "\"" + executable +
        "\" --open-notification \"%1\" --wait-seconds 600";

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static void Register()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + Scheme);
        key.SetValue("", "URL:MessagePurify integration probe");
        key.SetValue("URL Protocol", "");
        using var command = key.CreateSubKey(@"shell\open\command");
        command.SetValue("", OpenCommand(Environment.ProcessPath!));
    }
}
