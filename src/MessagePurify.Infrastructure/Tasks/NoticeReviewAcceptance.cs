using MessagePurify.Core;
using Microsoft.Data.Sqlite;

namespace MessagePurify.Infrastructure.Tasks;

public sealed partial class AccountTaskStore
{
    internal PendingReview AcceptNoticeInTransaction(SqliteConnection connection, SqliteTransaction transaction,
        string reviewId, string operationId, long expectedVersion, DateTimeOffset now,
        Action<PendingReview> saveNotice)
    {
        ValidateId(reviewId);
        return WriteInTransaction<PendingReview>(connection, transaction, operationId,
            new { Kind = "accept-notice", ReviewId = reviewId, ExpectedReviewVersion = expectedVersion },
            "review", (connection, transaction) =>
            {
                var review = Required<PendingReview>(connection, transaction, "reviews", reviewId);
                if (review.AccountId != AccountId) throw new InvalidDataException("待确认事项账号不匹配。");
                CheckVersion(review.Version, expectedVersion);
                if (review.State != ReviewState.Pending)
                    throw new InvalidOperationException("只有待确认状态的通知可以接受。");
                ValidateCandidate(review.Candidate);
                if (!MessageRuleExtractor.IsNoticeCandidate(review.Candidate))
                    throw new ArgumentException("只有普通通知可以从通知入口接受。", nameof(reviewId));
                saveNotice(review);
                var accepted = review with
                {
                    State = ReviewState.Accepted, AcceptedTaskId = null, UndoExpiresAt = null,
                    UndoOperationId = null, Version = review.Version + 1
                };
                Update(connection, transaction, "reviews", reviewId, accepted);
                return new(accepted, reviewId, review);
            }, now);
    }
}
