# Meeting Assistant for Windows

Native C# / WPF desktop experience for recording a meeting locally, identifying speaker turns, and turning the conversation into a transcript, summary, decisions, deadlines, and action items.

## Run it

Requirements: Windows 10/11 and the .NET 8 SDK. The project uses WASAPI through NAudio for microphone and Windows system-audio loopback. If Windows cannot open an endpoint, the app automatically switches to a visible preview fallback so the flow remains recoverable.

```powershell
dotnet restore MeetingAssistant.sln
dotnet build MeetingAssistant.sln --configuration Release
dotnet run --project src/MeetingAssistant/MeetingAssistant.csproj
```

The first screen supports email/password auth and a local workspace. Local workspace mode is intentionally available for offline use; it stores meetings under `%LOCALAPPDATA%\MeetingAssistant`. No meeting bot joins a call.

## Product flow included

- Auth shell with sign-in, sign-up, validation, and offline access.
- Meetings hub with search across titles, speakers, and transcript text.
- Recording setup for microphone, system audio, quality, local-copy retention, and device test.
- Active recording with elapsed timer, tray status, mic/system level feedback, active-speaker signal, pause/resume/stop controls, and `Ctrl + Shift + R` global hotkey.
- Processing state with transcription, speaker recognition, analysis, progress, retry, and recovery states.
- Meeting review with editable transcript, speaker filtering, summary, key points, decisions, action items, deadlines, questions, and speaker renaming.
- Speaker profile management and settings for capture sources, retention, sync pause, shortcut state, and startup preference.
- Responsive native window: the sidebar collapses to an icon rail, dense two-column screens reflow into one column, and every workspace view remains vertically scrollable down to the 860×600 minimum window size.

## Architecture

The UI depends on small service contracts for auth, meeting persistence, audio capture, meeting intelligence, cloud sync, global hotkey, and tray state. The current build includes local adapters so it is useful without credentials:

- `WindowsAudioCaptureService` captures separate WAV tracks with WASAPI and falls back to `DemoAudioCaptureService` when device access is unavailable.
- `JsonMeetingRepository` is atomic local JSON persistence with starter meetings for a new workspace.
- `DemoMeetingIntelligenceService` provides deterministic local processing so every UI state can be exercised.
- `LocalAuthService` and `LocalCloudSyncService` provide offline-first behavior. Firebase Auth/Firestore and hosted STT/AI providers can replace these adapters without changing the view model or screens.

To turn on the included Firebase REST adapters for a local run, set the public Firebase Web API key and project id in the process environment (never commit them to source):

```powershell
$env:MEETING_ASSISTANT_FIREBASE_API_KEY = "your-web-api-key"
$env:MEETING_ASSISTANT_FIREBASE_PROJECT_ID = "your-project-id"
dotnet run --project src/MeetingAssistant/MeetingAssistant.csproj
```

Without both values, the app intentionally stays in local mode and offers the offline workspace.

## Verification

Run the deterministic smoke checks:

```powershell
dotnet run --project tests/MeetingAssistant.Smoke/MeetingAssistant.Smoke.csproj --configuration Release
```

The smoke project checks initial auth state, both audio setup sources, WASAPI/fallback capture, processing progress, transcript speaker turns, speaker profiles, and summary action items.
