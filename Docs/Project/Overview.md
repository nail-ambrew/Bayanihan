# Overview

**Bayanihan: Disaster Rescue Game** — a flood-rescue game by ELEV8 GAMES (capstone). The player takes a rescue boat
through a flooded barangay, finds residents with the radio, treats and tows them, and brings them to the evacuation hall
before time runs out. Landscape, phone first (touch controls), keyboard and mouse supported.

## Scenes (in Build Settings, in this order)
| Scene | What it is |
|---|---|
| `Account.unity` | Sign in, register, quick login (Unity Authentication). |
| `MainMenu.unity` | The start scene: play, how to play, settings, credits, over a looping storm. |
| `Loadout.unity` | Choose the map and the items to bring, packed on a 3D stage; LAUNCH plays READY. |
| `Maps/MabuhayGameplay.unity` | **Barangay Mabuhay, Carmona** — the default map. Generated; see [[Rebuilding Mabuhay]]. |
| `Gameplay.unity` | **Manila** — the first map, kept as a sample. |
| `Cutscenes/Intro/Scene_IntroCutscene.unity` | The flood intro, loaded over a Mabuhay mission. See [[Cinematics]]. |
| `Cutscenes/MainMenu/Scene_MenuCinematic.unity` | The storm behind the main menu, loaded over `MainMenu`. |

Not in the build: `Maps/MabuhayMock.unity` (the bare generated map) and `Maps/MabuhayLayout.unity` (the layout preview).

## The mission
- Ten residents in trouble (in the water, on posts, on roofs, behind a debris jam), the same roster on both maps.
- Hazards: a live wire across the street (radio for a power cut), floating wreckage, a debris jam, rising water.
- The rescuer starts at the helm and can step off to swim: see [[Swim and Boat Cameras]]. The report at the end scores
  who was saved and how.
- With *Play Intro* on, a 24 s flood intro plays first on Mabuhay (SKIP at any time): see [[Cinematics]].

## Code layout (`Assets/Scripts`)
| Folder | What |
|---|---|
| root | The boat (`SimpleBoat`, `Buoyancy`, `BoatTools`), the swimmer (`PlayerRescuer`), water, current, residents, input. |
| `Rescue/` | Rescue actions, debris (`FloatingDebris`, `DebrisBarrier`, `DebrisField`), hazards. |
| `Map/` | Layout data and the planners for houses, foliage and props. |
| `Characters/` | The character library, rigging and animation. |
| `UI/` | Menus, HUD, settings, touch controls — all built in code (`UiKit`, `UiTheme`). |
| `Account/` | Login and registration. |
| `Cutscenes/` | The menu loop, the loadout packing stage, the flood intro, SKIP, the custom Timeline tracks. |
| `Editor/` | The builders and importers (`Bayanihan >` menu), including `Editor/Cutscenes`. |

## Assets that matter
- `Assets/3D Models/` — imported models: houses (Residential, Commercial), landmarks, foliage, prop packs, characters.
- `Assets/Prefabs/` — generated prefabs (`Foliage/`, `Props/`, `Placeholders/`).
- `Assets/Settings/Maps/` — map settings and the libraries (`PlaceholderLibrary`, `FoliageLibrary`, `PropLibrary`).
- `Assets/Generated/` — meshes and materials made by the importers.
- `Assets/Cutscenes/` — the cinematics' scenes, Timelines, placeholder sounds and models, the flood shader; its
  `README.md` and `REPORT.md` say how to change them and what to replace.
- `Assets/Audio/` — music, interface sounds, the boat engine and the `Bayanihan.mixer`.

## Running on phones (measured 19 Nov 2026)

The GRAPHICS setting (Low, Medium, High) now does its work at run time in the missions (`GameplayQuality`, applied by
`GameSettings.Apply`). It has to: a phone build has only the *Mobile* Unity quality level and a PC build only *PC*, so
switching quality levels never changed anything inside a platform. High changes almost nothing, so PC keeps the picture the
map was built for (its LOD bias is 1, not the PC level's 2: see below). Phones start on Medium (`GameSettings.DefaultQuality`); a low-end phone should be set to Low.

| | Low | Medium | High |
|---|---|---|---|
| Camera far clip | 140 m | 280 m | the camera's own (1,000 m) |
| Least fog density (exp²) | 0.0125 (95 % fog at 140 m) | 0.0062 | the weather's own |
| Shadows | off | on | on |
| LOD bias (houses drop out nearer) | 0.3 | 0.7 | 1 (the PC level's own is 2) |
| Grass and plant draw distance | × 0.25 | × 0.6 | × 1 |
| Grass and plants drawn | 20 % | 70 % | all |
| MSAA | off | on | on |

The far clip, shadows and MSAA are set on the mission's main camera just before it renders; the fog floor is applied
by `TyphoonWeather`; the LOD bias is put back when the game quits (in the Editor it would otherwise outlive Play mode).
Grass and plant tiles are shuffled once at load, so thinning draws an even share; a kind with one or two plants in a
tile drops out, which is what saves the draw calls. Retune a preset in `Assets/Scripts/GameplayQuality.cs`.

The build also cuts the whole-map meshes (street surfaces and paint, lot tints, streams, plinths, long landmark
blocks) into 64 m tiles (`MeshTiler`, run by `MapFinisher`; 14 meshes into 542 tiles, `Tiled_Meshes.asset`). A mesh is
culled whole, so the 800 m road net used to be drawn in full whenever any of it was in view. The picture is unchanged.

**Numbers.** From the boat's start as it was on 19 Nov 2026, 46 m along Bagumbayan Street (the crew now starts at the hall: about 12 % more
triangles and 11 % more batches, see `Map and Props`; the table below is not re-measured) (camera frozen at its first chase position, 1600×720, Editor on the PC render
pipeline asset, *Rendering statistics*):

| | Triangles | Draw calls | Batches | SetPass |
|---|---|---|---|---|
| Before (every preset looked like High) | 7.16M | 10,022 | 4,126 | 211 |
| Low | 442k | 1,458 | 394 | 134 |
| Medium | 1.83M | 4,936 | 2,296 | 205 |
| High (tiling only) | 5.80M | 10,512 | 4,126 | 213 |

How to measure again: enter Play mode on `MabuhayGameplay`, set `Time.timeScale = 0`, set the camera's aspect to
1600/720 and repaint the Game view from code (`EditorWindow.GetWindow(GameView).Repaint()`), then read
`unity cmd get_performance_stats`. Without the repaint the Editor in the background reports a stale frame. The numbers
above come from that method; they include the shadow and depth passes.

Where Low's frame still goes (switching one group off at a time): trees, grass and plants about 500 draw calls, the boat,
the residents and the hazards about 350 together (they are near the camera and stay), the landmarks and their compound
walls about 200, the row houses about 150; triangles are spread over the terrain, roads, houses and plants (none above ~100k).
Halving the far clip again saves little (100 m: 377k triangles, 1,355 draw calls), so the far clip is not the limit.

Still to do:
- **No phone build yet.** The Android Build Support module is not installed in Unity 6000.3.9f1 (the Android SDK from
  Android Studio is on the Mac, but Unity cannot target it without the module), nor is iOS. Install Android Build
  Support from Unity Hub, make a development build, and check frame times on a real low-end phone (Low should be the
  first thing to try). Editor numbers are counts, not frame times.
- Low is above the 100–300 draw calls a very weak phone wants. The next levers are content work: merge the compound
  walls per block, give the boat and the residents fewer materials, and fewer plant kinds per tile.
- Size: the project is about 4.2 GB on disk (`Assets` 322 MB, `Library` 3.7 GB, not in Git). The last Mac build
  (15 Nov) was 247 MB; a phone build should be roughly 150–250 MB.
