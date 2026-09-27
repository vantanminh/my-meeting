namespace MeetingAssistant.Services;

public sealed record TranscriptSegmentResult(string? Speaker, int StartMs, int EndMs, string Text);

public sealed record TranscriptResult(
    string Text,
    string? Language,
    int? DurationMs,
    IReadOnlyList<TranscriptSegmentResult> Segments,
    string Provider,
    string? ProviderJobId,
    string? Model);

public enum TranscriptionStage
{
    Uploading,
    Queued,
    Transcribing
}

public sealed record TranscriptionProgress(TranscriptionStage Stage, long BytesSent = 0, long TotalBytes = 0);

public sealed class TranscriptionRequest
{
    public required string AudioPath { get; init; }
    public string? LanguageCode { get; init; }
    public string? ExistingJobId { get; init; }
    public int? AudioDurationMs { get; init; }
    public Func<string, CancellationToken, Task>? JobSubmitted { get; init; }
    public IProgress<TranscriptionProgress>? Progress { get; init; }
}

public interface ITranscriptionService
{
    string ProviderName { get; }
    bool IsConfigured { get; }

    Task<TranscriptResult> TranscribeAsync(
        TranscriptionRequest request,
        CancellationToken cancellationToken = default);

    Task<TranscriptResult?> ResumeAsync(
        string jobId,
        int? audioDurationMs,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken = default)
        => Task.FromResult<TranscriptResult?>(null);
}
