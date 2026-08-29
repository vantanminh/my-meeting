using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MeetingAssistant.Models;

namespace MeetingAssistant.Services;

public sealed record OpenAiConnectionResult(bool Success, string Message);

public sealed class OpenAiMeetingIntelligenceService : IMeetingIntelligenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly OpenAiConfiguration _configuration;
    private readonly DemoMeetingIntelligenceService _localFallback;
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _connectionTimeout;

    public OpenAiMeetingIntelligenceService(
        OpenAiConfiguration configuration,
        DemoMeetingIntelligenceService? localFallback = null,
        HttpClient? httpClient = null,
        TimeSpan? connectionTimeout = null)
    {
        _configuration = configuration;
        _localFallback = localFallback ?? new DemoMeetingIntelligenceService();
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _connectionTimeout = connectionTimeout ?? TimeSpan.FromSeconds(12);
        if (_connectionTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(connectionTimeout), "The connection timeout must be positive.");
    }

    public string ProviderLabel => _configuration.IsConfigured
        ? $"OpenAI · {_configuration.TranscriptionModel} + {_configuration.SummaryModel}"
        : "Local demo · OpenAI key not configured";

    public async Task<ProcessingResult> ProcessAsync(
        RecordingData recording,
        IProgress<ProcessingProgress> progress,
        CancellationToken cancellationToken = default)
    {
        var tracks = AudioTracks(recording).ToList();
        if (!_configuration.IsConfigured || tracks.Count == 0)
            return await _localFallback.ProcessAsync(recording, progress, cancellationToken);

        progress.Report(new ProcessingProgress(3, 0, "Preparing audio", "Checking microphone and system audio tracks"));
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();

        var transcriptSegments = new List<TranscriptSegment>();
        for (var index = 0; index < tracks.Count; index++)
        {
            var track = tracks[index];
            var percent = 15 + (int)Math.Round(index / (double)tracks.Count * 35);
            progress.Report(new ProcessingProgress(percent, 1, "Transcribing", $"Transcribing {track.DisplayName.ToLowerInvariant()}"));
            var segments = await TranscribeAsync(track, recording.Duration, cancellationToken);
            transcriptSegments.AddRange(segments);
        }

        if (transcriptSegments.Count == 0)
            throw new OpenAiServiceException("OpenAI did not return any transcript text.");

        transcriptSegments = transcriptSegments.OrderBy(segment => segment.Start).ThenBy(segment => segment.SpeakerName).ToList();
        progress.Report(new ProcessingProgress(58, 2, "Recognizing speakers", "Grouping microphone and system-audio turns"));
        var speakers = transcriptSegments
            .GroupBy(segment => segment.SpeakerName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SpeakerProfile
            {
                Id = StableSpeakerId(group.Key),
                Name = group.Key,
                Role = group.Key.Contains("You", StringComparison.OrdinalIgnoreCase) ? "Host" : "Participant",
                AccentColor = group.Key.Contains("You", StringComparison.OrdinalIgnoreCase) ? "#66E3C0" : "#88A9FF",
                Meetings = 1
            })
            .ToList();

        progress.Report(new ProcessingProgress(67, 3, "Finding the signal", "Asking the selected model for decisions and next steps"));
        var summary = await SummarizeAsync(recording, transcriptSegments, cancellationToken);
        progress.Report(new ProcessingProgress(92, 4, "Finishing meeting", "Saving the OpenAI transcript and summary locally"));

        var meeting = new Meeting
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = string.IsNullOrWhiteSpace(recording.Title) ? "Untitled meeting" : recording.Title.Trim(),
            StartedAt = recording.StartedAt,
            Duration = recording.Duration,
            ParticipantCount = speakers.Count,
            Status = MeetingStatus.Ready,
            SyncStatus = "OpenAI · ready to sync",
            AudioSources = $"{recording.Configuration.Microphone} + {recording.Configuration.SystemAudio}",
            Speakers = speakers,
            Transcript = transcriptSegments,
            Summary = summary
        };

        progress.Report(new ProcessingProgress(100, 4, "Ready", "Your meeting is ready to review"));
        return new ProcessingResult(meeting);
    }

    public Task<OpenAiConnectionResult> TestConnectionAsync(CancellationToken cancellationToken = default)
        => TestConnectionAsync(apiKeyOverride: null, cancellationToken: cancellationToken);

    public async Task<OpenAiConnectionResult> TestConnectionAsync(
        string? apiKeyOverride,
        CancellationToken cancellationToken = default)
    {
        var apiKey = string.IsNullOrWhiteSpace(apiKeyOverride)
            ? _configuration.ApiKey
            : apiKeyOverride.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
            return new(false, "Enter an OpenAI API key first.");
        if (apiKey.Any(char.IsControl))
            return new(false, "The OpenAI API key format is invalid.");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_connectionTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.IsSuccessStatusCode)
                return new(true, "OpenAI connection is ready.");

            return new(false, await ErrorMessageAsync(response, "OpenAI rejected the connection.", timeout.Token));
        }
        catch (HttpRequestException)
        {
            return new(false, "OpenAI is not reachable. Check the network and try again.");
        }
        catch (FormatException)
        {
            return new(false, "The OpenAI API key format is invalid.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, "OpenAI took too long to respond.");
        }
    }

    private async Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(
        AudioTrack track,
        TimeSpan fallbackDuration,
        CancellationToken cancellationToken)
    {
        var model = _configuration.TranscriptionModel;
        var diarized = model.Contains("diarize", StringComparison.OrdinalIgnoreCase);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/transcriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _configuration.ApiKey);

        using var form = new MultipartFormDataContent();
        await using var stream = File.OpenRead(track.Path);
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(fileContent, "file", Path.GetFileName(track.Path));
        form.Add(new StringContent(model), "model");
        form.Add(new StringContent(diarized ? "diarized_json" : "json"), "response_format");
        if (diarized) form.Add(new StringContent("auto"), "chunking_strategy");
        request.Content = form;

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new OpenAiServiceException(await ErrorMessageAsync(response, "OpenAI transcription failed.", cancellationToken));

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var segments = new List<TranscriptSegment>();
            if (root.TryGetProperty("segments", out var segmentArray) && segmentArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var segment in segmentArray.EnumerateArray())
                {
                    var text = segment.TryGetProperty("text", out var textElement) ? textElement.GetString()?.Trim() : null;
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    var start = ReadDouble(segment, "start");
                    var end = ReadDouble(segment, "end");
                    var serverSpeaker = segment.TryGetProperty("speaker", out var speakerElement) ? speakerElement.GetString() : null;
                    var speakerName = string.IsNullOrWhiteSpace(serverSpeaker) ? track.DisplayName : $"{track.DisplayName} · {serverSpeaker}";
                    segments.Add(CreateSegment(speakerName, start, end, text));
                }
            }

            if (segments.Count > 0) return segments;

            var transcript = root.TryGetProperty("text", out var transcriptElement) ? transcriptElement.GetString()?.Trim() : null;
            return string.IsNullOrWhiteSpace(transcript)
                ? []
                : [CreateSegment(track.DisplayName, 0, Math.Max(fallbackDuration.TotalSeconds, 1), transcript)];
        }
        catch (JsonException)
        {
            throw new OpenAiServiceException("OpenAI returned an unexpected transcription response.");
        }
    }

    private async Task<MeetingSummary> SummarizeAsync(
        RecordingData recording,
        IReadOnlyCollection<TranscriptSegment> transcript,
        CancellationToken cancellationToken)
    {
        var transcriptText = string.Join(
            Environment.NewLine,
            transcript.Select(segment => $"[{segment.Timestamp}] {segment.SpeakerName}: {segment.Text}"));
        var payload = new
        {
            model = _configuration.SummaryModel,
            store = false,
            instructions = "Bạn là trợ lý ghi chép cuộc họp. Trả về đúng một JSON object, không markdown, với các field: overview (string), keyPoints (array of strings), decisions (array of strings), actionItems (array of objects gồm text, owner, due, isComplete), deadlines (array of objects gồm label, date, owner), questions (array of strings), importantMoments (array of strings). Viết bằng ngôn ngữ chính của transcript; nêu rõ khi thông tin chưa xác định.",
            input = $"Tên cuộc họp: {recording.Title}\n\nTranscript:\n{transcriptText}",
            text = new { format = new { type = "json_object" } }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _configuration.ApiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new OpenAiServiceException(await ErrorMessageAsync(response, "OpenAI summary failed.", cancellationToken));

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(body);
            var outputText = ExtractOutputText(document.RootElement);
            var json = ExtractJsonObject(outputText);
            var summary = JsonSerializer.Deserialize<MeetingSummary>(json, JsonOptions);
            if (summary is null) throw new JsonException();
            summary.KeyPoints ??= [];
            summary.Decisions ??= [];
            summary.ActionItems ??= [];
            summary.Deadlines ??= [];
            summary.Questions ??= [];
            summary.ImportantMoments ??= [];
            return summary;
        }
        catch (JsonException)
        {
            throw new OpenAiServiceException("OpenAI returned an unexpected summary response.");
        }
    }

    private static IEnumerable<AudioTrack> AudioTracks(RecordingData recording)
    {
        if (IsUsableAudioFile(recording.MicrophonePath))
            yield return new AudioTrack(recording.MicrophonePath!, "You");
        if (IsUsableAudioFile(recording.SystemAudioPath))
            yield return new AudioTrack(recording.SystemAudioPath!, "Meeting participant");
    }

    private static bool IsUsableAudioFile(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path) && new FileInfo(path).Length > 44;

    private static TranscriptSegment CreateSegment(string speakerName, double start, double end, string text)
        => new()
        {
            SpeakerId = StableSpeakerId(speakerName),
            SpeakerName = speakerName,
            Start = TimeSpan.FromSeconds(Math.Max(0, start)),
            End = TimeSpan.FromSeconds(Math.Max(Math.Max(0, start), end)),
            Text = text
        };

    private static string StableSpeakerId(string speakerName)
        => "openai-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(speakerName))).ToLowerInvariant()[..12];

    private static double ReadDouble(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.TryGetDouble(out var number) ? number : 0;

    private static string ExtractOutputText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
            return outputText.GetString() ?? string.Empty;

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var parts = new List<string>();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    parts.Add(text.GetString() ?? string.Empty);
            }
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static string ExtractJsonObject(string value)
    {
        var start = value.IndexOf('{');
        var end = value.LastIndexOf('}');
        if (start < 0 || end <= start) throw new JsonException();
        return value[start..(end + 1)];
    }

    private static async Task<string> ErrorMessageAsync(
        HttpResponseMessage response,
        string fallback,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                var message = error.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null;
                if (!string.IsNullOrWhiteSpace(message)) return $"{fallback} {message}";
            }
        }
        catch (JsonException)
        {
            // Keep a stable, user-facing message when a proxy returns non-JSON.
        }

        return $"{fallback} ({(int)response.StatusCode})";
    }

    private sealed record AudioTrack(string Path, string DisplayName);
}

public sealed class OpenAiServiceException : Exception
{
    public OpenAiServiceException(string message) : base(message) { }
}
