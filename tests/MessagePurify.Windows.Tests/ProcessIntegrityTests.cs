using System.Diagnostics;
using System.Text.RegularExpressions;
using MessagePurify.Windows.IntegrationProbe;

public class ProcessIntegrityTests
{
    [Fact]
    public void NativeIntegrityMatchesIndependentWindowsGroupQuery()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This test requires Windows.");
        using var query = Process.Start(new ProcessStartInfo("whoami.exe", "/groups /fo csv /nh")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        })!;
        var output = query.StandardOutput.ReadToEnd();
        query.WaitForExit();
        Assert.Equal(0, query.ExitCode);
        var label = Regex.Match(output, @"S-1-16-(\d+)");
        Assert.True(label.Success, "Windows did not return an integrity label.");
        var expectedRid = uint.Parse(label.Groups[1].Value);
        Assert.Equal(expectedRid, ProcessIntegrity.CurrentRid());
        Assert.Equal(expectedRid >= 0x3000, ProcessIntegrity.IsElevated());
    }
}
