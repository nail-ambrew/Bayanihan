# Rebuilding Mabuhay

Change the builder, then rebuild — never edit `MabuhayGameplay.unity` by hand.

| Menu (`Bayanihan >`) | When |
|---|---|
| Rebuild Barangay Layout | the GeoJSON or overrides changed |
| Make Placeholder Prefabs | house or landmark models changed |
| Foliage > Import Pack | the foliage pack changed (also adds the low-poly trees) |
| Props > Import Packs | a prop pack in `Assets/3D Models` changed |
| **Build Mabuhay Gameplay Scene** | everything else: map, level, foliage, props, in that order, then saves |
| Props > Place On Open Map | only the props, on the open scene |

From the Unity MCP: `MabuhayGameplayBuilder.BuildAndSave(false)` in an eval does the same as the menu. Afterwards run
`MabuhayLevelTests`, `MabuhaySmokeTests` and `MabuhayPropTests` (see [[Running Tests]]).
