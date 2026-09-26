using System.IO;
using System.Security;

namespace MeetingAssistant.Services;

public sealed class AssemblyAiConfiguration
{
    public const string ApiKeyEnvironmentVariable = "ASSEMBLYAI_API_KEY";
    public const string ScopedApiKeyEnvironmentVariable = "MEETING_ASSISTANT_ASSEMBLYAI_API_KEY";
    public const string SpeechModelsEnvironmentVariable = "MEETING_ASSISTANT_ASSEMBLYAI_SPEECH_MODELS";
    public const string ProviderEnvironmentVariable = "MEETING_TRANSCRIPTION_PROVIDER";
    public const string ScopedProviderEnvironmentVariable = "MEETING_ASSISTANT_TRANSCRIPTION_PROVIDER";
    public const string DefaultProvider = "assemblyai";

    public static readonly string[] DefaultSpeechModels = ["universal-3-5-pro", "universal-2"];

    private readonly string? _apiKeyOverride;
    private readonly string? _providerOverride;
    private readonly IUserEnvironmentStore _userEnvironment;

    public AssemblyAiConfiguration(
        string? apiKeyOverride = null,
        string? providerOverride = null,
        IUserEnvironmentStore? userEnvironment = null)
    {
        _apiKeyOverride = apiKeyOverride;
        _providerOverride = providerOverride;
        _userEnvironment = userEnvironment ?? new WindowsUserEnvironmentStore();
    }

    public string? ApiKey => FirstValue(
        _apiKeyOverride,
        ReadEnvironment(ScopedApiKeyEnvironmentVariable),
        ReadEnvironment(ApiKeyEnvironmentVariable));

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public string Provider => (FirstValue(
        _providerOverride,
        ReadEnvironment(ScopedProviderEnvironmentVariable),
        ReadEnvironment(ProviderEnvironmentVariable),
        DefaultProvider) ?? DefaultProvider).ToLowerInvariant();

    public IReadOnlyList<string> SpeechModels
    {
        get
        {
            var configured = ReadEnvironment(SpeechModelsEnvironmentVariable);
            if (string.IsNullOrWhiteSpace(configured)) return DefaultSpeechModels;

            var models = configured
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(model => model.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return models.Length == 0 ? DefaultSpeechModels : models;
        }
    }

    public string ApiKeySource
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_apiKeyOverride)) return "Test configuration";
            if (!string.IsNullOrWhiteSpace(ReadEnvironment(ScopedApiKeyEnvironmentVariable)))
                return ScopedApiKeyEnvironmentVariable;
            if (!string.IsNullOrWhiteSpace(ReadEnvironment(ApiKeyEnvironmentVariable)))
                return ApiKeyEnvironmentVariable;
            return "Not configured";
        }
    }

    public void SaveUserEnvironment(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) return;
        var normalized = apiKey.Trim();
        if (normalized.Any(char.IsControl))
            throw new ArgumentException("Environment values cannot contain control characters.", nameof(apiKey));

        _userEnvironment.Set(ScopedApiKeyEnvironmentVariable, normalized);
        Environment.SetEnvironmentVariable(ScopedApiKeyEnvironmentVariable, normalized, EnvironmentVariableTarget.Process);
    }

    public void ClearScopedApiKey()
    {
        _userEnvironment.Delete(ScopedApiKeyEnvironmentVariable);
        Environment.SetEnvironmentVariable(ScopedApiKeyEnvironmentVariable, null, EnvironmentVariableTarget.Process);
    }

    private string? ReadEnvironment(string name)
    {
        var processValue = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(processValue)) return processValue.Trim();

        try
        {
            return _userEnvironment.Get(name)?.Trim();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (SecurityException)
        {
            return null;
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static string? FirstValue(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
