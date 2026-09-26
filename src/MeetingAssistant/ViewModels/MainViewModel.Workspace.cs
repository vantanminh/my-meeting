using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using MeetingAssistant.Models;
using MeetingAssistant.Services;

namespace MeetingAssistant.ViewModels;

public sealed partial class MainViewModel
{
    private readonly IAudioDeviceCatalog _devices;
    private readonly IStartupRegistration _startup;
    private readonly JsonSyncOutbox _outbox;
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(4) };
    private IUserPrompt _prompt = new SilentUserPrompt();
    private readonly MeetingPlaybackService _playback = new();
    private string _transcriptionLanguage = "auto";
    private string _updatePolicy = nameof(UpdatePolicy.Ask);
    private string _greetingPrefix = "Good morning, ";
    private string _selectedAudioSource = "Microphone";
    private string _playbackStatus = "Audio is available when a local copy was kept.";
    private string _hubFilter = "All";
    private string _settingsSection = "Account";
    private string _playbackSpeedLabel = "1×";
    private bool _showArchived;
    private bool _isOnboardingVisible;
    private bool _isConfirmingClose;
    private bool _isPasswordVisible;
    private bool _minimizeToTrayOnClose;
    private bool _isPlayingAudio;
    private int _durationWarningHour;
    private double _playbackPosition;

    public void UsePrompt(IUserPrompt prompt) => _prompt = prompt;

    public ObservableCollection<AudioDeviceInfo> MicrophoneDevices { get; } = [];
    public ObservableCollection<AudioDeviceInfo> SystemAudioDevices { get; } = [];
    public ObservableCollection<EditableLineViewModel> KeyPointEditors { get; } = [];
    public ObservableCollection<EditableLineViewModel> DecisionEditors { get; } = [];
    public ObservableCollection<EditableLineViewModel> QuestionEditors { get; } = [];
    public ObservableCollection<EditableLineViewModel> MomentEditors { get; } = [];
    public ObservableCollection<ActionEditorViewModel> ActionEditors { get; } = [];
    public ObservableCollection<DeadlineEditorViewModel> DeadlineEditors { get; } = [];
    public ObservableCollection<InboxActionViewModel> InboxActions { get; } = [];
    public ObservableCollection<ProcessingStepViewModel> ProcessingSteps { get; } = [];
    public IReadOnlyList<string> TranscriptionLanguageOptions { get; } =
        ["auto", "vi", "en"];
    public IReadOnlyList<string> UpdatePolicyOptions { get; } =
        [nameof(UpdatePolicy.Automatic), nameof(UpdatePolicy.Ask), nameof(UpdatePolicy.Off)];
    public IReadOnlyList<string> AccentColorOptions { get; } =
        ["#66E3C0", "#88A9FF", "#FF977E", "#F3C878", "#C084FC"];
    public IReadOnlyList<string> AudioSourceOptions { get; } =
        ["Microphone", "System audio"];
    public IReadOnlyList<string> HubFilterOptions { get; } =
        ["All", "This week", "Open actions", "Failed", "Unsynced"];
    public IReadOnlyList<string> SettingsSectionOptions { get; } =
        ["Account", "Capture", "AI", "Privacy", "Updates", "Shortcuts"];
    public IReadOnlyList<string> PlaybackSpeedOptions { get; } =
        ["1×", "1.5×", "2×"];
    public ObservableCollection<FilterChipViewModel> TranscriptFilterChips { get; } = [];

    public ICommand AddKeyPointCommand { get; private set; } = null!;
    public ICommand AddDecisionCommand { get; private set; } = null!;
    public ICommand AddQuestionCommand { get; private set; } = null!;
    public ICommand AddMomentCommand { get; private set; } = null!;
    public ICommand AddActionCommand { get; private set; } = null!;
    public ICommand DeleteMeetingCommand { get; private set; } = null!;
    public ICommand ArchiveMeetingCommand { get; private set; } = null!;
    public ICommand ExportMeetingCommand { get; private set; } = null!;
    public AsyncRelayCommand ResummarizeCommand { get; private set; } = null!;
    public AsyncRelayCommand ScanRecordingsCommand { get; private set; } = null!;
    public ICommand MergeSelectedSpeakersCommand { get; private set; } = null!;
    public ICommand OpenInboxMeetingCommand { get; private set; } = null!;
    public ICommand DismissToastCommand { get; private set; } = null!;
    public ICommand CompleteOnboardingCommand { get; private set; } = null!;
    public ICommand SkipOnboardingCommand { get; private set; } = null!;
    public ICommand RevealAudioFolderCommand { get; private set; } = null!;
    public ICommand ChooseRecordingsFolderCommand { get; private set; } = null!;
    public ICommand PlayAudioCommand { get; private set; } = null!;
    public ICommand PauseAudioCommand { get; private set; } = null!;
    public ICommand TogglePlaybackCommand { get; private set; } = null!;
    public ICommand SkipBackCommand { get; private set; } = null!;
    public ICommand SkipForwardCommand { get; private set; } = null!;
    public ICommand SplitTurnCommand { get; private set; } = null!;
    public ICommand MergeTurnCommand { get; private set; } = null!;
    public ICommand InsertTurnCommand { get; private set; } = null!;
    public ICommand DeleteTurnCommand { get; private set; } = null!;
    public ICommand CopyTranscriptCommand { get; private set; } = null!;
    public ICommand SetHubFilterCommand { get; private set; } = null!;
    public ICommand SetSettingsSectionCommand { get; private set; } = null!;
    public ICommand TogglePasswordVisibilityCommand { get; private set; } = null!;
    public ICommand RemoveOpenAiKeyCommand { get; private set; } = null!;
    public ICommand RemoveAssemblyAiKeyCommand { get; private set; } = null!;
    public ICommand ResetWorkspaceCommand { get; private set; } = null!;
    public ICommand AssignSpeakerCommand { get; private set; } = null!;

    public string TranscriptionLanguage
    {
        get => _transcriptionLanguage;
        set => SetProperty(ref _transcriptionLanguage, value);
    }

    public string SelectedUpdatePolicy
    {
        get => _updatePolicy;
        set => SetProperty(ref _updatePolicy, value);
    }

    public string GreetingPrefix => LocalizationService.Translate(_greetingPrefix);
    public bool ShowArchived
    {
        get => _showArchived;
        set
        {
            if (!SetProperty(ref _showArchived, value)) return;
            RefreshVisibleMeetings();
        }
    }

    public bool IsOnboardingVisible
    {
        get => _isOnboardingVisible;
        private set => SetProperty(ref _isOnboardingVisible, value);
    }

    public string SelectedAudioSource
    {
        get => _selectedAudioSource;
        set
        {
            if (!SetProperty(ref _selectedAudioSource, value)) return;
            OnPropertyChanged(nameof(CurrentAudioPath));
            RefreshPlaybackSource();
        }
    }

    public string SelectedHubFilter
    {
        get => _hubFilter;
        set
        {
            if (!SetProperty(ref _hubFilter, value)) return;
            RefreshVisibleMeetings();
        }
    }

    public string SelectedSettingsSection
    {
        get => _settingsSection;
        set
        {
            if (!SetProperty(ref _settingsSection, value)) return;
            OnPropertyChanged(nameof(ShowAccountSettings));
            OnPropertyChanged(nameof(ShowCaptureSettings));
            OnPropertyChanged(nameof(ShowAiSettings));
            OnPropertyChanged(nameof(ShowPrivacySettings));
            OnPropertyChanged(nameof(ShowUpdateSettings));
            OnPropertyChanged(nameof(ShowShortcutSettings));
        }
    }

    public bool ShowAccountSettings => SelectedSettingsSection is "Account";
    public bool ShowCaptureSettings => SelectedSettingsSection is "Capture";
    public bool ShowAiSettings => SelectedSettingsSection is "AI";
    public bool ShowPrivacySettings => SelectedSettingsSection is "Privacy";
    public bool ShowUpdateSettings => SelectedSettingsSection is "Updates";
    public bool ShowShortcutSettings => SelectedSettingsSection is "Shortcuts";

    public bool IsPasswordVisible
    {
        get => _isPasswordVisible;
        private set => SetProperty(ref _isPasswordVisible, value);
    }

    public bool MinimizeToTrayOnClose
    {
        get => _minimizeToTrayOnClose;
        set => SetProperty(ref _minimizeToTrayOnClose, value);
    }

    public bool IsPlayingAudio
    {
        get => _isPlayingAudio;
        private set => SetProperty(ref _isPlayingAudio, value);
    }

    public string PlaybackSpeedLabel
    {
        get => _playbackSpeedLabel;
        set
        {
            if (!SetProperty(ref _playbackSpeedLabel, value)) return;
            _playback.Speed = value switch
            {
                "1.5×" => 1.5,
                "2×" => 2,
                _ => 1
            };
        }
    }

    public string FirebaseProjectLabel
    {
        get
        {
            var configuration = new FirebaseConfiguration();
            return configuration.IsConfigured
                ? $"Firebase project · {configuration.ProjectId}"
                : "Firebase is not configured on this install";
        }
    }

    public string PlaybackStatus
    {
        get => LocalizationService.Translate(_playbackStatus);
        private set => SetProperty(ref _playbackStatus, value);
    }

    public double PlaybackPosition
    {
        get => _playbackPosition;
        set => SetProperty(ref _playbackPosition, value);
    }

    public string CurrentAudioPath
    {
        get
        {
            if (CurrentMeeting is null) return string.Empty;
            return SelectedAudioSource == "System audio"
                ? CurrentMeeting.SystemAudioPath ?? string.Empty
                : CurrentMeeting.MicrophonePath ?? string.Empty;
        }
    }

    public bool CanPlayAudio => CurrentMeeting?.HasAudio == true;
    public bool ShowPauseButton => IsRecording && !IsPaused;
    public bool ShowResumeButton => IsRecording && IsPaused;
    public bool HasAnyMeetings => Meetings.Any(meeting => !meeting.IsArchived);
    public bool HasSearchWithoutResults => HasSearchQuery && VisibleMeetings.Count == 0 && !IsLoadingMeetings;
    public bool ShowEmptyWorkspace => !HasSearchQuery && !HasAnyMeetings && !IsLoadingMeetings;
    public int WeekCount => WeekStats.CountThisWeek(Meetings.Where(meeting => !meeting.IsArchived), DateTime.Today);
    public int OpenActionCount => Meetings.Where(meeting => !meeting.IsArchived).Sum(meeting => meeting.Summary.ActionItems.Count(item => !item.IsComplete));
    public string RecordingBytesLabel
    {
        get
        {
            var bytes = RetentionPolicy.RecordingBytes(AppPaths.RecordingsDirectory);
            return bytes >= 1024 * 1024
                ? $"{bytes / (1024d * 1024d):0.0} MB of local audio"
                : $"{Math.Max(bytes / 1024d, 0):0} KB of local audio";
        }
    }

    public string RecordingsFolderLabel => AppPaths.RecordingsDirectory;

    public string RecordingCapacityLabel
    {
        get
        {
            var free = DiskBudget.AvailableBytes(AppPaths.RecordingsDirectory);
            var remaining = DiskBudget.RemainingRecordingTime(free);
            var hours = (int)remaining.TotalHours;
            return $"{free / (1024d * 1024d * 1024d):0.0} GB free · about {hours}h {(int)remaining.Minutes}m of recording left";
        }
    }

    public string LastSyncLabel
    {
        get
        {
            if (SyncPaused) return LocalizationService.Translate("Sync paused");
            var latest = Meetings.Select(meeting => meeting.LastSyncedAt).Where(value => value is not null).Max();
            return latest is null
                ? LocalizationService.Translate("Not synced yet")
                : LocalizationService.Translate("Last sync") + " · " + latest.Value.ToLocalTime().ToString("t");
        }
    }

    public bool IsConfirmingClose => _isConfirmingClose;

    public void InitializeWorkspaceCommands()
    {
        AddKeyPointCommand = new RelayCommand(_ => KeyPointEditors.Add(new EditableLineViewModel("")));
        AddDecisionCommand = new RelayCommand(_ => DecisionEditors.Add(new EditableLineViewModel("")));
        AddQuestionCommand = new RelayCommand(_ => QuestionEditors.Add(new EditableLineViewModel("")));
        AddMomentCommand = new RelayCommand(_ => MomentEditors.Add(new EditableLineViewModel("")));
        AddActionCommand = new RelayCommand(_ => ActionEditors.Add(new ActionEditorViewModel(new ActionItem { Owner = "Unassigned", Due = "No date" })));
        DeleteMeetingCommand = new AsyncRelayCommand(DeleteCurrentMeetingAsync, () => CurrentMeeting is not null);
        ArchiveMeetingCommand = new AsyncRelayCommand(ArchiveCurrentMeetingAsync, () => CurrentMeeting is not null);
        ExportMeetingCommand = new AsyncRelayCommand(ExportCurrentMeetingAsync, () => CurrentMeeting is not null);
        ResummarizeCommand = new AsyncRelayCommand(ResummarizeAsync, () => CurrentMeeting is not null && !IsMeetingBusy(CurrentMeeting.Id));
        ScanRecordingsCommand = new AsyncRelayCommand(ScanRecordingsAsync, () => !IsLoadingMeetings);
        MergeSelectedSpeakersCommand = new AsyncRelayCommand(MergeSpeakersAsync, () => SpeakerEditors.Count >= 2);
        OpenInboxMeetingCommand = new RelayCommand(parameter =>
        {
            if (parameter is InboxActionViewModel inbox) OpenMeeting(inbox.Meeting);
        });
        DismissToastCommand = new RelayCommand(_ => ToastMessage = string.Empty);
        CompleteOnboardingCommand = new RelayCommand(_ => FinishOnboarding());
        SkipOnboardingCommand = new RelayCommand(_ => FinishOnboarding());
        RevealAudioFolderCommand = new RelayCommand(_ => RevealAudioFolder());
        ChooseRecordingsFolderCommand = new RelayCommand(_ => ChooseRecordingsFolder());
        PlayAudioCommand = new RelayCommand(_ => PlayAudio());
        PauseAudioCommand = new RelayCommand(_ => PauseAudio());
        TogglePlaybackCommand = new RelayCommand(_ => TogglePlayback());
        SkipBackCommand = new RelayCommand(_ => SkipPlayback(TimeSpan.FromSeconds(-5)));
        SkipForwardCommand = new RelayCommand(_ => SkipPlayback(TimeSpan.FromSeconds(5)));
        SplitTurnCommand = new RelayCommand(parameter => EditTurn(parameter as TranscriptSegment, "split"));
        MergeTurnCommand = new RelayCommand(parameter => EditTurn(parameter as TranscriptSegment, "merge"));
        InsertTurnCommand = new RelayCommand(parameter => EditTurn(parameter as TranscriptSegment, "insert"));
        DeleteTurnCommand = new RelayCommand(parameter => EditTurn(parameter as TranscriptSegment, "delete"));
        CopyTranscriptCommand = new RelayCommand(_ => CopyTranscript());
        SetHubFilterCommand = new RelayCommand(parameter => SelectedHubFilter = parameter as string ?? "All");
        SetSettingsSectionCommand = new RelayCommand(parameter => SelectedSettingsSection = parameter as string ?? "Account");
        TogglePasswordVisibilityCommand = new RelayCommand(_ => IsPasswordVisible = !IsPasswordVisible);
        RemoveOpenAiKeyCommand = new RelayCommand(_ => RemoveOpenAiKey());
        RemoveAssemblyAiKeyCommand = new RelayCommand(_ => RemoveAssemblyAiKey());
        ResetWorkspaceCommand = new RelayCommand(parameter => ResetWorkspace(parameter as string));
        AssignSpeakerCommand = new RelayCommand(parameter => AssignSpeaker(parameter));
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            ToastMessage = string.Empty;
        };
        RefreshDeviceLists();
        ResetProcessingSteps();
        RecordingTitle = $"Meeting · {DateTime.Now:g}";
    }

    public void RefreshDeviceLists()
    {
        MicrophoneDevices.Clear();
        foreach (var device in _devices.ListMicrophones()) MicrophoneDevices.Add(device);
        SystemAudioDevices.Clear();
        foreach (var device in _devices.ListSystemAudio()) SystemAudioDevices.Add(device);
        if (MicrophoneDevices.All(device => device.Name != SelectedMicrophone))
            SelectedMicrophone = MicrophoneDevices.FirstOrDefault()?.Name ?? "Default microphone";
        if (SystemAudioDevices.All(device => device.Name != SelectedSystemAudio))
            SelectedSystemAudio = SystemAudioDevices.FirstOrDefault()?.Name ?? "Default system audio";
    }

    public bool ConfirmExit()
    {
        if (!IsRecording && !IsProcessing) return true;
        _isConfirmingClose = true;
        var accepted = _prompt.Confirm(
            "Meeting Assistant",
            LocalizationService.Translate(IsRecording
                ? "A recording is still active. Stop and keep the audio before closing?"
                : "Processing is still running. Close anyway? The recording stays on this computer and you can process it again from the meeting list."));
        _isConfirmingClose = false;
        return accepted;
    }

    public async Task HandleExitAsync()
    {
        if (IsRecording) await StopRecordingAsync();
    }

    internal void BindWorkspaceAfterConstruction()
    {
        InitializeWorkspaceCommands();
        _greetingPrefix = GreetingCopy.TimeOfDay(DateTimeOffset.Now);
        AppPaths.SetRecordingsDirectory(_savedPreferences.RecordingsDirectory);
        SelectedUpdatePolicy = _savedPreferences.UpdatePolicy.ToString();
        TranscriptionLanguage = string.IsNullOrWhiteSpace(_savedPreferences.TranscriptionLanguage) ? "auto" : _savedPreferences.TranscriptionLanguage;
        MinimizeToTrayOnClose = _savedPreferences.MinimizeToTrayOnClose;
        IsOnboardingVisible = !_savedPreferences.OnboardingCompleted;
        if (!string.IsNullOrWhiteSpace(_savedPreferences.MicrophoneId))
        {
            var match = MicrophoneDevices.FirstOrDefault(device => device.Id == _savedPreferences.MicrophoneId);
            if (match is not null) SelectedMicrophone = match.Name;
        }
        if (!string.IsNullOrWhiteSpace(_savedPreferences.SystemAudioId))
        {
            var match = SystemAudioDevices.FirstOrDefault(device => device.Id == _savedPreferences.SystemAudioId);
            if (match is not null) SelectedSystemAudio = match.Name;
        }
    }

    internal AudioConfiguration CreateAudioConfiguration()
    {
        var microphone = MicrophoneDevices.FirstOrDefault(device => device.Name == SelectedMicrophone)
            ?? AudioDeviceCatalog.DefaultMicrophone;
        var system = SystemAudioDevices.FirstOrDefault(device => device.Name == SelectedSystemAudio)
            ?? AudioDeviceCatalog.DefaultSystemAudio;
        return new AudioConfiguration
        {
            Title = string.IsNullOrWhiteSpace(RecordingTitle) ? $"Meeting · {DateTime.Now:g}" : RecordingTitle.Trim(),
            Microphone = microphone.Name,
            SystemAudio = system.Name,
            MicrophoneId = microphone.Id,
            SystemAudioId = system.Id,
            Quality = SelectedQuality,
            SampleRate = _devices.SampleRateForQuality(SelectedQuality),
            KeepLocalCopy = KeepLocalCopy
        };
    }

    internal void ApplyWorkspace(UserSession session)
    {
        AppPaths.SetCurrentUser(session.UserId);
        AppPaths.SetRecordingsDirectory(_savedPreferences.RecordingsDirectory);
        RetentionPolicy.Sweep(AppPaths.RecordingsDirectory, RetentionPolicy.RetentionFor(_savedPreferences.RetentionOption), DateTimeOffset.Now);
        _startup.SetEnabled(StartOnLogin);
        RefreshDeviceLists();
        OnPropertyChanged(nameof(RecordingBytesLabel));
        OnPropertyChanged(nameof(GreetingPrefix));
    }

    internal void ClearWorkspace()
    {
        Meetings.Clear();
        VisibleMeetings.Clear();
        SpeakerEditors.Clear();
        InboxActions.Clear();
        CurrentMeeting = null;
        AppPaths.SetCurrentUser("offline");
        RefreshVisibleMeetings();
    }

    internal void ShowTimedToast(string message)
    {
        ToastMessage = message;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    internal void ResetProcessingSteps()
    {
        ProcessingSteps.Clear();
        foreach (var (index, label) in new (int, string)[]
        {
            (0, "Recording saved"),
            (1, "Audio prepared"),
            (2, "Uploading and transcribing"),
            (3, "Transcript saved"),
            (4, "Writing meeting notes"),
            (5, "Meeting saved")
        })
        {
            ProcessingSteps.Add(new ProcessingStepViewModel(index, label));
        }
    }

    internal void UpdateProcessingSteps(int stageIndex)
    {
        foreach (var step in ProcessingSteps)
        {
            step.IsComplete = step.Index < stageIndex;
            step.IsCurrent = step.Index == stageIndex;
        }
    }

    internal void BuildReviewEditors(Meeting meeting)
    {
        meeting.EnsureCollections();
        ReplaceLines(KeyPointEditors, meeting.Summary.KeyPoints);
        ReplaceLines(DecisionEditors, meeting.Summary.Decisions);
        ReplaceLines(QuestionEditors, meeting.Summary.Questions);
        ReplaceLines(MomentEditors, meeting.Summary.ImportantMoments);
        ActionEditors.Clear();
        foreach (var item in meeting.Summary.ActionItems)
            ActionEditors.Add(new ActionEditorViewModel(item));
        DeadlineEditors.Clear();
        foreach (var item in meeting.Summary.Deadlines)
            DeadlineEditors.Add(new DeadlineEditorViewModel(item));
        RebuildInbox();
        OnPropertyChanged(nameof(CanPlayAudio));
        OnPropertyChanged(nameof(CurrentAudioPath));
        PlaybackStatus = meeting.HasAudio
            ? "Local audio is ready · choose a source and jump from a timestamp"
            : "Audio expired or was not kept; the transcript remains.";
    }

    internal void ApplyReviewEditors(Meeting meeting)
    {
        meeting.Summary.KeyPoints = KeyPointEditors.Select(item => item.Text.Trim()).Where(text => text.Length > 0).ToList();
        meeting.Summary.Decisions = DecisionEditors.Select(item => item.Text.Trim()).Where(text => text.Length > 0).ToList();
        var previousDecisions = meeting.Summary.DecisionItems ?? [];
        meeting.Summary.DecisionItems = meeting.Summary.Decisions.Select((text, index) => new DecisionItem
        {
            Content = text,
            Speaker = index < previousDecisions.Count ? previousDecisions[index].Speaker : null
        }).ToList();
        meeting.Summary.Questions = QuestionEditors.Select(item => item.Text.Trim()).Where(text => text.Length > 0).ToList();
        meeting.Summary.ImportantMoments = MomentEditors.Select(item => item.Text.Trim()).Where(text => text.Length > 0).ToList();
        meeting.Summary.ActionItems = ActionEditors.Select(item => item.ToItem()).ToList();
        meeting.Summary.Deadlines = DeadlineEditors.Select(item => item.ToItem()).ToList();
    }

    internal void RebuildInbox()
    {
        InboxActions.Clear();
        foreach (var meeting in Meetings.Where(item => !item.IsArchived))
        {
            foreach (var action in meeting.Summary.ActionItems.Where(item => !item.IsComplete))
                InboxActions.Add(new InboxActionViewModel(meeting, action));
        }
        OnPropertyChanged(nameof(OpenActionCount));
        OnPropertyChanged(nameof(WeekCount));
    }

    private static void ReplaceLines(ObservableCollection<EditableLineViewModel> target, IEnumerable<string> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(new EditableLineViewModel(value));
    }

    private async Task DeleteCurrentMeetingAsync()
    {
        if (CurrentMeeting is null) return;
        if (!_prompt.Confirm("Delete meeting", "Delete this meeting and its local audio? This cannot be undone."))
            return;
        RecordingSafety.DeleteMeetingAudio(CurrentMeeting);
        Meetings.Remove(CurrentMeeting);
        await _repository.SaveAsync(Meetings);
        CurrentMeeting = null;
        CurrentView = WorkspaceView.Dashboard;
        RefreshVisibleMeetings();
        ShowTimedToast("Meeting deleted from this workspace");
    }

    private async Task ArchiveCurrentMeetingAsync()
    {
        if (CurrentMeeting is null) return;
        CurrentMeeting.IsArchived = !CurrentMeeting.IsArchived;
        CurrentMeeting.Status = CurrentMeeting.IsArchived ? MeetingStatus.Archived : MeetingStatus.Ready;
        CurrentMeeting.UpdatedAt = DateTimeOffset.Now;
        await _repository.SaveAsync(Meetings);
        RefreshVisibleMeetings();
        ShowTimedToast(CurrentMeeting.IsArchived ? "Meeting archived" : "Meeting restored");
    }

    private async Task ExportCurrentMeetingAsync()
    {
        if (CurrentMeeting is null) return;
        ApplyReviewEditors(CurrentMeeting);
        var path = _prompt.SaveFile("Export meeting", "Markdown|*.md|Text|*.txt|JSON|*.json", $"{CurrentMeeting.Title}.md");
        if (string.IsNullOrWhiteSpace(path))
        {
            var fallback = Path.Combine(AppPaths.DataDirectory, $"{CurrentMeeting.Id}.md");
            await MeetingExport.WriteAsync(CurrentMeeting, fallback);
            ShowTimedToast("Meeting exported to your workspace folder");
            return;
        }

        await MeetingExport.WriteAsync(CurrentMeeting, path);
        ShowTimedToast("Meeting exported");
    }

    private async Task ResummarizeAsync()
    {
        if (CurrentMeeting is null) return;
        try
        {
            var summary = await _intelligence.SummarizeAsync(CurrentMeeting);
            CurrentMeeting.Summary = summary;
            BuildReviewEditors(CurrentMeeting);
            await SaveMeetingAsync();
            ShowTimedToast("Summary refreshed from the current transcript");
        }
        catch (OpenAiServiceException exception)
        {
            ShowTimedToast(exception.Message);
        }
        catch (MeetingProcessingException exception)
        {
            ShowTimedToast(exception.Message);
        }
    }

    private async Task MergeSpeakersAsync()
    {
        if (SpeakerEditors.Count < 2 || CurrentMeeting is null) return;
        var keep = SpeakerEditors[0];
        var drop = SpeakerEditors[1];
        foreach (var meeting in Meetings)
        {
            foreach (var segment in meeting.Transcript.Where(segment => segment.SpeakerId == drop.Id))
            {
                segment.SpeakerId = keep.Id;
                segment.SpeakerName = keep.Name;
            }

            meeting.Speakers.RemoveAll(profile => profile.Id == drop.Id);
        }

        await SaveSpeakerAsync();
        ShowTimedToast("Speakers merged");
    }

    private void ResetWorkspace(string? choice)
    {
        if (!Enum.TryParse<WorkspaceResetChoice>(choice, out var parsed)) return;
        if (!_prompt.Confirm("Reset Meeting Assistant", WorkspaceReset.Describe(parsed))) return;
        WorkspaceReset.Apply(parsed);
        if (parsed == WorkspaceResetChoice.RemoveData)
        {
            Meetings.Clear();
            RefreshVisibleMeetings();
            ShowTimedToast("Meetings and recordings were removed from this computer");
            return;
        }

        System.Windows.Application.Current.Shutdown();
    }

    private void FinishOnboarding()
    {
        IsOnboardingVisible = false;
        _savedPreferences.OnboardingCompleted = true;
        _ = _preferences.SaveAsync(_savedPreferences);
    }

    private void RevealAudioFolder()
    {
        var folder = CurrentMeeting?.SessionDirectory;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            folder = _lastRecording?.SessionDirectory;
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            folder = AppPaths.RecordingsDirectory;
        Directory.CreateDirectory(folder);
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folder}\"",
                UseShellExecute = true
            });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            ShowTimedToast(folder);
        }
    }

    private void ChooseRecordingsFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose where recordings are stored",
            InitialDirectory = Directory.Exists(AppPaths.RecordingsDirectory) ? AppPaths.RecordingsDirectory : AppPaths.RootDirectory
        };
        if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FolderName))
            return;

        AppPaths.SetRecordingsDirectory(dialog.FolderName);
        _savedPreferences.RecordingsDirectory = AppPaths.RecordingsDirectory;
        _ = _preferences.SaveAsync(_savedPreferences);
        OnPropertyChanged(nameof(RecordingsFolderLabel));
        OnPropertyChanged(nameof(RecordingCapacityLabel));
        ShowTimedToast("New recordings will be saved in the selected folder");
    }

    public void SeekTo(TranscriptSegment segment)
    {
        RefreshPlaybackSource();
        _playback.Seek(segment.Start);
        PlaybackPosition = _playback.Position.TotalSeconds;
        PlaybackStatus = $"Seek {segment.Timestamp}";
        if (!IsPlayingAudio) PlayAudio();
    }

    public void HandlePlaybackKeys(string key)
    {
        if (CurrentView != WorkspaceView.Detail) return;
        switch (key)
        {
            case "Space":
                TogglePlayback();
                break;
            case "J":
                SkipPlayback(TimeSpan.FromSeconds(-5));
                break;
            case "K":
                SkipPlayback(TimeSpan.FromSeconds(5));
                break;
        }
    }

    internal void WarnIfLongRecording(TimeSpan elapsed)
    {
        var hour = (int)elapsed.TotalHours;
        if (hour < 1 || hour <= _durationWarningHour) return;
        _durationWarningHour = hour;
        ShowTimedToast(hour >= 2
            ? "This recording is over 2 hours. Transcription will be split into 25 MB chunks."
            : "This recording is over 1 hour. Long sessions increase transcription cost.");
    }

    internal void ResetDurationWarning() => _durationWarningHour = 0;

    internal async Task FlushOutboxAsync()
    {
        var items = await _outbox.LoadAsync();
        if (items.Count == 0) return;
        var flushed = 0;
        foreach (var item in items.ToList())
        {
            var meeting = Meetings.FirstOrDefault(candidate => candidate.Id == item.MeetingId);
            if (meeting is null)
            {
                await _outbox.RemoveAsync(item.MeetingId);
                continue;
            }

            var sync = await _cloud.SyncAsync(meeting);
            meeting.SyncStatus = sync.Label;
            if (sync.Success && sync.Label.Contains("Firebase", StringComparison.OrdinalIgnoreCase))
            {
                meeting.SyncState = SyncState.Synced;
                meeting.LastSyncedAt = DateTimeOffset.Now;
                await _outbox.RemoveAsync(item.MeetingId);
                flushed++;
            }
        }

        if (flushed > 0)
        {
            await _repository.SaveAsync(Meetings);
            RefreshVisibleMeetings();
        }
    }

    internal void DisposePlayback() => _playback.Dispose();

    private void PlayAudio()
    {
        if (!RefreshPlaybackSource())
        {
            PlaybackStatus = "Audio expired or was not kept; the transcript remains.";
            return;
        }

        _playback.Play();
        IsPlayingAudio = true;
        PlaybackStatus = "Playing local audio";
    }

    private void PauseAudio()
    {
        _playback.Pause();
        IsPlayingAudio = false;
        PlaybackPosition = _playback.Position.TotalSeconds;
        PlaybackStatus = "Playback paused";
    }

    private void TogglePlayback()
    {
        if (IsPlayingAudio) PauseAudio();
        else PlayAudio();
    }

    private void SkipPlayback(TimeSpan delta)
    {
        if (!RefreshPlaybackSource()) return;
        _playback.Skip(delta);
        PlaybackPosition = _playback.Position.TotalSeconds;
        PlaybackStatus = $"Seek {TimeSpan.FromSeconds(PlaybackPosition):mm\\:ss}";
    }

    private bool RefreshPlaybackSource()
    {
        var path = CurrentAudioPath;
        if (!_playback.Open(path))
        {
            IsPlayingAudio = false;
            return false;
        }

        return true;
    }

    private void EditTurn(TranscriptSegment? segment, string action)
    {
        if (CurrentMeeting is null || segment is null) return;
        try
        {
            switch (action)
            {
                case "split":
                    TranscriptEditing.Split(CurrentMeeting.Transcript, segment, Math.Max(segment.Text.Length / 2, 1));
                    break;
                case "merge":
                    TranscriptEditing.MergeWithNext(CurrentMeeting.Transcript, segment);
                    break;
                case "insert":
                    TranscriptEditing.InsertAfter(CurrentMeeting.Transcript, segment);
                    break;
                case "delete":
                    TranscriptEditing.Delete(CurrentMeeting.Transcript, segment);
                    break;
            }

            SetTranscriptFilter(TranscriptSpeakerFilter);
            ShowTimedToast("Transcript updated");
        }
        catch (InvalidOperationException exception)
        {
            ShowTimedToast(exception.Message);
        }
    }

    private void AssignSpeaker(object? parameter)
    {
        if (parameter is not object[] values || values.Length < 2) return;
        if (values[0] is not TranscriptSegment segment || values[1] is not SpeakerProfile speaker) return;
        TranscriptEditing.AssignSpeaker(segment, speaker);
        SetTranscriptFilter(TranscriptSpeakerFilter);
    }

    private void CopyTranscript()
    {
        var markdown = TranscriptEditing.CopyMarkdown(FilteredTranscript);
        _prompt.CopyText(markdown);
        ShowTimedToast("Visible transcript copied");
    }

    private void RemoveOpenAiKey()
    {
        _openAi.ClearScopedApiKey();
        OpenAiApiKeyInput = string.Empty;
        OnPropertyChanged(nameof(OpenAiKeyStatus));
        OnPropertyChanged(nameof(OpenAiProviderLabel));
        ShowTimedToast("OpenAI key removed from this Windows profile");
    }

    private void RemoveAssemblyAiKey()
    {
        _assemblyAi.ClearScopedApiKey();
        AssemblyAiApiKeyInput = string.Empty;
        OnPropertyChanged(nameof(AssemblyAiKeyStatus));
        OnPropertyChanged(nameof(OpenAiProviderLabel));
        ShowTimedToast("AssemblyAI key removed from this Windows profile");
    }
}
