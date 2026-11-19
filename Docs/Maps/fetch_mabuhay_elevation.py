#!/usr/bin/env python3
"""
Fetches the ground elevation of the Mabuhay map and writes mabuhay-elevation.json next to this script: a grid of
heights above sea level, in metres, every 30 m over the detail box plus a margin. The game reads it through MapHeight
(Assets/Scripts/Map/MapHeight.cs), which turns it into the gentle slopes, low streets and higher ground of the map.

    python3 -I Docs/Maps/fetch_mabuhay_elevation.py              fetch from OpenTopoData, save the raw answer, write the grid
    python3 -I Docs/Maps/fetch_mabuhay_elevation.py --offline    write the grid again from the saved raw answer

Only the coordinates of the grid points are sent. The source is the SRTM 30 m elevation model (NASA / USGS, public
domain), served by the public OpenTopoData API (opentopodata.org: at most 100 points a call and one call a second).
A 30 m model is coarse: it gives the lie of the land, not kerbs or drains, so the game smooths it and adds its own rules
for the flood.

Standard library only.
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
RAW = HERE / "raw" / "opentopodata-mabuhay.json"
OUT = HERE / "mabuhay-elevation.json"

API = "https://api.opentopodata.org/v1/{dataset}"
DATASETS = ("srtm30m", "aster30m")       # the second is asked for only where the first has no value
USER_AGENT = "Bayanihan-capstone-map/1.0 (Unity game; fetch_mabuhay_elevation.py)"

ORIGIN = (14.3116367, 121.0499152)      # the hall, (0, 0) of the layout (as in fetch_mabuhay_osm.py)
# The detail box of fetch_mabuhay_osm.py (west, south, east, north, metres from the hall) plus 150 m all round, so the
# levee and the land beyond it have heights too.
BOX_M = (-360.0, -495.0, 617.0, 680.0)
STEP_M = 30.0


def metres_per_degree(lat):
    p = math.radians(lat)
    east = 111412.84 * math.cos(p) - 93.5 * math.cos(3 * p) + 0.118 * math.cos(5 * p)
    north = 111132.92 - 559.82 * math.cos(2 * p) + 1.175 * math.cos(4 * p) - 0.0023 * math.cos(6 * p)
    return east, north


def grid():
    west, south, east, north = BOX_M
    nx = int(math.floor((east - west) / STEP_M)) + 1
    ny = int(math.floor((north - south) / STEP_M)) + 1
    me, mn = metres_per_degree(ORIGIN[0])
    pts = []
    for j in range(ny):
        for i in range(nx):
            x, y = west + i * STEP_M, south + j * STEP_M
            pts.append((ORIGIN[0] + y / mn, ORIGIN[1] + x / me))
    return nx, ny, pts


def ask(dataset, chunk):
    q = urllib.parse.urlencode({"locations": "|".join(f"{la:.6f},{lo:.6f}" for la, lo in chunk), "interpolation": "bilinear"})
    req = urllib.request.Request(API.format(dataset=dataset) + "?" + q, headers={"User-Agent": USER_AGENT})
    for attempt in range(5):
        try:
            with urllib.request.urlopen(req, timeout=60) as r:
                data = json.load(r)
            if data.get("status") != "OK":
                raise RuntimeError(data.get("error", "no status"))
            return [res.get("elevation") for res in data["results"]]
        except (urllib.error.URLError, RuntimeError, TimeoutError) as e:
            wait = 2 + attempt * 3
            print(f"  {dataset}: {e}; trying again in {wait} s", file=sys.stderr)
            time.sleep(wait)
    raise SystemExit(f"OpenTopoData did not answer for {dataset}.")


def fetch(pts):
    out = {}
    for dataset in DATASETS:
        todo = [k for k in range(len(pts)) if out.get(k) is None]
        if not todo:
            break
        print(f"{dataset}: {len(todo)} points", file=sys.stderr)
        for start in range(0, len(todo), 100):
            idx = todo[start:start + 100]
            heights = ask(dataset, [pts[k] for k in idx])
            for k, h in zip(idx, heights):
                if h is not None:
                    out[k] = {"h": h, "dataset": dataset}
            time.sleep(1.1)
    return [out.get(k) for k in range(len(pts))]


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--offline", action="store_true", help="rebuild from the saved raw answer")
    args = ap.parse_args()

    nx, ny, pts = grid()
    if args.offline:
        raw = json.loads(RAW.read_text())
    else:
        raw = {"fetched": time.strftime("%Y-%m-%d"), "points": fetch(pts)}
        RAW.parent.mkdir(exist_ok=True)
        RAW.write_text(json.dumps(raw))

    heights = [p["h"] if p else None for p in raw["points"]]
    missing = sum(1 for h in heights if h is None)
    known = [h for h in heights if h is not None]
    if not known:
        raise SystemExit("No heights at all.")
    fill = sum(known) / len(known)
    heights = [round(h if h is not None else fill, 2) for h in heights]

    west, south, east, north = BOX_M
    doc = {
        "about": "Ground height above sea level in metres, on a grid in metres east (x) and north (y) of Mabuhay "
                 "Barangay Hall. Row-major from the south-west corner: heights[j * nx + i] is at "
                 "(xMin + i * step, yMin + j * step).",
        "source": "SRTM 30 m (NASA/USGS, public domain), with ASTER GDEM 30 m where SRTM has no value, via the "
                  "OpenTopoData public API (opentopodata.org)",
        "fetched": raw.get("fetched", ""),
        "xMin": west, "yMin": south, "step": STEP_M, "nx": nx, "ny": ny,
        "min": min(heights), "max": max(heights), "filled": missing,
        "heights": heights,
    }
    OUT.write_text(json.dumps(doc, indent=1))
    print(f"Wrote {OUT.name}: {nx} x {ny} points, {doc['min']} to {doc['max']} m above sea level, {missing} filled.")


if __name__ == "__main__":
    main()
