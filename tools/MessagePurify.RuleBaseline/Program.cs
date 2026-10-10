using System.Text;
using MessagePurify.RuleBaseline;

try
{
    string? output = null;
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == "--output" && i + 1 < args.Length && output is null) output = args[++i];
        else throw new ArgumentException("用法：MessagePurify.RuleBaseline [--output 报告.md]");
    }
    var fixturePath = Path.Combine(AppContext.BaseDirectory, "fixtures", "rules", "synthetic.json");
    var report = RuleFixtureRunner.Run(fixturePath);
    if (output is not null)
    {
        var path = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, report.ToMarkdown(), new UTF8Encoding(false));
        Console.WriteLine($"报告：{path}");
    }
    else Console.WriteLine(report.ToMarkdown());
    Console.WriteLine($"合成样本 {report.Results.Count}；失败 {report.Results.Count(r => r.Errors.Count > 0)}；高影响错误 {report.HighImpactErrors.Count}。");
    return report.Success ? 0 : 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"评估失败：{exception.GetType().Name}: {exception.Message}");
    return 2;
}
