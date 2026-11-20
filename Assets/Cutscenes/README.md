# Cinematics: menu loop, loadout packing, story intro

Three short pieces of cinema around the game, all additive: no gameplay, save or sign-in logic changed.

| Piece | Where | Length | Built by |
|---|---|---|---|
| **Menu loop**: the storm behind the main menu | `MainMenu/Scene_MenuCinematic.unity`, `Cut_Menu_Loop.playable` | 20 s loop | Bayanihan > Cutscenes > **Build Menu Cinematic** |
| **Loadout packing**: the 3D bag on the LOADOUT card, and READY | built at runtime by `PackingStage` (models in `Loadout/Models`) | <0.6 s per item, READY ~2 s | Bayanihan > Cutscenes > **Build Audio** (registers the models) |
| **Story intro**: five beats before a Mabuhay mission, about the Dela Cruz family | `Intro/Scene_IntroCutscene.unity`, `Cut_Intro.playable` | 55 s | Bayanihan > Cutscenes > **Build Intro** |

Like the map, the cutscene scenes and Timelines are **generated**: change the builder (or its table), run its menu item,
and the scene and Timeline are rewritten. Don't hand-edit them; a rebuild overwrites hand edits. The builders only *read*
`MabuhayGameplay.unity` (shot positions, the map slice); they never change it.

## How it plays

- **Main menu**: `MainMenuScreen` loads `Scene_MenuCinematic` on top of the menu (`MenuBackdrop`). It switches the
  menu's own camera off and applies its own sky, fog and light; the menu stays the active scene. If the scene is not in
  the build, or has not started within 5 s, the still flooded-street picture is used instead. The logo pops in once and
  then shimmers (`LogoFx`); the buttons drift with the pointer and a vignette keeps them readable (`MenuParallax`).
- **Loadout**: tapping a card packs or unpacks it on the stage. A fifth item is refused (shake, red outline, thump). The
  meter counts **slots** (4), not weight: the game's items have no weight, so a weight meter would be invented data.
  LAUNCH plays READY (zip, buckle, the bag slung, READY stamp), then loads the mission.
- **Intro**: with *Settings > Play Intro* on and the Mabuhay map chosen, LAUNCH arms `IntroFlow`. The mission loads as
  usual behind the loading screen; the moment it is in, the mission is held (`CutsceneHold`) and `Scene_IntroCutscene`
  loads on top. `IntroCutscene` binds the Timeline to the map at runtime (scene files can't link across scenes), holds
  the boat and residents where the mission starts, and at the end lands exactly on the gameplay camera with the storm,
  rain and flood at mission values. The hold is released, the HUD fades in, the intro scene unloads.
  The Manila map, or Play Intro off: straight to the mission, as before.

**The story.** *Before the storm* (0-12 s): dusk over Barangay Mabuhay, a PAGASA Signal No. 4 bulletin on the radio, Ben, his daughter Joy
and his mother Lola Nena packing to leave; Lola Nena will not go without the family photo. *The storm hits* (12-22 s): the power cuts out,
the creek overflows, a tricycle goes over, neighbours shout to get to the roof, the dog barks. *Tragedy* (22-40 s): the water is at the door,
the family is on the roof, Joy asks if anyone is coming, the photo slips away, the neighbour's shanty gives way and Joy is carried off; Ben,
clinging to a post, shouts for her; Lola Nena, alone, asks God if anyone is still coming. Nothing is shown of anyone being hurt.
*Hope* (40-49 s): the Captain at the Barangay Hall radio calls the teams out; the crew load the boat; Lola Nena sees a light far off.
*Handover* (49-55 s): "Kapit lang po. Parating na kami.", the title card, the boat sets out from the hall into play. The three family
members end the film where the mission finds them (Nena on her roof, Ben on his post, Joy adrift), and the crew's start is the
Barangay Hall (`RescueBase_Spawn`). Dialogue is Tagalog with English under it.

**SKIP** is on every cutscene from its first frame (`CutsceneSkipButton`; the loadout's 2 s READY is a flourish, not a cutscene, and has none): the button, Esc, Space, Enter, or a gamepad's
Start or B. It fades to black in 0.3 s with the sound (`ScreenFade`), then hands over at once. Key presses in the first
0.2 s and 3 frames are ignored, so the Enter that launched the mission can't also skip.

**Never stuck**: `CutsceneManager` ends any cutscene whose Timeline stops moving for 3 s, or runs 5 s past its length.
`IntroFlow` starts the mission without the intro if it hasn't started within 8 s, and `CutsceneHold` lets go after 60 s
whatever happens.

## Settings

| Setting | Default | Effect |
|---|---|---|
| PLAY INTRO | ON | The flood intro before a Mabuhay mission |
| REDUCE MOTION | OFF | The menu loop holds a still frame; no camera shake, logo pop, shimmer, button drift, bag shake or chromatic aberration; **no lightning flashes** (the thunder is still heard) |
| SUBTITLES | ON | Subtitles for the radio bulletin and every spoken line (Tagalog, with the English translation under it) |

The rows sit on the existing settings page, which already scrolls, so there is no separate CINEMATIC tab.
Lightning never flashes more than **3 times a second** (`FlashLimiter`).

## How to...

### Retime shots
- **Intro**: edit the `Beats` table at the top of `Scripts/Editor/Cutscenes/IntroCutsceneBuilder.cs` (start and end of
  each beat), and the shot times in `MakeShots` (each `Add(name, start, end, ...)`). The flood level and storm clips,
  the spoken lines (`Lines`) and the cues (who is where, the photo, the shanty) are in `FillTimeline`. Then Bayanihan >
  Cutscenes > Build Intro. Every shot's camera is kept out of walls by the builder (`Clear`: it raises a camera that is inside a house
  or cannot see its subject); the build log lists any shot it could not fix. Keep the film under about 57 s: `CutsceneHold` lets go
  after 60 s (`MaxSeconds`).
- **Menu**: edit `Shots`, the `Add(...)` calls and `StillFrame` in `MenuCinematicBuilder.cs`, then Build Menu Cinematic.
- For a one-off try-out you can drag clips in the Timeline window (Window > Sequencing > Timeline, select the
  `IntroDirector` or `MenuDirector`), but a rebuild puts the builder's times back.
- Camera moves are `ShotMove` components (from-pose to to-pose over the shot's time), so they follow any retime.
- READY's timing is in `PackingStage.ReadyRoutine`; pack and unpack speeds are `PackSeconds`, `BulgeSeconds` and
  `UnpackSeconds` at the top of `PackingStage`.

### Change the flood level
`FloodLevel.Offset` is metres above the flood's start height: 0 is the mission's level, `FloodLevel.Dry` (-0.92) is just
over the ground (the ground is at y = -1, the flood at 0). In the intro it's the **Flood Level** track: each clip eases
from `from` to `to`. Change the `Flood(...)` lines in `IntroCutsceneBuilder.FillTimeline` and rebuild; keep the last clip
ending at 0 so the hand-over matches the mission. From code, call `FloodLevel.Set(metres)`. `FloodRise` (the
mission's slow climb) adds on top.

### Add a loadout item model
1. Add the item to `RescueItemId` and `RescueItems` as for any item.
2. Make `Item_<Id>.glb` (the name must match the enum). Either add a function to
   `Source Assets/Loadout/make_loadout_models.py` and run it
   (`/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup -P make_loadout_models.py -- <Game>/Assets/Cutscenes/Loadout/Models`),
   or drop in your own GLB. Real size in metres, front facing -Y in Blender (Unity +Z).
3. Run Bayanihan > Cutscenes > Build Audio: it registers every `Item_*.glb` and the bag in `CutsceneAssets`.
To replace the bag, keep the part names `Bag_Body` (it bulges) and `Bag_Flap` (pivot on the hinge; it opens and closes).

### Restyle the SKIP button
Change `CutsceneSkipButton.Style`: size, margin from the corner, fill and hover colours, outline colour and width, the
hover and press scale, and the label. The arrows are drawn shapes, so no font glyph is needed.

### Change the quality presets
`CinematicQuality` holds Low, Medium and High (picked by *Settings > Graphics*): rain particles per second, debris cap,
depth of field, film grain and chromatic aberration, camera far clip, effect particle scale, and how far grass and
undergrowth are drawn during a cinematic. Edit the numbers there; every cutscene reads them when it starts.

### Replace a placeholder sound
Replace the WAV under `Cutscenes/*/Audio` with one of the same name. Loops should loop cleanly. The intro's music cue is
`To the Ends` from `CutsceneAssets.musicCueStart` seconds: change that field to start it elsewhere. Run Build Audio
afterwards if you add or rename files.

## Audio

`Assets/Audio/Bayanihan.mixer` has Master > **Music, SFX, Ambience, VO**, each with its volume exposed (`MusicVolume`,
`SfxVolume`, `AmbienceVolume`, `VoVolume`). Cutscene sources play through it; `CutsceneAudio.ApplyVolumes` sets the
groups from the Settings sliders (Music for music, Sound Effects for the rest; the master volume is the listener's).
The game's existing `MusicPlayer` and `UiSound` keep their own volume handling and are **not** routed through the mixer,
so nothing is scaled twice. The intro lowers the background music (`MusicPlayer.Duck`) and brings it back at the end.

## Timeline cues (Signal Emitters)

Signals in `Intro/Signals` are acted on by `CutsceneCueReceiver` by name: `Cue_Lightning`, `Cue_Thunder`,
`Cue_TitleHit`, `Cue_Letterbox_In/_Out`, `Cue_Splash`, `Cue_Whoosh`, `Cue_Siren_On/_Off`, `Cue_Music_Duck/_Unduck`, and
`Cue_Debris`. `Beat_1`..`Beat_5` mark the intro's beats. `IntroCutscene` acts on `Cast_Show_<group>` / `Cast_Hide_<group>`
(HomeAll, RoofNena, RoofBen, RoofJoy, WaterJoy, PostBen, Hall, Beacon), `Cue_PowerOut`, `Cue_PhotoDrop`, `Cue_ShantyFall`,
`Cue_TricycleTip`, `Cue_Creak`, `Cue_DogBark` and `Cue_JoySplash`. Add a signal with a new name and listen to
`CutsceneCueReceiver.Cue` to hang your own action on it.

## Performance (measured in the Editor on a Mac, High quality)

| | Triangles | Batches | GPU frame |
|---|---|---|---|
| Menu loop | 95k–160k | 9–14 | 9–12 ms |
| Intro (per shot) | 270k–1.4M | 260–1,670 | 10–21 ms |
| Mission (for comparison) | ~3.5M | ~2,800 | 13–16 ms |

The story intro is built from the same map (nothing is added to the map scene; the cast is about eight characters and a few primitive props),
so it costs what the map costs where it is staged. The menu meets the 150k / 200 budget. The intro plays **inside the real map**, so it draws what the map draws near the
camera. That is already under half of the mission's cost, but it is over the spec's 150k-triangle / 200-draw-call
budget, which no shot over the full map can meet. To get there, the map itself needs lighter buildings and LODs, which
is a gameplay change outside this work. During the intro, grass and undergrowth draw at 30–50 % of their usual distance
(they are most of the map's draw calls) and come back to full over the last 2 s. Please confirm frame rates on your
target mid-range PC.

## Tests

- EditMode `CutsceneAssetTests`: Timelines, beats, bindings, build settings, every placeholder in its slot.
- PlayMode:
  - `CutsceneFoundationTests`: SKIP and its keys, fade, watchdog, mission hold, HUD fade, subtitles, letterbox,
    signals, reduce motion, flash limit.
  - `IntroCutsceneTests`: held start, 5 beats, skip hand-over at the hall, skip at 1 s / 30 s / 45 s, play-through with no camera jump,
    the cast standing on ground, roofs or water, SKIP only with the film, debris cap.
  - `MenuCinematicTests`: replaces the still picture, loops, reduce motion, logo, triangle budget.
  - `PackingStageTests`: pack and unpack under 0.6 s, denied, READY ~2 s (no SKIP over it).

See `REPORT.md` for every file created or changed and the placeholder list.
