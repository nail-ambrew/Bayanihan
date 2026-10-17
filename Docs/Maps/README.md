# Barangay Mabuhay layout

The real layout of **Barangay Mabuhay, City of Carmona, Cavite** (PSGC 0402104009), for building a level over it. It covers:
- the boundary;
- roads and highways;
- waterways;
- named areas: subdivisions and industrial parks;
- the places that matter in a flood: evacuation sites, the barangay hall, schools, health, police, fire and water.

This is a map, not 3D assets.

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

## Refreshing from OpenStreetMap

```bash
python3 -I Docs/Maps/fetch_mabuhay_osm.py
```

Then run **Bayanihan > Rebuild Barangay Layout** and **Export Barangay Layout Map** in Unity.
- `--offline` rebuilds the GeoJSON from `raw/` with no network access. Use it after editing the overrides.
- The script retries when Overpass is busy.
- Rebuilding keeps the asset's `scale`.

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

## Sources and licence

- **Map data © OpenStreetMap contributors**, under the [Open Database Licence (ODbL) 1.0](https://opendatacommons.org/licenses/odbl/).
  - Boundary: [relation 11978804](https://www.openstreetmap.org/relation/11978804).
  - The GeoJSON and the Unity asset are derived databases. Anywhere the map is shown, the game included, must credit "© OpenStreetMap contributors".
- **Population:** 12,714, from the PSA 2020 Census of Population and Housing (via PhilAtlas).
- **Evacuation evidence:** Manila Bulletin, 11 Nov 2020 (Typhoon Ulysses evacuations in Carmona); DPWH news release 19249 (2020).
