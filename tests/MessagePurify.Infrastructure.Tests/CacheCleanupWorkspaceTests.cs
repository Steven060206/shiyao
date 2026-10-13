using System.Text.Json;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace MessagePurify.Infrastructure.Tests;

public sealed class CacheCleanupWorkspaceTests : IDisposable
{
    private const string Account = "9007199254740993";
    private const string Group = "9007199254740995";
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 10, 0, 0, TimeSpan.FromHours(8));
    private readonly string root = Path.Combine(Path.GetTempPath(), "MessagePurify-cache-workspace-" + Guid.NewGuid().ToString("N"));
    private static readonly HashSet<string> Pins = [Group];

    private AccountWorkspace Workspace()
    {
        var workspace = new AccountWorkspace(root, Account);
        workspace.SetAvailableGroups([new(Group, "数学课程群")], Pins);
        return workspace;
    }

    private static SchoolMessage Message(string id, string text = "大家早上好") =>
        new(new(Account, Group, id), "teacher", Now.AddDays(-10), [new(SegmentKind.Text, text)]);
    private AccountMessageStore Messages() => new(root, Account);
    private static WorkspaceCommand Cleanup(string account = Account, string id = "cache", long version = 0) =>
        new(account, "clean-cache", id, Guid.NewGuid().ToString("N"), version);
    private static JsonElement Result(AccountWorkspace workspace) =>
        JsonSerializer.SerializeToElement(workspace.Snapshot(Now)).GetProperty("CacheCleanup");

    [Fact]
    public void CommandDeletesOnlyTemporaryMessagesAndKeepsEvidenceAndResultAfterReopen()
    {
        var workspace = Workspace();
        workspace.Ingest(Message("chat"), Pins, MessageOrigin.History, "history", Now);
        workspace.Ingest(Message("task", "10月20日提交数学作业"), Pins, MessageOrigin.History, "history", Now);
        var original = Assert.Single(workspace.Snapshot(Now).Items);

        workspace.Execute(Cleanup(), Now);

        Assert.Null(Messages().Find(Message("chat").Key));
        Assert.NotNull(Messages().Find(Message("task").Key));
        var reopened = Workspace();
        var retained = Assert.Single(reopened.Snapshot(Now).Items);
        Assert.Equal(original.Id, retained.Id);
        Assert.Equal(original.Version, retained.Version);
        Assert.Equal("10月20日提交数学作业", Assert.Single(retained.Evidence).Text);
        Assert.Equal(1, Result(reopened).GetProperty("DeletedMessages").GetInt32());
        Assert.Equal(Account, Result(reopened).GetProperty("AccountId").GetString());
        Assert.Equal(Now, Result(reopened).GetProperty("CompletedAt").GetDateTimeOffset());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CleanedMessageRepeatedReadDoesNotReenterInboxOrPendingQueue(bool reopen)
    {
        var workspace = Workspace();
        var message = Message("chat");
        Assert.True(workspace.Ingest(message, Pins, MessageOrigin.History, "first", Now));
        workspace.Execute(Cleanup(), Now);
        if (reopen) workspace = Workspace();

        Assert.False(workspace.Enqueue(message, Pins, MessageOrigin.History, "second", Now));
        Assert.False(workspace.Ingest(message, Pins, MessageOrigin.Realtime, "third", Now));
        Assert.Null(Messages().Find(message.Key));
        Assert.Empty(Messages().GetPending());
        Assert.Equal(0, workspace.ProcessPending(Pins, Now));
        Assert.Empty(workspace.Snapshot(Now).Items);
    }

    [Theory]
    [InlineData("123456", "cache", 0)]
    [InlineData(Account, "task", 0)]
    [InlineData(Account, "cache", 1)]
    [InlineData(Account, "cache", -1)]
    public void InvalidCommandDoesNotDeleteAnyMessage(string account, string id, long version)
    {
        var workspace = Workspace();
        workspace.Ingest(Message("chat"), Pins, MessageOrigin.History, "history", Now);
        Assert.Throws<ArgumentException>(() => workspace.Execute(Cleanup(account, id, version), Now));
        Assert.NotNull(Messages().Find(Message("chat").Key));
    }

    [Fact]
    public void FailureKeepsLastSuccessfulResultAndShowsIndependentError()
    {
        var workspace = Workspace();
        workspace.Execute(Cleanup(), Now);
        var previous = Result(workspace).GetRawText();
        workspace.Ingest(Message("corrupt"), Pins, MessageOrigin.History, "history", Now);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Messages().DatabasePath }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE messages SET message_json = 'invalid-json' WHERE message_id = 'corrupt';";
            command.ExecuteNonQuery();
        }

        Assert.ThrowsAny<Exception>(() => workspace.Execute(Cleanup(), Now.AddMinutes(1)));
        var snapshot = JsonSerializer.SerializeToElement(workspace.Snapshot(Now));
        Assert.Equal(previous, snapshot.GetProperty("CacheCleanup").GetRawText());
        Assert.False(string.IsNullOrWhiteSpace(snapshot.GetProperty("CacheCleanupError").GetString()));
    }

    [Fact]
    public void PausedCleanupRetainsMessagesAndPreviousSuccessWithoutFailureStatus()
    {
        var workspace = Workspace();
        workspace.CleanMessageCache(Now);
        var previous = workspace.Snapshot(Now).CacheCleanup;
        workspace.Ingest(Message("chat"), Pins, MessageOrigin.History, "history", Now);

        Assert.Throws<OperationCanceledException>(() => workspace.CleanMessageCache(Now.AddMinutes(1), () => false));

        Assert.NotNull(Messages().Find(Message("chat").Key));
        Assert.Equal(previous, workspace.Snapshot(Now).CacheCleanup);
        Assert.Null(workspace.Snapshot(Now).CacheCleanupError);
    }

    [Fact]
    public void CleanupAndResultAreScopedToCurrentAccount()
    {
        var workspace = Workspace();
        var other = new AccountWorkspace(root, "123456");
        var otherMessage = Message("chat") with { Key = new("123456", Group, "chat") };
        workspace.Ingest(Message("chat"), Pins, MessageOrigin.History, "first", Now);
        other.Ingest(otherMessage, Pins, MessageOrigin.History, "second", Now);

        workspace.Execute(Cleanup(), Now);

        Assert.Null(Messages().Find(Message("chat").Key));
        Assert.NotNull(new AccountMessageStore(root, "123456").Find(otherMessage.Key));
        Assert.NotNull(workspace.Snapshot(Now).CacheCleanup);
        Assert.Null(other.Snapshot(Now).CacheCleanup);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
