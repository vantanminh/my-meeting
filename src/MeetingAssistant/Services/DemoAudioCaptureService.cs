using System.Diagnostics;
using MeetingAssistant.Models;

namespace MeetingAssistant.Services;

/// <summary>
/// Local capture preview used when no device-specific capture adapter is configured.
/// The same contract can be backed by WASAPI/NAudio without changing the recording UI.
/// </summary>
public sealed class DemoAudioCaptureService : IAudioCaptureService
{
    private readonly Random _random = new();
    private readonly Stopwatch _stopwatch = new();
    private System.Threading.Timer? _levelTimer;
    private AudioConfiguration _configuration = new();
    private bool _isPaused;

    public string CaptureProvider => "Preview fallback";
    public bool IsCapturing { get; private set; }
    public bool IsPaused => _isPaused;
    public TimeSpan Elapsed => _stopwatch.Elapsed;
    public event EventHandler<AudioLevelsEventArgs>? LevelsChanged;

    public Task StartAsync(AudioConfiguration configuration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _configuration = configuration;
        _isPaused = false;
        IsCapturing = true;
        _stopwatch.Restart();
        _levelTimer = new System.Threading.Timer(EmitLevels, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(280));
        return Task.CompletedTask;
    }

    public Task PauseAsync()
    {
        if (IsCapturing) _isPaused = true;
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        if (IsCapturing) _isPaused = false;
        return Task.CompletedTask;
    }

    public Task<RecordingData> StopAsync()
    {
        var duration = _stopwatch.Elapsed;
        _levelTimer?.Dispose();
        _levelTimer = null;
        _stopwatch.Stop();
        IsCapturing = false;
        _isPaused = false;

        return Task.FromResult(new RecordingData
        {
            Title = _configuration.Title,
            StartedAt = DateTimeOffset.Now.Subtract(duration),
            Duration = duration,
            Configuration = _configuration
        });
    }

    public async Task<DeviceTestResult> TestAsync(AudioConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await StartAsync(configuration, cancellationToken);
        await Task.Delay(400, cancellationToken);
        var mic = 0.42;
        var system = 0.31;
        await StopAsync();
        return new DeviceTestResult(true, "Preview fallback is producing levels · check Windows microphone permission for live capture", mic, system);
    }

    private void EmitLevels(object? state)
    {
        if (!IsCapturing || _isPaused) return;

        var mic = Math.Clamp(0.38 + _random.NextDouble() * 0.5, 0.05, 0.98);
        var system = Math.Clamp(0.22 + _random.NextDouble() * 0.6, 0.04, 0.92);
        var speakers = new[] { "You", "Meeting participant" };
        var speaker = speakers[_random.Next(speakers.Length)];
        LevelsChanged?.Invoke(this, new AudioLevelsEventArgs(mic, system, speaker));
    }

    public void Dispose()
    {
        _levelTimer?.Dispose();
        _stopwatch.Stop();
    }
}
