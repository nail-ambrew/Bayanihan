# Map and Props

Barangay Mabuhay is generated from real map data; nothing on it is placed by hand.

1. **Layout** — `Docs/Maps/mabuhay-carmona.geojson` (OpenStreetMap) + `mabuhay-overrides.json` → `BarangayLayout`
   asset (`Bayanihan > Rebuild Barangay Layout`). Metres east/north of the barangay hall. See [[Maps/README|Maps]].
2. **Houses and landmarks** — `BarangayMockPlanner` gives every building one fate (a house prefab facing the street, a
   block, a shed, part of a landmark); `BarangayMockBuilder` makes it under `BarangayMock`. Landmarks are fitted to their
   lots. Every drawn part gets its own mesh collider (`SolidWhereDrawn`), slippery, so there are no invisible walls.
3. **Level** — `MabuhayLevelBuilder`: the hall and drop-off, the boat's start, residents, posts, live wire, the jam,
   wreckage fields, and the **levee** round the edge (crest at y 2.5, dry land beyond).
4. **Foliage** — `FoliagePlanner`/`FoliageBuilder`: the Carmona Roadside Flora pack plus five low-poly trees, by
   habitat (yards, roadsides, banks, open lots), ~3,000 plants + 430 on the levee. Trunks have thin colliders (layer 2).
5. **Props** — `PropPackImporter` (`Bayanihan > Props > Import Packs`) and `PropBuilder`:
   - jeepneys at the kerb of wide streets (never on the two mission streets, checked against `BoatReach`);
   - corrugated roof sheets leaning on shacks (no collider), buckets on roofs;
   - hollow-block stacks and rubble beside houses, barriers and onlookers on the levee, rescuers by the hall.
   - The roof, bucket and vegetation OBJs had no `.mtl`: the importer reads them piece by piece and colours them from
     the material names. Concrete blocks had no mesh: they are drawn from boxes.

Keep in mind: ground y -1, water y 0. Anything shorter than about 1.2 m on the ground is under water — put it on a roof, the
levee or make it debris. Tests: `MabuhayLevelTests`, `MabuhaySmokeTests`, `MabuhayPropTests`.
