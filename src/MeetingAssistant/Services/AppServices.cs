namespace MeetingAssistant.Services;

public sealed class AppServices : IDisposable
{
    public AppServices()
    {
        Preferences = new JsonUserPreferencesStore();
        var savedPreferences = Preferences.Load();
        OpenAiConfiguration = new OpenAiConfiguration(
            transcriptionModelOverride: string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(OpenAiConfiguration.TranscriptionModelEnvironmentVariable)) ? savedPreferences.TranscriptionModel : null,
            summaryModelOverride: string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(OpenAiConfiguration.SummaryModelEnvironmentVariable)) ? savedPreferences.SummaryModel : null,
            transcriptionLanguageOverride: savedPreferences.TranscriptionLanguage);
        AuthService = new LocalAuthService();
        MeetingRepository = new JsonMeetingRepository();
        AudioDevices = new AudioDeviceCatalog();
        AudioCaptureService = new WindowsAudioCaptureService();
        OpenAiIntelligence = new OpenAiMeetingIntelligenceService(OpenAiConfiguration);
        DemoIntelligence = new DemoMeetingIntelligenceService();
        IntelligenceService = OpenAiIntelligence;
        CloudSyncService = new LocalCloudSyncService();
        UpdateService = new UpdateChannelService(new UpdateChannelConfiguration());
        HotkeyService = new GlobalHotkeyService();
        TrayService = new TrayService();
        Startup = new WindowsStartupRegistration();
        Outbox = new JsonSyncOutbox();
    }

    public IAuthService AuthService { get; }
    public OpenAiConfiguration OpenAiConfiguration { get; }
    public OpenAiMeetingIntelligenceService OpenAiIntelligence { get; }
    public DemoMeetingIntelligenceService DemoIntelligence { get; }
    public JsonUserPreferencesStore Preferences { get; }
    public IMeetingRepository MeetingRepository { get; }
    public IAudioDeviceCatalog AudioDevices { get; }
    public IAudioCaptureService AudioCaptureService { get; }
    public IMeetingIntelligenceService IntelligenceService { get; }
    public ICloudSyncService CloudSyncService { get; }
    public IUpdateChannelService UpdateService { get; }
    public IGlobalHotkeyService HotkeyService { get; }
    public ITrayService TrayService { get; }
    public IStartupRegistration Startup { get; }
    public JsonSyncOutbox Outbox { get; }

    public void Dispose()
    {
        AudioCaptureService.Dispose();
        UpdateService.Dispose();
        HotkeyService.Dispose();
        TrayService.Dispose();
    }
}
