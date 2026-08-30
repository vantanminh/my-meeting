# Test Matrix

Maps product behavior to proof. Smoke is the integration gate; it requires a Windows desktop runtime.

| Story | Contract | Unit | Integration | E2E | Platform | Status | Evidence |
| --- | --- | --- | --- | --- | --- | --- | --- |
| US-001 | Product shell and capture loop | yes | yes | no | no | implemented | tests/MeetingAssistant.Smoke |
| US-002 | OpenAI intelligence path | yes | yes | no | no | implemented | tests/MeetingAssistant.Smoke |
| US-003 | Auth / per-user workspace | yes | yes | no | no | implemented | tests/MeetingAssistant.Smoke |
| US-004 | GitHub update channel | yes | yes | no | no | implemented | tests/MeetingAssistant.Smoke |
| US-010 | Empty workspace, quarantine | yes | yes | no | no | implemented | tests/MeetingAssistant.Smoke |
| US-011 | Per-user `users/{id}` isolation | yes | yes | no | no | implemented | tests/MeetingAssistant.Smoke |
| US-012 | Settings that execute | yes | yes | no | no | implemented | tests/MeetingAssistant.Smoke |
| US-013 | Real devices and quality hint | yes | yes | no | no | implemented | tests/MeetingAssistant.Smoke |
| US-014 | Honest processing / OpenAI gate | yes | yes | no | no | implemented | tests/MeetingAssistant.Smoke |
| US-015 | Recording leave/close/sign-out | yes | yes | no | no | implemented | MainWindow + ViewModel |
| US-016 | Dashboard honesty | yes | yes | no | no | implemented | WeekStats + greeting + chips |
| US-020 | Live WASAPI device test | yes | yes | no | no | implemented | WindowsAudioCaptureService.TestAsync |
| US-021 | REC chip and background capture | no | no | no | no | implemented | MainWindow RecordingChip |
| US-030 | Review editors, export, inbox | yes | yes | no | no | implemented | MeetingExport + inbox |
| US-040 | Overlap, language, speaker merge | yes | yes | no | no | implemented | TranscriptRepair |
| US-050 | Onboarding and update policy | no | no | no | no | implemented | Settings + overlay |
| US-060 | Sync outbox | yes | yes | no | no | implemented | JsonSyncOutbox |
