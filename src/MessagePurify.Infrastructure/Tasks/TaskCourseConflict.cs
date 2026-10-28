using MessagePurify.Core;
using Microsoft.Data.Sqlite;

namespace MessagePurify.Infrastructure.Tasks;

public sealed partial class AccountTaskStore
{
    internal bool SaveCourseConflictInTransaction(SqliteConnection connection, SqliteTransaction transaction,
        string id, RuleCandidate candidate, DateTimeOffset now)
    {
        ValidateCandidate(candidate);
        var previous = Read<PendingReview>(connection, transaction, "reviews", id);
        if (previous is not null)
        {
            if (!previous.IsCourseConflict || previous.State != ReviewState.Consolidated) return false;
            WriteInTransaction<PendingReview>(connection, transaction, $"refresh-course-conflict-{id}-v{previous.Version}",
                new { Kind = "refresh-course-conflict", Id = id, Version = previous.Version }, "review", (db, tx) =>
                {
                    var current = previous with { Candidate = Normalize(candidate), State = ReviewState.Pending,
                        Version = previous.Version + 1, ConsolidatedIntoId = null };
                    Update(db, tx, "reviews", id, current);
                    return new(current, id, previous);
                }, now);
            return true;
        }
        WriteInTransaction<PendingReview>(connection, transaction, "course-conflict-" + id,
            new { Kind = "course-conflict", Id = id, Candidate = Canonical(candidate) }, "review", (db, tx) =>
            {
                var review = new PendingReview(id, AccountId, Normalize(candidate), ReviewState.Pending,
                    now, null, null, 1, IsCourseConflict: true);
                Insert(db, tx, "reviews", id, review, "course-conflict:" + id);
                return new(review, id);
            }, now);
        return true;
    }

    internal void RetireCourseConflictInTransaction(SqliteConnection connection, SqliteTransaction transaction,
        PendingReview review, DateTimeOffset now)
    {
        if (!review.IsCourseConflict || review.State != ReviewState.Pending) return;
        WriteInTransaction<PendingReview>(connection, transaction, $"retire-course-conflict-{review.Id}-v{review.Version}",
            new { Kind = "retire-course-conflict", Id = review.Id, Version = review.Version }, "review", (db, tx) =>
            {
                var retired = review with { State = ReviewState.Consolidated, Version = review.Version + 1 };
                Update(db, tx, "reviews", review.Id, retired);
                return new(retired, review.Id, review);
            }, now);
    }
}
