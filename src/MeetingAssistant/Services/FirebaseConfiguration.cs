using System.IO;
using System.Text.Json;

namespace MeetingAssistant.Services;

public sealed class FirebaseConfiguration
{
    public const string ApiKeyEnvironmentVariable = "MEETING_ASSISTANT_FIREBASE_API_KEY";
    public const string ProjectIdEnvironmentVariable = "MEETING_ASSISTANT_FIREBASE_PROJECT_ID";
    public const string ConfigFileName = "firebase.config.json";

    public FirebaseConfiguration(string? baseDirectory = null)
    {
        var configPath = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, ConfigFileName);
        var fileConfiguration = LoadFileConfiguration(configPath);

        var environmentApiKey = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
        var environmentProjectId = Environment.GetEnvironmentVariable(ProjectIdEnvironmentVariable);

        ApiKey = FirstValue(environmentApiKey, fileConfiguration?.ApiKey);
        ProjectId = FirstValue(environmentProjectId, fileConfiguration?.ProjectId);
        ConfigurationPath = File.Exists(configPath) ? configPath : null;
        Source = !string.IsNullOrWhiteSpace(environmentApiKey) && !string.IsNullOrWhiteSpace(environmentProjectId)
            ? "Environment"
            : ConfigurationPath is not null ? "Installer config" : "Not configured";
    }

    public string? ApiKey { get; }
    public string? ProjectId { get; }
    public string? ConfigurationPath { get; }
    public string Source { get; }
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ProjectId);

    private static FirebaseFileConfiguration? LoadFileConfiguration(string path)
    {
        if (!File.Exists(path)) return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<FirebaseFileConfiguration>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? FirstValue(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private sealed class FirebaseFileConfiguration
    {
        public string? ApiKey { get; set; }
        public string? ProjectId { get; set; }
    }
}
