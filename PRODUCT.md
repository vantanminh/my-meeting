# Windows Meeting Recorder & AI Meeting Assistant

## 1. Purpose

The experience being introduced is a native Windows app that passively records online meetings (mic + system audio), separates speakers, transcribes the conversation, and produces structured AI summaries and action items.  

It solves the problem of fragmented note‑taking and unreliable human memory in multi‑participant online meetings, especially when calls happen across heterogeneous platforms like Zoom, Google Meet, Teams, Discord, and Messenger.  

The experience belongs in the product because the app’s core value is “Record → Understand Speakers → Transcribe → Analyze → Summarize”; speaker‑aware transcripts and summaries are the natural extension of the recording capability and are consistent with existing AI meeting assistant products such as Otter.ai and similar tools. 

***

## 2. Existing Product Context

### Entry points

From the product concept and architecture you defined, the core entry points are:

- Launching the desktop app from the Windows Start menu or taskbar.  
- Logging in via Firebase authentication to access synced data.
- Starting a new recording session from a primary “Record” control.  
- Potential future entry: global hotkey (e.g., `Ctrl+Shift+R`) to start/stop recording without focusing the window, similar to existing Windows meeting recorder apps.

### Current user journey (conceptual)

Based on the current vision, the app already assumes this high‑level journey:

1. User opens the app and logs in (via Firebase) to access their profile and previous meetings.
2. User selects microphone and system audio sources before or during an online meeting.  
3. User starts recording; the app captures both audio streams locally.  
4. Audio engine performs diarization / voice embeddings to differentiate speakers.  
5. Audio is sent to a Speech‑to‑Text API; transcript is generated with timestamps and speaker labels.  
6. Transcript is sent to an AI API that extracts summary, decisions, action items, deadlines, and important moments.
7. Data is saved and synced to the cloud via Firebase.

### Terminology

Key concepts that should remain consistent across the UX:

- Meeting  
- Recording  
- Transcript  
- Speakers  
- Summary  
- Action items  
- Important moments
- Account / Workspace (Firebase synced)

### Existing interaction conventions (concept-level)

From the product description and real‑world references:

- A “Record” primary CTA to start capture, with “Pause” and “Stop” as secondary controls. 
- A persistent indicator that recording is active (taskbar icon + timer, status chip, or badge). 
- Transcript presented as a chronological list grouped by speaker, with timestamps.  
- Summary presented as structured sections: Summary, Key points, Decisions, Action items, Deadlines, Questions.  

### Relevant screens (inferred from product intent)

Although the actual repo/UI is not yet inspected, the intended product implies at least:

- Login / Authentication Screen (Firebase Auth).
- Main window / Meetings list.  
- New recording setup.  
- Active recording view (live levels, active speaker indicator).  
- Post‑processing state (transcription + analysis running).  
- Meeting detail (Transcript + Summary + metadata).  
- Speaker management (rename speakers, manage voice profiles).  
- Settings (account info, audio devices, hotkeys, data retention, API configuration).  

### Important constraints

- **Platform & Tech Stack**: Native Windows desktop app (Win10/11) built using **C#**. The choice of C# is to guarantee a highly optimized native feel, deep integration with Windows APIs, and ease of UI development.
- **Backend & Storage**: **Firebase** is used for user authentication and cloud data storage, enabling users to log in, save their meeting history safely, and potentially sync across devices.
- **Performance & Stability**: High priority on performance (low CPU/RAM footprint during background recording), crash resistance, and a buttery-smooth UX.
- **Audio sources**: At least 2 streams (microphone + system audio).  
- **Privacy**: No bot joining the call; app records local audio only. 

***

## 3. Target User Experience

The target experience is that the user can:

- Securely log in and access all their past meeting insights via Firebase cloud sync.
- Start recording any online meeting with minimal friction and confidence that both their voice and the other participants are captured without performance lag.
- See clear, real‑time feedback that recording is active, which speaker is currently talking, and how long the meeting has been running.  
- After the meeting, quickly understand what happened via a clean transcript and an AI‑generated summary that highlights key points, decisions, action items, owners, and deadlines.  

From a UX perspective, the experience should feel:

- **Performant & Stable**: The app runs smoothly in the background without hogging system resources, preventing any disruption to the user's actual meeting. C# ensures native stability.
- **Clear**: The user always knows whether recording is on, what audio sources are used, and where the captured meeting will be stored.  
- **Trustworthy**: The app communicates privacy boundaries (no bot in calls, local capture) and status of cloud processing/syncing (transcription, summary, Firebase uploads).  
- **Low effort**: Setup and controls are streamlined; advanced options (speaker profiles, device tuning) are available via progressive disclosure.  
- **Continuous**: The journey from recording to transcript to summary is presented as one coherent flow.  
- **Recoverable**: If something goes wrong (device error, API failure, network drop during Firebase sync) the user has clear recovery paths without losing the entire meeting.  

***

## 4. User Journey

### Primary journey: Record and review a meeting

Entry & Authentication 
→ Device setup  
→ Active recording  
→ Transcription & AI processing  
→ Transcript & summary review  
→ Save / Cloud Sync (Firebase)  
→ Return later  

**Entry & Authentication**

- User launches the app.
- Unauthenticated users are prompted to log in or sign up via Firebase Auth.
- Authenticated users land on the Main screen. 

**Device setup**

- User selects microphone input and system audio source.  
- User can run a quick device test (see levels, confirm sources) before starting.  

**Active recording**

- User presses “Start recording”.  
- Main view shows timer, recording status, and some indication of active speaker / audio level.  
- User can Pause or Stop at any time.  

**Transcription & AI processing**

- After Stop, the app shows a processing screen while sending audio to STT and AI summary APIs.  
- Progress indicators and estimated time are visible.

**Transcript & summary review**

- User lands on a meeting detail view with:  
  - Speaker‑segmented transcript with timestamps.  
  - Summary sections: Summary, Key points, Decisions, Action items, Deadlines, Questions/unresolved.  
- User can rename speakers, correct transcripts, and edit summary fields.  

**Save / Cloud Sync (Firebase)**

- Meeting is securely stored and synced to the user's Firebase database.
- User can search by title, date, participants, or keywords. 

**Return later**

- User opens the app later on any Windows device, logs in, and finds prior meetings synced in the list.  

### Secondary journeys

- Manage speaker profiles: rename, merge, or remove speaker voice profiles; apply known profiles to new meetings.  
- Update device settings and hotkeys.  
- Handle errors: missing permissions, device conflicts, API failures, network disconnects during Firebase sync.  

***

## 5. Flow Specification

### Flow: Authentication (Login/Signup)

**Trigger**

- App launched without active session.

**Steps**

1. App displays Login/Signup screen.
2. User enters credentials (Email/Password) or uses OAuth (Google/Microsoft if implemented).
3. Firebase authenticates the user.
4. App fetches existing data (meetings, settings, speaker profiles).
5. User is redirected to Main Meetings List.

**Failure / recovery**

- Offline mode: If no internet, allow access to locally cached meetings and warn that sync is paused.
- Invalid credentials: Show clear error message.

### Flow: Start a new recording
*(Same as previously defined, but ensures local cache initiates for recording before cloud sync)*

### Flow: Active recording
*(Same as previously defined, focusing on C# optimized background threads to prevent UI freezing)*

### Flow: Post‑meeting processing (transcription + summary)
*(Same as previously defined)*

### Flow: Meeting review (transcript & summary)
*(Same as previously defined)*

### Flow: Speaker profile management
*(Same as previously defined, noting that speaker profiles are now tied to the user's Firebase account for cross-device consistency)*

***

## 6. Screen & State Inventory

### Screen: Authentication / Login
**Purpose**

Secure access to the user's meeting data via Firebase.

**Required information**

- App branding/logo.
- Email and password input fields.
- Login and Sign Up buttons.
- "Forgot password" link.
- Optional: Single Sign-On (Google, Microsoft) buttons.

**States**

- Default form.
- Loading/Authenticating spinner.
- Error state (wrong password, network issue).

---

### Screen: Main / Meetings List
**Purpose**

Provide a hub where users can see past meetings (fetched from Firebase) and start new recordings.  

**States**

- Default: meetings list loaded from cloud/local cache.
- Empty: onboarding copy.
- Syncing: Small indicator showing data is being pulled/pushed to Firebase.

---
*(Other screens: Recording Setup, Active Recording, Processing, Meeting Detail, Speaker Management Panel, Settings remain largely the same, optimized for C# native UI rendering)*

***

## 7. Interaction Requirements

- **Global hotkey and background recording**: User can start/stop recording via keyboard without focusing the app, leveraging C# Windows APIs for global hooks.
- **Progressive disclosure**: Advanced options are hidden under expandable sections.
- **Non‑intrusive status feedback**: Active recording state uses subtle signals (tray icon).
- **Speaker interaction**: Clicking a speaker label filters transcript.
- **Data Synchronization**: Background sync via Firebase. If the user edits a transcript, changes are saved locally and synced to the cloud seamlessly without freezing the UI.
- **Failure handling**: Graceful fallbacks for STT API failures, AI failures, or Firebase connection drops.

***

## 8. UX Patterns & Research Findings

*(Same as previously defined)*

***

## 9. Visual / Interaction Reference Board

*(Same as previously defined)*

***

## 10. Product Decisions

Intentional UX and Technical decisions based on context and evidence:

- **App Platform**: Built on Windows using **C#**. This guarantees native stability, excellent background performance, and access to robust Windows audio APIs.
- **Cloud Backend**: Integrated with **Firebase** for Authentication and Firestore/Realtime DB for saving and syncing meeting data.
- Recording is triggered from a primary “Record” action and may be accessible via global hotkey.
- The app captures mic + system audio locally, with no bot joining calls.  
- Speaker diarization is a core requirement; transcripts are grouped and labeled by speaker.  
- Advanced options (local/cloud, quality, retention) are hidden under progressive disclosure.  
- Calendar integration and automatic meeting joining are explicitly out of scope for the initial UX.  

***

## 11. Assumptions & Open Questions

### Confirmed

- **Platform and language**: Native Windows app built entirely in **C#**, heavily prioritizing performance, stability, and UI/UX responsiveness.
- **Backend**: **Firebase** will handle user authentication and cloud data storage.
- **Audio sources**: At minimum microphone and system audio streams.  
- **Meeting structure**: Meeting → Recording → Transcript → Speakers → Summary → Action Items → Important Moments.  

### Evidence‑backed recommendations

- Use passive local audio capture with no bot in calls.
- Provide structured summaries including action items and decisions.
- Integrate system tray and global hotkeys for low‑friction capture.

### Open questions

- **Firebase Data Sync vs Offline**: Should the app strictly require internet (Firebase Auth) to start recording, or can a user record entirely offline as a "Guest" and sync later?
- **C# UI Framework**: Will it be WPF, UWP, WinUI 3, or MAUI? (WinUI 3 / WPF is recommended for best native Windows desktop performance).
- Exact visual style: degree of “native Windows” (Fluent Design) vs custom visual language.  

## 12. Non‑goals

- Redesign or specify the full engineering architecture (audio pipeline, threading, memory management).  
- Define database schemas or API contracts beyond what is needed to reason about UX.  
- Choose specific speech‑to‑text or AI models or evaluate their performance.  
- Implement cross‑platform behavior (macOS, web, mobile); the focus is purely Windows desktop.  
