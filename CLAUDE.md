# Bayanihan — notes for Claude

A flood-rescue game for the ELEV8 GAMES capstone: Unity 6000.3.9f1, URP, uGUI built in code, Input System. Two maps:
**Barangay Mabuhay** (the default, `Assets/Scenes/Maps/MabuhayGameplay.unity`) and **Manila** (a sample,
`Assets/Scenes/Gameplay.unity`). Start scene: `Assets/Scenes/MainMenu.unity`.

This folder is `Game/` inside `~/Documents/Bayanihan` (see the README there). The project's notes are an Obsidian vault:
read `Docs/Home.md` and open only the notes the task needs.

## Rules

- **Two version controls.** Git (`origin` = github.com/nail-ambrew/Bayanihan, work branch `account-login-and-figma-ui`)
  and Unity Version Control (`Capstone/Bayanihan`). **Never check in to Unity Version Control**; `cm status` is safe.
- **Teammates leave uncommitted work** (UI, settings, TextMesh Pro). Commit only your own changes, file by file; for a
  file with someone else's edits, stage only your hunks. Never commit or push unless asked.
- **Scenes are built, not hand-edited.** Mabuhay comes from its builders (`Bayanihan > Build Mabuhay Gameplay Scene`);
  change the builder and rebuild. Don't edit the 20 MB scene files as text.
- **No passwords or sign-in** on Unity Authentication or any account.
- Game code is the `Bayanihan` assembly (`Assets/Scripts`), editor code `Bayanihan.Editor` (`Assets/Scripts/Editor`),
  tests in `Assets/Tests/EditMode` and `Assets/Tests/PlayMode`.

## Facts that are easy to get wrong

- The ground is at y = -1 and the flood at y = 0 (it rises up to +0.6). Anything under 1 m on the ground is hidden.
- Layer 8 is **Debris**: floating debris and the jam. The chase camera ignores it.
- Mission streets: Bagumbayan (`w668009058`) and A. Mapalad (`w145889435`). Nothing is placed on them.
- Every solid part of the map must be the shape drawn there (mesh colliders per drawn part); a test checks it.
- The swimmer's head is always on; first person keeps the eye outside the head (see `Docs/Systems/Swim and Boat Cameras.md`).

## Working with the Editor

See `Docs/Workflows/Running Tests.md` and `Docs/Workflows/Rebuilding Mabuhay.md`. In short: PlayMode tests can't run while
the Editor is in Play mode; run them with `TestRunnerApi` and read
`~/Library/Application Support/DefaultCompany/Bayanihan/TestResults.xml`. The Unity MCP connection drops on every
recompile; wait for port 7800.
