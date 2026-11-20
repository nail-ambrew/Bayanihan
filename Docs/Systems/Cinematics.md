# Cinematics

The menu loop, the loadout's packing stage and the story intro. Full guide: `Assets/Cutscenes/README.md`; file list
and placeholders: `Assets/Cutscenes/REPORT.md`.

- **Built, not hand-edited**: Bayanihan > Cutscenes > Build Audio / Build Intro / Build Menu Cinematic. Retime in the
  builders' tables (`IntroCutsceneBuilder.Beats`, `MenuCinematicBuilder.Shots`) and rebuild.
- **The intro is a story, about 55 s.** The Dela Cruz family (Ben, his daughter Joy, his mother Lola Nena: the same three
  the mission asks you to rescue) packs to leave as the storm comes in; the water takes the street, a neighbour's shanty,
  the family photo and Joy; the crew at the Barangay Hall sets out. Five beats: *Before the storm*, *The storm hits*,
  *Tragedy*, *Hope*, *Handover*. Nothing is shown of anyone being hurt; the loss is implied. Dialogue is Tagalog with
  English under it (`SubtitleClip` has `speaker`, `line`, `translation`).
- **The cast** is a set of `CharacterSpot` groups under `IntroCutscene.castRoot` (HomeAll, RoofNena, RoofBen, RoofJoy,
  WaterJoy, PostBen, Hall, Beacon), shown and hidden by `Cast_Show_<group>` / `Cast_Hide_<group>` cues, so people can be
  in a new place after each cut. The mission's own residents are hidden for the whole film and appear at the hand-over
  where the cast ended. New poses (`PoseOverlay.Clutch`, `Hug`, `ShoutCall`) are built on the existing IK; there are no
  new clips. Props that move on a cue (photo, shanty, tricycle) are `IntroPropMotion`.
- **The intro plays over the real mission.** LAUNCH arms `IntroFlow`; `CutsceneHold` freezes the clock, condition,
  flood climb, controls and HUD; `IntroCutscene` binds the Timeline to the map at runtime and lands on the gameplay
  camera (whichever of the boat's or the swimmer's camera is live).
- **SKIP** on every real cutscene (the loadout's 2 s READY is not one, so it has none) (button, Esc, Space, Enter, gamepad Start/B): 0.3 s fade, then hand-over. Watchdogs: 3 s stall,
  length + 5 s, intro start 8 s, hold 60 s (the film is 55 s, so there is little margin: keep it under about 57 s or raise
  `CutsceneHold.MaxSeconds`).
- **Settings**: Play Intro (on), Reduce Motion (off), Subtitles (on). Lightning ≤ 3 flashes/s (`FlashLimiter`).
- **Flood level**: `FloodLevel.Offset` (0 = mission level, -0.92 = just over the ground).
- **Water**: `Water_Mabuhay.mat` uses `Bayanihan/FloodWater` (edge foam needs the depth texture: on for PC, off on Mobile).
- **Easy to get wrong**: the menu cinematic must not become the active scene (`SceneFlow.Reload` reloads the active
  scene). Batched meshes can't be read for triangle counts; use `GetIndexCount`. Tests that pause time leave
  `Time.timeScale` at 0 for later tests unless reset.

Related: [[UI and Settings]], [[Running Tests]].
