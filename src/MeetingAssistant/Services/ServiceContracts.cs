using System.Windows;
using MeetingAssistant.Models;

namespace MeetingAssistant.Services;

public sealed record AuthResult(bool Success, UserSession? Session = null, string? Error = null);

public interface IAuthService
{
    UserSession? CurrentSession { get; }
    Task<UserSession?> RestoreAsync();
    Task<AuthResult> SignInAsync(string email, string password);
    Task<AuthResult> SignUpAsync(string displayName, string email, string password);
    Task<AuthResult> RequestPasswordResetAsync(string email);
    Task<AuthResult> SignInOfflineAsync();
    Task SignOutAsync();
}

public interface IMeetingRepository
{
    Task<IReadOnlyList<Meeting>> LoadAsync();
    Task SaveAsync(IReadOnlyCollection<Meeting> meetings);
}

public sealed class AudioLevelsEventArgs : EventArgs
{
    public AudioLevelsEventArgs(double microphone, double systemAudio, string activeSpeaker)
    {
        Microphone = microphone;
        SystemAudio = systemAudio;
        ActiveSpeaker = activeSpeaker;
    }

    public double Microphone { get; }
    public double SystemAudio { get; }
    public string ActiveSpeaker { get; }
}

public interface IAudioCaptureService : IDisposable
{
    string CaptureProvider { get; }
    bool IsCapturing { get; }
    bool IsPaused { get; }
    TimeSpan Elapsed { get; }
    event EventHandler<AudioLevelsEventArgs>? LevelsChanged;
    Task StartAsync(AudioConfiguration configuration, CancellationToken cancellationToken = default);
    Task PauseAsync();
    Task ResumeAsync();
    Task<RecordingData> StopAsync();
    Task<DeviceTestResult> TestAsync(AudioConfiguration configuration, CancellationToken cancellationToken = default);
}

public sealed record ProcessingProgress(int Percent, int StageIndex, string Stage, string Message, bool ShowPercent = true)
{
    /// <summary>Composite-format template translated before formatting with <see cref="DetailArgs"/>.</summary>
    public string? Detail { get; init; }
    public object[] DetailArgs { get; init; } = [];
}
public sealed record ProcessingResult(Meeting Meeting);

public sealed class MeetingProcessingRequest
{
    public required RecordingData Recording { get; init; }
    public required Meeting Meeting { get; init; }
    public string? TranscriptionLanguage { get; init; }
    public Func<Meeting, CancellationToken, Task>? Persist { get; init; }
    public Func<bool>? StillExists { get; init; }
}

public interface IMeetingIntelligenceService
{
    string ProviderLabel { get; }

    Task<ProcessingResult> ProcessAsync(
        RecordingData recording,
        IProgress<ProcessingProgress> progress,
        CancellationToken cancellationToken = default);

    Task<ProcessingResult> ProcessMeetingAsync(
        MeetingProcessingRequest request,
        IProgress<ProcessingProgress> progress,
        CancellationToken cancellationToken = default)
        => ProcessAsync(request.Recording, progress, cancellationToken);

    Task<MeetingSummary> SummarizeAsync(
        Meeting meeting,
        CancellationToken cancellationToken = default);
}

public sealed record SyncResult(bool Success, string Label, string Detail);

public interface ICloudSyncService
{
    bool IsPaused { get; set; }
    string StatusLabel { get; }
    Task<IReadOnlyList<Meeting>> LoadAsync(CancellationToken cancellationToken = default);
    Task<SyncResult> SyncAsync(Meeting meeting, CancellationToken cancellationToken = default);
}

public interface IGlobalHotkeyService : IDisposable
{
    bool IsRegistered { get; }
    event EventHandler? ToggleRecordingRequested;
    void Attach(Window window);
}

public enum TrayState
{
    Idle,
    Recording,
    Processing,
    Failed
}

public sealed class TrayActions
{
    public required Action ShowWindow { get; init; }
    public required Action ToggleFlyout { get; init; }
    public required Action ToggleRecording { get; init; }
    public required Action Exit { get; init; }
}

public interface ITrayService : IDisposable
{
    void Initialize(TrayActions actions);
    void UpdateStatus(TrayState state, string tooltip);
    void ShowNotification(string title, string message, bool isError = false);
}

public interface IUserPrompt
{
    bool Confirm(string title, string message);
    string? SaveFile(string title, string filter, string defaultName);
    void CopyText(string text);
}

public sealed class SilentUserPrompt : IUserPrompt
{
    public string? LastCopied { get; private set; }
    public bool Confirm(string title, string message) => true;
    public string? SaveFile(string title, string filter, string defaultName) => null;
    public void CopyText(string text) => LastCopied = text;
}
