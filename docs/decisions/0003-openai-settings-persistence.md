---
id: "0003"
type: decision
title: Keep OpenAI settings responsive by writing HKCU directly
status: accepted
doc: docs/decisions/0003-openai-settings-persistence.md
verify: "dotnet build MeetingAssistant.sln --configuration Release && dotnet run --project tests/MeetingAssistant.Smoke/MeetingAssistant.Smoke.csproj --configuration Release --no-build"
notes: "OpenAiConfiguration stores the existing MEETING_ASSISTANT_OPENAI_* values directly under the current user's HKCU\\\\Environment key and updates only the current process. It deliberately avoids EnvironmentVariableTarget.User because the framework synchronously broadcasts WM_SETTINGCHANGE and can block the WPF dispatcher when another process is hung. SaveSettingsAsync awaits a bounded background write; TestOpenAiAsync sends the pending key without persisting it first. The app also reads HKCU directly on startup so Explorer's inherited environment does not need to be refreshed."
created_at: "2026-08-29T11:48:46.484Z"
updated_at: "2026-08-29T11:48:46.484Z"
links:
  - US-004
  - IN-004
  - "0001"
---

# Keep OpenAI settings responsive by writing HKCU directly
