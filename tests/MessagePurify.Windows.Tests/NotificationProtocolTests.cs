using System.Xml.Linq;
using MessagePurify.Windows.IntegrationProbe;

namespace MessagePurify.Windows.Tests;

public class NotificationProtocolTests
{
    [Fact]
    public void ProtocolPayloadOpensTheSameAccountAndItemAndPreservesSoundMode()
    {
        var route = new NotificationRoute("demo-account", "demo-item", "probe-1");
        foreach (var silent in new[] { false, true })
        {
            var toast = XElement.Parse(NotificationProtocol.BuildPayload(route, silent));
            Assert.Equal("protocol", (string?)toast.Attribute("activationType"));
            Assert.True(NotificationProtocol.TryParse((string)toast.Attribute("launch")!, out var parsed));
            Assert.Equal(route, parsed);
            Assert.Equal(silent, toast.Element("audio") is not null);
            if (silent) Assert.Equal("true", (string?)toast.Element("audio")!.Attribute("silent"));
        }
    }

    [Theory]
    [InlineData("https://view/?action=view&account=a&item=t&probe=p")]
    [InlineData("messagepurify-probe://other/?action=view&account=a&item=t&probe=p")]
    [InlineData("messagepurify-probe://view/other?action=view&account=a&item=t&probe=p")]
    [InlineData("messagepurify-probe://view/?action=send&account=a&item=t&probe=p")]
    [InlineData("messagepurify-probe://view/?action=view&account=a&item=t&probe=p#fragment")]
    [InlineData("messagepurify-probe://view/?action=view&account=a&item=t&probe=p&account=b")]
    [InlineData("messagepurify-probe://view/?action=view&account=a&item=t&probe=%22%20--notification%20normal")]
    [InlineData("messagepurify-probe://view/?action=view&account=a&item=t&probe=\"p\"")]
    public void UntrustedOrMutatingUrisCannotLaunchNotifications(string uri)
    {
        Assert.False(NotificationProtocol.TryParse(uri, out _));
        Assert.Throws<ArgumentException>(() => ProbeOptions.Parse(["--open-notification", uri]));
    }

    [Fact]
    public void UriActivationOpensDetailsWithoutRegisteringOrSendingNotifications()
    {
        var uri = NotificationProtocol.ToUri(new("other-account", "9007199254740993", "p"));
        var options = ProbeOptions.Parse(["--open-notification", uri]);
        Assert.False(options.Bridge);
        Assert.False(options.ColdActivation);
        Assert.Null(options.Notification);
        Assert.Equal(uri, options.ProtocolActivation);
        Assert.True(NotificationProtocol.TryParse(uri, out var route));
        Assert.False(route!.BelongsTo("demo-account"));
        Assert.Throws<ArgumentException>(() => ProbeOptions.Parse(["--open-notification", uri, "--notification", "normal"]));
    }

    [Fact]
    public void ProtocolCommandQuotesExecutableAndUriAndUsesNoShell()
    {
        Assert.Equal("\"D:\\中文 路径\\probe.exe\" --open-notification \"%1\" --wait-seconds 600",
            NotificationProtocol.OpenCommand(@"D:\中文 路径\probe.exe"));
    }
}
