using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Globalization;
using MeetingAssistant.Models;

namespace MeetingAssistant.Services;

/// <summary>
/// Firestore REST adapter with a local-save fallback. The repository writes the local
/// copy after this call, so a network error never prevents a meeting from being saved.
/// </summary>
public sealed class FirebaseCloudSyncService : ICloudSyncService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly FirebaseConfiguration _configuration;
    private readonly IAuthService _auth;
    private readonly LocalCloudSyncService _localFallback = new();
    private readonly HttpClient _httpClient;

    public FirebaseCloudSyncService(FirebaseConfiguration configuration, IAuthService auth, HttpClient? httpClient = null)
    {
        _configuration = configuration;
        _auth = auth;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    }

    public bool IsPaused
    {
        get => _localFallback.IsPaused;
        set => _localFallback.IsPaused = value;
    }

    public string StatusLabel
    {
        get
        {
            if (IsPaused) return "Sync paused";
            if (!_configuration.IsConfigured) return "Local workspace";
            return _auth.CurrentSession?.AccessToken is not null ? "Firebase sync" : "Firebase ready";
        }
    }

    public async Task<IReadOnlyList<Meeting>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (IsPaused || !_configuration.IsConfigured || _auth.CurrentSession?.AccessToken is null)
            return [];

        var session = _auth.CurrentSession;
        var meetings = new List<Meeting>();
        string? pageToken = null;

        try
        {
            do
            {
                var query = string.IsNullOrWhiteSpace(pageToken)
                    ? "?pageSize=100"
                    : $"?pageSize=100&pageToken={Uri.EscapeDataString(pageToken)}";
                var endpoint = DocumentsEndpoint(session) + query;
                using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode) return [];

                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                var root = document.RootElement;
                if (root.TryGetProperty("documents", out var documents) && documents.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in documents.EnumerateArray())
                    {
                        var meeting = ParseMeeting(item);
                        if (meeting is not null) meetings.Add(meeting);
                    }
                }

                pageToken = root.TryGetProperty("nextPageToken", out var nextPageToken)
                    ? nextPageToken.GetString()
                    : null;
            }
            while (!string.IsNullOrWhiteSpace(pageToken));

            return meetings;
        }
        catch (HttpRequestException)
        {
            return [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return [];
        }
    }

    public async Task<SyncResult> SyncAsync(Meeting meeting, CancellationToken cancellationToken = default)
    {
        if (IsPaused || !_configuration.IsConfigured || _auth.CurrentSession?.AccessToken is null)
            return await _localFallback.SyncAsync(meeting, cancellationToken);

        var session = _auth.CurrentSession;
        try
        {
            var endpoint = $"{DocumentsEndpoint(session)}/{Uri.EscapeDataString(meeting.Id)}";
            var fields = new Dictionary<string, object>
            {
                ["title"] = StringValue(meeting.Title),
                ["startedAt"] = TimestampValue(meeting.StartedAt.UtcDateTime),
                ["updatedAt"] = TimestampValue(meeting.UpdatedAt.UtcDateTime),
                ["durationSeconds"] = DoubleValue(meeting.Duration.TotalSeconds),
                ["participantCount"] = DoubleValue(meeting.ParticipantCount),
                ["audioSources"] = StringValue(meeting.AudioSources),
                ["transcriptJson"] = StringValue(JsonSerializer.Serialize(meeting.Transcript)),
                ["summaryJson"] = StringValue(JsonSerializer.Serialize(meeting.Summary)),
                ["speakerJson"] = StringValue(JsonSerializer.Serialize(meeting.Speakers))
            };
            using var request = new HttpRequestMessage(HttpMethod.Patch, endpoint)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { fields }), Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
                return new SyncResult(true, "Synced to Firebase", "Meeting is available across your Windows devices");

            return new SyncResult(true, "Saved locally", "Firebase sync will retry when the connection is healthy");
        }
        catch (HttpRequestException)
        {
            return new SyncResult(true, "Saved locally", "Firebase is unavailable; sync will retry later");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SyncResult(true, "Saved locally", "Firebase took too long; sync will retry later");
        }
    }

    private static Dictionary<string, object> StringValue(string value)
        => new() { ["stringValue"] = value };

    private static Dictionary<string, object> DoubleValue(double value)
        => new() { ["doubleValue"] = value };

    private static Dictionary<string, object> TimestampValue(DateTime value)
        => new() { ["timestampValue"] = value.ToString("O") };

    private string DocumentsEndpoint(UserSession session)
        => $"https://firestore.googleapis.com/v1/projects/{Uri.EscapeDataString(_configuration.ProjectId!)}/databases/(default)/documents/users/{Uri.EscapeDataString(session.UserId)}/meetings";

    private static Meeting? ParseMeeting(JsonElement document)
    {
        if (!document.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Object)
            return null;

        var startedAt = ReadTimestamp(fields, "startedAt") ?? DateTimeOffset.Now;
        var meeting = new Meeting
        {
            Id = ReadDocumentId(document),
            Title = ReadString(fields, "title") ?? "Untitled meeting",
            StartedAt = startedAt,
            UpdatedAt = ReadTimestamp(fields, "updatedAt") ?? startedAt,
            Duration = TimeSpan.FromSeconds(ReadNumber(fields, "durationSeconds")),
            ParticipantCount = (int)Math.Round(ReadNumber(fields, "participantCount")),
            AudioSources = ReadString(fields, "audioSources") ?? "Microphone + system audio",
            SyncStatus = "Synced to Firebase",
            Status = MeetingStatus.Ready,
            Transcript = ReadJson<List<TranscriptSegment>>(fields, "transcriptJson") ?? [],
            Summary = ReadJson<MeetingSummary>(fields, "summaryJson") ?? new MeetingSummary(),
            Speakers = ReadJson<List<SpeakerProfile>>(fields, "speakerJson") ?? []
        };

        meeting.Summary.KeyPoints ??= [];
        meeting.Summary.Decisions ??= [];
        meeting.Summary.ActionItems ??= [];
        meeting.Summary.Deadlines ??= [];
        meeting.Summary.Questions ??= [];
        meeting.Summary.ImportantMoments ??= [];
        return meeting;
    }

    private static string ReadDocumentId(JsonElement document)
        => document.TryGetProperty("name", out var name)
            ? name.GetString()?.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? Guid.NewGuid().ToString("N")
            : Guid.NewGuid().ToString("N");

    private static string? ReadString(JsonElement fields, string propertyName)
        => fields.TryGetProperty(propertyName, out var field) && field.TryGetProperty("stringValue", out var value)
            ? value.GetString()
            : null;

    private static double ReadNumber(JsonElement fields, string propertyName)
    {
        if (!fields.TryGetProperty(propertyName, out var field)) return 0;
        if (field.TryGetProperty("doubleValue", out var doubleValue))
        {
            if (doubleValue.ValueKind == JsonValueKind.Number && doubleValue.TryGetDouble(out var number)) return number;
            if (double.TryParse(doubleValue.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return number;
        }

        if (field.TryGetProperty("integerValue", out var integerValue)
            && long.TryParse(integerValue.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            return integer;
        return 0;
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement fields, string propertyName)
    {
        if (!fields.TryGetProperty(propertyName, out var field)
            || !field.TryGetProperty("timestampValue", out var value)
            || !DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
            return null;
        return timestamp;
    }

    private static T? ReadJson<T>(JsonElement fields, string propertyName)
    {
        var value = ReadString(fields, propertyName);
        if (string.IsNullOrWhiteSpace(value)) return default;
        try
        {
            return JsonSerializer.Deserialize<T>(value, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
