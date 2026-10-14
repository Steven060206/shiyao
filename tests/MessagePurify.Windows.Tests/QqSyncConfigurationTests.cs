using System.Text.Json;
using MessagePurify.Windows.App;
using MessagePurify.Infrastructure.NapCat;

namespace MessagePurify.Windows.Tests;

public sealed class QqSyncConfigurationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "消息提纯 portable 配置 " + Guid.NewGuid().ToString("N"));
    private string AppDirectory => Path.Combine(root, "Shiyao", "app");

    [Fact]
    public void PortableConfigurationUsesMovedRootAndAbsoluteQqPath()
    {
        Directory.CreateDirectory(AppDirectory);
        var qq = Path.Combine(root, "程序 空格", "QQ.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(qq)!);
        File.WriteAllText(qq, "placeholder");
        File.WriteAllText(Path.Combine(AppDirectory, "qq-sync.json"), JsonSerializer.Serialize(new { QqExecutable = qq }));

        var configuration = QqSyncConfiguration.Load(AppDirectory);

        Assert.Equal(Path.GetFullPath(qq), configuration.QqExecutable);
        Assert.Equal(Path.GetFullPath(Path.Combine(root, "Shiyao", "napcat", "package")), configuration.PackageDirectory);
        Assert.Equal(Path.GetFullPath(Path.Combine(root, "Shiyao", "bridge", "Prepare-PinnedGroups.ps1")), configuration.PrepareScript);
    }

    [Fact]
    public void MarkerWithoutSetupConfigurationFailsInsteadOfUsingLegacyQq()
    {
        Directory.CreateDirectory(AppDirectory);
        File.WriteAllText(Path.Combine(AppDirectory, "qq-sync-required"), string.Empty);

        var error = Assert.Throws<QqSyncConfigurationException>(() => QqSyncConfiguration.Load(AppDirectory));

        Assert.Equal(QqSyncConfiguration.SetupRequiredMessage, error.Message);
        Assert.DoesNotContain("D:/QQ", error.Message);
    }

    [Fact]
    public void LegacyDirectoryWithoutMarkerOrJsonKeepsExistingDefaults()
    {
        Directory.CreateDirectory(AppDirectory);

        var configuration = QqSyncConfiguration.Load(AppDirectory);

        Assert.Equal("D:/QQ/QQ.exe", configuration.QqExecutable);
        Assert.Equal(Path.GetFullPath(Path.Combine(AppDirectory, "../../napcat/framework/v4.18.37/package")), configuration.PackageDirectory);
        Assert.Equal(Path.GetFullPath(Path.Combine(AppDirectory, "../../../packaging/napcat/Prepare-PinnedGroups.ps1")), configuration.PrepareScript);
    }

    [Fact]
    public void MalformedConfigurationIsReadableAndDoesNotExposeJson()
    {
        Directory.CreateDirectory(AppDirectory);
        File.WriteAllText(Path.Combine(AppDirectory, "qq-sync.json"), "{\"QqExecutable\":private-token");

        var error = Assert.Throws<QqSyncConfigurationException>(() => QqSyncConfiguration.Load(AppDirectory));

        Assert.Equal(QqSyncConfiguration.InvalidMessage, error.Message);
        Assert.DoesNotContain("private-token", error.ToString());
    }

    [Fact]
    public void RelativeQqPathIsRejected()
    {
        Directory.CreateDirectory(AppDirectory);
        File.WriteAllText(Path.Combine(AppDirectory, "qq-sync.json"), JsonSerializer.Serialize(new { QqExecutable = "relative/QQ.exe" }));

        Assert.Throws<QqSyncConfigurationException>(() => QqSyncConfiguration.Load(AppDirectory));
    }

    [Fact]
    public void ExistingExecutableWithWrongNameIsRejected()
    {
        Directory.CreateDirectory(AppDirectory);
        var wrongName = Path.Combine(root, "not-qq.exe");
        File.WriteAllText(wrongName, "placeholder");
        File.WriteAllText(Path.Combine(AppDirectory, "qq-sync.json"), JsonSerializer.Serialize(new { QqExecutable = wrongName }));

        Assert.Throws<QqSyncConfigurationException>(() => QqSyncConfiguration.Load(AppDirectory));
    }

    [Fact]
    public void MissingQqExecutableIsRejectedEvenWhenTheNameIsCorrect()
    {
        Directory.CreateDirectory(AppDirectory);
        var missing = Path.Combine(root, "QQ.exe");
        File.WriteAllText(Path.Combine(AppDirectory, "qq-sync.json"), JsonSerializer.Serialize(new { QqExecutable = missing }));

        Assert.Throws<QqSyncConfigurationException>(() => QqSyncConfiguration.Load(AppDirectory));
    }

    [Fact]
    public void TokenLookupUsesPortablePackageDirectoryFromTheSameConfiguration()
    {
        Directory.CreateDirectory(AppDirectory);
        var qq = Path.Combine(root, "QQ.exe");
        File.WriteAllText(qq, "placeholder");
        File.WriteAllText(Path.Combine(AppDirectory, "qq-sync.json"), JsonSerializer.Serialize(new { QqExecutable = qq }));
        var configuration = QqSyncConfiguration.Load(AppDirectory);
        Directory.CreateDirectory(configuration.TokenConfigurationDirectory);
        File.WriteAllText(Path.Combine(configuration.TokenConfigurationDirectory, "onebot11_portable.json"),
            "{\"network\":{\"websocketServers\":[{\"enable\":true,\"port\":3001,\"host\":\"127.0.0.1\",\"token\":\"portable-token\"}]}}");

        Assert.Equal("portable-token", AppRuntime.ReadAccessToken(configuration, null));
        Assert.Equal(Path.Combine(configuration.PackageDirectory, "config"), configuration.TokenConfigurationDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
