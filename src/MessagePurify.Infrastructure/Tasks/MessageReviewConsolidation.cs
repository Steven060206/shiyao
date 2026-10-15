using MessagePurify.Core;

namespace MessagePurify.Infrastructure.Tasks;

public sealed partial class AccountTaskStore
{
    internal bool ConsolidateMessageReviews(SchoolMessage message, IReadOnlyList<PendingReview> reviews,
        DateTimeOffset now)
    {
        if (reviews.Count == 0) return false;
        var combined = Normalize(MessageReviewCandidate.Combine(message, reviews.Select(review => review.Candidate).ToArray()));
        if (reviews.Count == 1 && combined.Time is not null && combined.TargetTaskId is null &&
            combined.Action is RuleAction.Submit or RuleAction.Complete or RuleAction.Attend or RuleAction.Register)
        {
            var rechecked = MessageRuleExtractor.Extract(message, new(AccountId, message.Key.GroupId,
                combined.Time.TimeZoneId, now, combined.Course)).Candidates[0];
            if (rechecked.Time is null && !rechecked.Assessments.Time.Reliable)
                combined = combined with { Time = null, SuggestedTime = null, Priority = null,
                    Assessments = combined.Assessments with { Time = rechecked.Assessments.Time },
                    ReviewReasons = combined.ReviewReasons.Concat(rechecked.ReviewReasons).Distinct().ToArray() };
        }
        ValidateCandidate(combined);
        return InTransaction((connection, transaction) =>
        {
            var current = reviews.Select(review => Required<PendingReview>(connection, transaction, "reviews", review.Id)).ToArray();
            if (current.Where((review, index) => review.Version != reviews[index].Version ||
                review.State != ReviewState.Pending || review.SplitParentTaskId is not null).Any()) return false;
            var keep = current[0];
            if (current.Length == 1 && Canonical(keep.Candidate) == Canonical(combined)) return false;
            WriteInTransaction<PendingReview>(connection, transaction, $"consolidate-message-{keep.Id}-v{keep.Version}",
                new { Kind = "consolidate-message-reviews", Message = message.Key,
                    Reviews = current.Select(review => new { review.Id, review.Version }).ToArray() },
                "review", (connection, transaction) =>
                {
                    var merged = keep with { Candidate = combined, Version = keep.Version + 1 };
                    var children = current.Skip(1).Select(review => review with
                    {
                        State = ReviewState.Consolidated, ConsolidatedIntoId = keep.Id,
                        Version = review.Version + 1, UndoOperationId = null, UndoExpiresAt = null
                    }).ToArray();
                    Update(connection, transaction, "reviews", keep.Id, merged);
                    foreach (var child in children) Update(connection, transaction, "reviews", child.Id, child);
                    return new(merged, keep.Id, new { Review = keep, ConsolidatedReviews = current.Skip(1).ToArray() },
                        new { Review = merged, ConsolidatedReviews = children });
                }, now);
            return true;
        });
    }
}
