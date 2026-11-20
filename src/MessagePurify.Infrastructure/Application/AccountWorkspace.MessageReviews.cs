using MessagePurify.Core;
using MessagePurify.Infrastructure.Processing;
using MessagePurify.Infrastructure.Storage;
using MessagePurify.Infrastructure.Tasks;

namespace MessagePurify.Infrastructure.Application;

public sealed partial class AccountWorkspace
{
    private void ConsolidateLegacyMessageReviews(DateTimeOffset now)
    {
        var sources = new Dictionary<MessageKey, StoredMessage?>();
        var groups = new Dictionary<MessageKey, List<(PendingReview Review, int Index)>>();
        foreach (var review in tasks.ListReviews(ReviewState.Pending).Where(review => review.SplitParentTaskId is null))
        {
            // Evidence is sorted for storage; a quoted message may appear before the actual source.
            foreach (var key in review.Candidate.Evidence)
            {
                if (!sources.TryGetValue(key, out var source)) sources[key] = source = messages.Find(key);
                if (source?.Extraction is null) continue;
                var index = Enumerable.Range(0, source.Extraction.Candidates.Count)
                    .FirstOrDefault(index => AccountCandidateRouter.ReviewId(key, index) == review.Id, -1);
                // OCR派生审核的原始提取仍可能是Ignored，不能用空白原文重建标题。
                if (index < 0 || source.Extraction.Candidates[index].Disposition == RuleDisposition.Ignored) continue;
                if (!groups.TryGetValue(key, out var group)) groups[key] = group = [];
                group.Add((review, index));
                break;
            }
        }
        foreach (var (key, group) in groups)
        {
            var message = sources[key]!.Message;
            if (!GroupRelay.IsTextResponseList(message))
            {
                tasks.ConsolidateMessageReviews(message, group.OrderBy(item => item.Index).Select(item => item.Review).ToArray(), now);
                continue;
            }
            // Version 1 is untouched. An explicit undo creates a newer pending version.
            foreach (var (review, _) in group.Where(item => item.Review.Version == 1))
            {
                try { tasks.IgnoreReview(review.Id, "ignore-group-relay-" + review.Id, review.Version, now); }
                catch (InvalidOperationException) when (tasks.FindReview(review.Id)?.Version != review.Version)
                { /* Another workspace has already processed this review. */ }
            }
        }
    }
}
