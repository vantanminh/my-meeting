---
id: "0005"
type: decision
title: Normalize captured audio before OpenAI transcription
status: accepted
doc: docs/decisions/0005-openai-audio-normalization.md
verify: "dotnet build MeetingAssistant.sln --configuration Release && dotnet run --project tests/MeetingAssistant.Smoke/MeetingAssistant.Smoke.csproj --configuration Release --no-build"
notes: "Native WASAPI capture is finalized completely before processing returns. The OpenAI adapter reopens each captured WAV through NAudio, downmixes/resamples it to a temporary mono 16 kHz PCM16 WAV, and uploads it with an extension-bearing .wav filename and audio/wav content type. Normalized tracks over the provider's 25 MB file limit are split below 25 MB and transcript timestamps receive chunk offsets. Empty optional loopback tracks are skipped; if no track can be prepared, the original local recording remains available and the UI reports a retryable error. Temporary normalized files are deleted after processing while original captures are preserved."
created_at: "2026-08-29T12:33:00.229Z"
updated_at: "2026-08-29T12:40:22.055Z"
links:
  - US-006
  - IN-006
  - US-002
---

# Normalize captured audio before OpenAI transcription
