using MessagePurify.Infrastructure.NapCat;

namespace MessagePurify.QqProbe;

public sealed record ProbeArguments(Uri Url, int ListenSeconds, int HistoryGroups, int HistoryCount, string? Output)
{
    public const string Usage = "用法：MessagePurify.QqProbe [--url 本机WS根地址] [--listen-seconds 0..300] [--history-groups 0..20] [--history-count 1..100] [--output 报告.md]；Token仅使用MESSAGEPURIFY_NAPCAT_TOKEN环境变量。";

    public static ProbeArguments Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var allowed = new HashSet<string>(["--url", "--listen-seconds", "--history-groups", "--history-count", "--output"]);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (!allowed.Contains(args[i]) || i + 1 >= args.Length || !values.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException(Usage);
        }
        if (!Uri.TryCreate(values.GetValueOrDefault("--url", "ws://127.0.0.1:3001/"), UriKind.Absolute, out var url))
            throw new ArgumentException(Usage);
        OneBotEndpoint.Validate(url);
        return new(url, Number("--listen-seconds", 30, 0, 300), Number("--history-groups", 1, 0, 20),
            Number("--history-count", 20, 1, 100), values.GetValueOrDefault("--output"));

        int Number(string key, int fallback, int minimum, int maximum)
        {
            if (!values.TryGetValue(key, out var value)) return fallback;
            if (!int.TryParse(value, out var number) || number < minimum || number > maximum)
                throw new ArgumentException(Usage);
            return number;
        }
    }
}
