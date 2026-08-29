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
}

public sealed record ProcessingProgress(int Percent, int StageIndex, string Stage, string Message);
public sealed record ProcessingResult(Meeting Meeting);

public interface IMeetingIntelligenceService
{
    string ProviderLabel { get; }

    Task<ProcessingResult> ProcessAsync(
        RecordingData recording,
        IProgress<ProcessingProgress> progress,
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

public interface ITrayService : IDisposable
{
    void Initialize(Action showWindow, Action toggleRecording);
    void SetRecordingState(bool isRecording, TimeSpan elapsed);
}
