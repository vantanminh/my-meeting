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
            var existing = await TryReadCompletedJobAsync(request.ExistingJobId, cancellationToken);
            if (existing is not null) return existing;
        }

        var uploadUrl = await UploadAsync(request.AudioPath, cancellationToken);
        var jobId = await SubmitAsync(uploadUrl, request.LanguageCode, cancellationToken);
        if (request.JobSubmitted is not null)
            await request.JobSubmitted(jobId, cancellationToken);
        return await PollAsync(jobId, cancellationToken);
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

    private async Task<TranscriptResult?> TryReadCompletedJobAsync(string jobId, CancellationToken cancellationToken)
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

        return await PollAsync(jobId, cancellationToken);
    }

    private async Task<string> UploadAsync(string path, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v2/upload");
            var stream = File.OpenRead(path);
            var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            request.Content = content;
            return request;
        }, TimeSpan.FromMinutes(15), cancellationToken);

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

    private async Task<TranscriptResult> PollAsync(string jobId, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddHours(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var response = await SendAsync(
                () => new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/v2/transcript/{Uri.EscapeDataString(jobId)}"),
                TimeSpan.FromSeconds(30),
                cancellationToken);
            var body = await ReadBodyAsync(response, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new MeetingProcessingException(MapHttpFailure(response.StatusCode, body, "AssemblyAI transcription failed."));

            using var document = ParseJson(body);
            var status = ReadString(document.RootElement, "status");
            if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
                return ParseCompleted(document.RootElement, jobId);
            if (string.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
            {
                throw new MeetingProcessingException(
                    "AssemblyAI transcription failed. " + Sanitize(ReadString(document.RootElement, "error") ?? "The transcription job failed."));
            }

            if (_pollInterval > TimeSpan.Zero)
                await Task.Delay(_pollInterval, cancellationToken);
        }

        throw new MeetingProcessingException("AssemblyAI took too long to respond. Retry processing.");
    }

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
}
