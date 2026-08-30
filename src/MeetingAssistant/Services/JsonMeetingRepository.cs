using System.IO;
using System.Text.Json;
using MeetingAssistant.Models;

namespace MeetingAssistant.Services;

public sealed class JsonMeetingRepository : IMeetingRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string LastQuarantinePath { get; private set; } = string.Empty;

    public async Task<IReadOnlyList<Meeting>> LoadAsync()
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var meetingsPath = AppPaths.MeetingsPath;

        if (!File.Exists(meetingsPath))
            return [];

        try
        {
            var json = await ProtectedWorkspaceStore.ReadAsync(meetingsPath);
            var meetings = JsonSerializer.Deserialize<List<Meeting>>(json, JsonOptions);
            if (meetings is null)
                return [];

            foreach (var meeting in meetings)
                meeting.EnsureCollections();

            return meetings;
        }
        catch (JsonException)
        {
            LastQuarantinePath = Quarantine(meetingsPath);
            return [];
        }
        catch (JsonWorkspaceException)
        {
            LastQuarantinePath = Quarantine(meetingsPath);
            return [];
        }
        catch (IOException)
        {
            LastQuarantinePath = Quarantine(meetingsPath);
            return [];
        }
    }

    public async Task SaveAsync(IReadOnlyCollection<Meeting> meetings)
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var meetingsPath = AppPaths.MeetingsPath;
        var temporaryPath = $"{meetingsPath}.{Guid.NewGuid():N}.tmp";
        var json = JsonSerializer.Serialize(meetings, JsonOptions);
        await ProtectedWorkspaceStore.WriteAsync(temporaryPath, json);
        File.Move(temporaryPath, meetingsPath, overwrite: true);
    }

    public static string Quarantine(string meetingsPath)
    {
        if (!File.Exists(meetingsPath))
            return string.Empty;

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var quarantinePath = $"{meetingsPath}.bak-{stamp}";
        try
        {
            File.Move(meetingsPath, quarantinePath, overwrite: false);
            return quarantinePath;
        }
        catch (IOException)
        {
            try
            {
                File.Copy(meetingsPath, quarantinePath, overwrite: true);
                File.Delete(meetingsPath);
                return quarantinePath;
            }
            catch (IOException)
            {
                return meetingsPath;
            }
        }
    }

    public static List<Meeting> CreateSampleMeetings()
    {
        var now = DateTimeOffset.Now;
        var priya = new SpeakerProfile { Id = "spk-priya", Name = "Priya Shah", Role = "Product", AccentColor = "#88A9FF", Meetings = 8 };
        var marcus = new SpeakerProfile { Id = "spk-marcus", Name = "Marcus Lee", Role = "Engineering", AccentColor = "#FF977E", Meetings = 6 };
        var jamie = new SpeakerProfile { Id = "spk-jamie", Name = "Jamie Chen", Role = "Design", AccentColor = "#F3C878", Meetings = 5 };
        var alex = new SpeakerProfile { Id = "spk-alex", Name = "You", Role = "Host", AccentColor = "#66E3C0", Meetings = 12 };

        return
        [
            new Meeting
            {
                Id = "seed-roadmap",
                Title = "Q3 product roadmap sync",
                StartedAt = now.AddDays(-1).AddHours(-2),
                Duration = TimeSpan.FromMinutes(42).Add(TimeSpan.FromSeconds(18)),
                ParticipantCount = 4,
                AudioSources = "Default microphone + system audio",
                Speakers = [alex, priya, marcus, jamie],
                Summary = new MeetingSummary
                {
                    Overview = "The team aligned on a focused Q3 launch.",
                    KeyPoints = ["Activation is the primary Q3 metric."],
                    Decisions = ["Keep the beta invite-only."],
                    ActionItems = [new() { Text = "Draft the activation experiment brief", Owner = "Alex Morgan", Due = "Tomorrow" }],
                    Deadlines = [new() { Label = "Design partner beta", Date = "Sep 12", Owner = "Priya Shah" }],
                    Questions = ["Which activation moment should be surfaced?"],
                    ImportantMoments = ["04:12 · The team aligned on first-value time."]
                },
                Transcript =
                [
                    new() { SpeakerId = alex.Id, SpeakerName = alex.Name, Start = TimeSpan.FromSeconds(8), End = TimeSpan.FromSeconds(31), Text = "I want to leave today with one clear activation goal." }
                ]
            }
        ];
    }
}
