# Meeting Assistant — Product & Engineering Improvement Plan

Status: planning snapshot  
Audience: product owner and implementing agents  
Scope: native Windows WPF client in this repo  
Non-goals: calendar auto-join, meeting bots, macOS/web/mobile clients

This plan is based on a full read of `PRODUCT.md`, current screens in `MainWindow.xaml`, `MainViewModel`, audio/OpenAI/Firebase adapters, installer/update channel, and the six implemented stories (US-001–US-006). It is intentionally long: every item names the current gap, why it matters, and what “done” should look like.

---

## 1. Current product, stated honestly

Meeting Assistant already has a complete **vertical slice**:

1. Auth shell (email/password + local workspace)
2. Meetings hub with search
3. Recording setup → live capture → processing → review
4. Speaker rename across meetings
5. Settings (theme, language, OpenAI key, updates)
6. Offline-first local JSON cache
7. Optional Firebase Auth + Firestore REST sync
8. GitHub Releases auto-update with in-app progress
9. Custom dark/light theme and Vietnamese/English string map

The product **looks** like Otter-class meeting software. Several surfaces still **perform as a polished demo**. That is the central risk: users will trust UI copy that the code does not keep.

### 1.1 What is real today

| Capability | Reality |
| --- | --- |
| Mic + system loopback capture | Real WASAPI via NAudio, two WAV files, writers closed before processing |
| Pause / resume / stop | Pause skips writing samples; stop finalizes RIFF |
| OpenAI transcription + summary | Real when a user key is set; WAV is normalized to mono 16 kHz PCM16; long files split under 25 MB |
| Firebase sign-in / reset / Firestore meeting JSON | Real when installer/env config is present |
| Local recovery | Meetings persist under `%LOCALAPPDATA%\MeetingAssistant` |
| Global hotkey | `Ctrl+Shift+R` registered on the window HWND |
| Tray icon | WinForms `NotifyIcon` with Open / Start-stop / Exit |
| Auto-update | Public GitHub latest release, silent Inno Setup, deferred while recording/processing |
| Theme / language | Dark/light palettes; visual-tree + dictionary localization |

### 1.2 What looks real but is not

These are the highest-priority honesty problems. Shipping more features on top of them will amplify distrust.

| UI promise | Actual behavior | File / symbol |
| --- | --- | --- |
| Microphone / system / quality ComboBoxes | Hard-coded labels. Capture always uses **default** WASAPI endpoints. Quality never changes sample rate or bitrate. | `MainViewModel` ctor; `WindowsAudioCaptureService.StartWasapiCapture` |
| Device test | 850 ms delay, then fake levels `0.72` / `0.58` and “Both sources look good”. No device is opened. | `TestDevicesAsync` |
| “ACTIVE SPEAKER” / “Speaker recognition is on” | Live label is only “You” vs “Meeting participant” from **which track is louder**. No embeddings, no enrollment. | `RaiseLevels`; Speakers hint copy |
| “Profiles grow with every review” | Rename remaps `SpeakerId` text. No voice print, no auto-apply on the next meeting. | `SaveSpeakerAsync` |
| Capture quality “48 kHz / 16 kHz” | Ignored. Device native format is written, then later resampled to 16 kHz for OpenAI. | `AudioConfiguration.Quality` unused in capture |
| Retention 7 / 30 / forever | Saved to `settings.json` only. No sweeper deletes WAV or meetings. | `UserPreferences.RetentionOption` |
| “Open when Windows starts” | Checkbox persisted. **No Run-key / Startup-folder write.** | `StartOnLogin` |
| “Keep a local copy” | Passed into `AudioConfiguration` and never read. Recordings always land in `Recordings/`. | `KeepLocalCopy` |
| Processing checklist (5 mint dots) | Always shown as complete. Not bound to `ProcessingStageIndex`. | Processing surface in `MainWindow.xaml` |
| “THIS WEEK” stat | Bound to `TodayCount` (today only). Copy says week; subtitle says “recorded today”. | Dashboard stats |
| Greeting “Good morning” | Hard-coded, not time-of-day, not localized as a phrase with the name. | Dashboard welcome |
| Last sync “Just now · local cache” | Never a real timestamp or cloud result. | `LastSyncLabel` |
| Seed meetings (Priya / Marcus / Jamie, “MacBook microphone”) | Injected whenever `meetings.json` is missing, empty, or **JSON-corrupt**. New users see fake work history. Empty cache after a crash **replaces real data with seeds**. | `JsonMeetingRepository.SeedMeetings` |
| Demo intelligence | If no OpenAI key, processing invents a generic 3-turn transcript and summary. A real meeting becomes **fiction**. | `DemoMeetingIntelligenceService` |
| “Leave recording” | Navigates to Dashboard. Capture **keeps running**. No confirm, no dashboard recording chip. | `BackToMeetingsCommand` → `Navigate` |
| Close window while recording | `Window_Closing` disposes the VM; does not stop/save the session first. | `MainWindow.Window_Closing` |
| Sign out | Does not clear `Meetings` in memory. Local JSON is **one file for every account** on the machine. | `AppPaths.DataDirectory`; `SignOutAsync` |
| Pause + Resume buttons | Both always visible. Resume is enabled only when paused, but the extra control is constant visual noise. | Recording actions |
| Transcript filter chips | No selected state. Every chip looks the same. | Detail `ItemsControl` of filters |
| Summary Key points / Decisions / Questions | `TextBlock` only. Overview is the only editable summary field. | Detail summary column |
| Action items | Checkbox + read-only owner/due. No add / edit / delete / date picker. | `ActionItem` template |
| Toast | Never auto-hides, never dismissible. Last toast sticks forever. | `HasToast` |
| Localization | Visual-tree walk of unbound `TextBlock`s plus a 290-entry English→Vietnamese dictionary. Bound `StringFormat` (`{0} speakers`, `{0} conversations`) stays English. ComboBox option lists stay English until translated one-by-one. Fragile and incomplete. | `LocalizationService` |
| Tray icon | `SystemIcons.Application` (generic Windows icon). | `TrayService` |
| Auto-update | Installs automatically when idle. Settings has no “check only / ask first / disable” policy. Overlay tint `#D90B0F17` ignores light theme. | `RefreshUpdateAvailability`; update overlay |

---

## 2. Product thesis (what “better” means)

The app should keep the PRODUCT.md promise:

> Record locally → understand speakers → transcribe → analyze → leave with next steps.  
> No bot joins the call.

“Better” is not more screens. It is:

1. **Trust** — every control does what it says; demo data never pretends to be the user’s meeting.
2. **Capture confidence** — real devices, real levels, recoverable failures, disk/permission honesty.
3. **Review usefulness** — play audio, jump to a timestamp, edit everything, export, act on tasks.
4. **Speaker memory** — names and (later) voice profiles actually transfer to the next call.
5. **Calm Windows UX** — Fluent-adjacent density, real icons, empty states, confirmations, keyboard, a11y.
6. **Safe cloud** — per-user local isolation, conflict-aware sync, no silent data loss.
7. **Ship quality** — unit tests for domain logic, UI Automation for journeys, signed installer.

Calendar integration and automatic meeting joining stay out of scope until the above is solid (`PRODUCT.md` §10).

---

## 3. Recommended sequence (phases, not calendar)

Implement in this order. Later phases assume earlier honesty work is done.

| Phase | Intent | Why this order |
| --- | --- | --- |
| **P0 — Trust & correctness** | Stop lying. Wire settings. Protect data. | Users already have a shippable installer. Fake device test / fake transcript / seed overwrite are product-breaking. |
| **P1 — Capture that professionals can trust** | Real devices, real test, safer recording UX. | Core differentiator vs “another notes app”. |
| **P2 — Review that replaces a notebook** | Playback, edit-all, actions inbox, export. | This is why people reopen the app tomorrow. |
| **P3 — Real speaker intelligence** | Diarize properly; optional embeddings; merge/split. | UI already claims this. Do not add more copy until the pipeline exists. |
| **P4 — UI / UX system upgrade** | Split views, iconography, motion, empty states, a11y. | Visual polish on a dishonest core would be wasted. After P0–P2, redesign pays off. |
| **P5 — Account, sync, and privacy hardening** | Per-user stores, SSO, retry queue, encryption at rest. | Required before multi-device is marketed as reliable. |
| **P6 — Architecture & quality bar** | Split God VM/XAML, test matrix, observability. | Makes every later story cheaper. Can start in parallel from P1. |
| **P7 — Growth features** | Templates, live notes, cost meter, process loopback, optional WinUI. | Only after the product is trustworthy. |

---

## 4. Phase 0 — Trust and correctness

Ship these before any new marketing surface.

### 4.1 Never invent a meeting the user did not have

- **Empty workspace is empty.** Remove automatic seed on first run from the production path. Keep seeds behind a debug flag or smoke-test helper.
- **Corrupt `meetings.json` must not become seed meetings.** Quarantine the broken file (`meetings.json.bak-{timestamp}`) and open an empty list with a recovery toast.
- **Demo intelligence must be impossible to confuse with a real transcript.** If no OpenAI key: refuse processing with a Settings CTA, or produce a clearly labeled “Preview (not your audio)” card. Never write invented speech into the meetings list as if it were the recording.
- **Store audio paths on `Meeting`.** Today `RecordingData` paths are dropped after processing. Retry, playback, and retention all need `MicrophonePath` / `SystemAudioPath` (or a session folder id) on the model.

### 4.2 Settings that persist must execute

| Setting | Required behavior |
| --- | --- |
| `StartOnLogin` | Write/delete `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` (or Startup `.lnk`) pointing at the installed exe. Verify after save. |
| Retention | Background sweep of `Recordings/` older than 7/30 days; never delete a meeting JSON the user can still open unless they opted into “delete meeting + audio”. Show “X hours of audio on disk”. |
| `KeepLocalCopy` | If false, delete session WAVs after successful processing **and** a confirmed retry window; if processing fails, keep files. |
| Device lists | Enumerate `MMDeviceEnumerator` capture + render/loopback devices. Persist device IDs, not display names. |
| Quality | Map Balanced / High / Compact to capture `WaveFormat` (or post-record downsample). Compact should actually be ~16 kHz. |
| Theme / language | Already apply live; also persist without requiring “Save settings” for those two (or make Save the only path and say so). |

### 4.3 Recording session safety

- Closing the window, signing out, or installing an update while capturing must **Stop → offer process / discard / save audio only**.
- “Leave recording” should mean **minimize to tray / go to dashboard with a persistent REC chip**, not silently abandon the recording view. Copy must match.
- Confirm Stop & process for sessions longer than a few seconds.
- `Window_Closing` must cancel in-flight processing cleanly and flush writers.

### 4.4 Local tenancy

- Partition data: `%LOCALAPPDATA%\MeetingAssistant\users\{userId}\meetings.json` and `Recordings\`.
- Offline workspace uses a dedicated `users\offline\` (or machine-local) store.
- Sign-out clears the in-memory collection and does not leak another account’s meetings.

### 4.5 UI honesty on existing screens

- Bind processing steps to `ProcessingStageIndex` (pending / current / done / error).
- Show Pause **or** Resume, not both.
- Fix THIS WEEK vs today (either count the week or rename the card).
- Time-aware greeting (morning / afternoon / evening) in both languages.
- `LastSyncLabel` from the latest successful `SyncResult` + `UpdatedAt`.
- Selected transcript filter chip uses the mint selected style.
- Toasts auto-dismiss after ~4s and have an ×.

### 4.6 Proof for P0

- Unit: repository empty-on-missing, quarantine-on-corrupt, no seed in production.
- Unit: device enumeration returns real IDs; capture service uses the selected ID.
- Integration: StartOnLogin writes and clears the Run key in a test hive or mocked store.
- Smoke: processing without API key does not insert fictional transcript as Ready.
- UI: device test opens devices and moves real level bars.

---

## 5. Phase 1 — Capture professionals can trust

### 5.1 Real device test

- Open the selected mic and loopback for 3–5 seconds.
- Drive the existing progress bars from `LevelsChanged`.
- Fail with actionable text: “Windows denied the microphone”, “No loopback endpoint”, “This device is exclusive to another app”.
- Optional: play a short tone / countdown so the user can speak and see the mic bar move.
- Surface Windows privacy settings deep-link (`ms-settings:privacy-microphone`).

### 5.2 Capture pipeline upgrades

- **Process-aware loopback** (Windows 10 2004+ / 11): optional “Meeting app audio only” that actually targets Zoom / Teams / Chrome process IDs via WASAPI process loopback — today that ComboBox item is a label.
- **Pause** should stop the stopwatch **and** optionally stop writers consistently; document whether paused time is included in duration (today paused time is excluded from elapsed because the stopwatch stops, but users may expect wall-clock).
- **Disk budget:** check free space before start; warn under ~500 MB; fail under a safer floor.
- **Max duration warning** at 1h / 2h (cost and 25 MB chunking).
- **Notification / ducking:** optional “ignore system sounds under N seconds” is later; for P1, show a note that loopback captures **all** system audio including pings.
- **AGC / noise suppression:** do not invent DSP in v1; if added, use Windows Audio Processing or a known library and keep a “raw” toggle.

### 5.3 Recording UX

- Persistent compact REC bar when the user is on Dashboard/Settings during an active capture (timer, levels, Stop).
- Tray: custom branded icon; red badge while recording; balloon “Recording 12:04 — double-click to return”.
- Optional minimize-to-tray on close (setting), vs exit.
- Live peak meters that decay smoothly (not 1 Hz clock only — levels already event-driven).
- Optional **waveform strip** (last 30s) for confidence, not decoration.

### 5.4 Hotkey

- User-configurable chord (record start/stop, optional pause).
- Conflict UI when `RegisterHotKey` fails (already has status string; add “choose another”).
- Pause hotkey separate from start/stop.

---

## 6. Phase 2 — Review that replaces a notebook

This is the largest product gap versus Otter / tl;dv / Fireflies.

### 6.1 Audio in the review surface

- Player for mic, system, or a mixed preview (mix at review time, do not require a third file).
- Click a transcript timestamp → seek.
- Speed 1× / 1.5× / 2×.
- Keyboard: space play/pause, j/k skip 5s.
- If files were deleted by retention, show “Audio expired; transcript remains”.

### 6.2 Transcript editing that matches the promise

Today only `TranscriptSegment.Text` is a `TextBox`. Add:

- Edit speaker on a turn (dropdown of known speakers + “New speaker”).
- Split / merge adjacent turns.
- Insert a turn.
- Delete a turn with undo.
- Copy all / copy visible filter as Markdown.
- Search-in-transcript with highlight (reuse hub query when opening from search).
- Virtualized list (`VirtualizingStackPanel` / `ListView`). Current `ItemsControl` will choke on a 90-minute meeting.

### 6.3 Summary as a working document

Every `MeetingSummary` collection must be editable:

- Key points, decisions, questions, important moments: inline edit, add, remove, reorder.
- Action items: edit text, owner (combo of speakers + free text), due (date or “no date”), complete, add, delete.
- Deadlines: same.
- **Re-summarize** after transcript edits (new OpenAI call, preserve user-locked fields).
- `Notes` is already on the model and unused — add a freeform notes card.

### 6.4 Meetings hub that scales

- Real empty state for **zero meetings** (not “No meetings match that search”).
- Distinct empty search state (already close; keep it).
- Delete meeting (and optional audio).
- Archive / restore.
- Status chips: Ready / Processing / Failed (enum exists, unused in UI).
- Sort: date, duration, action-item count.
- Filter: this week, with open actions, failed, unsynced.
- Search also hits overview, action text, notes.
- Context menu: open, rename, delete, export, reveal audio folder.
- **Do not** show seed colleagues as if they were the user’s team.

### 6.5 Actions inbox

Dashboard “ACTION ITEMS” is a count only.

- Dedicated “Next steps” view (or dashboard section): incomplete items across meetings, grouped by due date.
- Click through to the source meeting + timestamp if we store one later.
- Mark done from the inbox; persist + sync.

### 6.6 Export and share (local first)

Privacy promise says nothing leaves the device until the user chooses. Export is the honest share path:

- Markdown (transcript + summary)
- Plain text
- JSON (already the storage shape)
- Copy action items
- Later: DOCX / PDF print view
- Never auto-upload audio to Firebase until an explicit “include audio in cloud backup” setting exists (audio is not synced today — keep it that way by default)

---

## 7. Phase 3 — Speaker intelligence (make the Speakers page true)

### 7.1 Diarization that matches the Speakers page

Current live “active speaker” is a level comparison. OpenAI path:

- Two tracks transcribed separately, then interleaved by timestamp. Overlap/duplication is common (user’s voice in both mic and loopback).
- `gpt-4o-transcribe-diarize` is in the model list and parsed, but speakers are still “Microphone · SPEAKER_1” style labels, not people.

Required pipeline:

1. **Echo cancel / overlap policy:** prefer mic for “You”; strip near-duplicate loopback segments that match mic within a time window and similarity threshold.
2. **One diarized timeline** for the meeting (not two independent transcripts glued together).
3. Default transcription language from Settings (vi / en / auto). Today no `language` field is sent.
4. Map diarized labels → speaker profiles with stable IDs (`StableSpeakerId` already hashes the name — good start).

### 7.2 Profile operations from PRODUCT.md (still missing)

- Merge two profiles (“Speaker 2 is Priya”).
- Split a profile (“this turn was not Priya”).
- Delete / hide a profile.
- Edit role and accent color (model has both; UI ignores `AccentColor` and role is read-only).
- Color the transcript avatar from `AccentColor`.
- Meeting count that increments for real (today seed numbers are fake; new speakers get `Meetings = 1` and are not recounted).

### 7.3 Voice profiles (only after 7.1)

Do not advertise embeddings until:

- A short enrollment clip or “use turns from past meetings” exists.
- Matching is applied at process time and can be overridden.
- Profiles sync with the Firebase user (PRODUCT.md secondary journey).
- Users can delete biometric-like data.

Until then, change Speakers copy to “Saved names” not “Voice profile”.

---

## 8. Phase 4 — UI / UX system upgrade

The current visual language is a custom dark “ink + mint” shell with Segoe UI and Unicode glyphs (`◈ ◎ ✦ ◫ Ⅱ`). It is distinctive but uneven: CheckBoxes are stock Aero, icons are not an icon set, one 1,160-line XAML file owns every screen, and localization is a post-process tree walk.

### 8.1 Design system

- **Iconography:** Segoe Fluent Icons or a small SVG/Path set. Replace title-bar `— □ ×`, nav glyphs, and tray icon with branded assets.
- **Type scale:** document display / title / body / meta sizes; stop mixing 9 / 10 / 11 / 12 / 13 / 14 / 17 / 23 / 25 / 27 / 28 / 29 / 48 / 68 without a scale.
- **Control kit:** restyle `CheckBox`, `PasswordBox` (show/hide), focus rings, validation borders. Primary button hover currently does nothing meaningful.
- **Motion:** 150–200 ms ease for view changes, toast in/out, sidebar compact. WPF `BeginStoryboard` or a tiny behavior — no bounce-for-bounce’s-sake.
- **Elevation:** cards are flat; add a single shadow token for hero / modal / toast.
- **Light theme:** already exists; audit contrast (mint on white, gold on light). Update overlay must use theme brushes, not a hard-coded dark scrim.
- **Density modes:** comfortable (default) vs compact for 125–150% DPI (seed design critique already mentioned 125% scaling).
- **System theme follow:** third option “Windows”.

### 8.2 Information architecture

Suggested nav after P2:

1. Meetings  
2. Next steps (actions inbox)  
3. Speakers  
4. Settings  

Quick Start “New recording” stays. Add a global REC chip in the header when capturing.

Meeting detail: **tabs or sticky subnav** — Transcript | Summary | Actions | Audio — so a 90-minute review is not one endless `StackPanel`. Keep the two-column wide layout; keep the existing narrow reflow.

### 8.3 Screen-by-screen UX

**Auth**

- Show/hide password.
- Inline field validation before submit (email shape, password ≥ 6, name required on sign-up).
- Loading state that disables the form (command `CanExecute` exists; bind opacity/spinner on the button consistently).
- SSO slots (Google / Microsoft) only when Firebase providers are actually enabled — do not show dead buttons.
- Offline path: explain that this machine’s meetings will not merge into a later Firebase account until an explicit import exists (because of P0 tenancy).
- Forgot-password success should not look like an error (already uses `AuthInfo` — keep, add icon).

**Dashboard**

- Drop or shrink the large hero once the user has ≥ 1 real meeting (hero is onboarding, not a daily tax).
- Greeting + one sentence of “3 open actions, last meeting yesterday”.
- Meeting rows: status, failed badge, unsynced badge, first-line snippet.
- Hover actions: open / more (delete, export).

**Setup**

- Progressive disclosure: title + two devices + Start on top; quality / keep-local under “Advanced”.
- Live meters **during** device test (not after a fake delay).
- Default title: `Meeting · {local date time}` instead of always “Weekly team sync”.

**Recording**

- Single primary Stop; Pause toggles label.
- Confirm leave vs keep-in-background.
- Disk / permission errors as a banner, not a toast-only.

**Processing**

- Stage list bound to progress (see P0).
- Estimated time (heuristic from duration + track count).
- Cancel stays; add “Save audio and finish later”.
- Never show mint dots for unfinished work.

**Detail**

- Sticky header with title, player, save, more (export, delete, reprocess).
- Speaker chips use accent colors and selected state.
- Empty summary sections show “Add a key point” not a blank card.

**Speakers**

- Honest copy until embeddings exist.
- Empty state when the workspace has no speakers.
- Merge affordance.

**Settings**

- Sections as a left mini-nav on wide windows (Account, Capture, AI, Privacy, Updates, Shortcuts).
- Update policy: Automatic / Ask / Off.
- Changelog excerpt from GitHub release body.
- OpenAI: optional spend warning, last test time, “Remove key”.
- Reveal which Firebase project is configured (project id only, not the key).

### 8.4 Localization 2.0

Replace the dictionary + visual-tree walker:

- `.resx` (or a JSON resource per language) with **keys**, not English source strings as keys.
- Every visible string is bound (`{x:Static}` / markup extension).
- Format strings for counts, dates, durations (`DateLabel` today is English “Today/Yesterday/MMM d, yyyy” on the model).
- ComboBox items are localized display + stable value.
- Seed/demo content, if any remains for tests, is language-aware.
- Tray menu and installer (Inno Setup) localized.

### 8.5 Accessibility and input

- Keyboard: Tab order, `Esc` back, `Ctrl+F` search, `Ctrl+S` save, `Ctrl+N` new recording.
- High-contrast: do not rely on mint-on-mint-dim only.
- Screen reader: `AutomationProperties.Name` is started; extend to every icon-only button and the custom chrome.
- 125% / 150% / 200% DPI pass on 1366×768 and 1920×1080.
- Do not use color alone for recording state (add the word REC).

### 8.6 Onboarding

- First run: 3 steps — privacy promise, pick mic + test, optional OpenAI key.
- Sample **tour** can use clearly marked sample content, not silent seeds in the real list.
- After first successful recording, hide the hero / checklist.

---

## 9. Phase 5 — Account, sync, privacy

### 9.1 Auth

- Google / Microsoft OAuth via Firebase (PRODUCT.md already lists this as optional).
- Change display name; change password; delete account (cloud + local).
- Email verification when Firebase requires it.
- Session restore already uses refresh + DPAPI store — add expiry UX (“signed out, local meetings still here”).
- Sidebar “Switch account” should open an account picker or confirm sign-out, not immediately sign out.

### 9.2 Sync semantics

Today: last `UpdatedAt` wins; Firestore stores transcript/summary/speakers as **opaque JSON strings**; audio is never uploaded; failed PATCH still returns `Success: true` with “Saved locally”.

- Outbox queue: retry unsynced meetings on a timer and on network return.
- Conflict: if both sides changed, keep both revisions or a 3-way field merge; never drop local edits silently.
- Status enum: Synced / Pending / Offline / Conflict / Paused — replace free-text `SyncStatus` for UI.
- Do not claim “Firebase-ready workspace” when the user is offline-only.
- Speaker profiles as first-class documents (`users/{uid}/speakers/{id}`), not only embedded in each meeting.
- Optional encrypted audio backup to Storage — **explicit opt-in**, size limits, retention aligned with Settings.

### 9.3 Privacy and security

- Meetings JSON encryption at rest (DPAPI, like the Firebase session).
- OpenAI key: prefer Credential Manager / DPAPI over process environment (env is convenient but visible to other apps). Decision 0003 can be extended, not silently replaced.
- Redact key from logs; never write meetings to `startup-error.log`.
- Firestore rules already scope `users/{uid}/meetings/*` — add speaker and settings paths the same way.
- In-app privacy page: what is local, what is Firebase, what is OpenAI, retention, how to wipe.
- Optional “lock workspace” with Windows Hello.

---

## 10. Phase 6 — Architecture and quality

The slice is one process, one window, one 1,300-line view model, one 1,160-line XAML, contracts that are good, implementations that mix UI strings and I/O.

### 10.1 Structure

```text
MeetingAssistant.Core        models, merge, search, retention
MeetingAssistant.Capture     WASAPI adapter
MeetingAssistant.Intelligence  OpenAI + interfaces
MeetingAssistant.Sync        Firebase + local repo
MeetingAssistant.App         WPF views per screen
```

- Split `MainViewModel` into Auth / Meetings / Record / Review / Settings / Update view models.
- Split `MainWindow.xaml` into `Views/*.xaml` UserControls (AuthView, DashboardView, …).
- Replace `AppServices` new-up with a small composition root (even a manual container).
- Keep decision 0001 (WPF). Do **not** rewrite to WinUI 3 until the product is stable; treat WinUI as a later shell option.

### 10.2 Testing (TEST_MATRIX is still TBD)

| Layer | What to add |
| --- | --- |
| Unit | MergeMeetings, search, retention sweeper, audio normalize/split (already partly in smoke), transcript overlap collapse, settings persistence |
| Integration | Fake Firebase/OpenAI handlers (already in smoke) extracted to a test project; per-user repo isolation |
| UI Automation | Auth, real device test (or mocked capture), recording safety dialogs, review edit/save, export |
| Platform | WASAPI default + named device on a Windows runner; installer silent upgrade |

Move smoke from a console `Program.cs` into `MeetingAssistant.Tests` (xUnit/NUnit) so CI can fail per test.

### 10.3 Observability

- Ring-buffer log in Settings → “Copy diagnostics” (no secrets).
- Optional Sentry or similar only with consent.
- Processing errors already map some OpenAI statuses — keep and expand (quota, 25 MB, malformed WAV).

### 10.4 Release

- Authenticode sign the installer (SmartScreen).
- Separate **CI test** workflow from **release** (today every main push cuts a patch release).
- Semver policy: features = minor, fixes = patch; stop auto-bumping patch on every docs merge.
- Update setting: Automatic / Notify / Off.
- Show GitHub release notes in the overlay.

---

## 11. Phase 7 — Later / growth (do not start early)

- Live notes / bookmarks while recording (timestamped).
- Live partial transcript (streaming STT) — expensive and hard; only after batch quality is good.
- Meeting templates (“Standup”, “1:1”, “Discovery”) that change the summary schema.
- Custom summary prompt.
- Cost estimate before process (duration × model rates).
- Multiple AI providers (Azure OpenAI, local Whisper) behind the existing `IMeetingIntelligenceService`.
- Recurring action items / reminder toast.
- Tags, folders, pinned meetings.
- Full-text index if the library grows large.
- Teams/Zoom overlay window (always-on-top mini recorder).
- SQLite instead of one JSON file.
- WinUI 3 / Mica / snap layouts if WPF chrome becomes the limiter.
- Cross-device audio backup.
- Mobile/web — out of scope per PRODUCT.md.

---

## 12. Suggested story packets

Create these with `harness story add` when implementation starts. Do not open all at once.

### P0

| ID (suggested) | Title | Lane |
| --- | --- | --- |
| US-010 | Empty workspace without production seeds; quarantine corrupt cache | high-risk |
| US-011 | Per-user local data isolation and sign-out cleanup | high-risk |
| US-012 | Execute Start on login, retention sweep, keep-local-copy | normal |
| US-013 | Real WASAPI device enumeration and quality mapping | high-risk |
| US-014 | Honest processing UI + demo/OpenAI gating | normal |
| US-015 | Recording leave/close/sign-out/update safety | high-risk |
| US-016 | Dashboard copy/stat/toast/filter-chip honesty | tiny/normal |

### P1

| ID | Title | Lane |
| --- | --- | --- |
| US-020 | Real device test with live levels and privacy deep-link | normal |
| US-021 | In-app REC chip + tray branding while capturing | normal |
| US-022 | Disk budget and duration warnings | normal |
| US-023 | Configurable hotkeys | normal |

### P2

| ID | Title | Lane |
| --- | --- | --- |
| US-030 | Review audio player + timestamp seek | high-risk |
| US-031 | Full transcript edit (speaker, split/merge, virtualized) | high-risk |
| US-032 | Editable summary collections + re-summarize | normal |
| US-033 | Meeting delete/archive/status + hub filters | normal |
| US-034 | Actions inbox across meetings | normal |
| US-035 | Export Markdown / text / JSON | normal |

### P3

| ID | Title | Lane |
| --- | --- | --- |
| US-040 | Overlap collapse + single diarized timeline | high-risk |
| US-041 | Transcription language setting | normal |
| US-042 | Speaker merge/split/color/role | normal |
| US-043 | Optional voice enrollment (only after 040) | high-risk |

### P4

| ID | Title | Lane |
| --- | --- | --- |
| US-050 | Split views + Fluent icon system | normal |
| US-051 | Resx localization replacing dictionary walker | high-risk |
| US-052 | Onboarding + first-run empty states | normal |
| US-053 | Keyboard, DPI, contrast, AutomationNames | normal |
| US-054 | Settings IA + update policy + release notes | normal |

### P5–P6

| ID | Title | Lane |
| --- | --- | --- |
| US-060 | Sync outbox + conflict status | high-risk |
| US-061 | SSO + account management | high-risk |
| US-062 | DPAPI meetings + privacy page | high-risk |
| US-070 | xUnit test project + fill TEST_MATRIX | normal |
| US-071 | CI test-only workflow; signed installer track | high-risk |

---

## 13. Engineering constraints to keep

From existing decisions — do not casually reverse:

- **0001** — WPF .NET 8 native client; services stay UI-independent.
- **0002** — Public GitHub Releases as update channel; no GitHub credential in the app.
- **0003** — OpenAI key in the Windows user environment / HKCU (extend with Credential Manager if we change this; record a new decision).
- **0004** — Automatic update with progress; add a user policy on top, do not remove the overlay.
- **0005** — Normalize audio before OpenAI; keep original files for retry.

Still true from PRODUCT.md:

- No bot in the call.
- Offline workspace remains available.
- OpenAI keys never go in the installer.
- Firebase rules stay user-scoped.

---

## 14. What not to do in the next slice

- Do not add more marketing copy about “voice profiles” or “speaker recognition” until P3 exists.
- Do not seed fake teammates into a real workspace.
- Do not upload raw audio to Firestore as JSON.
- Do not rewrite the app in WinUI/MAUI to “improve UX” before P0–P2.
- Do not add a second AI vendor before the OpenAI path has language, overlap collapse, and re-summarize.
- Do not hand-edit harness story/intake files; use `harness` when cutting stories from §12.

---

## 15. Immediate next implementation slice (smallest valuable start)

When this plan is accepted, implement **P0 only**, in this order:

1. US-010 empty/quarantine repository (stops silent data loss and fake history)
2. US-014 gate demo processing (stops fictional transcripts)
3. US-013 + US-020 device list + real test (stops the most visible lie)
4. US-015 session safety (stops lost recordings on close)
5. US-011 per-user store (stops account bleed)
6. US-016 visual honesty (cheap, high polish)

After that, P2 US-030/031/035 is the first increment users will feel as “this replaced my notes.”
