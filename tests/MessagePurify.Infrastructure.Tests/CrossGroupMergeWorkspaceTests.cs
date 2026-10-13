using MessagePurify.Core;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.NapCat;
using MessagePurify.Infrastructure.Storage;
using MessagePurify.Infrastructure.Tasks;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace MessagePurify.Infrastructure.Tests;

public sealed class CrossGroupMergeWorkspaceTests : IDisposable
{
    private const string Account = "123";
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 10, 0, 0, TimeSpan.FromHours(8));
    private readonly string root = Path.Combine(Path.GetTempPath(), "MessagePurify-cross-workspace-" + Guid.NewGuid().ToString("N"));
    private AccountWorkspace Workspace() => new(root, Account);
    private AccountTaskStore Store() => new(root, Account);

    private AccountWorkspace Seed()
    {
        var workspace = Workspace();
        HashSet<string> pinned = ["100", "200"];
        workspace.SetAvailableGroups([new("100", "数学课程群"), new("200", "数学通知群")], pinned);
        foreach (var group in pinned)
        {
            workspace.Execute(new(Account, "confirm-group", group, "confirm-" + group, 0, Course: "数学"), Now);
            workspace.Ingest(new(new(Account, group, "1"), "teacher", Now.AddMinutes(-1),
                [new(SegmentKind.Text, "10月20日18点提交统计作业")]), pinned, MessageOrigin.History, "seed", Now);
        }
        Assert.Equal(2, Store().ListTasks().Count);
        return workspace;
    }

    private static ApplicationItem Suggestion(AccountWorkspace workspace) =>
        Assert.Single(workspace.Snapshot(Now).Items, item => item.Kind == "merge-review");

    [Fact]
    public void Snapshot_derives_suggestion_with_both_versions_and_original_evidence_without_saving_a_review()
    {
        var workspace = Seed();
        var suggestion = Suggestion(workspace);
        var tasks = workspace.Snapshot(Now).Items.Where(item => item.Kind == "task").OrderBy(item => item.Id).ToArray();
        Assert.Equal("pending", suggestion.State);
        Assert.Contains("可能重复", suggestion.Title);
        Assert.Equal(2, suggestion.Evidence.Count);
        Assert.Equal(["100", "200"], suggestion.Evidence.Select(item => item.GroupId).Order().ToArray());
        Assert.All(suggestion.Evidence, evidence => Assert.Equal("10月20日18点提交统计作业", evidence.Text));
        Assert.False(suggestion.CanAccept);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(suggestion));
        var match = json.RootElement.GetProperty("MergeSuggestion");
        Assert.Equal(tasks[0].Id, match.GetProperty("FirstTaskId").GetString());
        Assert.Equal(tasks[1].Id, match.GetProperty("SecondTaskId").GetString());
        Assert.Equal(tasks[0].Version, match.GetProperty("FirstTaskVersion").GetInt64());
        Assert.Equal(tasks[1].Version, match.GetProperty("SecondTaskVersion").GetInt64());
        Assert.Empty(Store().ListReviews());
        Assert.Equal(suggestion.Id, Suggestion(Workspace()).Id);
    }

    [Fact]
    public void Either_version_change_refreshes_suggestion_unread_even_when_the_other_version_is_larger()
    {
        var workspace = Seed();
        var first = Store().ListTasks().OrderBy(item => item.Id).First();
        var second = Store().ListTasks().OrderBy(item => item.Id).Last();
        for (var index = 0; index < 3; index++)
        {
            var current = Store().FindTask(first.Id)!;
            workspace.Execute(new(Account, "edit-task", first.Id, "edit-" + index, current.Version,
                Date: "2026-10-25", TimeKind: "deadline", Title: "手工核对" + index, Course: "数学", Priority: 2), Now);
        }
        var suggestion = Suggestion(workspace);
        workspace.Execute(new(Account, "mark-read", suggestion.Id, "read", suggestion.Version), Now);
        Assert.False(Suggestion(Workspace()).Unread);
        workspace.Execute(new(Account, "complete", second.Id, "complete", second.Version), Now);
        var changed = Suggestion(workspace);
        Assert.Equal(suggestion.Id, changed.Id);
        Assert.True(changed.Unread);
        Assert.True(changed.Version > suggestion.Version);
        Assert.Throws<InvalidOperationException>(() => workspace.Execute(new(Account, "mark-read", suggestion.Id,
            "stale-read", suggestion.Version), Now));
        Assert.Equal("2026-10-25", workspace.Snapshot(Now).Items.Single(item => item.Id == first.Id).Due);
        Assert.Equal("completed", workspace.Snapshot(Now).Items.Single(item => item.Id == second.Id).State);
    }

    [Theory]
    [InlineData("keep-separate")]
    [InlineData("merge-tasks")]
    public void Explicit_decision_removes_suggestion_and_survives_reopen(string operation)
    {
        var workspace = Seed();
        _ = Suggestion(workspace);
        var pair = Store().ListTasks().OrderBy(item => item.Id).ToArray();
        workspace.Execute(new(Account, operation, pair[0].Id, "decision", pair[0].Version,
            Priority: operation == "merge-tasks" ? 1 : null, MergeWithId: pair[1].Id, MergeWithVersion: pair[1].Version,
            MergeSelection: operation == "merge-tasks" ? new(pair[0].Id, pair[0].Id, pair[0].Id, pair[0].Id, pair[0].Id) : null), Now);
        Assert.DoesNotContain(Workspace().Snapshot(Now).Items, item => item.Kind == "merge-review");
        Assert.Equal(operation == "merge-tasks" ? 1 : 2, Store().ListTasks().Count);
        if (operation == "merge-tasks") Assert.Equal(2, Store().FindTask(pair[0].Id)!.Evidence.Count);
    }

    [Fact]
    public void Split_family_is_not_suggested_again_and_group_exclusion_removes_current_suggestion()
    {
        var workspace = Seed();
        _ = Suggestion(workspace);
        workspace.Execute(new(Account, "confirm-group", "200", "exclude", 0, Course: "数学", Excluded: true), Now);
        Assert.DoesNotContain(workspace.Snapshot(Now).Items, item => item.Kind == "merge-review");
        workspace.Execute(new(Account, "confirm-group", "200", "include", 0, Course: "数学"), Now);
        _ = Suggestion(workspace);
        var parent = Store().ListTasks()[0];
        workspace.Execute(new(Account, "split-task", parent.Id, "split", parent.Version,
            TimeKind: "deadline", Title: "独立实验", Course: "数学", Priority: 1,
            NewItemId: "split-review", State: "active", EvidenceIndexes: [0]), Now);
        Assert.DoesNotContain(Workspace().Snapshot(Now).Items, item => item.Kind == "merge-review");
        Assert.Contains(Workspace().Snapshot(Now).Items, item => item.Id == "split-review" && item.Kind == "review");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
