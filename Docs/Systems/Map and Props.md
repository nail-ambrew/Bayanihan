# Map and Props

Barangay Mabuhay is generated from real map data; nothing on it is placed by hand.

1. **Layout** — `Docs/Maps/mabuhay-carmona.geojson` (OpenStreetMap) + `mabuhay-overrides.json` → `BarangayLayout`
   asset (`Bayanihan > Rebuild Barangay Layout`). Metres east/north of the barangay hall. See [[Maps/README|Maps]].
2. **Houses and landmarks** — `BarangayMockPlanner` gives every building one fate (a house prefab facing the street, a
   block, a shed, part of a landmark); `BarangayMockBuilder` makes it under `BarangayMock`. Landmarks are fitted to their
   lots and stand square to their roads (the road named in `BarangayMockPlanner.FrontRoads`, else the nearest), with
   the INC's forecourt to Purificacion Street. Every drawn part gets its own mesh collider (`SolidWhereDrawn`), slippery, so there are no invisible walls.
3. **Level** — `MabuhayLevelBuilder`: the hall and drop-off, the boat's start, residents, posts, live wire, the jam,
   wreckage fields, and the **levee** round the edge (crest at y 2.5, dry land beyond).
4. **Streets** — `RoadNetwork` (junctions at shared OSM nodes, each street trimmed short of them, one paved patch per
   junction) and `RoadBuilder`: each class's RoadKit cross-section (carriageway, sidewalks or gutters, centre and edge
   lines) swept along its real centre line, inside the road's library width. Then, world-sized under
   `BarangayMock/Street Furniture`: the kit's bridges over the OSM bridge ways (footbridges for footpaths), utility poles
   with wires, lamps on the main roads, stop signs, a traffic light, street-name signs and zebra crossings — never on the
   two mission streets. RoadKit: `Bayanihan > Roads > Import RoadKit` (`Assets/3D Models/RoadKit`, `RoadKitLibrary`).
5. **Walls** — `CompoundWallBuilder`: hollow-block walls round schools, churches, the hospital, the water works and
   other institutions; a low wall with a metal fence round commercial and industrial lots (`BarangayMock/Compound Walls`).
   Openings wherever a street or driveway crosses, at least two ways into each compound, and a gate opened wherever the
   boat could not otherwise reach a resident or a compound's open ground. The layout has no residential subdivisions
   inside the map's region, so none are walled.
5b. **Rows of houses and shops**: `RowBuilder` (`Assets/Scripts/Editor/RowBuilder.cs`) runs right after the map, before
   the level, so the residents find their roofs on the rows. It replaces the one-house-per-outline houses with continuous
   rows built only from the commercial (`BAY_F_*`) and residential (`BAY_I_*`) packs in the placeholder library.
   - **Street fronts:** both sides of every street, 0.2–0.5 m off its edge, unit after unit wall to wall.
   - **Inner blocks:** back-to-back rows with 1.2 m alleys behind them, then a pass that packs the remaining holes.
   - **Which pack:** main-road fronts take the commercial pack; everywhere else takes the residential pack.
   - **Storeys, by measured height:**
     - up to 4 m is one storey, up to 6.5 m two, anything taller three;
     - the four-storey Tower is left out;
     - the target is 75 / 18 / 7, and the build gets about 79 / 18 / 3;
     - never two three-storey units within 25 m, or more than two taller units side by side.
   - **Variety:** no model is used twice within four units of a row. A taller unit swapped in for the mix may be up to
     20 % shallower.
   - **Kept clear:**
     - streets and their junction pads, and waterways;
     - landmark lots, walled compounds and the map's plain blocks;
     - WalterMart and Macaria Business Center, plus 5 m.
   - **Keeping the boat's ways open:** after the level is placed and again on the finished terrain, `RowBuilder.KeepReach`
     takes out the units that cut the boat off from a resident (a thrown line within 9.5 m, in plain view), from the
     hall, or from a compound's open ground. It never takes out a unit a resident stands on, and it removes the props
     on any unit it takes out.
   - **Report:** `Source Assets/Houses/Rows_Report.md`.

   The building kit's infill (`InfillBuilder`, `Source Assets/Buildings`) is no longer part of the build.
6. **Foliage** — `FoliagePlanner`/`FoliageBuilder`: the Carmona Roadside Flora pack plus five low-poly trees, by
   habitat (yards, roadsides, banks, open lots), planted out to the levee. A grassland pass leaves no bare ground: tall
   cogon/talahib stands (now and then a bush or a tree) wherever nothing grows, and low grass over all open ground.
   Trees are objects with thin trunk colliders (layer 2); everything without a trunk is drawn instanced
   (`InstancedFoliage`, `GrassField`) so the scene stores one line per plant.
7. **Props** — `PropPackImporter` (`Bayanihan > Props > Import Packs`) and `PropBuilder`:
   - jeepneys at the kerb of wide streets (never on the two mission streets, checked against `BoatReach`);
   - corrugated roof sheets leaning on shacks (no collider), buckets on roofs;
   - hollow-block stacks and rubble beside houses, barriers and onlookers on the levee, rescuers by the hall.
   - The roof, bucket and vegetation OBJs had no `.mtl`: the importer reads them piece by piece and colours them from
     the material names. Concrete blocks had no mesh: they are drawn from boxes.

8. **Terrain** — `TerrainBuilder` runs last. It turns the real elevation (`Docs/Maps/mabuhay-elevation.json`, SRTM 30 m,
   from `fetch_mabuhay_elevation.py`) into a `MapHeightField` (`Assets/Generated/MabuhayGameplay/MabuhayHeights.asset`):
   - smoothed over 45 m and taken relative to the hall;
   - scaled ×0.8 and softly held between −1.5 and +4 m about the ground level;
   - held at the ground level within 16 m of the mission route (easing back over 40 m), so the rescue keeps its depth;
   - raised to at least +1.75 m under candidate evacuation sites away from the route.

   It then replaces the flat ground with 64 m chunks, still named `Ground`, and bends the flat layers (lot tints,
   streets, paint, blocks, walls, wires) over them. It lifts everything that stands: a house goes to the highest ground
   under it, on a plinth (`Buildings/Plinths`), and whatever rests on something rises with it. It also lifts the plants
   and raises the levee's crest above the edge. At the flood's start about 60 % of the map is under water: the east and
   the creek valley are deep, and the west and south are dry.

9. **Sorting and NavMesh**: `MapFinisher` runs last of all.
   - **Sorting:** it files the finished map by category under `BarangayMock`:
     - `Terrain`: the ground, the lot tints, the streams, and `Foundations` (the plinths);
     - `Roads`: the surfaces and paint, plus `Street Furniture`;
     - `Residential`: the `BAY_I_` rows;
     - `Commercial`: the `BAY_F_` rows and the plain blocks;
     - `Landmarks`: the models, plus `Compound Walls` and `Signs`;
     - then `Foliage` and `Props`.

     Find groups with the `MapGroups` paths (`MapGroups.Ground`, `MapGroups.Houses(root)`, …): the builders before it
     work on the map as they lay it out.
   - **Tiles:** it cuts every plain mesh wider than 96 m (not the rows, compound walls, foliage or props) into 64 m tiles, each a child
     named like the mesh with its own collider (`MeshTiler`, `Tiled_Meshes.asset`).
   - **NavMesh:** it bakes a `NavMeshSurface` (humanoid, from the colliders) to
     `Assets/Generated/MabuhayGameplay/MabuhayNavMesh.asset`. Everything under the flood's starting level is marked not
     walkable, so it covers only the dry land (about 29 ha). Nothing walks on it yet.

**Performance.** All the houses of both packs share one material (`Assets/Materials/BAY_HousePalette_Shared.mat`): the
180 models each carried an identical copy. Static batching therefore merges the rows. Each row unit has a cull-only
`LODGroup` (`RowBuilder.CullBelowScreenHeight`, 1.5 % of the screen's height), so far houses are not drawn. From the
boat's start, meshes in view go from 3.8M triangles and 6,175 renderers to 1.1M and 1,471 (19 Nov 2026, counted in the
editor). That does not count the instanced grass and plants, or the shadow passes. The last build step then cuts
the whole-map meshes (roads, lot tints, streams, plinths) into 64 m tiles (`MeshTiler`), so only the tiles in view are
drawn. On phones the GRAPHICS setting shortens the view, thins the grass and drops the houses nearer
(`GameplayQuality`): see "Running on phones" in `Docs/Project/Overview.md`.

The map is drawn at real size (`MabuhayGameplayBuilder.MapScale` = 1): houses, streets, the crew and the boat are in
proportion. The Mabuhay flood uses its own murky, see-through material (`Water_Mabuhay.mat`).

Keep in mind: the ground level is y −1 and the water y 0, but the ground rises and falls from that level
(`MapHeightField.Sample`). On the mission route it is −1. Anything shorter than about 1.2 m on flooded ground is under
water: put it on a roof or the levee, or make it debris. Tests: `MabuhayLevelTests`, `MabuhaySmokeTests`, `MabuhayPropTests`, `MabuhayGroundTests`, `MabuhayRowsTests`, `MabuhayLandmarkTests`, EditMode `RoadNetworkTests`.
