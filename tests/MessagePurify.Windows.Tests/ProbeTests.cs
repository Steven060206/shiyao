using MessagePurify.Windows.IntegrationProbe;

namespace MessagePurify.Windows.Tests;

public class BridgeTests
{
    private const string Json = "{\"id\":\"r1\",\"operation\":\"echo\",\"parameters\":{\"value\":\"<script>中国😀</script>\",\"nonce\":\"challenge\"}}";

    [Fact]
    public void EchoPreservesLiteralTextAndRequestId()
    {
        var reply = BridgeProtocol.Handle("https://probe.messagepurify.local/index.html?nonce=challenge", Json, "challenge");
        Assert.True(reply.Success);
        Assert.Equal("r1", reply.Id);
        Assert.Equal("<script>中国😀</script>", reply.Value);
    }

    [Theory]
    [InlineData("http://probe.messagepurify.local/index.html")]
    [InlineData("https://probe.messagepurify.local.evil/index.html")]
    [InlineData("https://probe.messagepurify.local:444/index.html")]
    [InlineData("https://other.test/index.html")]
    [InlineData("file:///index.html")]
    [InlineData("https://user@probe.messagepurify.local/index.html")]
    [InlineData("https://probe.messagepurify.local/frame.html")]
    [InlineData("invalid")]
    public void OtherOriginsAndDocumentsAreRejected(string source)
    {
        var reply = BridgeProtocol.Handle(source, Json, "challenge");
        Assert.False(reply.Success);
        Assert.Equal("source", reply.Error);
    }

    [Theory]
    [InlineData("{", "json")]
    [InlineData("[]", "parameters")]
    [InlineData("{\"id\":123,\"operation\":\"echo\",\"parameters\":{}}", "parameters")]
    [InlineData("{\"id\":\"r\",\"operation\":\"readFile\",\"parameters\":{}}", "operation")]
    [InlineData("{\"id\":\"r\",\"operation\":\"echo\",\"parameters\":{\"value\":3,\"nonce\":\"challenge\"}}", "parameters")]
    [InlineData("{\"id\":\"r\",\"operation\":\"echo\",\"parameters\":{\"value\":\"ok\",\"nonce\":\"wrong\"}}", "nonce")]
    [InlineData("{\"id\":\"r\",\"id\":\"r2\",\"operation\":\"echo\",\"parameters\":{\"value\":\"ok\",\"nonce\":\"challenge\"}}", "parameters")]
    public void InvalidRequestsReturnSpecificErrors(string json, string error)
    {
        var reply = BridgeProtocol.Handle(BridgeProtocol.PageUrl, json, "challenge");
        Assert.False(reply.Success);
        Assert.Equal(error, reply.Error);
    }

    [Fact]
    public void OversizeMessagesAreRejected()
    {
        var reply = BridgeProtocol.Handle(BridgeProtocol.PageUrl, new string('x', 4097), "challenge");
        Assert.Equal("size", reply.Error);
    }
}

public class NotificationRouteTests
{
    [Fact]
    public void LargeIdsRemainStringsAndAccountIsBoundToRoute()
    {
        var route = new NotificationRoute("9007199254740993", "18446744073709551615", "probe-1");
        Assert.True(NotificationRoute.TryParse(route.ToArguments(), out var parsed));
        Assert.Equal(route, parsed);
        Assert.False(parsed!.BelongsTo("other-account"));
        Assert.True(parsed.BelongsTo(route.AccountId));
    }

    [Theory]
    [InlineData("action=view&account=a&item=t")]
    [InlineData("action=delete&account=a&item=t&probe=p")]
    [InlineData("action=view&account=a&item=t&probe=p&account=b")]
    [InlineData("action=view&account=&item=t&probe=p")]
    [InlineData("action=view&account=a&item=t&probe=p&token=secret")]
    [InlineData("action=view&account=a%0Aevil&item=t&probe=p")]
    [InlineData("action=view&account=a&item=t&probe=%ZZ")]
    public void MalformedOrMutatingRoutesAreRejected(string arguments)
    {
        Assert.False(NotificationRoute.TryParse(arguments, out _));
    }
}

public class ProbeOptionsTests
{
    [Fact]
    public void DefaultUsesHiddenBridgeAndBoundedTimeout()
    {
        var options = ProbeOptions.Parse([]);
        Assert.True(options.Bridge);
        Assert.False(options.InteractiveDesktop);
        Assert.Null(options.Notification);
        Assert.Equal(20, options.WaitSeconds);
    }

    [Fact]
    public void ExplicitNotificationAndReportCanBeCombined()
    {
        var options = ProbeOptions.Parse(["--notification", "silent", "--exit-after-send", "--report", "result.json"]);
        Assert.Equal("silent", options.Notification);
        Assert.True(options.ExitAfterSend);
        Assert.False(options.Bridge);
        Assert.Equal("result.json", options.ReportPath);
    }

    [Fact]
    public void ComColdStartWaitsForSdkNotificationEvent()
    {
        var options = ProbeOptions.Parse(["----AppNotificationActivated:"]);
        Assert.True(options.ColdActivation);
        Assert.False(options.Bridge);
    }

    [Theory]
    [InlineData("--wat")]
    [InlineData("--report")]
    [InlineData("--notification", "other")]
    [InlineData("--wait-seconds", "0")]
    [InlineData("--wait-seconds", "601")]
    [InlineData("--wait-seconds", "oops")]
    [InlineData("--exit-after-send")]
    public void InvalidCliFailsWithoutSideEffects(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => ProbeOptions.Parse(args));
    }
}

public class DesktopGeometryTests
{
    [Theory]
    [InlineData(50, 40, 600, 300, 50, 40, 600, 300)]
    [InlineData(-300, -100, 600, 300, 0, 0, 600, 300)]
    [InlineData(1800, 1000, 600, 300, 1320, 780, 600, 300)]
    [InlineData(10, 10, 3000, 2000, 0, 0, 1920, 1080)]
    public void LayoutStaysInsideMonitor(double x, double y, double width, double height,
        double expectedX, double expectedY, double expectedWidth, double expectedHeight)
    {
        Assert.Equal(new DesktopRect(expectedX, expectedY, expectedWidth, expectedHeight),
            DesktopGeometry.Clamp(new(x, y, width, height), new(0, 0, 1920, 1080)));
    }

    [Fact]
    public void NegativeMonitorCoordinatesRemainValid()
    {
        Assert.Equal(new DesktopRect(-1400, 100, 600, 300),
            DesktopGeometry.Clamp(new(-1400, 100, 600, 300), new(-1920, 0, 1920, 1080)));
    }

    [Fact]
    public void InvalidGeometryFailsExplicitly()
    {
        Assert.Throws<ArgumentException>(() => DesktopGeometry.Clamp(new(double.NaN, 0, 100, 100), new(0, 0, 1920, 1080)));
    }

    [Fact]
    public void DragUsesFixedParentCoordinatesWithoutMovingWindowFeedback()
    {
        var start = new DesktopRect(75, 100, 850, 375);
        var initialCursor = new DesktopPoint(100, 130);
        var parentBounds = new DesktopRect(0, 0, 2400, 1350);
        Assert.Equal(new DesktopRect(95, 110, 850, 375),
            DesktopGeometry.Drag(start, initialCursor, new(120, 140), parentBounds));
        Assert.Equal(new DesktopRect(115, 120, 850, 375),
            DesktopGeometry.Drag(start, initialCursor, new(140, 150), parentBounds));
    }

    [Fact]
    public void DragClampsInPhysicalParentClientCoordinates()
    {
        Assert.Equal(new DesktopRect(1550, 975, 850, 375),
            DesktopGeometry.Drag(new(75, 100, 850, 375), new(100, 130), new(3000, 2000), new(0, 0, 2400, 1350)));
    }

    [Fact]
    public void ResizeKeepsPhysicalRectangleInsideParent()
    {
        Assert.Equal(new DesktopRect(75, 100, 2325, 1250),
            DesktopGeometry.Resize(new(75, 100, 850, 375), new(3000, 2000), new(0, 0, 2400, 1350), new(500, 225)));
    }
}
