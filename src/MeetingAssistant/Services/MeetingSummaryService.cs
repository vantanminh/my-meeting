using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using MeetingAssistant.Models;

namespace MeetingAssistant.Services;

public sealed record MeetingNotes(string Title, MeetingSummary Summary);

public sealed record SummaryProgress(int CompletedParts, int TotalParts, bool Merging);

public sealed class MeetingSummaryService
{
    public const string Instructions =
        """
        You are a meeting analysis system.
        Analyze only the supplied meeting transcript.
        Your job is to extract factual meeting information without adding information that was not said.
        Produce:
        - a concise overall summary
        - important discussion points
        - confirmed decisions
        - actionable tasks
        - task owner when explicitly stated
        - deadline when explicitly stated
        - unresolved questions
        Rules:
        - Never invent a decision.
        - Never invent an owner.
        - Never invent a deadline.
        - Never infer a participant's real name from a generic speaker label.
        - Preserve technical terms, product names, code identifiers, URLs and proper nouns.
        - Treat suggestions and ideas as discussion unless participants clearly agree to them.
        - If information is uncertain, omit it or return null.
        - Keep action items specific and actionable.
        - Write the result in the dominant language of the meeting unless the product explicitly requests another language.
        - Do not translate technical terms into another language.
        - importantMoments only when a moment was explicitly emphasized; otherwise return an empty list.
        - deadlines only when a date or deadline was explicitly stated; otherwise return an empty list.
        """;

    private const int ReservedTokens = 24_000;
    private const int MaxAttempts = 2;
    private const int MaxParallelRequests = 3;
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromMinutes(8);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WireJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly OpenAiConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly int? _inputTokenBudget;
    private readonly TimeSpan _requestTimeout;
    private readonly TimeSpan _retryDelay;

    public MeetingSummaryService(
        OpenAiConfiguration configuration,
        HttpClient httpClient,
        int? inputTokenBudget = null,
        TimeSpan? requestTimeout = null,
        TimeSpan? retryDelay = null)
    {
        _configuration = configuration;
        _httpClient = httpClient;
        _inputTokenBudget = inputTokenBudget;
        _requestTimeout = requestTimeout ?? DefaultRequestTimeout;
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(3);
    }

    public async Task<MeetingNotes> SummarizeAsync(
        string title,
        string? language,
        IReadOnlyList<TranscriptSegment> transcript,
        CancellationToken cancellationToken = default,
        IProgress<SummaryProgress>? progress = null)
    {
        if (!_configuration.IsConfigured)
        {
            throw new MeetingProcessingException(
                "Add an OpenAI API key in Settings to generate meeting notes. The transcript is saved.");
        }

        if (transcript.Count == 0)
            throw new MeetingProcessingException("There is no transcript to summarize yet.");

        var budget = _inputTokenBudget ?? Math.Max(4_000, ContextWindow(_configuration.SummaryModel) - ReservedTokens);
        var fullText = FormatTranscript(transcript);
        if (EstimateTokens(fullText) <= budget)
            return await RequestAsync(title, language, fullText, merge: false, cancellationToken);

        var chunks = PackChunks(transcript, budget);
        var completed = 0;
        progress?.Report(new SummaryProgress(0, chunks.Count, Merging: false));
        var partials = await RunLimitedAsync(chunks.Select((chunk, index) => (Func<Task<MeetingNotes>>)(async () =>
        {
            var partTitle = $"{title} (part {index + 1} of {chunks.Count})";
            var notes = await RequestAsync(partTitle, language, FormatTranscript(chunk), merge: false, cancellationToken);
            progress?.Report(new SummaryProgress(Interlocked.Increment(ref completed), chunks.Count, Merging: false));
            return notes;
        })).ToList(), cancellationToken);

        progress?.Report(new SummaryProgress(chunks.Count, chunks.Count, Merging: true));
        return await MergeAsync(title, language, partials, budget, cancellationToken);
    }

    private static async Task<List<T>> RunLimitedAsync<T>(IReadOnlyList<Func<Task<T>>> work, CancellationToken cancellationToken)
    {
        using var gate = new SemaphoreSlim(MaxParallelRequests);
        var tasks = work.Select(async item =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return await item();
            }
            finally
            {
                gate.Release();
            }
        }).ToList();
        return (await Task.WhenAll(tasks)).ToList();
    }

    private async Task<MeetingNotes> MergeAsync(
        string title,
        string? language,
        IReadOnlyList<MeetingNotes> partials,
        int budget,
        CancellationToken cancellationToken)
    {
        var pending = partials.ToList();
        while (pending.Count > 1)
        {
            var groups = new List<List<MeetingNotes>>();
            var current = new List<MeetingNotes>();
            var tokens = 0;
            foreach (var partial in pending)
            {
                var serialized = JsonSerializer.Serialize(ToPayload(partial), JsonOptions);
                var cost = EstimateTokens(serialized);
                if (current.Count > 0 && tokens + cost > budget)
                {
                    groups.Add(current);
                    current = [];
                    tokens = 0;
                }

                current.Add(partial);
                tokens += cost;
            }

            if (current.Count > 0) groups.Add(current);
            if (groups.Count == pending.Count && groups.All(group => group.Count == 1))
            {
                var pairs = new List<Func<Task<MeetingNotes>>>();
                for (var index = 0; index < pending.Count; index += 2)
                {
                    if (index + 1 >= pending.Count)
                    {
                        var single = pending[index];
                        pairs.Add(() => Task.FromResult(single));
                        continue;
                    }

                    var pairedInput = JsonSerializer.Serialize(
                        new[] { ToPayload(pending[index]), ToPayload(pending[index + 1]) },
                        JsonOptions);
                    pairs.Add(() => RequestAsync(title, language, pairedInput, merge: true, cancellationToken));
                }

                pending = await RunLimitedAsync(pairs, cancellationToken);
                continue;
            }

            var merges = groups.Select(group => (Func<Task<MeetingNotes>>)(() =>
            {
                if (group.Count == 1) return Task.FromResult(group[0]);
                var input = JsonSerializer.Serialize(group.Select(ToPayload), JsonOptions);
                return RequestAsync(title, language, input, merge: true, cancellationToken);
            })).ToList();
            pending = await RunLimitedAsync(merges, cancellationToken);
        }

        return pending[0];
    }

    private async Task<MeetingNotes> RequestAsync(
        string title,
        string? language,
        string content,
        bool merge,
        CancellationToken cancellationToken)
    {
        var model = _configuration.SummaryModel;
        var instructions = new StringBuilder(Instructions);
        if (!string.IsNullOrWhiteSpace(language))
        {
            instructions.AppendLine();
            instructions.Append("The transcript language code is \"");
            instructions.Append(language.Trim());
            instructions.AppendLine("\".");
            instructions.AppendLine("Write the notes in that language. Do not translate technical terms, product names, code identifiers, URLs, or proper nouns.");
        }

        if (merge)
        {
            instructions.AppendLine();
            instructions.AppendLine("Merge these structured meeting extracts into one result.");
            instructions.AppendLine("Keep every confirmed decision, action item, and question.");
            instructions.AppendLine("Do not drop items and do not invent new ones.");
            instructions.AppendLine("Deduplicate overlaps.");
        }
        else if (title.Contains("(part ", StringComparison.Ordinal))
        {
            instructions.AppendLine();
            instructions.AppendLine("This is one time-ordered part of a longer meeting. Extract only facts stated in this part.");
        }

        var input = merge
            ? $"Meeting title: {title}{Environment.NewLine}{Environment.NewLine}Structured extracts:{Environment.NewLine}{content}"
            : $"Meeting title: {title}{Environment.NewLine}{Environment.NewLine}Transcript:{Environment.NewLine}{content}";

        var payload = new JsonObject
        {
            ["model"] = model,
            ["store"] = false,
            ["max_output_tokens"] = 12_000,
            ["instructions"] = instructions.ToString(),
            ["input"] = input,
            ["text"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["name"] = "meeting_notes",
                    ["strict"] = true,
                    ["schema"] = NotesSchema()
                }
            }
        };
        if (model.StartsWith("gpt-6", StringComparison.OrdinalIgnoreCase))
        {
            payload["reasoning"] = new JsonObject
            {
                ["effort"] = "low"
            };
        }

        var wireBody = payload.ToJsonString(WireJsonOptions);
        for (var attempt = 1; ; attempt++)
        {
            var (status, body, failure) = await SendOnceAsync(wireBody, cancellationToken);
            if (failure is null && status is >= 200 and < 300)
                return ParseResponse(body);

            var transient = failure is not null || status is 408 or 429 or 500 or 502 or 503 or 504;
            if (!transient || attempt >= MaxAttempts)
                throw failure ?? new MeetingProcessingException(MapSummaryFailure(status, body));

            MeetingProcessingLog.Write("meeting_summary_retry", "-", "openai", "retrying", null, failure?.Message ?? status.ToString());
            if (_retryDelay > TimeSpan.Zero)
                await Task.Delay(_retryDelay * attempt, cancellationToken);
        }
    }

    private async Task<(int Status, string Body, MeetingProcessingException? Failure)> SendOnceAsync(
        string wireBody,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses")
        {
            Content = new StringContent(wireBody, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _configuration.ApiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_requestTimeout);
        try
        {
            using var response = await _httpClient.SendAsync(request, timeout.Token);
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return ((int)response.StatusCode, body, null);
        }
        catch (HttpRequestException exception)
        {
            return (0, string.Empty, new MeetingProcessingException(
                "OpenAI is not reachable. The transcript is saved; retry to generate notes.",
                exception));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (0, string.Empty, new MeetingProcessingException(
                "OpenAI took too long to respond. The transcript is saved; retry to generate notes."));
        }
    }

    private static MeetingNotes ParseResponse(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("status", out var statusElement)
                && string.Equals(statusElement.GetString(), "incomplete", StringComparison.OrdinalIgnoreCase))
            {
                throw new MeetingProcessingException(
                    "The meeting notes were cut off before they could be saved. The transcript is saved; retry to generate notes.");
            }

            var outputText = ExtractOutputText(document.RootElement);
            if (string.IsNullOrWhiteSpace(outputText))
                throw new JsonException();
            return ParseNotes(outputText);
        }
        catch (MeetingProcessingException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new MeetingProcessingException(
                "The meeting notes were not in the expected format. The transcript is saved; retry to generate notes.",
                exception);
        }
    }

    internal static MeetingNotes ParseNotes(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException();

        var summary = new MeetingSummary
        {
            Overview = ReadString(root, "summary") ?? string.Empty,
            KeyPoints = ReadStringList(root, "keyPoints"),
            Questions = ReadStringList(root, "openQuestions"),
            ImportantMoments = ReadStringList(root, "importantMoments")
        };

        if (root.TryGetProperty("decisions", out var decisions) && decisions.ValueKind == JsonValueKind.Array)
        {
            foreach (var decision in decisions.EnumerateArray())
            {
                if (decision.ValueKind == JsonValueKind.String)
                {
                    var text = decision.GetString()?.Trim();
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    summary.DecisionItems.Add(new DecisionItem { Content = text });
                    summary.Decisions.Add(text);
                    continue;
                }

                var content = ReadString(decision, "content")?.Trim();
                if (string.IsNullOrWhiteSpace(content)) continue;
                var speaker = BlankToNull(ReadString(decision, "speaker"));
                summary.DecisionItems.Add(new DecisionItem { Content = content, Speaker = speaker });
                summary.Decisions.Add(string.IsNullOrWhiteSpace(speaker) ? content : $"{speaker}: {content}");
            }
        }

        if (root.TryGetProperty("actionItems", out var actions) && actions.ValueKind == JsonValueKind.Array)
        {
            foreach (var action in actions.EnumerateArray())
            {
                var task = (ReadString(action, "task") ?? ReadString(action, "text"))?.Trim();
                if (string.IsNullOrWhiteSpace(task)) continue;
                var owner = BlankToNull(ReadString(action, "owner"));
                var deadline = BlankToNull(ReadString(action, "deadline") ?? ReadString(action, "due"));
                summary.ActionItems.Add(new ActionItem
                {
                    Text = task,
                    Owner = owner ?? string.Empty,
                    Due = deadline ?? string.Empty
                });
                if (!string.IsNullOrWhiteSpace(deadline))
                {
                    summary.Deadlines.Add(new DeadlineItem
                    {
                        Label = task,
                        Date = deadline,
                        Owner = owner ?? string.Empty
                    });
                }
            }
        }

        if (root.TryGetProperty("deadlines", out var deadlines) && deadlines.ValueKind == JsonValueKind.Array)
        {
            foreach (var deadline in deadlines.EnumerateArray())
            {
                var label = ReadString(deadline, "label")?.Trim();
                var date = BlankToNull(ReadString(deadline, "date"));
                if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(date)) continue;
                var owner = BlankToNull(ReadString(deadline, "owner")) ?? string.Empty;
                var duplicate = summary.Deadlines.Any(existing =>
                    string.Equals(existing.Label, label, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(existing.Date, date, StringComparison.OrdinalIgnoreCase));
                if (duplicate) continue;
                summary.Deadlines.Add(new DeadlineItem { Label = label, Date = date, Owner = owner });
            }
        }

        summary.EnsureCollections();
        return new MeetingNotes((ReadString(root, "title") ?? string.Empty).Trim(), summary);
    }

    internal static List<List<TranscriptSegment>> PackChunks(IReadOnlyList<TranscriptSegment> transcript, int tokenBudget)
    {
        var chunks = new List<List<TranscriptSegment>>();
        var current = new List<TranscriptSegment>();
        var tokens = 0;
        foreach (var segment in transcript)
        {
            var cost = EstimateTokens(FormatLine(segment));
            if (current.Count > 0 && tokens + cost > tokenBudget)
            {
                chunks.Add(current);
                current = [];
                tokens = 0;
            }

            current.Add(segment);
            tokens += cost;
        }

        if (current.Count > 0) chunks.Add(current);
        return chunks;
    }

    internal static int EstimateTokens(string text) => Math.Max(1, text.Length / 4);

    public static int ContextWindow(string model)
    {
        if (model.Contains("gpt-6-luna", StringComparison.OrdinalIgnoreCase)) return 1_050_000;
        if (model.StartsWith("gpt-6", StringComparison.OrdinalIgnoreCase)) return 1_050_000;
        if (model.Contains("gpt-4.1", StringComparison.OrdinalIgnoreCase)) return 1_047_576;
        return 128_000;
    }

    private static string FormatTranscript(IEnumerable<TranscriptSegment> transcript)
        => string.Join(Environment.NewLine, transcript.Select(FormatLine));

    private static string FormatLine(TranscriptSegment segment)
        => $"[{segment.Timestamp}] {segment.SpeakerName}: {segment.Text}";

    private static object ToPayload(MeetingNotes notes)
        => new
        {
            title = notes.Title,
            summary = notes.Summary.Overview,
            keyPoints = notes.Summary.KeyPoints,
            decisions = notes.Summary.DecisionItems.Select(item => new { content = item.Content, speaker = item.Speaker }),
            actionItems = notes.Summary.ActionItems.Select(item => new
            {
                task = item.Text,
                owner = string.IsNullOrWhiteSpace(item.Owner) ? null : item.Owner,
                deadline = string.IsNullOrWhiteSpace(item.Due) ? null : item.Due
            }),
            openQuestions = notes.Summary.Questions,
            importantMoments = notes.Summary.ImportantMoments,
            deadlines = notes.Summary.Deadlines.Select(item => new { label = item.Label, date = item.Date, owner = item.Owner })
        };

    private static JsonObject NotesSchema()
    {
        JsonObject NullableString() => new()
        {
            ["type"] = new JsonArray(JsonValue.Create("string"), JsonValue.Create("null"))
        };
        JsonObject StringList() => new()
        {
            ["type"] = "array",
            ["items"] = new JsonObject { ["type"] = "string" }
        };
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray(
                JsonValue.Create("title"),
                JsonValue.Create("summary"),
                JsonValue.Create("keyPoints"),
                JsonValue.Create("decisions"),
                JsonValue.Create("actionItems"),
                JsonValue.Create("deadlines"),
                JsonValue.Create("openQuestions"),
                JsonValue.Create("importantMoments")),
            ["properties"] = new JsonObject
            {
                ["title"] = new JsonObject { ["type"] = "string" },
                ["summary"] = new JsonObject { ["type"] = "string" },
                ["keyPoints"] = StringList(),
                ["decisions"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = new JsonArray(JsonValue.Create("content"), JsonValue.Create("speaker")),
                        ["properties"] = new JsonObject
                        {
                            ["content"] = new JsonObject { ["type"] = "string" },
                            ["speaker"] = NullableString()
                        }
                    }
                },
                ["actionItems"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = new JsonArray(JsonValue.Create("task"), JsonValue.Create("owner"), JsonValue.Create("deadline")),
                        ["properties"] = new JsonObject
                        {
                            ["task"] = new JsonObject { ["type"] = "string" },
                            ["owner"] = NullableString(),
                            ["deadline"] = NullableString()
                        }
                    }
                },
                ["deadlines"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = new JsonArray(JsonValue.Create("label"), JsonValue.Create("date"), JsonValue.Create("owner")),
                        ["properties"] = new JsonObject
                        {
                            ["label"] = new JsonObject { ["type"] = "string" },
                            ["date"] = NullableString(),
                            ["owner"] = NullableString()
                        }
                    }
                },
                ["openQuestions"] = StringList(),
                ["importantMoments"] = StringList()
            }
        };
    }

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
                var type = contentItem.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
                if (string.Equals(type, "refusal", StringComparison.OrdinalIgnoreCase))
                    throw new MeetingProcessingException(
                        "The meeting notes were not in the expected format. The transcript is saved; retry to generate notes.");
                if (contentItem.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    parts.Add(text.GetString() ?? string.Empty);
            }
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static string MapSummaryFailure(int status, string body)
    {
        var message = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            if (document.RootElement.TryGetProperty("error", out var error))
                message = error.TryGetProperty("message", out var messageElement) ? messageElement.GetString() ?? string.Empty : string.Empty;
        }
        catch (JsonException)
        {
        }

        if (status == 401)
            return "OpenAI rejected the API key. The transcript is saved; check the key in Settings and retry.";
        if (status == 429)
            return "OpenAI is rate limiting requests. The transcript is saved; wait a moment and retry.";
        var sanitized = AssemblyAiTranscriptionService.Sanitize(message);
        return string.IsNullOrWhiteSpace(sanitized)
            ? "OpenAI could not generate meeting notes. The transcript is saved; retry to generate notes."
            : $"OpenAI could not generate meeting notes. The transcript is saved. {sanitized}";
    }

    private static List<string> ReadStringList(JsonElement element, string name)
    {
        var values = new List<string>();
        if (!element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
            return values;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) continue;
            var text = item.GetString()?.Trim();
            if (!string.IsNullOrWhiteSpace(text)) values.Add(text);
        }

        return values;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static string? BlankToNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return IsUnknown(trimmed) ? null : trimmed;
    }

    private static bool IsUnknown(string value)
        => value.Equals("null", StringComparison.OrdinalIgnoreCase)
            || value.Equals("n/a", StringComparison.OrdinalIgnoreCase)
            || value.Equals("na", StringComparison.OrdinalIgnoreCase)
            || value.Equals("none", StringComparison.OrdinalIgnoreCase)
            || value.Equals("unknown", StringComparison.OrdinalIgnoreCase)
            || value.Equals("unassigned", StringComparison.OrdinalIgnoreCase)
            || value.Equals("tbd", StringComparison.OrdinalIgnoreCase)
            || value.Equals("no date", StringComparison.OrdinalIgnoreCase)
            || value.Equals("no deadline", StringComparison.OrdinalIgnoreCase)
            || value.Equals("không rõ", StringComparison.OrdinalIgnoreCase)
            || value.Equals("chưa rõ", StringComparison.OrdinalIgnoreCase);
}
