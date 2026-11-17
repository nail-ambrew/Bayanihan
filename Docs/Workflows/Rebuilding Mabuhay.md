# Rebuilding Mabuhay

Change the builder, then rebuild — never edit `MabuhayGameplay.unity` by hand.

| Menu (`Bayanihan >`) | When |
|---|---|
| Rebuild Barangay Layout | the GeoJSON or overrides changed |
| Make Placeholder Prefabs | house or landmark models changed |
| Foliage > Import Pack | the foliage pack changed (also adds the low-poly trees) |
| Props > Import Packs | a prop pack in `Assets/3D Models` changed |
| Buildings > Import Building Kit | the building models changed (rerun `make_buildings.py`, copy the GLBs to `Assets/3D Models/Buildings`) |
| Roads > Import RoadKit | the RoadKit changed (`Source Assets/RoadKit.zip` → `Assets/3D Models/RoadKit`) |
| **Build Mabuhay Gameplay Scene** | everything else: map and streets, level, murky water, street furniture, walls, infill buildings, foliage, props, in that order, then saves |
| Props > Place On Open Map | only the props, on the open scene |

From the Unity MCP or the `unity` CLI: `MabuhayGameplayBuilder.BuildAndSave(false)` in an eval does the same as the menu. The
build takes over a minute, longer than the CLI's 5 s eval limit, so queue it with `EditorApplication.delayCall` and have it
write its result to a file. Afterwards run
`MabuhayLevelTests`, `MabuhaySmokeTests` and `MabuhayPropTests` (see [[Running Tests]]).
