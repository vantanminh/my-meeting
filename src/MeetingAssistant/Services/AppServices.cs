namespace MeetingAssistant.Services;

public sealed class AppServices : IDisposable
{
    public AppServices()
    {
        var firebaseConfiguration = new FirebaseConfiguration();
        var localAuth = new LocalAuthService();
        AuthService = new FirebaseAuthService(firebaseConfiguration, localAuth);
        Preferences = new JsonUserPreferencesStore();
        MeetingRepository = new JsonMeetingRepository();
        AudioCaptureService = new WindowsAudioCaptureService();
        IntelligenceService = new DemoMeetingIntelligenceService();
        CloudSyncService = new FirebaseCloudSyncService(firebaseConfiguration, AuthService);
        HotkeyService = new GlobalHotkeyService();
        TrayService = new TrayService();
    }

    public IAuthService AuthService { get; }
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
