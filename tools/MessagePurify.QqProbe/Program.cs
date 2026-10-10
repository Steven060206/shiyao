using System.Text;
using MessagePurify.QqProbe;

if (args.Length == 1 && args[0] == "--help")
{
    Console.WriteLine(ProbeArguments.Usage);
    return 0;
}
try
{
    var options = ProbeArguments.Parse(args);
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, input) => { input.Cancel = true; cancellation.Cancel(); };
    var report = await ProbeRunner.RunAsync(options, Environment.GetEnvironmentVariable("MESSAGEPURIFY_NAPCAT_TOKEN"), cancellation.Token);
    var markdown = report.ToMarkdown();
    if (options.Output is not null)
    {
        var output = Path.GetFullPath(options.Output);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, markdown, new UTF8Encoding(false));
    }
    Console.WriteLine(markdown);
    return report.Success ? 0 : 1;
}
catch (ArgumentException)
{
    Console.Error.WriteLine(ProbeArguments.Usage);
    return 2;
}
catch (Exception)
{
    Console.Error.WriteLine("探针运行或报告写入失败；请检查本机服务、参数和输出目录。");
    return 2;
}
