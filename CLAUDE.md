# Bayanihan — notes for Claude

**Bayanihan: Disaster Rescue Game**, the ELEV8 GAMES capstone. The player drives a rescue boat through a flooded Filipino
barangay, finds and triages residents, treats and tows them, and registers them at the evacuation hall before time runs
out. Unity 6000.3.9f1, URP, uGUI built in code, Input System, Cinemachine 3 and Timeline.

This folder is `Game/` inside `~/Documents/Bayanihan` (see the README there). The notes are an Obsidian vault: read
`Docs/Home.md` and open only the notes the task needs.

## What the game is aiming for

- **Real place, real people.** Barangay Mabuhay, Carmona, Cavite is built from OpenStreetMap with its real landmarks;
  the residents and crew are Filipino characters. Keep Filipino terms and context where the game already uses them.
- **Bayanihan.** Triage, families, the community response. Rescue decisions should matter more than reflexes.
- **Phone first, landscape**, with touch controls; keyboard and mouse work too (a gamepad only skips cutscenes).
- **Accessible.** Settings for Reduce Motion and Subtitles; lightning never flashes more than 3 times a second; SKIP on
  every cutscene. Honour these in anything new.
- **Assessed with ISO 25010** (`Docs/Evaluation/`): functional fit, performance, usability and reliability count, so
  test what you build and keep the player from ever getting stuck.

## The game today

- **Flow:** `Account` → `MainMenu` (start scene, looping storm behind the menu) → `Loadout` (pick the map and 4 of 6
  items on a 3D packing stage; LAUNCH plays READY) → the mission. On Mabuhay with *Play Intro* on, a 24 s flood intro
  plays over the held mission first.
- **Maps:** **Barangay Mabuhay** (the default, `Assets/Scenes/Maps/MabuhayGameplay.unity`) and **Manila** (a sample,
  `Assets/Scenes/Gameplay.unity`). Ten residents, hazards (live wire, debris jam, rising water), a scored report.
- **The rescuer starts at the helm** with the boat's chase camera (`PlayerRescuer.startAboard`); E / RESCUE steps off
  into the water, where the swimmer's own first- or third-person view takes over.
- **Code:** game code is the `Bayanihan` assembly (`Assets/Scripts`), editor code `Bayanihan.Editor`
  (`Assets/Scripts/Editor`), tests in `Assets/Tests/EditMode` and `Assets/Tests/PlayMode`. Cinematics are in
  `Assets/Scripts/Cutscenes` and `Assets/Cutscenes` (guide: `Assets/Cutscenes/README.md`).

## Rules

- **Two version controls.** Git (`origin` = github.com/nail-ambrew/Bayanihan): work on `account-login-and-figma-ui`,
  finished work reaches `main` by pull request with a merge commit. Unity Version Control (`Capstone/Bayanihan`):
  **never check in**; `cm status` is safe.
- **Commit, push, open or merge a PR only when asked.** Commit file by file; never sweep in unrelated changes.
- **Teammates leave uncommitted work** (UI, audio, settings, TextMesh Pro). Leave it alone unless the owner says to
  include it; for a file you share with them, stage only your hunks. Say what you left out.
- **Scenes and cutscenes are built, not hand-edited.** Change the builder and rerun it from the `Bayanihan` menu:
  *Build Mabuhay Gameplay Scene*; *Cutscenes > Build Audio / Build Intro / Build Menu Cinematic*. Don't edit the scene
  files as text; if one shows as modified and you didn't rebuild it, find out why before committing.
- **Add, don't break.** New features sit beside gameplay, saves and sign-in; existing tests must keep passing. When a
  test encodes old behaviour you were asked to change, update the test and say so.
- **Every placeholder is listed.** Generated stand-in art or sound goes in the relevant README or REPORT with how to
  replace it (scripts for them live in `../Source Assets`).
- **No passwords or sign-in** on Unity Authentication or any other account.
- **Docs follow the code.** When behaviour changes, update the note in `Docs/` (and `Docs/Home.md` for a new one).
- **Code comments** are plain sentences explaining why, as in the existing scripts; public members get `<summary>` docs.

## Facts that are easy to get wrong

- The flood is at y = 0 (it rises up to +0.6). The ground *level* is y = -1, but on Mabuhay the terrain rises and falls
  from it (`MapHeightField.Sample`, made by `TerrainBuilder`). It is held at -1 along the mission route, deeper in the
  east and the creek valley, and dry in the west and south. Anything under 1 m on flooded ground is hidden.
  `FloodLevel.Offset` moves the flood for cutscenes (0 = mission level).
- Mabuhay is drawn at real size (`MabuhayGameplayBuilder.MapScale` = 1): houses, streets and the crew are in proportion.
- Layer 8 is **Debris** (floating debris and the jam; the chase camera ignores it). Layer 30 is the loadout's packing
  stage.
- Mission streets: Bagumbayan (`w668009058`) and A. Mapalad (`w145889435`). Nothing is placed on them.
- Every solid part of the map must be the shape drawn there (mesh colliders per drawn part); a test checks it.
- The swimmer's head is always on; first person keeps the eye outside the head (`Docs/Systems/Swim and Boat Cameras.md`).
- There are two gameplay cameras: the boat's chase camera and the swimmer's, which is live (priority 20) only in the
  water. Anything that hands over to gameplay must land on whichever is live.
- `CutsceneHold.Active` freezes the mission clock, residents' condition, the flood's climb, the controls and the HUD.
- The menu cinematic must not become the active scene: `SceneFlow.Reload` reloads the active scene.
- The finished Mabuhay map is sorted under `BarangayMock` into Terrain, Roads, Residential, Commercial, Landmarks,
  Foliage and Props (`MapFinisher`, last step of the build). Find groups with `MapGroups` paths, not old names.
- Performance: Mabuhay's rows are about 6,600 houses. They share one material and cull below 1.5 % of screen height.
  From the boat's start, meshes in view are about 1.1M triangles (plus instanced grass and plants, and the shadows). That
  is fine on PC and far too much for a low-end phone. New work should not add to it; see `Docs/Systems/Map and Props.md`.

## Working with the Editor

Drive the open Editor with the `unity` CLI (`unity cmd <command>`): `eval`, `eval_file`, `menu --path`, `recompile`,
`editor_play` / `editor_stop`, `capture_game_view`, `get_performance_stats`, `console`. In short (details in
`Docs/Workflows/Running Tests.md`):
- PlayMode tests run only asynchronously: `run_tests --mode playmode --filter X --async_tests true`, then poll
  `test_status`. Wait until it reports *running* before polling, or you read the previous run's results; if it sticks
  on *running* with the Editor stopped, `cancel_tests`.
- `eval` has a 5 s limit and `EditorApplication.delayCall` may never fire while the Editor is in the background: run
  long jobs as menu items.
- In the background the Game view doesn't redraw: capture with `capture_game_view` (camera only, no UI) or read a
  RenderTexture, not `ScreenCapture`. Frame timings are unreliable then too.
- The connection drops on every recompile and Play-mode change; wait for port 7800.
- PlayMode tests that pause time or leave a mission loaded affect later tests: reset `Time.timeScale` and start from a
  clean scene.
