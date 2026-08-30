using MeetingAssistant.Models;
using NAudio.CoreAudioApi;

namespace MeetingAssistant.Services;

public sealed class DeviceTestResult
{
    public DeviceTestResult(bool success, string message, double microphoneLevel, double systemAudioLevel)
    {
        Success = success;
        Message = message;
        MicrophoneLevel = microphoneLevel;
        SystemAudioLevel = systemAudioLevel;
    }

    public bool Success { get; }
    public string Message { get; }
    public double MicrophoneLevel { get; }
    public double SystemAudioLevel { get; }
}

public interface IAudioDeviceCatalog
{
    IReadOnlyList<AudioDeviceInfo> ListMicrophones();
    IReadOnlyList<AudioDeviceInfo> ListSystemAudio();
    int SampleRateForQuality(string quality);
}

public sealed class AudioDeviceCatalog : IAudioDeviceCatalog
{
    public static readonly AudioDeviceInfo DefaultMicrophone = new()
    {
        Id = "default",
        Name = "Default microphone",
        Kind = "microphone",
        IsDefault = true
    };

    public static readonly AudioDeviceInfo DefaultSystemAudio = new()
    {
        Id = "default",
        Name = "Default system audio",
        Kind = "system",
        IsDefault = true
    };

    public IReadOnlyList<AudioDeviceInfo> ListMicrophones()
    {
        var devices = new List<AudioDeviceInfo> { DefaultMicrophone };
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                devices.Add(new AudioDeviceInfo
                {
                    Id = device.ID,
                    Name = device.FriendlyName,
                    Kind = "microphone",
                    IsDefault = false
                });
            }
        }
        catch
        {
            // WASAPI is unavailable in this environment; keep the default entry.
        }

        return devices;
    }

    public IReadOnlyList<AudioDeviceInfo> ListSystemAudio()
    {
        var devices = new List<AudioDeviceInfo> { DefaultSystemAudio };
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                devices.Add(new AudioDeviceInfo
                {
                    Id = device.ID,
                    Name = device.FriendlyName,
                    Kind = "system",
                    IsDefault = false
                });
            }
        }
        catch
        {
            // WASAPI is unavailable in this environment; keep the default entry.
        }

        return devices;
    }

    public int SampleRateForQuality(string quality)
    {
        if (quality.Contains("16 kHz", StringComparison.OrdinalIgnoreCase)
            || quality.Contains("Compact", StringComparison.OrdinalIgnoreCase))
            return 16_000;
        return 48_000;
    }
}
