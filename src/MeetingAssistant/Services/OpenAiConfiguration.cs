namespace MeetingAssistant.Services;

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

    public OpenAiConfiguration(
        string? apiKeyOverride = null,
        string? transcriptionModelOverride = null,
        string? summaryModelOverride = null)
    {
        _apiKeyOverride = apiKeyOverride;
        _transcriptionModelOverride = transcriptionModelOverride;
        _summaryModelOverride = summaryModelOverride;
    }

    public string? ApiKey => FirstValue(
        _apiKeyOverride,
        Environment.GetEnvironmentVariable(ScopedApiKeyEnvironmentVariable),
        Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable));

    public string TranscriptionModel => FirstValue(
        Environment.GetEnvironmentVariable(TranscriptionModelEnvironmentVariable),
        _transcriptionModelOverride,
        DefaultTranscriptionModel) ?? DefaultTranscriptionModel;

    public string SummaryModel => FirstValue(
        Environment.GetEnvironmentVariable(SummaryModelEnvironmentVariable),
        _summaryModelOverride,
        DefaultSummaryModel) ?? DefaultSummaryModel;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public string ApiKeySource
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_apiKeyOverride)) return "Test configuration";
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ScopedApiKeyEnvironmentVariable)))
                return ScopedApiKeyEnvironmentVariable;
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable)))
                return ApiKeyEnvironmentVariable;
            return "Not configured";
        }
    }

    public void SaveUserEnvironment(string apiKey, string transcriptionModel, string summaryModel)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
            SetUserAndProcess(ScopedApiKeyEnvironmentVariable, apiKey.Trim());

        if (!string.IsNullOrWhiteSpace(transcriptionModel))
            SetUserAndProcess(TranscriptionModelEnvironmentVariable, transcriptionModel.Trim());

        if (!string.IsNullOrWhiteSpace(summaryModel))
            SetUserAndProcess(SummaryModelEnvironmentVariable, summaryModel.Trim());
    }

    public void ClearScopedApiKey()
    {
        Environment.SetEnvironmentVariable(ScopedApiKeyEnvironmentVariable, null, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable(ScopedApiKeyEnvironmentVariable, null, EnvironmentVariableTarget.Process);
    }

    private static void SetUserAndProcess(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
    }

    private static string? FirstValue(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
