using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MeetingAssistant.Services;

public sealed class AssemblyAiTranscriptionService : ITranscriptionService
{
    public const string BaseUrl = "https://api.assemblyai.com";

    private readonly AssemblyAiConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _pollInterval;

    public AssemblyAiTranscriptionService(
        AssemblyAiConfiguration configuration,
        HttpClient httpClient,
        TimeSpan? pollInterval = null)
    {
        _configuration = configuration;
        _httpClient = httpClient;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(3);
        if (_pollInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollInterval));
    }

    public string ProviderName => "assemblyai";
    public bool IsConfigured => _configuration.IsConfigured;

    public async Task<TranscriptResult> TranscribeAsync(
        TranscriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_configuration.IsConfigured)
        {
            throw new MeetingProcessingException(
                "Add an AssemblyAI API key to transcribe this recording. Your audio is still saved locally.");
        }

        if (string.IsNullOrWhiteSpace(request.AudioPath) || !File.Exists(request.AudioPath))
        {
            throw new MeetingProcessingException(
                "No usable recording audio was found. Your recording is still available; record a new meeting and retry.");
        }

        if (!string.IsNullOrWhiteSpace(request.ExistingJobId))
        {
            var existing = await TryReadCompletedJobAsync(request.ExistingJobId, request.AudioDurationMs, request.Progress, cancellationToken);
            if (existing is not null) return existing;
        }

        var uploadUrl = await UploadAsync(request.AudioPath, request.Progress, cancellationToken);
        var jobId = await SubmitAsync(uploadUrl, request.LanguageCode, cancellationToken);
        if (request.JobSubmitted is not null)
            await request.JobSubmitted(jobId, cancellationToken);
        return await PollAsync(jobId, request.AudioDurationMs, request.Progress, cancellationToken);
    }

    public async Task<TranscriptResult?> ResumeAsync(
        string jobId,
        int? audioDurationMs,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        if (!_configuration.IsConfigured || string.IsNullOrWhiteSpace(jobId))
            return null;
        return await TryReadCompletedJobAsync(jobId, audioDurationMs, progress, cancellationToken);
    }

    internal static TimeSpan PollDeadline(int? audioDurationMs)
    {
        // AssemblyAI usually returns well under half the audio length. The job id is saved,
        // so hitting this limit only pauses the meeting; a retry resumes polling.
        var audio = TimeSpan.FromMilliseconds(Math.Max(0, audioDurationMs ?? 0));
        var deadline = TimeSpan.FromMinutes(15) + audio * 0.5;
        if (deadline < TimeSpan.FromMinutes(20)) return TimeSpan.FromMinutes(20);
        return deadline > TimeSpan.FromHours(3) ? TimeSpan.FromHours(3) : deadline;
    }

    internal static TimeSpan UploadTimeout(long bytes)
    {
        var atSlowLink = TimeSpan.FromSeconds(bytes / 150_000.0);
        return atSlowLink < TimeSpan.FromMinutes(10) ? TimeSpan.FromMinutes(10) : atSlowLink;
    }

    internal static JsonObject CreateTranscriptBody(string audioUrl, string? languageCode, IReadOnlyList<string> speechModels)
    {
        var body = new JsonObject
        {
            ["audio_url"] = audioUrl,
            ["speaker_labels"] = true,
            ["speech_models"] = new JsonArray(speechModels.Select(model => JsonValue.Create(model)!).ToArray())
        };

        if (string.IsNullOrWhiteSpace(languageCode)
            || languageCode.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            // Universal-3.5 Pro code-switches across its languages when detection is on,
            // so English technical terms stay as spoken instead of being rewritten.
            body["language_detection"] = true;
            return body;
        }

        if (languageCode.Equals("vi", StringComparison.OrdinalIgnoreCase))
        {
            // language_code cannot be combined with language_detection.
            // A Vietnamese meeting still needs English code switching for product names
            // and technical terms, so steer detection instead of locking a single language.
            body["language_detection"] = true;
            body["language_detection_options"] = new JsonObject
            {
                ["expected_languages"] = new JsonArray(JsonValue.Create("vi"), JsonValue.Create("en")),
                ["fallback_language"] = "vi",
                ["code_switching"] = true
            };
            return body;
        }

        body["language_code"] = languageCode.Trim().ToLowerInvariant();
        body["language_detection"] = false;
        return body;
    }

    private async Task<TranscriptResult?> TryReadCompletedJobAsync(
        string jobId,
        int? audioDurationMs,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/v2/transcript/{Uri.EscapeDataString(jobId)}"),
            TimeSpan.FromSeconds(30),
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        var body = await ReadBodyAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new MeetingProcessingException(MapHttpFailure(response.StatusCode, body, "AssemblyAI transcription failed."));

        using var document = ParseJson(body);
        var status = ReadString(document.RootElement, "status");
        if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
            return ParseCompleted(document.RootElement, jobId);
        if (string.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
            return null;

        return await PollAsync(jobId, audioDurationMs, progress, cancellationToken);
    }

    private async Task<string> UploadAsync(
        string path,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var totalBytes = new FileInfo(path).Length;
        progress?.Report(new TranscriptionProgress(TranscriptionStage.Uploading, 0, totalBytes));
        using var response = await SendAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v2/upload");
            var stream = new ProgressReadStream(File.OpenRead(path), sent =>
                progress?.Report(new TranscriptionProgress(TranscriptionStage.Uploading, sent, totalBytes)));
            var content = new StreamContent(stream, 1 << 16);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Headers.ContentLength = totalBytes;
            request.Content = content;
            return request;
        }, UploadTimeout(totalBytes), cancellationToken);

        var body = await ReadBodyAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new MeetingProcessingException(MapHttpFailure(response.StatusCode, body, "AssemblyAI could not accept the recording."));

        using var document = ParseJson(body);
        var uploadUrl = ReadString(document.RootElement, "upload_url");
        if (string.IsNullOrWhiteSpace(uploadUrl))
            throw new MeetingProcessingException("AssemblyAI did not return an upload location.");
        return uploadUrl;
    }

    private async Task<string> SubmitAsync(string audioUrl, string? languageCode, CancellationToken cancellationToken)
    {
        var payload = CreateTranscriptBody(audioUrl, languageCode, _configuration.SpeechModels);
        using var response = await SendAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v2/transcript")
            {
                Content = new StringContent(payload.ToJsonString(), System.Text.Encoding.UTF8, "application/json")
            };
            return request;
        }, TimeSpan.FromSeconds(60), cancellationToken);

        var body = await ReadBodyAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new MeetingProcessingException(MapHttpFailure(response.StatusCode, body, "AssemblyAI transcription failed."));

        using var document = ParseJson(body);
        var jobId = ReadString(document.RootElement, "id");
        if (string.IsNullOrWhiteSpace(jobId))
            throw new MeetingProcessingException("AssemblyAI did not return a transcription job.");

        var status = ReadString(document.RootElement, "status");
        if (string.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
        {
            throw new MeetingProcessingException(
                "AssemblyAI transcription failed. " + Sanitize(ReadString(document.RootElement, "error") ?? "The transcription job failed."));
        }

        return jobId;
    }

    private async Task<TranscriptResult> PollAsync(
        string jobId,
        int? audioDurationMs,
        IProgress<TranscriptionProgress>? progress,
        CancellationToken cancellationToken)
    {
        const int maxConsecutiveFailures = 5;
        var deadline = DateTimeOffset.UtcNow + PollDeadline(audioDurationMs);
        var consecutiveFailures = 0;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string body;
            try
            {
                using var response = await SendAsync(
                    () => new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/v2/transcript/{Uri.EscapeDataString(jobId)}"),
                    TimeSpan.FromSeconds(30),
                    cancellationToken);
                body = await ReadBodyAsync(response, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var failure = new MeetingProcessingException(MapHttpFailure(response.StatusCode, body, "AssemblyAI transcription failed."));
                    if (!IsTransient(response.StatusCode) || ++consecutiveFailures >= maxConsecutiveFailures)
                        throw failure;
                    await DelayAsync(consecutiveFailures, cancellationToken);
                    continue;
                }
            }
            catch (MeetingProcessingException exception) when (exception.InnerException is HttpRequestException
                || exception.Message.Contains("took too long", StringComparison.OrdinalIgnoreCase))
            {
                // A short network drop while waiting should not throw away a job that is
                // still running on AssemblyAI.
                if (++consecutiveFailures >= maxConsecutiveFailures) throw;
                await DelayAsync(consecutiveFailures, cancellationToken);
                continue;
            }

            consecutiveFailures = 0;
            using var document = ParseJson(body);
            var status = ReadString(document.RootElement, "status");
            if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
                return ParseCompleted(document.RootElement, jobId);
            if (string.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
            {
                throw new MeetingProcessingException(
                    "AssemblyAI transcription failed. " + Sanitize(ReadString(document.RootElement, "error") ?? "The transcription job failed."));
            }

            progress?.Report(new TranscriptionProgress(
                string.Equals(status, "queued", StringComparison.OrdinalIgnoreCase)
                    ? TranscriptionStage.Queued
                    : TranscriptionStage.Transcribing));

            if (_pollInterval > TimeSpan.Zero)
                await Task.Delay(_pollInterval, cancellationToken);
        }

        throw new MeetingProcessingException(
            "AssemblyAI is still transcribing this recording. The job is saved; retry in a few minutes to pick up the result.");
    }

    private async Task DelayAsync(int attempt, CancellationToken cancellationToken)
    {
        if (_pollInterval <= TimeSpan.Zero) return;
        await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 2 * attempt)), cancellationToken);
    }

    private static bool IsTransient(HttpStatusCode statusCode)
        => (int)statusCode is 408 or 429 or 500 or 502 or 503 or 504;

    private static TranscriptResult ParseCompleted(JsonElement root, string jobId)
    {
        var language = ReadString(root, "language_code");
        var model = ReadString(root, "speech_model_used");
        var text = ReadString(root, "text")?.Trim() ?? string.Empty;
        int? durationMs = null;
        if (root.TryGetProperty("audio_duration", out var durationElement) && durationElement.TryGetDouble(out var seconds))
            durationMs = (int)Math.Round(Math.Max(0, seconds) * 1000);

        var segments = new List<TranscriptSegmentResult>();
        if (root.TryGetProperty("utterances", out var utterances) && utterances.ValueKind == JsonValueKind.Array)
        {
            foreach (var utterance in utterances.EnumerateArray())
            {
                var utteranceText = ReadString(utterance, "text")?.Trim();
                if (string.IsNullOrWhiteSpace(utteranceText)) continue;
                segments.Add(new TranscriptSegmentResult(
                    ReadString(utterance, "speaker"),
                    ReadInt(utterance, "start"),
                    ReadInt(utterance, "end"),
                    utteranceText));
            }
        }

        if (segments.Count == 0 && !string.IsNullOrWhiteSpace(text))
        {
            segments.Add(new TranscriptSegmentResult(null, 0, durationMs ?? 0, text));
        }

        if (segments.Count == 0)
            throw new MeetingProcessingException("AssemblyAI did not return any transcript text.");

        if (string.IsNullOrWhiteSpace(text))
            text = string.Join(Environment.NewLine, segments.Select(segment => segment.Text));

        return new TranscriptResult(text, language, durationMs, segments, "assemblyai", jobId, model);
    }

    private async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> create,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var request = create();
        try
        {
            var apiKey = _configuration.ApiKey;
            if (!string.IsNullOrWhiteSpace(apiKey))
                request.Headers.TryAddWithoutValidation("authorization", apiKey);
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token);
        }
        catch (HttpRequestException exception)
        {
            throw new MeetingProcessingException(
                "AssemblyAI is not reachable. Check the network and try again.",
                exception);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new MeetingProcessingException("AssemblyAI took too long to respond. Retry processing.");
        }
        finally
        {
            request.Dispose();
        }
    }

    private static Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        => response.Content.ReadAsStringAsync(cancellationToken);

    private static JsonDocument ParseJson(string body)
    {
        try
        {
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        }
        catch (JsonException exception)
        {
            throw new MeetingProcessingException("AssemblyAI returned an unexpected transcription response.", exception);
        }
    }

    private static string MapHttpFailure(HttpStatusCode statusCode, string body, string fallback)
    {
        var message = ExtractProviderMessage(body);
        var status = (int)statusCode;
        if (status == 401)
            return "AssemblyAI rejected the API key. Check the key in Settings and retry.";
        if (status == 429)
            return "AssemblyAI is rate limiting requests. Wait a moment and retry.";
        if (status is 408 or 504)
            return "AssemblyAI took too long to respond. Retry processing.";
        if (ContainsAny(message, "corrupt", "unsupported", "invalid file", "could not be decoded", "no audio"))
            return "The recording audio could not be read. Your recording is still available; retry processing or record again.";

        var sanitized = Sanitize(message);
        return string.IsNullOrWhiteSpace(sanitized)
            ? $"{fallback} ({status})"
            : $"{fallback} {sanitized}";
    }

    private static string ExtractProviderMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                var direct = ReadString(root, "error");
                if (!string.IsNullOrWhiteSpace(direct)) return direct;
                if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                    return ReadString(error, "message") ?? string.Empty;
            }
        }
        catch (JsonException)
        {
        }

        return string.Empty;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null => null,
            _ => value.ToString()
        };
    }

    private static int ReadInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;

    internal static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var sanitized = Regex.Replace(value, @"https?://\S+", "[url]", RegexOptions.IgnoreCase);
        sanitized = Regex.Replace(sanitized, @"(?i)(api[_-]?key|bearer|authorization)\s*[:=]\s*\S+", "$1=[redacted]");
        sanitized = Regex.Replace(sanitized, @"sk-[A-Za-z0-9_-]{8,}", "[redacted]");
        sanitized = Regex.Replace(sanitized, @"\s+", " ").Trim();
        return sanitized.Length > 300 ? sanitized[..300] : sanitized;
    }

    private static bool ContainsAny(string value, params string[] tokens)
        => tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private sealed class ProgressReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly Action<long> _report;
        private long _sent;
        private long _lastReported;

        public ProgressReadStream(Stream inner, Action<long> report)
        {
            _inner = inner;
            _report = report;
        }

        public override bool CanRead => true;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set
            {
                _inner.Position = value;
                _sent = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count) => Track(_inner.Read(buffer, offset, count));

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => Track(await _inner.ReadAsync(buffer, cancellationToken));

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Track(await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken));

        private int Track(int read)
        {
            _sent += read;
            if (read == 0 || _sent - _lastReported >= 256 * 1024)
            {
                _lastReported = _sent;
                _report(_sent);
            }

            return read;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => _sent = _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
