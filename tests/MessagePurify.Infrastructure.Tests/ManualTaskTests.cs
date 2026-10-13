using System.Text.Json;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Application;
using MessagePurify.Infrastructure.NapCat;
using MessagePurify.Infrastructure.Storage;
using MessagePurify.Infrastructure.Tasks;
using Microsoft.Data.Sqlite;

namespace MessagePurify.Infrastructure.Tests;

public sealed class ManualTaskTests : IDisposable
{
    private const string Account = "9007199254740993";
    private const string Group = "9007199254740995";
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 10, 0, 0, TimeSpan.FromHours(8));
    private readonly string root = Path.Combine(Path.GetTempPath(), "MessagePurify-manual-" + Guid.NewGuid().ToString("N"));
    private AccountWorkspace Workspace() => new(root, Account);
    private AccountTaskStore Store() => new(root, Account);
    private static WorkspaceCommand Create(string id = "manual-1", string operationId = "create-manual-1") =>
        new(Account, "create-task", id, operationId, 0, Date: "2026-10-21", TimeKind: "deadline",
            Course: "数学", Title: "补做课堂练习");
    private static string? Source(object item) => JsonSerializer.SerializeToElement(item).GetProperty("Source").GetString();

    [Fact]
    public void Create_PersistsProtectedTaskWithManualSourceAndNoMessageEvidence()
    {
        Workspace().Execute(Create() with { Title = " 补做课堂练习 ", Course = " 数学 " }, Now);

        var task = Assert.Single(Store().ListTasks());
        Assert.Equal("补做课堂练习", task.Title);
        Assert.Equal("数学", task.Course);
        Assert.Equal("manual", Source(task));
        Assert.Equal(TaskState.Active, task.State);
        Assert.Equal(RuleAction.Complete, task.Action);
        Assert.True(task.TimeProtected);
        Assert.Equal(1, task.Version);
        Assert.Equal(1, task.TimeVersion);
        Assert.Equal(new DateOnly(2026, 10, 21), task.Time.Date);
        Assert.Null(task.Time.Time);
        Assert.Equal("Asia/Shanghai", task.Time.TimeZoneId);
        Assert.Null(task.PriorityOverride);
        Assert.Empty(task.Evidence);
        Assert.Empty(Store().ListReviews());
        Assert.Equal(0, CountRows(Store().DatabasePath, "task_evidence"));
        Assert.Equal(0, CountRows(new AccountMessageStore(root, Account).DatabasePath, "messages"));
        var item = Assert.Single(Workspace().Snapshot(Now).Items);
        Assert.Equal("manual", Source(item));
        Assert.Equal("手动添加", item.Group);
        Assert.Equal("complete", item.Action);
        Assert.True(item.Unread);
        Assert.Empty(item.Evidence);
        Assert.Equal("手动添加任务", Assert.Single(item.Changes!).Label);
        Assert.Empty(Assert.Single(item.Changes!).Evidence);
        Assert.Equal("create-task", Assert.Single(Store().ListTaskHistory()).Kind);
    }

    [Fact]
    public void Create_AutomaticPriorityTracksTheCurrentClock()
    {
        var workspace = Workspace();
        workspace.Execute(Create(), Now);
        Assert.Equal(3, Assert.Single(workspace.Snapshot(Now).Items).Priority);
        Assert.Equal(2, Assert.Single(workspace.Snapshot(Now.AddDays(2)).Items).Priority);
        Assert.Equal(1, Assert.Single(workspace.Snapshot(Now.AddDays(5)).Items).Priority);
        Assert.Equal(0, Assert.Single(workspace.Snapshot(Now.AddDays(8)).Items).Priority);
        Assert.Null(Store().FindTask("manual-1")!.PriorityOverride);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Create_SelectedPriorityPersistsInsteadOfFollowingTheClock(int priority)
    {
        Workspace().Execute(Create() with { Priority = priority }, Now);
        Assert.Equal(priority, Store().FindTask("manual-1")!.PriorityOverride);
        Assert.Equal(priority, Assert.Single(Workspace().Snapshot(Now.AddDays(30)).Items).Priority);
    }

    [Fact]
    public void Create_ClockAndStartKindArePreservedThroughRestart()
    {
        Workspace().Execute(Create() with { TimeKind = "start", Time = "18:30" }, Now);
        var task = Store().FindTask("manual-1")!;
        Assert.Equal(TimeKind.Start, task.Time.Kind);
        Assert.Equal(new TimeOnly(18, 30), task.Time.Time);
        var item = Assert.Single(Workspace().Snapshot(Now).Items);
        Assert.Equal("start", item.TimeKind);
        Assert.Equal("18:30", item.Time);
    }

    [Fact]
    public void Create_ReplaysOneOperationAndAllowsIdenticalTitlesWithDifferentIds()
    {
        var workspace = Workspace();
        workspace.Execute(Create(), Now);
        Workspace().Execute(Create(), Now.AddMinutes(1));
        Assert.Single(Store().ListTasks());
        Assert.Single(Store().ListTaskHistory());
        Workspace().Execute(Create("manual-2", "create-manual-2"), Now.AddMinutes(2));
        Assert.Equal(2, Store().ListTasks().Count);
        Assert.Equal(2, Store().ListTaskHistory().Count);
    }

    [Fact]
    public void Create_ConflictingOperationOrExistingIdCannotOverwriteTheTask()
    {
        var workspace = Workspace();
        workspace.Execute(Create(), Now);
        Assert.Throws<InvalidOperationException>(() => workspace.Execute(Create() with { Title = "另一任务" }, Now));
        Assert.Throws<InvalidOperationException>(() => workspace.Execute(Create("manual-2"), Now));
        Assert.Throws<InvalidOperationException>(() => workspace.Execute(Create(operationId: "other-operation"), Now));
        Assert.Equal("补做课堂练习", Assert.Single(Store().ListTasks()).Title);
        Assert.Single(Store().ListTaskHistory());
    }

    public static TheoryData<string, string?> InvalidFields => new()
    {
        { "Title", null }, { "Title", "" }, { "Title", "  " }, { "Title", "标题\n换行" },
        { "Title", new string('字', 257) },
        { "Course", null }, { "Course", "" }, { "Course", "  " }, { "Course", "课程\t" },
        { "Course", new string('字', 257) },
        { "Date", null }, { "Date", "" }, { "Date", "2026-02-30" }, { "Date", "26-10-21" },
        { "Time", "24:00" }, { "Time", "18:60" },
        { "TimeKind", null }, { "TimeKind", "" }, { "TimeKind", "unexpected" }
    };

    [Theory]
    [MemberData(nameof(InvalidFields))]
    public void Create_InvalidRequiredFieldsCannotPersistTaskOrOperation(string field, string? value)
    {
        var command = field switch
        {
            "Title" => Create() with { Title = value },
            "Course" => Create() with { Course = value },
            "Date" => Create() with { Date = value },
            "Time" => Create() with { Time = value },
            "TimeKind" => Create() with { TimeKind = value },
            _ => throw new ArgumentException(nameof(field))
        };
        Assert.ThrowsAny<ArgumentException>(() => Workspace().Execute(command, Now));
        Assert.Empty(Store().ListTasks());
        Assert.Empty(Store().ListTaskHistory());
        Workspace().Execute(Create(), Now);
        Assert.Single(Store().ListTasks());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void Create_InvalidPriorityCannotPersist(int priority)
    {
        Assert.Throws<ArgumentException>(() => Workspace().Execute(Create() with { Priority = priority }, Now));
        Assert.Empty(Store().ListTasks());
    }

    [Fact]
    public void Create_RequiresNewRecordVersionAndMatchingAccount()
    {
        Assert.Throws<ArgumentException>(() => Workspace().Execute(Create() with { Version = 1 }, Now));
        Assert.Throws<ArgumentException>(() => Workspace().Execute(Create() with { Version = -1 }, Now));
        Assert.Throws<ArgumentException>(() => Workspace().Execute(Create() with { AccountId = "123" }, Now));
        Assert.Empty(Store().ListTasks());
        new AccountWorkspace(root, "123").Execute(Create() with { AccountId = "123" }, Now);
        Assert.Empty(Store().ListTasks());
        Assert.Single(new AccountTaskStore(root, "123").ListTasks());
    }

    [Fact]
    public void ManualSourceSurvivesEditingCompletionUndoArchiveRestoreAndCancellation()
    {
        Workspace().Execute(Create(), Now);
        var store = Store();
        var edited = store.EditTask("manual-1", "edit", 1, "更正练习", "数学", new(TimeKind.Deadline,
            new(2026, 10, 22), new(18, 0), "Asia/Shanghai"), 2, Now);
        var completed = store.Complete(edited.Id, "complete", edited.Version, Now);
        var undone = store.UndoComplete(completed.Id, "undo", completed.Version, Now.AddMinutes(1), "complete");
        var archived = store.Archive(undone.Id, "archive", undone.Version, Now.AddMinutes(2));
        var restored = store.RestoreArchived(archived.Id, "restore", archived.Version, Now.AddMinutes(3));
        var cancelled = store.Cancel(restored.Id, "cancel", restored.Version, Now.AddMinutes(4));
        Assert.All(new[] { edited, completed, undone, archived, restored, cancelled }, task =>
        {
            Assert.Equal("manual", Source(task));
            Assert.Empty(task.Evidence);
        });
        Assert.Equal(TaskState.Cancelled, Store().FindTask("manual-1")!.State);
        var item = Assert.Single(Workspace().Snapshot(Now.AddMinutes(5)).Items);
        Assert.Equal("manual", Source(item));
        Assert.Equal("手动添加", item.Group);
    }

    [Fact]
    public void ManualTaskWithoutEvidenceDoesNotBecomeAnAutomaticGroupChangeTarget()
    {
        var workspace = Workspace();
        workspace.Execute(Create() with { Title = "提交统计作业" }, Now);
        workspace.SetAvailableGroups([new(Group, "数学课程群")], new HashSet<string> { Group });
        workspace.Execute(new(Account, "confirm-group", Group, "confirm-group", 0, Course: "数学"), Now);
        var message = new SchoolMessage(new(Account, Group, "1"), "teacher", Now,
            [new(SegmentKind.Text, "统计作业延期到10月23日18点提交")]);

        Assert.True(workspace.Ingest(message, new HashSet<string> { Group }, MessageOrigin.Realtime, "live", Now));

        var manual = Store().FindTask("manual-1")!;
        Assert.Equal(new DateOnly(2026, 10, 21), manual.Time.Date);
        Assert.Equal(1, manual.Version);
        Assert.Empty(manual.Evidence);
        Assert.Contains(Store().ListReviews(), review => review.Candidate.TargetTaskId is null);
    }

    [Fact]
    public void LegacyJsonWithoutSourceKeepsQQTaskPresentation()
    {
        var task = new TaskItem("legacy", Account, "旧任务", "数学", RuleAction.Submit,
            new(TimeKind.Deadline, new(2026, 10, 21), null, "Asia/Shanghai"), TaskState.Active,
            false, 3, 1, 1, [new(Account, Group, "old-message")], Now, null, null, null);
        var json = JsonSerializer.SerializeToElement(task).EnumerateObject()
            .Where(property => property.Name != "Source")
            .ToDictionary(property => property.Name, property => property.Value);
        var loaded = JsonSerializer.Deserialize<TaskItem>(JsonSerializer.Serialize(json))!;
        Assert.Equal("qq", Source(loaded));
    }

    [Theory]
    [InlineData("补交练习，逾期不收", 2, 1)]
    [InlineData("补交练习，不是逾期不收", 3, 2)]
    public void AutomaticPriority_UsesExplicitConsequencesFromManualTitle(string title, int initial, int later)
    {
        Workspace().Execute(Create() with { Title = title }, Now);
        Assert.Equal(initial, Assert.Single(Workspace().Snapshot(Now).Items).Priority);
        Assert.Equal(later, Assert.Single(Workspace().Snapshot(Now.AddDays(2)).Items).Priority);
    }

    [Fact]
    public void StoreCreation_ValidatesTimeZoneAndTimeKindWithoutPersisting()
    {
        var time = new TaskTime(TimeKind.Deadline, new(2026, 10, 21), null, "Asia/Shanghai");
        var store = Store();
        Assert.Throws<ArgumentException>(() => store.CreateManualTask("manual-1", "练习", "数学",
            time with { TimeZoneId = "not-a-time-zone" }, null, "invalid-zone", Now));
        Assert.Throws<ArgumentException>(() => store.CreateManualTask("manual-1", "练习", "数学",
            time with { TimeZoneId = "UTC" }, null, "utc-zone", Now));
        Assert.Throws<ArgumentException>(() => store.CreateManualTask("manual-1", "练习", "数学",
            time with { Kind = (TimeKind)99 }, null, "invalid-kind", Now));
        Assert.Empty(Store().ListTasks());
        Assert.Empty(Store().ListTaskHistory());
    }

    [Fact]
    public void Create_Accepts256CharacterFieldsAndKeepsVersionedReadBehavior()
    {
        var workspace = Workspace();
        workspace.Execute(Create() with { Title = new string('字', 256), Course = new string('课', 256) }, Now);
        var item = Assert.Single(workspace.Snapshot(Now).Items);
        workspace.Execute(new(Account, "mark-read", item.Id, "read", item.Version), Now);
        Assert.False(Assert.Single(workspace.Snapshot(Now).Items).Unread);
        Assert.Equal(256, Store().FindTask(item.Id)!.Title.Length);
        Assert.Equal(256, Store().FindTask(item.Id)!.Course!.Length);
        var edited = Store().EditTask(item.Id, "edit", item.Version, "修改的任务", null,
            new(TimeKind.Deadline, new(2026, 10, 21), null, "Asia/Shanghai"), 2, Now);
        Assert.Throws<InvalidOperationException>(() => workspace.Execute(new(Account, "mark-read", item.Id, "stale-read", item.Version), Now));
        var changed = Assert.Single(workspace.Snapshot(Now).Items);
        Assert.True(changed.Unread);
        Assert.Equal("已修改", changed.ChangeLabel);
        Assert.Equal("manual", Source(changed));
        Assert.Equal("手动添加", changed.Group);
        Assert.Null(changed.Course);
        Assert.Equal(edited.Version, changed.Version);
    }

    private static long CountRows(string databasePath, string table)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = databasePath, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM " + table + ";";
        return (long)command.ExecuteScalar()!;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
