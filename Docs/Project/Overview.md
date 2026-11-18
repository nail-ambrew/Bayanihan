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
