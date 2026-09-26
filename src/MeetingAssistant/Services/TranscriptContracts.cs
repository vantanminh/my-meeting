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

public sealed class TranscriptionRequest
{
    public required string AudioPath { get; init; }
    public string? LanguageCode { get; init; }
    public string? ExistingJobId { get; init; }
    public Func<string, CancellationToken, Task>? JobSubmitted { get; init; }
}

public interface ITranscriptionService
{
    string ProviderName { get; }
    bool IsConfigured { get; }

    Task<TranscriptResult> TranscribeAsync(
        TranscriptionRequest request,
        CancellationToken cancellationToken = default);
}
