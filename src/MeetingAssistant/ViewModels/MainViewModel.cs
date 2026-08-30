using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MeetingAssistant.Models;
using MeetingAssistant.Services;

namespace MeetingAssistant.ViewModels;

public enum WorkspaceView
{
    Auth,
    Dashboard,
    Setup,
    Recording,
    Processing,
    Detail,
    Speakers,
    Actions,
    Settings
}

public sealed class SpeakerEditorViewModel : ViewModelBase
{
    private string _name;
    private string _role;
    private string _accentColor;
    private int _meetings;

    public SpeakerEditorViewModel(SpeakerProfile profile)
    {
        Id = profile.Id;
        _name = profile.Name;
        _role = profile.Role;
        _accentColor = profile.AccentColor;
        _meetings = profile.Meetings;
    }

    public string Id { get; }
    public int Meetings
    {
        get => _meetings;
        set => SetProperty(ref _meetings, value);
    }

    public string Role
    {
        get => _role;
        set => SetProperty(ref _role, value);
    }

    public string AccentColor
    {
        get => _accentColor;
        set => SetProperty(ref _accentColor, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }
}

public sealed partial class MainViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly IAuthService _auth;
    private readonly IMeetingRepository _repository;
    private readonly IAudioCaptureService _audio;
    private readonly IMeetingIntelligenceService _intelligence;
    private readonly ICloudSyncService _cloud;
    private readonly IGlobalHotkeyService _hotkey;
    private readonly JsonUserPreferencesStore _preferences;
    private readonly UserPreferences _savedPreferences;
    private readonly OpenAiConfiguration _openAi;
    private readonly OpenAiMeetingIntelligenceService _openAiIntelligence;
    private readonly IUpdateChannelService _updates;
    private readonly DispatcherTimer _recordingTimer;
    private readonly DispatcherTimer _updateCheckTimer;
    private CancellationTokenSource? _processingCancellation;
    private CancellationTokenSource? _updateCheckCancellation;
    private CancellationTokenSource? _updateCancellation;
    private RecordingData? _lastRecording;
    private AppUpdateInfo? _availableUpdate;
    private WorkspaceView _currentView = WorkspaceView.Auth;
    private UserSession? _currentUser;
    private Meeting? _currentMeeting;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string _displayName = string.Empty;
    private string _authError = string.Empty;
    private string _authInfo = string.Empty;
    private string _searchQuery = string.Empty;
    private string _recordingTitle = "Weekly team sync";
    private string _selectedMicrophone = "Default microphone";
    private string _selectedSystemAudio = "Default system audio";
    private string _selectedQuality = "Balanced · 48 kHz";
    private string _deviceTestStatus = "Not tested yet";
    private string _recordingStatus = "Recording live";
    private string _elapsedLabel = "00:00";
    private string _activeSpeaker = "Listening for a speaker";
    private string _processingStage = "Preparing audio";
    private string _processingMessage = "Checking microphone and system audio tracks";
    private string _transcriptSpeakerFilter = "All speakers";
    private string _toastMessage = string.Empty;
    private string _retentionOption = "Keep recordings for 30 days";
    private string _selectedLanguage = "Tiếng Việt";
    private string _selectedTheme = nameof(ThemeMode.Dark);
    private string _openAiApiKeyInput = string.Empty;
    private string _openAiTranscriptionModel = OpenAiConfiguration.DefaultTranscriptionModel;
    private string _openAiSummaryModel = OpenAiConfiguration.DefaultSummaryModel;
    private string _openAiConnectionStatus = "Not tested yet";
    private string _updateStatus = "Updates are not configured for this build.";
    private UpdateProgressStage _updateProgressStage = UpdateProgressStage.Downloading;
    private int _updateProgressPercent;
    private long _updateDownloadedBytes;
    private long? _updateTotalBytes;
    private double _microphoneLevel;
    private double _systemAudioLevel;
    private int _processingPercent;
    private int _processingStageIndex;
    private bool _isAuthenticated;
    private bool _isSignUpMode;
    private bool _isAuthenticating;
    private bool _isLoadingMeetings;
    private bool _isTestingDevices;
    private bool _deviceTested;
    private bool _isRecording;
    private bool _isPaused;
    private bool _isStopping;
    private bool _isProcessing;
    private bool _processingHasError;
    private bool _keepLocalCopy = true;
    private bool _syncPaused;
    private bool _startOnLogin = true;
    private bool _isTestingOpenAi;
    private bool _isSavingSettings;
    private bool _isCheckingForUpdates;
    private bool _isInstallingUpdate;
    private bool _isUpdateSurfaceVisible;

    public MainViewModel(AppServices services)
    {
        _services = services;
        _auth = services.AuthService;
        _repository = services.MeetingRepository;
        _audio = services.AudioCaptureService;
        _intelligence = services.IntelligenceService;
        _cloud = services.CloudSyncService;
        _hotkey = services.HotkeyService;
        _preferences = services.Preferences;
        _savedPreferences = _preferences.Load();
        _openAi = services.OpenAiConfiguration;
        _openAiIntelligence = services.OpenAiIntelligence;
        _updates = services.UpdateService;
        _devices = services.AudioDevices;
        _startup = services.Startup;
        _outbox = services.Outbox;
        _updateStatus = _updates.Configuration.IsConfigured
            ? "Updates are ready to check."
            : "Updates are not configured for this build.";
        _selectedTheme = ThemeService.Parse(_savedPreferences.Theme).ToString();
        _selectedLanguage = string.Equals(_savedPreferences.Language, "en", StringComparison.OrdinalIgnoreCase) ? "English" : "Tiếng Việt";
        _retentionOption = _savedPreferences.RetentionOption;
        _keepLocalCopy = _savedPreferences.KeepLocalCopy;
        _syncPaused = _savedPreferences.SyncPaused;
        _startOnLogin = _savedPreferences.StartOnLogin;
        _openAiTranscriptionModel = _openAi.TranscriptionModel;
        _openAiSummaryModel = _openAi.SummaryModel;
        _cloud.IsPaused = _syncPaused;
        ThemeService.Apply(ThemeService.Parse(_selectedTheme));
        LocalizationService.SetLanguage(_selectedLanguage == "English" ? "en" : "vi");
        LocalizationService.LanguageChanged += OnLanguageChanged;

        _recordingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _recordingTimer.Tick += (_, _) => UpdateRecordingClock();
        _updateCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        _updateCheckTimer.Tick += (_, _) => _ = CheckForUpdatesAsync(silent: true);
        _audio.LevelsChanged += OnAudioLevelsChanged;
        _hotkey.ToggleRecordingRequested += OnGlobalHotkeyRequested;

        Meetings = [];
        VisibleMeetings = [];
        MicrophoneOptions = ["Default microphone"];
        SystemAudioOptions = ["Default system audio"];
        QualityOptions = ["Balanced · 48 kHz", "High quality · 48 kHz", "Compact · 16 kHz"];
        RetentionOptions = ["Keep recordings for 7 days", "Keep recordings for 30 days", "Keep recordings until deleted"];
        ThemeOptions = [nameof(ThemeMode.Dark), nameof(ThemeMode.Light)];
        OpenAiTranscriptionModelOptions = ["gpt-4o-transcribe", "gpt-4o-mini-transcribe", "gpt-transcribe", "gpt-4o-transcribe-diarize", "whisper-1"];
        OpenAiSummaryModelOptions = ["gpt-4.1-mini", "gpt-4o-mini", "gpt-4o"];
        LanguageOptions = ["Tiếng Việt", "English"];
        TranscriptFilterOptions = ["All speakers"];
        FilteredTranscript = [];
        SpeakerEditors = [];

        NavigateCommand = new RelayCommand(parameter => Navigate(parameter as string ?? "Meetings"));
        OpenMeetingCommand = new RelayCommand(parameter => OpenMeeting(parameter as Meeting));
        OpenSetupCommand = new RelayCommand(_ => OpenSetup());
        BackToMeetingsCommand = new RelayCommand(_ => Navigate("Meetings"));
        SignInCommand = new AsyncRelayCommand(SubmitAuthAsync, () => !IsAuthenticating);
        OfflineAccessCommand = new AsyncRelayCommand(SignInOfflineAsync, () => !IsAuthenticating);
        ToggleAuthModeCommand = new RelayCommand(_ => ToggleAuthMode());
        ForgotPasswordCommand = new AsyncRelayCommand(RequestPasswordResetAsync, () => !IsAuthenticating);
        SignOutCommand = new AsyncRelayCommand(SignOutAsync);
        TestDevicesCommand = new AsyncRelayCommand(TestDevicesAsync, () => !IsTestingDevices);
        StartRecordingCommand = new AsyncRelayCommand(StartRecordingAsync, () => !IsRecording && !IsProcessing && !IsInstallingUpdate);
        PauseRecordingCommand = new AsyncRelayCommand(PauseRecordingAsync, () => IsRecording && !IsStopping);
        ResumeRecordingCommand = new AsyncRelayCommand(ResumeRecordingAsync, () => IsRecording && IsPaused && !IsStopping);
        StopRecordingCommand = new AsyncRelayCommand(StopRecordingAsync, () => IsRecording && !IsStopping);
        RetryProcessingCommand = new AsyncRelayCommand(RetryProcessingAsync, () => _lastRecording is not null && !IsProcessing);
        CancelProcessingCommand = new RelayCommand(_ => CancelProcessing(), _ => IsProcessing);
        SaveMeetingCommand = new AsyncRelayCommand(SaveMeetingAsync, () => CurrentMeeting is not null);
        SetTranscriptFilterCommand = new RelayCommand(parameter => SetTranscriptFilter(parameter as string ?? "All speakers"));
        SaveSpeakerCommand = new AsyncRelayCommand(SaveSpeakerAsync, () => true);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, () => !IsSavingSettings && !IsTestingOpenAi);
        ClearSearchCommand = new RelayCommand(_ => SearchQuery = string.Empty, _ => HasSearchQuery);
        TestOpenAiCommand = new AsyncRelayCommand(TestOpenAiAsync, () => !IsTestingOpenAi && !IsSavingSettings);
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync, () => !IsCheckingForUpdates && !IsInstallingUpdate);
        InstallUpdateCommand = new AsyncRelayCommand(InstallUpdateAsync, () => HasAvailableUpdate && CanInstallUpdateNow && !IsCheckingForUpdates && !IsInstallingUpdate);
        BindWorkspaceAfterConstruction();
    }

    public ObservableCollection<Meeting> Meetings { get; }
    public ObservableCollection<Meeting> VisibleMeetings { get; }
    public ObservableCollection<SpeakerEditorViewModel> SpeakerEditors { get; }
    public ObservableCollection<TranscriptSegment> FilteredTranscript { get; }
    public IReadOnlyList<string> MicrophoneOptions { get; }
    public IReadOnlyList<string> SystemAudioOptions { get; }
    public IReadOnlyList<string> QualityOptions { get; }
    public IReadOnlyList<string> RetentionOptions { get; }
    public IReadOnlyList<string> ThemeOptions { get; }
    public IReadOnlyList<string> LanguageOptions { get; }
    public IReadOnlyList<string> OpenAiTranscriptionModelOptions { get; }
    public IReadOnlyList<string> OpenAiSummaryModelOptions { get; }
    public ObservableCollection<string> TranscriptFilterOptions { get; }

    public ICommand NavigateCommand { get; }
    public ICommand OpenMeetingCommand { get; }
    public ICommand OpenSetupCommand { get; }
    public ICommand BackToMeetingsCommand { get; }
    public AsyncRelayCommand SignInCommand { get; }
    public AsyncRelayCommand OfflineAccessCommand { get; }
    public ICommand ToggleAuthModeCommand { get; }
    public AsyncRelayCommand ForgotPasswordCommand { get; }
    public AsyncRelayCommand SignOutCommand { get; }
    public AsyncRelayCommand TestDevicesCommand { get; }
    public AsyncRelayCommand StartRecordingCommand { get; }
    public AsyncRelayCommand PauseRecordingCommand { get; }
    public AsyncRelayCommand ResumeRecordingCommand { get; }
    public AsyncRelayCommand StopRecordingCommand { get; }
    public AsyncRelayCommand RetryProcessingCommand { get; }
    public RelayCommand CancelProcessingCommand { get; }
    public AsyncRelayCommand SaveMeetingCommand { get; }
    public ICommand SetTranscriptFilterCommand { get; }
    public AsyncRelayCommand SaveSpeakerCommand { get; }
    public AsyncRelayCommand SaveSettingsCommand { get; }
    public RelayCommand ClearSearchCommand { get; }
    public AsyncRelayCommand TestOpenAiCommand { get; }
    public AsyncRelayCommand CheckForUpdatesCommand { get; }
    public AsyncRelayCommand InstallUpdateCommand { get; }

    public WorkspaceView CurrentView
    {
        get => _currentView;
        private set
        {
            if (!SetProperty(ref _currentView, value)) return;
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(PageDescription));
            OnPropertyChanged(nameof(MeetingsNavState));
            OnPropertyChanged(nameof(SpeakersNavState));
            OnPropertyChanged(nameof(ActionsNavState));
            OnPropertyChanged(nameof(SettingsNavState));
            OnPropertyChanged(nameof(IsRecordingSurface));
        }
    }

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        private set => SetProperty(ref _isAuthenticated, value);
    }

    public UserSession? CurrentUser
    {
        get => _currentUser;
        private set
        {
            if (!SetProperty(ref _currentUser, value)) return;
            OnPropertyChanged(nameof(UserInitials));
            OnPropertyChanged(nameof(UserFirstName));
            OnPropertyChanged(nameof(SessionModeLabel));
            OnPropertyChanged(nameof(CloudStatusLabel));
        }
    }

    public string UserInitials => CurrentUser?.Initials ?? "MA";
    public string UserFirstName => LocalizationService.Translate(CurrentUser?.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there");
    public string SessionModeLabel => LocalizationService.Translate(CurrentUser?.IsOffline == true ? "Offline workspace" : "Firebase-ready workspace");
    public string CloudStatusLabel => LocalizationService.Translate(_cloud.StatusLabel);

    public string Email { get => _email; set => SetProperty(ref _email, value); }
    public string Password { get => _password; set => SetProperty(ref _password, value); }
    public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }
    public string AuthError { get => LocalizationService.Translate(_authError); private set => SetProperty(ref _authError, value); }
    public bool IsSignUpMode
    {
        get => _isSignUpMode;
        private set
        {
            if (!SetProperty(ref _isSignUpMode, value)) return;
            OnPropertyChanged(nameof(AuthHeading));
            OnPropertyChanged(nameof(AuthSubheading));
            OnPropertyChanged(nameof(AuthSubmitLabel));
            OnPropertyChanged(nameof(AuthSwitchLabel));
        }
    }
    public bool IsAuthenticating
    {
        get => _isAuthenticating;
        private set
        {
            if (!SetProperty(ref _isAuthenticating, value)) return;
            SignInCommand.RaiseCanExecuteChanged();
            OfflineAccessCommand.RaiseCanExecuteChanged();
            ForgotPasswordCommand.RaiseCanExecuteChanged();
        }
    }
    public string AuthHeading => LocalizationService.Translate(IsSignUpMode ? "Create your workspace" : "Welcome back");
    public string AuthSubheading => LocalizationService.Translate(IsSignUpMode ? "A quieter way to remember every meeting." : "Your meetings, speakers, and next steps in one calm place.");
    public string AuthSubmitLabel => LocalizationService.Translate(IsAuthenticating ? "Connecting…" : (IsSignUpMode ? "Create account" : "Sign in"));
    public string AuthSwitchLabel => LocalizationService.Translate(IsSignUpMode ? "Already have an account? Sign in" : "New here? Create a workspace");
    public string AuthInfo { get => LocalizationService.Translate(_authInfo); private set => SetProperty(ref _authInfo, value); }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (!SetProperty(ref _searchQuery, value)) return;
            RefreshVisibleMeetings();
            OnPropertyChanged(nameof(HasSearchQuery));
            ClearSearchCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasSearchQuery => !string.IsNullOrWhiteSpace(SearchQuery);

    public string RecordingTitle { get => _recordingTitle; set => SetProperty(ref _recordingTitle, value); }
    public string SelectedMicrophone { get => _selectedMicrophone; set => SetProperty(ref _selectedMicrophone, value); }
    public string SelectedSystemAudio { get => _selectedSystemAudio; set => SetProperty(ref _selectedSystemAudio, value); }
    public string SelectedQuality { get => _selectedQuality; set => SetProperty(ref _selectedQuality, value); }
    public bool KeepLocalCopy { get => _keepLocalCopy; set => SetProperty(ref _keepLocalCopy, value); }
    public string DeviceTestStatus { get => LocalizationService.Translate(_deviceTestStatus); private set => SetProperty(ref _deviceTestStatus, value); }
    public bool IsTestingDevices
    {
        get => _isTestingDevices;
        private set
        {
            if (!SetProperty(ref _isTestingDevices, value)) return;
            TestDevicesCommand.RaiseCanExecuteChanged();
        }
    }
    public bool DeviceTested { get => _deviceTested; private set => SetProperty(ref _deviceTested, value); }

    public bool IsRecording
    {
        get => _isRecording;
        private set
        {
            if (!SetProperty(ref _isRecording, value)) return;
            PauseRecordingCommand.RaiseCanExecuteChanged();
            ResumeRecordingCommand.RaiseCanExecuteChanged();
            StopRecordingCommand.RaiseCanExecuteChanged();
            StartRecordingCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(RecordingIndicatorLabel));
            OnPropertyChanged(nameof(ShowPauseButton));
            OnPropertyChanged(nameof(ShowResumeButton));
            RefreshUpdateAvailability();
        }
    }
    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (!SetProperty(ref _isPaused, value)) return;
            PauseRecordingCommand.RaiseCanExecuteChanged();
            ResumeRecordingCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(RecordingIndicatorLabel));
            OnPropertyChanged(nameof(ShowPauseButton));
            OnPropertyChanged(nameof(ShowResumeButton));
        }
    }
    public bool IsStopping
    {
        get => _isStopping;
        private set
        {
            if (!SetProperty(ref _isStopping, value)) return;
            InstallUpdateCommand.RaiseCanExecuteChanged();
        }
    }
    public string RecordingStatus { get => LocalizationService.Translate(_recordingStatus); private set => SetProperty(ref _recordingStatus, value); }
    public string ElapsedLabel { get => _elapsedLabel; private set => SetProperty(ref _elapsedLabel, value); }
    public string ActiveSpeaker { get => LocalizationService.Translate(_activeSpeaker); private set => SetProperty(ref _activeSpeaker, value); }
    public double MicrophoneLevel { get => _microphoneLevel; private set => SetProperty(ref _microphoneLevel, value); }
    public double SystemAudioLevel { get => _systemAudioLevel; private set => SetProperty(ref _systemAudioLevel, value); }
    public string RecordingIndicatorLabel => LocalizationService.Translate(IsPaused ? "Recording paused" : "Recording live");
    public bool IsRecordingSurface => CurrentView == WorkspaceView.Recording;
    public string CaptureProvider => LocalizationService.Translate(_audio.CaptureProvider);

    public Meeting? CurrentMeeting
    {
        get => _currentMeeting;
        private set
        {
            if (!SetProperty(ref _currentMeeting, value)) return;
            OnPropertyChanged(nameof(CurrentMeetingTitle));
            SaveMeetingCommand.RaiseCanExecuteChanged();
        }
    }
    public string CurrentMeetingTitle => CurrentMeeting?.Title ?? LocalizationService.Translate("Meeting");

    public string ProcessingStage { get => LocalizationService.Translate(_processingStage); private set => SetProperty(ref _processingStage, value); }
    public string ProcessingMessage { get => LocalizationService.Translate(_processingMessage); private set => SetProperty(ref _processingMessage, value); }
    public int ProcessingPercent { get => _processingPercent; private set => SetProperty(ref _processingPercent, value); }
    public int ProcessingStageIndex { get => _processingStageIndex; private set => SetProperty(ref _processingStageIndex, value); }
    public bool IsProcessing
    {
        get => _isProcessing;
        private set
        {
            if (!SetProperty(ref _isProcessing, value)) return;
            RetryProcessingCommand.RaiseCanExecuteChanged();
            CancelProcessingCommand.RaiseCanExecuteChanged();
            StartRecordingCommand.RaiseCanExecuteChanged();
            RefreshUpdateAvailability();
        }
    }
    public bool ProcessingHasError { get => _processingHasError; private set => SetProperty(ref _processingHasError, value); }

    public string TranscriptSpeakerFilter
    {
        get => _transcriptSpeakerFilter;
        private set => SetProperty(ref _transcriptSpeakerFilter, value);
    }
    public bool IsLoadingMeetings { get => _isLoadingMeetings; private set => SetProperty(ref _isLoadingMeetings, value); }

    public bool SyncPaused
    {
        get => _syncPaused;
        set
        {
            if (!SetProperty(ref _syncPaused, value)) return;
            _cloud.IsPaused = value;
            OnPropertyChanged(nameof(CloudStatusLabel));
            OnPropertyChanged(nameof(SettingsSyncDescription));
        }
    }
    public string RetentionOption { get => _retentionOption; set => SetProperty(ref _retentionOption, value); }
    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (!SetProperty(ref _selectedTheme, value)) return;
            ThemeService.Apply(ThemeService.Parse(value));
        }
    }
    public string SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (!SetProperty(ref _selectedLanguage, value)) return;
            LocalizationService.SetLanguage(value == "English" ? "en" : "vi");
        }
    }
    public string OpenAiApiKeyInput { get => _openAiApiKeyInput; set => SetProperty(ref _openAiApiKeyInput, value); }
    public string OpenAiTranscriptionModel { get => _openAiTranscriptionModel; set => SetProperty(ref _openAiTranscriptionModel, value); }
    public string OpenAiSummaryModel { get => _openAiSummaryModel; set => SetProperty(ref _openAiSummaryModel, value); }
    public string OpenAiKeyStatus => _openAi.IsConfigured
        ? $"{LocalizationService.Translate("Configured")} · {_openAi.ApiKeySource}"
        : LocalizationService.Translate("Not configured · processing waits for a key");
    public string OpenAiConnectionStatus { get => LocalizationService.Translate(_openAiConnectionStatus); private set => SetProperty(ref _openAiConnectionStatus, value); }
    public string OpenAiProviderLabel => LocalizationService.Translate(_intelligence.ProviderLabel);
    public bool IsTestingOpenAi
    {
        get => _isTestingOpenAi;
        private set
        {
            if (!SetProperty(ref _isTestingOpenAi, value)) return;
            TestOpenAiCommand.RaiseCanExecuteChanged();
            SaveSettingsCommand.RaiseCanExecuteChanged();
        }
    }
    public bool IsSavingSettings
    {
        get => _isSavingSettings;
        private set
        {
            if (!SetProperty(ref _isSavingSettings, value)) return;
            SaveSettingsCommand.RaiseCanExecuteChanged();
            TestOpenAiCommand.RaiseCanExecuteChanged();
        }
    }
    public string AppVersionLabel => $"v{_updates.CurrentVersion}";
    public string UpdateChannelLabel => LocalizationService.Translate(_updates.Configuration.IsConfigured ? "GitHub Releases" : "Not configured");
    public string UpdateStatus { get => LocalizationService.Translate(_updateStatus); private set => SetProperty(ref _updateStatus, value); }
    public bool HasAvailableUpdate => _availableUpdate is not null;
    public string UpdateVersionLabel => _availableUpdate is null ? string.Empty : $"v{_availableUpdate.Version}";
    public string UpdateVersionDescription => $"{LocalizationService.Translate("Installing update")} {UpdateVersionLabel}".Trim();
    public UpdateProgressStage CurrentUpdateStage => _updateProgressStage;
    public int UpdateProgressPercent => _updateProgressPercent;
    public bool IsUpdateProgressIndeterminate => _updateTotalBytes is null;
    public string UpdateProgressPercentLabel => IsUpdateProgressIndeterminate ? "—" : $"{UpdateProgressPercent}%";
    public string UpdateProgressDetail
    {
        get
        {
            if (_updateProgressStage == UpdateProgressStage.Downloading)
            {
                if (_updateTotalBytes is > 0)
                    return $"{FormatBytes(_updateDownloadedBytes)} / {FormatBytes(_updateTotalBytes.Value)}";
                if (_updateDownloadedBytes > 0)
                    return FormatBytes(_updateDownloadedBytes);
            }

            return LocalizationService.Translate(_updateProgressStage switch
            {
                UpdateProgressStage.Installing => "Installing update in the background...",
                UpdateProgressStage.Restarting => "Restarting Meeting Assistant automatically...",
                _ => "Preparing a secure download..."
            });
        }
    }
    public string UpdateStageLabel => LocalizationService.Translate(_updateProgressStage switch
    {
        UpdateProgressStage.Installing => "Installing update",
        UpdateProgressStage.Restarting => "Restarting Meeting Assistant",
        _ => "Downloading update"
    });
    public bool IsUpdateSurfaceVisible
    {
        get => _isUpdateSurfaceVisible;
        private set => SetProperty(ref _isUpdateSurfaceVisible, value);
    }
    public bool IsCheckingForUpdates
    {
        get => _isCheckingForUpdates;
        private set
        {
            if (!SetProperty(ref _isCheckingForUpdates, value)) return;
            CheckForUpdatesCommand.RaiseCanExecuteChanged();
            InstallUpdateCommand.RaiseCanExecuteChanged();
        }
    }
    public bool IsInstallingUpdate
    {
        get => _isInstallingUpdate;
        private set
        {
            if (!SetProperty(ref _isInstallingUpdate, value)) return;
            CheckForUpdatesCommand.RaiseCanExecuteChanged();
            InstallUpdateCommand.RaiseCanExecuteChanged();
            StartRecordingCommand.RaiseCanExecuteChanged();
        }
    }
    public bool StartOnLogin { get => _startOnLogin; set => SetProperty(ref _startOnLogin, value); }
    public string SettingsSyncDescription => LocalizationService.Translate(SyncPaused ? "Meetings stay on this device until you turn sync back on." : "Cloud sync will run in the background when Firebase is connected.");
    public string HotkeyStatus => LocalizationService.Translate(_hotkey.IsRegistered ? "Registered · Ctrl + Shift + R" : "Unavailable · another app may own this shortcut");

    public string ToastMessage
    {
        get => LocalizationService.Translate(_toastMessage);
        private set
        {
            if (!SetProperty(ref _toastMessage, value)) return;
            OnPropertyChanged(nameof(HasToast));
        }
    }
    public bool HasToast => !string.IsNullOrWhiteSpace(ToastMessage);

    public int MeetingCount => Meetings.Count(meeting => !meeting.IsArchived);
    public int TodayCount => Meetings.Count(m => !m.IsArchived && m.StartedAt.LocalDateTime.Date == DateTime.Today);
    public int ActionCount => OpenActionCount;
    public string PageTitle => LocalizationService.Translate(CurrentView switch
    {
        WorkspaceView.Dashboard => "Meetings",
        WorkspaceView.Setup => "New recording",
        WorkspaceView.Recording => "Recording live",
        WorkspaceView.Processing => "Processing your meeting",
        WorkspaceView.Detail => CurrentMeetingTitle,
        WorkspaceView.Speakers => "Speaker profiles",
        WorkspaceView.Actions => "Next steps",
        WorkspaceView.Settings => "Settings",
        _ => "Meeting Assistant"
    });
    public string PageDescription => LocalizationService.Translate(CurrentView switch
    {
        WorkspaceView.Dashboard => "A clear record of every conversation and what happens next.",
        WorkspaceView.Setup => "Choose what to capture. Nothing joins your call.",
        WorkspaceView.Recording => "Local capture is active. Your meeting stays yours.",
        WorkspaceView.Processing => "Turning conversation into something you can use.",
        WorkspaceView.Detail => "Review the signal, correct the record, and share the next steps.",
        WorkspaceView.Speakers => "Keep names and voices consistent across your meetings.",
        WorkspaceView.Actions => "Open action items across every meeting in this workspace.",
        WorkspaceView.Settings => "Tune capture, privacy, and your workspace preferences.",
        _ => ""
    });
    public string MeetingsNavState => CurrentView is WorkspaceView.Dashboard or WorkspaceView.Setup or WorkspaceView.Recording or WorkspaceView.Processing or WorkspaceView.Detail ? "Selected" : "";
    public string SpeakersNavState => CurrentView == WorkspaceView.Speakers ? "Selected" : "";
    public string ActionsNavState => CurrentView == WorkspaceView.Actions ? "Selected" : "";
    public string SettingsNavState => CurrentView == WorkspaceView.Settings ? "Selected" : "";

    public async Task InitializeAsync()
    {
        var session = await _auth.RestoreAsync();
        if (session is null) return;
        await EnterWorkspaceAsync(session, showToast: false);
    }

    public void RefreshSystemStatus()
    {
        OnPropertyChanged(nameof(HotkeyStatus));
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (var propertyName in new[]
        {
            nameof(UserFirstName), nameof(SessionModeLabel), nameof(CloudStatusLabel), nameof(AuthError), nameof(AuthInfo),
            nameof(AuthHeading), nameof(AuthSubheading), nameof(AuthSubmitLabel), nameof(AuthSwitchLabel), nameof(DeviceTestStatus),
            nameof(RecordingStatus), nameof(ActiveSpeaker), nameof(RecordingIndicatorLabel), nameof(CaptureProvider),
            nameof(CurrentMeetingTitle), nameof(ProcessingStage), nameof(ProcessingMessage), nameof(SettingsSyncDescription),
            nameof(HotkeyStatus), nameof(ToastMessage), nameof(LastSyncLabel), nameof(PageTitle), nameof(PageDescription),
            nameof(OpenAiKeyStatus), nameof(OpenAiConnectionStatus), nameof(OpenAiProviderLabel), nameof(UpdateChannelLabel),
            nameof(UpdateStatus), nameof(UpdateStageLabel), nameof(UpdateProgressDetail), nameof(UpdateVersionDescription),
            nameof(GreetingPrefix), nameof(PlaybackStatus), nameof(LastSyncLabel)
        })
        {
            OnPropertyChanged(propertyName);
        }
    }

    public void HandleGlobalHotkey()
    {
        if (!IsAuthenticated) return;
        if (IsInstallingUpdate) return;
        if (IsRecording)
        {
            StopRecordingCommand.Execute(null);
            return;
        }

        if (CurrentView == WorkspaceView.Setup)
        {
            StartRecordingCommand.Execute(null);
            return;
        }

        OpenSetup();
        ToastMessage = "Recording setup opened · press Ctrl + Shift + R again to start";
    }

    private async Task SubmitAuthAsync()
    {
        AuthError = string.Empty;
        AuthInfo = string.Empty;
        var validation = IsSignUpMode
            ? AuthValidation.ValidateSignUp(DisplayName, Email, Password)
            : AuthValidation.ValidateSignIn(Email, Password);
        if (validation is not null)
        {
            AuthError = validation;
            return;
        }

        IsAuthenticating = true;
        var result = IsSignUpMode
            ? await _auth.SignUpAsync(DisplayName, Email, Password)
            : await _auth.SignInAsync(Email, Password);
        IsAuthenticating = false;

        if (!result.Success || result.Session is null)
        {
            AuthError = result.Error ?? "We could not sign you in. Try again.";
            return;
        }

        await EnterWorkspaceAsync(result.Session);
    }

    private async Task SignInOfflineAsync()
    {
        AuthError = string.Empty;
        AuthInfo = string.Empty;
        IsAuthenticating = true;
        var result = await _auth.SignInOfflineAsync();
        IsAuthenticating = false;
        if (!result.Success || result.Session is null)
        {
            AuthError = result.Error ?? "Offline workspace could not be opened.";
            return;
        }

        await EnterWorkspaceAsync(result.Session);
    }

    private async Task EnterWorkspaceAsync(UserSession session, bool showToast = true)
    {
        CurrentUser = session;
        IsAuthenticated = true;
        ApplyWorkspace(session);
        CurrentView = WorkspaceView.Dashboard;
        await LoadMeetingsAsync();
        await FlushOutboxAsync();
        StartUpdateMonitoring();
        if (Enum.TryParse<UpdatePolicy>(SelectedUpdatePolicy, out var policy) && policy != UpdatePolicy.Off)
            _ = CheckForUpdatesAsync(silent: true);
        if (showToast)
            ShowTimedToast(session.IsOffline ? "Offline workspace ready · your meetings are stored locally" : "Workspace ready · your session is secure");
        if (!_savedPreferences.OnboardingCompleted)
            IsOnboardingVisible = true;
    }

    private async Task SignOutAsync()
    {
        if (IsRecording && !_prompt.Confirm("Switch account", "Stop the current recording and sign out?"))
            return;
        if (IsRecording) await StopRecordingAsync();
        if (!_prompt.Confirm("Switch account", "Sign out of this workspace?"))
            return;
        await _auth.SignOutAsync();
        _updateCheckTimer.Stop();
        _updateCheckCancellation?.Cancel();
        ClearWorkspace();
        CurrentUser = null;
        IsAuthenticated = false;
        CurrentView = WorkspaceView.Auth;
        Password = string.Empty;
        AuthError = string.Empty;
    }

    private void ToggleAuthMode()
    {
        IsSignUpMode = !IsSignUpMode;
        AuthError = string.Empty;
        AuthInfo = string.Empty;
    }

    private async Task RequestPasswordResetAsync()
    {
        AuthError = string.Empty;
        AuthInfo = string.Empty;
        var result = await _auth.RequestPasswordResetAsync(Email);
        if (!result.Success)
        {
            AuthError = result.Error ?? "We could not start a password reset.";
            return;
        }

        AuthInfo = "If an account exists for this email, reset instructions are on their way.";
    }

    private async Task LoadMeetingsAsync()
    {
        IsLoadingMeetings = true;
        var localMeetings = await _repository.LoadAsync();
        var meetings = localMeetings.ToList();
        var cloudMeetings = await _cloud.LoadAsync();
        if (cloudMeetings.Count > 0)
        {
            meetings = MergeMeetings(localMeetings, cloudMeetings);
            await _repository.SaveAsync(meetings);
        }

        Meetings.Clear();
        foreach (var meeting in meetings.OrderByDescending(m => m.StartedAt))
        {
            meeting.Summary ??= new MeetingSummary();
            meeting.Summary.ImportantMoments ??= [];
            meeting.Speakers ??= [];
            meeting.Transcript ??= [];
            Meetings.Add(meeting);
        }
        RefreshVisibleMeetings();
        IsLoadingMeetings = false;
    }

    private static List<Meeting> MergeMeetings(IEnumerable<Meeting> localMeetings, IEnumerable<Meeting> cloudMeetings)
    {
        var merged = localMeetings.ToDictionary(meeting => meeting.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var cloudMeeting in cloudMeetings)
        {
            if (!merged.TryGetValue(cloudMeeting.Id, out var localMeeting)
                || cloudMeeting.UpdatedAt >= localMeeting.UpdatedAt)
            {
                merged[cloudMeeting.Id] = cloudMeeting;
            }
        }

        return merged.Values.ToList();
    }

    private void RefreshVisibleMeetings()
    {
        var query = SearchQuery.Trim();
        var results = HubMeetingFilter.Apply(
                Meetings.Where(meeting => ShowArchived || !meeting.IsArchived),
                SelectedHubFilter,
                DateTime.Today)
            .Where(meeting => string.IsNullOrWhiteSpace(query)
                || meeting.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || meeting.DateLabel.Contains(query, StringComparison.OrdinalIgnoreCase)
                || meeting.Notes?.Contains(query, StringComparison.OrdinalIgnoreCase) == true
                || meeting.Summary.Overview.Contains(query, StringComparison.OrdinalIgnoreCase)
                || meeting.Summary.ActionItems.Any(item => item.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
                || meeting.Speakers.Any(speaker => speaker.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                || meeting.Transcript.Any(segment => segment.Text.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(meeting => meeting.StartedAt)
            .ToList();

        VisibleMeetings.Clear();
        foreach (var meeting in results) VisibleMeetings.Add(meeting);
        RebuildInbox();
        OnPropertyChanged(nameof(MeetingCount));
        OnPropertyChanged(nameof(TodayCount));
        OnPropertyChanged(nameof(ActionCount));
        OnPropertyChanged(nameof(WeekCount));
        OnPropertyChanged(nameof(HasAnyMeetings));
        OnPropertyChanged(nameof(HasSearchWithoutResults));
        OnPropertyChanged(nameof(ShowEmptyWorkspace));
        OnPropertyChanged(nameof(LastSyncLabel));
        OnPropertyChanged(nameof(RecordingBytesLabel));
    }

    private void Navigate(string destination)
    {
        if (!IsAuthenticated) return;
        if (IsRecording && destination is "Meetings" or "Speakers" or "Settings" or "Actions")
        {
            CurrentView = destination switch
            {
                "Speakers" => WorkspaceView.Speakers,
                "Settings" => WorkspaceView.Settings,
                "Actions" => WorkspaceView.Actions,
                _ => WorkspaceView.Dashboard
            };
            ShowTimedToast("Recording continues in the background");
            if (CurrentView == WorkspaceView.Speakers) BuildSpeakerEditors();
            if (CurrentView == WorkspaceView.Actions) RebuildInbox();
            return;
        }

        CurrentView = destination switch
        {
            "Speakers" => WorkspaceView.Speakers,
            "Settings" => WorkspaceView.Settings,
            "Actions" => WorkspaceView.Actions,
            _ => WorkspaceView.Dashboard
        };

        if (CurrentView == WorkspaceView.Speakers) BuildSpeakerEditors();
        if (CurrentView == WorkspaceView.Actions) RebuildInbox();
    }

    private void OpenSetup()
    {
        if (!IsAuthenticated) return;
        CurrentView = WorkspaceView.Setup;
        DeviceTested = false;
        DeviceTestStatus = "Not tested yet";
    }

    private void OpenMeeting(Meeting? meeting)
    {
        if (meeting is null) return;
        CurrentMeeting = meeting;
        BuildDetailState(meeting);
        CurrentView = WorkspaceView.Detail;
    }

    private void BuildDetailState(Meeting meeting)
    {
        TranscriptFilterOptions.Clear();
        TranscriptFilterChips.Clear();
        TranscriptFilterOptions.Add("All speakers");
        TranscriptFilterChips.Add(new FilterChipViewModel("All speakers", isSelected: true));
        foreach (var speaker in meeting.Speakers.OrderBy(speaker => speaker.Name))
        {
            TranscriptFilterOptions.Add(speaker.Name);
            TranscriptFilterChips.Add(new FilterChipViewModel(speaker.Name));
        }
        TranscriptSpeakerFilter = "All speakers";
        SetTranscriptFilter("All speakers");
        BuildSpeakerEditors(meeting);
        BuildReviewEditors(meeting);
    }

    private void BuildSpeakerEditors(Meeting? meeting = null)
    {
        SpeakerEditors.Clear();
        var profiles = (meeting?.Speakers ?? Meetings.SelectMany(item => item.Speakers))
            .GroupBy(speaker => speaker.Id)
            .Select(group => group.First())
            .OrderBy(speaker => speaker.Name);
        foreach (var profile in profiles)
        {
            profile.Meetings = SpeakerMemory.Recount(Meetings, profile.Id);
            SpeakerEditors.Add(new SpeakerEditorViewModel(profile));
        }
    }

    private void SetTranscriptFilter(string filter)
    {
        TranscriptSpeakerFilter = filter;
        foreach (var chip in TranscriptFilterChips)
            chip.IsSelected = string.Equals(chip.Name, filter, StringComparison.Ordinal);
        FilteredTranscript.Clear();
        if (CurrentMeeting is null) return;
        var segments = filter == "All speakers"
            ? CurrentMeeting.Transcript
            : CurrentMeeting.Transcript.Where(segment => segment.SpeakerName == filter);
        foreach (var segment in segments) FilteredTranscript.Add(segment);
    }

    private async Task TestDevicesAsync()
    {
        IsTestingDevices = true;
        DeviceTestStatus = "Listening for both audio sources…";
        try
        {
            var result = await _audio.TestAsync(CreateAudioConfiguration());
            DeviceTested = result.Success;
            DeviceTestStatus = result.Message;
            MicrophoneLevel = result.MicrophoneLevel;
            SystemAudioLevel = result.SystemAudioLevel;
        }
        catch (Exception)
        {
            DeviceTested = false;
            DeviceTestStatus = "Could not open the selected devices. Check Windows microphone permission.";
        }
        finally
        {
            IsTestingDevices = false;
        }
    }

    private async Task StartRecordingAsync()
    {
        if (IsRecording) return;
        Directory.CreateDirectory(AppPaths.RecordingsDirectory);
        var warning = DiskBudget.WarningFor(DiskBudget.AvailableBytes(AppPaths.RecordingsDirectory), null);
        if (warning is not null && warning.Contains("not enough", StringComparison.OrdinalIgnoreCase))
        {
            ShowTimedToast(warning);
            return;
        }
        if (warning is not null) ShowTimedToast(warning);

        var configuration = CreateAudioConfiguration();

        await _audio.StartAsync(configuration);
        OnPropertyChanged(nameof(CaptureProvider));
        RecordingTitle = configuration.Title;
        ElapsedLabel = "00:00";
        RecordingStatus = "Recording live";
        ActiveSpeaker = "Listening for a speaker";
        MicrophoneLevel = 0.12;
        SystemAudioLevel = 0.08;
        IsPaused = false;
        ResetDurationWarning();
        IsRecording = true;
        CurrentView = WorkspaceView.Recording;
        _recordingTimer.Start();
        _services.TrayService.SetRecordingState(true, TimeSpan.Zero);
    }

    private async Task PauseRecordingAsync()
    {
        await _audio.PauseAsync();
        IsPaused = true;
        RecordingStatus = "Recording paused";
        ToastMessage = "Capture paused · press Resume when you are ready";
    }

    private async Task ResumeRecordingAsync()
    {
        await _audio.ResumeAsync();
        IsPaused = false;
        RecordingStatus = "Recording live";
        ToastMessage = "Capture resumed";
    }

    private async Task StopRecordingAsync()
    {
        if (!IsRecording || IsStopping) return;
        IsStopping = true;
        var recording = await _audio.StopAsync();
        _recordingTimer.Stop();
        IsRecording = false;
        IsPaused = false;
        IsStopping = false;
        _services.TrayService.SetRecordingState(false, recording.Duration);
        _lastRecording = recording;
        _processingCancellation?.Dispose();
        _processingCancellation = new CancellationTokenSource();
        CurrentView = WorkspaceView.Processing;
        await ProcessRecordingAsync(_processingCancellation.Token);
    }

    private async Task RetryProcessingAsync()
    {
        if (_lastRecording is null) return;
        _processingCancellation?.Dispose();
        _processingCancellation = new CancellationTokenSource();
        ProcessingHasError = false;
        IsProcessing = true;
        await ProcessRecordingAsync(_processingCancellation.Token);
    }

    private async Task ProcessRecordingAsync(CancellationToken cancellationToken)
    {
        if (_lastRecording is null) return;
        IsProcessing = true;
        ProcessingHasError = false;
        ProcessingPercent = 0;
        ProcessingStageIndex = 0;
        ProcessingStage = "Preparing audio";
        ProcessingMessage = "Checking microphone and system audio tracks";
        try
        {
            var progress = new Progress<ProcessingProgress>(value =>
            {
                ProcessingPercent = value.Percent;
                ProcessingStageIndex = value.StageIndex;
                ProcessingStage = value.Stage;
                ProcessingMessage = value.Message;
                UpdateProcessingSteps(value.StageIndex);
            });
            ResetProcessingSteps();
            var result = await _intelligence.ProcessAsync(_lastRecording, progress, cancellationToken);
            result.Meeting.OwnerUserId = CurrentUser?.UserId;
            result.Meeting.MicrophonePath = _lastRecording.MicrophonePath;
            result.Meeting.SystemAudioPath = _lastRecording.SystemAudioPath;
            result.Meeting.SessionDirectory = _lastRecording.SessionDirectory;
            if (!KeepLocalCopy)
                RecordingSafety.DeleteSessionAudio(_lastRecording);
            var sync = await _cloud.SyncAsync(result.Meeting, cancellationToken);
            result.Meeting.SyncStatus = sync.Label;
            result.Meeting.SyncState = sync.Success && sync.Label.Contains("Firebase", StringComparison.OrdinalIgnoreCase)
                ? SyncState.Synced
                : SyncState.Pending;
            if (result.Meeting.SyncState == SyncState.Synced)
                result.Meeting.LastSyncedAt = DateTimeOffset.Now;
            else
                await _outbox.EnqueueAsync(result.Meeting.Id);
            Meetings.Insert(0, result.Meeting);
            await _repository.SaveAsync(Meetings);
            RefreshVisibleMeetings();
            CurrentMeeting = result.Meeting;
            BuildDetailState(result.Meeting);
            IsProcessing = false;
            CurrentView = WorkspaceView.Detail;
            ShowTimedToast("Meeting ready · transcript and summary are saved locally");
        }
        catch (OperationCanceledException)
        {
            IsProcessing = false;
            ProcessingHasError = true;
            ProcessingStage = "Processing paused";
            ProcessingMessage = "Your capture is still available. Retry whenever you are ready.";
        }
        catch (OpenAiServiceException exception)
        {
            IsProcessing = false;
            ProcessingHasError = true;
            ProcessingStage = "We hit a processing problem";
            ProcessingMessage = exception.Message;
        }
        catch (Exception)
        {
            IsProcessing = false;
            ProcessingHasError = true;
            ProcessingStage = "We hit a processing problem";
            ProcessingMessage = "The meeting stays available locally. Check your connection or retry.";
        }
    }

    private void CancelProcessing()
    {
        _processingCancellation?.Cancel();
        CurrentView = WorkspaceView.Dashboard;
        ToastMessage = "Processing paused · your recording is available to retry";
    }

    private void UpdateRecordingClock()
    {
        if (!IsRecording) return;
        var elapsed = _audio.Elapsed;
        ElapsedLabel = elapsed.ToString(elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss");
        WarnIfLongRecording(elapsed);
        _services.TrayService.SetRecordingState(true, elapsed);
    }

    private void OnAudioLevelsChanged(object? sender, AudioLevelsEventArgs args)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (!IsRecording) return;
            MicrophoneLevel = args.Microphone;
            SystemAudioLevel = args.SystemAudio;
            ActiveSpeaker = args.ActiveSpeaker;
        });
    }

    private void OnGlobalHotkeyRequested(object? sender, EventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(HandleGlobalHotkey);
    }

    private async Task SaveMeetingAsync()
    {
        if (CurrentMeeting is null) return;
        ApplyReviewEditors(CurrentMeeting);
        CurrentMeeting.UpdatedAt = DateTimeOffset.Now;
        var sync = await _cloud.SyncAsync(CurrentMeeting);
        CurrentMeeting.SyncStatus = sync.Label;
        CurrentMeeting.SyncState = sync.Success && sync.Label.Contains("Firebase", StringComparison.OrdinalIgnoreCase)
            ? SyncState.Synced
            : SyncState.Pending;
        if (CurrentMeeting.SyncState == SyncState.Synced)
            CurrentMeeting.LastSyncedAt = DateTimeOffset.Now;
        else
            await _outbox.EnqueueAsync(CurrentMeeting.Id);
        await _repository.SaveAsync(Meetings);
        RefreshVisibleMeetings();
        ShowTimedToast(sync.Success ? "Meeting changes saved to your local workspace" : "Meeting changes saved locally; cloud sync will retry");
    }

    private async Task SaveSpeakerAsync()
    {
        if (SpeakerEditors.Count == 0) return;
        foreach (var editor in SpeakerEditors)
        {
            var profiles = Meetings.SelectMany(meeting => meeting.Speakers).Where(profile => profile.Id == editor.Id).ToList();
            foreach (var profile in profiles)
            {
                profile.Name = string.IsNullOrWhiteSpace(editor.Name) ? "Unknown speaker" : editor.Name.Trim();
                profile.Role = string.IsNullOrWhiteSpace(editor.Role) ? "Participant" : editor.Role.Trim();
                profile.AccentColor = string.IsNullOrWhiteSpace(editor.AccentColor) ? "#88A9FF" : editor.AccentColor.Trim();
                foreach (var segment in Meetings.SelectMany(meeting => meeting.Transcript).Where(segment => segment.SpeakerId == profile.Id))
                    segment.SpeakerName = profile.Name;
            }
        }

        if (CurrentMeeting is not null) BuildDetailState(CurrentMeeting);
        foreach (var meeting in Meetings)
        {
            meeting.UpdatedAt = DateTimeOffset.Now;
            var sync = await _cloud.SyncAsync(meeting);
            meeting.SyncStatus = sync.Label;
        }
        await _repository.SaveAsync(Meetings);
        ToastMessage = "Speaker names updated across your meetings";
    }

    private async Task SaveSettingsAsync()
    {
        if (IsSavingSettings) return;

        IsSavingSettings = true;
        try
        {
            if (!string.IsNullOrWhiteSpace(OpenAiApiKeyInput))
            {
                await _openAi.SaveUserEnvironmentAsync(
                    OpenAiApiKeyInput,
                    OpenAiTranscriptionModel,
                    OpenAiSummaryModel);
            }

            _savedPreferences.Theme = SelectedTheme;
            _savedPreferences.Language = SelectedLanguage == "English" ? "en" : "vi";
            _savedPreferences.RetentionOption = RetentionOption;
            _savedPreferences.KeepLocalCopy = KeepLocalCopy;
            _savedPreferences.SyncPaused = SyncPaused;
            _savedPreferences.StartOnLogin = StartOnLogin;
            _savedPreferences.TranscriptionModel = OpenAiTranscriptionModel;
            _savedPreferences.SummaryModel = OpenAiSummaryModel;
            _savedPreferences.TranscriptionLanguage = TranscriptionLanguage;
            _savedPreferences.UpdatePolicy = Enum.TryParse<UpdatePolicy>(SelectedUpdatePolicy, out var policy) ? policy : UpdatePolicy.Ask;
            _savedPreferences.MicrophoneId = CreateAudioConfiguration().MicrophoneId ?? "default";
            _savedPreferences.SystemAudioId = CreateAudioConfiguration().SystemAudioId ?? "default";
            _savedPreferences.Quality = SelectedQuality;
            _savedPreferences.MinimizeToTrayOnClose = MinimizeToTrayOnClose;
            await _preferences.SaveAsync(_savedPreferences);
            _startup.SetEnabled(StartOnLogin);
            RetentionPolicy.Sweep(AppPaths.RecordingsDirectory, RetentionPolicy.RetentionFor(RetentionOption), DateTimeOffset.Now);
            OnPropertyChanged(nameof(RecordingBytesLabel));
            OnPropertyChanged(nameof(HotkeyStatus));
            OnPropertyChanged(nameof(SettingsSyncDescription));
            OnPropertyChanged(nameof(OpenAiKeyStatus));
            OnPropertyChanged(nameof(OpenAiProviderLabel));
            ShowTimedToast("Settings saved · your preferences apply to the next recording");
        }
        catch (TimeoutException)
        {
            ToastMessage = "Could not save settings in time. Try again.";
        }
        catch (Exception)
        {
            ToastMessage = "Could not save settings. Check your Windows profile and try again.";
        }
        finally
        {
            IsSavingSettings = false;
        }
    }

    private async Task TestOpenAiAsync()
    {
        IsTestingOpenAi = true;
        OpenAiConnectionStatus = "Testing OpenAI connection…";
        try
        {
            var pendingApiKey = string.IsNullOrWhiteSpace(OpenAiApiKeyInput) ? null : OpenAiApiKeyInput;
            var result = await _openAiIntelligence.TestConnectionAsync(apiKeyOverride: pendingApiKey);
            OpenAiConnectionStatus = result.Message;
            OnPropertyChanged(nameof(OpenAiKeyStatus));
            OnPropertyChanged(nameof(OpenAiProviderLabel));
        }
        catch (HttpRequestException)
        {
            OpenAiConnectionStatus = "OpenAI is not reachable. Check the network and try again.";
        }
        catch (TimeoutException)
        {
            OpenAiConnectionStatus = "OpenAI took too long to respond.";
        }
        catch (Exception)
        {
            OpenAiConnectionStatus = "Could not test OpenAI. Check the key and try again.";
        }
        finally
        {
            IsTestingOpenAi = false;
        }
    }

    private bool CanInstallUpdateNow => !IsRecording && !IsProcessing && !IsStopping;

    private void StartUpdateMonitoring()
    {
        if (_updates.Configuration.IsConfigured && IsAuthenticated)
            _updateCheckTimer.Start();
    }

    private void RefreshUpdateAvailability()
    {
        InstallUpdateCommand.RaiseCanExecuteChanged();
        if (!IsAuthenticated || !CanInstallUpdateNow || _availableUpdate is null || IsCheckingForUpdates || IsInstallingUpdate)
            return;
        if (!string.Equals(SelectedUpdatePolicy, nameof(UpdatePolicy.Automatic), StringComparison.OrdinalIgnoreCase))
            return;

        _ = InstallUpdateAsync(_availableUpdate);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024)
            return $"{bytes / (1024d * 1024d):0.0} MB";
        if (bytes >= 1024)
            return $"{bytes / 1024d:0.0} KB";
        return $"{bytes:N0} B";
    }

    private void ApplyUpdateProgress(UpdateDownloadProgress progress)
    {
        _updateProgressStage = progress.Stage;
        _updateProgressPercent = Math.Clamp(progress.Percent, 0, 100);
        _updateDownloadedBytes = Math.Max(0, progress.BytesDownloaded);
        _updateTotalBytes = progress.TotalBytes;
        OnPropertyChanged(nameof(CurrentUpdateStage));
        OnPropertyChanged(nameof(UpdateProgressPercent));
        OnPropertyChanged(nameof(IsUpdateProgressIndeterminate));
        OnPropertyChanged(nameof(UpdateProgressPercentLabel));
        OnPropertyChanged(nameof(UpdateProgressDetail));
        OnPropertyChanged(nameof(UpdateStageLabel));
    }

    private Task CheckForUpdatesAsync() => CheckForUpdatesAsync(silent: false);

    private async Task CheckForUpdatesAsync(bool silent)
    {
        if (!IsAuthenticated || IsCheckingForUpdates || IsInstallingUpdate) return;
        if (string.Equals(SelectedUpdatePolicy, nameof(UpdatePolicy.Off), StringComparison.OrdinalIgnoreCase))
        {
            UpdateStatus = "Automatic updates are turned off.";
            return;
        }

        IsCheckingForUpdates = true;
        if (!silent) UpdateStatus = "Checking for updates...";
        var cancellation = new CancellationTokenSource();
        _updateCheckCancellation = cancellation;
        AppUpdateInfo? updateToInstall = null;
        try
        {
            var result = await _updates.CheckAsync(cancellation.Token);
            _availableUpdate = result.Update;
            OnPropertyChanged(nameof(HasAvailableUpdate));
            OnPropertyChanged(nameof(UpdateVersionLabel));
            OnPropertyChanged(nameof(UpdateVersionDescription));
            UpdateStatus = result.Status == UpdateCheckStatus.UpdateAvailable && result.Update is not null
                ? $"{LocalizationService.Translate("Update available")}: v{result.Update.Version}"
                : result.Message;

            if (result.Status == UpdateCheckStatus.UpdateAvailable && result.Update is not null)
            {
                var automatic = string.Equals(SelectedUpdatePolicy, nameof(UpdatePolicy.Automatic), StringComparison.OrdinalIgnoreCase);
                if (automatic && CanInstallUpdateNow)
                    updateToInstall = result.Update;
                else if (automatic)
                    UpdateStatus = "An update will install automatically when your meeting is finished.";
                else
                    UpdateStatus = $"{LocalizationService.Translate("Update available")}: v{result.Update.Version}";
            }

            InstallUpdateCommand.RaiseCanExecuteChanged();
            if (!silent && result.Status == UpdateCheckStatus.UpdateAvailable && updateToInstall is null)
                ToastMessage = result.Message;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // The window can close while a background update check is in flight.
        }
        catch (Exception)
        {
            _availableUpdate = null;
            OnPropertyChanged(nameof(HasAvailableUpdate));
            OnPropertyChanged(nameof(UpdateVersionLabel));
            OnPropertyChanged(nameof(UpdateVersionDescription));
            InstallUpdateCommand.RaiseCanExecuteChanged();
            UpdateStatus = "Could not check for updates. Check your network and try again.";
        }
        finally
        {
            if (ReferenceEquals(_updateCheckCancellation, cancellation))
                _updateCheckCancellation = null;
            cancellation.Dispose();
            IsCheckingForUpdates = false;
        }

        if (updateToInstall is not null)
            await InstallUpdateAsync(updateToInstall);
    }

    private Task InstallUpdateAsync() => InstallUpdateAsync(_availableUpdate);

    private async Task InstallUpdateAsync(AppUpdateInfo? update)
    {
        if (update is null || !CanInstallUpdateNow || IsCheckingForUpdates || IsInstallingUpdate)
            return;

        IsInstallingUpdate = true;
        IsUpdateSurfaceVisible = true;
        _updateProgressStage = UpdateProgressStage.Downloading;
        _updateProgressPercent = 0;
        _updateDownloadedBytes = 0;
        _updateTotalBytes = null;
        OnPropertyChanged(nameof(CurrentUpdateStage));
        OnPropertyChanged(nameof(UpdateProgressPercent));
        OnPropertyChanged(nameof(IsUpdateProgressIndeterminate));
        OnPropertyChanged(nameof(UpdateProgressPercentLabel));
        OnPropertyChanged(nameof(UpdateProgressDetail));
        OnPropertyChanged(nameof(UpdateStageLabel));
        UpdateStatus = "Downloading update...";

        var cancellation = new CancellationTokenSource();
        _updateCancellation = cancellation;
        var shutdownRequested = false;
        try
        {
            var progress = new Progress<UpdateDownloadProgress>(ApplyUpdateProgress);
            var result = await _updates.DownloadAndLaunchAsync(update, cancellation.Token, progress);
            UpdateStatus = result.Message;
            if (result.Success)
            {
                shutdownRequested = true;
                ToastMessage = "Update is installing automatically. Restarting Meeting Assistant.";
                System.Windows.Application.Current?.Shutdown();
            }
            else
            {
                IsUpdateSurfaceVisible = false;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            IsUpdateSurfaceVisible = false;
        }
        catch (Exception)
        {
            UpdateStatus = "Could not install the update. Try again later.";
            IsUpdateSurfaceVisible = false;
        }
        finally
        {
            if (ReferenceEquals(_updateCancellation, cancellation))
                _updateCancellation = null;
            cancellation.Dispose();
            IsInstallingUpdate = false;
            if (!shutdownRequested)
                IsUpdateSurfaceVisible = false;
        }
    }

    public void Dispose()
    {
        _recordingTimer.Stop();
        _updateCheckTimer.Stop();
        _toastTimer.Stop();
        _audio.LevelsChanged -= OnAudioLevelsChanged;
        _hotkey.ToggleRecordingRequested -= OnGlobalHotkeyRequested;
        _processingCancellation?.Cancel();
        _processingCancellation?.Dispose();
        _updateCheckCancellation?.Cancel();
        _updateCheckCancellation?.Dispose();
        _updateCancellation?.Cancel();
        _updateCancellation?.Dispose();
        LocalizationService.LanguageChanged -= OnLanguageChanged;
        DisposePlayback();
    }
}
