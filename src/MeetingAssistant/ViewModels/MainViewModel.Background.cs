using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using MeetingAssistant.Services;

namespace MeetingAssistant.ViewModels;

public sealed class ProcessingFinishedEventArgs : EventArgs
{
    public ProcessingFinishedEventArgs(bool succeeded, string meetingTitle, string message)
    {
        Succeeded = succeeded;
        MeetingTitle = meetingTitle;
        Message = message;
    }

    public bool Succeeded { get; }
    public string MeetingTitle { get; }
    public string Message { get; }
}

public sealed partial class MainViewModel
{
    public const double MinUiScale = 0.8;
    public const double MaxUiScale = 1.5;
    private static readonly double[] UiScaleSteps = [0.8, 0.9, 1.0, 1.1, 1.25, 1.4, 1.5];

    private readonly DispatcherTimer _processingClock = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTimeOffset? _processingStartedAt;
    private string? _processingDetail;
    private object[] _processingDetailArgs = [];
    private string _processingElapsedLabel = "00:00";
    private double _uiScale = 1.0;
    private bool _processingClockHooked;
    private string _processingMeetingTitle = string.Empty;
    private Models.Meeting? _unopenedFinishedMeeting;

    public event EventHandler<ProcessingFinishedEventArgs>? ProcessingFinished;

    public string ProcessingDetail
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_processingDetail)) return string.Empty;
            var template = LocalizationService.Translate(_processingDetail);
            try
            {
                return string.Format(CultureInfo.CurrentCulture, template, _processingDetailArgs);
            }
            catch (FormatException)
            {
                return template;
            }
        }
    }

    public string ProcessingMeetingTitle
    {
        get => string.IsNullOrWhiteSpace(_processingMeetingTitle) ? CurrentMeetingTitle : _processingMeetingTitle;
        private set => SetProperty(ref _processingMeetingTitle, value ?? string.Empty);
    }

    public string ProcessingElapsedLabel
    {
        get => _processingElapsedLabel;
        private set => SetProperty(ref _processingElapsedLabel, value);
    }

    public string BackgroundStatusTitle
    {
        get
        {
            if (IsRecording) return LocalizationService.Translate(IsPaused ? "Recording paused" : "Recording");
            if (IsProcessing) return LocalizationService.Translate("Processing your meeting");
            if (ProcessingHasError) return LocalizationService.Translate("Unable to process this meeting.");
            return LocalizationService.Translate("Ready");
        }
    }

    public string BackgroundStatusSummary
    {
        get
        {
            if (IsRecording) return $"{BackgroundStatusTitle} · {ElapsedLabel}";
            if (IsProcessing) return $"{ProcessingStage} · {ProcessingElapsedLabel}";
            if (ProcessingHasError) return BackgroundStatusTitle;
            return "Meeting Assistant";
        }
    }

    public bool IsIdle => !IsRecording && !IsProcessing;

    public double UiScale
    {
        get => _uiScale;
        set
        {
            var clamped = Math.Round(Math.Clamp(value, MinUiScale, MaxUiScale), 2);
            if (!SetProperty(ref _uiScale, clamped)) return;
            OnPropertyChanged(nameof(UiScaleLabel));
            OnPropertyChanged(nameof(SelectedUiScaleOption));
            _savedPreferences.UiScale = clamped;
            _ = SaveUiScaleAsync();
        }
    }

    private async Task SaveUiScaleAsync()
    {
        try
        {
            await _preferences.SaveAsync(_savedPreferences);
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
        }
    }

    public string UiScaleLabel => $"{Math.Round(UiScale * 100):0}%";

    public IReadOnlyList<string> UiScaleOptions { get; } = UiScaleSteps.Select(step => $"{Math.Round(step * 100):0}%").ToList();

    public string SelectedUiScaleOption
    {
        get => UiScaleLabel;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (double.TryParse(value.TrimEnd('%', ' '), NumberStyles.Number, CultureInfo.InvariantCulture, out var percent))
                UiScale = percent / 100d;
        }
    }

    public ICommand ZoomInCommand { get; private set; } = null!;
    public ICommand ZoomOutCommand { get; private set; } = null!;
    public ICommand ResetZoomCommand { get; private set; } = null!;

    internal void InitializeBackgroundState()
    {
        _uiScale = Math.Clamp(_savedPreferences.UiScale <= 0 ? 1.0 : _savedPreferences.UiScale, MinUiScale, MaxUiScale);
        ZoomInCommand = new RelayCommand(_ => StepUiScale(+1), _ => UiScale < MaxUiScale);
        ZoomOutCommand = new RelayCommand(_ => StepUiScale(-1), _ => UiScale > MinUiScale);
        ResetZoomCommand = new RelayCommand(_ => UiScale = 1.0);
        if (!_processingClockHooked)
        {
            _processingClock.Tick += (_, _) => UpdateProcessingClock();
            _processingClockHooked = true;
        }
    }

    public void StepUiScale(int direction)
    {
        var current = UiScale;
        var next = direction > 0
            ? UiScaleSteps.FirstOrDefault(step => step > current + 0.001, MaxUiScale)
            : UiScaleSteps.LastOrDefault(step => step < current - 0.001, MinUiScale);
        UiScale = next;
    }

    private void OnProcessingStateChanged()
    {
        if (IsProcessing)
        {
            _processingStartedAt = DateTimeOffset.Now;
            ProcessingElapsedLabel = "00:00";
            _processingClock.Start();
        }
        else
        {
            _processingClock.Stop();
            _processingStartedAt = null;
        }

        RaiseBackgroundStatusChanged();
    }

    private void UpdateProcessingClock()
    {
        if (_processingStartedAt is null) return;
        var elapsed = DateTimeOffset.Now - _processingStartedAt.Value;
        ProcessingElapsedLabel = elapsed.ToString(elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss", CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(BackgroundStatusSummary));
    }

    private void SetProcessingDetail(string? template, object[]? args)
    {
        _processingDetail = template;
        _processingDetailArgs = args ?? [];
        OnPropertyChanged(nameof(ProcessingDetail));
    }

    private void RaiseProcessingFinished(bool succeeded, Models.Meeting meeting, string message, bool opened)
    {
        _unopenedFinishedMeeting = succeeded && !opened ? meeting : null;
        RaiseBackgroundStatusChanged();
        ProcessingFinished?.Invoke(this, new ProcessingFinishedEventArgs(succeeded, meeting.Title, message));
    }

    private void RaiseBackgroundStatusChanged()
    {
        OnPropertyChanged(nameof(BackgroundStatusTitle));
        OnPropertyChanged(nameof(BackgroundStatusSummary));
        OnPropertyChanged(nameof(IsIdle));
    }

    public void OpenCurrentMeetingFromTray()
    {
        if (IsRecording)
        {
            CurrentView = WorkspaceView.Recording;
            return;
        }

        if (IsProcessing || ProcessingHasError)
        {
            CurrentView = WorkspaceView.Processing;
            return;
        }

        var finished = _unopenedFinishedMeeting;
        _unopenedFinishedMeeting = null;
        if (finished is not null && Meetings.Contains(finished))
            OpenMeeting(finished);
    }
}
