# Cinematics

The menu loop, the loadout's packing stage and the flood intro. Full guide: `Assets/Cutscenes/README.md`; file list
and placeholders: `Assets/Cutscenes/REPORT.md`.

- **Built, not hand-edited**: Bayanihan > Cutscenes > Build Audio / Build Intro / Build Menu Cinematic. Retime in the
  builders' tables (`IntroCutsceneBuilder.Beats`, `MenuCinematicBuilder.Shots`) and rebuild.
- **The intro plays over the real mission.** LAUNCH arms `IntroFlow`; `CutsceneHold` freezes the clock, condition,
  flood climb, controls and HUD; `IntroCutscene` binds the Timeline to the map at runtime and lands on the gameplay
  camera (whichever of the boat's or the swimmer's camera is live).
- **SKIP** everywhere (button, Esc, Space, Enter, gamepad Start/B): 0.3 s fade, then hand-over. Watchdogs: 3 s stall,
  length + 5 s, intro start 8 s, hold 60 s.
- **Settings**: Play Intro (on), Reduce Motion (off), Subtitles (on). Lightning ≤ 3 flashes/s (`FlashLimiter`).
- **Flood level**: `FloodLevel.Offset` (0 = mission level, -0.92 = just over the ground).
- **Water**: `Water_Mabuhay.mat` uses `Bayanihan/FloodWater` (edge foam needs the depth texture: on for PC, off on Mobile).
- **Easy to get wrong**: the menu cinematic must not become the active scene (`SceneFlow.Reload` reloads the active
  scene). Batched meshes can't be read for triangle counts; use `GetIndexCount`. Tests that pause time leave
  `Time.timeScale` at 0 for later tests unless reset.

Related: [[UI and Settings]], [[Running Tests]].
