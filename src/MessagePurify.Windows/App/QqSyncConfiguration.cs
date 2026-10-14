using System.IO;
using System.Text.Json;

namespace MessagePurify.Windows.App;

internal sealed record QqSyncConfiguration(string QqExecutable, string PackageDirectory, string PrepareScript)
{
    internal const string InvalidMessage = "QQ 路径配置无法识别，请重新运行 Setup.cmd；原 QQ 保持运行。";
    internal const string SetupRequiredMessage = "请先运行分发目录中的 Setup.cmd 配置 QQ；原 QQ 保持运行。";
    public string TokenConfigurationDirectory => Path.Combine(PackageDirectory, "config");

    public static QqSyncConfiguration Load(string applicationDirectory)
    {
        var appDirectory = Path.GetFullPath(applicationDirectory);
        var configurationPath = Path.Combine(appDirectory, "qq-sync.json");
        if (!File.Exists(configurationPath) && !Directory.Exists(configurationPath))
        {
            var setupMarker = Path.Combine(appDirectory, "qq-sync-required");
            if (File.Exists(setupMarker) || Directory.Exists(setupMarker))
                throw new QqSyncConfigurationException(SetupRequiredMessage);
            return new("D:/QQ/QQ.exe",
                Path.GetFullPath(Path.Combine(appDirectory, "../../napcat/framework/v4.18.37/package")),
                Path.GetFullPath(Path.Combine(appDirectory, "../../../packaging/napcat/Prepare-PinnedGroups.ps1")));
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configurationPath));
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("QqExecutable", out var executable)
                || executable.ValueKind != JsonValueKind.String
                || executable.GetString() is not { } qqPath
                || !Path.IsPathFullyQualified(qqPath)
                || !string.Equals(Path.GetFileName(qqPath), "QQ.exe", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(qqPath))
                throw new QqSyncConfigurationException(InvalidMessage);
            return new(Path.GetFullPath(qqPath),
                Path.GetFullPath(Path.Combine(appDirectory, "../napcat/package")),
                Path.GetFullPath(Path.Combine(appDirectory, "../bridge/Prepare-PinnedGroups.ps1")));
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Keep raw JSON, credentials, and path fragments out of the application status.
            throw new QqSyncConfigurationException(InvalidMessage);
        }
    }
}

internal sealed class QqSyncConfigurationException(string message) : InvalidOperationException(message);
