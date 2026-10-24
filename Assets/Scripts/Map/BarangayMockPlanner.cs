using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>What one thing on the placeholder map is.</summary>
public enum PlacementKind
{
    /// <summary>A house prefab on a footprint, turned to face the street.</summary>
    House,
    /// <summary>A landmark prefab (the hall, a school block, a shop house) fitted to a footprint.</summary>
    Model,
    /// <summary>The real outline of a building, raised to a height and painted one colour.</summary>
    Block,
    Shed,
    /// <summary>A roof on posts, no walls.</summary>
    Canopy,
    /// <summary>The grounds of a school or a market: a flat tinted slab under its buildings.</summary>
    Grounds,
    /// <summary>A flat slab with an open roof: a basketball court.</summary>
    Court,
    /// <summary>A name written above a building.</summary>
    Sign,
    /// <summary>An orange pole and a notice: here is somewhere to evacuate to.</summary>
    Banner
}

/// <summary>One thing to put on the map, worked out but not yet made. Positions are real metres east and north of the hall.</summary>
public class MockPlacement
{
    public PlacementKind kind;
    /// <summary>The OpenStreetMap id of the building or landmark it comes from.</summary>
    public string sourceId = "";
    public LandmarkCategory category = LandmarkCategory.Other;
    /// <summary>The name of the placeholder model used, if any.</summary>
    public string modelName = "";
    /// <summary>The prefab to place. Null if the library has none: the builder then draws a block of the model's size.</summary>
    public GameObject prefab;
    /// <summary>The model's size in metres, before scaling.</summary>
    public Vector3 modelSize;
    public Vector2 position;
    /// <summary>Degrees about +Y that turn the model's front (+Z) to face the street.</summary>
    public float yaw;
    public Vector3 scale = Vector3.one;
    /// <summary>Counter-clockwise outline for blocks, grounds, canopies and courts.</summary>
    public List<Vector2> outline;
    public float height;
    public Color colour = Color.white;
    public string label = "";
}

/// <summary>Everything the generator will put on the map, and what it made of each building.</summary>
public class MockPlan
{
    public readonly List<MockPlacement> placements = new List<MockPlacement>();
    /// <summary>What the planner decided and why, one line each: how each landmark was drawn, what had no place.</summary>
    public readonly List<string> notes = new List<string>();
    /// <summary>For every building in the region, what became of it: "two-storey", "block", "replaced by ...".</summary>
    public readonly Dictionary<string, string> fates = new Dictionary<string, string>();

    public int Count(PlacementKind kind)
    {
        int n = 0;
        foreach (var p in placements) if (p.kind == kind) n++;
        return n;
    }
}

/// <summary>
/// Works out what stands where on the placeholder map. Every building in the region gets exactly one fate: a house
/// that fits its footprint and faces the street, a row of houses, a plain block, a shed, a canopy, or - when a landmark
/// claims it - part of that landmark. It reads only data, so the same layout and library always give the same plan.
/// </summary>
public static class BarangayMockPlanner
{
    public static MockPlan Plan(BarangayLayout layout, Rect region, PlaceholderLibrary library)
    {
        return new Run(layout, region, library).Execute();
    }

    /// <summary>The colour of a plain block for a kind of building.</summary>
    public static Color BlockColour(BuildingKind kind)
    {
        switch (kind)
        {
            case BuildingKind.Industrial: return new Color(0.62f, 0.63f, 0.66f);
            case BuildingKind.Warehouse:  return new Color(0.55f, 0.59f, 0.62f);
            case BuildingKind.Commercial: return new Color(0.86f, 0.80f, 0.68f);
            case BuildingKind.Retail:     return new Color(0.90f, 0.74f, 0.52f);
            case BuildingKind.Civic:      return new Color(0.70f, 0.76f, 0.86f);
            case BuildingKind.School:     return new Color(0.60f, 0.80f, 0.86f);
            case BuildingKind.Hospital:   return new Color(0.95f, 0.80f, 0.82f);
            case BuildingKind.Religious:  return new Color(0.90f, 0.88f, 0.80f);
            default:                      return new Color(0.76f, 0.73f, 0.68f);
        }
    }

    /// <summary>How tall a plain block is: its storeys if known, otherwise by kind, with a little variety.</summary>
    public static float BlockHeight(MapBuilding b)
    {
        if (b.levels > 0) return b.levels * 3.2f + 0.4f;
        float h;
        switch (b.kind)
        {
            case BuildingKind.Industrial:
            case BuildingKind.Warehouse:   h = 9f; break;
            case BuildingKind.Commercial:  h = 8f; break;
            case BuildingKind.Retail:      h = 6f; break;
            case BuildingKind.Civic:       h = 8f; break;
            case BuildingKind.School:      h = 8f; break;
            case BuildingKind.Hospital:    h = 12f; break;
            case BuildingKind.Religious:   h = 10f; break;
            case BuildingKind.Residential: h = 9f; break;
            default:                       h = 7f; break;
        }
        return h + (MapGeometry.Unit(b.osmId) - 0.5f);
    }

    // ------------------------------------------------------------------ the pieces the planner works with

    /// <summary>A building in the region, with what the planner needs to know about it.</summary>
    sealed class Site
    {
        public MapBuilding b;
        public Vector2 centre;
        public float area;
        public MapGeometry.OrientedRect rect;
        public bool claimed;
        /// <summary>Set when a landmark has taken this building as its own (a shop house or a block).</summary>
        public bool drawnAsLandmark;
        public float drawnHeight;
        public int signs;
    }

    /// <summary>Which way a building faces and how big its front is.</summary>
    sealed class Facing
    {
        public Vector2 front;
        public float frontage;
        public float depth;
        public bool onStreet;
        public RoadClass road;
    }

    /// <summary>The road segments near the region, for finding which street a building faces.</summary>
    sealed class Streets
    {
        readonly List<Vector2> a = new List<Vector2>(), b = new List<Vector2>();
        readonly List<float> penalty = new List<float>();
        readonly List<RoadClass> cls = new List<RoadClass>();

        public Streets(BarangayLayout layout, Rect near)
        {
            foreach (var road in layout.roads)
            {
                // A lane or a footpath is a poor front door: it counts as further away than it is.
                float extra = road.roadClass <= RoadClass.Track ? (road.roadClass == RoadClass.Footway ? 6f : 3f) :
                              road.roadClass == RoadClass.Service ? 3f : 0f;
                for (int i = 0; i + 1 < road.points.Count; i++)
                {
                    Vector2 p = road.points[i], q = road.points[i + 1];
                    if (Mathf.Max(p.x, q.x) < near.xMin || Mathf.Min(p.x, q.x) > near.xMax ||
                        Mathf.Max(p.y, q.y) < near.yMin || Mathf.Min(p.y, q.y) > near.yMax) continue;
                    a.Add(p); b.Add(q); penalty.Add(extra); cls.Add(road.roadClass);
                }
            }
        }

        /// <summary>The nearest street (counting lanes as further away) within reach, and the point on it.</summary>
        public bool Nearest(Vector2 p, float reach, out Vector2 closest, out RoadClass roadClass)
        {
            closest = p;
            roadClass = RoadClass.Footway;
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < a.Count; i++)
            {
                Vector2 ab = b[i] - a[i];
                float len2 = ab.sqrMagnitude;
                float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a[i], ab) / len2);
                Vector2 c = a[i] + ab * t;
                float d = (p - c).magnitude;
                if (d > reach) continue;
                float score = d + penalty[i];
                if (score < best - 1e-4f) { best = score; closest = c; roadClass = cls[i]; found = true; }
            }
            return found;
        }
    }

    // ------------------------------------------------------------------ one run of the planner

    sealed class Run
    {
        const float StreetReach = 40f;

        readonly BarangayLayout layout;
        readonly Rect region;
        readonly PlaceholderLibrary lib;
        readonly MockPlan plan = new MockPlan();
        readonly List<Site> sites = new List<Site>();
        Streets streets;
        bool warnedNoHouses;

        public Run(BarangayLayout layout, Rect region, PlaceholderLibrary library)
        {
            this.layout = layout;
            this.region = region;
            lib = library;
        }

        public MockPlan Execute()
        {
            foreach (var b in layout.BuildingsIn(region).OrderBy(b => b.osmId, StringComparer.Ordinal))
            {
                var ring = MapGeometry.CounterClockwise(b.outline);
                sites.Add(new Site
                {
                    b = b,
                    centre = MapGeometry.Centroid(ring),
                    area = MapGeometry.SignedArea(ring),
                    rect = MapGeometry.MinimumRectangle(ring)
                });
            }
            streets = new Streets(layout, Rect.MinMaxRect(region.xMin - 60f, region.yMin - 60f, region.xMax + 60f, region.yMax + 60f));

            foreach (var l in layout.landmarks.Where(l => !l.positionKnown).OrderBy(l => l.id, StringComparer.Ordinal))
                plan.notes.Add(l.Label + ": no position is known, so it is not on the map. Add one in Docs/Maps/mabuhay-overrides.json.");

            // Landmarks first, in a fixed order, so they claim their buildings before the houses are chosen.
            foreach (var l in layout.landmarks.Where(l => l.positionKnown && region.Contains(l.position)).OrderBy(l => l.id, StringComparer.Ordinal))
                PlaceLandmark(l);

            foreach (var s in sites)
                if (!s.claimed) PlaceBuilding(s);
            return plan;
        }

        // ---------------------------------------------------------------- ordinary buildings

        void PlaceBuilding(Site s)
        {
            var b = s.b;
            if (b.kind == BuildingKind.Roof)
            {
                plan.placements.Add(new MockPlacement
                {
                    kind = PlacementKind.Canopy, sourceId = b.osmId, outline = MapGeometry.CounterClockwise(b.outline),
                    height = 4f, colour = new Color(0.55f, 0.52f, 0.5f)
                });
                plan.fates[b.osmId] = "canopy";
                return;
            }

            bool heavy = b.kind == BuildingKind.Industrial || b.kind == BuildingKind.Warehouse || b.kind == BuildingKind.Commercial ||
                         b.kind == BuildingKind.Retail || b.kind == BuildingKind.Civic || b.kind == BuildingKind.School ||
                         b.kind == BuildingKind.Hospital || b.kind == BuildingKind.Religious;
            if (heavy || s.area > lib.blockArea)
            {
                AddBlock(b.osmId, MapGeometry.CounterClockwise(b.outline), BlockHeight(b), BlockColour(b.kind), LandmarkCategory.Other);
                plan.fates[b.osmId] = "block";
            }
            else if (s.area < lib.shedArea)
            {
                AddBlock(b.osmId, MapGeometry.CounterClockwise(b.outline), 2.6f, new Color(0.70f, 0.66f, 0.60f), LandmarkCategory.Other);
                plan.fates[b.osmId] = "shed";
            }
            else if (lib.houses.Count == 0)
            {
                if (!warnedNoHouses)
                {
                    plan.notes.Add("The library has no houses, so every house is a plain block. Run Bayanihan > Make Placeholder Prefabs.");
                    warnedNoHouses = true;
                }
                AddBlock(b.osmId, MapGeometry.CounterClockwise(b.outline), 6f, BlockColour(BuildingKind.House), LandmarkCategory.Other);
                plan.fates[b.osmId] = "block";
            }
            else PlaceHouses(s);
        }

        void PlaceHouses(Site s)
        {
            var id = s.b.osmId;
            var f = FaceStreet(s.rect, id);
            bool majorStreet = f.onStreet && f.road >= RoadClass.Tertiary;

            int units = 1;
            if (f.frontage > lib.rowSplit * lib.rowUnit)
                units = Mathf.Clamp(Mathf.RoundToInt(f.frontage / lib.rowUnit), 2, 6);
            float unitFrontage = f.frontage / units;
            Vector2 widthAxis = new Vector2(f.front.y, -f.front.x);     // the model's +X once it is turned to face the street

            string last = "";
            for (int i = 0; i < units; i++)
            {
                string key = units == 1 ? id : id + "#" + i;
                int vi = ChooseVariant(key, unitFrontage, f.depth, majorStreet, s.b.levels, s.area / units, out float sx, out float sz);
                var v = lib.houses[vi];
                plan.placements.Add(new MockPlacement
                {
                    kind = PlacementKind.House, sourceId = id, modelName = v.name, prefab = v.prefab, modelSize = v.size,
                    position = s.rect.centre + widthAxis * (((i + 0.5f) / units - 0.5f) * f.frontage),
                    yaw = MapGeometry.Heading(f.front),
                    scale = new Vector3(sx, Mathf.Clamp(Mathf.Sqrt(sx * sz), 0.9f, 1.25f), sz)
                });
                last = v.name;
            }
            plan.fates[id] = units == 1 ? last : units + " houses in a row";
        }

        /// <summary>
        /// The house that needs the least stretching to fill the plot. A shop is preferred on a main street and avoided
        /// on a side street, a tower is avoided on a small plot, a plot with known storeys prefers that many, and a
        /// little repeatable variety stops a whole street from being one house.
        /// </summary>
        int ChooseVariant(string key, float frontage, float depth, bool majorStreet, int levels, float area, out float sx, out float sz)
        {
            int best = 0;
            float bestCost = float.MaxValue;
            for (int i = 0; i < lib.houses.Count; i++)
            {
                var v = lib.houses[i];
                if (v.size.x <= 0.1f || v.size.z <= 0.1f) continue;
                float cost = Mathf.Abs(Mathf.Log(frontage / v.size.x)) + Mathf.Abs(Mathf.Log(depth / v.size.z));
                if (v.type == HouseType.Shop) cost += majorStreet ? -0.2f : 0.4f;
                if (v.type == HouseType.Tower && area < 45f) cost += 0.6f;
                if (levels > 0) cost += 0.3f * Mathf.Abs(v.storeys - levels);
                cost += 0.25f * MapGeometry.Unit(key + "/" + i);
                if (cost < bestCost) { bestCost = cost; best = i; }
            }
            var chosen = lib.houses[best];
            sx = Mathf.Clamp(frontage / Mathf.Max(chosen.size.x, 0.1f), lib.minStretch, lib.maxStretch);
            sz = Mathf.Clamp(depth / Mathf.Max(chosen.size.z, 0.1f), lib.minStretch, lib.maxStretch);
            return best;
        }

        // ---------------------------------------------------------------- landmarks

        void PlaceLandmark(MapLandmark l)
        {
            var style = lib.StyleFor(l.category);
            Color colour = style.colour.a > 0f ? style.colour : BarangayLayoutView.LandmarkColour(l.category);
            string label = !string.IsNullOrEmpty(style.sign) ? style.sign : l.Label;
            float signHeight;

            if (l.footprint.Count >= 3)
            {
                var fp = MapGeometry.CounterClockwise(l.footprint);
                float fArea = MapGeometry.SignedArea(fp);
                var inside = sites.Where(s => !s.claimed && MapGeometry.Contains(fp, s.centre)).ToList();
                float insideArea = inside.Sum(s => s.area);

                if (inside.Count > 0 && fArea > 1.3f * insideArea)
                {
                    // Grounds: the footprint is the whole site, and the buildings in it are the buildings.
                    plan.placements.Add(new MockPlacement
                    {
                        kind = PlacementKind.Grounds, sourceId = l.id, category = l.category, outline = fp,
                        colour = Color.Lerp(colour, new Color(0.62f, 0.66f, 0.56f), 0.65f)
                    });
                    float top = style.blockHeight;
                    foreach (var s in inside)
                    {
                        s.claimed = true;
                        top = Mathf.Max(top, PlaceInGrounds(s, l, style, colour));
                    }
                    plan.notes.Add(l.Label + ": grounds of " + Mathf.RoundToInt(fArea) + " m2 with " + inside.Count + " building" + (inside.Count == 1 ? "" : "s") + " in them.");
                    signHeight = top;
                }
                else
                {
                    foreach (var s in inside) { s.claimed = true; plan.fates[s.b.osmId] = "replaced by " + l.Label; }
                    signHeight = PlaceOn(l, style, colour, fp, MapGeometry.MinimumRectangle(fp), fArea);
                }
            }
            else
            {
                var host = FindHost(l.position);
                if (host != null && host.drawnAsLandmark)
                {
                    signHeight = host.drawnHeight;      // another landmark already drew this building: add a sign to it
                    plan.notes.Add(l.Label + ": in the same building as another landmark, so it only adds a sign.");
                }
                else if (host != null)
                {
                    host.claimed = true;
                    host.drawnAsLandmark = true;
                    var ring = MapGeometry.CounterClockwise(host.b.outline);
                    // A mall is a mall whichever shop in it is listed first: only a plain house or shed takes its tenant's colour.
                    bool plain = host.b.kind == BuildingKind.House || host.b.kind == BuildingKind.Generic ||
                                 host.b.kind == BuildingKind.Residential || host.b.kind == BuildingKind.Other;
                    signHeight = PlaceOn(l, style, colour, ring, host.rect, host.area, plain ? colour : BlockColour(host.b.kind));
                    host.drawnHeight = signHeight;
                    plan.fates[host.b.osmId] = "landmark: " + l.Label;
                }
                else signHeight = PlaceStandalone(l, style, colour);
                if (host != null) { signHeight += 2.5f * host.signs; host.signs++; }
            }

            if (label.Length > 0)
                plan.placements.Add(new MockPlacement
                {
                    kind = PlacementKind.Sign, sourceId = l.id, category = l.category, position = l.position,
                    height = signHeight + 3f, colour = colour, label = label
                });
            if (l.IsEvacuationSite)
                plan.placements.Add(new MockPlacement
                {
                    kind = PlacementKind.Banner, sourceId = l.id, category = l.category, position = l.position,
                    height = Mathf.Max(signHeight + 8f, 14f), colour = l.verified && l.evacuationRole == EvacuationRole.Designated
                        ? new Color(0.25f, 0.85f, 0.35f) : new Color(1f, 0.55f, 0.1f),
                    label = l.verified && l.evacuationRole == EvacuationRole.Designated ? "Evacuation site" : "Evacuation site (unconfirmed)"
                });
        }

        /// <summary>
        /// A landmark that is one building, with its own outline: the model if it fits, a shop house if it is small, a
        /// court if it is a court, otherwise a coloured block. Returns the height of what was drawn.
        /// </summary>
        float PlaceOn(MapLandmark l, LandmarkStyle style, Color colour, IList<Vector2> outline, MapGeometry.OrientedRect rect, float area, Color? blockColour = null)
        {
            switch (style.placement)
            {
                case LandmarkPlacement.Court:
                    plan.placements.Add(new MockPlacement
                    {
                        kind = PlacementKind.Court, sourceId = l.id, category = l.category, outline = new List<Vector2>(outline),
                        height = 5.5f, colour = colour
                    });
                    plan.notes.Add(l.Label + ": a slab with an open roof.");
                    return 5.5f;

                case LandmarkPlacement.Prefab:
                    if (style.model != null && TryFitModel(l, style.model, style, rect, out float modelHeight, l.Label)) return modelHeight;
                    break;

                case LandmarkPlacement.Parts:
                    {
                        var f = FaceStreet(rect, l.id);
                        var part = BestPart(style, l.id, f, out float px, out float pz);
                        if (part != null)
                        {
                            AddModel(l, part, rect.centre, f.front, px, pz);
                            plan.notes.Add(l.Label + ": " + part.name + " fitted at " + px.ToString("0.00") + " x " + pz.ToString("0.00") + ".");
                            return part.size.y * Mathf.Clamp(Mathf.Sqrt(px * pz), 0.7f, 1.2f);
                        }
                        break;
                    }

                case LandmarkPlacement.ShopHouse:
                    if (area <= lib.hostMaxArea)
                    {
                        var f = FaceStreet(rect, l.id);
                        var shop = BestShop(l.id, f, out float sx, out float sz);
                        if (shop != null)
                        {
                            AddModel(l, shop, rect.centre, f.front, sx, sz);
                            plan.notes.Add(l.Label + ": a shop house (" + shop.name + ") on a " + Mathf.RoundToInt(area) + " m2 building.");
                            return shop.size.y * Mathf.Clamp(Mathf.Sqrt(sx * sz), 0.9f, 1.25f);
                        }
                    }
                    break;
            }

            AddBlock(l.id, new List<Vector2>(outline), style.blockHeight, blockColour ?? colour, l.category);
            plan.notes.Add(l.Label + ": a block of its " + Mathf.RoundToInt(rect.sizeU) + " x " + Mathf.RoundToInt(rect.sizeV) + " m outline" +
                           (style.placement == LandmarkPlacement.Prefab ? ", too far from the model's size to fit it" : "") + ".");
            return style.blockHeight;
        }

        /// <summary>A landmark with no building to stand in: a shop house (or a block) of its own, facing the nearest street.</summary>
        float PlaceStandalone(MapLandmark l, LandmarkStyle style, Color colour)
        {
            var shops = lib.houses.Where(h => h.type == HouseType.Shop && h.size.x > 0.1f).ToList();
            Vector2 size = shops.Count > 0 ? new Vector2(shops[0].size.x, shops[0].size.z) : new Vector2(6f, 6f);
            var probe = new MapGeometry.OrientedRect { centre = l.position, axisU = Vector2.right, axisV = Vector2.up, sizeU = size.x, sizeV = size.y };
            var f = FaceStreet(probe, l.id);
            Vector2 width = new Vector2(f.front.y, -f.front.x);
            var rect = new MapGeometry.OrientedRect { centre = l.position, axisU = width, axisV = f.front, sizeU = size.x, sizeV = size.y };

            foreach (var s in sites)
                if (!s.claimed && rect.Contains(s.centre, 1f)) { s.claimed = true; plan.fates[s.b.osmId] = "replaced by " + l.Label; }

            if (style.placement == LandmarkPlacement.ShopHouse || style.placement == LandmarkPlacement.Prefab)
            {
                if (shops.Count > 0)
                {
                    var shop = shops[(int)(MapGeometry.StableHash(l.id) % (uint)shops.Count)];
                    AddModel(l, shop, l.position, f.front, 1f, 1f);
                    plan.notes.Add(l.Label + ": no building at its position, so a " + shop.name + " stands there facing the street.");
                    return shop.size.y;
                }
            }
            AddBlock(l.id, rect.Corners(), Mathf.Min(style.blockHeight, 8f), colour, l.category);
            plan.notes.Add(l.Label + ": no building at its position, so a " + Mathf.RoundToInt(size.x) + " x " + Mathf.RoundToInt(size.y) + " m block stands there.");
            return Mathf.Min(style.blockHeight, 8f);
        }

        /// <summary>One building inside a landmark's grounds: the best-fitting part if there are any, otherwise a block.</summary>
        float PlaceInGrounds(Site s, MapLandmark l, LandmarkStyle style, Color colour)
        {
            if (style.placement == LandmarkPlacement.Parts)
            {
                var f = FaceStreet(s.rect, s.b.osmId);
                var part = BestPart(style, s.b.osmId, f, out float px, out float pz);
                if (part != null)
                {
                    AddModel(l, part, s.rect.centre, f.front, px, pz);
                    plan.fates[s.b.osmId] = l.Label + ": " + part.name;
                    return part.size.y * Mathf.Clamp(Mathf.Sqrt(px * pz), 0.7f, 1.2f);
                }
            }
            AddBlock(s.b.osmId, MapGeometry.CounterClockwise(s.b.outline), style.blockHeight, Color.Lerp(colour, Color.white, 0.25f), l.category);
            plan.fates[s.b.osmId] = l.Label + ": block";
            return style.blockHeight;
        }

        bool TryFitModel(MapLandmark l, PlaceholderModel model, LandmarkStyle style, MapGeometry.OrientedRect rect, out float height, string what)
        {
            height = 0f;
            if (model.size.x <= 0.1f || model.size.z <= 0.1f) return false;
            var f = FaceStreet(rect, l.id);
            float sx = f.frontage / model.size.x, sz = f.depth / model.size.z;
            if (sx < style.minScale || sx > style.maxScale || sz < style.minScale || sz > style.maxScale) return false;
            AddModel(l, model, rect.centre, f.front, sx, sz);
            plan.notes.Add(what + ": " + model.name + " model fitted at " + sx.ToString("0.00") + " x " + sz.ToString("0.00") + ". Swap in the real model when it is made.");
            height = model.size.y * Mathf.Clamp(Mathf.Sqrt(sx * sz), 0.7f, 1.2f);
            return true;
        }

        PlaceholderModel BestPart(LandmarkStyle style, string key, Facing f, out float sx, out float sz)
        {
            PlaceholderModel best = null;
            float bestCost = float.MaxValue;
            sx = sz = 1f;
            for (int i = 0; i < style.parts.Count; i++)
            {
                var p = style.parts[i];
                if (p.size.x <= 0.1f || p.size.z <= 0.1f) continue;
                float x = f.frontage / p.size.x, z = f.depth / p.size.z;
                if (x < style.minScale || x > style.maxScale || z < style.minScale || z > style.maxScale) continue;
                float cost = Mathf.Abs(Mathf.Log(x)) + Mathf.Abs(Mathf.Log(z)) + 0.25f * MapGeometry.Unit(key + "/" + i);
                if (cost < bestCost) { bestCost = cost; best = p; sx = x; sz = z; }
            }
            return best;
        }

        HouseVariant BestShop(string key, Facing f, out float sx, out float sz)
        {
            HouseVariant best = null;
            float bestCost = float.MaxValue;
            sx = sz = 1f;
            for (int i = 0; i < lib.houses.Count; i++)
            {
                var v = lib.houses[i];
                if (v.type != HouseType.Shop || v.size.x <= 0.1f || v.size.z <= 0.1f) continue;
                float x = f.frontage / v.size.x, z = f.depth / v.size.z;
                if (x < lib.minStretch || x > lib.maxStretch || z < lib.minStretch || z > lib.maxStretch) continue;
                float cost = Mathf.Abs(Mathf.Log(x)) + Mathf.Abs(Mathf.Log(z)) + 0.25f * MapGeometry.Unit(key + "/" + i);
                if (cost < bestCost) { bestCost = cost; best = v; sx = x; sz = z; }
            }
            return best;
        }

        /// <summary>The building a shop node is in, or the nearest one within reach (shops often sit on the pavement).</summary>
        Site FindHost(Vector2 p)
        {
            Site best = null;
            float bestDistance = float.MaxValue;
            foreach (var s in sites)
            {
                if (s.area < lib.shedArea) continue;            // a shed is not somewhere to run a shop
                if (!s.rect.Contains(p, lib.hostReach + 1f)) continue;
                float d = MapGeometry.Contains(s.b.outline, p) ? 0f : DistanceToRing(s.b.outline, p);
                if (d > lib.hostReach) continue;
                // Inside two buildings, the smaller one is the one that is meant.
                if (d < bestDistance - 1e-3f || (Mathf.Abs(d - bestDistance) <= 1e-3f && best != null && s.area < best.area))
                {
                    best = s;
                    bestDistance = d;
                }
            }
            return best;
        }

        static float DistanceToRing(IList<Vector2> ring, Vector2 p)
        {
            var closed = new List<Vector2>(ring) { ring[0] };
            return MapGeometry.DistanceToPolyline(closed, p, out _);
        }

        // ---------------------------------------------------------------- facing the street

        /// <summary>
        /// Which way a building faces: towards the nearest street, as the best of the four sides of its rectangle. The
        /// frontage is the side that faces the street, the depth the other. With no street near, the long side faces
        /// one way or the other, chosen by the building's id so the same plan is made every time.
        /// </summary>
        Facing FaceStreet(MapGeometry.OrientedRect rect, string id)
        {
            var f = new Facing();
            Vector2 toStreet = Vector2.zero;
            if (streets.Nearest(rect.centre, StreetReach, out Vector2 closest, out RoadClass road))
            {
                toStreet = closest - rect.centre;
                f.onStreet = true;
                f.road = road;
            }
            if (toStreet.sqrMagnitude > 0.25f)
            {
                Vector2 dir = toStreet.normalized;
                Vector2[] sides = { rect.axisU, -rect.axisU, rect.axisV, -rect.axisV };
                float bestDot = float.MinValue;
                foreach (var side in sides)
                {
                    float dot = Vector2.Dot(side, dir);
                    if (dot > bestDot + 1e-4f) { bestDot = dot; f.front = side; }
                }
            }
            else
            {
                f.onStreet = false;
                f.front = MapGeometry.Unit(id) < 0.5f ? rect.axisV : -rect.axisV;
            }
            bool frontIsAlongU = Mathf.Abs(Vector2.Dot(f.front, rect.axisU)) > 0.5f;
            f.frontage = frontIsAlongU ? rect.sizeV : rect.sizeU;
            f.depth = frontIsAlongU ? rect.sizeU : rect.sizeV;
            return f;
        }

        // ---------------------------------------------------------------- adding things

        void AddBlock(string id, List<Vector2> outline, float height, Color colour, LandmarkCategory category)
        {
            plan.placements.Add(new MockPlacement
            {
                kind = height <= 3f ? PlacementKind.Shed : PlacementKind.Block, sourceId = id, category = category,
                outline = outline, height = height, colour = colour
            });
        }

        void AddModel(MapLandmark l, PlaceholderModel model, Vector2 position, Vector2 front, float sx, float sz)
        {
            plan.placements.Add(new MockPlacement
            {
                kind = PlacementKind.Model, sourceId = l.id, category = l.category, modelName = model.name, prefab = model.prefab,
                modelSize = model.size, position = position, yaw = MapGeometry.Heading(front),
                scale = new Vector3(sx, Mathf.Clamp(Mathf.Sqrt(sx * sz), 0.7f, 1.25f), sz)
            });
        }
    }
}
