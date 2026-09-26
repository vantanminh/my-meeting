using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using MeetingAssistant.Models;

namespace MeetingAssistant.Services;

public sealed class MeetingProcessingException : Exception
{
    public MeetingProcessingException(string message) : base(message) { }
    public MeetingProcessingException(string message, Exception innerException) : base(message, innerException) { }
    public bool MeetingDeleted { get; init; }
}

public sealed class MeetingProcessingService : IMeetingIntelligenceService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);
    private static readonly string[] SpeakerColors = ["#88A9FF", "#66E3C0", "#FF977E", "#F3C878", "#C084FC"];

    private readonly AssemblyAiConfiguration _assemblyAi;
    private readonly OpenAiConfiguration _openAi;
    private readonly ITranscriptionService _transcription;
    private readonly MeetingSummaryService _summary;
    private readonly OpenAiMeetingIntelligenceService _openAiIntelligence;

    public MeetingProcessingService(
        AssemblyAiConfiguration assemblyAi,
        OpenAiConfiguration openAi,
        ITranscriptionService transcription,
        MeetingSummaryService summary,
        OpenAiMeetingIntelligenceService openAiIntelligence)
    {
        _assemblyAi = assemblyAi;
        _openAi = openAi;
        _transcription = transcription;
        _summary = summary;
        _openAiIntelligence = openAiIntelligence;
    }

    public string ProviderLabel
    {
        get
        {
            if (UseOpenAiTranscription)
                return _openAiIntelligence.ProviderLabel;
            if (!_assemblyAi.IsConfigured)
                return "AssemblyAI key not configured";
            return $"AssemblyAI · {string.Join(" + ", _assemblyAi.SpeechModels)} + {_openAi.SummaryModel}";
        }
    }

    private bool UseOpenAiTranscription
        => _assemblyAi.Provider is "openai" or "openai-transcribe";

    public async Task<ProcessingResult> ProcessAsync(
        RecordingData recording,
        IProgress<ProcessingProgress> progress,
        CancellationToken cancellationToken = default)
    {
        var meeting = new Meeting
        {
            Title = string.IsNullOrWhiteSpace(recording.Title) ? "Untitled meeting" : recording.Title.Trim(),
            StartedAt = recording.StartedAt,
            Duration = recording.Duration,
            MicrophonePath = recording.MicrophonePath,
            SystemAudioPath = recording.SystemAudioPath,
            SessionDirectory = recording.SessionDirectory,
            AudioSources = $"{recording.Configuration.Microphone} + {recording.Configuration.SystemAudio}",
            Status = MeetingStatus.Processing,
            ProcessingPhase = ProcessingPhase.Queued
        };
        return await ProcessMeetingAsync(
            new MeetingProcessingRequest { Recording = recording, Meeting = meeting },
            progress,
            cancellationToken);
    }

    public async Task<ProcessingResult> ProcessMeetingAsync(
        MeetingProcessingRequest request,
        IProgress<ProcessingProgress> progress,
        CancellationToken cancellationToken = default)
    {
        var meeting = request.Meeting;
        meeting.EnsureCollections();
        var gate = Gates.GetOrAdd(meeting.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (IsFinished(meeting))
            {
                Report(progress, 100, 4, "Generating meeting notes...", "Generating meeting notes...");
                return new ProcessingResult(meeting);
            }

            EnsureStillThere(request);
            if (UseOpenAiTranscription)
                return await ProcessWithOpenAiAsync(request, progress, cancellationToken);

            return await ProcessWithAssemblyAiAsync(request, progress, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<MeetingSummary> SummarizeAsync(Meeting meeting, CancellationToken cancellationToken = default)
    {
        meeting.EnsureCollections();
        return SummarizeNotesAsync(meeting, cancellationToken);
    }

    private async Task<ProcessingResult> ProcessWithAssemblyAiAsync(
        MeetingProcessingRequest request,
        IProgress<ProcessingProgress> progress,
        CancellationToken cancellationToken)
    {
        var meeting = request.Meeting;
        var started = Stopwatch.StartNew();
        meeting.ProcessingStartedAt ??= DateTimeOffset.Now;
        try
        {
            if (meeting.Transcript.Count == 0)
            {
                if (!_transcription.IsConfigured)
                {
                    throw new MeetingProcessingException(
                        "Add an AssemblyAI API key to transcribe this recording. Your audio is still saved locally.");
                }

                meeting.Status = MeetingStatus.Processing;
                meeting.ProcessingPhase = ProcessingPhase.Queued;
                meeting.ProcessingError = null;
                await PersistAsync(request, cancellationToken);
                Report(progress, 10, 0, "Processing recording...", "Processing recording...");
                MeetingProcessingLog.Write("meeting_transcription_started", meeting.Id, "assemblyai", "started", null, null);

                using var prepared = MeetingAudioPreparation.Prepare(request.Recording, cancellationToken);
                meeting.AudioDurationMs = prepared.DurationMs;
                EnsureStillThere(request);
                meeting.ProcessingPhase = ProcessingPhase.Transcribing;
                await PersistAsync(request, cancellationToken);
                Report(progress, 40, 1, "Transcribing meeting...", "Transcribing meeting...");

                var transcriptionStarted = Stopwatch.StartNew();
                var transcript = await _transcription.TranscribeAsync(new TranscriptionRequest
                {
                    AudioPath = prepared.Path,
                    LanguageCode = request.TranscriptionLanguage,
                    ExistingJobId = meeting.TranscriptionJobId,
                    JobSubmitted = async (jobId, token) =>
                    {
                        meeting.TranscriptionJobId = jobId;
                        meeting.TranscriptionProvider = "assemblyai";
                        meeting.ProcessingPhase = ProcessingPhase.Transcribing;
                        await PersistAsync(request, token);
                    }
                }, cancellationToken);

                ApplyTranscript(meeting, transcript, transcriptionStarted.Elapsed);
                Report(progress, 55, 2, "Transcribing meeting...", "Transcribing meeting...");
                await PersistAsync(request, cancellationToken);
                MeetingProcessingLog.Write(
                    "meeting_transcription_completed",
                    meeting.Id,
                    transcript.Provider,
                    "completed",
                    transcriptionStarted.Elapsed,
                    null);
            }

            EnsureStillThere(request);
            meeting.ProcessingPhase = ProcessingPhase.Summarizing;
            meeting.Status = MeetingStatus.Processing;
            await PersistAsync(request, cancellationToken);
            Report(progress, 80, 3, "Generating meeting notes...", "Generating meeting notes...");
            MeetingProcessingLog.Write("meeting_summary_started", meeting.Id, "openai", "started", null, null);
            var summaryStarted = Stopwatch.StartNew();
            var notes = await _summary.SummarizeAsync(
                meeting.Title,
                meeting.DetectedLanguage,
                meeting.Transcript,
                cancellationToken);
            ApplyNotes(meeting, notes, summaryStarted.Elapsed);
            meeting.ProcessingPhase = ProcessingPhase.Completed;
            meeting.Status = MeetingStatus.Ready;
            meeting.ProcessingError = null;
            meeting.ProcessedAt = DateTimeOffset.Now;
            meeting.UpdatedAt = DateTimeOffset.Now;
            await PersistAsync(request, cancellationToken);
            MeetingProcessingLog.Write("meeting_summary_completed", meeting.Id, "openai", "completed", summaryStarted.Elapsed, null);
            Report(progress, 100, 4, "Generating meeting notes...", "Generating meeting notes...");
            return new ProcessingResult(meeting);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not MeetingProcessingException || !((MeetingProcessingException)exception).MeetingDeleted)
        {
            var message = exception is MeetingProcessingException processing
                ? processing.Message
                : "Unable to process this meeting.";
            await MarkFailedAsync(request, message, exception, started.Elapsed, cancellationToken);
            if (exception is MeetingProcessingException)
                throw;
            throw new MeetingProcessingException(message, exception);
        }
    }

    private async Task<ProcessingResult> ProcessWithOpenAiAsync(
        MeetingProcessingRequest request,
        IProgress<ProcessingProgress> progress,
        CancellationToken cancellationToken)
    {
        var meeting = request.Meeting;
        try
        {
            if (meeting.Transcript.Count == 0)
            {
                Report(progress, 10, 0, "Processing recording...", "Processing recording...");
                var quiet = new Progress<ProcessingProgress>(value =>
                    progress.Report(value with { ShowPercent = false }));
                var result = await _openAiIntelligence.ProcessAsync(request.Recording, quiet, cancellationToken);
                CopyOnto(meeting, result.Meeting);
                meeting.TranscriptionProvider = "openai";
                meeting.ProcessingPhase = ProcessingPhase.Completed;
                meeting.Status = MeetingStatus.Ready;
                meeting.ProcessingError = null;
                meeting.ProcessedAt = DateTimeOffset.Now;
                await PersistAsync(request, cancellationToken);
                Report(progress, 100, 4, "Generating meeting notes...", "Generating meeting notes...");
                return new ProcessingResult(meeting);
            }

            Report(progress, 80, 3, "Generating meeting notes...", "Generating meeting notes...");
            meeting.Summary = await _openAiIntelligence.SummarizeAsync(meeting, cancellationToken);
            meeting.Summary.EnsureCollections();
            meeting.ProcessingPhase = ProcessingPhase.Completed;
            meeting.Status = MeetingStatus.Ready;
            meeting.ProcessingError = null;
            meeting.SummarizedAt = DateTimeOffset.Now;
            meeting.ProcessedAt = DateTimeOffset.Now;
            await PersistAsync(request, cancellationToken);
            Report(progress, 100, 4, "Generating meeting notes...", "Generating meeting notes...");
            return new ProcessingResult(meeting);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var message = exception.Message;
            await MarkFailedAsync(request, message, exception, null, cancellationToken);
            throw;
        }
    }

    private async Task<MeetingSummary> SummarizeNotesAsync(Meeting meeting, CancellationToken cancellationToken)
    {
        if (UseOpenAiTranscription && meeting.Transcript.Count > 0 && _openAi.IsConfigured)
            return await _openAiIntelligence.SummarizeAsync(meeting, cancellationToken);

        var notes = await _summary.SummarizeAsync(meeting.Title, meeting.DetectedLanguage, meeting.Transcript, cancellationToken);
        return notes.Summary;
    }

    private static void ApplyTranscript(Meeting meeting, TranscriptResult transcript, TimeSpan elapsed)
    {
        var segments = new List<TranscriptSegment>(transcript.Segments.Count);
        foreach (var segment in transcript.Segments)
        {
            var speakerName = DisplaySpeaker(segment.Speaker);
            var start = TimeSpan.FromMilliseconds(Math.Max(0, segment.StartMs));
            var endMs = Math.Max(segment.StartMs, segment.EndMs);
            segments.Add(new TranscriptSegment
            {
                SpeakerId = StableSpeakerId(speakerName),
                SpeakerName = speakerName,
                Start = start,
                End = TimeSpan.FromMilliseconds(endMs),
                Text = segment.Text
            });
        }

        meeting.Transcript = segments.OrderBy(segment => segment.Start).ToList();
        meeting.TranscriptText = string.IsNullOrWhiteSpace(transcript.Text)
            ? string.Join(Environment.NewLine, meeting.Transcript.Select(segment => segment.Text))
            : transcript.Text;
        meeting.DetectedLanguage = transcript.Language;
        meeting.TranscriptionProvider = transcript.Provider;
        meeting.TranscriptionModel = transcript.Model;
        meeting.TranscriptionJobId = transcript.ProviderJobId ?? meeting.TranscriptionJobId;
        meeting.TranscriptionDurationMs = (long)elapsed.TotalMilliseconds;
        meeting.TranscribedAt = DateTimeOffset.Now;
        if (transcript.DurationMs is > 0)
            meeting.AudioDurationMs = transcript.DurationMs;
        meeting.Speakers = meeting.Transcript
            .GroupBy(segment => segment.SpeakerName, StringComparer.OrdinalIgnoreCase)
            .Select((group, index) => new SpeakerProfile
            {
                Id = group.First().SpeakerId,
                Name = group.Key,
                Role = "Participant",
                AccentColor = SpeakerColors[index % SpeakerColors.Length],
                Meetings = 1
            })
            .ToList();
        meeting.ParticipantCount = meeting.Speakers.Count;
        meeting.ProcessingPhase = ProcessingPhase.Summarizing;
    }

    private static void ApplyNotes(Meeting meeting, MeetingNotes notes, TimeSpan elapsed)
    {
        notes.Summary.EnsureCollections();
        meeting.Summary = notes.Summary;
        meeting.SummarizedAt = DateTimeOffset.Now;
        meeting.SummaryDurationMs = (long)elapsed.TotalMilliseconds;
        if (!string.IsNullOrWhiteSpace(notes.Title)
            && (string.IsNullOrWhiteSpace(meeting.Title) || meeting.Title.Equals("Untitled meeting", StringComparison.OrdinalIgnoreCase)))
        {
            meeting.Title = notes.Title.Trim();
        }
    }

    private static void CopyOnto(Meeting target, Meeting source)
    {
        source.EnsureCollections();
        target.Title = string.IsNullOrWhiteSpace(target.Title) ? source.Title : target.Title;
        target.Duration = source.Duration == TimeSpan.Zero ? target.Duration : source.Duration;
        target.ParticipantCount = source.ParticipantCount;
        target.Speakers = source.Speakers;
        target.Transcript = source.Transcript;
        target.Summary = source.Summary;
        target.TranscriptText = string.Join(Environment.NewLine, source.Transcript.Select(segment => segment.Text));
        target.MicrophonePath ??= source.MicrophonePath;
        target.SystemAudioPath ??= source.SystemAudioPath;
        target.SessionDirectory ??= source.SessionDirectory;
    }

    private static bool IsFinished(Meeting meeting)
        => meeting.ProcessingPhase == ProcessingPhase.Completed
            && meeting.Transcript.Count > 0
            && meeting.Summary is not null;

    private static void EnsureStillThere(MeetingProcessingRequest request)
    {
        if (request.StillExists is not null && !request.StillExists())
        {
            throw new MeetingProcessingException("This meeting was deleted while it was processing.")
            {
                MeetingDeleted = true
            };
        }
    }

    private static async Task PersistAsync(MeetingProcessingRequest request, CancellationToken cancellationToken)
    {
        if (request.Persist is null) return;
        request.Meeting.UpdatedAt = DateTimeOffset.Now;
        await request.Persist(request.Meeting, cancellationToken);
    }

    private async Task MarkFailedAsync(
        MeetingProcessingRequest request,
        string message,
        Exception exception,
        TimeSpan? elapsed,
        CancellationToken cancellationToken)
    {
        var meeting = request.Meeting;
        var sanitized = AssemblyAiTranscriptionService.Sanitize(message);
        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = "Unable to process this meeting.";
        meeting.ProcessingPhase = ProcessingPhase.Failed;
        meeting.Status = MeetingStatus.Failed;
        meeting.ProcessingError = sanitized;
        meeting.UpdatedAt = DateTimeOffset.Now;
        var transcriptEvent = meeting.Transcript.Count == 0
            ? "meeting_transcription_failed"
            : "meeting_summary_failed";
        var provider = meeting.Transcript.Count == 0 ? _transcription.ProviderName : "openai";
        MeetingProcessingLog.Write(transcriptEvent, meeting.Id, provider, "failed", elapsed, sanitized);
        try
        {
            await PersistAsync(request, CancellationToken.None);
        }
        catch (Exception persistException) when (persistException is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("meeting_processing_persist_failed meeting={0}", meeting.Id);
        }

        _ = exception;
        _ = cancellationToken;
    }

    private static void Report(
        IProgress<ProcessingProgress> progress,
        int percent,
        int stageIndex,
        string stage,
        string message)
        => progress.Report(new ProcessingProgress(percent, stageIndex, stage, message, ShowPercent: false));

    internal static string DisplaySpeaker(string? speaker)
    {
        if (string.IsNullOrWhiteSpace(speaker)) return "Speaker";
        var trimmed = speaker.Trim();
        if (trimmed.StartsWith("Speaker ", StringComparison.OrdinalIgnoreCase)) return trimmed;
        if (trimmed.Length == 1 && char.IsLetter(trimmed[0]))
            return "Speaker " + char.ToUpperInvariant(trimmed[0]);
        return trimmed;
    }

    private static string StableSpeakerId(string speakerName)
        => "spk-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("assemblyai:" + speakerName))).ToLowerInvariant()[..12];
}

internal static class MeetingProcessingLog
{
    public static void Write(
        string eventName,
        string meetingId,
        string provider,
        string status,
        TimeSpan? duration,
        string? error)
    {
        var durationMs = duration is null ? string.Empty : ((long)duration.Value.TotalMilliseconds).ToString();
        var sanitized = AssemblyAiTranscriptionService.Sanitize(error);
        Trace.TraceInformation(
            "{0} meeting={1} provider={2} status={3} durationMs={4} error={5}",
            eventName,
            meetingId,
            provider,
            status,
            durationMs,
            sanitized);
    }
}
