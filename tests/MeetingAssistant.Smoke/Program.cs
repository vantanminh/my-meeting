using System.Buffers.Binary;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using MeetingAssistant.Models;
using MeetingAssistant.Services;
using MeetingAssistant.ViewModels;
using NAudio.Wave;

var failures = new List<string>();
var smokeRoot = Path.Combine(Path.GetTempPath(), $"meeting-assistant-smoke-{Guid.NewGuid():N}");
Directory.CreateDirectory(smokeRoot);
AppPaths.UseRoot(smokeRoot);
AppPaths.SetCurrentUser("smoke-user");
var services = new AppServices();

try
{
    var viewModel = new MainViewModel(services);
    if (viewModel.CurrentView != WorkspaceView.Auth) failures.Add("new sessions should begin on auth");
    if (viewModel.MicrophoneDevices.Count < 1 || viewModel.SystemAudioDevices.Count < 1)
        failures.Add("audio setup should expose both source lists");
    var greeting = GreetingCopy.TimeOfDay(DateTimeOffset.Now);
    if (greeting is not ("Good morning, " or "Good afternoon, " or "Good evening, "))
        failures.Add("greeting should follow the time of day");
    viewModel.Dispose();

    var storedMeetings = await services.MeetingRepository.LoadAsync();
    if (storedMeetings.Count != 0) failures.Add("a new workspace should start empty without seed meetings");
    var sampleMeetings = JsonMeetingRepository.CreateSampleMeetings();
    await services.MeetingRepository.SaveAsync(sampleMeetings);
    var reloadedMeetings = await services.MeetingRepository.LoadAsync();
    if (reloadedMeetings.Count != sampleMeetings.Count) failures.Add("meeting cache should round-trip through JSON");

    var corruptPath = AppPaths.MeetingsPath;
    await File.WriteAllTextAsync(corruptPath, "{not-json");
    var repository = new JsonMeetingRepository();
    var recovered = await repository.LoadAsync();
    if (recovered.Count != 0 || !Directory.GetFiles(AppPaths.DataDirectory, "meetings.json.bak-*").Any())
        failures.Add("a corrupt meetings cache should be quarantined and open empty");
    await services.MeetingRepository.SaveAsync(sampleMeetings);

    var isolatedA = Path.Combine(smokeRoot, "user-a");
    AppPaths.UseRoot(isolatedA);
    AppPaths.SetCurrentUser("alice");
    await services.MeetingRepository.SaveAsync(sampleMeetings);
    AppPaths.SetCurrentUser("bob");
    var bobMeetings = await services.MeetingRepository.LoadAsync();
    if (bobMeetings.Count != 0) failures.Add("switching users should isolate meeting files");
    AppPaths.UseRoot(smokeRoot);
    AppPaths.SetCurrentUser("smoke-user");

    var overlap = TranscriptRepair.CollapseOverlaps(
    [
        new TranscriptSegment { SpeakerId = "you", SpeakerName = "You", Start = TimeSpan.FromSeconds(1), Text = "Hello there team" },
        new TranscriptSegment { SpeakerId = "guest", SpeakerName = "Meeting participant", Start = TimeSpan.FromSeconds(1.2), Text = "Hello there team" }
    ]);
    if (overlap.Count != 1 || overlap[0].SpeakerName != "You")
        failures.Add("overlapping duplicate turns should collapse to the host track");

    var export = MeetingExport.Build(sampleMeetings[0]);
    if (!export.Markdown.Contains(sampleMeetings[0].Title) || !export.Json.Contains("Transcript"))
        failures.Add("meeting export should include title and transcript JSON");

    var retentionDir = Path.Combine(smokeRoot, "old-recordings", "stale-session");
    Directory.CreateDirectory(retentionDir);
    File.WriteAllText(Path.Combine(retentionDir, "microphone.wav"), "x");
    Directory.SetCreationTimeUtc(retentionDir, DateTime.UtcNow.AddDays(-40));
    var removed = RetentionPolicy.Sweep(Path.Combine(smokeRoot, "old-recordings"), TimeSpan.FromDays(30), DateTimeOffset.UtcNow);
    if (removed < 1) failures.Add("retention sweep should delete expired recording folders");

    if (DiskBudget.WarningFor(10 * 1024 * 1024, null) is null)
        failures.Add("disk budget should warn when free space is critically low");

    var silent = new byte[64];
    if (AudioLevelMeter.FromBuffer(silent, silent.Length, 32) > 0.05)
        failures.Add("a silent float buffer should sit near the bottom of the meter");
    var loud = new byte[64];
    for (var index = 0; index < loud.Length; index += 4)
        BitConverter.TryWriteBytes(loud.AsSpan(index), 0.5f);
    var loudLevel = AudioLevelMeter.FromBuffer(loud, loud.Length, 32);
    if (loudLevel < 0.7)
        failures.Add("a loud float buffer should move the meter");
    if (AudioLevelMeter.Smooth(0.1, 0.9) <= 0.1)
        failures.Add("meter smoothing should move toward the new level");
    var remaining = DiskBudget.RemainingRecordingTime(2L * 1024 * 1024 * 1024);
    if (remaining < TimeSpan.FromMinutes(30))
        failures.Add("two gigabytes free should leave a usable recording budget");

    var bottomTaskbar = MaximizedWindowPlacement.ForWorkArea(
        new ScreenRect(0, 0, 1920, 1080),
        new ScreenRect(0, 0, 1920, 1040));
    if (bottomTaskbar != new MaximizedPlacement(0, 0, 1920, 1040))
        failures.Add("maximized window should stop above a bottom taskbar");
    var topTaskbar = MaximizedWindowPlacement.ForWorkArea(
        new ScreenRect(1920, 0, 3840, 1080),
        new ScreenRect(1920, 48, 3840, 1080));
    if (topTaskbar != new MaximizedPlacement(0, 48, 1920, 1032))
        failures.Add("maximized window should start below a top taskbar on a secondary monitor");
    var leftTaskbar = MaximizedWindowPlacement.ForWorkArea(
        new ScreenRect(-1920, 0, 0, 1080),
        new ScreenRect(-1872, 0, 0, 1080));
    if (leftTaskbar != new MaximizedPlacement(48, 0, 1872, 1080))
        failures.Add("maximized window should start to the right of a left taskbar");
    var covered = MaximizedWindowPlacement.OverflowOutside(
        new ScreenRect(-7, -7, 1927, 1087),
        new ScreenRect(0, 0, 1920, 1040));
    if (covered != new WorkAreaOverflow(7, 7, 7, 47))
        failures.Add("content inset should clear the taskbar and the off-screen maximize border");
    var fitted = MaximizedWindowPlacement.OverflowOutside(
        new ScreenRect(0, 0, 1920, 1040),
        new ScreenRect(0, 0, 1920, 1040));
    if (!fitted.IsEmpty)
        failures.Add("a window already inside the work area should not add extra inset");

    if (AuthValidation.ValidateSignIn("bad", "123") is null || AuthValidation.ValidateSignUp("", "a@b.com", "password") is null)
        failures.Add("auth validation should reject incomplete credentials before a network call");

    var editable = new List<TranscriptSegment>
    {
        new() { SpeakerId = "you", SpeakerName = "You", Start = TimeSpan.FromSeconds(1), End = TimeSpan.FromSeconds(4), Text = "Hello there team" },
        new() { SpeakerId = "guest", SpeakerName = "Guest", Start = TimeSpan.FromSeconds(5), End = TimeSpan.FromSeconds(8), Text = "Next idea" }
    };
    TranscriptEditing.Split(editable, editable[0], 6);
    if (editable.Count != 3) failures.Add("splitting a turn should insert a second segment");
    TranscriptEditing.MergeWithNext(editable, editable[0]);
    if (editable.Count != 2) failures.Add("merging adjacent turns should collapse them");
    var copied = TranscriptEditing.CopyMarkdown(editable);
    if (!copied.Contains("You:")) failures.Add("copied transcript should include speaker names");

    var monday = new DateTime(2026, 8, 24);
    var weekMeetings = new[]
    {
        new Meeting { StartedAt = new DateTimeOffset(monday.AddDays(1), TimeSpan.Zero), Summary = new MeetingSummary { ActionItems = [new() { Text = "Open", IsComplete = false }] }, SyncState = SyncState.Pending },
        new Meeting { StartedAt = new DateTimeOffset(monday.AddDays(-8), TimeSpan.Zero), Status = MeetingStatus.Failed }
    };
    if (HubMeetingFilter.Apply(weekMeetings, "This week", monday.AddDays(3)).Count() != 1)
        failures.Add("hub filter should keep only this week's meetings");
    if (HubMeetingFilter.Apply(weekMeetings, "Open actions", monday.AddDays(3)).Count() != 1)
        failures.Add("hub filter should keep meetings with open actions");
    if (HubMeetingFilter.Apply(weekMeetings, "Failed", monday.AddDays(3)).Count() != 1)
        failures.Add("hub filter should keep failed meetings");

    var protectedPath = Path.Combine(smokeRoot, "protected-meetings.json");
    await ProtectedWorkspaceStore.WriteAsync(protectedPath, "[{\"title\":\"Protected\"}]");
    var protectedJson = await ProtectedWorkspaceStore.ReadAsync(protectedPath);
    if (!protectedJson.Contains("Protected")) failures.Add("workspace store should round-trip meeting JSON");

    var progressValues = new List<ProcessingProgress>();
    Console.WriteLine("processing...");
    var recording = new RecordingData
    {
        Title = "Smoke test meeting",
        StartedAt = DateTimeOffset.Now,
        Duration = TimeSpan.FromSeconds(2),
        Configuration = new AudioConfiguration()
    };
    try
    {
        await services.IntelligenceService.ProcessAsync(recording, new Progress<ProcessingProgress>(_ => { }));
        failures.Add("OpenAI processing without a key should refuse instead of inventing a transcript");
    }
    catch (OpenAiServiceException exception) when (exception.Message.Contains("OpenAI API key", StringComparison.OrdinalIgnoreCase))
    {
        // Expected honest gate.
    }

    var processed = await services.DemoIntelligence.ProcessAsync(
        recording,
        new Progress<ProcessingProgress>(value => progressValues.Add(value)));
    Console.WriteLine("processing complete");

    if (processed.Meeting.Status != MeetingStatus.Ready) failures.Add("processed meeting should be ready");
    if (processed.Meeting.Transcript.Count < 2) failures.Add("processed meeting should contain speaker turns");
    if (processed.Meeting.Speakers.Count < 2) failures.Add("processed meeting should contain speakers");
    if (processed.Meeting.Summary.ActionItems.Count < 1) failures.Add("processed meeting should contain an action item");
    if (processed.Meeting.Summary.ImportantMoments.Count < 1) failures.Add("processed meeting should contain an important moment");
    if (progressValues.Count == 0 || progressValues[^1].Percent != 100) failures.Add("processing should report completion");
    var syncResult = await services.CloudSyncService.SyncAsync(processed.Meeting);
    if (!syncResult.Success) failures.Add("local/cloud sync should preserve a meeting when Firebase is not configured");

    var firebaseTestDirectory = Path.Combine(Path.GetTempPath(), $"meeting-assistant-firebase-{Guid.NewGuid():N}");
    Directory.CreateDirectory(firebaseTestDirectory);
    try
    {
        await File.WriteAllTextAsync(
            Path.Combine(firebaseTestDirectory, FirebaseConfiguration.ConfigFileName),
            "{\"apiKey\":\"test-firebase-key\",\"projectId\":\"test-project\"}");
        var remoteMeeting = new Meeting
        {
            Id = "cloud-meeting",
            Title = "Remote Firebase meeting",
            StartedAt = DateTimeOffset.Now.AddHours(-1),
            UpdatedAt = DateTimeOffset.Now,
            Duration = TimeSpan.FromMinutes(18),
            ParticipantCount = 2,
            AudioSources = "Test microphone + test system audio",
            Speakers = [new SpeakerProfile { Id = "cloud-speaker", Name = "Cloud speaker", Meetings = 1 }],
            Transcript = [new TranscriptSegment { SpeakerId = "cloud-speaker", SpeakerName = "Cloud speaker", Start = TimeSpan.FromSeconds(1), End = TimeSpan.FromSeconds(4), Text = "Loaded from Firestore." }],
            Summary = new MeetingSummary { Overview = "Remote summary", KeyPoints = ["Cloud data is readable"] }
        };
        var firebaseHandler = new FakeFirebaseHandler("test-project", "firebase-user", remoteMeeting);
        using var firebaseHttpClient = new HttpClient(firebaseHandler);
        var firebaseAuth = new FakeAuthService(new UserSession
        {
            UserId = "firebase-user",
            Email = "test@example.com",
            DisplayName = "Test User",
            AccessToken = "test-access-token"
        });
        var firebaseCloud = new FirebaseCloudSyncService(
            new FirebaseConfiguration(firebaseTestDirectory),
            firebaseAuth,
            firebaseHttpClient);

        var cloudMeetings = await firebaseCloud.LoadAsync();
        if (cloudMeetings.Count != 1 || cloudMeetings[0].Title != remoteMeeting.Title)
            failures.Add("Firebase adapter should load and parse Firestore meetings");
        if (cloudMeetings.Count == 1 && cloudMeetings[0].Transcript.Count != 1)
            failures.Add("Firebase adapter should restore transcript JSON");

        var firebaseSyncResult = await firebaseCloud.SyncAsync(remoteMeeting);
        if (!firebaseSyncResult.Success || !firebaseHandler.SawPatch)
            failures.Add("Firebase adapter should PATCH meetings to Firestore");
    }
    finally
    {
        if (Directory.Exists(firebaseTestDirectory)) Directory.Delete(firebaseTestDirectory, recursive: true);
    }

    var fakeAudioDirectory = Path.Combine(Path.GetTempPath(), $"meeting-assistant-openai-{Guid.NewGuid():N}");
    Directory.CreateDirectory(fakeAudioDirectory);
    var fakeMicrophonePath = Path.Combine(fakeAudioDirectory, "microphone.wav");
    var fakeSystemAudioPath = Path.Combine(fakeAudioDirectory, "system-audio.wav");
    WriteFloatWaveFile(fakeMicrophonePath);
    WriteFloatWaveFile(fakeSystemAudioPath);
    var fakeHttpHandler = new FakeOpenAiHandler();
    using (var fakeHttpClient = new HttpClient(fakeHttpHandler))
    {
        var fakeOpenAi = new OpenAiMeetingIntelligenceService(
            new OpenAiConfiguration("test-key", "gpt-4o-transcribe", "gpt-4.1-mini"),
            httpClient: fakeHttpClient);
        var openAiRecording = new RecordingData
        {
            Title = "OpenAI smoke test",
            StartedAt = DateTimeOffset.Now,
            Duration = TimeSpan.FromSeconds(10),
            Configuration = new AudioConfiguration(),
            MicrophonePath = fakeMicrophonePath,
            SystemAudioPath = fakeSystemAudioPath
        };
        var openAiProcessed = await fakeOpenAi.ProcessAsync(openAiRecording, new Progress<ProcessingProgress>(_ => { }));
        if (openAiProcessed.Meeting.Transcript.Count != 2) failures.Add("OpenAI adapter should merge both audio tracks");
        if (openAiProcessed.Meeting.Summary.ActionItems.Count != 1) failures.Add("OpenAI adapter should parse a structured summary");
        if (openAiProcessed.Meeting.Status != MeetingStatus.Ready) failures.Add("OpenAI adapter should return a ready meeting");
        if (fakeHttpHandler.AudioPayloads.Count != 2 || fakeHttpHandler.AudioPayloads.Any(payload => !IsPcm16Wave(payload)))
            failures.Add("OpenAI adapter should normalize float WAV tracks to valid PCM16 WAV uploads");
        if (fakeHttpHandler.TranscriptionBodies.Count == 0
            || fakeHttpHandler.TranscriptionBodies.Any(body => body.Contains("filename*", StringComparison.OrdinalIgnoreCase)))
            failures.Add("OpenAI uploads should send a quoted filename without RFC 5987 filename*");
        if (fakeHttpHandler.TranscriptionBodies.Any(body =>
                !body.Contains("filename=", StringComparison.OrdinalIgnoreCase)
                || (!body.Contains(".wav", StringComparison.OrdinalIgnoreCase))))
            failures.Add("OpenAI uploads should include a .wav filename on the file part");

        var tinySystemPath = Path.Combine(fakeAudioDirectory, "tiny-system-audio.wav");
        using (var tinyWriter = new WaveFileWriter(tinySystemPath, new WaveFormat(16_000, 16, 1)))
            tinyWriter.Write(new byte[] { 0 }, 0, 1);
        fakeHttpHandler.AudioPayloads.Clear();
        var partialTrackResult = await fakeOpenAi.ProcessAsync(
            new RecordingData
            {
                Title = "Partial loopback smoke test",
                StartedAt = DateTimeOffset.Now,
                Duration = TimeSpan.FromSeconds(1),
                Configuration = new AudioConfiguration(),
                MicrophonePath = fakeMicrophonePath,
                SystemAudioPath = tinySystemPath
            },
            new Progress<ProcessingProgress>(_ => { }));
        if (fakeHttpHandler.AudioPayloads.Count != 1 || partialTrackResult.Meeting.Transcript.Count != 1)
            failures.Add("OpenAI adapter should skip a partial empty loopback track when microphone audio is usable");

        var malformedPath = Path.Combine(fakeAudioDirectory, "malformed.wav");
        await File.WriteAllBytesAsync(malformedPath, new byte[128]);
        try
        {
            await fakeOpenAi.ProcessAsync(
                new RecordingData
                {
                    Title = "Malformed audio smoke test",
                    StartedAt = DateTimeOffset.Now,
                    Duration = TimeSpan.FromSeconds(1),
                    Configuration = new AudioConfiguration(),
                    MicrophonePath = malformedPath
                },
                new Progress<ProcessingProgress>(_ => { }));
            failures.Add("OpenAI adapter should reject malformed audio with a recoverable error");
        }
        catch (OpenAiServiceException exception) when (exception.Message.Contains("incomplete or unsupported", StringComparison.OrdinalIgnoreCase))
        {
            // Expected: the original recording remains available for retry.
        }

        try
        {
            var unauthorizedHandler = new FakeOpenAiHandler
            {
                TranscriptionStatus = HttpStatusCode.Unauthorized,
                TranscriptionErrorJson = "{\"error\":{\"message\":\"Incorrect API key provided\",\"code\":\"invalid_api_key\"}}"
            };
            using var unauthorizedClient = new HttpClient(unauthorizedHandler);
            var unauthorizedOpenAi = new OpenAiMeetingIntelligenceService(
                new OpenAiConfiguration("test-key", "gpt-4o-transcribe", "gpt-4.1-mini"),
                httpClient: unauthorizedClient);
            await unauthorizedOpenAi.ProcessAsync(
                new RecordingData
                {
                    Title = "Unauthorized OpenAI smoke test",
                    StartedAt = DateTimeOffset.Now,
                    Duration = TimeSpan.FromSeconds(1),
                    Configuration = new AudioConfiguration(),
                    MicrophonePath = fakeMicrophonePath
                },
                new Progress<ProcessingProgress>(_ => { }));
            failures.Add("OpenAI adapter should surface invalid API key errors");
        }
        catch (OpenAiServiceException exception)
        {
            if (!exception.Message.Contains("API key", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("corrupted", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add("OpenAI adapter should not map API key failures to a corrupted-audio message");
            }
        }

        try
        {
            var audioErrorHandler = new FakeOpenAiHandler
            {
                TranscriptionStatus = HttpStatusCode.BadRequest,
                TranscriptionErrorJson = "{\"error\":{\"message\":\"Audio file might be corrupted or unsupported\"}}"
            };
            using var audioErrorClient = new HttpClient(audioErrorHandler);
            var audioErrorOpenAi = new OpenAiMeetingIntelligenceService(
                new OpenAiConfiguration("test-key", "gpt-4o-transcribe", "gpt-4.1-mini"),
                httpClient: audioErrorClient);
            await audioErrorOpenAi.ProcessAsync(
                new RecordingData
                {
                    Title = "Corrupt audio OpenAI smoke test",
                    StartedAt = DateTimeOffset.Now,
                    Duration = TimeSpan.FromSeconds(1),
                    Configuration = new AudioConfiguration(),
                    MicrophonePath = fakeMicrophonePath
                },
                new Progress<ProcessingProgress>(_ => { }));
            failures.Add("OpenAI adapter should surface unreadable audio errors");
        }
        catch (OpenAiServiceException exception)
        {
            if (!exception.Message.Contains("could not read the uploaded audio", StringComparison.OrdinalIgnoreCase)
                || exception.Message.Contains("Audio file might be corrupted or unsupported", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add("OpenAI adapter should replace the generic corrupted-audio provider text with a retryable local message");
            }
        }

        var connection = await fakeOpenAi.TestConnectionAsync();
        if (!connection.Success) failures.Add("OpenAI connection test should accept a reachable API");

        var pendingConnection = await fakeOpenAi.TestConnectionAsync(apiKeyOverride: "pending-key");
        if (!pendingConnection.Success || fakeHttpHandler.LastAuthorization != "pending-key")
            failures.Add("OpenAI connection test should use the key currently entered in Settings");

        var longAudioPath = Path.Combine(fakeAudioDirectory, "long-meeting.wav");
        WriteFloatWaveFile(longAudioPath, durationSeconds: 14 * 60, sampleRate: 8_000, channels: 1);
        var chunkHandler = new FakeOpenAiHandler();
        using var chunkHttpClient = new HttpClient(chunkHandler);
        var chunkedOpenAi = new OpenAiMeetingIntelligenceService(
            new OpenAiConfiguration("test-key", "gpt-4o-transcribe", "gpt-4.1-mini"),
            httpClient: chunkHttpClient);
        var chunkedResult = await chunkedOpenAi.ProcessAsync(
            new RecordingData
            {
                Title = "Long OpenAI smoke test",
                StartedAt = DateTimeOffset.Now,
                Duration = TimeSpan.FromMinutes(14),
                Configuration = new AudioConfiguration(),
                MicrophonePath = longAudioPath
            },
            new Progress<ProcessingProgress>(_ => { }));
        if (chunkHandler.AudioPayloads.Count < 2
            || chunkHandler.AudioPayloads.Any(payload => payload.Length > 24 * 1024 * 1024 || !IsPcm16Wave(payload))
            || chunkedResult.Meeting.Transcript.Count < 2
            || chunkedResult.Meeting.Transcript[1].Start <= TimeSpan.Zero)
        {
            failures.Add("OpenAI adapter should split long PCM16 WAV uploads below the provider limit and restore timestamps");
        }
    }
    Directory.Delete(fakeAudioDirectory, recursive: true);

    var testEnvironment = new InMemoryUserEnvironmentStore();
    var savedOpenAiConfiguration = new OpenAiConfiguration(userEnvironment: testEnvironment);
    await savedOpenAiConfiguration.SaveUserEnvironmentAsync(" saved-key ", " saved-transcription ", " saved-summary ");
    if (testEnvironment.Get(OpenAiConfiguration.ScopedApiKeyEnvironmentVariable) != "saved-key"
        || savedOpenAiConfiguration.ApiKey != "saved-key"
        || savedOpenAiConfiguration.TranscriptionModel != "saved-transcription"
        || savedOpenAiConfiguration.SummaryModel != "saved-summary")
    {
        failures.Add("OpenAI settings should persist trimmed values and reload them from the user environment");
    }

    using (var hangingHttpClient = new HttpClient(new HangingOpenAiHandler()))
    {
        var hangingOpenAi = new OpenAiMeetingIntelligenceService(
            new OpenAiConfiguration("test-key"),
            httpClient: hangingHttpClient,
            connectionTimeout: TimeSpan.FromMilliseconds(50));
        var startedAt = DateTime.UtcNow;
        var connection = await hangingOpenAi.TestConnectionAsync();
        if (connection.Success || !connection.Message.Contains("too long", StringComparison.OrdinalIgnoreCase))
            failures.Add("OpenAI connection test should report a timeout when the network hangs");
        if (DateTime.UtcNow - startedAt > TimeSpan.FromSeconds(2))
            failures.Add("OpenAI connection timeout should return promptly");
    }

    var updateTestDirectory = Path.Combine(Path.GetTempPath(), $"meeting-assistant-update-{Guid.NewGuid():N}");
    Directory.CreateDirectory(updateTestDirectory);
    string? downloadedUpdatePath = null;
    try
    {
        await File.WriteAllTextAsync(
            Path.Combine(updateTestDirectory, UpdateChannelConfiguration.ConfigFileName),
            "{\"enabled\":true,\"owner\":\"test-owner\",\"repository\":\"test-repo\",\"assetName\":\"MeetingAssistant-Setup.exe\"}");

        using var updateHttpClient = new HttpClient(new FakeGitHubUpdateHandler());
        var installerLauncher = new CapturingUpdateInstallerLauncher();
        using var updateService = new UpdateChannelService(
            new UpdateChannelConfiguration(updateTestDirectory),
            httpClient: updateHttpClient,
            currentVersion: new SemanticVersion(1, 0, 0),
            checkTimeout: TimeSpan.FromMilliseconds(250),
            installerLauncher: installerLauncher);
        var updateCheck = await updateService.CheckAsync();
        if (updateCheck.Status != UpdateCheckStatus.UpdateAvailable || updateCheck.Update?.Version.ToString() != "1.0.1")
            failures.Add($"update service should detect a newer GitHub release asset (status: {updateCheck.Status}, message: {updateCheck.Message})");

        if (updateCheck.Update is not null)
        {
            var updateProgressValues = new List<UpdateDownloadProgress>();
            var download = await updateService.DownloadAsync(
                updateCheck.Update,
                progress: new Progress<UpdateDownloadProgress>(progress => updateProgressValues.Add(progress)));
            downloadedUpdatePath = download.InstallerPath;
            if (!download.Success || string.IsNullOrWhiteSpace(download.InstallerPath) || !File.Exists(download.InstallerPath))
                failures.Add("update service should download the configured installer asset");
            if (updateProgressValues.Count < 2
                || updateProgressValues[0].Stage != UpdateProgressStage.Downloading
                || updateProgressValues[^1].Stage != UpdateProgressStage.Downloading
                || updateProgressValues[^1].Percent != 100
                || updateProgressValues[^1].BytesDownloaded <= 0
                || updateProgressValues[^1].TotalBytes != updateProgressValues[^1].BytesDownloaded)
                failures.Add("update service should report byte-level download progress through completion");

            var launchProgressValues = new List<UpdateDownloadProgress>();
            var launch = await updateService.DownloadAndLaunchAsync(
                updateCheck.Update,
                progress: new Progress<UpdateDownloadProgress>(progress => launchProgressValues.Add(progress)));
            downloadedUpdatePath = launch.InstallerPath;
            if (!launch.Success)
                failures.Add("update service should launch the downloaded installer");

            var expectedInstallerArguments = new[]
            {
                "/VERYSILENT",
                "/SUPPRESSMSGBOXES",
                "/NORESTART",
                "/SP-",
                "/CLOSEAPPLICATIONS",
                "/FORCECLOSEAPPLICATIONS",
                "/RESTARTAPPLICATIONS"
            };
            if (!installerLauncher.Arguments.SequenceEqual(expectedInstallerArguments))
                failures.Add("update installer should run silently, suppress prompts, close the app, and restart it");
            if (launchProgressValues.Count == 0
                || launchProgressValues[^1].Stage != UpdateProgressStage.Restarting
                || !launchProgressValues.Any(progress => progress.Stage == UpdateProgressStage.Installing))
                failures.Add("update service should report installing and restarting stages");
        }

        using var hangingUpdateHttpClient = new HttpClient(new HangingGitHubUpdateHandler());
        using var hangingUpdateService = new UpdateChannelService(
            new UpdateChannelConfiguration(updateTestDirectory),
            httpClient: hangingUpdateHttpClient,
            currentVersion: new SemanticVersion(1, 0, 0),
            checkTimeout: TimeSpan.FromMilliseconds(50));
        var updateStartedAt = DateTime.UtcNow;
        var hangingUpdate = await hangingUpdateService.CheckAsync();
        if (hangingUpdate.Status != UpdateCheckStatus.Failed || !hangingUpdate.Message.Contains("too long", StringComparison.OrdinalIgnoreCase))
            failures.Add("update service should report a timeout when GitHub hangs");
        if (DateTime.UtcNow - updateStartedAt > TimeSpan.FromSeconds(2))
            failures.Add("GitHub update timeout should return promptly");
    }
    finally
    {
        if (!string.IsNullOrWhiteSpace(downloadedUpdatePath) && File.Exists(downloadedUpdatePath))
            File.Delete(downloadedUpdatePath);
        if (Directory.Exists(updateTestDirectory)) Directory.Delete(updateTestDirectory, recursive: true);
    }

    using var capture = new WindowsAudioCaptureService();
    Console.WriteLine("capture start...");
    await capture.StartAsync(new AudioConfiguration { Title = "Capture smoke test" });
    Console.WriteLine("capture started: " + capture.CaptureProvider);
    await Task.Delay(120);
    var captureResult = await capture.StopAsync();
    Console.WriteLine("capture stopped");
    if (captureResult.Duration < TimeSpan.Zero) failures.Add("capture duration cannot be negative");
    if (string.IsNullOrWhiteSpace(capture.CaptureProvider)) failures.Add("capture provider should be reported");
    foreach (var path in new[] { captureResult.MicrophonePath, captureResult.SystemAudioPath }.Where(path => !string.IsNullOrWhiteSpace(path)))
    {
        try
        {
            if (new FileInfo(path!).Length <= 44)
            {
                failures.Add("native capture should close WAV files before processing");
                continue;
            }

            using var reader = new WaveFileReader(path!);
        }
        catch (Exception exception)
        {
            failures.Add($"native capture should leave a readable WAV file: {exception.Message}");
        }
    }
}
catch (Exception exception)
{
    failures.Add(exception.GetType().Name + ": " + exception.Message);
}
finally
{
    services.Dispose();
    AppPaths.Reset();
    try
    {
        if (Directory.Exists(smokeRoot)) Directory.Delete(smokeRoot, recursive: true);
    }
    catch (IOException)
    {
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("Smoke checks failed:");
    foreach (var failure in failures) Console.Error.WriteLine(" - " + failure);
    return 1;
}

Console.WriteLine("Smoke checks passed: auth state, audio setup, WASAPI/fallback capture, processing, transcript, speakers, summary, progress, and GitHub updates.");
return 0;

static void WriteFloatWaveFile(string path, double durationSeconds = 1, int sampleRate = 48_000, int channels = 2)
{
    var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
    var totalSamples = checked((long)Math.Round(durationSeconds * format.SampleRate * format.Channels));
    var samples = new float[Math.Min(format.SampleRate * format.Channels, 48_000)];
    using var writer = new WaveFileWriter(path, format);
    for (long writtenSamples = 0; writtenSamples < totalSamples;)
    {
        var samplesToWrite = (int)Math.Min(samples.Length, totalSamples - writtenSamples);
        for (var index = 0; index < samplesToWrite; index++)
        {
            var phase = (writtenSamples + index) / (double)format.Channels / format.SampleRate;
            samples[index] = (float)(Math.Sin(phase * Math.PI * 2 * 440) * 0.15);
        }

        writer.WriteSamples(samples, 0, samplesToWrite);
        writtenSamples += samplesToWrite;
    }
}

static bool IsPcm16Wave(byte[] payload)
{
    if (payload.Length < 44 || !payload.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !payload.AsSpan(8, 4).SequenceEqual("WAVE"u8))
        return false;

    var hasPcm16Format = false;
    var hasAudioData = false;
    var offset = 12;
    while (offset + 8 <= payload.Length)
    {
        var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset + 4, 4));
        if (chunkSize < 0 || offset + 8 > payload.Length) return false;
        var chunkDataStart = offset + 8;
        var chunkDataEnd = Math.Min(payload.Length, chunkDataStart + chunkSize);
        if (payload.AsSpan(offset, 4).SequenceEqual("fmt "u8) && chunkSize >= 16 && chunkDataEnd >= chunkDataStart + 16)
        {
            var audioFormat = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(chunkDataStart, 2));
            var channels = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(chunkDataStart + 2, 2));
            var sampleRate = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(chunkDataStart + 4, 4));
            var bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(chunkDataStart + 14, 2));
            hasPcm16Format = chunkSize == 16 && audioFormat == 1 && channels == 1 && sampleRate == 16_000 && bitsPerSample == 16;
        }
        else if (payload.AsSpan(offset, 4).SequenceEqual("data"u8) && chunkSize > 0)
        {
            hasAudioData = true;
        }

        if (chunkDataStart + chunkSize > payload.Length) return false;
        offset = chunkDataStart + chunkSize + (chunkSize & 1);
    }

    return hasPcm16Format && hasAudioData;
}

sealed class FakeOpenAiHandler : HttpMessageHandler
{
    public string? LastAuthorization { get; private set; }
    public List<byte[]> AudioPayloads { get; } = [];
    public List<string> TranscriptionBodies { get; } = [];
    public HttpStatusCode TranscriptionStatus { get; set; } = HttpStatusCode.OK;
    public string? TranscriptionErrorJson { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastAuthorization = request.Headers.Authorization?.Parameter;

        if (request.RequestUri?.AbsolutePath == "/v1/audio/transcriptions")
        {
            var requestBody = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
            TranscriptionBodies.Add(Encoding.ASCII.GetString(requestBody));
            var wavPayload = ExtractWavPayload(requestBody);
            if (wavPayload.Length > 0) AudioPayloads.Add(wavPayload);
            if (TranscriptionStatus != HttpStatusCode.OK)
            {
                return new HttpResponseMessage(TranscriptionStatus)
                {
                    Content = new StringContent(
                        TranscriptionErrorJson ?? "{\"error\":{\"message\":\"failed\"}}",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            return JsonResponse(JsonSerializer.Serialize(new { text = "A transcript returned by the fake OpenAI server." }));
        }

        if (request.RequestUri?.AbsolutePath == "/v1/responses")
        {
            var summary = JsonSerializer.Serialize(new
            {
                overview = "A concise smoke-test summary.",
                keyPoints = new[] { "The fake provider was reached." },
                decisions = new[] { "Keep the adapter covered by a deterministic test." },
                actionItems = new[] { new { text = "Review the OpenAI adapter", owner = "You", due = "Tomorrow", isComplete = false } },
                deadlines = Array.Empty<object>(),
                questions = Array.Empty<string>(),
                importantMoments = new[] { "The structured response was parsed." }
            });
            return JsonResponse(JsonSerializer.Serialize(new { output_text = summary }));
        }

        if (request.RequestUri?.AbsolutePath == "/v1/models")
            return JsonResponse("{\"data\":[]}");

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static byte[] ExtractWavPayload(byte[] requestBody)
    {
        var riff = Encoding.ASCII.GetBytes("RIFF");
        for (var index = 0; index <= requestBody.Length - riff.Length; index++)
        {
            if (!requestBody.AsSpan(index, riff.Length).SequenceEqual(riff)) continue;
            if (index + 12 > requestBody.Length || !requestBody.AsSpan(index + 8, 4).SequenceEqual("WAVE"u8))
                continue;

            var riffSize = BinaryPrimitives.ReadInt32LittleEndian(requestBody.AsSpan(index + 4, 4));
            var payloadLength = Math.Min(requestBody.Length - index, Math.Max(riffSize + 8, 0));
            return requestBody.AsSpan(index, payloadLength).ToArray();
        }

        return [];
    }
}

sealed class InMemoryUserEnvironmentStore : IUserEnvironmentStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public string? Get(string name) => _values.TryGetValue(name, out var value) ? value : null;
    public void Set(string name, string value) => _values[name] = value;
    public void Delete(string name) => _values.Remove(name);
}

sealed class HangingOpenAiHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK);
    }
}

sealed class FakeGitHubUpdateHandler : HttpMessageHandler
{
    private static readonly byte[] InstallerBytes = Encoding.UTF8.GetBytes("fake Meeting Assistant installer");

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.AbsolutePath == "/repos/test-owner/test-repo/releases/latest")
        {
            var payload = JsonSerializer.Serialize(new
            {
                tag_name = "v1.0.1",
                name = "Meeting Assistant 1.0.1",
                html_url = "https://github.com/test-owner/test-repo/releases/tag/v1.0.1",
                draft = false,
                prerelease = false,
                published_at = DateTimeOffset.UtcNow,
                assets = new[]
                {
                    new
                    {
                        name = "MeetingAssistant-Setup.exe",
                        browser_download_url = "https://github.com/test-owner/test-repo/releases/download/v1.0.1/MeetingAssistant-Setup.exe"
                    }
                }
            });
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, payload));
        }

        if (request.RequestUri?.AbsolutePath == "/test-owner/test-repo/releases/download/v1.0.1/MeetingAssistant-Setup.exe")
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(InstallerBytes) });

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json)
        => new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

sealed class HangingGitHubUpdateHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return new HttpResponseMessage(HttpStatusCode.OK);
    }
}

sealed class CapturingUpdateInstallerLauncher : IUpdateInstallerLauncher
{
    public List<string> Arguments { get; } = [];

    public void Launch(string installerPath, IReadOnlyList<string> arguments)
    {
        Arguments.Clear();
        Arguments.AddRange(arguments);
    }
}

sealed class FakeFirebaseHandler : HttpMessageHandler
{
    private readonly string _projectId;
    private readonly string _userId;
    private readonly string _payload;

    public FakeFirebaseHandler(string projectId, string userId, Meeting meeting)
    {
        _projectId = projectId;
        _userId = userId;
        var fields = new Dictionary<string, object>
        {
            ["title"] = StringValue(meeting.Title),
            ["startedAt"] = TimestampValue(meeting.StartedAt),
            ["updatedAt"] = TimestampValue(meeting.UpdatedAt),
            ["durationSeconds"] = DoubleValue(meeting.Duration.TotalSeconds),
            ["participantCount"] = new Dictionary<string, object> { ["integerValue"] = meeting.ParticipantCount.ToString() },
            ["audioSources"] = StringValue(meeting.AudioSources),
            ["transcriptJson"] = StringValue(JsonSerializer.Serialize(meeting.Transcript)),
            ["summaryJson"] = StringValue(JsonSerializer.Serialize(meeting.Summary)),
            ["speakerJson"] = StringValue(JsonSerializer.Serialize(meeting.Speakers))
        };
        _payload = JsonSerializer.Serialize(new
        {
            documents = new[]
            {
                new
                {
                    name = $"projects/{projectId}/databases/(default)/documents/users/{userId}/meetings/{meeting.Id}",
                    fields
                }
            }
        });
    }

    public bool SawPatch { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var expectedPath = $"/v1/projects/{_projectId}/databases/(default)/documents/users/{_userId}/meetings";
        if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == expectedPath)
            return JsonResponse(_payload);

        if (request.Method == HttpMethod.Patch && request.RequestUri?.AbsolutePath.StartsWith(expectedPath + "/", StringComparison.Ordinal) == true)
        {
            SawPatch = (await request.Content!.ReadAsStringAsync(cancellationToken)).Contains("updatedAt", StringComparison.Ordinal);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static Dictionary<string, object> StringValue(string value)
        => new() { ["stringValue"] = value };

    private static Dictionary<string, object> DoubleValue(double value)
        => new() { ["doubleValue"] = value };

    private static Dictionary<string, object> TimestampValue(DateTimeOffset value)
        => new() { ["timestampValue"] = value.ToUniversalTime().ToString("O") };
}

sealed class FakeAuthService : IAuthService
{
    public FakeAuthService(UserSession session) => CurrentSession = session;

    public UserSession? CurrentSession { get; }
    public Task<UserSession?> RestoreAsync() => Task.FromResult(CurrentSession);
    public Task<AuthResult> SignInAsync(string email, string password) => throw new NotSupportedException();
    public Task<AuthResult> SignUpAsync(string displayName, string email, string password) => throw new NotSupportedException();
    public Task<AuthResult> RequestPasswordResetAsync(string email) => throw new NotSupportedException();
    public Task<AuthResult> SignInOfflineAsync() => throw new NotSupportedException();
    public Task SignOutAsync() => Task.CompletedTask;
}
