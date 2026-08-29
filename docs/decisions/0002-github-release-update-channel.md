---
id: "0002"
type: decision
title: Use public GitHub Releases as the update channel
status: accepted
doc: docs/decisions/0002-github-release-update-channel.md
verify: dotnet build MeetingAssistant.sln --configuration Release
notes: "Release tags use vMAJOR.MINOR.PATCH. CI computes the next patch tag from existing v* tags, builds the version into the executable and Inno Setup metadata, and uploads MeetingAssistant-Setup.exe to the public GitHub Release. The app calls the unauthenticated latest-release endpoint over HTTPS, matches the configured installer asset, downloads to a per-user temp folder, and launches the installer only after a successful download. Repository identity is injected into the package at build time so source builds can remain portable."
created_at: "2026-08-29T10:45:11.213Z"
updated_at: "2026-08-29T10:45:11.214Z"
links:
  - US-003
  - IN-003
---

# Use public GitHub Releases as the update channel
