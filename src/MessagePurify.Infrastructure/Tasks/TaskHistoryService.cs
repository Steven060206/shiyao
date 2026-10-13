using System.Globalization;
using System.Text.Json;
using MessagePurify.Core;

namespace MessagePurify.Infrastructure.Tasks;

public sealed record TaskHistoryEntry(string OperationId, string Kind, DateTimeOffset At,
    TaskItem? Before, TaskItem After, RuleAction? Action, IReadOnlyList<MessageKey> Evidence);

public sealed partial class AccountTaskStore
{
    /// <summary>按持久化操作顺序读取当前账号的任务历史，不回写旧审计。</summary>
    public IReadOnlyList<TaskHistoryEntry> ListTaskHistory()
    {
        using var connection = Open();
        using var command = Command(connection, null, """
            SELECT operation_id, request_json, result_id, processed_at, before_json, after_json
            FROM operations WHERE result_kind = 'task' ORDER BY sequence;
            """);
        using var reader = command.ExecuteReader();
        var history = new List<TaskHistoryEntry>();
        var created = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            using var request = JsonDocument.Parse(reader.GetString(1));
            using var afterDocument = JsonDocument.Parse(reader.GetString(5));
            var after = TaskFromAudit(afterDocument.RootElement)
                ?? throw new InvalidDataException("任务操作缺少结果快照。");
            if (after.AccountId != AccountId || after.Id != reader.GetString(2))
                throw new InvalidDataException("任务操作快照归属不一致。");
            TaskItem? before = null;
            if (!reader.IsDBNull(4))
            {
                using var beforeDocument = JsonDocument.Parse(reader.GetString(4));
                before = TaskFromAudit(beforeDocument.RootElement);
            }
            // 重复创建或无状态变化的幂等请求仍保留原审计，但不重复展示。
            if (before is null && !created.Add(after.Id) || before?.Version == after.Version) continue;
            created.Add(after.Id);
            RuleAction? action = CandidateAction(request.RootElement);
            if (afterDocument.RootElement.TryGetProperty("Review", out var review))
                action = CandidateAction(review) ?? action;
            history.Add(new(reader.GetString(0), request.RootElement.GetProperty("Kind").GetString()!,
                DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture), before, after, action,
                after.Evidence.Except(before?.Evidence ?? []).ToArray()));
        }
        return history;
    }

    private static TaskItem? TaskFromAudit(JsonElement root)
    {
        if (root.TryGetProperty("Task", out var nested)) root = nested;
        return root.ValueKind == JsonValueKind.Null ? null : root.Deserialize<TaskItem>();
    }

    private static RuleAction? CandidateAction(JsonElement root)
    {
        if (root.TryGetProperty("Action", out var directAction) && directAction.ValueKind == JsonValueKind.String)
            return directAction.Deserialize<RuleAction>();
        if (!root.TryGetProperty("Candidate", out var candidate) || candidate.ValueKind != JsonValueKind.Object
            || !candidate.TryGetProperty("Action", out var action)) return null;
        return action.Deserialize<RuleAction>();
    }
}
