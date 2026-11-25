# Bayanihan HQ

A personal developer tool, **not part of the game**: a low-poly Barangay Mabuhay DRRM operations centre that shows,
live, what each Claude Code agent is doing while it works on Bayanihan. It runs only in the Unity Editor. The assembly
compiles only with `UNITY_EDITOR`, and the scene is not in Build Settings, so none of it can ship.

## Run it
1. Open the project in Unity 6000.3.9f1.
2. Choose **Bayanihan HQ ▸ Open Office** (it builds the scene the first time), then press **Play**.
3. Start Claude Code in **`~/Documents/Bayanihan`**, the folder above `Game/`. Hooks are read only from the folder
   `claude` starts in, so a session started inside `Game/` won't show up.
4. Each tool call reaches the right desk in about 0.1–0.2 s.

**Demo with no session:** choose **Bayanihan HQ ▸ Play Demo (Simulate)**, or press **Simulate** in the top bar. It plays
a scripted 2-minute work session that loops:
- the level builder rebuilds the map;
- QA's test fails, a scripter fixes it, and the rerun passes;
- someone waits for approval;
- idle responders wander to the kubo.

Turning Simulate off brings back the real log. Simulated events are never written to the log.

## What you see
| Status | Responder | Elsewhere |
|---|---|---|
| Working | types; bubble "Edit → SimpleBoat.cs" | monitor glows |
| Thinking | looks at the monitor | |
| **Waiting for you** | right hand up, yellow bubble with the command, gold diamond overhead | yellow lamp, ping, on-screen badge (pinned to the screen edge when off-screen), top banner, signal 4–5 |
| Error | slumps; red bubble | red lamp |
| Done | waves with confetti, then idle | whiteboard item ticked |
| Idle ≥ 60 s | walks to the kubo / sari-sari corner | |
| Subagent started | walks in from the door to the desk | Kap. Dado's bubble "Assign → Name" |

- **Whiteboard (*Gawain ngayon*):** each hand-off to a subagent, plus TaskCreate items when Claude uses them.
- **PAGASA signal board:**
  - 1: all quiet
  - 2: work under way
  - 3: busy, or a failure in the last 2 minutes
  - 4: someone waiting for you
  - 5: an error, 3+ failures in 2 minutes, or a wait over 2 minutes
- **Map table:** a plan of Barangay Mabuhay drawn from `Resources/Layouts/MabuhayCarmona.asset`.

**Controls:**
- Camera: right-drag or Q/E orbits, the scroll wheel zooms, middle-drag or WASD pans, F frames the room.
- Click a responder or their card to focus on them and open their details (task, tool, file, files touched, last 20 events).
- The log chips filter the event log by agent.
- Overlay: H hides it, and F1 shows the plain debug view. Simulate / Pause / Reset are in the top bar. While paused, live
  events queue up and are applied on resume.

## How it works
```
Claude Code hooks ─► .claude/office/hook.sh ─► hook.mjs ─┬─► .claude/office/events.jsonl  (always; rotated at 5 MB)
  (async, 14 events)                                     └─► POST 127.0.0.1:47821/event   (only if Unity is listening)
Unity: HqReceiver (HttpListener, background thread) ─► queue ─► HqDirector (main thread) ─► OfficeState ─► office + overlay
       HqReplay reads the log tail on Play, so the room is up to date even if Unity opened after the session began.
```
- **Hooks** live in `~/Documents/Bayanihan/.claude/settings.json`, outside git:
  - SessionStart/End, UserPromptSubmit, Pre/PostToolUse, PostToolUseFailure, PermissionRequest, Notification,
    SubagentStart/Stop, Stop, PreCompact, TaskCreated/Completed.
  - All are `async`, so Claude never waits on them.
  - The script always exits 0, prints nothing, and kills itself after 800 ms.
- **Payload:**
  - Sent: time, session, agent, event, tool, project-relative file path, a redacted preview of at most 100 characters,
    and success or failure (plus opaque agent and tool-use ids, and task titles for the whiteboard).
  - Never sent: prompts, file contents, tool output or environment variables.
- **Who did it:**
  - The `agent_type` that hooks report inside subagents decides first.
  - For hand-offs, the Agent tool's `subagent_type` decides.
  - Otherwise the path and command rules in `.claude/office/routing.json` decide. Set `mainSessionUsesPathRules` to
    false to keep the main session's own edits on the lead's desk.
- **Subagents:** nine role subagents are in `~/Documents/Bayanihan/.claude/agents/`. Their `name` must match a `key` in
  `Config/roster.json`.

## Customise
- `Config/roster.json` sets each desk's Filipino name, role, colour and desk slot. It's read on Play.
- `.claude/office/routing.json` holds the file-path and command rules for each desk.
- `Runtime/Events/HqSimulator.cs` is the demo story.
- After changing `Editor/OfficeSceneBuilder.cs`, run **Bayanihan HQ ▸ Build Office Scene**. Like the game's scenes, the
  office is built from code, not edited by hand.

## Keeping the game untouched
- Everything is in `Assets/_BayanihanHQ/` (its own assemblies: `BayanihanHQ`, `BayanihanHQ.Editor`, `BayanihanHQ.Tests`).
- It *reads* game code and assets (CharacterLibrary, CharacterPoses, BarangayLayout, UiTheme, a few props and the Kenney
  `question_002` sound) and never changes them.
- Text is drawn with in-memory copies of the game fonts, and the scene builder makes no text in the Editor. Dynamic TMP
  fonts save every new character into their asset, so this keeps the game's and TMP's font assets unchanged.
- The builder saves only its own generated assets.
- Unity itself re-saves `ProjectSettings/EditorBuildSettings.asset`, with identical content, whenever any scene is saved,
  so Unity Version Control may list it as changed. Git sees no change.

## Placeholders
- All furniture is built from primitives (desks, chairs, monitors, boards, kubo, sari-sari counter). The vests and hard
  hats are primitives on the game's characters. No new art or sound was added.

## Tests
`unity cmd run_tests --mode editor --filter BayanihanHQ` runs the EditMode tests for event parsing, statuses,
out-of-order events, permission waits, errors vs interrupts, hand-offs, tasks, log replay and the Simulate story.
