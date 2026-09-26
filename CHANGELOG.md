# Changelog

## Unreleased

### Processing

- A one-hour meeting no longer uploads about 115 MB of WAV. Audio is prepared off the UI thread, mixed on disk, and sent as a 48 kbps MP3, about 20 MB per hour.
- Waiting for AssemblyAI scales with the recording length (20 minutes to 3 hours) instead of a fixed 2 hours. Brief provider outages while polling, or one failed summary request, are retried instead of failing the meeting.
- Long transcripts are summarized in parallel parts, and each summary request is limited to 8 minutes.
- The processing screen shows what is happening right now (upload percent, queued, transcribing, summary parts) and how long it has been running. Daily processing logs are written to `logs/` in the app data folder.

### Background and tray

- Closing the window while a meeting is processing keeps it running in the notification area and shows a notification when it is done.
- Clicking the tray icon opens a quick-status panel. It shows the recording timer or processing stage and can stop, cancel, retry, start a recording, open the app, or exit.
- The tray icon shows a colored dot for recording, processing, or failed.

### Recording

- Live microphone and system meters read 32-bit float WASAPI buffers and ease between samples about 30 times a second, so the bar follows speech instead of sitting still.
- Device check updates those same meters while it listens. A device that Windows will not open fails immediately instead of saving an empty preview recording.
- A failed transcription keeps the raw session folder, lists the meeting as failed, and can be retried. Settings can choose that folder and open it.
- Sample meetings whose ids start with `seed-` are removed from the local workspace on launch.
- The capture screen shows free disk space and an estimate of how much longer the current quality can record.

### Local workspace

- The app opens straight into a local workspace. Sign-in, sign-up, and Firebase are no longer used.
- Settings, theme, language, devices, models, and the recordings folder are written to the local settings file.

### Interface

- Six themes: Ink, Harbor, Dusk, Paper, Moss, and Amber. Paper, Moss, and Amber are light palettes with their own text and accent colors.
- Settings sections share one fixed row, so switching tabs no longer changes the menu width.
- Dropdowns open without a slide animation.
- Mouse-wheel scrolling eases toward the next position.
- On wide windows the sidebar and content scale up slightly.
- Display size from 80% to 150% with Ctrl + plus, Ctrl + minus, Ctrl + 0, Ctrl + mouse wheel, or Settings. Layout breakpoints follow the scaled size, so zooming in switches to the compact layout instead of clipping.
- Back links are now real buttons with an arrow icon. Sidebar navigation keeps 44 px targets with Fluent icons in the compact layout.
- Headers, badges, button rows, and settings rows wrap or trim instead of drawing text on top of each other in narrow or zoomed windows.
- A small red mark stays in the title bar whenever a recording is running.
- The sidebar opens Settings instead of asking to switch accounts.

### Updates and reset

- Unless updates are turned off, an available update downloads and installs on its own when the app is idle.
- Privacy settings can remove the app and keep data, delete meetings and recordings, or erase the local app data and startup entry as well.
