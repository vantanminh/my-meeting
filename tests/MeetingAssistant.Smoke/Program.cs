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
