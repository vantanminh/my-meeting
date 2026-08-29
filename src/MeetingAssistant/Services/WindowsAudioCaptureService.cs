using System.Diagnostics;
using System.IO;
using MeetingAssistant.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeetingAssistant.Services;

/// <summary>
/// Captures the default microphone and Windows loopback endpoint into separate WAV
/// tracks. If Windows denies a device, a local signal adapter keeps the journey
/// recoverable and makes the failure visible through CaptureProvider.
/// </summary>
public sealed class WindowsAudioCaptureService : IAudioCaptureService
{
    private readonly object _writerLock = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly DemoAudioCaptureService _fallback = new();
    private WasapiCapture? _microphone;
    private WasapiLoopbackCapture? _systemAudio;
    private WaveFileWriter? _microphoneWriter;
    private WaveFileWriter? _systemWriter;
    private AudioConfiguration _configuration = new();
    private DateTimeOffset _startedAt;
    private string? _microphonePath;
    private string? _systemAudioPath;
    private bool _usingFallback;
    private bool _isPaused;
    private double _lastMicLevel;
    private double _lastSystemLevel;
    private string _activeSpeaker = "Listening for a speaker";

    public WindowsAudioCaptureService()
    {
        _fallback.LevelsChanged += ForwardFallbackLevels;
    }

    public string CaptureProvider { get; private set; } = "WASAPI ready";
    public bool IsCapturing { get; private set; }
    public bool IsPaused => _isPaused;
    public TimeSpan Elapsed => _usingFallback ? _fallback.Elapsed : _stopwatch.Elapsed;
    public event EventHandler<AudioLevelsEventArgs>? LevelsChanged;

    public async Task StartAsync(AudioConfiguration configuration, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _configuration = configuration;
        _startedAt = DateTimeOffset.Now;
        _isPaused = false;
        _microphonePath = null;
        _systemAudioPath = null;

        try
        {
            var nativeStart = Task.Run(StartWasapiCapture, cancellationToken);
            var completed = await Task.WhenAny(nativeStart, Task.Delay(TimeSpan.FromSeconds(3), cancellationToken));
            if (completed != nativeStart)
            {
                _usingFallback = true;
                CaptureProvider = "Preview fallback · check device permissions";
                _ = DisposeLateNativeCaptureAsync(nativeStart);
                await _fallback.StartAsync(configuration, cancellationToken);
            }
            else
            {
                await nativeStart;
                _usingFallback = false;
                CaptureProvider = "WASAPI · mic + system audio";
            }
        }
        catch
        {
            _ = Task.Run(StopWasapiCapture);
            _usingFallback = true;
            CaptureProvider = "Preview fallback · check device permissions";
            await _fallback.StartAsync(configuration, cancellationToken);
        }

        IsCapturing = true;
    }

    public Task PauseAsync()
    {
        if (!IsCapturing) return Task.CompletedTask;
        _isPaused = true;
        if (_usingFallback) _fallback.PauseAsync();
        else _stopwatch.Stop();
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        if (!IsCapturing) return Task.CompletedTask;
        _isPaused = false;
        if (_usingFallback) _fallback.ResumeAsync();
        else _stopwatch.Start();
        return Task.CompletedTask;
    }

    public async Task<RecordingData> StopAsync()
    {
        if (_usingFallback)
        {
            var fallbackResult = await _fallback.StopAsync();
            IsCapturing = false;
            _isPaused = false;
            return fallbackResult;
        }

        var duration = _stopwatch.Elapsed;
        // Do not return the recording until both WASAPI streams and both WAV
        // writers have been fully stopped. Processing opens these files
        // immediately after StopAsync returns; returning on a timeout leaves
        // an unfinished RIFF header visible to the transcription provider.
        await Task.Run(StopWasapiCapture);
        _stopwatch.Stop();
        IsCapturing = false;
        _isPaused = false;

        return new RecordingData
        {
            Title = _configuration.Title,
            StartedAt = _startedAt,
            Duration = duration,
            Configuration = _configuration,
            MicrophonePath = _microphonePath,
            SystemAudioPath = _systemAudioPath
        };
    }

    private void StartWasapiCapture()
    {
        Directory.CreateDirectory(AppPaths.RecordingsDirectory);
        var sessionDirectory = Path.Combine(AppPaths.RecordingsDirectory, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sessionDirectory);
        _microphonePath = Path.Combine(sessionDirectory, "microphone.wav");
        _systemAudioPath = Path.Combine(sessionDirectory, "system-audio.wav");

        using var enumerator = new MMDeviceEnumerator();
        var microphoneDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
        _microphone = new WasapiCapture(microphoneDevice);
        _systemAudio = new WasapiLoopbackCapture();
        _microphoneWriter = new WaveFileWriter(_microphonePath!, _microphone.WaveFormat);
        _systemWriter = new WaveFileWriter(_systemAudioPath!, _systemAudio.WaveFormat);

        _microphone.DataAvailable += MicrophoneDataAvailable;
        _systemAudio.DataAvailable += SystemAudioDataAvailable;
        _microphone.StartRecording();
        _systemAudio.StartRecording();
        _stopwatch.Restart();
    }

    private void MicrophoneDataAvailable(object? sender, WaveInEventArgs args)
    {
        if (!_isPaused)
        {
            lock (_writerLock) _microphoneWriter?.Write(args.Buffer, 0, args.BytesRecorded);
        }

        _lastMicLevel = LevelFromBuffer(args.Buffer, args.BytesRecorded);
        RaiseLevels();
    }

    private void SystemAudioDataAvailable(object? sender, WaveInEventArgs args)
    {
        if (!_isPaused)
        {
            lock (_writerLock) _systemWriter?.Write(args.Buffer, 0, args.BytesRecorded);
        }

        _lastSystemLevel = LevelFromBuffer(args.Buffer, args.BytesRecorded);
        RaiseLevels();
    }

    private void RaiseLevels()
    {
        if (!IsCapturing || _isPaused || _usingFallback) return;
        _activeSpeaker = _lastMicLevel >= _lastSystemLevel ? "You" : "Meeting participant";
        LevelsChanged?.Invoke(this, new AudioLevelsEventArgs(_lastMicLevel, _lastSystemLevel, _activeSpeaker));
    }

    private static double LevelFromBuffer(byte[] buffer, int count)
    {
        if (count < 2) return 0.05;
        var sampleCount = Math.Min(count / 2, 480);
        long sum = 0;
        for (var index = 0; index < sampleCount * 2; index += 2)
        {
            var sample = (short)(buffer[index] | (buffer[index + 1] << 8));
            sum += Math.Abs(sample);
        }

        var average = sum / (double)(sampleCount * short.MaxValue);
        return Math.Clamp(average * 2.1, 0.02, 1.0);
    }

    private void StopWasapiCapture()
    {
        if (_microphone is not null) _microphone.DataAvailable -= MicrophoneDataAvailable;
        if (_systemAudio is not null) _systemAudio.DataAvailable -= SystemAudioDataAvailable;

        using var microphoneStopped = new ManualResetEventSlim(_microphone is null);
        using var systemStopped = new ManualResetEventSlim(_systemAudio is null);

        void OnMicrophoneStopped(object? sender, StoppedEventArgs args) => microphoneStopped.Set();
        void OnSystemStopped(object? sender, StoppedEventArgs args) => systemStopped.Set();

        if (_microphone is not null) _microphone.RecordingStopped += OnMicrophoneStopped;
        if (_systemAudio is not null) _systemAudio.RecordingStopped += OnSystemStopped;

        try { _microphone?.StopRecording(); } catch { microphoneStopped.Set(); }
        try { _systemAudio?.StopRecording(); } catch { systemStopped.Set(); }

        microphoneStopped.Wait(TimeSpan.FromSeconds(5));
        systemStopped.Wait(TimeSpan.FromSeconds(5));

        if (_microphone is not null) _microphone.RecordingStopped -= OnMicrophoneStopped;
        if (_systemAudio is not null) _systemAudio.RecordingStopped -= OnSystemStopped;

        try { _microphone?.Dispose(); } catch { }
        try { _systemAudio?.Dispose(); } catch { }
        lock (_writerLock)
        {
            try { _microphoneWriter?.Flush(); } catch { }
            try { _systemWriter?.Flush(); } catch { }
            try { _microphoneWriter?.Dispose(); } catch { }
            try { _systemWriter?.Dispose(); } catch { }
        }

        _microphone = null;
        _systemAudio = null;
        _microphoneWriter = null;
        _systemWriter = null;
    }

    private async Task DisposeLateNativeCaptureAsync(Task nativeStart)
    {
        try
        {
            await nativeStart;
        }
        catch
        {
            // The fallback is already active; a failed native attempt is expected.
        }
        finally
        {
            StopWasapiCapture();
        }
    }

    private void ForwardFallbackLevels(object? sender, AudioLevelsEventArgs args)
        => LevelsChanged?.Invoke(this, args);

    public void Dispose()
    {
        if (IsCapturing && !_usingFallback) StopWasapiCapture();
        _fallback.LevelsChanged -= ForwardFallbackLevels;
        _fallback.Dispose();
    }
}
