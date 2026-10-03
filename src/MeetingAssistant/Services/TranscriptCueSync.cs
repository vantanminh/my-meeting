using MeetingAssistant.Models;

namespace MeetingAssistant.Services;

public static class TranscriptCueSync
{
    public static TimeSpan CueEnd(TranscriptSegment segment)
    {
        if (segment.End > segment.Start)
            return segment.End;
        return segment.Start + TimeSpan.FromSeconds(4);
    }

    public static TranscriptSegment? CueAt(IReadOnlyList<TranscriptSegment> segments, TimeSpan position)
    {
        if (position < TimeSpan.Zero)
            return null;

        TranscriptSegment? match = null;
        foreach (var segment in segments)
        {
            var start = segment.Start < TimeSpan.Zero ? TimeSpan.Zero : segment.Start;
            if (position >= start && position < CueEnd(segment))
                match = segment;
        }

        return match;
    }

    public static bool ShouldStopClip(TimeSpan position, TimeSpan? clipEnd)
        => clipEnd is { } end && position >= end;

    public static string FormatClock(TimeSpan value)
    {
        var safe = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        return safe.ToString(safe.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");
    }
}
