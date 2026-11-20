# Cinematics: report

What was added for the menu loop, the loadout packing stage and the story intro, what was changed, and what is a
placeholder to replace. How to use and change it all: `README.md`.

## Created

### Runtime code (`Assets/Scripts/Cutscenes/`)
| File | What it is |
|---|---|
| `CutsceneManager.cs` | Plays a cutscene's Timeline; SKIP with 0.3 s fade; watchdog; loop and Reduce Motion still frame |
| `CutsceneSkipButton.cs` | The SKIP pill, its keys (Esc/Space/Enter, gamepad Start/B) and its `Style` |
| `CutsceneOverlay.cs` | Letterbox 2.39:1, subtitles, the BAYANIHAN title card |
| `CutsceneCueReceiver.cs` | Acts on Signal Emitters by signal name |
| `CutsceneHold.cs` | Holds the mission (clock, condition, flood climb, controls, HUD) during the intro; 60 s failsafe |
| `CutsceneAssets.cs` | Registry of the mixer, placeholder sounds and loadout models (`Resources/Cutscenes`); `CutsceneAudio` |
| `ScreenFade.cs` | Persistent fade to and from black, with the sound |
| `FloodLevel.cs` | The shared flood level offset |
| `FlashLimiter.cs` | At most 3 flashes a second, none with Reduce Motion; `CinematicMotion` change event |
| `CinematicQuality.cs` | Low / Medium / High cinematic presets |
| `IntroFlow.cs` | Arms the intro on LAUNCH, loads it over the held mission, 8 s failsafe |
| `IntroCutscene.cs` | Binds the intro to the map, holds the boat and hides the mission's residents, shows and hides the story's cast groups on `Cast_Show_` / `Cast_Hide_` cues, power-out, hands over to the gameplay camera |
| `IntroPropMotion.cs` | The few props that move on a cue: the family photo (falls, then drifts), the shanty and the tricycle (tilt and sink) |
| `IntroDebrisPool.cs` | Pooled flood debris (16 kinds, capped by preset, 40 on High) |
| `ShotMove.cs` | Timeline-timed camera (or object) moves |
| `SirenLight.cs`, `LightFlicker.cs`, `FloatOnWater.cs`, `CharacterSpot.cs` | Siren lamp, sparking-wire light, surface riding, cutscene characters |
| `MenuCinematic.cs` | The menu loop's runtime (camera takeover, sky, crew); `MenuBackdrop` loader |
| `LogoFx.cs`, `MenuParallax.cs` | Logo pop and shimmer; button drift and vignette |
| `PackingStage.cs` | The loadout's 3D packing stage, meter and READY sequence |
| `Timeline/FloodLevelClip.cs`, `FloodLevelTrack.cs`, `StormClip.cs`, `StormTrack.cs`, `SubtitleClip.cs` (speaker, line, translation), `SubtitleTrack.cs`, `RampBehaviour.cs` | Custom Timeline tracks |

### Editor code (`Assets/Scripts/Editor/Cutscenes/`)
| File | Menu item |
|---|---|
| `CutsceneAudioBuilder.cs` | Bayanihan > Cutscenes > Build Audio (mixer, imports, registry) |
| `IntroCutsceneBuilder.cs` | Bayanihan > Cutscenes > Build Intro |
| `MenuCinematicBuilder.cs` | Bayanihan > Cutscenes > Build Menu Cinematic |
| `CutsceneKit.cs` | Shared: placeholder models, particles, materials, shots, Timeline helpers |

### Content
| Path | What |
|---|---|
| `Assets/Cutscenes/Intro/Scene_IntroCutscene.unity`, `Timelines/Cut_Intro.playable`, `Cut_Intro_Look.asset`, `Signals/*.signal` | The intro (built) |
| `Assets/Cutscenes/MainMenu/Scene_MenuCinematic.unity`, `Timelines/Cut_Menu_Loop.playable` | The menu loop (built) |
| `Assets/Cutscenes/Shared/Shaders/FloodWater.shader` | Stylised muddy flood (gameplay and cutscenes) |
| `Assets/Cutscenes/Shared/Materials/*` | Prop and particle materials, soft particle texture |
| `Assets/Cutscenes/Intro/Prefabs/*` | Dog, storm drain, six rubbish kinds, family photo, shanty, tricycle, rescue gear |
| `Assets/Cutscenes/Loadout/Models/*.glb` | Dry-bag and six items |
| `Assets/Cutscenes/*/Audio/*.wav` | Placeholder sounds (below) |
| `Assets/Audio/Bayanihan.mixer` | Music / SFX / Ambience / VO mixer |
| `Assets/Resources/Cutscenes/CutsceneAssets.asset` | The registry |
| `Source Assets/Audio/make_cutscene_audio.py` (outside the repo) | Generates the placeholder sounds |
| `Source Assets/Loadout/make_loadout_models.py` (outside the repo) | Generates the loadout models (Blender) |

### Tests
`Tests/EditMode/CutsceneAssetTests.cs` (7); `Tests/PlayMode/CutsceneFoundationTests.cs` (21), `IntroCutsceneTests.cs` (12),
`MenuCinematicTests.cs` (5), `PackingStageTests.cs` (4).

## Changed (existing files)

| File | Change |
|---|---|
| `UI/GameSettings.cs` | `PlayIntro`, `ReduceMotion`, `Subtitles` (and in `Reset`) |
| `UI/MenuPages.cs` | Three settings rows: PLAY INTRO, REDUCE MOTION, SUBTITLES |
| `UI/MainMenuScreen.cs` | Loads the menu cinematic (still picture as fallback), logo FX, parallax, vignette |
| `LoadoutScreen.cs` | Packing stage and 2x2 slots on the LOADOUT card; pack/unpack/deny calls; LAUNCH plays READY (no SKIP: it is a 2 s flourish, not a cutscene) then arms the intro |
| `Characters/CharacterPoses.cs` | New poses `Clutch`, `Hug`, `ShoutCall` (IK, no new clips) |
| `Cutscenes/CutsceneAssets.cs`, `CutsceneOverlay.cs` | Intro voice lines and story sounds in the registry; two-line subtitle box |
| `Editor/MabuhayLevelBuilder.cs`, `Editor/MabuhayMissionBuilder.cs` | The rescue crew starts at the Barangay Hall (`HallStartS`, the `RescueBase_Spawn` marker) |
| `RescueManager.cs` | Clock waits while `CutsceneHold` is active |
| `Rescue/SurvivorCondition.cs` | No condition drain while held |
| `Rescue/FloodRise.cs` | Climb waits while held; adds `FloodLevel.Offset` |
| `InputHelper.cs` | `GameplayLocked` while held |
| `UI/GameHud.cs` | Hidden while held, fades in after; Esc doesn't pause while held |
| `UI/MusicPlayer.cs` | `Duck` / `Unduck` for cutscene music |
| `TyphoonWeather.cs` | Lightning through `FlashLimiter`; `StrikeNow`, `SnapIntensity`, `autoLightning`, `rainRateScale`, `CurrentIntensity`, `Flash`; rain-ring global for the flood shader |
| `Map/GrassField.cs`, `Map/InstancedFoliage.cs` | `InstancedFoliage.DistanceScale` (1 in play) |
| `Editor/MabuhayGameplayBuilder.cs` | `MurkyWater()` uses the FloodWater shader |
| `Materials/Water_Mabuhay.mat` | Now the FloodWater shader |
| `Bayanihan.asmdef`, `Bayanihan.Editor.asmdef`, test asmdefs | Timeline / Cinemachine / URP references |
| `ProjectSettings/EditorBuildSettings.asset` | The two cutscene scenes added |

`MabuhayGameplay.unity` was rebuilt for the hall start (everything else on the map is as before); the Manila map, sign-in, saves and mission rules are unchanged.

## Placeholders to replace

| Placeholder | Made by | Replace with |
|---|---|---|
| Rain light/heavy, tin drips, rushing water, wind loops | `make_cutscene_audio.py` (synthesised noise) | Recorded ambience, same names, seamless loops |
| Thunder x2, siren, whoosh, splash | same | Recorded or licensed effects |
| Pack rustle, zip, buckle, denied thump, READY stamp | same | Foley |
| Radio bulletin (VO), now in Tagalog | macOS `say` (the Indonesian voice Damayanti, a stand-in: macOS has no Filipino voice) with a radio filter | A real voice actor; update `CutsceneAssets.radioBulletinText` and the subtitle clips if the words change |
| The story's spoken lines `VO_Intro_*` (Ben, Nena1-3, Joy, BenShout, Neighbour1-2, Captain, Rescuer) | `make_cutscene_audio.py` (`say`, Damayanti, pitched per speaker, rough and fast on purpose) | Recorded Tagalog dialogue, same file names; lines and translations are in `IntroCutsceneBuilder.Lines` |
| Creak, collapse, dog bark, power-out thump | same (synthesised) | Foley |
| Music bed under the tragedy (`Music_Bed_Loop`) | same (synthesised piano and strings in A minor) | A composed or licensed piece (loops, about 22 s) |
| Intro music cue | `To the Ends` (Kevin MacLeod, CC BY 4.0, already credited) from 0 s | A cue scored to the beats, or a chosen start in `musicCueStart` |
| Dry-bag and six item models | `make_loadout_models.py` (Blender, simple shapes) | Final models, same names and part names |
| Dog, storm drain, rubbish (bottle, trash bag, cardboard, plastic chair, bin, coconut frond), hall siren, downed pole and cable, family photo, shanty, tricycle, rescue gear (vests, first-aid box, rope), the rescue beacon | `CutsceneKit` (Unity primitives) | Modelled props, same prefab names |
| Hug, clutch and shout poses | `CharacterPoses` (procedural IK on the four existing clips) | Animation clips (hug, hold, call out) if the characters get a full animation set |
| Mist, foam, spray, sparks, splash particles | `CutsceneKit.Particles` (soft dot) | Textured particles or VFX Graph (the project has no VFX Graph package) |

## Departures from the brief, and why

- **Particle Systems, not VFX Graph**: the project has no VFX Graph package; the brief allows either.
- **Slot meter, not weight**: items have no weight in the game.
- **Settings rows, not a tab**: the settings page already scrolls.
- **Draw budget**: met by the menu loop; the intro plays inside the real map and is over 150k triangles / 200 draw calls
  (see README, Performance). Reaching it needs lighter map art, which is outside this work.
- **Intro scene loads over the mission**, rather than a separate scene before it, so skipping is instant and the
  hand-over is seamless.
