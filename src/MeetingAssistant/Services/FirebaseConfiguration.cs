namespace MeetingAssistant.Services;

public sealed class FirebaseConfiguration
{
    public FirebaseConfiguration()
    {
        ApiKey = Environment.GetEnvironmentVariable("MEETING_ASSISTANT_FIREBASE_API_KEY");
        ProjectId = Environment.GetEnvironmentVariable("MEETING_ASSISTANT_FIREBASE_PROJECT_ID");
    }

    public string? ApiKey { get; }
    public string? ProjectId { get; }
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(ProjectId);
}
