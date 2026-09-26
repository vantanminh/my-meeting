using System.Buffers.Binary;
using System.IO;
using System.Text;
using MeetingAssistant.Models;
using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MeetingAssistant.Services;

internal static class MeetingAudioPreparation
{
    public const int SampleRate = 16_000;
    private const int BytesPerSecond = SampleRate * 2;
    private const int Mp3SampleRate = 32_000;
    private const int Mp3Bitrate = 48_000;
    private const double NormalizeShare = 0.7;
    private const double MixShare = 0.1;

    public static PreparedMeetingAudio Prepare(
        RecordingData recording,
        CancellationToken cancellationToken,
        IProgress<double>? progress = null,
        bool compress = true)
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
            var fractions = new double[candidates.Count];
            void ReportNormalize(int index, double fraction)
            {
                fractions[index] = fraction;
                progress?.Report(NormalizeShare * fractions.Average());
            }

            // Each track is decoded and resampled on its own thread; long meetings spend most
            // of their preparation time here.
            var jobs = candidates.Select((candidate, index) => Task.Run(() =>
            {
                var pcmPath = Path.Combine(directory, $"track-{index}.pcm");
                try
                {
                    var length = NormalizeToFile(candidate.Path, candidate.Label, pcmPath, cancellationToken, fraction => ReportNormalize(index, fraction));
                    return (Path: pcmPath, Length: length, Failure: (MeetingProcessingException?)null);
                }
                catch (MeetingProcessingException exception) when (exception.Message.Contains("incomplete or unsupported", StringComparison.OrdinalIgnoreCase))
                {
                    return (Path: pcmPath, Length: 0L, Failure: exception);
                }
            }, cancellationToken)).ToArray();

            try
            {
                Task.WaitAll(jobs, cancellationToken);
            }
            catch (AggregateException aggregate)
            {
                var inner = aggregate.Flatten().InnerExceptions;
                var canceled = inner.OfType<OperationCanceledException>().FirstOrDefault();
                if (canceled is not null) throw canceled;
                var processing = inner.OfType<MeetingProcessingException>().FirstOrDefault();
                if (processing is not null) throw processing;
                throw inner[0];
            }

            var results = jobs.Select(job => job.Result).ToList();
            var tracks = results.Where(result => result.Length > 0).ToList();
            if (tracks.Count == 0)
            {
                var firstFailure = results.Select(result => result.Failure).FirstOrDefault(failure => failure is not null);
                if (firstFailure is not null) throw firstFailure;
                throw new MeetingProcessingException(
                    "No usable recording audio was found. Your recording is still available; record a new meeting and retry.");
            }

            var wavPath = Path.Combine(directory, "meeting-mix.wav");
            var dataLength = MixToWav(tracks.Select(track => track.Path).ToList(), wavPath, cancellationToken,
                fraction => progress?.Report(NormalizeShare + MixShare * fraction));
            foreach (var track in results)
                TryDeleteFile(track.Path);

            var durationMs = (int)Math.Round(dataLength / (double)BytesPerSecond * 1000);
            var uploadPath = wavPath;
            if (compress)
            {
                var mp3Path = Path.Combine(directory, "meeting-mix.mp3");
                if (TryEncodeMp3(wavPath, mp3Path, cancellationToken,
                        fraction => progress?.Report(NormalizeShare + MixShare + (1 - NormalizeShare - MixShare) * fraction)))
                {
                    uploadPath = mp3Path;
                }
            }

            progress?.Report(1);
            return new PreparedMeetingAudio(directory, uploadPath, wavPath, durationMs);
        }
        catch
        {
            TryDeleteDirectory(directory);
            throw;
        }
    }

    private static bool IsCandidate(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path) && new FileInfo(path).Length > 44;

    private static long NormalizeToFile(
        string path,
        string label,
        string outputPath,
        CancellationToken cancellationToken,
        Action<double> reportFraction)
    {
        try
        {
            using var source = OpenWaveFile(path);
            var totalSourceBytes = Math.Max(1, source.Length);
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
            using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
            var buffer = new byte[BytesPerSecond * 4];
            long written = 0;
            var lastReported = -1.0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytesRead = pcm16.Read(buffer, 0, buffer.Length);
                if (bytesRead <= 0) break;
                output.Write(buffer, 0, bytesRead);
                written += bytesRead;
                var fraction = Math.Min(1, source.Position / (double)totalSourceBytes);
                if (fraction - lastReported >= 0.01)
                {
                    lastReported = fraction;
                    reportFraction(fraction);
                }
            }

            if (written % 2 != 0)
            {
                written--;
                output.SetLength(written);
            }

            reportFraction(1);
            return written;
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

    private static long MixToWav(
        IReadOnlyList<string> trackPaths,
        string outputPath,
        CancellationToken cancellationToken,
        Action<double> reportFraction)
    {
        var streams = trackPaths
            .Select(path => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            .ToList();
        try
        {
            var dataLength = streams.Max(stream => stream.Length);
            dataLength -= dataLength % 2;
            if (dataLength <= 0 || dataLength > int.MaxValue - 36)
            {
                throw new MeetingProcessingException(
                    "The recording audio is incomplete or unsupported. Your recording is still available; retry processing or record again.");
            }

            using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
            WriteWavHeader(output, (int)dataLength);

            const int blockSize = BytesPerSecond * 4;
            var buffers = streams.Select(_ => new byte[blockSize]).ToArray();
            var lengths = new int[streams.Count];
            var mixed = new byte[blockSize];
            long remaining = dataLength;
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var block = (int)Math.Min(blockSize, remaining);
                for (var track = 0; track < streams.Count; track++)
                    lengths[track] = ReadFully(streams[track], buffers[track], block);

                if (streams.Count == 1)
                {
                    Array.Clear(mixed, lengths[0], block - lengths[0]);
                    Buffer.BlockCopy(buffers[0], 0, mixed, 0, lengths[0]);
                }
                else
                {
                    for (var offset = 0; offset + 1 < block; offset += 2)
                    {
                        var sum = 0;
                        for (var track = 0; track < streams.Count; track++)
                        {
                            if (offset + 1 >= lengths[track]) continue;
                            sum += BinaryPrimitives.ReadInt16LittleEndian(buffers[track].AsSpan(offset, 2));
                        }

                        sum = Math.Clamp(sum, short.MinValue, short.MaxValue);
                        BinaryPrimitives.WriteInt16LittleEndian(mixed.AsSpan(offset, 2), (short)sum);
                    }
                }

                output.Write(mixed, 0, block);
                remaining -= block;
                reportFraction(1 - remaining / (double)dataLength);
            }

            return dataLength;
        }
        finally
        {
            foreach (var stream in streams) stream.Dispose();
        }
    }

    private static int ReadFully(Stream stream, byte[] buffer, int count)
    {
        var total = 0;
        while (total < count)
        {
            var read = stream.Read(buffer, total, count - total);
            if (read <= 0) break;
            total += read;
        }

        return total;
    }

    private static bool TryEncodeMp3(
        string wavPath,
        string mp3Path,
        CancellationToken cancellationToken,
        Action<double> reportFraction)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(6, 2))
            return false;

        try
        {
            MediaFoundationApi.Startup();
            using var reader = OpenWaveFile(wavPath);
            var totalBytes = Math.Max(1, reader.Length);
            var resampled = new SampleToWaveProvider16(new WdlResamplingSampleProvider(reader.ToSampleProvider(), Mp3SampleRate));
            var tracked = new ProgressWaveProvider(resampled, cancellationToken, () => reportFraction(Math.Min(1, reader.Position / (double)totalBytes)));
            MediaFoundationEncoder.EncodeToMp3(tracked, mp3Path, Mp3Bitrate);
            return File.Exists(mp3Path) && new FileInfo(mp3Path).Length > 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Some Windows editions ship without the Media Foundation MP3 encoder; the WAV
            // upload is slower but always accepted.
            MeetingProcessingLog.Write("meeting_audio_compression_skipped", "-", "mediafoundation", "skipped", null, exception.GetType().Name);
            TryDeleteFile(mp3Path);
            return false;
        }
    }

    internal static WaveStream OpenWaveFile(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        try
        {
            // WaveFileReader(Stream) does not own the stream, so the WAV stays locked
            // unless this wrapper closes it.
            return new OwnedWaveFileReader(stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private sealed class OwnedWaveFileReader : WaveFileReader
    {
        private readonly Stream _ownedStream;

        public OwnedWaveFileReader(Stream stream) : base(stream) => _ownedStream = stream;

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _ownedStream.Dispose();
        }
    }

    private sealed class ProgressWaveProvider : IWaveProvider
    {
        private readonly IWaveProvider _source;
        private readonly CancellationToken _cancellationToken;
        private readonly Action _report;
        private int _reads;

        public ProgressWaveProvider(IWaveProvider source, CancellationToken cancellationToken, Action report)
        {
            _source = source;
            _cancellationToken = cancellationToken;
            _report = report;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(byte[] buffer, int offset, int count)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var read = _source.Read(buffer, offset, count);
            if (++_reads % 16 == 0) _report();
            return read;
        }
    }

    private static void WriteWavHeader(Stream stream, int dataLength)
    {
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write(SampleRate);
        writer.Write(BytesPerSecond);
        writer.Write((ushort)2);
        writer.Write((ushort)16);
        writer.Write("data"u8);
        writer.Write(dataLength);
        writer.Flush();
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
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
    public PreparedMeetingAudio(string directory, string path, string wavPath, int durationMs)
    {
        Directory = directory;
        Path = path;
        WavPath = wavPath;
        DurationMs = durationMs;
    }

    private string Directory { get; }
    public string Path { get; }
    public string WavPath { get; }
    public int DurationMs { get; }
    public long UploadBytes => File.Exists(Path) ? new FileInfo(Path).Length : 0;

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
