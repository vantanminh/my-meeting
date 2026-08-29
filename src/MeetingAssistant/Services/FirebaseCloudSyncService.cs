using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MeetingAssistant.Models;

namespace MeetingAssistant.Services;

/// <summary>
/// Firestore REST adapter with a local-save fallback. The repository writes the local
/// copy after this call, so a network error never prevents a meeting from being saved.
/// </summary>
public sealed class FirebaseCloudSyncService : ICloudSyncService
{
    private readonly FirebaseConfiguration _configuration;
    private readonly IAuthService _auth;
    private readonly LocalCloudSyncService _localFallback = new();
    private readonly HttpClient _httpClient = new();

    public FirebaseCloudSyncService(FirebaseConfiguration configuration, IAuthService auth)
    {
        _configuration = configuration;
        _auth = auth;
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

    public async Task<SyncResult> SyncAsync(Meeting meeting, CancellationToken cancellationToken = default)
    {
        if (IsPaused || !_configuration.IsConfigured || _auth.CurrentSession?.AccessToken is null)
            return await _localFallback.SyncAsync(meeting, cancellationToken);

        var session = _auth.CurrentSession;
        try
        {
            var endpoint = $"https://firestore.googleapis.com/v1/projects/{Uri.EscapeDataString(_configuration.ProjectId!)}/databases/(default)/documents/users/{Uri.EscapeDataString(session.UserId)}/meetings/{Uri.EscapeDataString(meeting.Id)}";
            var fields = new Dictionary<string, object>
            {
                ["title"] = StringValue(meeting.Title),
                ["startedAt"] = TimestampValue(meeting.StartedAt.UtcDateTime),
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
}
