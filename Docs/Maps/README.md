# Barangay Mabuhay layout

The real layout of **Barangay Mabuhay, City of Carmona, Cavite** (PSGC 0402104009), for building a level over it. It covers:
- the boundary;
- roads and highways;
- waterways;
- named areas: subdivisions and industrial parks;
- every building footprint (3,228 of them: the whole barangay, plus the built-up centre just outside it);
- the places that matter in a flood: evacuation sites, the barangay hall, schools, health, police, fire and water.

This is a map, not 3D assets. A placeholder 3D version of the built-up centre, made from the models already in the
project, is in `Assets/Scenes/Maps/MabuhayMock.unity` (see "The placeholder map" below).

| File | What it is |
|---|---|
| `mabuhay-carmona.geojson` | The map snapshot, in WGS84 longitude and latitude. Opens in [geojson.io](https://geojson.io) or QGIS. |
| `mabuhay-overrides.json` | Hand-curated facts OpenStreetMap does not hold: which places are evacuation sites, and why. |
| `fetch_mabuhay_osm.py` | Refreshes the GeoJSON from OpenStreetMap. Standard-library Python only. |
| `raw/overpass-mabuhay.json` | The raw OpenStreetMap answer the GeoJSON was built from, so `--offline` can rebuild it exactly. |
| `mabuhay-layout.svg` | A printable map exported from Unity, with names, a legend and a scale bar. |

In Unity, the layout is `Assets/Resources/Layouts/MabuhayCarmona.asset` (`BarangayLayout`). It is built from the GeoJSON with **Bayanihan > Rebuild Barangay Layout**.

## How it fits the game

- **Coordinates:** points are stored at real size, in metres east (x) and north (y) of **Mabuhay Barangay Hall**, which is (0, 0).
- **Scale:** the asset's `scale` (default 0.15) shrinks the map to game size. `ToWorld` puts east on +X and north on +Z. At 0.15 the barangay, about 4.3 × 2.4 km, becomes about 640 × 360 m.
- **Road widths** are game values and are never scaled. At 0.15, subdivision streets about 60 m apart end up about 9 m apart, which is narrower than one game street. Use the focus area (the built-up centre) at a scale of 0.5–1.0 for a detailed section.
- **Preview:** **Bayanihan > Add Barangay Layout Preview** adds a Scene-view preview (`BarangayLayoutView`) that draws lines and markers only. The object is tagged EditorOnly.
- **Printable map:** **Bayanihan > Export Barangay Layout Map** writes `mabuhay-layout.svg`.
- **Placeholder 3D map:** **Bayanihan > Make Placeholder Prefabs**, then **Bayanihan > Build Barangay Mock**.

## Refreshing from OpenStreetMap

```bash
python3 -I Docs/Maps/fetch_mabuhay_osm.py
```

Then run **Bayanihan > Rebuild Barangay Layout**, **Export Barangay Layout Map** and **Build Barangay Mock** in Unity.
The script keeps every road, stream and building inside the *detail area* (the focus area plus 60 m), even where it
spills past the barangay boundary, so a mock of the centre is complete to its edge.
- `--offline` rebuilds the GeoJSON from `raw/` with no network access. Use it after editing the overrides.
- The script retries when Overpass is busy.
- Rebuilding keeps the asset's `scale`.

## Landmark categories

Each landmark has a category (`LandmarkCategory`), so a level can swap in a prefab per kind of place. Positions come from OpenStreetMap unless a place is listed under `extraSites` in `mabuhay-overrides.json`.

| Category | Examples in Mabuhay | Position |
|---|---|---|
| Barangay hall, court, school, hospital, health centre, police, fire station, water supply | Mabuhay Barangay Hall, Mabuhay Elementary School, Carmona Hospital and Medical Center | From OpenStreetMap |
| **Bank** | BDO, Metrobank | From OpenStreetMap |
| **Grocery** (supermarkets, groceries, convenience stores) | WalterMart Supermarket, 7-Eleven, Dali, Amipar Enterprises | From OpenStreetMap |
| Market (malls, marketplaces) | WalterMart (the mall) | From OpenStreetMap |
| **Pawnshop** | M Lhuillier pawnshop | **Unknown**: not in OpenStreetMap |
| **Barangay outpost** | The tanod outpost | **Unknown**: not in OpenStreetMap |

The four bold categories are new. A place with an unknown position is listed in the layout but not drawn. The printable map lists it under "Not yet located". To place one, give its `lat` and `lon` in `mabuhay-overrides.json` (ask the barangay hall for the outpost), run the script with `--offline`, then rebuild the layout in Unity.

## Evacuation sites: not yet confirmed

No official list of Mabuhay's evacuation centres is published online. Every site in the layout is therefore a **candidate**, marked `verified: false`, and each one says where the claim comes from:

| Site | Where | Why it is a candidate |
|---|---|---|
| Mabuhay Barangay Hall | In Mabuhay, (0, 0) | Barangay halls usually serve as the command post and first refuge |
| Court beside the hall | In Mabuhay | Barangay courts are commonly used as evacuation sites |
| Mabuhay Elementary School | In Mabuhay | Public schools took Mabuhay's evacuees during Typhoon Ulysses (Manila Bulletin, 11 Nov 2020) |
| Milagrosa Elementary School, Carmona Elementary School, Carmona National High School | Nearby | Same Typhoon Ulysses report |
| Phase 2 Milagrosa Covered Court, Carsigma Gymnasium | Nearby | Courts and gymnasiums are commonly used |
| Carmona Evacuation Center (DPWH) | Barangay Lantic; **position unknown** | Built by DPWH (news release 19249, 2020). It is not in OpenStreetMap, so no coordinates were invented |

To confirm the sites, ask the barangay hall or **Carmona CDRRMO** (0917-112-0291, (046) 889-2773):
1. Which sites are designated for Mabuhay, and how many people each holds.
2. Where the DPWH Carmona Evacuation Center in Lantic is. Add its `lat` and `lon` in `mabuhay-overrides.json`.
3. Which streets and riverbanks flood first. The layout has no elevation or flood-depth data yet; Project NOAH hazard maps are a possible source.

For each confirmed site, set `"evacuation": "designated"` and `"verified": true` in `mabuhay-overrides.json`, and record who confirmed it and when in `"source"`. Then run the script with `--offline`, followed by **Rebuild Barangay Layout**. A test fails if a site is designated without being verified.

## The placeholder map

A 3D version of the barangay, made only of stand-ins, so a level can take shape before the final models arrive.
It is built in its own scene, `Assets/Scenes/Maps/MabuhayMock.unity`, and **never in Gameplay**. The default covers the
focus area at real size (about 557 x 755 m, roughly 980 buildings); the whole barangay (about 3,000 buildings) builds in
well under a second too.

| Step | Menu item | What it does |
|---|---|---|
| 1 | **Bayanihan > Make Placeholder Prefabs** | Bakes the 24 slum-map houses, the hall, the market and the three school blocks into one mesh each, and fills the **Placeholder Library** and the **Settings** (`Assets/Settings/Maps`). Run it once, and again if a source model changes. |
| 2 | **Bayanihan > Build Barangay Mock** | Builds the map from the layout into the mock scene and saves it. The whole map is one object, `BarangayMock`, deleted and made again every time: put nothing of your own inside it. |

**What is in it.** The ground; subdivision and industrial-park tints; streams; every road at its real width (a bridge is a
lighter strip); every building; the landmarks; a flat name above each landmark; and an orange pole and notice at every
evacuation site (**orange = not yet confirmed**, green once it is). The meshes (roads, ground, plain blocks) are kept in
`Assets/Generated/BarangayMock`, so the scene itself holds only objects and links. Plain materials are made once in
`Assets/Materials/Placeholders` and then left alone: change a colour there and it stays; delete one to have it made again.

**How each building is decided** (the same layout and library always give the same map):

- A **house** is the slum-map house that needs the least stretching to fill the footprint (up to 0.8x to 1.6x on each
  side), turned to face the nearest street. A shop house is preferred on a main road and avoided on a side street. A
  frontage over about 8 m becomes a **row** of houses side by side.
- A factory, a shop, a school, a church, a hospital, or anything over 300 m2 is a plain **block** of its real outline.
  Under 15 m2 it is a shed. A building tagged as a roof is an open canopy.
- A **landmark takes over** the building it stands in. The hall gets the hall model if the footprint is close enough in
  size, otherwise a block. A school or a mall is *grounds*: a flat slab with its buildings on it, the school's as school
  blocks. A bank, pharmacy or grocery in a small building turns it into a shop house; in a big shared building it adds a
  sign. A landmark with no building at all gets a shop house of its own.
- Landmarks with no known position (the pawnshop, the outpost, the DPWH evacuation centre) are left off the map, and the
  build log says so.

The **Console** shows a line for every landmark after a build ("Mabuhay Barangay Hall: Hall_Barangay67 model fitted at
0.69 x 0.76. Swap in the real model when it is made."), and `BarangayMock` carries a **Barangay Mock Info** component with
the counts and the layout revision it was built from.

### Swapping in the final models

Open `Assets/Settings/Maps/PlaceholderLibrary.asset`.

1. **Make the prefab follow one convention:** the pivot is the middle of the footprint at ground level, and the front
   faces **+Z**. The generator measures the prefab each time, so its size is never typed.
2. **A general house:** add a **Houses** entry with your prefab, a type and a storey count, then delete (or keep) the
   baked ones. Each building takes the entry that fits it best.
3. **A landmark** (hall, bank, grocery, barangay outpost, pawnshop, ...): in **Landmarks**, find the category and set its
   **Placement**:
   - `Prefab` for one model of the whole thing (fitted to the footprint if it is within **Min / Max Scale**, otherwise a block);
   - `ShopHouse` for a model of a small shop on a small building (the **Houses** entries of type Shop are used);
   - `Parts` for several models, one per building in the grounds;
   - `Block` for a plain coloured block, with its height and colour.
4. Run **Bayanihan > Build Barangay Mock** again.

**Make Placeholder Prefabs never overwrites a model you set up with your own prefab** (an entry with no source model), so
re-running it after the final models arrive is safe. Styles that have no prefab (plain blocks, shop houses, courts) are
reset to their defaults each time, so make a block's height or colour permanent by giving the category a prefab instead,
or change the defaults in `Assets/Scripts/Editor/PlaceholderBaker.cs`.

Other knobs are in `Assets/Settings/Maps/BarangayMockSettings.asset`: the region (focus area, detail area, whole
barangay, or your own rectangle), the scale (the map is built at real size and the whole thing is scaled), which layers to
build, and **Colliders** (off by default; on gives the ground, blocks and houses colliders so a boat can be tried on it).

**Known limits of the placeholders:** flat ground (no elevation or flood depth: see the gap above); houses are stretched
rather than redrawn; no trees, vehicles or street furniture; roads are plain strips with no kerbs or markings; names are
flat text that shows through walls (switch the **Signs** object off for street-level work); the hall model is bigger than Mabuhay's real hall, so it is scaled down to about 0.7x to
fit and its details are squeezed. The overview camera's near plane is 1 m, because the flat layers are only a few centimetres apart: a closer near
plane makes them flicker at a distance.

## Playing on the map: Barangay Mabuhay

The flood-rescue mission can be played on this map, in `Assets/Scenes/Maps/MabuhayGameplay.unity`. Pick **Mabuhay** on the loadout screen and press LAUNCH.

| Step | Menu item or file | What it does |
|---|---|---|
| 1 | **Bayanihan > Build Mabuhay Gameplay Scene** | Builds the map from `Assets/Settings/Maps/BarangayMockSettings_Gameplay.asset` (colliders on, ground a metre below the water, scale 1), then places the mission on it. Copies the boat, swimmer, camera, water and storm from Gameplay, read only. Adds the scene to Build Settings. |
| 2 | **Bayanihan > Rebuild Mabuhay Mission Asset** | Writes `Assets/Resources/Maps/MabuhayMission.asset`: the same ten residents as Manila, with the Mabuhay briefing. It lives outside `Resources/Missions`, so Manila's mission stays the one that ships. |

**Where things are.** Everything is placed from the layout's streets and the map's own colliders, never from how the models look, so swapping the final models in and rebuilding puts the posts, roofs and hazards back. The hall (0, 0) faces Bagumbayan Street, the evacuation drop-off is on the street in front of it, and the boat starts 46 m along the street. The live wire crosses the street at about 86 m, and the debris jam is in the A. Mapalad Street lane, 47 m west of the hall. J. M. Loyola Street is more than 240 m from the hall by water, so the mission stays on Bagumbayan Street and the lane.

**Checks.** `MabuhaySmokeTests`, `MabuhayLevelTests`, `MabuhayFlowTests` and `MapChoiceFlowTests` run in PlayMode. `MapChoiceTests`, `MabuhayMissionTests` and `BoatReachTests` run in EditMode. The level tests ask the physics engine where a boat can go (`BoatReach`), so they hold whatever the map is built from.

**The keyboard bot.** `MabuhayBotTests.The_bot_rescues_all_ten_residents_with_real_key_input_and_the_report_appears` plays the whole mission with a virtual keyboard. It is explicit, so run it by name with the Game view showing:

```bash
unity command run_tests --mode PlayMode --async_tests true --include_explicit true --filter The_bot_rescues_all_ten_residents_with_real_key_input_and_the_report_appears
```

It takes about four and a half minutes. Its log and photographs of the start, the helm, each rescue, the hall and the report land in `Temp/MabuhayBot/`.

## Sources and licence

- **Map data © OpenStreetMap contributors**, under the [Open Database Licence (ODbL) 1.0](https://opendatacommons.org/licenses/odbl/).
  - Boundary: [relation 11978804](https://www.openstreetmap.org/relation/11978804).
  - The GeoJSON and the Unity asset are derived databases. Anywhere the map is shown, the game included, must credit "© OpenStreetMap contributors".
- **Population:** 12,714, from the PSA 2020 Census of Population and Housing (via PhilAtlas).
- **Evacuation evidence:** Manila Bulletin, 11 Nov 2020 (Typhoon Ulysses evacuations in Carmona); DPWH news release 19249 (2020).
