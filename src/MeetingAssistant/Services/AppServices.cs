namespace MeetingAssistant.Services;

public sealed class AppServices : IDisposable
{
    public AppServices()
    {
        var firebaseConfiguration = new FirebaseConfiguration();
        Preferences = new JsonUserPreferencesStore();
        var savedPreferences = Preferences.Load();
        OpenAiConfiguration = new OpenAiConfiguration(
            transcriptionModelOverride: string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(OpenAiConfiguration.TranscriptionModelEnvironmentVariable)) ? savedPreferences.TranscriptionModel : null,
            summaryModelOverride: string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(OpenAiConfiguration.SummaryModelEnvironmentVariable)) ? savedPreferences.SummaryModel : null);
        var localAuth = new LocalAuthService();
        AuthService = new FirebaseAuthService(firebaseConfiguration, localAuth);
        MeetingRepository = new JsonMeetingRepository();
        AudioCaptureService = new WindowsAudioCaptureService();
        OpenAiIntelligence = new OpenAiMeetingIntelligenceService(OpenAiConfiguration);
        IntelligenceService = OpenAiIntelligence;
        CloudSyncService = new FirebaseCloudSyncService(firebaseConfiguration, AuthService);
        HotkeyService = new GlobalHotkeyService();
        TrayService = new TrayService();
    }

    public IAuthService AuthService { get; }
    public OpenAiConfiguration OpenAiConfiguration { get; }
    public OpenAiMeetingIntelligenceService OpenAiIntelligence { get; }
    public JsonUserPreferencesStore Preferences { get; }
    public IMeetingRepository MeetingRepository { get; }
    public IAudioCaptureService AudioCaptureService { get; }
    public IMeetingIntelligenceService IntelligenceService { get; }
    public ICloudSyncService CloudSyncService { get; }
    public IGlobalHotkeyService HotkeyService { get; }
    public ITrayService TrayService { get; }

    public void Dispose()
    {
        AudioCaptureService.Dispose();
        HotkeyService.Dispose();
        TrayService.Dispose();
    }
}
