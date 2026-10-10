using System.Text.RegularExpressions;

namespace MessagePurify.Windows.IntegrationProbe;

public sealed record NotificationRoute(string AccountId, string ItemId, string ProbeId)
{
    public string ToArguments() => "action=view&account=" + Uri.EscapeDataString(AccountId) +
        "&item=" + Uri.EscapeDataString(ItemId) + "&probe=" + Uri.EscapeDataString(ProbeId);

    public bool BelongsTo(string activeAccount) => AccountId == activeAccount;

    public static bool TryParse(string arguments, out NotificationRoute? route)
    {
        route = null;
        if (arguments.Length is 0 or > 1024 || Regex.IsMatch(arguments, "%([^0-9a-fA-F]|$)|%[0-9a-fA-F]([^0-9a-fA-F]|$)")) return false;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in arguments.Split('&'))
        {
            var split = pair.Split('=', 2);
            if (split.Length != 2) return false;
            var key = Uri.UnescapeDataString(split[0]);
            var value = Uri.UnescapeDataString(split[1]);
            if (!values.TryAdd(key, value)) return false;
        }
        if (values.Count != 4 || !values.TryGetValue("action", out var action) || action != "view" ||
            !values.TryGetValue("account", out var account) || !values.TryGetValue("item", out var item) ||
            !values.TryGetValue("probe", out var probe) || !ValidId(account) || !ValidId(item) || !ValidId(probe)) return false;
        route = new(account, item, probe);
        return true;
    }

    private static bool ValidId(string value) => value.Length is > 0 and <= 128 &&
        value.All(character => !char.IsControl(character) && !char.IsWhiteSpace(character));
}
