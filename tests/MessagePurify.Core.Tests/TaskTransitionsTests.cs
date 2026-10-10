using MessagePurify.Core;
using Xunit;

namespace MessagePurify.Core.Tests;

public sealed class TaskTransitionsTests
{
    private static readonly DateTimeOffset ProcessedAt =
        DateTimeOffset.Parse("2026-10-10T10:00:00+08:00");

    private static TaskSnapshot Task(TaskState state = TaskState.Active,
        bool timeProtected = false) => new("t1", state,
            new TaskTime(TimeKind.Deadline, new DateOnly(2026, 10, 12),
                new TimeOnly(18, 0), "China Standard Time"),
            timeProtected, 3, 2);

    [Fact]
    public void ProposedTime_DoesNotOverrideProtectedTime()
    {
        var task = Task(timeProtected: true);
        var later = task.Time with { Date = new DateOnly(2026, 10, 14) };

        var result = TaskTransitions.ProposeTime(task, later);

        Assert.Equal(task, result.Current);
        Assert.Equal(3, result.Current.Version);
        Assert.Equal(2, result.Current.TimeVersion);
        Assert.Equal(later, result.SuggestedTime);
        Assert.False(result.Changed);
    }

    [Fact]
    public void ProposedTime_UpdatesUnprotectedTimeAndBothVersions()
    {
        var task = Task();
        var later = task.Time with { Date = new DateOnly(2026, 10, 14) };

        var result = TaskTransitions.ProposeTime(task, later);

        Assert.Equal(later, result.Current.Time);
        Assert.Equal(TaskState.Active, result.Current.State);
        Assert.False(result.Current.TimeProtected);
        Assert.Equal(4, result.Current.Version);
        Assert.Equal(3, result.Current.TimeVersion);
        Assert.Null(result.SuggestedTime);
        Assert.True(result.Changed);
        Assert.Equal(new DateOnly(2026, 10, 12), task.Time.Date);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProposedSameTime_IsIdempotent(bool timeProtected)
    {
        var task = Task(timeProtected: timeProtected);

        var result = TaskTransitions.ProposeTime(task, task.Time with { });

        Assert.Equal(task, result.Current);
        Assert.Null(result.SuggestedTime);
        Assert.False(result.Changed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcceptTime_ChangesFormalTimeAndProtectsIt(bool timeProtected)
    {
        var task = Task(timeProtected: timeProtected);
        var later = task.Time with { Date = new DateOnly(2026, 10, 14), Time = null };

        var current = TaskTransitions.AcceptTime(task, later);

        Assert.Equal(later, current.Time);
        Assert.Equal(task.Id, current.Id);
        Assert.Equal(task.State, current.State);
        Assert.True(current.TimeProtected);
        Assert.Equal(4, current.Version);
        Assert.Equal(3, current.TimeVersion);
    }

    [Fact]
    public void AcceptSameUnprotectedTime_OnlyChangesProtectionAndTaskVersion()
    {
        var task = Task();

        var current = TaskTransitions.AcceptTime(task, task.Time);

        Assert.Equal(task.Time, current.Time);
        Assert.True(current.TimeProtected);
        Assert.Equal(4, current.Version);
        Assert.Equal(2, current.TimeVersion);
    }

    [Fact]
    public void AcceptSameProtectedTime_IsIdempotent()
    {
        var task = Task(timeProtected: true);

        Assert.Equal(task, TaskTransitions.AcceptTime(task, task.Time with { }));
    }

    [Fact]
    public void Complete_OnlyChangesActiveStateAndTaskVersion()
    {
        var task = Task(timeProtected: true);

        var current = TaskTransitions.Complete(task);

        Assert.Equal(task with { State = TaskState.Completed, Version = 4 }, current);
        Assert.Equal(TaskState.Active, task.State);
    }

    [Fact]
    public void RepeatingCompletion_IsIdempotent()
    {
        var completed = TaskTransitions.Complete(Task());

        Assert.Equal(completed, TaskTransitions.Complete(completed));
    }

    [Theory]
    [InlineData(TaskState.Active)]
    [InlineData(TaskState.Completed)]
    [InlineData(TaskState.Archived)]
    public void Cancel_TakesPrecedenceAndPreservesProtectedTime(TaskState state)
    {
        var task = Task(state, timeProtected: true);

        var current = TaskTransitions.Cancel(task);

        Assert.Equal(task with { State = TaskState.Cancelled, Version = 4 }, current);
    }

    [Fact]
    public void RepeatingCancellation_IsIdempotent()
    {
        var cancelled = TaskTransitions.Cancel(Task());

        Assert.Equal(cancelled, TaskTransitions.Cancel(cancelled));
    }

    [Fact]
    public void OldLifecycleOperations_CannotOverrideCancellation()
    {
        var cancelled = TaskTransitions.Cancel(Task(timeProtected: true));

        Assert.Equal(cancelled, TaskTransitions.Complete(cancelled));
        Assert.Equal(cancelled, TaskTransitions.Archive(cancelled));
        Assert.Equal(cancelled, TaskTransitions.RestoreArchived(cancelled));
        Assert.Equal(cancelled, TaskTransitions.RestoreForNewAction(cancelled));
    }

    [Theory]
    [InlineData(TaskState.Active)]
    [InlineData(TaskState.Completed)]
    public void Archive_ChangesStateAndTaskVersion(TaskState state)
    {
        var task = Task(state, timeProtected: true);

        Assert.Equal(task with { State = TaskState.Archived, Version = 4 },
            TaskTransitions.Archive(task));
    }

    [Fact]
    public void RepeatingArchive_IsIdempotent()
    {
        var archived = TaskTransitions.Archive(Task());

        Assert.Equal(archived, TaskTransitions.Archive(archived));
    }

    [Fact]
    public void RestoreArchived_RestoresOnlyStateAndTaskVersion()
    {
        var task = Task(TaskState.Archived, timeProtected: true);

        Assert.Equal(task with { State = TaskState.Active, Version = 4 },
            TaskTransitions.RestoreArchived(task));
    }

    [Theory]
    [InlineData(TaskState.Active)]
    [InlineData(TaskState.Completed)]
    [InlineData(TaskState.Cancelled)]
    public void RestoreArchived_DoesNothingToOtherStates(TaskState state)
    {
        var task = Task(state);

        Assert.Equal(task, TaskTransitions.RestoreArchived(task));
    }

    [Fact]
    public void RestoringArchiveAgain_IsIdempotent()
    {
        var restored = TaskTransitions.RestoreArchived(Task(TaskState.Archived));

        Assert.Equal(restored, TaskTransitions.RestoreArchived(restored));
    }

    [Fact]
    public void CompletedTask_WithReliableNewActionRestoresActive()
    {
        var task = Task(TaskState.Completed, timeProtected: true);

        Assert.Equal(task with { State = TaskState.Active, Version = 4 },
            TaskTransitions.RestoreForNewAction(task));
    }

    [Theory]
    [InlineData(TaskState.Active)]
    [InlineData(TaskState.Archived)]
    [InlineData(TaskState.Cancelled)]
    public void NewAction_DoesNotRestoreOtherStates(TaskState state)
    {
        var task = Task(state);

        Assert.Equal(task, TaskTransitions.RestoreForNewAction(task));
    }

    [Fact]
    public void OldComplete_CannotRemoveAnArchivedTask()
    {
        var task = Task(TaskState.Archived);

        Assert.Equal(task, TaskTransitions.Complete(task));
    }

    [Theory]
    [InlineData(UndoKind.Complete, 1)]
    [InlineData(UndoKind.Ignore, 24)]
    public void CreateUndo_UsesProcessingTimeAndPreservesSequence(UndoKind kind,
        int hours)
    {
        var ticket = TaskTransitions.CreateUndo(kind, ProcessedAt, 17);

        Assert.Equal(kind, ticket.Kind);
        Assert.Equal(ProcessedAt.AddHours(hours), ticket.ExpiresAt);
        Assert.Equal(17, ticket.OperationSequence);
    }

    [Theory]
    [InlineData(UndoKind.Complete)]
    [InlineData(UndoKind.Ignore)]
    public void UndoEligibility_ExpiresAtExactBoundaryAndRejectsSuperseded(
        UndoKind kind)
    {
        var ticket = TaskTransitions.CreateUndo(kind, ProcessedAt, 17);

        Assert.True(TaskTransitions.CanUndo(ticket, ticket.ExpiresAt.AddTicks(-1), false));
        Assert.False(TaskTransitions.CanUndo(ticket, ticket.ExpiresAt, false));
        Assert.False(TaskTransitions.CanUndo(ticket, ticket.ExpiresAt.AddTicks(1), false));
        Assert.False(TaskTransitions.CanUndo(ticket, ProcessedAt, true));
    }

    [Fact]
    public void UndoCompletion_RestoresStateWithoutChangingTimeOrProtection()
    {
        var task = Task(TaskState.Completed, timeProtected: true);
        var ticket = TaskTransitions.CreateUndo(UndoKind.Complete, ProcessedAt, 17);

        var result = TaskTransitions.UndoComplete(task, ticket,
            ticket.ExpiresAt.AddTicks(-1), false);

        Assert.Equal(task with { State = TaskState.Active, Version = 4 }, result.Current);
        Assert.Null(result.SuggestedTime);
        Assert.True(result.Changed);
    }

    [Fact]
    public void UndoCompletion_AfterAutomaticPostponementKeepsLatestTime()
    {
        var completed = TaskTransitions.Complete(Task());
        var ticket = TaskTransitions.CreateUndo(UndoKind.Complete, ProcessedAt, 17);
        var later = completed.Time with { Date = new DateOnly(2026, 10, 14) };
        var postponed = TaskTransitions.ProposeTime(completed, later).Current;

        var result = TaskTransitions.UndoComplete(postponed, ticket,
            ProcessedAt.AddMinutes(20), false);

        Assert.Equal(TaskState.Active, result.Current.State);
        Assert.Equal(later, result.Current.Time);
        Assert.Equal(6, result.Current.Version);
        Assert.Equal(3, result.Current.TimeVersion);
        Assert.False(result.Current.TimeProtected);
        Assert.True(result.Changed);
    }

    [Fact]
    public void UndoCompletion_AfterAcceptedPostponementKeepsLatestProtectedTime()
    {
        var completed = TaskTransitions.Complete(Task(timeProtected: true));
        var ticket = TaskTransitions.CreateUndo(UndoKind.Complete, ProcessedAt, 17);
        var later = completed.Time with { Date = new DateOnly(2026, 10, 14), Time = null };
        var postponed = TaskTransitions.AcceptTime(completed, later);

        var result = TaskTransitions.UndoComplete(postponed, ticket,
            ProcessedAt.AddMinutes(20), false);

        Assert.Equal(postponed with { State = TaskState.Active, Version = 6 }, result.Current);
        Assert.True(result.Current.TimeProtected);
        Assert.True(result.Changed);
    }

    [Fact]
    public void UndoCompletion_AfterCancellationDoesNothing()
    {
        var completed = TaskTransitions.Complete(Task());
        var ticket = TaskTransitions.CreateUndo(UndoKind.Complete, ProcessedAt, 17);
        var cancelled = TaskTransitions.Cancel(completed);

        var result = TaskTransitions.UndoComplete(cancelled, ticket, ProcessedAt, false);

        Assert.Equal(cancelled, result.Current);
        Assert.Null(result.SuggestedTime);
        Assert.False(result.Changed);
    }

    [Fact]
    public void UndoCompletion_AfterNewActionDoesNothing()
    {
        var completed = TaskTransitions.Complete(Task());
        var ticket = TaskTransitions.CreateUndo(UndoKind.Complete, ProcessedAt, 17);
        var restored = TaskTransitions.RestoreForNewAction(completed);

        var result = TaskTransitions.UndoComplete(restored, ticket, ProcessedAt, false);

        Assert.Equal(restored, result.Current);
        Assert.False(result.Changed);
    }

    [Fact]
    public void OldUndo_AfterNewActionAndLaterCompletionIsSuperseded()
    {
        var firstCompletion = TaskTransitions.Complete(Task());
        var oldTicket = TaskTransitions.CreateUndo(UndoKind.Complete, ProcessedAt, 17);
        var restored = TaskTransitions.RestoreForNewAction(firstCompletion);
        var current = TaskTransitions.Complete(restored);

        var result = TaskTransitions.UndoComplete(current, oldTicket,
            ProcessedAt.AddMinutes(20), true);

        Assert.Equal(current, result.Current);
        Assert.False(result.Changed);
    }

    [Theory]
    [InlineData(TaskState.Active)]
    [InlineData(TaskState.Archived)]
    [InlineData(TaskState.Cancelled)]
    public void UndoCompletion_OnlyActsOnCompletedState(TaskState state)
    {
        var task = Task(state);
        var ticket = TaskTransitions.CreateUndo(UndoKind.Complete, ProcessedAt, 17);

        var result = TaskTransitions.UndoComplete(task, ticket, ProcessedAt, false);

        Assert.Equal(task, result.Current);
        Assert.Null(result.SuggestedTime);
        Assert.False(result.Changed);
    }

    [Fact]
    public void IgnoreTicket_CannotUndoCompletion()
    {
        var task = Task(TaskState.Completed);
        var ticket = TaskTransitions.CreateUndo(UndoKind.Ignore, ProcessedAt, 17);

        var result = TaskTransitions.UndoComplete(task, ticket, ProcessedAt, false);

        Assert.Equal(task, result.Current);
        Assert.False(result.Changed);
    }

    [Fact]
    public void ExpiredCompletionUndo_DoesNothingAtExpiry()
    {
        var task = Task(TaskState.Completed);
        var ticket = TaskTransitions.CreateUndo(UndoKind.Complete, ProcessedAt, 17);

        var result = TaskTransitions.UndoComplete(task, ticket, ticket.ExpiresAt, false);

        Assert.Equal(task, result.Current);
        Assert.False(result.Changed);
    }

    [Fact]
    public void RepeatingCompletionUndo_IsIdempotent()
    {
        var task = Task(TaskState.Completed);
        var ticket = TaskTransitions.CreateUndo(UndoKind.Complete, ProcessedAt, 17);
        var restored = TaskTransitions.UndoComplete(task, ticket, ProcessedAt, false).Current;

        var repeated = TaskTransitions.UndoComplete(restored, ticket, ProcessedAt, false);

        Assert.Equal(restored, repeated.Current);
        Assert.False(repeated.Changed);
    }
}
