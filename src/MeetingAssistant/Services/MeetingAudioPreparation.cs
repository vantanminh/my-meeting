using System.Buffers.Binary;
using System.IO;
using System.Text;
using MeetingAssistant.Models;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MeetingAssistant.Services;

internal static class MeetingAudioPreparation
{
    public const int SampleRate = 16_000;

    public static PreparedMeetingAudio Prepare(RecordingData recording, CancellationToken cancellationToken)
    {
        var candidates = new List<(string Path, string Label)>();
        if (IsCandidate(recording.MicrophonePath))
            candidates.Add((recording.MicrophonePath!, "microphone"));
        if (IsCandidate(recording.SystemAudioPath))
            candidates.Add((recording.SystemAudioPath!, "system audio"));
        if (candidates.Count == 0)
        {
            throw new MeetingProcessingException(
                "No usable recording audio was found. Your recording is still available; record a new meeting and retry.");
        }

        var directory = Path.Combine(Path.GetTempPath(), $"MeetingAssistant-Audio-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var tracks = new List<byte[]>();
            MeetingProcessingException? firstFailure = null;
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var pcm = Normalize(candidate.Path, candidate.Label, cancellationToken);
                    if (pcm.Length > 0) tracks.Add(pcm);
                }
                catch (MeetingProcessingException exception) when (exception.Message.Contains("incomplete or unsupported", StringComparison.OrdinalIgnoreCase))
                {
                    firstFailure ??= exception;
                }
            }

            if (tracks.Count == 0)
            {
                if (firstFailure is not null) throw firstFailure;
                throw new MeetingProcessingException(
                    "No usable recording audio was found. Your recording is still available; record a new meeting and retry.");
            }

            var mixed = tracks.Count == 1 ? tracks[0] : Mix(tracks);
            var outputPath = Path.Combine(directory, "meeting-mix.wav");
            WritePcm16MonoWav(outputPath, mixed);
            var durationMs = (int)Math.Round(mixed.Length / (double)(SampleRate * 2) * 1000);
            return new PreparedMeetingAudio(directory, outputPath, durationMs);
        }
        catch
        {
            TryDeleteDirectory(directory);
            throw;
        }
    }

    private static bool IsCandidate(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path) && new FileInfo(path).Length > 44;

    private static byte[] Normalize(string path, string label, CancellationToken cancellationToken)
    {
        try
        {
            using var source = OpenSource(path);
            ISampleProvider sampleProvider = source.ToSampleProvider();
            if (sampleProvider.WaveFormat.Channels == 2)
            {
                sampleProvider = new StereoToMonoSampleProvider(sampleProvider)
                {
                    LeftVolume = 0.5f,
                    RightVolume = 0.5f
                };
            }
            else if (sampleProvider.WaveFormat.Channels > 2)
            {
                var mono = new MultiplexingSampleProvider(new[] { sampleProvider }, 1);
                mono.ConnectInputToOutput(0, 0);
                sampleProvider = mono;
            }

            if (sampleProvider.WaveFormat.SampleRate != SampleRate)
                sampleProvider = new WdlResamplingSampleProvider(sampleProvider, SampleRate);

            var pcm16 = new SampleToWaveProvider16(sampleProvider);
            using var pcmData = new MemoryStream();
            var buffer = new byte[Math.Max(pcm16.WaveFormat.AverageBytesPerSecond, 4096)];
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytesRead = pcm16.Read(buffer, 0, buffer.Length);
                if (bytesRead <= 0) break;
                pcmData.Write(buffer, 0, bytesRead);
            }

            var aligned = pcmData.ToArray();
            var length = aligned.Length - (aligned.Length % 2);
            if (length <= 0) return [];
            if (length == aligned.Length) return aligned;
            return aligned.AsSpan(0, length).ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (MeetingProcessingException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new MeetingProcessingException(
                $"The {label} audio is incomplete or unsupported. Your recording is still available; retry processing or record again.",
                exception);
        }
    }

    private static byte[] Mix(IReadOnlyList<byte[]> tracks)
    {
        var length = tracks.Max(track => track.Length);
        length -= length % 2;
        var mixed = new byte[length];
        for (var offset = 0; offset < length; offset += 2)
        {
            var sum = 0;
            foreach (var track in tracks)
            {
                if (offset + 1 >= track.Length) continue;
                sum += BinaryPrimitives.ReadInt16LittleEndian(track.AsSpan(offset, 2));
            }

            sum = Math.Clamp(sum, short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(mixed.AsSpan(offset, 2), (short)sum);
        }

        return mixed;
    }

    private static WaveStream OpenSource(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        try
        {
            return new WaveFileReader(stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static void WritePcm16MonoWav(string path, byte[] pcmData)
    {
        var alignedLength = pcmData.Length - (pcmData.Length % 2);
        if (alignedLength <= 0)
            throw new MeetingProcessingException(
                "The recording audio is incomplete or unsupported. Your recording is still available; retry processing or record again.");

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        writer.Write("RIFF"u8);
        writer.Write(36 + alignedLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);
        writer.Write((ushort)2);
        writer.Write((ushort)16);
        writer.Write("data"u8);
        writer.Write(alignedLength);
        writer.Write(pcmData, 0, alignedLength);
        writer.Flush();
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

internal sealed class PreparedMeetingAudio : IDisposable
{
    public PreparedMeetingAudio(string directory, string path, int durationMs)
    {
        Directory = directory;
        Path = path;
        DurationMs = durationMs;
    }

    private string Directory { get; }
    public string Path { get; }
    public int DurationMs { get; }

    public void Dispose()
    {
        try
        {
            if (System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
