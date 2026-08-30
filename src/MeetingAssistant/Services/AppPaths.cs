using System.IO;
using System.Text.RegularExpressions;

namespace MeetingAssistant.Services;

public static class AppPaths
{
    private static readonly object Gate = new();
    private static string? _rootOverride;
    private static string _currentUserId = "offline";

    public static string RootDirectory
    {
        get
        {
            lock (Gate)
            {
                return _rootOverride ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MeetingAssistant");
            }
        }
    }

    public static string CurrentUserId
    {
        get { lock (Gate) return _currentUserId; }
    }

    public static string DataDirectory => Path.Combine(RootDirectory, "users", SanitizeUserId(CurrentUserId));

    public static string RecordingsDirectory => Path.Combine(DataDirectory, "Recordings");

    public static string MeetingsPath => Path.Combine(DataDirectory, "meetings.json");

    public static string SettingsPath => Path.Combine(RootDirectory, "settings.json");

    public static string SessionPath => Path.Combine(RootDirectory, "session.json");

    public static string FirebaseSessionPath => Path.Combine(RootDirectory, "firebase-session.json");

    public static string OutboxPath => Path.Combine(DataDirectory, "sync-outbox.json");

    public static void UseRoot(string? root)
    {
        lock (Gate)
        {
            _rootOverride = string.IsNullOrWhiteSpace(root) ? null : Path.GetFullPath(root);
        }
    }

    public static void SetCurrentUser(string? userId)
    {
        lock (Gate)
        {
            _currentUserId = string.IsNullOrWhiteSpace(userId) ? "offline" : userId.Trim();
        }

        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(RecordingsDirectory);
        MigrateLegacyWorkspace();
    }

    public static void Reset()
    {
        lock (Gate)
        {
            _rootOverride = null;
            _currentUserId = "offline";
        }
    }

    public static string SanitizeUserId(string userId)
    {
        var trimmed = string.IsNullOrWhiteSpace(userId) ? "offline" : userId.Trim();
        var safe = Regex.Replace(trimmed, @"[^\w.@+-]+", "_");
        return string.IsNullOrWhiteSpace(safe) ? "offline" : safe;
    }

    private static void MigrateLegacyWorkspace()
    {
        var legacyMeetings = Path.Combine(RootDirectory, "meetings.json");
        if (!System.IO.File.Exists(legacyMeetings) || System.IO.File.Exists(MeetingsPath))
            return;

        try
        {
            System.IO.File.Copy(legacyMeetings, MeetingsPath, overwrite: false);
        }
        catch (IOException)
        {
            // A concurrent first launch can create the destination; keep both copies.
        }
    }
}
