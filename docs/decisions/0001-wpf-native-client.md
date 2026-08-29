---
id: "0001"
type: decision
title: Use WPF for the native Windows client
status: accepted
doc: docs/decisions/0001-wpf-native-client.md
verify: null
notes: "Use a net8.0-windows WPF desktop application. Keep domain/application services UI-independent; use local JSON persistence and explicit adapters for Firebase, speech-to-text, AI analysis, audio capture, and global hotkeys. This provides a buildable offline-first product slice while preserving integration seams."
created_at: "2026-08-29T05:21:18.644Z"
updated_at: "2026-08-29T05:21:18.645Z"
---

# Use WPF for the native Windows client
