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
    private readonly bool _compressUploads;

    public MeetingProcessingService(
        AssemblyAiConfiguration assemblyAi,
        OpenAiConfiguration openAi,
        ITranscriptionService transcription,
        MeetingSummaryService summary,
        OpenAiMeetingIntelligenceService openAiIntelligence,
        bool compressUploads = true)
    {
        _assemblyAi = assemblyAi;
        _openAi = openAi;
        _transcription = transcription;
        _summary = summary;
        _openAiIntelligence = openAiIntelligence;
        _compressUploads = compressUploads;
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
                Report(progress, 5, 0, "Processing recording...", "Processing recording...");
                MeetingProcessingLog.Write("meeting_transcription_started", meeting.Id, "assemblyai", "started", null, null);

                var transcriptionStarted = Stopwatch.StartNew();
                var transcriptionProgress = new ForwardProgress<TranscriptionProgress>(value => ReportTranscription(progress, value));
                TranscriptResult? transcript = null;
                if (!string.IsNullOrWhiteSpace(meeting.TranscriptionJobId))
                {
                    Report(progress, 45, 1, "Transcribing meeting...", "Transcribing meeting...", "Checking the saved transcription job");
                    transcript = await _transcription.ResumeAsync(
                        meeting.TranscriptionJobId,
                        meeting.AudioDurationMs is { } savedMs ? (int)Math.Min(savedMs, int.MaxValue) : null,
                        transcriptionProgress,
                        cancellationToken);
                }

                if (transcript is null)
                {
                    var prepareStarted = Stopwatch.StartNew();
                    var prepareProgress = new ForwardProgress<double>(fraction =>
                    {
                        var percent = (int)Math.Round(fraction * 100);
                        Report(progress, 5 + (int)(fraction * 20), 0, "Processing recording...", "Processing recording...", "Preparing audio · {0}%", percent);
                    });
                    using var prepared = await Task.Run(
                        () => MeetingAudioPreparation.Prepare(request.Recording, cancellationToken, prepareProgress, _compressUploads),
                        cancellationToken);
                    MeetingProcessingLog.Write(
                        "meeting_audio_prepared",
                        meeting.Id,
                        Path.GetExtension(prepared.Path).TrimStart('.'),
                        $"bytes={prepared.UploadBytes} audioMs={prepared.DurationMs}",
                        prepareStarted.Elapsed,
                        null);
                    meeting.AudioDurationMs = prepared.DurationMs;
                    EnsureStillThere(request);
                    meeting.ProcessingPhase = ProcessingPhase.Transcribing;
                    await PersistAsync(request, cancellationToken);
                    Report(progress, 25, 1, "Transcribing meeting...", "Transcribing meeting...");

                    transcript = await _transcription.TranscribeAsync(new TranscriptionRequest
                    {
                        AudioPath = prepared.Path,
                        LanguageCode = request.TranscriptionLanguage,
                        AudioDurationMs = prepared.DurationMs,
                        Progress = transcriptionProgress,
                        JobSubmitted = async (jobId, token) =>
                        {
                            meeting.TranscriptionJobId = jobId;
                            meeting.TranscriptionProvider = "assemblyai";
                            meeting.ProcessingPhase = ProcessingPhase.Transcribing;
                            await PersistAsync(request, token);
                        }
                    }, cancellationToken);
                }

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
            Report(progress, 80, 3, "Generating meeting notes...", "Generating meeting notes...", "Writing the summary, decisions and action items");
            MeetingProcessingLog.Write("meeting_summary_started", meeting.Id, "openai", "started", null, null);
            var summaryStarted = Stopwatch.StartNew();
            var notes = await _summary.SummarizeAsync(
                meeting.Title,
                meeting.DetectedLanguage,
                meeting.Transcript,
                cancellationToken,
                new ForwardProgress<SummaryProgress>(value =>
                {
                    if (value.Merging)
                        Report(progress, 92, 3, "Generating meeting notes...", "Generating meeting notes...", "Merging notes from every part");
                    else
                        Report(progress, 80 + 12 * value.CompletedParts / Math.Max(1, value.TotalParts), 3,
                            "Generating meeting notes...", "Generating meeting notes...",
                            "Summarized {0} of {1} parts", value.CompletedParts, value.TotalParts);
                }));
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
        var cause = exception.InnerException ?? exception;
        MeetingProcessingLog.Write(transcriptEvent, meeting.Id, provider, "failed", elapsed, $"{sanitized} ({cause.GetType().Name})");
        try
        {
            await PersistAsync(request, CancellationToken.None);
        }
        catch (Exception persistException) when (persistException is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("meeting_processing_persist_failed meeting={0}", meeting.Id);
        }

        _ = cancellationToken;
    }

    private static void Report(
        IProgress<ProcessingProgress> progress,
        int percent,
        int stageIndex,
        string stage,
        string message,
        string? detail = null,
        params object[] detailArgs)
        => progress.Report(new ProcessingProgress(percent, stageIndex, stage, message, ShowPercent: false)
        {
            Detail = detail,
            DetailArgs = detailArgs
        });

    private static void ReportTranscription(IProgress<ProcessingProgress> progress, TranscriptionProgress value)
    {
        switch (value.Stage)
        {
            case TranscriptionStage.Uploading:
                var fraction = value.TotalBytes > 0 ? Math.Clamp(value.BytesSent / (double)value.TotalBytes, 0, 1) : 0;
                Report(progress, 25 + (int)(fraction * 15), 1, "Transcribing meeting...", "Transcribing meeting...",
                    "Uploading recording · {0}% of {1}", (int)Math.Round(fraction * 100), FormatSize(value.TotalBytes));
                break;
            case TranscriptionStage.Queued:
                Report(progress, 45, 1, "Transcribing meeting...", "Transcribing meeting...", "AssemblyAI has queued the recording");
                break;
            default:
                Report(progress, 55, 1, "Transcribing meeting...", "Transcribing meeting...", "AssemblyAI is transcribing and separating speakers");
                break;
        }
    }

    private static string FormatSize(long bytes)
        => bytes >= 1024 * 1024
            ? $"{bytes / (1024d * 1024d):0.#} MB"
            : $"{Math.Max(1, bytes / 1024)} KB";

    private sealed class ForwardProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;

        public ForwardProgress(Action<T> handler) => _handler = handler;

        public void Report(T value) => _handler(value);
    }

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
    private static readonly object FileGate = new();
    private static string? _directory;

    public static string? Directory => _directory;

    public static void UseDirectory(string directory)
    {
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            foreach (var old in new DirectoryInfo(directory).GetFiles("processing-*.log"))
            {
                if (old.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-14)) old.Delete();
            }

            _directory = directory;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _directory = null;
        }
    }

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
        var line = string.Format(
            "{0} meeting={1} provider={2} status={3} durationMs={4} error={5}",
            eventName,
            meetingId,
            provider,
            status,
            durationMs,
            sanitized);
        Trace.TraceInformation(line);

        var directory = _directory;
        if (directory is null) return;
        try
        {
            lock (FileGate)
            {
                File.AppendAllText(
                    Path.Combine(directory, $"processing-{DateTime.Now:yyyyMMdd}.log"),
                    $"{DateTimeOffset.Now:O} {line}{Environment.NewLine}");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
