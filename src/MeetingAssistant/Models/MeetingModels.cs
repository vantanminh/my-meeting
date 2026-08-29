using System.Text.Json.Serialization;

namespace MeetingAssistant.Models;

public enum MeetingStatus
{
    Ready,
    Processing,
    Recording,
    Failed
}

public sealed class UserSession
{
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsOffline { get; set; }

    [JsonIgnore]
    public string? AccessToken { get; set; }

    [JsonIgnore]
    public string? RefreshToken { get; set; }

    [JsonIgnore]
    public string Initials
    {
        get
        {
            var parts = DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length switch
            {
                0 => "MA",
                1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant(),
                _ => string.Concat(parts[0][0], parts[^1][0]).ToUpperInvariant()
            };
        }
    }
}

public sealed class Meeting
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Untitled meeting";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public TimeSpan Duration { get; set; }
    public int ParticipantCount { get; set; }
    public MeetingStatus Status { get; set; } = MeetingStatus.Ready;
    public string SyncStatus { get; set; } = "Local cache";
    public string AudioSources { get; set; } = "Microphone + system audio";
    public List<SpeakerProfile> Speakers { get; set; } = [];
    public List<TranscriptSegment> Transcript { get; set; } = [];
    public MeetingSummary Summary { get; set; } = new();
    public string? Notes { get; set; }

    [JsonIgnore]
    public string DurationLabel => Duration.TotalHours >= 1
        ? Duration.ToString(@"h\:mm\:ss")
        : Duration.ToString(@"m\:ss");

    [JsonIgnore]
    public string DateLabel
    {
        get
        {
            var date = StartedAt.LocalDateTime.Date;
            if (date == DateTime.Today) return "Today";
            if (date == DateTime.Today.AddDays(-1)) return "Yesterday";
            return StartedAt.ToLocalTime().ToString("MMM d, yyyy");
        }
    }
}

public sealed class SpeakerProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Unknown speaker";
    public string Role { get; set; } = "Participant";
    public string AccentColor { get; set; } = "#88A9FF";
    public int Meetings { get; set; }
    public string? VoiceNote { get; set; }
}

public sealed class TranscriptSegment
{
    public string SpeakerId { get; set; } = string.Empty;
    public string SpeakerName { get; set; } = "Unknown speaker";
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
    public string Text { get; set; } = string.Empty;

    [JsonIgnore]
    public string Timestamp => Start.ToString(@"mm\:ss");
}

public sealed class MeetingSummary
{
    public string Overview { get; set; } = string.Empty;
    public List<string> KeyPoints { get; set; } = [];
    public List<string> Decisions { get; set; } = [];
    public List<ActionItem> ActionItems { get; set; } = [];
    public List<DeadlineItem> Deadlines { get; set; } = [];
    public List<string> Questions { get; set; } = [];
    public List<string> ImportantMoments { get; set; } = [];
}

public sealed class ActionItem
{
    public string Text { get; set; } = string.Empty;
    public string Owner { get; set; } = "Unassigned";
    public string Due { get; set; } = "No date";
    public bool IsComplete { get; set; }
}

public sealed class DeadlineItem
{
    public string Label { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string Owner { get; set; } = "Unassigned";
}

public sealed class AudioConfiguration
{
    public string Microphone { get; set; } = "Default microphone";
    public string SystemAudio { get; set; } = "Default system audio";
    public string Quality { get; set; } = "Balanced · 48 kHz";
    public string Title { get; set; } = "Untitled meeting";
    public bool KeepLocalCopy { get; set; } = true;
}

public sealed class RecordingData
{
    public string Title { get; init; } = "Untitled meeting";
    public DateTimeOffset StartedAt { get; init; }
    public TimeSpan Duration { get; init; }
    public AudioConfiguration Configuration { get; init; } = new();
    public string? MicrophonePath { get; init; }
    public string? SystemAudioPath { get; init; }
}
