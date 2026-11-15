#!/usr/bin/env python3
"""
Fetches the layout of Barangay Mabuhay, City of Carmona, Cavite from OpenStreetMap and writes
mabuhay-carmona.geojson next to this script: the boundary, the roads, the waterways, the named areas, the building
footprints, and the places that matter in a flood (evacuation sites, the barangay hall, schools, health, police, fire,
water).

    python3 -I Docs/Maps/fetch_mabuhay_osm.py             fetch from Overpass, save the raw answer, write the GeoJSON
    python3 -I Docs/Maps/fetch_mabuhay_osm.py --offline   write the GeoJSON again from the saved raw answer

Which places are evacuation sites, and why we think so, is hand-curated in mabuhay-overrides.json. This script reads
that file and never writes it. Unity reads the GeoJSON with Bayanihan > Rebuild Barangay Layout.

Standard library only. Map data © OpenStreetMap contributors, ODbL 1.0.
"""
import argparse
import json
import math
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent
RAW = HERE / "raw" / "overpass-mabuhay.json"
OVERRIDES = HERE / "mabuhay-overrides.json"
OUT = HERE / "mabuhay-carmona.geojson"

OVERPASS = "https://overpass-api.de/api/interpreter"
USER_AGENT = "Bayanihan-capstone-map/1.0 (Unity game; fetch_mabuhay_osm.py)"

BOUNDARY_RELATION = 11978804            # Mabuhay, admin_level 10, PSGC 0402104009
CITY_RELATION = 2008000                 # City of Carmona: places outside Mabuhay are only kept if they are in the city
HALL_ID = "w620109194"                  # Mabuhay Barangay Hall: (0, 0) of the layout
CONTEXT = (14.290, 121.010, 14.320, 121.065)   # south, west, north, east: Mabuhay, plus the Poblacion to the east

# The detail box: every road, stream and building inside it is kept, even outside Mabuhay, so a mock of the built-up
# centre is not half empty. It is the layout's focus area plus 60 m. Metres west, south, east, north of the hall.
DETAIL_ORIGIN = (14.3116367, 121.0499152)       # the hall; close enough to size the box before the data is read
DETAIL_M = (-210.0, -345.0, 467.0, 530.0)

MINOR_MARGIN = 60.0     # metres past the boundary kept for streets and streams, so they are not cut off flush
DENSIFY = 10.0          # metres: no segment longer than this before clipping
GAP_CLOSE = 200.0       # metres: a stretch dropped between two kept ones is kept after all if it is shorter than this,
                        # so a river that weaves along the city line is one line, not twenty scraps
SIMPLIFY = 1.0          # metres: Douglas-Peucker tolerance for lines once clipped
FOOTPRINT_SIMPLIFY = 0.5
BUILDING_MIN_AREA = 4.0     # square metres: smaller outlines are mapping noise, not buildings

BUILDING_KIND = {
    "house": "house", "detached": "house", "semidetached_house": "house", "terrace": "house", "bungalow": "house",
    "cabin": "house", "hut": "house", "static_caravan": "house",
    "residential": "residential", "apartments": "residential", "dormitory": "residential", "hotel": "residential",
    "yes": "generic", "garage": "generic", "garages": "generic", "service": "generic", "shed": "generic",
    "roof": "roof", "carport": "roof", "canopy": "roof",
    "commercial": "commercial", "office": "commercial",
    "retail": "retail", "supermarket": "retail", "kiosk": "retail",
    "industrial": "industrial", "factory": "industrial", "manufacture": "industrial",
    "warehouse": "warehouse", "storage_tank": "warehouse", "barn": "warehouse",
    "civic": "civic", "public": "civic", "government": "civic", "fire_station": "civic", "gymnasium": "civic",
    "sports_hall": "civic",
    "school": "school", "kindergarten": "school", "college": "school", "university": "school",
    "hospital": "hospital", "clinic": "hospital",
    "church": "religious", "chapel": "religious", "cathedral": "religious", "mosque": "religious",
    "temple": "religious", "religious": "religious",
}

ROAD_CLASS = {
    "motorway": "trunk", "motorway_link": "trunk", "trunk": "trunk", "trunk_link": "trunk",
    "primary": "primary", "primary_link": "primary",
    "secondary": "secondary", "secondary_link": "secondary",
    "tertiary": "tertiary", "tertiary_link": "tertiary",
    "unclassified": "unclassified",
    "residential": "residential", "living_street": "residential", "road": "residential",
    "service": "service", "track": "track",
    "footway": "footway", "path": "footway", "pedestrian": "footway", "steps": "footway", "cycleway": "footway",
}
MAJOR_ROADS = {"trunk", "primary", "secondary", "tertiary"}

WATERWAY_KIND = {"river": "river", "canal": "canal", "stream": "stream", "drain": "drain", "ditch": "ditch"}
MAJOR_WATERWAYS = {"river", "canal"}

AREA_KIND = {
    "residential": "residential", "commercial": "commercial", "retail": "commercial", "industrial": "industrial",
    "religious": "institutional", "education": "institutional", "institutional": "institutional",
    "military": "institutional", "grass": "green", "recreation_ground": "green", "village_green": "green",
    "forest": "green", "meadow": "green", "park": "green", "farmland": "farmland", "orchard": "farmland",
    "plant_nursery": "farmland", "cemetery": "cemetery", "reservoir": "water", "basin": "water",
}

# Kept even when they are outside the barangay: the places a flood response in Mabuhay would lean on.
ESSENTIAL_OUTSIDE = {
    "barangay_hall", "city_hall", "evacuation_centre", "school", "sports_hall", "hospital", "health_centre",
    "fire_station", "police", "dam", "assembly_point", "barangay_outpost",
}
# Also kept outside Mabuhay when they have an override entry, so named businesses can be added by hand.

LAYER_ORDER = {"boundary": 0, "area": 1, "waterway": 2, "road": 3, "landmark": 4, "building": 5}


# ---------------------------------------------------------------------------------------------- fetching

def detail_box_ll():
    """The detail box as latitude and longitude: south, west, north, east."""
    p = Projection(*DETAIL_ORIGIN)
    west, south, east, north = DETAIL_M
    s, w = p.to_ll(west, south)
    n, e = p.to_ll(east, north)
    return (round(s, 6), round(w, 6), round(n, 6), round(e, 6))


def overpass_query(override_ids):
    s, w, n, e = CONTEXT
    bb = f"({s},{w},{n},{e})"
    ds, dw, dn, de = detail_box_ll()
    dbb = f"({ds},{dw},{dn},{de})"
    by_type = {"n": "node", "w": "way", "r": "rel"}
    extra = ""
    for prefix, kind in by_type.items():
        ids = sorted(i[1:] for i in override_ids if i.startswith(prefix))
        if ids:
            extra += f"  {kind}(id:{','.join(ids)});\n"
    return f"""[out:json][timeout:180];
rel({BOUNDARY_RELATION});
out geom;
rel({CITY_RELATION});
out geom;
area({3600000000 + BOUNDARY_RELATION})->.mab;
(
  way["highway"](area.mab);
  way["waterway"](area.mab);
  way["natural"="water"](area.mab);
  way["landuse"](area.mab);
  rel["landuse"](area.mab);
  way["building"](area.mab);
  rel["building"](area.mab);
  way["highway"]{dbb};
  way["waterway"]{dbb};
  way["building"]{dbb};
  rel["building"]{dbb};
  way["highway"~"^(motorway|trunk|primary|secondary|tertiary)(_link)?$"]{bb};
  way["waterway"~"^(river|canal|dam)$"]{bb};
  way["natural"="water"]{bb};
  nwr["amenity"~"^(townhall|school|college|kindergarten|hospital|clinic|doctors|fire_station|police|community_centre|place_of_worship|pharmacy|fuel|marketplace|water_tank|bank)$"]{bb};
  nwr["name"~"outpost|tanod",i]{bb};
  nwr["healthcare"]{bb};
  nwr["office"="government"]{bb};
  nwr["leisure"~"^(pitch|sports_centre|sports_hall)$"]{bb};
  nwr["building"~"^(church|chapel|school|hospital|civic|government|gymnasium)$"]["name"]{bb};
  nwr["shop"~"^(mall|supermarket|convenience|grocery|pawnbroker)$"]{bb};
  nwr["man_made"~"^(water_tower|storage_tank|water_works|reservoir_covered)$"]{bb};
  nwr["emergency"~"^(water_tank|assembly_point)$"]{bb};
  node["emergency"="yes"]["barrier"]{bb};
{extra});
out geom;
"""


def fetch(query):
    data = urllib.parse.urlencode({"data": query}).encode()
    for wait in (0, 30, 60, 120):
        if wait:
            print(f"Overpass is busy; trying again in {wait} s.", file=sys.stderr)
            time.sleep(wait)
        request = urllib.request.Request(OVERPASS, data=data, headers={"User-Agent": USER_AGENT})
        try:
            with urllib.request.urlopen(request, timeout=240) as response:
                return response.read()
        except urllib.error.HTTPError as error:
            if error.code in (429, 502, 503, 504):
                continue
            raise
    sys.exit("Overpass did not answer after four tries. Run this again later, or use --offline.")


# ---------------------------------------------------------------------------------------------- geometry

class Projection:
    """Local flat map in metres around an origin: x east, y north. Uses the same metres-per-degree series as
    BarangayLayout.MetresPerDegree, so Unity and this script agree. Over 4 km the error is well under a metre."""

    def __init__(self, lat0, lon0):
        p = math.radians(lat0)
        self.lat0, self.lon0 = lat0, lon0
        self.north = 111132.954 - 559.822 * math.cos(2 * p) + 1.175 * math.cos(4 * p)
        self.east = 111412.84 * math.cos(p) - 93.5 * math.cos(3 * p) + 0.118 * math.cos(5 * p)

    def to_m(self, lat, lon):
        return ((lon - self.lon0) * self.east, (lat - self.lat0) * self.north)

    def to_ll(self, x, y):
        return (self.lat0 + y / self.north, self.lon0 + x / self.east)


def centroid(ring):
    pts = ring[:-1] if len(ring) > 1 and ring[0] == ring[-1] else ring
    n = len(pts)
    a = cx = cy = 0.0
    for i in range(n):
        x0, y0 = pts[i]
        x1, y1 = pts[(i + 1) % n]
        c = x0 * y1 - x1 * y0
        a += c
        cx += (x0 + x1) * c
        cy += (y0 + y1) * c
    if abs(a) < 1e-6:
        return (sum(p[0] for p in pts) / n, sum(p[1] for p in pts) / n)
    return (cx / (3 * a), cy / (3 * a))


def signed_area(ring):
    return 0.5 * sum(x0 * y1 - x1 * y0 for (x0, y0), (x1, y1) in zip(ring, ring[1:] + ring[:1]))


def inside(pt, rings):
    """Even-odd test over every ring, so holes work too."""
    x, y = pt
    hit = False
    for ring in rings:
        n = len(ring)
        j = n - 1
        for i in range(n):
            xi, yi = ring[i]
            xj, yj = ring[j]
            if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
                hit = not hit
            j = i
    return hit


def segment_distance(p, a, b):
    (px, py), (ax, ay), (bx, by) = p, a, b
    dx, dy = bx - ax, by - ay
    length2 = dx * dx + dy * dy
    t = 0.0 if length2 == 0 else max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / length2))
    return math.hypot(px - ax - t * dx, py - ay - t * dy)


def distance_to_rings(pt, rings):
    best = math.inf
    for ring in rings:
        for a, b in zip(ring, ring[1:] + ring[:1]):
            best = min(best, segment_distance(pt, a, b))
    return best


def densify(pts, step):
    out = [pts[0]]
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        n = max(1, math.ceil(math.hypot(x1 - x0, y1 - y0) / step))
        for k in range(1, n + 1):
            out.append((x0 + (x1 - x0) * k / n, y0 + (y1 - y0) * k / n))
    return out


def simplify(pts, tolerance):
    """Douglas-Peucker, without recursion."""
    if len(pts) < 3:
        return pts
    keep = [False] * len(pts)
    keep[0] = keep[-1] = True
    stack = [(0, len(pts) - 1)]
    while stack:
        a, b = stack.pop()
        best, index = -1.0, -1
        for i in range(a + 1, b):
            d = segment_distance(pts[i], pts[a], pts[b])
            if d > best:
                best, index = d, i
        if best > tolerance:
            keep[index] = True
            stack.append((a, index))
            stack.append((index, b))
    return [p for p, k in zip(pts, keep) if k]


def stitch(segments):
    """Joins way geometries that share end points into closed rings. Raises ValueError if they do not close."""
    pool = [list(s) for s in segments if len(s) >= 2]
    rings = []
    while pool:
        ring = pool.pop(0)
        while ring[0] != ring[-1]:
            for i, s in enumerate(pool):
                if s[0] == ring[-1]:
                    ring.extend(s[1:])
                elif s[-1] == ring[-1]:
                    ring.extend(reversed(s[:-1]))
                elif s[-1] == ring[0]:
                    ring[:0] = s[:-1]
                elif s[0] == ring[0]:
                    ring[:0] = list(reversed(s[1:]))
                else:
                    continue
                pool.pop(i)
                break
            else:
                raise ValueError("ring does not close")
        rings.append(ring)
    return rings


# ---------------------------------------------------------------------------------------------- reading OSM

def element_id(e):
    return e["type"][0] + str(e["id"])


def way_points(e):
    return [(g["lat"], g["lon"]) for g in e.get("geometry", []) if g]


def relation_outer_rings(e):
    segments = [[(g["lat"], g["lon"]) for g in m.get("geometry", []) if g]
                for m in e.get("members", []) if m.get("type") == "way" and m.get("role") in ("outer", "")]
    try:
        return stitch(segments)
    except ValueError:
        return []


def is_closed(points):
    return len(points) >= 4 and points[0] == points[-1]


def category_of(t):
    a = t.get("amenity")
    name = t.get("name", "").lower()
    if "outpost" in name or "tanod" in name:
        return "barangay_outpost"
    if a == "bank":
        return "bank"
    if t.get("shop") == "pawnbroker":
        return "pawnshop"
    if t.get("shop") in ("supermarket", "convenience", "grocery"):
        return "grocery"
    if a == "townhall":
        return "city_hall" if ("city hall" in name or "municipal" in name) else "barangay_hall"
    if a in ("school", "college", "kindergarten", "university") or t.get("building") == "school":
        return "school"
    if a == "pharmacy" or t.get("healthcare") == "pharmacy":
        return "pharmacy"
    if a == "hospital" or t.get("healthcare") == "hospital" or t.get("building") == "hospital":
        return "hospital"
    if a in ("clinic", "doctors") or t.get("healthcare") in ("clinic", "doctor", "centre", "birthing_centre", "midwife"):
        return "health_centre"
    if a == "fire_station":
        return "fire_station"
    if a == "police":
        return "police"
    if a == "community_centre":
        return "community_centre"
    if a == "place_of_worship" or t.get("building") in ("church", "chapel"):
        return "church"
    if a == "fuel":
        return "fuel"
    if a == "marketplace" or t.get("shop") == "mall":
        return "market"
    if t.get("office") == "government" or t.get("building") in ("civic", "government"):
        return "government"
    if t.get("leisure") == "pitch":
        return "court"
    if t.get("leisure") in ("sports_centre", "sports_hall") or t.get("building") == "gymnasium":
        return "sports_hall"
    if (t.get("man_made") in ("water_tower", "storage_tank", "water_works", "reservoir_covered")
            or a == "water_tank" or t.get("emergency") == "water_tank"):
        return "water_supply"
    if t.get("waterway") == "dam":
        return "dam"
    if t.get("emergency") == "assembly_point":
        return "assembly_point"
    if t.get("emergency") == "yes" and "barrier" in t:
        return "emergency_access"
    return None


def tag_summary(t):
    for key in ("amenity", "healthcare", "leisure", "office", "shop", "man_made", "emergency", "waterway", "building"):
        if key in t:
            return f"{key}={t[key]}"
    return ""


# ---------------------------------------------------------------------------------------------- building the map

def build(raw, overrides):
    elements = {}
    for e in raw["elements"]:
        elements.setdefault((e["type"], e["id"]), e)

    boundary = elements.get(("relation", BOUNDARY_RELATION))
    if boundary is None:
        sys.exit(f"The answer has no boundary relation {BOUNDARY_RELATION}.")
    city = elements.get(("relation", CITY_RELATION))
    if city is None:
        sys.exit(f"The answer has no city boundary relation {CITY_RELATION}.")
    hall = next((e for e in elements.values() if element_id(e) == HALL_ID), None)
    if hall is None or not is_closed(way_points(hall)):
        sys.exit(f"The answer has no footprint for the barangay hall ({HALL_ID}), which is the origin.")

    # The origin is the hall's footprint centroid. A first projection near it finds the centroid in metres.
    first = Projection(*way_points(hall)[0])
    hx, hy = centroid([first.to_m(*p) for p in way_points(hall)])
    proj = Projection(*first.to_ll(hx, hy))

    def to_m(points):
        return [proj.to_m(lat, lon) for lat, lon in points]

    def to_coords(points_m):
        out = []
        for x, y in points_m:
            lat, lon = proj.to_ll(x, y)
            out.append([round(lon, 7), round(lat, 7)])
        return out

    # Boundary: one or more closed rings, counter-clockwise (RFC 7946).
    ll_rings = relation_outer_rings(boundary)
    if not ll_rings:
        sys.exit("The boundary's outer ways do not join into a closed ring.")
    rings_m = []
    for ring in ll_rings:
        pts = to_m(ring[:-1])
        if signed_area(pts) < 0:
            pts.reverse()
        rings_m.append(pts)
    bx = [p[0] for r in rings_m for p in r]
    by = [p[1] for r in rings_m for p in r]
    near_box = (min(bx) - MINOR_MARGIN, min(by) - MINOR_MARGIN, max(bx) + MINOR_MARGIN, max(by) + MINOR_MARGIN)
    s, w, n, e = CONTEXT
    cx0, cy0 = proj.to_m(s, w)
    cx1, cy1 = proj.to_m(n, e)

    # The city's boundary, simplified: only used to drop places that belong to the next town.
    city_ll = relation_outer_rings(city)
    if not city_ll:
        sys.exit("The city boundary's outer ways do not join into a closed ring.")
    city_rings = [simplify(to_m(r), 5.0)[:-1] for r in city_ll]

    def in_context(p):
        return cx0 <= p[0] <= cx1 and cy0 <= p[1] <= cy1 and inside(p, city_rings)

    ds, dw, dn, de = detail_box_ll()
    dx0, dy0 = proj.to_m(ds, dw)
    dx1, dy1 = proj.to_m(dn, de)

    def in_detail(p):
        return dx0 <= p[0] <= dx1 and dy0 <= p[1] <= dy1

    def near_barangay(p):
        if not (near_box[0] <= p[0] <= near_box[2] and near_box[1] <= p[1] <= near_box[3]):
            return False
        return inside(p, rings_m) or distance_to_rings(p, rings_m) <= MINOR_MARGIN

    features = []
    boundary_coords = [[to_coords(r + r[:1])] for r in rings_m]
    features.append({
        "type": "Feature",
        "geometry": ({"type": "Polygon", "coordinates": boundary_coords[0]} if len(boundary_coords) == 1
                     else {"type": "MultiPolygon", "coordinates": boundary_coords}),
        "properties": {"layer": "boundary", "id": element_id(boundary), "name": boundary["tags"].get("name", "Mabuhay")},
    })

    def clipped_runs(points_m, major):
        keep_test = (lambda p: near_barangay(p) or in_detail(p) or in_context(p)) if major \
            else (lambda p: near_barangay(p) or in_detail(p))
        dense = densify(points_m, DENSIFY)
        keep = [keep_test(p) for p in dense]
        i = 0
        while i < len(dense):
            if keep[i]:
                i += 1
                continue
            j = i
            while j < len(dense) and not keep[j]:
                j += 1
            if 0 < i and j < len(dense):
                gap = sum(math.dist(dense[k], dense[k + 1]) for k in range(i - 1, j))
                if gap <= GAP_CLOSE:
                    for k in range(i, j):
                        keep[k] = True
            i = j
        runs, run = [], []
        for p, kept in zip(dense, keep):
            if kept:
                run.append(p)
            else:
                if len(run) >= 2:
                    runs.append(run)
                run = []
        if len(run) >= 2:
            runs.append(run)
        return [simplify(r, SIMPLIFY) for r in runs]

    def add_lines(e, layer, props, major):
        runs = clipped_runs(to_m(way_points(e)), major)
        base = element_id(e)
        for i, run in enumerate(runs):
            if len(run) < 2:
                continue
            fid = base if len(runs) == 1 else f"{base}.{i + 1}"
            features.append({
                "type": "Feature",
                "geometry": {"type": "LineString", "coordinates": to_coords(run)},
                "properties": {"layer": layer, "id": fid, "name": e["tags"].get("name", ""), **props},
            })

    landmark_ids = set(overrides.get("landmarks", {}))
    warnings = []

    for e in elements.values():
        if e is boundary or e is city:
            continue
        t = e.get("tags", {})
        eid = element_id(e)

        # Roads and waterways (the same way can also be a landmark, e.g. a dam, so no "continue" until handled).
        if e["type"] == "way" and t.get("highway") in ROAD_CLASS and t.get("area") != "yes":
            cls = ROAD_CLASS[t["highway"]]
            add_lines(e, "road", {
                "class": cls,
                "oneway": t.get("oneway") in ("yes", "1", "true"),
                "bridge": t.get("bridge", "no") not in ("no", ""),
            }, cls in MAJOR_ROADS)
            continue
        if e["type"] == "way" and t.get("waterway") in WATERWAY_KIND:
            kind = WATERWAY_KIND[t["waterway"]]
            add_lines(e, "waterway", {"kind": kind}, kind in MAJOR_WATERWAYS)
            continue

        # Named areas, and every body of water near the barangay.
        is_water = t.get("natural") == "water" or t.get("landuse") in ("reservoir", "basin")
        if (t.get("landuse") in AREA_KIND or is_water) and eid not in landmark_ids:
            if e["type"] == "way":
                pts = way_points(e)
                ll_ring = pts if is_closed(pts) else None
            else:
                outer = relation_outer_rings(e)
                ll_ring = max(outer, key=lambda r: abs(signed_area(to_m(r)))) if outer else None
            if ll_ring is None:
                continue
            ring = to_m(ll_ring[:-1])
            c = centroid(ring)
            name = t.get("name", "")
            if is_water:
                if not near_barangay(c) and not any(near_barangay(p) for p in ring):
                    continue
            elif not name or not inside(c, rings_m):
                continue
            ring = simplify(ring + ring[:1], SIMPLIFY)[:-1]
            if signed_area(ring) < 0:
                ring.reverse()
            features.append({
                "type": "Feature",
                "geometry": {"type": "Polygon", "coordinates": [to_coords(ring + ring[:1])]},
                "properties": {"layer": "area", "id": eid, "name": name,
                               "kind": "water" if is_water else AREA_KIND[t["landuse"]]},
            })
            if category_of(t) is None:      # a bank or a mall can be a commercial area and a landmark at once
                continue

        # Landmarks.
        override = overrides.get("landmarks", {}).get(eid, {})
        category = override.get("category") or category_of(t)
        if category is None:
            continue
        if not override and not t.get("name") and tag_summary(t).startswith("building="):
            continue        # an unnamed school, hospital or church building is a building, drawn by the building layer
        footprint = None
        if e["type"] == "node":
            pos = proj.to_m(e["lat"], e["lon"])
        elif e["type"] == "way":
            pts = way_points(e)
            if is_closed(pts):
                ring = to_m(pts[:-1])
                pos = centroid(ring)
                footprint = ring
            else:
                line = to_m(pts)
                pos = line[len(line) // 2]
        else:
            outer = relation_outer_rings(e)
            if outer:
                ring = to_m(max(outer, key=lambda r: abs(signed_area(to_m(r))))[:-1])
                pos = centroid(ring)
                footprint = ring
            else:
                member_pts = [proj.to_m(g["lat"], g["lon"]) for m in e.get("members", [])
                              for g in m.get("geometry", []) if g]
                if not member_pts:
                    continue
                pos = (sum(p[0] for p in member_pts) / len(member_pts), sum(p[1] for p in member_pts) / len(member_pts))
        in_barangay = inside(pos, rings_m)
        if not in_barangay and category not in ESSENTIAL_OUTSIDE and not override:
            continue
        if not in_context(pos) and not override:
            continue
        props = {
            "layer": "landmark", "id": eid, "name": override.get("name", t.get("name", "")),
            "category": category, "tag": tag_summary(t),
            "evacuation": override.get("evacuation", "none"),
            "verified": bool(override.get("verified", False)),
            "source": override.get("source", ""), "note": override.get("note", ""),
        }
        if footprint:
            fp = simplify(footprint + footprint[:1], FOOTPRINT_SIMPLIFY)[:-1]
            if signed_area(fp) < 0:
                fp.reverse()
            props["footprint"] = to_coords(fp + fp[:1])
        features.append({
            "type": "Feature",
            "geometry": {"type": "Point", "coordinates": to_coords([pos])[0]},
            "properties": props,
        })

    # Buildings: every footprint in Mabuhay or in the detail box that is not already a landmark.
    landmark_emitted = {f["properties"]["id"] for f in features if f["properties"]["layer"] == "landmark"}
    too_small = 0
    for e in elements.values():
        t = e.get("tags", {})
        eid = element_id(e)
        if e is boundary or e is city or e["type"] == "node" or t.get("building", "no") == "no":
            continue
        if eid in landmark_emitted:
            continue
        if e["type"] == "way":
            pts = way_points(e)
            if not is_closed(pts):
                continue
            ll_ring = pts
        else:
            outer = relation_outer_rings(e)
            if not outer:
                continue
            ll_ring = max(outer, key=lambda r: abs(signed_area(to_m(r))))
        ring = to_m(ll_ring[:-1])
        if len(ring) < 3:
            continue
        c = centroid(ring)
        if not (inside(c, rings_m) or in_detail(c)):
            continue
        ring = simplify(ring + ring[:1], FOOTPRINT_SIMPLIFY)[:-1]
        if len(ring) < 3:
            continue
        if abs(signed_area(ring)) < BUILDING_MIN_AREA:
            too_small += 1
            continue
        if signed_area(ring) < 0:
            ring.reverse()
        try:
            levels = int(float(str(t.get("building:levels", "0")).split(";")[0]))
        except ValueError:
            levels = 0
        features.append({
            "type": "Feature",
            "geometry": {"type": "Polygon", "coordinates": [to_coords(ring + ring[:1])]},
            "properties": {"layer": "building", "id": eid, "name": t.get("name", ""),
                           "kind": BUILDING_KIND.get(t["building"], "other"), "levels": max(levels, 0)},
        })
    if too_small:
        warnings.append(f"{too_small} building outlines under {BUILDING_MIN_AREA:g} m² were left out.")

    found = {f["properties"]["id"] for f in features}
    for oid in landmark_ids:
        if oid not in found:
            warnings.append(f"{oid} is in the overrides but not in the OpenStreetMap answer; it is left out.")

    for site in overrides.get("extraSites", []):
        lat, lon = site.get("lat"), site.get("lon")
        geometry = None
        if lat is not None and lon is not None:
            geometry = {"type": "Point", "coordinates": [round(lon, 7), round(lat, 7)]}
        else:
            warnings.append(f"{site['id']} ({site.get('name', '')}) has no position; it is listed without one.")
        features.append({
            "type": "Feature",
            "geometry": geometry,
            "properties": {
                "layer": "landmark", "id": site["id"], "name": site.get("name", ""),
                "category": site.get("category", "evacuation_centre"), "tag": "",
                "evacuation": site.get("evacuation", "none"),
                "verified": bool(site.get("verified", False)),
                "source": site.get("source", ""), "note": site.get("note", ""),
            },
        })

    features.sort(key=lambda f: (LAYER_ORDER[f["properties"]["layer"]], f["properties"]["id"]))

    tags = boundary["tags"]
    metadata = {
        "barangay": tags.get("name", "Mabuhay"),
        "city": "City of Carmona",
        "province": "Cavite",
        "region": "CALABARZON (Region IV-A)",
        "psgc": tags.get("ref", ""),
        "postalCode": "4116",
        "population": 12714,
        "populationYear": 2020,
        "populationSource": "PSA 2020 Census of Population and Housing (via PhilAtlas)",
        "osmRelation": BOUNDARY_RELATION,
        "origin": {"id": HALL_ID, "name": "Mabuhay Barangay Hall",
                   "lat": round(proj.lat0, 7), "lon": round(proj.lon0, 7)},
        "detailArea": {"xMin": round(dx0, 2), "yMin": round(dy0, 2), "xMax": round(dx1, 2), "yMax": round(dy1, 2),
                       "note": "Metres east and north of the origin. Roads, streams and buildings are complete in here."},
        "snapshot": raw.get("osm3s", {}).get("timestamp_osm_base", ""),
        "attribution": "© OpenStreetMap contributors",
        "licence": "ODbL-1.0 (https://opendatacommons.org/licenses/odbl/)",
        "source": f"https://www.openstreetmap.org/relation/{BOUNDARY_RELATION}",
        "notes": "Coordinates are WGS84 longitude, latitude. Evacuation roles come from mabuhay-overrides.json and "
                 "are unverified unless a feature says otherwise.",
    }
    return metadata, features, warnings


def write(metadata, features):
    lines = ",\n".join(json.dumps(f, ensure_ascii=False, separators=(",", ":")) for f in features)
    with open(OUT, "w", encoding="utf-8", newline="\n") as out:
        out.write('{"type":"FeatureCollection",\n"metadata":')
        out.write(json.dumps(metadata, ensure_ascii=False, separators=(",", ":")))
        out.write(',\n"features":[\n')
        out.write(lines)
        out.write("\n]}\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__.strip().splitlines()[0])
    parser.add_argument("--offline", action="store_true", help="use the saved raw answer instead of fetching")
    args = parser.parse_args()

    overrides = json.loads(OVERRIDES.read_text(encoding="utf-8"))
    if args.offline:
        if not RAW.exists():
            sys.exit(f"There is no saved answer at {RAW}. Run once without --offline first.")
        raw_bytes = RAW.read_bytes()
    else:
        ids = set(overrides.get("landmarks", {})) | {HALL_ID}
        raw_bytes = fetch(overpass_query(ids))
        json.loads(raw_bytes)                        # fail here, not half-way through, if Overpass sent an error page
        RAW.parent.mkdir(exist_ok=True)
        RAW.write_bytes(raw_bytes)

    metadata, features, warnings = build(json.loads(raw_bytes), overrides)
    write(metadata, features)

    count = {}
    for f in features:
        count[f["properties"]["layer"]] = count.get(f["properties"]["layer"], 0) + 1
    sites = [f for f in features if f["properties"].get("evacuation", "none") != "none"]
    print(f"Wrote {OUT.name}: " + ", ".join(f"{count.get(k, 0)} {k}" for k in LAYER_ORDER) +
          f"; {len(sites)} evacuation sites ({sum(1 for f in sites if not f['properties']['verified'])} unverified). "
          f"Snapshot {metadata['snapshot']}.")
    for warning in warnings:
        print("Note: " + warning)


if __name__ == "__main__":
    main()
