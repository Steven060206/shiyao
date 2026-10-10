namespace MessagePurify.Core;

public enum TaskState { Active, Completed, Cancelled, Archived }
public enum UndoKind { Complete, Ignore }

public sealed record TaskSnapshot(string Id, TaskState State, TaskTime Time,
    bool TimeProtected, long Version, long TimeVersion);

public sealed record UndoTicket(UndoKind Kind, DateTimeOffset ExpiresAt,
    long OperationSequence);

public sealed record TransitionResult(TaskSnapshot Current,
    TaskTime? SuggestedTime, bool Changed);

public static class TaskTransitions
{
    public static TaskSnapshot Complete(TaskSnapshot task) =>
        task.State == TaskState.Active ? ChangeState(task, TaskState.Completed) : task;

    public static TaskSnapshot Cancel(TaskSnapshot task) =>
        ChangeState(task, TaskState.Cancelled);

    public static TaskSnapshot Archive(TaskSnapshot task) =>
        task.State == TaskState.Cancelled ? task : ChangeState(task, TaskState.Archived);

    public static TaskSnapshot RestoreArchived(TaskSnapshot task) =>
        task.State == TaskState.Archived ? ChangeState(task, TaskState.Active) : task;

    public static TransitionResult ProposeTime(TaskSnapshot task, TaskTime proposed)
    {
        if (task.Time == proposed) return new(task, null, false);
        if (task.TimeProtected) return new(task, proposed, false);

        var updated = task with
        {
            Time = proposed,
            Version = task.Version + 1,
            TimeVersion = task.TimeVersion + 1
        };
        return new(updated, null, true);
    }

    public static TaskSnapshot AcceptTime(TaskSnapshot task, TaskTime proposed)
    {
        var timeChanged = task.Time != proposed;
        if (!timeChanged && task.TimeProtected) return task;

        return task with
        {
            Time = proposed,
            TimeProtected = true,
            Version = task.Version + 1,
            TimeVersion = timeChanged ? task.TimeVersion + 1 : task.TimeVersion
        };
    }

    // The caller validates the source, task association and required time first.
    public static TaskSnapshot RestoreForNewAction(TaskSnapshot task) =>
        task.State == TaskState.Completed ? ChangeState(task, TaskState.Active) : task;

    public static UndoTicket CreateUndo(UndoKind kind, DateTimeOffset processedAt,
        long operationSequence) => new(kind,
            processedAt.AddHours(kind == UndoKind.Complete ? 1 : 24), operationSequence);

    public static bool CanUndo(UndoTicket ticket, DateTimeOffset now, bool superseded) =>
        !superseded && now < ticket.ExpiresAt;

    public static TransitionResult UndoComplete(TaskSnapshot current, UndoTicket ticket,
        DateTimeOffset now, bool superseded)
    {
        if (ticket.Kind != UndoKind.Complete || current.State != TaskState.Completed ||
            !CanUndo(ticket, now, superseded)) return new(current, null, false);

        return new(ChangeState(current, TaskState.Active), null, true);
    }

    private static TaskSnapshot ChangeState(TaskSnapshot task, TaskState state) =>
        task.State == state ? task : task with { State = state, Version = task.Version + 1 };
}
