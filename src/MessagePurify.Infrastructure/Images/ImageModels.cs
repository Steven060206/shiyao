using MessagePurify.Core;

namespace MessagePurify.Infrastructure.Images;

public sealed record OcrTextLine(string Text, IReadOnlyList<IReadOnlyList<double>> Box, double Confidence);
public sealed record ImageRecognition(string Text, IReadOnlyList<OcrTextLine> Lines);

public interface IImageTextRecognizer
{
    Task<ImageRecognition> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default);
}

public sealed record ImageEvidenceRecord(string Id, MessageKey Message, int SegmentIndex, string? AssetId,
    string Status, string? RelativePath, string? Sha256, long? Length, ImageRecognition? Recognition,
    string? Error, int Attempts, DateTimeOffset UpdatedAt);
