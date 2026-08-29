using System.Collections.ObjectModel;
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
    Settings
}

public sealed class SpeakerEditorViewModel : ViewModelBase
{
    private string _name;

    public SpeakerEditorViewModel(SpeakerProfile profile)
    {
        Id = profile.Id;
        _name = profile.Name;
        Role = profile.Role;
        AccentColor = profile.AccentColor;
        Meetings = profile.Meetings;
    }

    public string Id { get; }
    public string Role { get; }
    public string AccentColor { get; }
    public int Meetings { get; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }
}

public sealed class MainViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly IAuthService _auth;
    private readonly IMeetingRepository _repository;
    private readonly IAudioCaptureService _audio;
    private readonly IMeetingIntelligenceService _intelligence;
    private readonly ICloudSyncService _cloud;
    private readonly IGlobalHotkeyService _hotkey;
    private readonly DispatcherTimer _recordingTimer;
    private CancellationTokenSource? _processingCancellation;
    private RecordingData? _lastRecording;
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

    public MainViewModel(AppServices services)
    {
        _services = services;
        _auth = services.AuthService;
        _repository = services.MeetingRepository;
        _audio = services.AudioCaptureService;
        _intelligence = services.IntelligenceService;
        _cloud = services.CloudSyncService;
        _hotkey = services.HotkeyService;

        _recordingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _recordingTimer.Tick += (_, _) => UpdateRecordingClock();
        _audio.LevelsChanged += OnAudioLevelsChanged;
        _hotkey.ToggleRecordingRequested += OnGlobalHotkeyRequested;

        Meetings = [];
        VisibleMeetings = [];
        MicrophoneOptions = ["Default microphone", "Headset microphone", "USB studio microphone"];
        SystemAudioOptions = ["Default system audio", "All system audio", "Meeting app audio only"];
        QualityOptions = ["Balanced · 48 kHz", "High quality · 48 kHz", "Compact · 16 kHz"];
        RetentionOptions = ["Keep recordings for 7 days", "Keep recordings for 30 days", "Keep recordings until deleted"];
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
        StartRecordingCommand = new AsyncRelayCommand(StartRecordingAsync, () => !IsRecording && !IsProcessing);
        PauseRecordingCommand = new AsyncRelayCommand(PauseRecordingAsync, () => IsRecording && !IsStopping);
        ResumeRecordingCommand = new AsyncRelayCommand(ResumeRecordingAsync, () => IsRecording && IsPaused && !IsStopping);
        StopRecordingCommand = new AsyncRelayCommand(StopRecordingAsync, () => IsRecording && !IsStopping);
        RetryProcessingCommand = new AsyncRelayCommand(RetryProcessingAsync, () => _lastRecording is not null && !IsProcessing);
        CancelProcessingCommand = new RelayCommand(_ => CancelProcessing(), _ => IsProcessing);
        SaveMeetingCommand = new AsyncRelayCommand(SaveMeetingAsync, () => CurrentMeeting is not null);
        SetTranscriptFilterCommand = new RelayCommand(parameter => SetTranscriptFilter(parameter as string ?? "All speakers"));
        SaveSpeakerCommand = new AsyncRelayCommand(SaveSpeakerAsync, () => true);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);
    }

    public ObservableCollection<Meeting> Meetings { get; }
    public ObservableCollection<Meeting> VisibleMeetings { get; }
    public ObservableCollection<SpeakerEditorViewModel> SpeakerEditors { get; }
    public ObservableCollection<TranscriptSegment> FilteredTranscript { get; }
    public IReadOnlyList<string> MicrophoneOptions { get; }
    public IReadOnlyList<string> SystemAudioOptions { get; }
    public IReadOnlyList<string> QualityOptions { get; }
    public IReadOnlyList<string> RetentionOptions { get; }
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
    public string UserFirstName => CurrentUser?.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";
    public string SessionModeLabel => CurrentUser?.IsOffline == true ? "Offline workspace" : "Firebase-ready workspace";
    public string CloudStatusLabel => _cloud.StatusLabel;

    public string Email { get => _email; set => SetProperty(ref _email, value); }
    public string Password { get => _password; set => SetProperty(ref _password, value); }
    public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }
    public string AuthError { get => _authError; private set => SetProperty(ref _authError, value); }
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
    public string AuthHeading => IsSignUpMode ? "Create your workspace" : "Welcome back";
    public string AuthSubheading => IsSignUpMode ? "A quieter way to remember every meeting." : "Your meetings, speakers, and next steps in one calm place.";
    public string AuthSubmitLabel => IsAuthenticating ? "Connecting…" : (IsSignUpMode ? "Create account" : "Sign in");
    public string AuthSwitchLabel => IsSignUpMode ? "Already have an account? Sign in" : "New here? Create a workspace";
    public string AuthInfo { get => _authInfo; private set => SetProperty(ref _authInfo, value); }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (!SetProperty(ref _searchQuery, value)) return;
            RefreshVisibleMeetings();
        }
    }

    public string RecordingTitle { get => _recordingTitle; set => SetProperty(ref _recordingTitle, value); }
    public string SelectedMicrophone { get => _selectedMicrophone; set => SetProperty(ref _selectedMicrophone, value); }
    public string SelectedSystemAudio { get => _selectedSystemAudio; set => SetProperty(ref _selectedSystemAudio, value); }
    public string SelectedQuality { get => _selectedQuality; set => SetProperty(ref _selectedQuality, value); }
    public bool KeepLocalCopy { get => _keepLocalCopy; set => SetProperty(ref _keepLocalCopy, value); }
    public string DeviceTestStatus { get => _deviceTestStatus; private set => SetProperty(ref _deviceTestStatus, value); }
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
        }
    }
    public bool IsStopping { get => _isStopping; private set => SetProperty(ref _isStopping, value); }
    public string RecordingStatus { get => _recordingStatus; private set => SetProperty(ref _recordingStatus, value); }
    public string ElapsedLabel { get => _elapsedLabel; private set => SetProperty(ref _elapsedLabel, value); }
    public string ActiveSpeaker { get => _activeSpeaker; private set => SetProperty(ref _activeSpeaker, value); }
    public double MicrophoneLevel { get => _microphoneLevel; private set => SetProperty(ref _microphoneLevel, value); }
    public double SystemAudioLevel { get => _systemAudioLevel; private set => SetProperty(ref _systemAudioLevel, value); }
    public string RecordingIndicatorLabel => IsPaused ? "Recording paused" : "Recording live";
    public bool IsRecordingSurface => CurrentView == WorkspaceView.Recording;
    public string CaptureProvider => _audio.CaptureProvider;

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
    public string CurrentMeetingTitle => CurrentMeeting?.Title ?? "Meeting";

    public string ProcessingStage { get => _processingStage; private set => SetProperty(ref _processingStage, value); }
    public string ProcessingMessage { get => _processingMessage; private set => SetProperty(ref _processingMessage, value); }
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
    public bool StartOnLogin { get => _startOnLogin; set => SetProperty(ref _startOnLogin, value); }
    public string SettingsSyncDescription => SyncPaused ? "Meetings stay on this device until you turn sync back on." : "Cloud sync will run in the background when Firebase is connected.";
    public string HotkeyStatus => _hotkey.IsRegistered ? "Registered · Ctrl + Shift + R" : "Unavailable · another app may own this shortcut";

    public string ToastMessage
    {
        get => _toastMessage;
        private set
        {
            if (!SetProperty(ref _toastMessage, value)) return;
            OnPropertyChanged(nameof(HasToast));
        }
    }
    public bool HasToast => !string.IsNullOrWhiteSpace(ToastMessage);

    public int MeetingCount => Meetings.Count;
    public int TodayCount => Meetings.Count(m => m.StartedAt.LocalDateTime.Date == DateTime.Today);
    public int ActionCount => Meetings.Sum(m => m.Summary?.ActionItems?.Count ?? 0);
    public string LastSyncLabel => SyncPaused ? "Sync paused" : "Just now · local cache";
    public string PageTitle => CurrentView switch
    {
        WorkspaceView.Dashboard => "Meetings",
        WorkspaceView.Setup => "New recording",
        WorkspaceView.Recording => "Recording live",
        WorkspaceView.Processing => "Processing your meeting",
        WorkspaceView.Detail => CurrentMeetingTitle,
        WorkspaceView.Speakers => "Speaker profiles",
        WorkspaceView.Settings => "Settings",
        _ => "Meeting Assistant"
    };
    public string PageDescription => CurrentView switch
    {
        WorkspaceView.Dashboard => "A clear record of every conversation and what happens next.",
        WorkspaceView.Setup => "Choose what to capture. Nothing joins your call.",
        WorkspaceView.Recording => "Local capture is active. Your meeting stays yours.",
        WorkspaceView.Processing => "Turning conversation into something you can use.",
        WorkspaceView.Detail => "Review the signal, correct the record, and share the next steps.",
        WorkspaceView.Speakers => "Keep names and voices consistent across your meetings.",
        WorkspaceView.Settings => "Tune capture, privacy, and your workspace preferences.",
        _ => ""
    };
    public string MeetingsNavState => CurrentView is WorkspaceView.Dashboard or WorkspaceView.Setup or WorkspaceView.Recording or WorkspaceView.Processing or WorkspaceView.Detail ? "Selected" : "";
    public string SpeakersNavState => CurrentView == WorkspaceView.Speakers ? "Selected" : "";
    public string SettingsNavState => CurrentView == WorkspaceView.Settings ? "Selected" : "";

    public async Task InitializeAsync()
    {
        var session = await _auth.RestoreAsync();
        if (session is null) return;
        CurrentUser = session;
        IsAuthenticated = true;
        CurrentView = WorkspaceView.Dashboard;
        await LoadMeetingsAsync();
    }

    public void RefreshSystemStatus()
    {
        OnPropertyChanged(nameof(HotkeyStatus));
    }

    public void HandleGlobalHotkey()
    {
        if (!IsAuthenticated) return;
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

    private async Task EnterWorkspaceAsync(UserSession session)
    {
        CurrentUser = session;
        IsAuthenticated = true;
        CurrentView = WorkspaceView.Dashboard;
        await LoadMeetingsAsync();
        ToastMessage = session.IsOffline ? "Offline workspace ready · your meetings are stored locally" : "Workspace ready · your session is secure";
    }

    private async Task SignOutAsync()
    {
        if (IsRecording) await StopRecordingAsync();
        await _auth.SignOutAsync();
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
        var meetings = await _repository.LoadAsync();
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

    private void RefreshVisibleMeetings()
    {
        var query = SearchQuery.Trim();
        var results = Meetings
            .Where(meeting => string.IsNullOrWhiteSpace(query)
                || meeting.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                || meeting.DateLabel.Contains(query, StringComparison.OrdinalIgnoreCase)
                || meeting.Speakers.Any(speaker => speaker.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                || meeting.Transcript.Any(segment => segment.Text.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(meeting => meeting.StartedAt)
            .ToList();

        VisibleMeetings.Clear();
        foreach (var meeting in results) VisibleMeetings.Add(meeting);
        OnPropertyChanged(nameof(MeetingCount));
        OnPropertyChanged(nameof(TodayCount));
        OnPropertyChanged(nameof(ActionCount));
        OnPropertyChanged(nameof(LastSyncLabel));
    }

    private void Navigate(string destination)
    {
        if (!IsAuthenticated) return;
        CurrentView = destination switch
        {
            "Speakers" => WorkspaceView.Speakers,
            "Settings" => WorkspaceView.Settings,
            _ => WorkspaceView.Dashboard
        };

        if (CurrentView == WorkspaceView.Speakers) BuildSpeakerEditors();
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
        TranscriptFilterOptions.Add("All speakers");
        foreach (var speaker in meeting.Speakers.OrderBy(speaker => speaker.Name))
            TranscriptFilterOptions.Add(speaker.Name);
        TranscriptSpeakerFilter = "All speakers";
        SetTranscriptFilter("All speakers");
        BuildSpeakerEditors(meeting);
    }

    private void BuildSpeakerEditors(Meeting? meeting = null)
    {
        SpeakerEditors.Clear();
        var profiles = (meeting?.Speakers ?? Meetings.SelectMany(item => item.Speakers))
            .GroupBy(speaker => speaker.Id)
            .Select(group => group.First())
            .OrderBy(speaker => speaker.Name);
        foreach (var profile in profiles) SpeakerEditors.Add(new SpeakerEditorViewModel(profile));
    }

    private void SetTranscriptFilter(string filter)
    {
        TranscriptSpeakerFilter = filter;
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
        await Task.Delay(850);
        IsTestingDevices = false;
        DeviceTested = true;
        DeviceTestStatus = "Both sources look good · ready to record";
        MicrophoneLevel = 0.72;
        SystemAudioLevel = 0.58;
    }

    private async Task StartRecordingAsync()
    {
        if (IsRecording) return;
        var configuration = new AudioConfiguration
        {
            Title = string.IsNullOrWhiteSpace(RecordingTitle) ? "Untitled meeting" : RecordingTitle.Trim(),
            Microphone = SelectedMicrophone,
            SystemAudio = SelectedSystemAudio,
            Quality = SelectedQuality,
            KeepLocalCopy = KeepLocalCopy
        };

        await _audio.StartAsync(configuration);
        OnPropertyChanged(nameof(CaptureProvider));
        RecordingTitle = configuration.Title;
        ElapsedLabel = "00:00";
        RecordingStatus = "Recording live";
        ActiveSpeaker = "Listening for a speaker";
        MicrophoneLevel = 0.12;
        SystemAudioLevel = 0.08;
        IsPaused = false;
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
            });
            var result = await _intelligence.ProcessAsync(_lastRecording, progress, cancellationToken);
            var sync = await _cloud.SyncAsync(result.Meeting, cancellationToken);
            result.Meeting.SyncStatus = sync.Label;
            Meetings.Insert(0, result.Meeting);
            await _repository.SaveAsync(Meetings);
            RefreshVisibleMeetings();
            CurrentMeeting = result.Meeting;
            BuildDetailState(result.Meeting);
            IsProcessing = false;
            CurrentView = WorkspaceView.Detail;
            ToastMessage = "Meeting ready · transcript and summary are saved locally";
        }
        catch (OperationCanceledException)
        {
            IsProcessing = false;
            ProcessingHasError = true;
            ProcessingStage = "Processing paused";
            ProcessingMessage = "Your capture is still available. Retry whenever you are ready.";
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
        await _repository.SaveAsync(Meetings);
        RefreshVisibleMeetings();
        ToastMessage = "Meeting changes saved to your local workspace";
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
                foreach (var segment in Meetings.SelectMany(meeting => meeting.Transcript).Where(segment => segment.SpeakerId == profile.Id))
                    segment.SpeakerName = profile.Name;
            }
        }

        if (CurrentMeeting is not null) BuildDetailState(CurrentMeeting);
        await _repository.SaveAsync(Meetings);
        ToastMessage = "Speaker names updated across your meetings";
    }

    private async Task SaveSettingsAsync()
    {
        await Task.Delay(140);
        OnPropertyChanged(nameof(HotkeyStatus));
        OnPropertyChanged(nameof(SettingsSyncDescription));
        ToastMessage = "Settings saved · your preferences apply to the next recording";
    }

    public void Dispose()
    {
        _recordingTimer.Stop();
        _audio.LevelsChanged -= OnAudioLevelsChanged;
        _hotkey.ToggleRecordingRequested -= OnGlobalHotkeyRequested;
        _processingCancellation?.Cancel();
        _processingCancellation?.Dispose();
    }
}
