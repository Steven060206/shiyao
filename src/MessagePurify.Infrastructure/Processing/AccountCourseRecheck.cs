using System.Globalization;
using System.Text.Json;
using MessagePurify.Core;
using MessagePurify.Infrastructure.Storage;
using MessagePurify.Infrastructure.Tasks;

namespace MessagePurify.Infrastructure.Processing;

public sealed record CourseRecheckResult(string AccountId, string GroupId, string? Course, bool Excluded,
    DateTimeOffset CompletedAt, int CheckedMessages, int CheckedTasks, int AddedTasks, int AddedNotices,
    int AddedReviews, int PreservedMessages, DateTimeOffset? FirstMessageAt, DateTimeOffset? LastMessageAt,
    int MissingEvidenceMessages, bool SkippedUnpinned);

public sealed partial class AccountCandidateRouter
{
    public CourseRecheckResult? ReadCourseRecheckResult()
    {
        using var db = Open(RoutingDatabasePath);
        CheckStore(db, null, "main", messages.AccountId);
        using (var table = Command(db, null, "SELECT 1 FROM sqlite_master WHERE type='table' AND name='course_recheck_result';"))
            if (table.ExecuteScalar() is null) return null; // 兼容在本工作区直接恢复旧备份。
        using var command = Command(db, null, "SELECT record_json FROM course_recheck_result WHERE id=1;");
        if (command.ExecuteScalar() is not string json) return null;
        var result = JsonSerializer.Deserialize<CourseRecheckResult>(json) ?? throw new InvalidDataException("课程重查结果为空。");
        if (result.AccountId != messages.AccountId) throw new InvalidDataException("课程重查结果账号不匹配。");
        return result;
    }

    /// <summary>只恢复尚无业务记录的本地消息；已有任务和所有人工审核决定保留。</summary>
    public CourseRecheckResult RecheckCourse(RuleContext context, IReadOnlySet<string> pinnedGroupIds)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(pinnedGroupIds);
        if (context.AccountId != messages.AccountId) throw new ArgumentException("重查账号不匹配。", nameof(context));
        ArgumentException.ThrowIfNullOrWhiteSpace(context.GroupId);
        using var db = OpenAttached();
        using var tx = db.BeginTransaction();
        foreach (var store in new[] { "main", "inbox", "routing" }) CheckStore(db, tx, store, messages.AccountId);
        using (var table = Command(db, tx, "CREATE TABLE IF NOT EXISTS routing.course_recheck_result(id INTEGER PRIMARY KEY CHECK(id=1),record_json TEXT NOT NULL);"))
            table.ExecuteNonQuery();
        var retained = new List<StoredMessage>();
        using (var command = Command(db, tx, """
            SELECT message_json,origin,batch_id,received_at,processing_version,extraction_json,processed_at
            FROM inbox.messages WHERE account_id=$account AND group_id=$group ORDER BY sent_utc_ticks,rowid;
            """, ("$account", context.AccountId), ("$group", context.GroupId)))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
                retained.Add(new(JsonSerializer.Deserialize<SchoolMessage>(reader.GetString(0))
                    ?? throw new InvalidDataException("重查消息为空。"), (MessageOrigin)reader.GetInt32(1), reader.GetString(2),
                    ParseTime(reader.GetString(3)), reader.GetInt64(4), reader.IsDBNull(5) ? null :
                    JsonSerializer.Deserialize<RuleExtractionResult>(reader.GetString(5)),
                    reader.IsDBNull(6) ? null : ParseTime(reader.GetString(6))));

        var taskList = new List<TaskItem>();
        var courseConflicts = new List<PendingReview>();
        var protectedKeys = new HashSet<MessageKey>();
        using (var command = Command(db, tx, "SELECT record_json FROM main.tasks ORDER BY rowid;"))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var task = JsonSerializer.Deserialize<TaskItem>(reader.GetString(0)) ?? throw new InvalidDataException("任务为空。");
                if (task.AccountId != context.AccountId) throw new InvalidDataException("任务账号不匹配。");
                taskList.Add(task);
                protectedKeys.UnionWith(task.Evidence);
            }
        using (var command = Command(db, tx, "SELECT record_json FROM main.reviews;"))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var review = JsonSerializer.Deserialize<PendingReview>(reader.GetString(0)) ?? throw new InvalidDataException("审核为空。");
                if (review.AccountId != context.AccountId) throw new InvalidDataException("审核账号不匹配。");
                if (review.IsCourseConflict && review.State == ReviewState.Pending &&
                    review.Candidate.Evidence.Any(key => key.GroupId == context.GroupId)) courseConflicts.Add(review);
                protectedKeys.UnionWith(review.Candidate.Evidence);
            }
        using (var command = Command(db, tx, "SELECT notice_json FROM routing.notices;"))
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var notice = JsonSerializer.Deserialize<StoredNotice>(reader.GetString(0)) ?? throw new InvalidDataException("通知为空。");
                if (notice.AccountId != context.AccountId) throw new InvalidDataException("通知账号不匹配。");
                protectedKeys.Add(notice.Source);
                protectedKeys.UnionWith(notice.Candidate.Evidence);
            }

        var pinned = pinnedGroupIds.Contains(context.GroupId);
        var addedTasks = 0;
        var addedNotices = 0;
        var addedReviews = 0;
        var preserved = 0;
        foreach (var stored in retained)
        {
            var message = stored.Message;
            if (message.Key.AccountId != context.AccountId || message.Key.GroupId != context.GroupId || message.IsPrivate)
                throw new InvalidDataException("重查消息超出账号或群范围。");
            if (protectedKeys.Contains(message.Key)) { preserved++; continue; }
            if (!pinned || context.CourseExcluded || context.ExcludedSenders?.Contains(message.SenderId) == true) continue;
            var extraction = MessageRuleExtractor.Extract(message, CurrentContext(db, tx, context));
            var candidate = extraction.Candidates.Single();
            if (candidate.Disposition == RuleDisposition.Ignored) continue;
            ValidateEvidence(candidate, message.Key);
            var identity = StableId(message.Key, 0);
            var version = SaveExtraction(db, tx, stored, extraction, context.Now);
            if (candidate.Disposition == RuleDisposition.Task)
            {
                tasks.CreateTaskInTransaction(db, tx, "task-" + identity, candidate, "course-recheck-task-" + identity, context.Now);
                addedTasks++;
            }
            else if (candidate.Disposition == RuleDisposition.Notice)
            {
                var id = "notice-" + identity;
                var notice = new StoredNotice(id, context.AccountId, candidate, message.Key, version,
                    stored.Origin, stored.BatchId, message.SentAt, context.Now);
                using var command = Command(db, tx, "INSERT INTO routing.notices(id,notice_json) VALUES($id,$json);",
                    ("$id", id), ("$json", JsonSerializer.Serialize(notice)));
                command.ExecuteNonQuery();
                addedNotices++;
            }
            else
            {
                // 重查绝不重放取消、延期或恢复；有目标的变更仍由用户确认。
                candidate = candidate with { Disposition = RuleDisposition.Pending };
                tasks.SavePendingInTransaction(db, tx, ReviewId(message.Key, 0), candidate, "course-recheck-review-" + identity, context.Now);
                addedReviews++;
            }
            using (var receipt = Command(db, tx, """
                INSERT INTO routing.route_receipts(account_id,group_id,message_id,processing_version,rule_version,routed_at)
                VALUES($account,$group,$message,$version,$rules,$at)
                ON CONFLICT(account_id,group_id,message_id) DO UPDATE SET
                    processing_version=excluded.processing_version,rule_version=excluded.rule_version,routed_at=excluded.routed_at;
                """, ("$account", context.AccountId), ("$group", context.GroupId), ("$message", message.Key.MessageId),
                ("$version", version), ("$rules", extraction.RuleVersion), ("$at", context.Now.ToString("O", CultureInfo.InvariantCulture))))
                receipt.ExecuteNonQuery();
        }

        var related = taskList.Where(task => task.MergedIntoId is null &&
            task.Evidence.Any(key => key.GroupId == context.GroupId)).ToArray();
        var retainedKeys = retained.Select(stored => stored.Message.Key).ToHashSet();
        var currentConflictIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var task in related.Where(task => context.CourseExcluded || task.Course != context.ConfirmedCourse))
        {
            var evidence = task.Evidence.Where(retainedKeys.Contains).ToArray();
            if (evidence.Length == 0) continue;
            var reason = context.CourseExcluded ? "此群已排除；原任务及其状态保留，请核对是否仍适用。" :
                $"群课程关联已改为“{context.ConfirmedCourse ?? "未确认"}”；原任务课程“{task.Course ?? "未填写"}”保留，请打开原任务核对。";
            var assessment = new RuleAssessment(reason, false);
            var candidate = new RuleCandidate(task.Title, RuleAction.Remind, RuleDisposition.Pending,
                context.ConfirmedCourse, null, null, task.Id, null,
                new(assessment, assessment, assessment, assessment, assessment, assessment), [reason], evidence);
            var key = new MessageKey(context.AccountId, context.GroupId,
                JsonSerializer.Serialize(new { task.Id, Course = context.ConfirmedCourse, context.CourseExcluded }));
            var id = "course-review-" + StableId(key, 0);
            currentConflictIds.Add(id);
            if (tasks.SaveCourseConflictInTransaction(db, tx, id, candidate, context.Now)) addedReviews++;
        }
        foreach (var stale in courseConflicts.Where(review => !currentConflictIds.Contains(review.Id)))
            tasks.RetireCourseConflictInTransaction(db, tx, stale, context.Now);
        var result = new CourseRecheckResult(context.AccountId, context.GroupId, context.ConfirmedCourse,
            context.CourseExcluded, context.Now, retained.Count, related.Length, addedTasks, addedNotices, addedReviews,
            preserved, retained.Count == 0 ? null : retained.Min(item => item.Message.SentAt),
            retained.Count == 0 ? null : retained.Max(item => item.Message.SentAt),
            protectedKeys.Count(key => key.GroupId == context.GroupId && !retainedKeys.Contains(key)), !pinned);
        using (var save = Command(db, tx, "INSERT OR REPLACE INTO routing.course_recheck_result(id,record_json) VALUES(1,$json);",
            ("$json", JsonSerializer.Serialize(result)))) save.ExecuteNonQuery();
        tx.Commit();
        return result;
    }
}
