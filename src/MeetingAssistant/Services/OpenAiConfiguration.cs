using System.IO;
using System.Security;
using Microsoft.Win32;

namespace MeetingAssistant.Services;

public interface IUserEnvironmentStore
{
    string? Get(string name);
    void Set(string name, string value);
    void Delete(string name);
}

public sealed class WindowsUserEnvironmentStore : IUserEnvironmentStore
{
    private const string EnvironmentKeyPath = "Environment";

    public string? Get(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(EnvironmentKeyPath, writable: false);
        var value = key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return value?.ToString();
    }

    public void Set(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(EnvironmentKeyPath, writable: true)
            ?? throw new InvalidOperationException("The current Windows user environment is unavailable.");
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void Delete(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(EnvironmentKeyPath, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}

public sealed class OpenAiConfiguration
{
    public const string ApiKeyEnvironmentVariable = "OPENAI_API_KEY";
    public const string ScopedApiKeyEnvironmentVariable = "MEETING_ASSISTANT_OPENAI_API_KEY";
    public const string TranscriptionModelEnvironmentVariable = "MEETING_ASSISTANT_OPENAI_TRANSCRIPTION_MODEL";
    public const string SummaryModelEnvironmentVariable = "MEETING_ASSISTANT_OPENAI_SUMMARY_MODEL";
    public const string DefaultTranscriptionModel = "gpt-4o-transcribe";
    public const string DefaultSummaryModel = "gpt-4.1-mini";

    private readonly string? _apiKeyOverride;
    private readonly string? _transcriptionModelOverride;
    private readonly string? _summaryModelOverride;
    private readonly IUserEnvironmentStore _userEnvironment;

    private static readonly TimeSpan UserEnvironmentWriteTimeout = TimeSpan.FromSeconds(5);

    public OpenAiConfiguration(
        string? apiKeyOverride = null,
        string? transcriptionModelOverride = null,
        string? summaryModelOverride = null,
        IUserEnvironmentStore? userEnvironment = null)
    {
        _apiKeyOverride = apiKeyOverride;
        _transcriptionModelOverride = transcriptionModelOverride;
        _summaryModelOverride = summaryModelOverride;
        _userEnvironment = userEnvironment ?? new WindowsUserEnvironmentStore();
    }

    public string? ApiKey => FirstValue(
        _apiKeyOverride,
        ReadEnvironment(ScopedApiKeyEnvironmentVariable),
        ReadEnvironment(ApiKeyEnvironmentVariable));

    public string TranscriptionModel => FirstValue(
        ReadEnvironment(TranscriptionModelEnvironmentVariable),
        _transcriptionModelOverride,
        DefaultTranscriptionModel) ?? DefaultTranscriptionModel;

    public string SummaryModel => FirstValue(
        ReadEnvironment(SummaryModelEnvironmentVariable),
        _summaryModelOverride,
        DefaultSummaryModel) ?? DefaultSummaryModel;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

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

    public void SaveUserEnvironment(string apiKey, string transcriptionModel, string summaryModel)
        => SaveUserEnvironmentCore(apiKey, transcriptionModel, summaryModel, CancellationToken.None);

    public async Task SaveUserEnvironmentAsync(
        string apiKey,
        string transcriptionModel,
        string summaryModel,
        CancellationToken cancellationToken = default)
    {
        await Task.Run(
                () => SaveUserEnvironmentCore(apiKey, transcriptionModel, summaryModel, cancellationToken),
                cancellationToken)
            .WaitAsync(UserEnvironmentWriteTimeout, cancellationToken);
    }

    public void ClearScopedApiKey()
    {
        _userEnvironment.Delete(ScopedApiKeyEnvironmentVariable);
        Environment.SetEnvironmentVariable(ScopedApiKeyEnvironmentVariable, null, EnvironmentVariableTarget.Process);
    }

    private void SaveUserEnvironmentCore(
        string apiKey,
        string transcriptionModel,
        string summaryModel,
        CancellationToken cancellationToken)
    {
        SetUserAndProcess(ScopedApiKeyEnvironmentVariable, apiKey, cancellationToken);
        SetUserAndProcess(TranscriptionModelEnvironmentVariable, transcriptionModel, cancellationToken);
        SetUserAndProcess(SummaryModelEnvironmentVariable, summaryModel, cancellationToken);
    }

    private void SetUserAndProcess(string name, string value, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        var normalizedValue = value.Trim();
        if (normalizedValue.Any(char.IsControl))
            throw new ArgumentException("Environment values cannot contain control characters.", nameof(value));

        cancellationToken.ThrowIfCancellationRequested();

        // EnvironmentVariableTarget.User broadcasts WM_SETTINGCHANGE synchronously. A hung process can
        // make that framework call block the WPF dispatcher for tens of seconds, so write HKCU directly.
        _userEnvironment.Set(name, normalizedValue);
        Environment.SetEnvironmentVariable(name, normalizedValue, EnvironmentVariableTarget.Process);
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
