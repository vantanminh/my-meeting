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

- `WindowsAudioCaptureService` captures separate WAV tracks with WASAPI, fully closes both writers before processing starts, and falls back to `DemoAudioCaptureService` when device access is unavailable.
- `JsonMeetingRepository` is atomic local JSON persistence with starter meetings for a new workspace.
- `DemoMeetingIntelligenceService` provides deterministic local processing so every UI state can be exercised.
- `LocalAuthService` and `LocalCloudSyncService` provide offline-first behavior. The included Firebase Auth/Firestore adapters activate from package-time configuration without changing the view model or screens.
- Firebase sync restores the signed-in user's meetings at startup, writes new meetings and edits to `users/{uid}/meetings/{meetingId}`, and keeps the local JSON cache as the recovery path when the network is unavailable.

To turn on the included Firebase REST adapters for a local run, set the public Firebase Web API key and project id in the process environment (never commit them to source):

```powershell
$env:MEETING_ASSISTANT_FIREBASE_API_KEY = "your-web-api-key"
$env:MEETING_ASSISTANT_FIREBASE_PROJECT_ID = "your-project-id"
dotnet run --project src/MeetingAssistant/MeetingAssistant.csproj
```

Without both values, the app intentionally stays in local mode and offers the offline workspace.

Before sharing a Firebase-backed build, enable Email/Password under Firebase Authentication, create the Firestore database, and deploy the user-scoped rules from the repository after selecting the intended project:

```powershell
$env:MEETING_ASSISTANT_FIREBASE_PROJECT_ID = "your-project-id"
.\firebase\deploy-rules.ps1
```

The rules allow an authenticated user to read and write only `users/{uid}/meetings/*`.

## OpenAI transcription and summaries

The Settings page can save the OpenAI key to the current Windows user's environment and choose the models used for transcription and summaries. The app reads these variables (the scoped key takes precedence):

```text
OPENAI_API_KEY
MEETING_ASSISTANT_OPENAI_API_KEY
MEETING_ASSISTANT_OPENAI_TRANSCRIPTION_MODEL
MEETING_ASSISTANT_OPENAI_SUMMARY_MODEL
```

The default transcription model is `gpt-4o-transcribe`. The default summary model is `gpt-4.1-mini`; both are editable in Settings. If no OpenAI key is configured, recordings use the deterministic local demo processor. A real WASAPI recording is reopened and converted to provider-compatible mono 16 kHz PCM16 WAV files before upload, with an extension-bearing filename and `audio/wav` content type. Long tracks are split into safe sub-25 MB uploads and their transcript timestamps are restored. Empty loopback tracks are skipped when another track is valid; preview fallback recordings continue to use the local demo. The original local recording remains available when every track is invalid so `Retry processing` never loses the capture.

For a development run, the minimum setup is:

```powershell
$env:OPENAI_API_KEY = "your-openai-api-key"
$env:MEETING_ASSISTANT_OPENAI_TRANSCRIPTION_MODEL = "gpt-4o-transcribe"
$env:MEETING_ASSISTANT_OPENAI_SUMMARY_MODEL = "gpt-4.1-mini"
dotnet run --project src/MeetingAssistant/MeetingAssistant.csproj
```

Never put an OpenAI key in source control or in the shareable installer. Each person should use a separate key or project key and set spending limits.

## Build a shareable installer

The installer is self-contained, so another Windows machine does not need the .NET runtime. Firebase's Web API key and project id are written into `firebase.config.json` inside the package at build time. They are public Firebase client configuration values, but should still be supplied through environment variables rather than committed:

```powershell
$env:MEETING_ASSISTANT_FIREBASE_API_KEY = "your-web-api-key"
$env:MEETING_ASSISTANT_FIREBASE_PROJECT_ID = "your-project-id"
.\installer\build-installer.ps1
```

Install Inno Setup first if the script reports that `ISCC.exe` is missing:

```powershell
winget install --id JRSoftware.InnoSetup -e
```

The generated installer is `dist\MeetingAssistant-Setup.exe`. Do not put an OpenAI API key in this installer; each user sets their own key from Settings.

## GitHub update channel and CI/CD release

The app checks a public GitHub repository for its latest non-prerelease release when a workspace opens and periodically in the background. When a newer release contains `MeetingAssistant-Setup.exe`, the app waits until it is idle, downloads the package with byte-level progress, and shows a dedicated in-app update surface for downloading, installing, and restarting. The installer runs silently and relaunches Meeting Assistant automatically, so the user never has to step through an installer wizard. Network checks are bounded by a short timeout so an unavailable GitHub does not block the UI.

The workflow at `.github/workflows/release.yml` runs on every push to `main` or `master` (and can be started manually). It finds the highest existing `vMAJOR.MINOR.PATCH` tag, increments the patch number, embeds that version into the executable and installer, builds the self-contained `.exe`, and publishes a GitHub Release with the installer asset. Version numbers are therefore advanced by release tags and do not require a source-code version commit or a CI loop.

Before enabling the workflow:

1. Make the GitHub repository **Public**. The installed app uses GitHub's unauthenticated latest-release endpoint.
2. In **Settings → Secrets and variables → Actions → Variables**, add `MEETING_ASSISTANT_FIREBASE_API_KEY` and `MEETING_ASSISTANT_FIREBASE_PROJECT_ID`. The API key is the Firebase Web API key; it is not an OpenAI key.
3. Ensure Actions can write repository contents. The workflow requests `contents: write` and uses the automatically provided `GITHUB_TOKEN`; no GitHub personal access token is packaged into the app.

The release build injects the GitHub owner and repository name automatically from the workflow context. Local builds default to `vantanminh/my-meeting`; override them when building a fork:

```powershell
$env:MEETING_ASSISTANT_GITHUB_OWNER = "your-github-owner"       # optional for this repository
$env:MEETING_ASSISTANT_GITHUB_REPOSITORY = "your-public-repository" # optional for this repository
$env:MEETING_ASSISTANT_APP_VERSION = "1.0.0"
.\installer\build-installer.ps1
```

The first CI run creates `v1.0.0`; later runs create `v1.0.1`, `v1.0.2`, and so on. A package built from source remains usable and uses the default public repository unless its update channel is explicitly disabled.

## Verification

Run the deterministic smoke checks:

```powershell
dotnet run --project tests/MeetingAssistant.Smoke/MeetingAssistant.Smoke.csproj --configuration Release
```

The smoke project checks initial auth state, both audio setup sources, WASAPI/fallback capture and finalized WAV files, processing progress, transcript speaker turns, speaker profiles, PCM16 OpenAI audio normalization and malformed-audio recovery, summary action items, GitHub release parsing/downloads, and bounded update timeouts.
