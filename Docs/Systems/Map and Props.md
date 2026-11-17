# Map and Props

Barangay Mabuhay is generated from real map data; nothing on it is placed by hand.

1. **Layout** — `Docs/Maps/mabuhay-carmona.geojson` (OpenStreetMap) + `mabuhay-overrides.json` → `BarangayLayout`
   asset (`Bayanihan > Rebuild Barangay Layout`). Metres east/north of the barangay hall. See [[Maps/README|Maps]].
2. **Houses and landmarks** — `BarangayMockPlanner` gives every building one fate (a house prefab facing the street, a
   block, a shed, part of a landmark); `BarangayMockBuilder` makes it under `BarangayMock`. Landmarks are fitted to their
   lots. Every drawn part gets its own mesh collider (`SolidWhereDrawn`), slippery, so there are no invisible walls.
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

The map is drawn at 2× real size (`MabuhayGameplayBuilder.MapScale`); people, props, grass, poles and walls stay real-sized
(their groups are scaled by 1/scale). The Mabuhay flood uses its own murky, see-through material (`Water_Mabuhay.mat`).

Keep in mind: ground y -1, water y 0. Anything shorter than about 1.2 m on the ground is under water — put it on a roof, the
levee or make it debris. Tests: `MabuhayLevelTests`, `MabuhaySmokeTests`, `MabuhayPropTests`, `MabuhayGroundTests`, EditMode `RoadNetworkTests`.
