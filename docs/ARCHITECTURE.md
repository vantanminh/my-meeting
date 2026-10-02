# Architecture

No application stack is locked by the harness installer. Record stack choices in
`docs/decisions/` when they constrain future work.

## Default Layering

```text
domain
  <- application
      <- infrastructure
          <- interface
              <- app surfaces
```

Inner layers must not depend on outer layers. Parse unknown input at boundaries
before it enters domain code.

## Meeting transcription and notes

Stopping a recording finalizes the local WAV tracks and saves the meeting immediately, then a single background worker runs `MeetingProcessingService` for that meeting: queued, transcribing, summarizing, completed, or failed. The meeting list and the Record action stay available, so another capture can start while notes are still being written. Progress is a sequence of real stages (recording saved, audio prepared, uploading and transcribing, transcript saved, writing notes, meeting saved) with elapsed time, not a synthetic percent. AssemblyAI is the default speech-to-text adapter behind `ITranscriptionService`. OpenAI summarization uses the Responses API with a strict meeting-notes schema. A saved transcript is not submitted again when only the summary needs to be retried. A meeting left in queued, transcribing, or summarizing is marked retryable on the next launch, and the saved transcript is kept. Folders under the recordings directory that contain a usable `microphone.wav` or `system-audio.wav` but no meeting row are added back by startup recovery and by Scan recordings.

Long recordings stay bounded and observable:

- Audio preparation runs off the UI thread. Each track is streamed to a 16 kHz mono PCM temp file in parallel, mixed on disk, and encoded to 48 kbps MP3 through Media Foundation before upload (about 20 MB per hour instead of about 115 MB of WAV). If the encoder is unavailable the WAV mix is uploaded instead.
- The provider `HttpClient` has no global timeout; every call sets its own. Uploads scale with file size, AssemblyAI polling waits 15 minutes plus half the audio length (20 minutes to 3 hours) and tolerates short provider outages, and each OpenAI summary request has an 8 minute limit with one retry on transient failures. Long transcripts are summarized in parallel parts.
- When the polling limit is reached, the job id is kept on the meeting, so a retry resumes polling instead of uploading again.
- Every stage reports a detail line (upload percent, queued, transcribing, summary parts) plus an elapsed clock, and `MeetingProcessingLog` writes a daily `logs/processing-yyyyMMdd.log` under the app data folder (kept 14 days).

## Responsiveness while processing

The UI thread only takes snapshots. `JsonMeetingRepository.SaveAsync` copies the meeting list, then serializes, encrypts and writes it on the thread pool; writes never overlap, and a save overtaken by a newer one is skipped. Loading decrypts and parses off the UI thread too. Processing progress redraws only the processing meeting's row (`RefreshMeetingRow`), and the meeting list and action inbox are updated by diff (`CollectionSync`) instead of being cleared and rebuilt. Search waits 180 ms after typing stops. The transcript list has a bounded height so it virtualizes, and `SmoothScroll` lets it scroll before the page does.

## Speaker identity

Speaker ids belong to one meeting (`SpeakerIdentity.For(meetingId, label)`), because "Speaker A" in two meetings is two different people. Renaming or merging inside a meeting changes only that meeting. On load, ids that older versions shared across meetings are split deterministically. The Speakers page lists people by the names users gave them and leaves generic labels to be named inside each meeting.

## Background processing and tray

`MainWindow` owns the tray wiring. Closing the window while a meeting is processing hides it instead of exiting. Processing keeps running, and the tray icon (a runtime-drawn state dot: red for recording, gold for processing, coral for failed) keeps its tooltip in sync with `MainViewModel.BackgroundStatusSummary`. Left-clicking the icon opens `TrayFlyoutWindow`, a quick-status panel bound to the same view model. It can stop a recording, cancel or retry processing, start a recording, open the app, or exit. When processing finishes and the window is not in front, a tray notification opens the finished meeting. Exit from the tray or flyout asks for confirmation while work is running.

## Display scaling

`UserPreferences.UiScale` (80% to 150%) is applied as a `LayoutTransform` on the authenticated and sign-in surfaces, never on the title bar. Responsive breakpoints are computed on the effective (unscaled) width, and the scale is capped so the layout never drops below its 760 × 520 design minimum. Users change it with Ctrl + plus, Ctrl + minus, Ctrl + 0, Ctrl + mouse wheel, or Settings > Account > Display size.

## Audio capture and transcription boundary

`WindowsAudioCaptureService.StopAsync` waits for both WASAPI endpoints to stop
and for both `WaveFileWriter` instances to close before returning
`RecordingData`. This keeps the RIFF sizes and `data` chunks finalized before
the processing pipeline opens the files. A loopback endpoint that produces only
an empty/tiny track is allowed; a healthy microphone track can still be
processed.

`OpenAiMeetingIntelligenceService` treats the local WAV files as untrusted
provider input. It reopens every candidate through NAudio, converts it to a
mono 16 kHz PCM16 WAV in a temporary per-processing directory, and uploads the
normalized file with an extension-bearing `.wav` name and `audio/wav` content
type. Tracks larger than the provider's file limit are split below 25 MB and
their transcript timestamps receive the corresponding chunk offset. Empty or
malformed optional tracks are skipped when another track is usable. If no track
can be prepared, the original local recording is retained and a retryable
`OpenAiServiceException` is shown. Normalized temporary files are removed after
processing; source recordings are not modified.

## Release and update channel

`UpdateChannelService` is an infrastructure adapter for the public GitHub Releases API. The package-time `update.config.json` contains only the public repository identity and installer asset name. The adapter validates the release tag as `MAJOR.MINOR.PATCH`, uses a bounded metadata request, accepts only HTTPS GitHub download URLs, limits the installer size, and streams the package to a per-user temporary folder while reporting byte-level progress. `MainViewModel` checks on workspace entry and on a six-hour timer, defers installation while recording or processing, and exposes download/install/restart stages to the WPF update surface. The Inno Setup package is launched with silent, close-applications, and restart-applications flags; its run entry always relaunches the packaged executable after the files are copied.

The release workflow owns version advancement: it derives the next patch version from existing `v*` tags, passes that version to both .NET and Inno Setup, and publishes the resulting installer as the matching GitHub Release asset. The app never needs a GitHub credential; the CI job uses its scoped `GITHUB_TOKEN` only for publishing.
