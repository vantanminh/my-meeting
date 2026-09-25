using System.Diagnostics;
using System.IO;
using MeetingAssistant.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeetingAssistant.Services;

/// <summary>
/// Captures the default microphone and Windows loopback endpoint into separate WAV
/// tracks. A denied device is reported as a failure; capture does not pretend to
/// be running, and any partial WAV writers are closed before the error returns.
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
    private double _displayMicLevel;
    private double _displaySystemLevel;
    private int _microphoneBits = 16;
    private int _systemBits = 16;
    private string _candidateSpeaker = "You";
    private int _speakerVotes;
    private string _activeSpeaker = "Listening for a speaker";
    private long _lastLevelTick;
    private readonly ICaptureDeviceOpener _deviceOpener;

    public const string MicrophoneDeniedMessage = "Windows did not open the microphone. Allow microphone access and try again.";

    public WindowsAudioCaptureService()
        : this(new WasapiCaptureDeviceOpener())
    {
    }

    public WindowsAudioCaptureService(ICaptureDeviceOpener deviceOpener)
    {
        _deviceOpener = deviceOpener;
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
            await Task.Run(() => StartWasapiCapture(cancellationToken), cancellationToken);
            _usingFallback = false;
            CaptureProvider = "WASAPI · mic + system audio";
        }
        catch (Exception exception)
        {
            try
            {
                await Task.Run(StopWasapiCapture);
            }
            catch (Exception cleanup)
            {
                Trace.TraceWarning("Capture cleanup failed after a denied device open: {0}", cleanup.Message);
            }

            _usingFallback = false;
            IsCapturing = false;
            CaptureProvider = "WASAPI unavailable";
            if (exception is OperationCanceledException)
                throw;
            throw new InvalidOperationException(MicrophoneDeniedMessage, exception);
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
            SystemAudioPath = _systemAudioPath,
            SessionDirectory = Path.GetDirectoryName(_microphonePath)
        };
    }

    public async Task<DeviceTestResult> TestAsync(AudioConfiguration configuration, CancellationToken cancellationToken = default)
    {
        try
        {
            await StartAsync(configuration, cancellationToken);
            var started = DateTimeOffset.Now;
            var peakMic = 0.0;
            var peakSystem = 0.0;
            EventHandler<AudioLevelsEventArgs> handler = (_, args) =>
            {
                peakMic = Math.Max(peakMic, args.Microphone);
                peakSystem = Math.Max(peakSystem, args.SystemAudio);
            };
            LevelsChanged += handler;
            try
            {
                while (DateTimeOffset.Now - started < TimeSpan.FromSeconds(1.6))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Delay(80, cancellationToken);
                }
            }
            finally
            {
                LevelsChanged -= handler;
            }

            var testRecording = await StopAsync();
            RecordingSafety.DeleteSessionAudio(testRecording);
            if (_usingFallback)
                return new DeviceTestResult(false, "Windows did not open the selected devices. Check microphone permission.", peakMic, peakSystem);

            var ok = peakMic > 0.04 || peakSystem > 0.04;
            return new DeviceTestResult(
                ok,
                ok ? "Both sources are producing signal · ready to record" : "Devices opened but stayed silent. Speak or play meeting audio, then test again.",
                peakMic,
                peakSystem);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new DeviceTestResult(false, "Windows denied a capture device. Open privacy settings and allow microphone access.", 0, 0);
        }
    }

    private void StartWasapiCapture(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(AppPaths.RecordingsDirectory);
        var sessionDirectory = Path.Combine(AppPaths.RecordingsDirectory, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sessionDirectory);
        _microphonePath = Path.Combine(sessionDirectory, "microphone.wav");
        _systemAudioPath = Path.Combine(sessionDirectory, "system-audio.wav");
        _displayMicLevel = 0;
        _displaySystemLevel = 0;
        _lastLevelTick = 0;

        _deviceOpener.Open(new CaptureSession(this, _configuration, _microphonePath, _systemAudioPath), cancellationToken);
        if (_microphone is not null) _microphone.DataAvailable += MicrophoneDataAvailable;
        if (_systemAudio is not null) _systemAudio.DataAvailable += SystemAudioDataAvailable;
        _microphone?.StartRecording();
        _systemAudio?.StartRecording();
        _stopwatch.Restart();
    }

    internal void AttachOpenedDevices(
        WasapiCapture? microphone,
        WasapiLoopbackCapture? systemAudio,
        WaveFileWriter microphoneWriter,
        WaveFileWriter systemWriter,
        int microphoneBits,
        int systemBits)
    {
        _microphone = microphone;
        _systemAudio = systemAudio;
        _microphoneWriter = microphoneWriter;
        _systemWriter = systemWriter;
        _microphoneBits = microphoneBits;
        _systemBits = systemBits;
    }

    internal static MMDevice ResolveDevice(MMDeviceEnumerator enumerator, DataFlow flow, string? deviceId)
    {
        if (!string.IsNullOrWhiteSpace(deviceId) && !string.Equals(deviceId, "default", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return enumerator.GetDevice(deviceId);
            }
            catch
            {
                // Fall back to the default endpoint when a saved id is stale.
            }
        }

        return enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
    }

    private void MicrophoneDataAvailable(object? sender, WaveInEventArgs args)
    {
        if (!_isPaused)
        {
            lock (_writerLock) _microphoneWriter?.Write(args.Buffer, 0, args.BytesRecorded);
        }

        _lastMicLevel = AudioLevelMeter.FromBuffer(args.Buffer, args.BytesRecorded, _microphoneBits);
        RaiseLevels();
    }

    private void SystemAudioDataAvailable(object? sender, WaveInEventArgs args)
    {
        if (!_isPaused)
        {
            lock (_writerLock) _systemWriter?.Write(args.Buffer, 0, args.BytesRecorded);
        }

        _lastSystemLevel = AudioLevelMeter.FromBuffer(args.Buffer, args.BytesRecorded, _systemBits);
        RaiseLevels();
    }

    private void RaiseLevels()
    {
        if (_isPaused || _usingFallback) return;
        var now = Environment.TickCount64;
        if (now - _lastLevelTick < 33) return;
        _lastLevelTick = now;

        _displayMicLevel = AudioLevelMeter.Smooth(_displayMicLevel, _lastMicLevel);
        _displaySystemLevel = AudioLevelMeter.Smooth(_displaySystemLevel, _lastSystemLevel);
        var nextSpeaker = _displayMicLevel >= _displaySystemLevel ? "You" : "Meeting participant";
        if (nextSpeaker == _candidateSpeaker)
            _speakerVotes++;
        else
        {
            _candidateSpeaker = nextSpeaker;
            _speakerVotes = 1;
        }

        if (_speakerVotes >= 6)
            _activeSpeaker = _candidateSpeaker;

        LevelsChanged?.Invoke(this, new AudioLevelsEventArgs(_displayMicLevel, _displaySystemLevel, _activeSpeaker));
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

    private void ForwardFallbackLevels(object? sender, AudioLevelsEventArgs args)
        => LevelsChanged?.Invoke(this, args);

    public void Dispose()
    {
        if (IsCapturing && !_usingFallback) StopWasapiCapture();
        _fallback.LevelsChanged -= ForwardFallbackLevels;
        _fallback.Dispose();
    }
}

public interface ICaptureDeviceOpener
{
    void Open(CaptureSession session, CancellationToken cancellationToken);
}

public sealed class CaptureSession
{
    private readonly WindowsAudioCaptureService _service;

    internal CaptureSession(WindowsAudioCaptureService service, AudioConfiguration configuration, string microphonePath, string systemAudioPath)
    {
        _service = service;
        Configuration = configuration;
        MicrophonePath = microphonePath;
        SystemAudioPath = systemAudioPath;
    }

    public AudioConfiguration Configuration { get; }
    public string MicrophonePath { get; }
    public string SystemAudioPath { get; }

    public void Attach(
        WasapiCapture? microphone,
        WasapiLoopbackCapture? systemAudio,
        WaveFileWriter microphoneWriter,
        WaveFileWriter systemWriter,
        int microphoneBits,
        int systemBits)
        => _service.AttachOpenedDevices(microphone, systemAudio, microphoneWriter, systemWriter, microphoneBits, systemBits);
}

file sealed class WasapiCaptureDeviceOpener : ICaptureDeviceOpener
{
    public void Open(CaptureSession session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var enumerator = new MMDeviceEnumerator();
        var microphoneDevice = WindowsAudioCaptureService.ResolveDevice(enumerator, DataFlow.Capture, session.Configuration.MicrophoneId);
        var systemDevice = WindowsAudioCaptureService.ResolveDevice(enumerator, DataFlow.Render, session.Configuration.SystemAudioId);
        WasapiCapture? microphone = null;
        WasapiLoopbackCapture? systemAudio = null;
        WaveFileWriter? microphoneWriter = null;
        WaveFileWriter? systemWriter = null;
        try
        {
            microphone = new WasapiCapture(microphoneDevice) { ShareMode = AudioClientShareMode.Shared };
            systemAudio = systemDevice is null ? new WasapiLoopbackCapture() : new WasapiLoopbackCapture(systemDevice);
            microphoneWriter = new WaveFileWriter(session.MicrophonePath, microphone.WaveFormat);
            systemWriter = new WaveFileWriter(session.SystemAudioPath, systemAudio.WaveFormat);
            session.Attach(
                microphone,
                systemAudio,
                microphoneWriter,
                systemWriter,
                microphone.WaveFormat.BitsPerSample,
                systemAudio.WaveFormat.BitsPerSample);
        }
        catch
        {
            try { microphoneWriter?.Dispose(); } catch { }
            try { systemWriter?.Dispose(); } catch { }
            try { microphone?.Dispose(); } catch { }
            try { systemAudio?.Dispose(); } catch { }
            throw;
        }
    }
}
