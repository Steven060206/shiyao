using System.Globalization;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Tasks;

namespace MessagePurify.Infrastructure.Application;

internal static class TaskChangeProjection
{
    internal sealed record Change(TaskHistoryEntry Entry, string Label,
        IReadOnlyList<ApplicationFieldChange> Fields, bool Important, string? Badge);

    internal static Change? Build(TaskHistoryEntry entry)
    {
        var before = entry.Before;
        var after = entry.After;
        var fields = new List<ApplicationFieldChange>();
        void Field(string name, string? oldValue, string? newValue)
        {
            if (oldValue != newValue) fields.Add(new(name, oldValue ?? "未设置", newValue ?? "未设置"));
        }
        Field("标题", before?.Title, after.Title);
        Field("课程", before is null ? null : before.Course ?? "未分类", after.Course ?? "未分类");
        Field("正式日期", before?.Time.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            after.Time.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Field("钟点", before is null ? null : Clock(before.Time), Clock(after.Time));
        Field("时间类型", before is null ? null : TimeKindLabel(before.Time.Kind), TimeKindLabel(after.Time.Kind));
        Field("优先级", before is null ? null : Priority(before), Priority(after));
        Field("状态", before is null ? null : State(before.State), State(after.State));
        if (fields.Count == 0 && entry.Evidence.Count == 0) return null;
        var newRequirement = before is not null && entry.Action == RuleAction.NewRequirement && entry.Evidence.Count > 0;
        var important = before is null || fields.Count > 0 || newRequirement;
        var label = entry.Kind switch
        {
            "create" => "建立任务",
            "create-task" => "手动添加任务",
            "convert-notice" => "从通知加入任务",
            "edit-task" => "修改任务",
            "accept-time" => "修改正式时间",
            "complete" => "完成任务",
            "undo-complete" => "撤销完成",
            "archive" => "归档任务",
            "restore" => "恢复待办",
            "accept-review" when before is null => "确认并加入任务",
            "accept-review" => entry.Action switch
            {
                RuleAction.Postpone => "确认延期", RuleAction.Cancel => "确认取消任务",
                RuleAction.NewRequirement => "确认新要求", RuleAction.Remind => "确认催交补充", _ => "确认任务变更"
            },
            "automatic-change" => entry.Action switch
            {
                RuleAction.Postpone => "任务延期", RuleAction.Cancel => "任务取消",
                RuleAction.NewRequirement => "新增要求", RuleAction.Remind => "催交补充", _ => "补充原消息"
            },
            _ => "任务更新"
        };
        string? badge = before is null || !important ? null : entry.Action switch
        {
            RuleAction.NewRequirement => "新要求", RuleAction.Postpone => "已延期", RuleAction.Cancel => "已取消",
            _ when entry.Kind == "edit-task" => "已修改",
            _ when before.Time != after.Time => "时间变更",
            _ when before.State != after.State => State(after.State),
            _ => "已修改"
        };
        return new(entry, label, fields, important, badge);
    }

    internal static string SuggestionLabel(RuleAction action) => action switch
    {
        RuleAction.Postpone => "待确认延期", RuleAction.Cancel => "待确认取消",
        RuleAction.NewRequirement => "待确认新要求", RuleAction.Remind => "待确认催交补充", _ => "待确认变更"
    };

    private static string Clock(TaskTime time) => time.Time?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "未填写";
    private static string Priority(TaskItem task) => task.PriorityOverride is int priority ? "P" + priority : "自动分级";
    private static string TimeKindLabel(TimeKind kind) => kind == TimeKind.Start ? "开始时间" : "截止时间";
    private static string State(TaskState state) => state switch
    {
        TaskState.Active => "待办", TaskState.Completed => "已完成", TaskState.Archived => "已归档",
        TaskState.Cancelled => "已取消", _ => state.ToString()
    };
}
