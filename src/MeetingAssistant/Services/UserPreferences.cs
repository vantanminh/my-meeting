using System.IO;
using System.Text.Json;

namespace MeetingAssistant.Services;

public sealed class UserPreferences
{
    public string Theme { get; set; } = nameof(ThemeMode.Dark);
    public string Language { get; set; } = "vi";
    public string RetentionOption { get; set; } = "Keep recordings for 30 days";
    public bool KeepLocalCopy { get; set; } = true;
    public bool SyncPaused { get; set; }
    public bool StartOnLogin { get; set; } = true;
    public string TranscriptionModel { get; set; } = "gpt-4o-transcribe";
    public string SummaryModel { get; set; } = "gpt-4.1-mini";
}

public sealed class JsonUserPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path = Path.Combine(AppPaths.DataDirectory, "settings.json");

    public UserPreferences Load()
    {
        if (!File.Exists(_path)) return new UserPreferences();

        try
        {
            var json = File.ReadAllText(_path);
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
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, preferences, JsonOptions);
        }

        File.Move(temporaryPath, _path, overwrite: true);
    }
}
