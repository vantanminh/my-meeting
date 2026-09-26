using System.IO;
using System.Text.Json;
using MeetingAssistant.Models;

namespace MeetingAssistant.Services;

public sealed class UserPreferences
{
    public string Theme { get; set; } = nameof(ThemeMode.Dark);
    public string Language { get; set; } = "vi";
    public string RetentionOption { get; set; } = "Keep recordings for 30 days";
    public bool KeepLocalCopy { get; set; } = true;
    public bool SyncPaused { get; set; }
    public bool StartOnLogin { get; set; }
    public bool MinimizeToTrayOnClose { get; set; }
    public bool OnboardingCompleted { get; set; }
    public string TranscriptionModel { get; set; } = "gpt-4o-transcribe";
    public string SummaryModel { get; set; } = OpenAiConfiguration.DefaultSummaryModel;
    public string TranscriptionLanguage { get; set; } = "auto";
    public string MicrophoneId { get; set; } = "default";
    public string SystemAudioId { get; set; } = "default";
    public string Quality { get; set; } = "Balanced · 48 kHz";
    public string RecordingsDirectory { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "Local workspace";
    public UpdatePolicy UpdatePolicy { get; set; } = UpdatePolicy.Automatic;
    public string HotkeyDisplay { get; set; } = "Ctrl + Shift + R";
}

public sealed class JsonUserPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private string PathName => AppPaths.SettingsPath;

    public UserPreferences Load()
    {
        if (!File.Exists(PathName)) return new UserPreferences();

        try
        {
            var json = File.ReadAllText(PathName);
            return JsonSerializer.Deserialize<UserPreferences>(json, JsonOptions) ?? new UserPreferences();
        }
        catch (IOException)
        {
            return new UserPreferences();
        }
        catch (JsonException)
        {
            return new UserPreferences();
        }
    }

    public async Task SaveAsync(UserPreferences preferences)
    {
        Directory.CreateDirectory(AppPaths.RootDirectory);
        var temporaryPath = $"{PathName}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, preferences, JsonOptions);
        }

        File.Move(temporaryPath, PathName, overwrite: true);
    }
}
