# Changelog

## Unreleased

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
- A small red mark stays in the title bar whenever a recording is running.
- The sidebar opens Settings instead of asking to switch accounts.

### Updates and reset

- Unless updates are turned off, an available update downloads and installs on its own when the app is idle.
- Privacy settings can remove the app and keep data, delete meetings and recordings, or erase the local app data and startup entry as well.
