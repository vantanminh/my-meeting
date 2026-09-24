using System.IO;
using MeetingAssistant.Models;
using Microsoft.Win32;

namespace MeetingAssistant.Services;

public interface IStartupRegistration
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}

public sealed class WindowsStartupRegistration : IStartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MeetingAssistant";

    public bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
            }
            catch
            {
                return false;
            }
        }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null) return;
            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return;
            }

            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable)) return;
            key.SetValue(ValueName, $"\"{executable}\"", RegistryValueKind.String);
        }
        catch
        {
            // Startup registration is best-effort on locked or non-Windows profiles.
        }
    }
}

public static class RetentionPolicy
{
    public static TimeSpan? RetentionFor(string option)
    {
        if (option.Contains("7 days", StringComparison.OrdinalIgnoreCase))
            return TimeSpan.FromDays(7);
        if (option.Contains("30 days", StringComparison.OrdinalIgnoreCase))
            return TimeSpan.FromDays(30);
        return null;
    }

    public static int Sweep(string recordingsDirectory, TimeSpan? retention, DateTimeOffset now)
    {
        if (retention is null || !Directory.Exists(recordingsDirectory))
            return 0;

        var removed = 0;
        foreach (var directory in Directory.GetDirectories(recordingsDirectory))
        {
            DateTimeOffset stamp;
            try
            {
                stamp = Directory.GetCreationTimeUtc(directory);
            }
            catch (IOException)
            {
                continue;
            }

            if (now - stamp < retention) continue;
            try
            {
                Directory.Delete(directory, recursive: true);
                removed++;
            }
            catch (IOException)
            {
            }
        }

        return removed;
    }

    public static long RecordingBytes(string recordingsDirectory)
    {
        if (!Directory.Exists(recordingsDirectory)) return 0;
        try
        {
            return Directory.EnumerateFiles(recordingsDirectory, "*", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path).Length)
                .Sum();
        }
        catch (IOException)
        {
            return 0;
        }
    }
}

public static class RecordingSafety
{
    public static void DeleteSessionAudio(RecordingData? recording)
    {
        if (recording is null) return;
        DeleteIfExists(recording.MicrophonePath);
        DeleteIfExists(recording.SystemAudioPath);
        if (!string.IsNullOrWhiteSpace(recording.SessionDirectory) && Directory.Exists(recording.SessionDirectory))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(recording.SessionDirectory).Any())
                    Directory.Delete(recording.SessionDirectory);
            }
            catch (IOException)
            {
            }
        }
    }

    public static void DeleteMeetingAudio(Meeting meeting)
    {
        DeleteIfExists(meeting.MicrophonePath);
        DeleteIfExists(meeting.SystemAudioPath);
        if (!string.IsNullOrWhiteSpace(meeting.SessionDirectory) && Directory.Exists(meeting.SessionDirectory))
        {
            try { Directory.Delete(meeting.SessionDirectory, recursive: true); }
            catch (IOException) { }
        }

        meeting.MicrophonePath = null;
        meeting.SystemAudioPath = null;
        meeting.SessionDirectory = null;
    }

    private static void DeleteIfExists(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        try { File.Delete(path); }
        catch (IOException) { }
    }
}

public static class DiskBudget
{
    public static long AvailableBytes(string directory)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(directory));
            if (string.IsNullOrWhiteSpace(root)) return long.MaxValue;
            var info = new DriveInfo(root);
            return info.IsReady ? info.AvailableFreeSpace : long.MaxValue;
        }
        catch
        {
            return long.MaxValue;
        }
    }

    public static string? WarningFor(long availableBytes, TimeSpan? duration)
    {
        if (availableBytes < 80L * 1024 * 1024)
            return "There is not enough free disk space to start a safe recording.";
        if (availableBytes < 500L * 1024 * 1024)
            return "Disk space is low. Long recordings may fail.";
        if (duration is { TotalHours: >= 1 })
            return "Long recordings increase transcription cost and are split into 25 MB chunks.";
        return null;
    }

    public static TimeSpan RemainingRecordingTime(long availableBytes)
    {
        const long bytesPerSecond = 48_000L * 4 * 2 * 2;
        var usable = Math.Max(0, availableBytes - (80L * 1024 * 1024));
        return TimeSpan.FromSeconds(usable / (double)bytesPerSecond);
    }
}

public sealed class ExportResult
{
    public ExportResult(string markdown, string plainText, string json)
    {
        Markdown = markdown;
        PlainText = plainText;
        Json = json;
    }

    public string Markdown { get; }
    public string PlainText { get; }
    public string Json { get; }
}

public static class MeetingExport
{
    public static ExportResult Build(Meeting meeting)
    {
        meeting.EnsureCollections();
        var lines = new List<string>
        {
            $"# {meeting.Title}",
            "",
            $"{meeting.DateLabel} · {meeting.DurationLabel} · {meeting.AudioSources}",
            "",
            "## Overview",
            meeting.Summary.Overview,
            "",
            "## Key points"
        };
        lines.AddRange(meeting.Summary.KeyPoints.Select(item => $"- {item}"));
        lines.Add("");
        lines.Add("## Decisions");
        lines.AddRange(meeting.Summary.Decisions.Select(item => $"- {item}"));
        lines.Add("");
        lines.Add("## Action items");
        lines.AddRange(meeting.Summary.ActionItems.Select(item => $"- [{(item.IsComplete ? "x" : " ")}] {item.Text} ({item.Owner}, {item.Due})"));
        lines.Add("");
        lines.Add("## Transcript");
        lines.AddRange(meeting.Transcript.Select(segment => $"[{segment.Timestamp}] {segment.SpeakerName}: {segment.Text}"));

        var markdown = string.Join(Environment.NewLine, lines);
        var plain = string.Join(Environment.NewLine, meeting.Transcript.Select(segment => $"{segment.SpeakerName}: {segment.Text}"));
        var json = System.Text.Json.JsonSerializer.Serialize(meeting, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        return new ExportResult(markdown, plain, json);
    }

    public static async Task WriteAsync(Meeting meeting, string path)
    {
        var export = Build(meeting);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var content = extension switch
        {
            ".json" => export.Json,
            ".txt" => export.PlainText,
            _ => export.Markdown
        };
        await File.WriteAllTextAsync(path, content);
    }
}

public static class TranscriptRepair
{
    public static IReadOnlyList<TranscriptSegment> CollapseOverlaps(
        IReadOnlyList<TranscriptSegment> segments,
        TimeSpan? window = null)
    {
        var tolerance = window ?? TimeSpan.FromSeconds(1.2);
        var ordered = segments.OrderBy(segment => segment.Start).ThenBy(segment => segment.SpeakerName).ToList();
        var kept = new List<TranscriptSegment>();

        foreach (var segment in ordered)
        {
            var duplicate = kept.LastOrDefault(existing =>
                existing.SpeakerId != segment.SpeakerId
                && Math.Abs((existing.Start - segment.Start).TotalSeconds) <= tolerance.TotalSeconds
                && Similar(existing.Text, segment.Text));

            if (duplicate is null)
            {
                kept.Add(segment);
                continue;
            }

            if (IsPreferredHost(segment) && !IsPreferredHost(duplicate))
            {
                kept.Remove(duplicate);
                kept.Add(segment);
            }
        }

        return kept.OrderBy(segment => segment.Start).ToList();
    }

    private static bool IsPreferredHost(TranscriptSegment segment)
        => segment.SpeakerName.Contains("You", StringComparison.OrdinalIgnoreCase)
            || segment.SpeakerId.Contains("you", StringComparison.OrdinalIgnoreCase);

    private static bool Similar(string left, string right)
    {
        if (string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;
        var a = Normalize(left);
        var b = Normalize(right);
        if (a.Length == 0 || b.Length == 0) return false;
        return a.Contains(b, StringComparison.OrdinalIgnoreCase) || b.Contains(a, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value)
        => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}

public static class GreetingCopy
{
    public static string TimeOfDay(DateTimeOffset now)
        => now.LocalDateTime.Hour switch
        {
            < 12 => "Good morning, ",
            < 18 => "Good afternoon, ",
            _ => "Good evening, "
        };
}

public static class WeekStats
{
    public static int CountThisWeek(IEnumerable<Meeting> meetings, DateTime today)
    {
        var start = today.AddDays(-(int)today.DayOfWeek);
        if (today.DayOfWeek == DayOfWeek.Sunday)
            start = today.AddDays(-6);
        else
            start = today.AddDays(1 - (int)today.DayOfWeek);
        return meetings.Count(meeting => meeting.StartedAt.LocalDateTime.Date >= start && meeting.StartedAt.LocalDateTime.Date <= today);
    }
}
