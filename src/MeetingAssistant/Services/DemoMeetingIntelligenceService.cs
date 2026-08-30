using MeetingAssistant.Models;

namespace MeetingAssistant.Services;

/// <summary>
/// Deterministic local processing adapter. It demonstrates the full processing state
/// machine and is ready to be replaced by Speech-to-Text plus an AI provider.
/// </summary>
public sealed class DemoMeetingIntelligenceService : IMeetingIntelligenceService
{
    public string ProviderLabel => "Local demo";

    public async Task<ProcessingResult> ProcessAsync(
        RecordingData recording,
        IProgress<ProcessingProgress> progress,
        CancellationToken cancellationToken = default)
    {
        var stages = new[]
        {
            ("Preparing audio", "Checking microphone and system audio tracks"),
            ("Transcribing", "Turning conversation into a timestamped transcript"),
            ("Recognizing speakers", "Matching voices and grouping speaker turns"),
            ("Finding the signal", "Extracting decisions, owners, and deadlines"),
            ("Finishing meeting", "Saving your local meeting workspace")
        };

        for (var stageIndex = 0; stageIndex < stages.Length; stageIndex++)
        {
            var (stage, message) = stages[stageIndex];
            for (var step = 0; step <= 4; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var percent = (int)Math.Round(((stageIndex * 5d + step) / (stages.Length * 5d)) * 100);
                progress.Report(new ProcessingProgress(percent, stageIndex, stage, message));
                await Task.Delay(260, cancellationToken);
            }
        }

        var you = new SpeakerProfile { Id = "recording-you", Name = "You", Role = "Host", AccentColor = "#66E3C0", Meetings = 1 };
        var guest = new SpeakerProfile { Id = "recording-guest", Name = "Guest speaker", Role = "Participant", AccentColor = "#88A9FF", Meetings = 1 };
        var transcript = new List<TranscriptSegment>
        {
            new() { SpeakerId = you.Id, SpeakerName = you.Name, Start = TimeSpan.FromSeconds(4), End = TimeSpan.FromSeconds(29), Text = "Thanks for joining. I wanted to use this time to align on the next step and leave with a clear owner." },
            new() { SpeakerId = guest.Id, SpeakerName = guest.Name, Start = TimeSpan.FromSeconds(42), End = TimeSpan.FromSeconds(79), Text = "The strongest option is to keep the first version small, validate it with a few people, and then expand once the workflow feels reliable." },
            new() { SpeakerId = you.Id, SpeakerName = you.Name, Start = TimeSpan.FromSeconds(96), End = TimeSpan.FromSeconds(132), Text = "That sounds right. I will write the brief and share it tomorrow so we can review it asynchronously." }
        };

        var meeting = new Meeting
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = string.IsNullOrWhiteSpace(recording.Title) ? "Untitled meeting" : recording.Title.Trim(),
            StartedAt = recording.StartedAt,
            Duration = recording.Duration,
            ParticipantCount = 2,
            Status = MeetingStatus.Ready,
            SyncStatus = "Local cache · ready to sync",
            AudioSources = $"{recording.Configuration.Microphone} + {recording.Configuration.SystemAudio}",
            Speakers = [you, guest],
            Transcript = transcript,
            Summary = new MeetingSummary
            {
                Overview = "A focused conversation with a clear next step. The group agreed to keep the first version small, validate the workflow, and review the brief asynchronously.",
                KeyPoints = ["Start with a small, testable first version.", "Validate the workflow with a few people before expanding.", "Keep the review async once the brief is shared."],
                Decisions = ["Proceed with a focused first version and validate before broadening scope."],
                ActionItems = [new() { Text = "Write and share the project brief", Owner = "You", Due = "Tomorrow" }],
                Deadlines = [new() { Label = "Project brief", Date = "Tomorrow", Owner = "You" }],
                Questions = ["Who should be included in the first validation group?"],
                ImportantMoments = ["01:18 · The group converged on a small, testable first version.", "01:36 · You committed to share the brief tomorrow."]
            }
        };

        progress.Report(new ProcessingProgress(100, stages.Length - 1, "Ready", "Your meeting is ready to review"));
        meeting.Notes = "Preview only · this transcript was not generated from your audio.";
        return new ProcessingResult(meeting);
    }

    public Task<MeetingSummary> SummarizeAsync(Meeting meeting, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        meeting.EnsureCollections();
        return Task.FromResult(meeting.Summary);
    }
}
