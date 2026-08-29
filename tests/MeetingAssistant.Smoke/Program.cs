using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using MeetingAssistant.Models;
using MeetingAssistant.Services;
using MeetingAssistant.ViewModels;

var failures = new List<string>();
var services = new AppServices();

try
{
    var viewModel = new MainViewModel(services);
    if (viewModel.CurrentView != WorkspaceView.Auth) failures.Add("new sessions should begin on auth");
    if (viewModel.MicrophoneOptions.Count < 1 || viewModel.SystemAudioOptions.Count < 1)
        failures.Add("audio setup should expose both source lists");
    viewModel.Dispose();

    var storedMeetings = await services.MeetingRepository.LoadAsync();
    if (storedMeetings.Count < 3) failures.Add("a new workspace should have useful starter meetings");
    await services.MeetingRepository.SaveAsync(storedMeetings);
    var reloadedMeetings = await services.MeetingRepository.LoadAsync();
    if (reloadedMeetings.Count != storedMeetings.Count) failures.Add("meeting cache should round-trip through JSON");

    var progressValues = new List<ProcessingProgress>();
    Console.WriteLine("processing...");
    var recording = new RecordingData
    {
        Title = "Smoke test meeting",
        StartedAt = DateTimeOffset.Now,
        Duration = TimeSpan.FromSeconds(2),
        Configuration = new AudioConfiguration()
    };
    var processed = await services.IntelligenceService.ProcessAsync(
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
    await File.WriteAllBytesAsync(fakeMicrophonePath, new byte[128]);
    await File.WriteAllBytesAsync(fakeSystemAudioPath, new byte[128]);
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

        var connection = await fakeOpenAi.TestConnectionAsync();
        if (!connection.Success) failures.Add("OpenAI connection test should accept a reachable API");

        var pendingConnection = await fakeOpenAi.TestConnectionAsync(apiKeyOverride: "pending-key");
        if (!pendingConnection.Success || fakeHttpHandler.LastAuthorization != "pending-key")
            failures.Add("OpenAI connection test should use the key currently entered in Settings");
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
}
catch (Exception exception)
{
    failures.Add(exception.GetType().Name + ": " + exception.Message);
}
finally
{
    services.Dispose();
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("Smoke checks failed:");
    foreach (var failure in failures) Console.Error.WriteLine(" - " + failure);
    return 1;
}

Console.WriteLine("Smoke checks passed: auth state, audio setup, WASAPI/fallback capture, processing, transcript, speakers, summary, progress, and GitHub updates.");
return 0;

sealed class FakeOpenAiHandler : HttpMessageHandler
{
    public string? LastAuthorization { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastAuthorization = request.Headers.Authorization?.Parameter;

        if (request.RequestUri?.AbsolutePath == "/v1/audio/transcriptions")
        {
            await request.Content!.ReadAsByteArrayAsync(cancellationToken);
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
