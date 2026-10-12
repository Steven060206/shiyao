using MessagePurify.Core;
using MessagePurify.Infrastructure.Reminders;
using MessagePurify.Infrastructure.Tasks;
using Microsoft.Data.Sqlite;
using Xunit;

namespace MessagePurify.Infrastructure.Tests;

public sealed class PersistedReminderTests : IDisposable
{
    private const string Account = "123456";
    private readonly string root = Path.Combine(Path.GetTempPath(), "reminder-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 8, 0, 0, TimeSpan.FromHours(8));
    private AccountTaskStore Tasks() => new(root, Account);
    private AccountReminderService Service() => new(root, Account, Tasks());

    [Fact]
    public void DefaultExactTime_UsesTwentyFourAndTwoHoursAndSurvivesReopen()
    {
        Create("one", new(2026, 10, 14), new(18, 0));
        var plan = Assert.Single(Service().Synchronize(Now));
        Assert.Equal(new[] { new DateTimeOffset(2026, 10, 13, 18, 0, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2026, 10, 14, 16, 0, 0, TimeSpan.FromHours(8)) }, plan.Nodes.Select(n => n.ScheduledAt));
        var saved = Assert.Single(Service().Synchronize(Now));
        Assert.Equal(plan.Nodes.Select(n => n.Id), saved.Nodes.Select(n => n.Id));
        Assert.Equal(plan.PlanVersion, saved.PlanVersion);
    }

    [Fact]
    public void DefaultDate_UsesPreviousNoonAndLeavesFormalTimeUnspecified()
    {
        Create("one", new(2026, 10, 14));
        var node = Assert.Single(Assert.Single(Service().Synchronize(Now)).Nodes);
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.FromHours(8)), node.ScheduledAt);
        Assert.Null(Tasks().FindTask("one")!.Time.Time);
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("cancel")]
    [InlineData("archive")]
    public void InactiveTask_DoesNotSubmitDueNode(string action)
    {
        var task = Create("one", new(2026, 10, 13), new(18, 0));
        var tasks = Tasks();
        if (action == "complete") tasks.Complete(task.Id, action, task.Version, Now);
        else if (action == "cancel") tasks.Cancel(task.Id, action, task.Version, Now);
        else tasks.Archive(task.Id, action, task.Version, Now);
        var service = Service();
        Assert.False(Assert.Single(service.Synchronize(Now)).Active);
        Assert.Empty(service.DispatchDue(Now.AddHours(11), new Sink()));
        Assert.Empty(service.Deliveries());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ExactDeadlineAndStartAtBoundary_AreExcluded(int kind)
    {
        Create("one", new(2026, 10, 12), new(18, 0), (TimeKind)kind);
        var service = Service();
        Assert.Empty(service.DispatchDue(Now.AddHours(10), new Sink(), recovering: true));
        Assert.False(Assert.Single(service.Synchronize(Now.AddHours(10))).Active);
    }

    [Fact]
    public void DateOnly_OnDateRemainsActiveAndCanRecoverUntilNextDay()
    {
        Create("one", new(2026, 10, 12));
        var service = Service();
        Assert.Single(service.DispatchDue(Now.AddHours(15), new Sink(), true));
        Assert.False(Assert.Single(service.Synchronize(Now.AddDays(1))).Active);
    }

    [Fact]
    public void HourPreference_PersistsAndMovesWithTimeThenFallsBackOnDatePrecision()
    {
        var task = Create("one", new(2026, 10, 14), new(18, 0));
        var service = Service();
        var original = Assert.Single(service.Synchronize(Now));
        var custom = service.Configure(task.Id, task.Version, original.PlanVersion, new("hours", Hours: 1), "config", Now);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 17, 0, 0, TimeSpan.FromHours(8)), Assert.Single(custom.Nodes).ScheduledAt);
        var changed = Tasks().AcceptTime(task.Id, "postpone", task.Version,
            new(TimeKind.Deadline, new(2026, 10, 16), new(18, 0), "Asia/Shanghai"), Now);
        var postponed = Assert.Single(Service().Synchronize(Now));
        Assert.Equal(custom.Preference, postponed.Preference);
        Assert.Equal(new DateTimeOffset(2026, 10, 16, 17, 0, 0, TimeSpan.FromHours(8)), Assert.Single(postponed.Nodes).ScheduledAt);
        Tasks().AcceptTime(task.Id, "date", changed.Version, changed.Time with { Time = null }, Now);
        var date = Assert.Single(Service().Synchronize(Now));
        Assert.True(date.NeedsReview);
        Assert.Equal(custom.Preference, date.Preference);
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.FromHours(8)), Assert.Single(date.Nodes).ScheduledAt);
    }

    [Theory]
    [InlineData("off")]
    [InlineData("calendar")]
    public void OffAndCalendarPreference_RemainAfterDatePrecisionChange(string mode)
    {
        var task = Create("one", new(2026, 10, 14), new(18, 0));
        var service = Service();
        var plan = Assert.Single(service.Synchronize(Now));
        var preference = mode == "off" ? new ReminderPreference("off") : new("calendar", Days: 1, Clock: "20:00");
        service.Configure(task.Id, task.Version, plan.PlanVersion, preference, "config", Now);
        Tasks().AcceptTime(task.Id, "date", task.Version,
            new(TimeKind.Deadline, new(2026, 10, 16), null, "Asia/Shanghai"), Now);
        var saved = Assert.Single(Service().Synchronize(Now));
        Assert.Equal(preference, saved.Preference);
        Assert.False(saved.NeedsReview);
        if (mode == "off") Assert.Empty(saved.Nodes);
        else Assert.Equal(new DateTimeOffset(2026, 10, 15, 20, 0, 0, TimeSpan.FromHours(8)), Assert.Single(saved.Nodes).ScheduledAt);
    }

    [Fact]
    public void Configure_ChecksVersionsAndReplaysOperationWithoutChangingPlan()
    {
        var task = Create("one", new(2026, 10, 14), new(18, 0));
        var service = Service();
        var plan = Assert.Single(service.Synchronize(Now));
        Assert.Throws<InvalidOperationException>(() => service.Configure(task.Id, 0, plan.PlanVersion, new("off"), "wrong-task", Now));
        Assert.Throws<InvalidOperationException>(() => service.Configure(task.Id, task.Version, 0, new("off"), "wrong-plan", Now));
        var first = service.Configure(task.Id, task.Version, plan.PlanVersion, new("off"), "config", Now);
        var retry = Service().Configure(task.Id, task.Version, plan.PlanVersion, new("off"), "config", Now.AddSeconds(1));
        Assert.Equal(first.PlanVersion, retry.PlanVersion);
        Assert.Equal(first.Preference, retry.Preference);
        Assert.Throws<InvalidOperationException>(() => service.Configure(task.Id, task.Version, plan.PlanVersion, new(), "config", Now));
        var current = Assert.Single(Service().Synchronize(Now));
        var identical = service.Configure(task.Id, task.Version, current.PlanVersion, new("off"), "same-preference", Now);
        Assert.Equal(current.PlanVersion, identical.PlanVersion);
    }

    [Theory]
    [InlineData("hours", -1d, null, null)]
    [InlineData("hours", 0d, null, null)]
    [InlineData("calendar", null, -1, "12:00")]
    [InlineData("calendar", null, 1, "25:00")]
    [InlineData("calendar", null, 0, "18:00")]
    [InlineData("off", 1d, null, null)]
    [InlineData("other", null, null, null)]
    public void InvalidPreference_IsRejectedWithoutChangingPlan(string mode, double? hours, int? days, string? clock)
    {
        var task = Create("one", new(2026, 10, 14), new(18, 0));
        var service = Service();
        var plan = Assert.Single(service.Synchronize(Now));
        Assert.ThrowsAny<ArgumentException>(() => service.Configure(task.Id, task.Version, plan.PlanVersion,
            new(mode, hours, days, clock), "invalid", Now));
        Assert.Equal(plan.Preference, Assert.Single(Service().Synchronize(Now)).Preference);
    }

    [Fact]
    public void DateOnly_RejectsNewHoursButAllowsSameDayCalendarWithoutInventingTime()
    {
        var task = Create("one", new(2026, 10, 14));
        var service = Service();
        var plan = Assert.Single(service.Synchronize(Now));
        Assert.Throws<ArgumentException>(() => service.Configure(task.Id, task.Version, plan.PlanVersion, new("hours", Hours: 1), "hours", Now));
        var configured = service.Configure(task.Id, task.Version, plan.PlanVersion, new("calendar", Days: 0, Clock: "20:00"), "calendar", Now);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 20, 0, 0, TimeSpan.FromHours(8)), Assert.Single(configured.Nodes).ScheduledAt);
        Assert.Null(Tasks().FindTask(task.Id)!.Time.Time);
    }

    [Fact]
    public void Submission_IsDurablySendingBeforeExternalCallAndDoesNotMarkTaskSeen()
    {
        Create("one", new(2026, 10, 13), new(18, 0));
        var service = Service();
        var sink = new Sink { OnSubmit = delivery =>
        {
            var durable = Assert.Single(Service().Deliveries());
            Assert.Equal(delivery.Id, durable.Id);
            Assert.Equal("sending", durable.State);
            Assert.Equal("sending", Assert.Single(Assert.Single(Service().Synchronize(Now)).Nodes, n => n.ScheduledAt <= Now.AddHours(11)).State);
            return new("submitted", delivery.Id);
        }};
        var sent = Assert.Single(service.DispatchDue(Now.AddHours(11), sink));
        Assert.Equal("submitted", sent.State);
        Assert.Equal("submitted", Assert.Single(Service().Deliveries()).State);
        Assert.Equal(1, Tasks().FindTask("one")!.Version);
        Assert.Empty(Service().DispatchDue(Now.AddHours(11), sink));
    }

    [Fact]
    public void ContentEditAndCompleteUndo_DoNotCreateOrReplaySubmittedNode()
    {
        var task = Create("one", new(2026, 10, 13), new(18, 0));
        var service = Service();
        var due = Now.AddHours(11);
        var sent = Assert.Single(service.DispatchDue(due, new Sink()));
        var before = Assert.Single(service.Synchronize(due));
        using (var connection = Open(Tasks().DatabasePath))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE tasks SET record_json = json_set(record_json, '$.Title', '新标题', '$.Version', 2) WHERE id = 'one';";
            command.ExecuteNonQuery();
        }
        var edited = Assert.Single(Service().Synchronize(due));
        Assert.Equal(before.PlanVersion, edited.PlanVersion);
        Assert.Equal(before.Nodes.Select(n => n.Id), edited.Nodes.Select(n => n.Id));
        var completed = Tasks().Complete(task.Id, "complete", 2, due);
        var sink = new Sink();
        Service().ReconcileNotifications(sink, due);
        Assert.Contains(sent.NotificationId!, sink.Removed);
        Tasks().UndoComplete(task.Id, "undo", completed.Version, due.AddMinutes(1));
        Assert.Empty(Service().DispatchDue(due.AddMinutes(1), sink));
        Assert.Equal("submitted", Assert.Single(Service().Synchronize(due.AddMinutes(1))).Nodes[0].State);
    }

    [Fact]
    public void Recovery_MergesAllMissedNodesAndTasksAndNeverReplays()
    {
        Create("one", new(2026, 10, 12), new(18, 0));
        Create("two", new(2026, 10, 13), new(17, 0));
        var service = Service();
        var sent = Assert.Single(service.DispatchDue(Now.AddHours(9), new Sink(), true));
        Assert.Equal("recovery", sent.Kind);
        Assert.Equal(Account, sent.AccountId);
        Assert.Equal(new[] { "one", "two" }, sent.Members.Select(m => m.TaskId));
        Assert.Equal(2, sent.Members.Single(m => m.TaskId == "one").NodeIds.Count);
        Assert.Single(sent.Members.Single(m => m.TaskId == "two").NodeIds);
        Assert.Empty(Service().DispatchDue(Now.AddHours(9), new Sink(), true));
        Assert.All(Service().Synchronize(Now.AddHours(9)).SelectMany(p => p.Nodes.Where(n => n.ScheduledAt <= Now.AddHours(9))), n => Assert.Equal("submitted", n.State));
    }

    [Fact]
    public void LiveDispatch_MergesMissedNodesForSameTask()
    {
        Create("one", new(2026, 10, 12), new(18, 0));
        var sent = Assert.Single(Service().DispatchDue(Now.AddHours(9), new Sink()));
        Assert.Equal("time", sent.Kind);
        Assert.Equal(2, Assert.Single(sent.Members).NodeIds.Count);
    }

    [Fact]
    public void Blocked_IsPersistedAndNeverAutomaticallyResubmitted()
    {
        Create("one", new(2026, 10, 13), new(18, 0));
        var due = Now.AddHours(11);
        var sent = Assert.Single(Service().DispatchDue(due, new Sink { OnSubmit = _ => new("blocked", Error: "disabled") }));
        Assert.Equal("blocked", sent.State);
        Assert.Equal("disabled", sent.Error);
        Assert.Empty(Service().DispatchDue(due, new Sink()));
        Assert.Empty(Service().DispatchDue(due, new Sink(), true));
        Assert.Equal("blocked", Assert.Single(Service().Deliveries()).State);
    }

    [Fact]
    public void ExplicitFailure_RetriesSameIdAtMostThreeAttemptsAcrossReopens()
    {
        Create("one", new(2026, 10, 13), new(18, 0));
        var due = Now.AddHours(11);
        var sink = new Sink { OnSubmit = _ => new("failed", Error: "unavailable") };
        var attempts = Enumerable.Range(0, 3).Select(_ => Assert.Single(Service().DispatchDue(due, sink))).ToArray();
        Assert.Single(attempts.Select(a => a.Id).Distinct());
        Assert.All(attempts, a => Assert.Equal("failed", a.State));
        Assert.Empty(Service().DispatchDue(due, sink));
        Assert.Empty(Service().DispatchDue(due, sink, true));
        Assert.Single(Service().Deliveries());
    }

    [Fact]
    public void FailedSubmission_ResumesWithinRetryBudgetAfterCompleteUndo()
    {
        var task = Create("one", new(2026, 10, 13), new(18, 0));
        var due = Now.AddHours(11);
        var first = Assert.Single(Service().DispatchDue(due, new Sink { OnSubmit = _ => new("failed", Error: "unavailable") }));
        var completed = Tasks().Complete(task.Id, "complete", task.Version, due);
        Service().ReconcileNotifications(new Sink(), due);
        Assert.Empty(Service().DispatchDue(due, new Sink()));
        Tasks().UndoComplete(task.Id, "undo", completed.Version, due.AddMinutes(1));
        var resumed = Assert.Single(Service().DispatchDue(due.AddMinutes(1), new Sink()));
        Assert.Equal(first.Id, resumed.Id);
        Assert.Equal("submitted", resumed.State);
        Assert.Empty(Service().DispatchDue(due.AddMinutes(1), new Sink()));
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("postpone")]
    public void FailedRecoveryMemberInvalidation_RetriesOnlyEffectiveMembersWithOriginalId(string action)
    {
        var firstTask = Create("one", new(2026, 10, 13), new(18, 0));
        Create("two", new(2026, 10, 13), new(18, 0));
        var due = Now.AddHours(11);
        var failed = Assert.Single(Service().DispatchDue(due, new Sink { OnSubmit = _ => new("failed", Error: "unavailable") }, true));
        if (action == "complete") Tasks().Complete(firstTask.Id, "complete", firstTask.Version, due);
        else Tasks().AcceptTime(firstTask.Id, "postpone", firstTask.Version, firstTask.Time with { Date = new(2026, 10, 20) }, due);
        Service().ReconcileNotifications(new Sink(), due);
        var retry = Assert.Single(Service().DispatchDue(due, new Sink(), true));
        Assert.Equal(failed.Id, retry.Id);
        Assert.Equal("two", Assert.Single(retry.Members).TaskId);
        Assert.Equal("submitted", retry.State);
        Assert.Equal("submitted", Service().Synchronize(due).Single(p => p.TaskId == "two").Nodes[0].State);
        Assert.Empty(Service().DispatchDue(due, new Sink(), true));
    }

    [Fact]
    public void FailedRecoveryAfterPartialSuccess_UndoResumesUnsentMemberWithoutReplayingSuccessfulMember()
    {
        var task = Create("one", new(2026, 10, 13), new(18, 0));
        Create("two", new(2026, 10, 13), new(18, 0));
        var due = Now.AddHours(11);
        var failed = Assert.Single(Service().DispatchDue(due, new Sink { OnSubmit = _ => new("failed", Error: "unavailable") }, true));
        var completed = Tasks().Complete(task.Id, "complete", task.Version, due);
        var partial = Assert.Single(Service().DispatchDue(due, new Sink(), true));
        Assert.Equal("two", Assert.Single(partial.Members).TaskId);
        Tasks().UndoComplete(task.Id, "undo", completed.Version, due.AddMinutes(1));
        var resumed = Assert.Single(Service().DispatchDue(due.AddMinutes(1), new Sink(), true));
        Assert.Equal(failed.Id, resumed.Id);
        Assert.Equal("one", Assert.Single(resumed.Members).TaskId);
        Assert.All(Service().Synchronize(due).Select(p => p.Nodes[0]), n => Assert.Equal("submitted", n.State));
        Assert.Single(Service().Deliveries());
        Assert.Empty(Service().DispatchDue(due.AddMinutes(1), new Sink(), true));
    }

    [Fact]
    public void FailedRecoveryMemberChanges_DoNotResetPersistedRetryBudget()
    {
        var task = Create("one", new(2026, 10, 13), new(18, 0));
        Create("two", new(2026, 10, 13), new(18, 0));
        var due = Now.AddHours(11);
        var failing = new Sink { OnSubmit = _ => new("failed", Error: "unavailable") };
        var first = Assert.Single(Service().DispatchDue(due, failing, true));
        var completed = Tasks().Complete(task.Id, "complete", task.Version, due);
        for (var i = 0; i < 2; i++)
        {
            var retry = Assert.Single(Service().DispatchDue(due, failing, true));
            Assert.Equal(first.Id, retry.Id);
            Assert.Equal("two", Assert.Single(retry.Members).TaskId);
        }
        Tasks().UndoComplete(task.Id, "undo", completed.Version, due.AddMinutes(1));
        Assert.Empty(Service().DispatchDue(due.AddMinutes(1), new Sink(), true));
        Assert.Single(Service().Deliveries());
        Assert.All(Service().Synchronize(due).Select(p => p.Nodes[0]), n => Assert.Equal("failed", n.State));
    }

    [Fact]
    public void EarlierLiveSubmissionCancelingLaterTask_ExcludesLaterTask()
    {
        Create("one", new(2026, 10, 13), new(18, 0));
        var later = Create("two", new(2026, 10, 13), new(18, 0));
        var due = Now.AddHours(11);
        var sink = new Sink { OnSubmit = delivery =>
        {
            Assert.Equal("one", Assert.Single(delivery.Members).TaskId);
            Tasks().Cancel(later.Id, "cancel-later", later.Version, due);
            return new("submitted", delivery.Id);
        }};
        var sent = Assert.Single(Service().DispatchDue(due, sink));
        Assert.Equal("one", Assert.Single(sent.Members).TaskId);
        Assert.Single(Service().Deliveries());
        Assert.False(Service().Synchronize(due).Single(p => p.TaskId == later.Id).Active);
    }

    [Fact]
    public void RecoveryCrash_DoesNotGenerateSecondSummary()
    {
        Create("one", new(2026, 10, 13), new(18, 0));
        var due = Now.AddHours(11);
        Service().DispatchDue(due, new Sink(), true);
        using (var connection = Open(ReminderPath()))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE reminder_state SET record_json = replace(record_json, 'submitted', 'sending');";
            command.ExecuteNonQuery();
        }
        Assert.Empty(Service().DispatchDue(due, new Sink(), true));
        Assert.Equal("unknown", Assert.Single(Service().Deliveries()).State);
        Assert.Equal("covered", Service().Synchronize(due)[0].Nodes[0].State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingDeliveryLeftBeforeSubmission_RecoveryContinuesSummaryAndMergesTime(bool recovery)
    {
        Create("one", new(2026, 10, 13), new(18, 0));
        var due = Now.AddHours(11);
        var original = Assert.Single(Service().DispatchDue(due, new Sink(), recovery));
        using (var connection = Open(ReminderPath()))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE reminder_state SET record_json = replace(replace(record_json, 'submitted', 'pending'), '\"Attempts\":1', '\"Attempts\":0');";
            command.ExecuteNonQuery();
        }
        Assert.Equal("pending", Assert.Single(Service().Deliveries()).State);
        var continued = Assert.Single(Service().DispatchDue(due, new Sink(), true));
        if (recovery) Assert.Equal(original.Id, continued.Id);
        else Assert.NotEqual(original.Id, continued.Id);
        Assert.Equal("recovery", continued.Kind);
        Assert.Equal("submitted", continued.State);
        Assert.Equal(recovery ? 1 : 2, Service().Deliveries().Count);
        Assert.Empty(Service().DispatchDue(due, new Sink(), true));
    }

    [Fact]
    public void MultiplePendingTimeDeliveries_RecoverySubmitsOneCombinedSummary()
    {
        Create("one", new(2026, 10, 13), new(18, 0));
        Create("two", new(2026, 10, 13), new(18, 0));
        var due = Now.AddHours(11);
        Assert.Equal(2, Service().DispatchDue(due, new Sink()).Count);
        LeavePendingBeforeSubmission();
        var summary = Assert.Single(Service().DispatchDue(due, new Sink(), true));
        Assert.Equal("recovery", summary.Kind);
        Assert.Equal(new[] { "one", "two" }, summary.Members.Select(m => m.TaskId));
        Assert.Equal(2, Service().Deliveries().Count(d => d.State == "superseded"));
        Assert.Empty(Service().DispatchDue(due, new Sink(), true));
    }

    [Fact]
    public void PendingRecoveryAndNewlyDueTask_JoinOriginalSummaryIdBeforeSending()
    {
        Create("one", new(2026, 10, 13), new(18, 0));
        Create("two", new(2026, 10, 13), new(20, 0));
        var earlier = Now.AddHours(11);
        var original = Assert.Single(Service().DispatchDue(earlier, new Sink(), true));
        Assert.Single(original.Members);
        LeavePendingBeforeSubmission();
        var later = Now.AddHours(13);
        var summary = Assert.Single(Service().DispatchDue(later, new Sink(), true));
        Assert.Equal(original.Id, summary.Id);
        Assert.Equal(new[] { "one", "two" }, summary.Members.Select(m => m.TaskId));
        Assert.Single(Service().Deliveries());
        Assert.Empty(Service().DispatchDue(later, new Sink(), true));
    }

    private void LeavePendingBeforeSubmission()
    {
        using var connection = Open(ReminderPath());
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE reminder_state SET record_json = replace(replace(record_json, 'submitted', 'pending'), '\"Attempts\":1', '\"Attempts\":0');";
        command.ExecuteNonQuery();
    }

    [Fact]
    public void UnknownTimeResult_IsNotReplayedAndCanJoinOnlyOneRecovery()
    {
        Create("one", new(2026, 10, 13), new(18, 0));
        var due = Now.AddHours(11);
        var first = Assert.Single(Service().DispatchDue(due, new Sink { OnSubmit = _ => throw new IOException("uncertain") }));
        Assert.Equal("unknown", first.State);
        Assert.Empty(Service().DispatchDue(due, new Sink()));
        var summary = Assert.Single(Service().DispatchDue(due, new Sink { OnSubmit = _ => new("unknown", Error: "uncertain summary") }, true));
        Assert.Equal("recovery", summary.Kind);
        Assert.Empty(Service().DispatchDue(due, new Sink(), true));
        Assert.Equal(2, Service().Deliveries().Count);
    }

    [Fact]
    public void SendingLeftByCrash_IsNotReplayedAsIndividualTimeNotification()
    {
        Create("one", new(2026, 10, 13), new(18, 0));
        var due = Now.AddHours(11);
        Service().DispatchDue(due, new Sink());
        using (var connection = Open(ReminderPath()))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE reminder_state SET record_json = replace(record_json, 'submitted', 'sending');";
            command.ExecuteNonQuery();
        }
        Assert.Empty(Service().DispatchDue(due, new Sink()));
        var recovered = Assert.Single(Service().DispatchDue(due, new Sink(), true));
        Assert.Equal("recovery", recovered.Kind);
        Assert.Empty(Service().DispatchDue(due, new Sink(), true));
    }

    [Fact]
    public void DirectTaskMutationDuringSubmission_RemovesNewlySubmittedNotification()
    {
        var task = Create("one", new(2026, 10, 13), new(18, 0));
        var sink = new Sink { OnSubmit = delivery =>
        {
            Tasks().Cancel(task.Id, "cancel-during-submit", task.Version, Now.AddHours(11));
            return new("submitted", delivery.Id);
        }};
        var sent = Assert.Single(Service().DispatchDue(Now.AddHours(11), sink));
        Assert.Equal("removed", sent.State);
        Assert.Contains(sent.NotificationId!, sink.Removed);
        Assert.Equal("removed", Assert.Single(Service().Deliveries()).State);
    }

    [Fact]
    public void CleanupFailure_PreservesNotificationAndRetriesOnNextReconcile()
    {
        var task = Create("one", new(2026, 10, 13), new(18, 0));
        var service = Service();
        var sent = Assert.Single(service.DispatchDue(Now.AddHours(11), new Sink()));
        Tasks().Complete(task.Id, "complete", task.Version, Now.AddHours(11));
        service.ReconcileNotifications(new Sink { OnRemove = _ => throw new IOException("remove failed") }, Now.AddHours(11));
        var failedCleanup = Assert.Single(Service().Deliveries());
        Assert.Equal(sent.NotificationId, failedCleanup.NotificationId);
        Assert.Contains("remove failed", failedCleanup.Error);
        Assert.Equal("submitted", failedCleanup.State);
        var retry = new Sink();
        Service().ReconcileNotifications(retry, Now.AddHours(11));
        Assert.Contains(sent.NotificationId!, retry.Removed);
        Assert.Equal("removed", Assert.Single(Service().Deliveries()).State);
    }

    [Fact]
    public void ChangedRecoveryMember_CleansEntireOldSummaryAndDoesNotReplayOtherMembers()
    {
        var task = Create("one", new(2026, 10, 13), new(18, 0));
        Create("two", new(2026, 10, 13), new(18, 0));
        var service = Service();
        var sent = Assert.Single(service.DispatchDue(Now.AddHours(11), new Sink(), true));
        Tasks().AcceptTime(task.Id, "postpone", task.Version, task.Time with { Date = new(2026, 10, 20) }, Now.AddHours(11));
        var sink = new Sink();
        service.ReconcileNotifications(sink, Now.AddHours(11));
        Assert.Contains(sent.NotificationId!, sink.Removed);
        Assert.Empty(service.DispatchDue(Now.AddHours(11), sink, true));
        Assert.Equal("submitted", service.Synchronize(Now.AddHours(11)).Single(p => p.TaskId == "two").Nodes[0].State);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("owner")]
    [InlineData("corrupt")]
    public void InvalidReminderDatabase_IsRejectedWithoutReplacingFile(string defect)
    {
        Create("one", new(2026, 10, 14));
        Service().Synchronize(Now);
        if (defect == "corrupt") File.WriteAllText(ReminderPath(), "not sqlite");
        else
        {
            using var connection = Open(ReminderPath());
            using var command = connection.CreateCommand();
            command.CommandText = defect == "schema" ? "PRAGMA user_version=99;" : "UPDATE store_metadata SET account_id='999999';";
            command.ExecuteNonQuery();
        }
        var before = File.ReadAllBytes(ReminderPath());
        Assert.ThrowsAny<Exception>(() => Service());
        Assert.Equal(before, File.ReadAllBytes(ReminderPath()));
        Assert.Single(Tasks().ListTasks());
    }

    [Fact]
    public void ForeignTaskStore_IsRejectedAndAccountsKeepIndependentPlans()
    {
        Create("one", new(2026, 10, 14));
        Assert.Throws<ArgumentException>(() => new AccountReminderService(root, "999999", Tasks()));
        Assert.Single(Service().Synchronize(Now));
        Assert.Empty(new AccountReminderService(root, "999999", new(root, "999999")).Synchronize(Now));
    }

    private string ReminderPath() => Path.Combine(root, "accounts", Account, "reminders.sqlite3");
    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private sealed class Sink : IReminderSink
    {
        public Func<ReminderDelivery, ReminderSubmission>? OnSubmit { get; init; }
        public Action<string>? OnRemove { get; init; }
        public List<string> Removed { get; } = [];
        public ReminderSubmission Submit(ReminderDelivery delivery) => OnSubmit?.Invoke(delivery) ?? new("submitted", delivery.Id);
        public void Remove(string id) { OnRemove?.Invoke(id); Removed.Add(id); }
    }

    private TaskItem Create(string id, DateOnly date, TimeOnly? time = null, TimeKind kind = TimeKind.Deadline)
        => Tasks().CreateTask(id, new RuleCandidate("任务 " + id, RuleAction.Submit, RuleDisposition.Task, "课程",
            new(kind, date, time, "Asia/Shanghai"), null, null, 1,
            new(new("行动", true), new("对象", true), new("课程", true), new("时间", true), new("来源", true), new("关联", false)),
            [], [new(Account, "10001", id)]), "create-" + id, Now);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
