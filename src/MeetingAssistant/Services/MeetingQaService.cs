using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MeetingAssistant.Models;

namespace MeetingAssistant.Services;

public sealed record QaCitation(string Label, TimeSpan Position, string? Excerpt);

public sealed record MeetingAnswer(string Answer, IReadOnlyList<QaCitation> Citations);

public readonly record struct QaExchange(string Question, string Answer);

public sealed class MeetingQaService
{
    public const string Instructions =
        """
        You answer questions about a single meeting.
        Use only the supplied transcript, notes, and earlier answers in this conversation.
        If the supplied text does not contain the answer, say that it was not discussed.
        Never invent a decision, owner, deadline, attendance fact, or real name.
        Do not infer a person's real name from a generic speaker label.
        Preserve technical terms, product names, code identifiers, URLs, and proper nouns.
        citations must be timestamps copied from the transcript lines you used, formatted mm:ss or h:mm:ss.
        Return an empty citations list when you cannot point at a line.
        Write the answer in the language of the question. If that language is unclear, use the meeting language.
        """;

    private const int ReservedTokens = 16_000;
    private const int MaxAttempts = 2;
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromMinutes(2);
    private static readonly Regex TimestampPattern = new(@"\b\d{1,2}:\d{2}(?::\d{2})?\b", RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions WireJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly OpenAiConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly int? _inputTokenBudget;
    private readonly TimeSpan _requestTimeout;
    private readonly TimeSpan _retryDelay;

    public MeetingQaService(
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
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(2);
    }

    public async Task<MeetingAnswer> AskAsync(
        Meeting meeting,
        string question,
        IReadOnlyList<QaExchange> history,
        CancellationToken cancellationToken = default)
    {
        if (!_configuration.IsConfigured)
        {
            throw new MeetingProcessingException(
                "Add an OpenAI API key in Settings to ask about this meeting.");
        }

        var trimmed = question.Trim();
        if (trimmed.Length == 0)
            throw new MeetingProcessingException("Enter a question about this meeting.");

        meeting.EnsureCollections();
        if (meeting.Transcript.Count == 0 && string.IsNullOrWhiteSpace(meeting.Summary.Overview))
            throw new MeetingProcessingException("There is no transcript to ask about yet.");

        var budget = _inputTokenBudget ?? Math.Max(4_000, MeetingSummaryService.ContextWindow(_configuration.SummaryModel) - ReservedTokens);
        var historyText = FormatHistory(history);
        var notes = FormatNotes(meeting);
        var transcriptBudget = Math.Max(400, budget - MeetingSummaryService.EstimateTokens(notes) - MeetingSummaryService.EstimateTokens(historyText));
        var context = SelectContext(trimmed, meeting.Transcript, transcriptBudget);
        var input = BuildInput(meeting, trimmed, notes, historyText, context, meeting.Transcript.Count);
        var payload = new JsonObject
        {
            ["model"] = _configuration.SummaryModel,
            ["store"] = false,
            ["max_output_tokens"] = 2_000,
            ["instructions"] = Instructions,
            ["input"] = input,
            ["text"] = new JsonObject
            {
                ["format"] = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["name"] = "meeting_answer",
                    ["strict"] = true,
                    ["schema"] = AnswerSchema()
                }
            }
        };
        if (_configuration.SummaryModel.StartsWith("gpt-6", StringComparison.OrdinalIgnoreCase))
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
                return ParseResponse(body, meeting.Transcript);

            var transient = failure is not null || status is 408 or 429 or 500 or 502 or 503 or 504;
            if (!transient || attempt >= MaxAttempts)
                throw failure ?? new MeetingProcessingException(MapFailure(status, body));

            if (_retryDelay > TimeSpan.Zero)
                await Task.Delay(_retryDelay * attempt, cancellationToken);
        }
    }

    internal static IReadOnlyList<TranscriptSegment> SelectContext(
        string question,
        IReadOnlyList<TranscriptSegment> transcript,
        int tokenBudget)
    {
        if (transcript.Count == 0)
            return transcript;

        if (MeetingSummaryService.EstimateTokens(FormatTranscript(transcript)) <= tokenBudget)
            return transcript;

        var terms = Tokenize(question);
        var cited = CitedPositions(question);
        var ranked = transcript
            .Select((segment, index) => (index, score: Score(segment, terms, cited)))
            .OrderByDescending(item => item.score)
            .ThenBy(item => item.index)
            .ToList();

        var chosen = new List<int>();
        var tokens = 0;
        if (ranked.Count > 0 && ranked[0].score > 0)
        {
            foreach (var item in ranked)
            {
                if (item.score <= 0)
                    break;
                if (!TryAdd(transcript, chosen, ref tokens, tokenBudget, item.index) && chosen.Count > 0)
                    break;
                TryAdd(transcript, chosen, ref tokens, tokenBudget, item.index - 1);
                TryAdd(transcript, chosen, ref tokens, tokenBudget, item.index + 1);
            }
        }

        if (chosen.Count == 0)
        {
            for (var index = 0; index < transcript.Count; index++)
            {
                if (!TryAdd(transcript, chosen, ref tokens, tokenBudget, index))
                    break;
            }
        }

        return chosen.OrderBy(index => index).Select(index => transcript[index]).ToList();
    }

    private static bool TryAdd(
        IReadOnlyList<TranscriptSegment> transcript,
        List<int> chosen,
        ref int tokens,
        int tokenBudget,
        int index)
    {
        if (index < 0 || index >= transcript.Count || chosen.Contains(index))
            return false;
        var cost = MeetingSummaryService.EstimateTokens(FormatLine(transcript[index]));
        if (tokens + cost > tokenBudget && chosen.Count > 0)
            return false;
        chosen.Add(index);
        tokens += cost;
        return true;
    }

    internal static MeetingAnswer ParseAnswer(string json, IReadOnlyList<TranscriptSegment> transcript)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new JsonException();

        var answer = root.TryGetProperty("answer", out var answerElement) && answerElement.ValueKind == JsonValueKind.String
            ? answerElement.GetString()?.Trim() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrWhiteSpace(answer))
            throw new JsonException();

        var citations = new List<QaCitation>();
        if (root.TryGetProperty("citations", out var citationElement) && citationElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in citationElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                    continue;
                var label = item.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(label) || !TryParseTimestamp(label, out var position))
                    continue;
                var cue = CueAt(transcript, position);
                var cuePosition = cue?.Start ?? position;
                citations.Add(new QaCitation(TranscriptCueSync.FormatClock(cuePosition), cuePosition, cue?.Text));
            }
        }

        return new MeetingAnswer(answer, citations);
    }

    internal static bool TryParseTimestamp(string label, out TimeSpan position)
    {
        position = default;
        var text = label.Trim().Trim('[', ']');
        var parts = text.Split(':');
        if (parts.Length is < 2 or > 3)
            return false;
        if (!parts.All(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            return false;

        var hours = 0;
        int minutes;
        int seconds;
        if (parts.Length == 3)
        {
            hours = int.Parse(parts[0], CultureInfo.InvariantCulture);
            minutes = int.Parse(parts[1], CultureInfo.InvariantCulture);
            seconds = int.Parse(parts[2], CultureInfo.InvariantCulture);
            if (minutes > 59)
                return false;
        }
        else
        {
            minutes = int.Parse(parts[0], CultureInfo.InvariantCulture);
            seconds = int.Parse(parts[1], CultureInfo.InvariantCulture);
        }

        if (seconds > 59)
            return false;
        position = new TimeSpan(hours, minutes, seconds);
        return true;
    }

    private static TranscriptSegment? CueAt(IReadOnlyList<TranscriptSegment> transcript, TimeSpan position)
        => TranscriptCueSync.CueAt(transcript, position)
            ?? transcript.FirstOrDefault(segment => segment.Start == position);

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
                "OpenAI is not reachable. Try asking again.",
                exception));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (0, string.Empty, new MeetingProcessingException(
                "OpenAI took too long to answer. Try asking again."));
        }
    }

    private static MeetingAnswer ParseResponse(string body, IReadOnlyList<TranscriptSegment> transcript)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("status", out var statusElement)
                && string.Equals(statusElement.GetString(), "incomplete", StringComparison.OrdinalIgnoreCase))
            {
                throw new MeetingProcessingException("The answer was cut off. Try asking again.");
            }

            var outputText = ExtractOutputText(document.RootElement);
            if (string.IsNullOrWhiteSpace(outputText))
                throw new JsonException();
            return ParseAnswer(outputText, transcript);
        }
        catch (MeetingProcessingException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new MeetingProcessingException(
                "The answer was not in the expected format. Try asking again.",
                exception);
        }
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
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var contentItem in content.EnumerateArray())
            {
                var type = contentItem.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
                if (string.Equals(type, "refusal", StringComparison.OrdinalIgnoreCase))
                    throw new MeetingProcessingException("OpenAI declined to answer from this meeting.");
                if (contentItem.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    parts.Add(text.GetString() ?? string.Empty);
            }
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static string MapFailure(int status, string body)
    {
        var message = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var messageElement))
                message = messageElement.GetString() ?? string.Empty;
        }
        catch (JsonException)
        {
        }

        if (status == 401)
            return "OpenAI rejected the API key. Check the key in Settings and try again.";
        if (status == 429)
            return "OpenAI is rate limiting requests. Wait a moment and try again.";
        var sanitized = AssemblyAiTranscriptionService.Sanitize(message);
        return string.IsNullOrWhiteSpace(sanitized)
            ? "OpenAI could not answer from this meeting. Try again."
            : $"OpenAI could not answer from this meeting. {sanitized}";
    }

    private static string BuildInput(
        Meeting meeting,
        string question,
        string notes,
        string history,
        IReadOnlyList<TranscriptSegment> context,
        int transcriptCount)
    {
        var builder = new StringBuilder();
        builder.Append("Meeting title: ").AppendLine(meeting.Title);
        if (!string.IsNullOrWhiteSpace(meeting.DetectedLanguage))
            builder.Append("Language: ").AppendLine(meeting.DetectedLanguage);
        builder.AppendLine(notes);
        if (!string.IsNullOrWhiteSpace(history))
        {
            builder.AppendLine();
            builder.AppendLine("Earlier questions:");
            builder.AppendLine(history);
        }

        builder.AppendLine();
        builder.AppendLine("Transcript:");
        builder.AppendLine(context.Count == 0 ? "(no transcript lines)" : FormatTranscript(context));
        if (context.Count < transcriptCount)
            builder.AppendLine("The transcript above is the portion most related to the question, in time order.");
        builder.AppendLine();
        builder.Append("Question: ").Append(question);
        return builder.ToString();
    }

    private static string FormatNotes(Meeting meeting)
    {
        var summary = meeting.Summary;
        var builder = new StringBuilder();
        builder.Append("Overview: ").AppendLine(summary.Overview);
        if (summary.Decisions.Count > 0)
            builder.Append("Decisions: ").AppendLine(string.Join("; ", summary.Decisions));
        if (summary.ActionItems.Count > 0)
            builder.Append("Action items: ").AppendLine(string.Join("; ", summary.ActionItems.Select(item => item.Text)));
        if (summary.Questions.Count > 0)
            builder.Append("Open questions: ").AppendLine(string.Join("; ", summary.Questions));
        return builder.ToString().TrimEnd();
    }

    private static string FormatHistory(IReadOnlyList<QaExchange> history)
    {
        if (history.Count == 0)
            return string.Empty;
        return string.Join(Environment.NewLine, history.TakeLast(6).Select(turn => $"Q: {turn.Question}{Environment.NewLine}A: {turn.Answer}"));
    }

    internal static string FormatTranscript(IEnumerable<TranscriptSegment> transcript)
        => string.Join(Environment.NewLine, transcript.Select(FormatLine));

    internal static string FormatLine(TranscriptSegment segment)
        => $"[{TranscriptCueSync.FormatClock(segment.Start)}] {segment.SpeakerName}: {segment.Text}";

    private static int Score(TranscriptSegment segment, IReadOnlyList<string> terms, IReadOnlyList<TimeSpan> cited)
    {
        var haystack = $"{segment.SpeakerName} {segment.Text}";
        var score = 0;
        foreach (var term in terms)
        {
            if (haystack.Contains(term, StringComparison.CurrentCultureIgnoreCase))
                score += 2;
        }

        foreach (var position in cited)
        {
            if (position >= segment.Start && position < TranscriptCueSync.CueEnd(segment))
                score += 5;
        }

        return score;
    }

    private static List<string> Tokenize(string question)
    {
        var terms = new List<string>();
        var current = new StringBuilder();
        foreach (var character in question)
        {
            if (char.IsLetterOrDigit(character))
            {
                current.Append(character);
                continue;
            }

            AddTerm(terms, current);
        }

        AddTerm(terms, current);
        return terms;
    }

    private static void AddTerm(List<string> terms, StringBuilder current)
    {
        if (current.Length >= 2)
            terms.Add(current.ToString());
        current.Clear();
    }

    private static List<TimeSpan> CitedPositions(string question)
    {
        var positions = new List<TimeSpan>();
        foreach (Match match in TimestampPattern.Matches(question))
        {
            if (TryParseTimestamp(match.Value, out var position))
                positions.Add(position);
        }

        return positions;
    }

    private static JsonObject AnswerSchema()
        => new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray(JsonValue.Create("answer"), JsonValue.Create("citations")),
            ["properties"] = new JsonObject
            {
                ["answer"] = new JsonObject { ["type"] = "string" },
                ["citations"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string" }
                }
            }
        };
}
