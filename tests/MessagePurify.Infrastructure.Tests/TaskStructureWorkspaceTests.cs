using MessagePurify.Core;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.Tasks;
using Microsoft.Data.Sqlite;

namespace MessagePurify.Infrastructure.Tests;

public sealed class TaskStructureWorkspaceTests : IDisposable
{
    private const string Account = "123";
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 10, 0, 0, TimeSpan.FromHours(8));
    private readonly string root = Path.Combine(Path.GetTempPath(), "MessagePurify-structure-workspace-" + Guid.NewGuid().ToString("N"));
    private AccountWorkspace Workspace() => new(root, Account);
    private AccountTaskStore Store() => new(root, Account);
    private void Seed()
    {
        foreach (var (id, day) in new[] { ("a", 20), ("b", 21) })
            Workspace().Execute(new(Account, "create-task", id, "create-" + id, 0, Date: $"2026-10-{day}",
                TimeKind: "deadline", Title: "作业" + id, Course: "数学"), Now);
    }
    private static WorkspaceCommand Merge() => new(Account, "merge-tasks", "a", "merge", 1,
        Priority: 3, MergeWithId: "b", MergeWithVersion: 1, MergeSelection: new("a", "b", "b", "a", "a"));
    private static WorkspaceCommand Split() => new(Account, "split-task", "a", "split", 1,
        Date: "2026-10-22", TimeKind: "start", Title: "独立实验", Course: null,
        NewItemId: "child", State: "active", EvidenceIndexes: []);

    [Fact]
    public void Merge_projects_old_id_redirect_and_selected_values_after_reopen()
    {
        Seed(); Workspace().Execute(Merge(), Now);
        var items = Workspace().Snapshot(Now).Items;
        var kept = Assert.Single(items, item => item.Id == "a");
        var old = Assert.Single(items, item => item.Id == "b");
        Assert.Equal("2026-10-21", kept.Due);
        Assert.Equal("active", kept.State);
        Assert.Equal("merged", old.State);
        Assert.Equal("a", old.MergedIntoId);
        Assert.Contains("b", kept.RelatedItemIds!);
        Assert.Contains(kept.Changes!, change => change.Label == "合并重复事项");
        Assert.Contains(kept.Changes!, change => change.Label == "手动添加任务" && change.Fields.Any(field => field.After == "作业b"));
        Workspace().Execute(Merge(), Now.AddMinutes(1));
        Assert.Single(Store().ListTasks());
    }

    [Fact]
    public void Split_projects_navigation_and_does_not_change_parent_formal_fields()
    {
        Seed(); Workspace().Execute(Split(), Now);
        var items = Workspace().Snapshot(Now).Items;
        var parent = items.Single(item => item.Id == "a");
        var child = items.Single(item => item.Id == "child");
        Assert.Equal("2026-10-20", parent.Due);
        Assert.Equal("2026-10-22", child.Due);
        Assert.Equal("start", child.TimeKind);
        Assert.Null(child.Course);
        Assert.Contains("child", parent.RelatedItemIds!);
        Assert.Contains("a", child.RelatedItemIds!);
        Workspace().Execute(Split(), Now.AddMinutes(1));
        Assert.Equal(3, Store().ListTasks().Count);
    }

    [Theory]
    [InlineData("other-account")]
    [InlineData("stale")]
    [InlineData("bad-evidence")]
    [InlineData("missing-date")]
    public void Invalid_structure_commands_do_not_write_any_task(string failure)
    {
        Seed(); var command = failure switch
        {
            "other-account" => Merge() with { AccountId = "456" },
            "stale" => Merge() with { MergeWithVersion = 2 },
            "bad-evidence" => Split() with { EvidenceIndexes = [0] },
            _ => Split() with { Date = null, TimeKind = "deadline" }
        };
        Assert.ThrowsAny<Exception>(() => Workspace().Execute(command, Now));
        Assert.Equal(2, Store().ListTasks().Count);
        Assert.All(Store().ListTasks(), item => Assert.Equal(1, item.Version));
    }

    [Fact]
    public void Keep_separate_is_persisted_and_can_be_explicitly_replaced_by_merge()
    {
        Seed(); Workspace().Execute(Merge() with { Operation = "keep-separate", MergeSelection = null }, Now);
        Assert.Contains("b", Store().FindTask("a")!.SeparateTaskIds!);
        Workspace().Execute(Merge() with { OperationId = "explicit-merge", Version = 2, MergeWithVersion = 2 }, Now);
        Assert.Single(Store().ListTasks());
    }

    [Fact]
    public void Split_without_date_preserves_start_kind_and_navigates_after_acceptance()
    {
        var time = new TaskTime(TimeKind.Deadline, new(2026, 10, 20), null, "Asia/Shanghai");
        var assessment = new RuleAssessment("明确", true);
        Store().CreateTask("a", new("实验", RuleAction.Submit, RuleDisposition.Task, "数学", time, null, null, 2,
            new(assessment, assessment, assessment, assessment, assessment, assessment), [], [new(Account, "100", "1")]), "qq-create", Now);
        Workspace().Execute(Split() with { Date = null, EvidenceIndexes = [0] }, Now);
        var review = Workspace().Snapshot(Now).Items.Single(item => item.Id == "child");
        Assert.Equal("review", review.Kind);
        Assert.Equal("pending", review.State);
        Assert.Equal("start", review.TimeKind);
        Assert.Contains("a", review.RelatedItemIds!);
        Workspace().Execute(new(Account, "accept-review", "child", "accept-split", review.Version,
            Date: "2026-10-22", TimeKind: "start"), Now);
        var reopened = Workspace().Snapshot(Now).Items;
        var accepted = reopened.Single(item => item.Id == "child");
        Assert.Equal("accepted", accepted.State);
        Assert.Equal("accepted-child", accepted.TargetTaskId);
        Assert.Equal("start", reopened.Single(item => item.Id == accepted.TargetTaskId).TimeKind);
    }

    [Fact]
    public void Split_of_task_with_new_requirements_can_be_confirmed_without_linked_change_target()
    {
        Seed(); var assessment = new RuleAssessment("明确", true);
        Store().SavePending("new-requirement", new("补充要求", RuleAction.NewRequirement, RuleDisposition.Change, "数学",
            new(TimeKind.Deadline, new(2026, 10, 20), null, "Asia/Shanghai"), null, "a", 2,
            new(assessment, assessment, assessment, assessment, assessment, assessment), [], [new(Account, "100", "new-message")]), "save-requirement", Now);
        Store().AcceptReview("new-requirement", "a", "accept-requirement", 1, null, Now, 1);
        Assert.Equal(RuleAction.NewRequirement, Store().FindTask("a")!.Action);
        Workspace().Execute(Split() with { Version = 2, Date = null, EvidenceIndexes = [0] }, Now);
        var review = Workspace().Snapshot(Now).Items.Single(item => item.Id == "child");
        Assert.True(review.CanAccept);
        Workspace().Execute(new(Account, "accept-review", review.Id, "confirm-split", review.Version,
            Date: "2026-10-23", TimeKind: "start"), Now);
        Assert.Equal("2026-10-23", Workspace().Snapshot(Now).Items.Single(item => item.Id == "accepted-child").Due);
    }

    [Fact]
    public void Merge_preserves_explicitly_selected_priority_when_time_comes_from_other_task()
    {
        Workspace().Execute(new(Account, "create-task", "a", "today", 0, Date: "2026-10-13",
            TimeKind: "deadline", Title: "今天作业", Course: "数学"), Now);
        Workspace().Execute(new(Account, "create-task", "b", "later", 0, Date: "2026-11-13",
            TimeKind: "deadline", Title: "稍后作业", Course: "数学"), Now);
        Assert.Equal(0, Workspace().Snapshot(Now).Items.Single(item => item.Id == "a").Priority);
        Workspace().Execute(Merge() with { Priority = 0 }, Now);
        var merged = Workspace().Snapshot(Now.AddDays(1)).Items.Single(item => item.Id == "a");
        Assert.Equal("2026-11-13", merged.Due);
        Assert.Equal(0, merged.Priority);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
