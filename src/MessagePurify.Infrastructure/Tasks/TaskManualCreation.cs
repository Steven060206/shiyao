using MessagePurify.Core;

namespace MessagePurify.Infrastructure.Tasks;

public sealed partial class AccountTaskStore
{
    public TaskItem CreateManualTask(string id, string title, string course, TaskTime time,
        int? priority, string operationId, DateTimeOffset now)
    {
        ValidateId(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(course);
        if (title.Length > 256 || title.Any(char.IsControl))
            throw new ArgumentException("标题须在 256 字以内且不含控制字符。", nameof(title));
        if (course.Length > 256 || course.Any(char.IsControl))
            throw new ArgumentException("课程须在 256 字以内且不含控制字符。", nameof(course));
        if (priority is < 0 or > 3)
            throw new ArgumentException("优先级须为 0 到 3，或留空自动计算。", nameof(priority));
        ValidateTime(time);
        if (time.TimeZoneId != "Asia/Shanghai")
            throw new ArgumentException("手动任务使用北京时间。", nameof(time));
        title = title.Trim();
        course = course.Trim();
        return Write<TaskItem>(operationId,
            new { Kind = "create-task", Id = id, Title = title, Course = course, Time = time, Priority = priority },
            "task", (connection, transaction) =>
            {
                if (Read<TaskItem>(connection, transaction, "tasks", id) is not null)
                    throw new InvalidOperationException("任务 ID 已被其他事项使用。");
                var task = new TaskItem(id, AccountId, title, course, RuleAction.Complete,
                    time, TaskState.Active, true,
                    priority ?? PriorityCalculator.Calculate(time, now, PriorityCalculator.HasClearConsequence(title)),
                    1, 1, [], now, null, null, null, PriorityOverride: priority, Source: "manual");
                Insert(connection, transaction, "tasks", id, task, "manual:" + AccountId + ":" + id);
                return new(task, task.Id);
            }, now);
    }
}
