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

    private readonly string _meetingsPath = Path.Combine(AppPaths.DataDirectory, "meetings.json");

    public async Task<IReadOnlyList<Meeting>> LoadAsync()
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);

        if (!File.Exists(_meetingsPath))
        {
            var seeded = SeedMeetings();
            await SaveAsync(seeded);
            return seeded;
        }

        try
        {
            await using var stream = File.OpenRead(_meetingsPath);
            var meetings = await JsonSerializer.DeserializeAsync<List<Meeting>>(stream, JsonOptions);
            return meetings is { Count: > 0 } ? meetings : SeedMeetings();
        }
        catch (JsonException)
        {
            // A broken cache should never prevent the user from entering the app.
            return SeedMeetings();
        }
    }

    public async Task SaveAsync(IReadOnlyCollection<Meeting> meetings)
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var temporaryPath = $"{_meetingsPath}.{Guid.NewGuid():N}.tmp";

        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, meetings, JsonOptions);
        }

        File.Move(temporaryPath, _meetingsPath, overwrite: true);
    }

    private static List<Meeting> SeedMeetings()
    {
        var now = DateTimeOffset.Now;
        var priya = new SpeakerProfile { Id = "spk-priya", Name = "Priya Shah", Role = "Product", AccentColor = "#88A9FF", Meetings = 8 };
        var marcus = new SpeakerProfile { Id = "spk-marcus", Name = "Marcus Lee", Role = "Engineering", AccentColor = "#FF977E", Meetings = 6 };
        var jamie = new SpeakerProfile { Id = "spk-jamie", Name = "Jamie Chen", Role = "Design", AccentColor = "#F3C878", Meetings = 5 };
        var alex = new SpeakerProfile { Id = "spk-alex", Name = "You", Role = "Host", AccentColor = "#66E3C0", Meetings = 12 };

        var roadmap = new Meeting
        {
            Id = "seed-roadmap",
            Title = "Q3 product roadmap sync",
            StartedAt = now.AddDays(-1).AddHours(-2),
            Duration = TimeSpan.FromMinutes(42).Add(TimeSpan.FromSeconds(18)),
            ParticipantCount = 6,
            AudioSources = "MacBook microphone + system audio",
            Speakers = [alex, priya, marcus, jamie],
            Summary = new MeetingSummary
            {
                Overview = "The team aligned on a focused Q3 launch: improve activation, ship the shared workspace beta, and measure first-value time. Engineering will protect two weeks for reliability work before the public rollout.",
                KeyPoints = [
                    "Activation is the primary Q3 metric; target a 20% improvement in first-week completion.",
                    "The shared workspace beta will launch to 10 design partners before general availability.",
                    "Reliability work is scheduled ahead of the public launch to keep the experience trustworthy."
                ],
                Decisions = [
                    "Use first-value time as the north-star metric for the onboarding refresh.",
                    "Keep the beta invite-only and collect structured feedback after each session."
                ],
                ActionItems = [
                    new() { Text = "Draft the activation experiment brief", Owner = "Alex Morgan", Due = "Tomorrow" },
                    new() { Text = "Prepare the partner beta onboarding checklist", Owner = "Priya Shah", Due = "Fri, Aug 30" },
                    new() { Text = "Reserve a reliability sprint before launch", Owner = "Marcus Lee", Due = "Next week" }
                ],
                Deadlines = [
                    new() { Label = "Design partner beta", Date = "Sep 12", Owner = "Priya Shah" },
                    new() { Label = "Reliability sprint", Date = "Sep 16–27", Owner = "Marcus Lee" }
                ],
                Questions = ["Which activation moment should be surfaced in the empty state?", "Do we need a separate admin role for beta workspaces?"],
                ImportantMoments = ["04:12 · The team aligned on first-value time as the north-star.", "17:08 · Reliability work was protected ahead of launch."]
            },
            Transcript = [
                new() { SpeakerId = alex.Id, SpeakerName = alex.Name, Start = TimeSpan.FromSeconds(8), End = TimeSpan.FromSeconds(31), Text = "Thanks everyone. I want to leave today with one clear activation goal and a small, testable beta plan." },
                new() { SpeakerId = priya.Id, SpeakerName = priya.Name, Start = TimeSpan.FromSeconds(45), End = TimeSpan.FromSeconds(76), Text = "The biggest drop is between creating a workspace and inviting a first teammate. We can make that step feel much more immediate." },
                new() { SpeakerId = marcus.Id, SpeakerName = marcus.Name, Start = TimeSpan.FromSeconds(93), End = TimeSpan.FromSeconds(133), Text = "The shared workspace is close, but I would like two weeks to harden sync and conflict handling before we open it up." },
                new() { SpeakerId = jamie.Id, SpeakerName = jamie.Name, Start = TimeSpan.FromSeconds(152), End = TimeSpan.FromSeconds(191), Text = "For the beta, I can make the first-run checklist contextual and collect feedback without adding another modal." },
                new() { SpeakerId = alex.Id, SpeakerName = alex.Name, Start = TimeSpan.FromSeconds(224), End = TimeSpan.FromSeconds(267), Text = "Great. Let's call first-value time the north-star, keep the beta to ten partners, and protect the reliability sprint." }
            ]
        };

        var design = new Meeting
        {
            Id = "seed-design",
            Title = "Workspace design critique",
            StartedAt = now.AddDays(-4).AddHours(-1),
            Duration = TimeSpan.FromMinutes(28).Add(TimeSpan.FromSeconds(44)),
            ParticipantCount = 4,
            AudioSources = "Default microphone + system audio",
            Speakers = [alex, jamie, priya],
            Summary = new MeetingSummary
            {
                Overview = "A review of the workspace navigation and meeting review surfaces. The group favored a calm information hierarchy with the transcript and summary visible together.",
                KeyPoints = ["Keep meeting review in one continuous surface.", "Use progressive disclosure for advanced capture settings.", "Show sync status beside the meeting title."],
                Decisions = ["Use a two-column review layout on wide screens.", "Keep speaker colors consistent across meetings."],
                ActionItems = [new() { Text = "Polish the empty state illustration", Owner = "Jamie Chen", Due = "Mon, Sep 2" }, new() { Text = "Validate the review layout at 125% scaling", Owner = "Alex Morgan", Due = "No date" }],
                Deadlines = [new() { Label = "Review-ready prototype", Date = "Sep 4", Owner = "Jamie Chen" }],
                Questions = ["Should action items support recurring due dates?"],
                ImportantMoments = ["09:40 · Review shifted from dashboard to one continuous surface."]
            },
            Transcript = [
                new() { SpeakerId = jamie.Id, SpeakerName = jamie.Name, Start = TimeSpan.FromSeconds(12), End = TimeSpan.FromSeconds(47), Text = "The review view should feel like one place to understand what happened, not another dashboard to learn." },
                new() { SpeakerId = priya.Id, SpeakerName = priya.Name, Start = TimeSpan.FromSeconds(69), End = TimeSpan.FromSeconds(105), Text = "I agree. The action items are the bridge between the transcript and the work that follows." },
                new() { SpeakerId = alex.Id, SpeakerName = alex.Name, Start = TimeSpan.FromSeconds(131), End = TimeSpan.FromSeconds(176), Text = "Let's keep the summary visible as the transcript scrolls, then make deeper controls available when someone needs them." }
            ]
        };

        var retro = new Meeting
        {
            Id = "seed-retro",
            Title = "July team retro",
            StartedAt = now.AddDays(-11),
            Duration = TimeSpan.FromMinutes(36).Add(TimeSpan.FromSeconds(2)),
            ParticipantCount = 7,
            AudioSources = "Default microphone + system audio",
            Speakers = [alex, priya, marcus],
            Summary = new MeetingSummary
            {
                Overview = "The team celebrated faster reviews and identified notification noise as the main drag on focus. A small experiment will test batching updates by project.",
                KeyPoints = ["Review turnaround improved after async context was added.", "Notification volume is still interrupting deep work.", "Batching updates by project is the next experiment."],
                Decisions = ["Run a two-week notification batching experiment."],
                ActionItems = [new() { Text = "Define notification batching rules", Owner = "Marcus Lee", Due = "Complete" }],
                Deadlines = [],
                Questions = ["Which notifications should remain immediate?"],
                ImportantMoments = ["22:05 · The team chose a two-week batching experiment."]
            },
            Transcript = [new() { SpeakerId = priya.Id, SpeakerName = priya.Name, Start = TimeSpan.FromSeconds(22), End = TimeSpan.FromSeconds(65), Text = "The async review context is working. The thing I still notice is how often notifications pull me out of focus." }]
        };

        return [roadmap, design, retro];
    }
}
