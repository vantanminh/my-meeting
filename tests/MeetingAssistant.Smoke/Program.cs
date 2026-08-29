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
    using (var fakeHttpClient = new HttpClient(new FakeOpenAiHandler()))
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
    }
    Directory.Delete(fakeAudioDirectory, recursive: true);

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

Console.WriteLine("Smoke checks passed: auth state, audio setup, WASAPI/fallback capture, processing, transcript, speakers, summary, and progress.");
return 0;

sealed class FakeOpenAiHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
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
