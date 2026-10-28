using MessagePurify.Core;
using MessagePurify.Infrastructure.Images;
using MessagePurify.Infrastructure.NapCat;
using MessagePurify.Infrastructure.Tasks;

namespace MessagePurify.Infrastructure.Application;

public sealed partial class AccountWorkspace
{
    private readonly SemaphoreSlim imageWork = new(1, 1);
    private readonly HashSet<string> imageAnalysis = new(StringComparer.Ordinal);

    private void RegisterRetainedImages(DateTimeOffset now)
    {
        foreach (var source in messages.ListRetained())
            if (source.Message.Segments.Any(segment => segment.Kind == SegmentKind.Image)) images.Register(source.Message, now);
    }

    private bool ImageAllowed(ImageEvidenceRecord image) => image.Message.AccountId == AccountId &&
        pinnedGroups.Contains(image.Message.GroupId) && !Context(image.Message.GroupId, DateTimeOffset.UtcNow).CourseExcluded &&
        messages.Find(image.Message) is not null;

    public async Task<int> DownloadImagesAsync(Func<string, CancellationToken, Task<OneBotImage>> download,
        DateTimeOffset now, Func<bool>? canContinue = null, CancellationToken cancellationToken = default)
    {
        await imageWork.WaitAsync(cancellationToken);
        try
        {
            ImageEvidenceRecord[] pending; long generation;
            lock (gate)
            {
                RegisterRetainedImages(now);
                pending = images.List().Where(image => image.Attempts < 3 && ImageAllowed(image) &&
                    (image.Status is "pending" or "download-failed" || images.GetOriginalPath(image) is null)).ToArray();
                generation = Generation;
            }
            var count = 0;
            foreach (var expected in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (canContinue?.Invoke() == false) break;
                try
                {
                    if (string.IsNullOrWhiteSpace(expected.AssetId)) throw new InvalidDataException("image-reference-unavailable");
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(20));
                    var original = await download(expected.AssetId, timeout.Token);
                    lock (gate)
                    {
                        if (Generation != generation || canContinue?.Invoke() == false || !ImageAllowed(expected) || !ImageUnchanged(expected)) continue;
                        images.SaveOriginal(expected.Id, original.FilePath, now);
                        imageAnalysis.Clear();
                        count++;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception)
                {
                    lock (gate)
                        if (Generation == generation && ImageAllowed(expected) && ImageUnchanged(expected))
                            images.SetFailure(expected.Id, "download", "image-download-failed", now);
                }
            }
            return count;
        }
        finally { imageWork.Release(); }
    }

    public async Task<int> RecognizeImagesAsync(IImageTextRecognizer recognizer, DateTimeOffset now,
        Func<bool>? canContinue = null, CancellationToken cancellationToken = default)
    {
        await imageWork.WaitAsync(cancellationToken);
        try
        {
            ImageEvidenceRecord[] pending; long generation;
            lock (gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (canContinue?.Invoke() == false) return 0;
                pending = images.List().Where(image => image.Status == "downloaded" && ImageAllowed(image)).ToArray();
                generation = Generation;
                // 完成识别后中断的派生审核可重试；已有业务记录使该步骤幂等。
                foreach (var key in images.List().Where(image => image.Status == "recognized" && ImageAllowed(image)).Select(image => image.Message).Distinct())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (canContinue?.Invoke() == false) return 0;
                    ApplyImageAnalysis(key, now);
                }
            }
            var count = 0;
            foreach (var expected in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (canContinue?.Invoke() == false) break;
                try
                {
                    string path;
                    lock (gate) path = images.GetOriginalPath(expected) ?? throw new FileNotFoundException();
                    var recognition = await recognizer.RecognizeAsync(path, cancellationToken);
                    lock (gate)
                    {
                        if (Generation != generation || canContinue?.Invoke() == false || !ImageAllowed(expected) || !ImageUnchanged(expected)) continue;
                        images.SaveRecognition(expected.Id, recognition, now);
                        ApplyImageAnalysis(expected.Message, now);
                        count++;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception)
                {
                    lock (gate)
                        if (Generation == generation && ImageAllowed(expected) && ImageUnchanged(expected))
                        {
                            images.SetFailure(expected.Id, "ocr", "image-ocr-failed", now);
                            ApplyImageAnalysis(expected.Message, now);
                        }
                }
            }
            return count;
        }
        finally { imageWork.Release(); }
    }

    public void RetryImage(string id, long generation, DateTimeOffset now)
    {
        lock (gate)
        {
            if (generation != Generation) throw new InvalidOperationException("工作区已恢复，请刷新后重试。");
            images.Retry(id, now);
        }
    }

    public string? OriginalImagePath(string id, long generation)
    {
        lock (gate)
        {
            if (Generation != generation) return null;
            var image = images.Find(id);
            return image is null || messages.Find(image.Message) is null ? null : images.GetOriginalPath(image);
        }
    }

    private bool ImageUnchanged(ImageEvidenceRecord expected)
    {
        var current = images.Find(expected.Id);
        return current is not null && current.Status == expected.Status && current.UpdatedAt == expected.UpdatedAt &&
            current.Attempts == expected.Attempts && current.Sha256 == expected.Sha256;
    }

    private ApplicationImage[] ImagesFor(MessageKey key) => images.ListFor(key).Select(image =>
    {
        var missing = images.GetOriginalPath(image) is null;
        return new ApplicationImage(image.Id, missing && image.RelativePath is not null ? "download-failed" : image.Status,
            missing ? null : $"/evidence/{AccountId}/{Generation}/{image.Id}",
            image.Recognition?.Text, image.Error, missing || image.Status is "download-failed" or "ocr-failed");
    }).ToArray();

    private SchoolMessage? EnrichedMessage(MessageKey key)
    {
        var original = messages.Find(key)?.Message;
        if (original is null) return null;
        var recognized = images.ListFor(key).Where(image => image.Recognition is not null)
            .ToDictionary(image => image.SegmentIndex);
        return original with { Segments = original.Segments.Select((segment, index) =>
            recognized.TryGetValue(index, out var image) ? segment with { Text = image.Recognition!.Text } : segment).ToArray() };
    }

    private void ApplyImageAnalysis(MessageKey key, DateTimeOffset now)
    {
        if (images.ListFor(key).Any(image => image.Status is "pending" or "downloaded")) return;
        var enriched = EnrichedMessage(key);
        if (enriched is null) return;
        var fingerprint = System.Text.Json.JsonSerializer.Serialize(new { key, Context = Context(key.GroupId, now) with { Now = DateTimeOffset.MinValue }, enriched.Segments });
        if (imageAnalysis.Contains(fingerprint)) return;
        router.ReanalyzeImage(enriched, Context(key.GroupId, now), pinnedGroups);
        imageAnalysis.Add(fingerprint);
    }

    private RuleCandidate? ImageReviewCandidate(PendingReview review)
    {
        if (review.State != ReviewState.Pending || review.IsCourseConflict || review.SplitParentTaskId is not null || review.Candidate.Time is not null)
            return null;
        foreach (var key in review.Candidate.Evidence)
        {
            if (review.Id != Processing.AccountCandidateRouter.ReviewId(key, 0)) continue;
            var enriched = EnrichedMessage(key);
            if (enriched is null || !enriched.Segments.Any(segment => segment.Kind == SegmentKind.Image && !string.IsNullOrWhiteSpace(segment.Text))) continue;
            var candidate = MessageRuleExtractor.Extract(enriched, Context(key.GroupId, DateTimeOffset.UtcNow)).Candidates.Single();
            if (candidate.SuggestedTime is null) continue;
            return review.Candidate with { SuggestedTime = candidate.SuggestedTime };
        }
        return null;
    }
}
