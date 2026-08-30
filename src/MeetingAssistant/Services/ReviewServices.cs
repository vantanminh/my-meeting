using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using MeetingAssistant.Models;

namespace MeetingAssistant.Services;

public static class AuthValidation
{
    public static string? ValidateSignIn(string email, string password)
    {
        if (!email.Contains('@', StringComparison.Ordinal) || email.Trim().Length < 5)
            return "Enter a valid email address.";
        if (password.Length < 6)
            return "Password must be at least 6 characters.";
        return null;
    }

    public static string? ValidateSignUp(string displayName, string email, string password)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return "Tell us your name to create the workspace.";
        return ValidateSignIn(email, password);
    }
}

public static class HubMeetingFilter
{
    public static IEnumerable<Meeting> Apply(
        IEnumerable<Meeting> meetings,
        string filter,
        DateTime today)
    {
        return filter switch
        {
            "This week" => meetings.Where(meeting => WeekStats.CountThisWeek([meeting], today) == 1),
            "Open actions" => meetings.Where(meeting => meeting.Summary.ActionItems.Any(item => !item.IsComplete)),
            "Failed" => meetings.Where(meeting => meeting.Status == MeetingStatus.Failed),
            "Unsynced" => meetings.Where(meeting => meeting.SyncState is SyncState.Pending or SyncState.Local or SyncState.Offline),
            _ => meetings
        };
    }
}

public static class TranscriptEditing
{
    public static void AssignSpeaker(TranscriptSegment segment, SpeakerProfile speaker)
    {
        segment.SpeakerId = speaker.Id;
        segment.SpeakerName = speaker.Name;
    }

    public static TranscriptSegment Split(List<TranscriptSegment> transcript, TranscriptSegment source, int atCharacter)
    {
        var index = transcript.IndexOf(source);
        if (index < 0) throw new InvalidOperationException("The turn is not in this transcript.");
        var safeAt = Math.Clamp(atCharacter, 1, Math.Max(source.Text.Length - 1, 1));
        if (source.Text.Length < 2)
            throw new InvalidOperationException("A turn needs at least two characters to split.");

        var left = source.Text[..safeAt].TrimEnd();
        var right = source.Text[safeAt..].TrimStart();
        if (left.Length == 0 || right.Length == 0)
            throw new InvalidOperationException("Split would leave an empty turn.");

        var midpoint = source.Start + TimeSpan.FromTicks((source.End - source.Start).Ticks / 2);
        source.Text = left;
        source.End = midpoint;
        var created = new TranscriptSegment
        {
            SpeakerId = source.SpeakerId,
            SpeakerName = source.SpeakerName,
            Start = midpoint,
            End = source.End == midpoint ? midpoint + TimeSpan.FromSeconds(1) : source.End,
            Text = right
        };
        if (created.End <= created.Start)
            created.End = created.Start + TimeSpan.FromSeconds(1);
        transcript.Insert(index + 1, created);
        return created;
    }

    public static void MergeWithNext(List<TranscriptSegment> transcript, TranscriptSegment source)
    {
        var index = transcript.IndexOf(source);
        if (index < 0 || index >= transcript.Count - 1)
            throw new InvalidOperationException("There is no next turn to merge.");
        var next = transcript[index + 1];
        source.Text = $"{source.Text.Trim()} {next.Text.Trim()}".Trim();
        source.End = next.End > source.End ? next.End : source.End;
        transcript.RemoveAt(index + 1);
    }

    public static TranscriptSegment InsertAfter(List<TranscriptSegment> transcript, TranscriptSegment source)
    {
        var index = transcript.IndexOf(source);
        if (index < 0) throw new InvalidOperationException("The turn is not in this transcript.");
        var created = new TranscriptSegment
        {
            SpeakerId = source.SpeakerId,
            SpeakerName = source.SpeakerName,
            Start = source.End,
            End = source.End + TimeSpan.FromSeconds(2),
            Text = ""
        };
        transcript.Insert(index + 1, created);
        return created;
    }

    public static void Delete(List<TranscriptSegment> transcript, TranscriptSegment source)
    {
        if (!transcript.Remove(source))
            throw new InvalidOperationException("The turn is not in this transcript.");
    }

    public static string CopyMarkdown(IEnumerable<TranscriptSegment> segments)
        => string.Join(Environment.NewLine, segments.Select(segment => $"[{segment.Timestamp}] {segment.SpeakerName}: {segment.Text}"));
}

public static class SpeakerMemory
{
    public static int Recount(IEnumerable<Meeting> meetings, string speakerId)
        => meetings.Count(meeting => meeting.Speakers.Any(profile => profile.Id == speakerId));
}

public static class ProtectedWorkspaceStore
{
    private static readonly byte[] Magic = "MA1P"u8.ToArray();

    public static async Task WriteAsync(string path, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        try
        {
            var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
            var payload = new byte[Magic.Length + protectedBytes.Length];
            Magic.CopyTo(payload, 0);
            protectedBytes.CopyTo(payload, Magic.Length);
            await File.WriteAllBytesAsync(path, payload);
        }
        catch (PlatformNotSupportedException)
        {
            await File.WriteAllBytesAsync(path, bytes);
        }
        catch (CryptographicException)
        {
            await File.WriteAllBytesAsync(path, bytes);
        }
    }

    public static async Task<string> ReadAsync(string path)
    {
        var data = await File.ReadAllBytesAsync(path);
        if (data.Length >= Magic.Length && data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            try
            {
                var plain = ProtectedData.Unprotect(data.AsSpan(Magic.Length).ToArray(), optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plain);
            }
            catch (CryptographicException)
            {
                throw new JsonWorkspaceException("The protected workspace could not be unlocked on this Windows profile.");
            }
            catch (PlatformNotSupportedException)
            {
                throw new JsonWorkspaceException("Protected workspace files can only be opened on Windows.");
            }
        }

        return Encoding.UTF8.GetString(data);
    }

    public static bool LooksProtected(ReadOnlySpan<byte> data)
        => data.Length >= Magic.Length && data[..Magic.Length].SequenceEqual(Magic);
}

public sealed class JsonWorkspaceException : Exception
{
    public JsonWorkspaceException(string message) : base(message) { }
}

public sealed class MeetingPlaybackService : IDisposable
{
    private readonly MediaPlayer _player = new();
    private string? _openPath;

    public bool IsPlaying { get; private set; }
    public double Speed
    {
        get => _player.SpeedRatio <= 0 ? 1 : _player.SpeedRatio;
        set => _player.SpeedRatio = Math.Clamp(value, 0.5, 2);
    }

    public TimeSpan Position => _player.Position;
    public string? OpenPath => _openPath;

    public bool Open(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Stop();
            return false;
        }

        if (string.Equals(_openPath, path, StringComparison.OrdinalIgnoreCase))
            return true;

        _player.Open(new Uri(path, UriKind.Absolute));
        _openPath = path;
        return true;
    }

    public void Play()
    {
        _player.Play();
        IsPlaying = true;
    }

    public void Pause()
    {
        _player.Pause();
        IsPlaying = false;
    }

    public void Toggle()
    {
        if (IsPlaying) Pause();
        else Play();
    }

    public void Seek(TimeSpan position)
    {
        _player.Position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
    }

    public void Skip(TimeSpan delta)
    {
        var next = _player.Position + delta;
        Seek(next < TimeSpan.Zero ? TimeSpan.Zero : next);
    }

    public void Stop()
    {
        _player.Stop();
        IsPlaying = false;
        _openPath = null;
    }

    public void Dispose()
    {
        Stop();
        _player.Close();
    }
}
