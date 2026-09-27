using System.Globalization;
using System.IO;
using MeetingAssistant.Models;
using NAudio.Wave;

namespace MeetingAssistant.Services;

public static class RecordingRecovery
{
    public const string InterruptedMessage =
        "Processing stopped before it finished. The recording is still saved. Retry to continue.";

    public static int MarkInterrupted(IEnumerable<Meeting> meetings)
    {
        var changed = 0;
        foreach (var meeting in meetings)
        {
            meeting.EnsureCollections();
            if (meeting.ProcessingPhase is not (ProcessingPhase.Queued or ProcessingPhase.Transcribing or ProcessingPhase.Summarizing))
                continue;

            meeting.Status = MeetingStatus.Failed;
            meeting.ProcessingPhase = ProcessingPhase.Failed;
            meeting.ProcessingError = InterruptedMessage;
            meeting.SyncStatus = string.IsNullOrWhiteSpace(meeting.SyncStatus) ? "Saved locally" : meeting.SyncStatus;
            meeting.UpdatedAt = DateTimeOffset.Now;
            changed++;
        }

        return changed;
    }

    public static IReadOnlyList<Meeting> RecoverMissing(string? recordingsDirectory, IEnumerable<Meeting> existing)
    {
        if (string.IsNullOrWhiteSpace(recordingsDirectory) || !Directory.Exists(recordingsDirectory))
            return [];

        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var meeting in existing)
        {
            Remember(known, meeting.SessionDirectory);
            Remember(known, ParentDirectory(meeting.MicrophonePath));
            Remember(known, ParentDirectory(meeting.SystemAudioPath));
        }

        var recovered = new List<Meeting>();
        foreach (var directory in Directory.EnumerateDirectories(recordingsDirectory))
        {
            var fullDirectory = Path.GetFullPath(directory);
            if (known.Contains(fullDirectory)) continue;

            var microphone = Path.Combine(fullDirectory, "microphone.wav");
            var systemAudio = Path.Combine(fullDirectory, "system-audio.wav");
            var microphoneUsable = IsUsableAudio(microphone);
            var systemUsable = IsUsableAudio(systemAudio);
            if (!microphoneUsable && !systemUsable) continue;

            var startedAt = StartedAtFromFolder(fullDirectory);
            var duration = TimeSpan.Zero;
            if (microphoneUsable) duration = Max(duration, DurationOf(microphone));
            if (systemUsable) duration = Max(duration, DurationOf(systemAudio));

            var meeting = new Meeting
            {
                Title = "Recovered meeting · " + startedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                StartedAt = startedAt,
                Duration = duration,
                Status = MeetingStatus.Failed,
                ProcessingPhase = ProcessingPhase.Failed,
                ProcessingError = InterruptedMessage,
                SyncStatus = "Saved locally",
                SyncState = SyncState.Local,
                AudioSources = "Recovered recording",
                SessionDirectory = fullDirectory,
                MicrophonePath = microphoneUsable ? microphone : null,
                SystemAudioPath = systemUsable ? systemAudio : null,
                UpdatedAt = DateTimeOffset.Now
            };
            meeting.EnsureCollections();
            recovered.Add(meeting);
        }

        return recovered;
    }

    public static bool IsUsableAudio(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path) && new FileInfo(path).Length > 44;

    private static void Remember(HashSet<string> known, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            known.Add(Path.GetFullPath(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }
    }

    private static string? ParentDirectory(string? path)
        => string.IsNullOrWhiteSpace(path) ? null : Path.GetDirectoryName(path);

    private static DateTimeOffset StartedAtFromFolder(string directory)
    {
        var name = Path.GetFileName(directory);
        var stamp = name.Length >= 15 ? name[..15] : name;
        if (DateTime.TryParseExact(stamp, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed))
            return new DateTimeOffset(parsed);

        return new DateTimeOffset(Directory.GetCreationTime(directory));
    }

    private static TimeSpan DurationOf(string path)
    {
        try
        {
            using var reader = new WaveFileReader(path);
            return reader.TotalTime < TimeSpan.Zero ? TimeSpan.Zero : reader.TotalTime;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or FormatException or ArgumentException or UnauthorizedAccessException)
        {
            return TimeSpan.Zero;
        }
    }

    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left >= right ? left : right;
}
