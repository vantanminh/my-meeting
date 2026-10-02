using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MeetingAssistant.Models;

namespace MeetingAssistant.Services;

/// <summary>
/// Speaker ids belong to one meeting. "Speaker A" in Monday's call and "Speaker A" in
/// Tuesday's call are different people, so renaming one must never rename the other.
/// </summary>
public static class SpeakerIdentity
{
    private static readonly Regex GenericLabel = new(
        @"^(speaker|unknown speaker|người nói)(\s*[\p{L}\p{N}]{1,3})?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string For(string meetingId, string speakerKey)
        => "spk-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(meetingId + ":" + speakerKey))).ToLowerInvariant()[..12];

    /// <summary>True for labels the transcription provider made up, such as "Speaker A" or "Speaker 2".</summary>
    public static bool IsGenericName(string? name)
        => string.IsNullOrWhiteSpace(name) || GenericLabel.IsMatch(name.Trim());

    /// <summary>Re-keys every speaker in <paramref name="meeting"/> to an id scoped to that meeting.</summary>
    public static void Scope(Meeting meeting)
    {
        var map = meeting.Speakers.Select(profile => profile.Id)
            .Concat(meeting.Transcript.Select(segment => segment.SpeakerId))
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(id => id, id => For(meeting.Id, id), StringComparer.Ordinal);
        Apply(meeting, map);
    }

    /// <summary>
    /// Splits speaker ids that are shared by more than one meeting, which older versions
    /// created by hashing only the label. Deterministic, so running it again changes nothing.
    /// Returns the meetings that were changed.
    /// </summary>
    public static IReadOnlyList<Meeting> Isolate(IEnumerable<Meeting> meetings)
    {
        var list = meetings.ToList();
        var idsByMeeting = list.ToDictionary(
            meeting => meeting,
            meeting => meeting.Speakers.Select(profile => profile.Id)
                .Concat(meeting.Transcript.Select(segment => segment.SpeakerId))
                .Where(id => !string.IsNullOrEmpty(id))
                .ToHashSet(StringComparer.Ordinal));
        var shared = idsByMeeting.Values
            .SelectMany(ids => ids)
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (shared.Count == 0) return [];

        var changed = new List<Meeting>();
        foreach (var meeting in list)
        {
            var map = idsByMeeting[meeting]
                .Where(shared.Contains)
                .ToDictionary(id => id, id => For(meeting.Id, id), StringComparer.Ordinal);
            if (map.Count == 0) continue;
            Apply(meeting, map);
            changed.Add(meeting);
        }

        return changed;
    }

    private static void Apply(Meeting meeting, IReadOnlyDictionary<string, string> map)
    {
        for (var index = 0; index < meeting.Speakers.Count; index++)
        {
            var profile = meeting.Speakers[index];
            if (!map.TryGetValue(profile.Id, out var id)) continue;
            // Profiles can be the same object in several meetings, so replace rather than mutate.
            meeting.Speakers[index] = new SpeakerProfile
            {
                Id = id,
                Name = profile.Name,
                Role = profile.Role,
                AccentColor = profile.AccentColor,
                Meetings = profile.Meetings,
                VoiceNote = profile.VoiceNote
            };
        }

        foreach (var segment in meeting.Transcript)
        {
            if (map.TryGetValue(segment.SpeakerId, out var id))
                segment.SpeakerId = id;
        }
    }
}
