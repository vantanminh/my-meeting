using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MeetingAssistant.Models;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MeetingAssistant.Services;

public sealed record OpenAiConnectionResult(bool Success, string Message);

public sealed class OpenAiMeetingIntelligenceService : IMeetingIntelligenceService
{
    private const int ProviderSampleRate = 16_000;
    private const long MaxTranscriptionFileBytes = 24L * 1024 * 1024;
    private const int ChunkHeaderSafetyBytes = 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly OpenAiConfiguration _configuration;
    private readonly DemoMeetingIntelligenceService _localFallback;
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _connectionTimeout;

    public OpenAiMeetingIntelligenceService(
        OpenAiConfiguration configuration,
        DemoMeetingIntelligenceService? localFallback = null,
        HttpClient? httpClient = null,
        TimeSpan? connectionTimeout = null)
    {
        _configuration = configuration;
        _localFallback = localFallback ?? new DemoMeetingIntelligenceService();
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _connectionTimeout = connectionTimeout ?? TimeSpan.FromSeconds(12);
        if (_connectionTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(connectionTimeout), "The connection timeout must be positive.");
    }

    public string ProviderLabel => _configuration.IsConfigured
        ? $"OpenAI · {_configuration.TranscriptionModel} + {_configuration.SummaryModel}"
        : "Local demo · OpenAI key not configured";

    public async Task<ProcessingResult> ProcessAsync(
        RecordingData recording,
        IProgress<ProcessingProgress> progress,
        CancellationToken cancellationToken = default)
    {
        var tracks = AudioTracks(recording).ToList();
        if (!_configuration.IsConfigured)
        {
            throw new OpenAiServiceException(
                "Add an OpenAI API key in Settings to transcribe this recording. Your audio is still saved locally.");
        }
        if (tracks.Count == 0)
        {
            throw new OpenAiServiceException(
                "No usable recording audio was found. Your recording is still available; record a new meeting and retry.");
        }

        progress.Report(new ProcessingProgress(3, 0, "Preparing audio", "Checking microphone and system audio tracks"));
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        progress.Report(new ProcessingProgress(8, 0, "Preparing audio", "Converting captured tracks to provider-compatible PCM WAV"));
        using var preparedTracks = await Task.Run(
            () => PrepareAudioTracks(tracks, cancellationToken),
            cancellationToken);

        var transcriptSegments = new List<TranscriptSegment>();
        for (var index = 0; index < preparedTracks.Tracks.Count; index++)
        {
            var track = preparedTracks.Tracks[index];
            var percent = 15 + (int)Math.Round(index / (double)preparedTracks.Tracks.Count * 35);
            progress.Report(new ProcessingProgress(percent, 1, "Transcribing", $"Transcribing {track.DisplayName.ToLowerInvariant()}"));
            var segments = await TranscribeAsync(track, recording.Duration, cancellationToken);
            transcriptSegments.AddRange(segments);
        }

        if (transcriptSegments.Count == 0)
            throw new OpenAiServiceException("OpenAI did not return any transcript text.");

        transcriptSegments = TranscriptRepair.CollapseOverlaps(
            transcriptSegments.OrderBy(segment => segment.Start).ThenBy(segment => segment.SpeakerName).ToList()).ToList();
        progress.Report(new ProcessingProgress(58, 2, "Recognizing speakers", "Grouping microphone and system-audio turns"));
        var speakers = transcriptSegments
            .GroupBy(segment => segment.SpeakerName, StringComparer.OrdinalIgnoreCase)
            .Select(group => new SpeakerProfile
            {
                Id = StableSpeakerId(group.Key),
                Name = group.Key,
                Role = group.Key.Contains("You", StringComparison.OrdinalIgnoreCase) ? "Host" : "Participant",
                AccentColor = group.Key.Contains("You", StringComparison.OrdinalIgnoreCase) ? "#66E3C0" : "#88A9FF",
                Meetings = 1
            })
            .ToList();

        progress.Report(new ProcessingProgress(67, 3, "Finding the signal", "Asking the selected model for decisions and next steps"));
        var summary = await SummarizeAsync(recording, transcriptSegments, cancellationToken);
        progress.Report(new ProcessingProgress(92, 4, "Finishing meeting", "Saving the OpenAI transcript and summary locally"));

        var meeting = new Meeting
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = string.IsNullOrWhiteSpace(recording.Title) ? "Untitled meeting" : recording.Title.Trim(),
            StartedAt = recording.StartedAt,
            Duration = recording.Duration,
            ParticipantCount = speakers.Count,
            Status = MeetingStatus.Ready,
            SyncStatus = "OpenAI · ready to sync",
            AudioSources = $"{recording.Configuration.Microphone} + {recording.Configuration.SystemAudio}",
            Speakers = speakers,
            Transcript = transcriptSegments,
            Summary = summary,
            MicrophonePath = recording.MicrophonePath,
            SystemAudioPath = recording.SystemAudioPath,
            SessionDirectory = recording.SessionDirectory
        };

        progress.Report(new ProcessingProgress(100, 4, "Ready", "Your meeting is ready to review"));
        return new ProcessingResult(meeting);
    }

    public async Task<MeetingSummary> SummarizeAsync(Meeting meeting, CancellationToken cancellationToken = default)
    {
        if (!_configuration.IsConfigured)
            throw new OpenAiServiceException("Add an OpenAI API key in Settings to refresh this summary.");
        meeting.EnsureCollections();
        var recording = new RecordingData
        {
            Title = meeting.Title,
            StartedAt = meeting.StartedAt,
            Duration = meeting.Duration
        };
        return await SummarizeAsync(recording, meeting.Transcript, cancellationToken);
    }

    public Task<OpenAiConnectionResult> TestConnectionAsync(CancellationToken cancellationToken = default)
        => TestConnectionAsync(apiKeyOverride: null, cancellationToken: cancellationToken);

    public async Task<OpenAiConnectionResult> TestConnectionAsync(
        string? apiKeyOverride,
        CancellationToken cancellationToken = default)
    {
        var apiKey = string.IsNullOrWhiteSpace(apiKeyOverride)
            ? _configuration.ApiKey
            : apiKeyOverride.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
            return new(false, "Enter an OpenAI API key first.");
        if (apiKey.Any(char.IsControl))
            return new(false, "The OpenAI API key format is invalid.");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_connectionTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.IsSuccessStatusCode)
                return new(true, "OpenAI connection is ready.");

            return new(false, await ErrorMessageAsync(response, "OpenAI rejected the connection.", timeout.Token));
        }
        catch (HttpRequestException)
        {
            return new(false, "OpenAI is not reachable. Check the network and try again.");
        }
        catch (FormatException)
        {
            return new(false, "The OpenAI API key format is invalid.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, "OpenAI took too long to respond.");
        }
    }

    private async Task<IReadOnlyList<TranscriptSegment>> TranscribeAsync(
        AudioTrack track,
        TimeSpan fallbackDuration,
        CancellationToken cancellationToken)
    {
        var model = _configuration.TranscriptionModel;
        var diarized = model.Contains("diarize", StringComparison.OrdinalIgnoreCase);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/transcriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _configuration.ApiKey);

        using var form = new MultipartFormDataContent();
        var wavBytes = await File.ReadAllBytesAsync(track.Path, cancellationToken);
        using var fileContent = new ByteArrayContent(wavBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        AddMultipartFile(form, fileContent, "file", UploadFileName(track.Path));
        form.Add(new StringContent(model), "model");
        if (!string.IsNullOrWhiteSpace(_configuration.TranscriptionLanguage)
            && !string.Equals(_configuration.TranscriptionLanguage, "auto", StringComparison.OrdinalIgnoreCase))
        {
            form.Add(new StringContent(_configuration.TranscriptionLanguage), "language");
        }
        form.Add(new StringContent(diarized ? "diarized_json" : "json"), "response_format");
        if (diarized) form.Add(new StringContent("auto"), "chunking_strategy");
        request.Content = form;

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new OpenAiServiceException("OpenAI is not reachable. Check the network and try again.", exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new OpenAiServiceException(await ErrorMessageAsync(response, "OpenAI transcription failed.", cancellationToken));

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            try
            {
                return ParseTranscript(body, track, fallbackDuration);
            }
            catch (JsonException)
            {
                throw new OpenAiServiceException("OpenAI returned an unexpected transcription response.");
            }
        }

    }

    private static IReadOnlyList<TranscriptSegment> ParseTranscript(
        string body,
        AudioTrack track,
        TimeSpan fallbackDuration)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var segments = new List<TranscriptSegment>();
        if (root.TryGetProperty("segments", out var segmentArray) && segmentArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var segment in segmentArray.EnumerateArray())
            {
                var text = segment.TryGetProperty("text", out var textElement) ? textElement.GetString()?.Trim() : null;
                if (string.IsNullOrWhiteSpace(text)) continue;
                var start = ReadDouble(segment, "start");
                var end = ReadDouble(segment, "end");
                var serverSpeaker = segment.TryGetProperty("speaker", out var speakerElement) ? speakerElement.GetString() : null;
                var speakerName = string.IsNullOrWhiteSpace(serverSpeaker) ? track.DisplayName : $"{track.DisplayName} · {serverSpeaker}";
                segments.Add(CreateSegment(speakerName, start + track.Offset.TotalSeconds, end + track.Offset.TotalSeconds, text));
            }
        }

        if (segments.Count > 0) return segments;

        var transcript = root.TryGetProperty("text", out var transcriptElement) ? transcriptElement.GetString()?.Trim() : null;
        var trackDuration = track.Duration > TimeSpan.Zero ? track.Duration : fallbackDuration;
        return string.IsNullOrWhiteSpace(transcript)
            ? []
            : [CreateSegment(
                track.DisplayName,
                track.Offset.TotalSeconds,
                track.Offset.TotalSeconds + Math.Max(trackDuration.TotalSeconds, 1),
                transcript)];
    }

    private async Task<MeetingSummary> SummarizeAsync(
        RecordingData recording,
        IReadOnlyCollection<TranscriptSegment> transcript,
        CancellationToken cancellationToken)
    {
        var transcriptText = string.Join(
            Environment.NewLine,
            transcript.Select(segment => $"[{segment.Timestamp}] {segment.SpeakerName}: {segment.Text}"));
        var payload = new
        {
            model = _configuration.SummaryModel,
            store = false,
            instructions = "Bạn là trợ lý ghi chép cuộc họp. Trả về đúng một json object, không markdown, với các field: overview (string), keyPoints (array of strings), decisions (array of strings), actionItems (array of objects gồm text, owner, due, isComplete), deadlines (array of objects gồm label, date, owner), questions (array of strings), importantMoments (array of strings). Viết bằng ngôn ngữ chính của transcript; nêu rõ khi thông tin chưa xác định.",
            input = $"Respond in json.\n\nTên cuộc họp: {recording.Title}\n\nTranscript:\n{transcriptText}",
            text = new { format = new { type = "json_object" } }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _configuration.ApiKey);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new OpenAiServiceException(await ErrorMessageAsync(response, "OpenAI summary failed.", cancellationToken));

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(body);
            var outputText = ExtractOutputText(document.RootElement);
            var json = ExtractJsonObject(outputText);
            var summary = JsonSerializer.Deserialize<MeetingSummary>(json, JsonOptions);
            if (summary is null) throw new JsonException();
            summary.KeyPoints ??= [];
            summary.Decisions ??= [];
            summary.ActionItems ??= [];
            summary.Deadlines ??= [];
            summary.Questions ??= [];
            summary.ImportantMoments ??= [];
            return summary;
        }
        catch (JsonException)
        {
            throw new OpenAiServiceException("OpenAI returned an unexpected summary response.");
        }
    }

    private static IEnumerable<AudioTrack> AudioTracks(RecordingData recording)
    {
        if (IsUsableAudioFile(recording.MicrophonePath))
            yield return new AudioTrack(recording.MicrophonePath!, "You");
        if (IsUsableAudioFile(recording.SystemAudioPath))
            yield return new AudioTrack(recording.SystemAudioPath!, "Meeting participant");
    }

    private static bool HasCapturedAudioPath(RecordingData recording)
        => !string.IsNullOrWhiteSpace(recording.MicrophonePath)
            || !string.IsNullOrWhiteSpace(recording.SystemAudioPath);

    private static bool IsUsableAudioFile(string? path)
        => !string.IsNullOrWhiteSpace(path) && File.Exists(path) && new FileInfo(path).Length > 44;

    private static PreparedAudioTracks PrepareAudioTracks(
        IReadOnlyCollection<AudioTrack> tracks,
        CancellationToken cancellationToken)
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"MeetingAssistant-Audio-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var prepared = new List<AudioTrack>(tracks.Count);
            OpenAiServiceException? firstFailure = null;
            foreach (var track in tracks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var outputPath = Path.Combine(
                    temporaryDirectory,
                    track.DisplayName.Equals("You", StringComparison.OrdinalIgnoreCase)
                        ? "microphone.wav"
                        : "system-audio.wav");
                try
                {
                    NormalizeTrack(track, outputPath, cancellationToken);
                    var uploadTracks = CreateUploadTracks(track, outputPath, temporaryDirectory, cancellationToken);
                    prepared.AddRange(uploadTracks);
                    if (uploadTracks.Count > 1) DeleteFile(outputPath);
                }
                catch (EmptyAudioTrackException)
                {
                    // WASAPI loopback can produce a valid, tiny WAV header when
                    // no system sound was playing. It must not be uploaded.
                    DeleteFile(outputPath);
                }
                catch (OpenAiServiceException exception)
                {
                    // Keep a healthy microphone track usable when the optional
                    // system track is malformed or unavailable. If every track
                    // fails, the first detailed error is returned below.
                    firstFailure ??= exception;
                    DeleteFile(outputPath);
                }
                catch (Exception exception) when (exception is not OperationCanceledException && exception is not OutOfMemoryException)
                {
                    firstFailure ??= new OpenAiServiceException(
                        $"The {TrackLabel(track)} audio could not be prepared for transcription. Your recording is still available; record a new meeting and retry.",
                        exception);
                    DeleteFile(outputPath);
                }
            }

            if (prepared.Count == 0)
            {
                if (firstFailure is not null) throw firstFailure;
                throw new OpenAiServiceException(
                    "No usable recording audio was found. Your recording is still available; record a new meeting and retry.");
            }

            return new PreparedAudioTracks(temporaryDirectory, prepared);
        }
        catch
        {
            DeleteDirectory(temporaryDirectory);
            throw;
        }
    }

    private static void NormalizeTrack(
        AudioTrack track,
        string outputPath,
        CancellationToken cancellationToken)
    {
        try
        {
            using var source = OpenSourceAudio(track.Path);
            ISampleProvider sampleProvider = source.ToSampleProvider();
            if (sampleProvider.WaveFormat.Channels == 2)
            {
                var stereo = new StereoToMonoSampleProvider(sampleProvider)
                {
                    LeftVolume = 0.5f,
                    RightVolume = 0.5f
                };
                sampleProvider = stereo;
            }
            else if (sampleProvider.WaveFormat.Channels > 2)
            {
                var mono = new MultiplexingSampleProvider(new[] { sampleProvider }, 1);
                mono.ConnectInputToOutput(0, 0);
                sampleProvider = mono;
            }

            if (sampleProvider.WaveFormat.SampleRate != ProviderSampleRate)
                sampleProvider = new WdlResamplingSampleProvider(sampleProvider, ProviderSampleRate);

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

            if (pcmData.Length == 0)
                throw new EmptyAudioTrackException();

            WriteCanonicalPcm16MonoWav(outputPath, pcmData.ToArray());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (EmptyAudioTrackException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new OpenAiServiceException(
                $"The {TrackLabel(track)} audio is incomplete or unsupported. Your recording is still available; record a new meeting and retry.",
                exception);
        }

        if (!IsUsableAudioFile(outputPath))
            throw new OpenAiServiceException(
                $"The {TrackLabel(track)} audio could not be prepared for transcription. Your recording is still available; record a new meeting and retry.");
    }

    private static IReadOnlyList<AudioTrack> CreateUploadTracks(
        AudioTrack source,
        string normalizedPath,
        string temporaryDirectory,
        CancellationToken cancellationToken)
    {
        if (new FileInfo(normalizedPath).Length <= MaxTranscriptionFileBytes)
        {
            using var reader = new WaveFileReader(normalizedPath);
            return
            [
                new AudioTrack(
                    normalizedPath,
                    source.DisplayName,
                    TimeSpan.Zero,
                    DurationFromBytes(reader.Length, reader.WaveFormat.AverageBytesPerSecond))
            ];
        }

        using var sourceReader = new WaveFileReader(normalizedPath);
        var bytesPerSecond = sourceReader.WaveFormat.AverageBytesPerSecond;
        var blockAlign = Math.Max(sourceReader.WaveFormat.BlockAlign, 1);
        var maxChunkDataBytes = MaxTranscriptionFileBytes - ChunkHeaderSafetyBytes;
        maxChunkDataBytes -= maxChunkDataBytes % blockAlign;
        if (bytesPerSecond <= 0 || maxChunkDataBytes < blockAlign)
            throw new InvalidDataException("The normalized audio format cannot be split safely.");

        var buffer = new byte[Math.Min(64 * 1024, (int)maxChunkDataBytes)];
        var chunks = new List<AudioTrack>();
        long totalBytesRead = 0;
        var chunkIndex = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunkPath = Path.Combine(
                temporaryDirectory,
                $"{TrackFileStem(source)}-{chunkIndex + 1:000}.wav");
            long chunkBytesRead = 0;
            using (var chunkData = new MemoryStream())
            {
                while (chunkBytesRead < maxChunkDataBytes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var requestedBytes = (int)Math.Min(buffer.Length, maxChunkDataBytes - chunkBytesRead);
                    var bytesRead = sourceReader.Read(buffer, 0, requestedBytes);
                    if (bytesRead <= 0) break;
                    chunkData.Write(buffer, 0, bytesRead);
                    chunkBytesRead += bytesRead;
                }

                if (chunkBytesRead == 0)
                {
                    DeleteFile(chunkPath);
                    break;
                }

                WriteCanonicalPcm16MonoWav(chunkPath, chunkData.ToArray());
            }

            chunks.Add(new AudioTrack(
                chunkPath,
                source.DisplayName,
                DurationFromBytes(totalBytesRead, bytesPerSecond),
                DurationFromBytes(chunkBytesRead, bytesPerSecond)));
            totalBytesRead += chunkBytesRead;
            chunkIndex++;
            if (chunkBytesRead < maxChunkDataBytes) break;
        }

        return chunks.Count == 0
            ? throw new EmptyAudioTrackException()
            : chunks;
    }

    private static TimeSpan DurationFromBytes(long bytes, int bytesPerSecond)
        => bytesPerSecond <= 0
            ? TimeSpan.Zero
            : TimeSpan.FromSeconds(bytes / (double)bytesPerSecond);

    private static string TrackLabel(AudioTrack track)
        => track.DisplayName.Equals("You", StringComparison.OrdinalIgnoreCase)
            ? "microphone"
            : "system audio";

    private static string TrackFileStem(AudioTrack track)
        => track.DisplayName.Equals("You", StringComparison.OrdinalIgnoreCase)
            ? "microphone"
            : "system-audio";

    private static void DeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void DeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static TranscriptSegment CreateSegment(string speakerName, double start, double end, string text)
        => new()
        {
            SpeakerId = StableSpeakerId(speakerName),
            SpeakerName = speakerName,
            Start = TimeSpan.FromSeconds(Math.Max(0, start)),
            End = TimeSpan.FromSeconds(Math.Max(Math.Max(0, start), end)),
            Text = text
        };

    private static string StableSpeakerId(string speakerName)
        => "openai-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(speakerName))).ToLowerInvariant()[..12];

    private static double ReadDouble(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.TryGetDouble(out var number) ? number : 0;

    private static string ExtractOutputText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
            return outputText.GetString() ?? string.Empty;

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var parts = new List<string>();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;
            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                    parts.Add(text.GetString() ?? string.Empty);
            }
        }

        return string.Join(Environment.NewLine, parts);
    }

    private static string ExtractJsonObject(string value)
    {
        var start = value.IndexOf('{');
        var end = value.LastIndexOf('}');
        if (start < 0 || end <= start) throw new JsonException();
        return value[start..(end + 1)];
    }

    private static WaveStream OpenSourceAudio(string path)
    {
        try
        {
            return MeetingAudioPreparation.OpenWaveFile(path);
        }
        catch (Exception)
        {
            return new AudioFileReader(path);
        }
    }

    private static void WriteCanonicalPcm16MonoWav(string path, byte[] pcmData)
    {
        var alignedLength = pcmData.Length - (pcmData.Length % 2);
        if (alignedLength <= 0) throw new EmptyAudioTrackException();

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        writer.Write("RIFF"u8);
        writer.Write(36 + alignedLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write(ProviderSampleRate);
        writer.Write(ProviderSampleRate * 2);
        writer.Write((ushort)2);
        writer.Write((ushort)16);
        writer.Write("data"u8);
        writer.Write(alignedLength);
        writer.Write(pcmData, 0, alignedLength);
        writer.Flush();
    }

    private static void AddMultipartFile(MultipartFormDataContent form, HttpContent content, string name, string fileName)
    {
        form.Add(content, name, fileName);
        var disposition = content.Headers.ContentDisposition;
        if (disposition is null) return;

        // .NET also sets filename* (RFC 5987). OpenAI's parser often ignores that
        // and then treats the part as having no extension, returning
        // "Audio file might be corrupted or unsupported".
        disposition.Name = name;
        disposition.FileName = fileName;
        disposition.FileNameStar = null;
    }

    private static string UploadFileName(string path)
    {
        var name = Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(name) || !name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
            ? "audio.wav"
            : name;
    }

    private static async Task<string> ErrorMessageAsync(
        HttpResponseMessage response,
        string fallback,
        CancellationToken cancellationToken = default)
    {
        string? apiMessage = null;
        string? apiCode = null;
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                apiMessage = error.TryGetProperty("message", out var messageElement) ? messageElement.GetString() : null;
                apiCode = error.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
                apiCode ??= error.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
            }
        }
        catch (JsonException)
        {
            // Keep a stable, user-facing message when a proxy returns non-JSON.
        }

        return MapProviderError(response.StatusCode, apiCode, apiMessage, fallback);
    }

    private static string MapProviderError(
        System.Net.HttpStatusCode statusCode,
        string? apiCode,
        string? apiMessage,
        string fallback)
    {
        var status = (int)statusCode;
        var code = apiCode ?? string.Empty;
        var message = apiMessage ?? string.Empty;

        if (status == 401 || ContainsAny(code, "invalid_api_key") || ContainsAny(message, "invalid api key", "incorrect api key"))
            return "OpenAI rejected the API key. Check the key in Settings and retry.";
        if (status == 429 || ContainsAny(code, "rate_limit") || ContainsAny(message, "rate limit"))
            return "OpenAI is rate limiting requests. Wait a moment and retry.";
        if (ContainsAny(code, "insufficient_quota") || ContainsAny(message, "quota", "billing"))
            return "OpenAI quota has been exceeded. Check your OpenAI account and retry.";
        if (status == 413 || ContainsAny(message, "maximum content size", "file is too large", "25mb"))
            return "The recording is too large for OpenAI. Record a shorter meeting and retry.";
        if (ContainsAny(message, "corrupted", "unsupported", "invalid file format", "could not be decoded"))
            return "OpenAI could not read the uploaded audio. Your recording is still available; retry processing or record again.";
        if (!string.IsNullOrWhiteSpace(message))
            return $"{fallback} {message.Trim()}";

        return $"{fallback} ({status})";
    }

    private static bool ContainsAny(string value, params string[] tokens)
        => tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private sealed record AudioTrack(
        string Path,
        string DisplayName,
        TimeSpan Offset = default,
        TimeSpan Duration = default);

    private sealed class PreparedAudioTracks : IDisposable
    {
        public PreparedAudioTracks(string directory, IReadOnlyList<AudioTrack> tracks)
        {
            Directory = directory;
            Tracks = tracks;
        }

        private string Directory { get; }
        public IReadOnlyList<AudioTrack> Tracks { get; }

        public void Dispose() => DeleteDirectory(Directory);
    }

    private sealed class EmptyAudioTrackException : Exception
    {
    }
}

public sealed class OpenAiServiceException : Exception
{
    public OpenAiServiceException(string message) : base(message) { }
    public OpenAiServiceException(string message, Exception innerException) : base(message, innerException) { }
}
