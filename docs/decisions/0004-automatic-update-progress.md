---
id: "0004"
type: decision
title: Use an in-app progress surface and silent installer for automatic updates
status: accepted
doc: docs/decisions/0004-automatic-update-progress.md
verify: "dotnet build MeetingAssistant.sln --configuration Release && dotnet run --project tests/MeetingAssistant.Smoke/MeetingAssistant.Smoke.csproj --configuration Release --no-build"
notes: "The WPF app checks GitHub Releases in the background, starts a streamed byte-progress update only when the workspace is idle, and displays download/install/restart stages in-app. The downloaded Inno Setup package is launched with VERYSILENT, SUPPRESSMSGBOXES, CLOSEAPPLICATIONS, and RESTARTAPPLICATIONS; the installer always runs the packaged executable after copying files so silent updates reopen the app without wizard navigation."
created_at: "2026-08-29T12:13:03.862Z"
updated_at: "2026-08-29T12:13:03.863Z"
links:
  - US-003
  - US-005
---

# Use an in-app progress surface and silent installer for automatic updates
