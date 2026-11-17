using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One plant on the map: which species, where (metres east, north), turned and scaled how.</summary>
[Serializable]
public struct FoliagePlacement
{
    public int species;
    public Vector2 position;
    public float yaw;
    public float scale;
    /// <summary>A small lean off upright, degrees about x and z.</summary>
    public float leanX, leanZ;
    public FoliageHabitat habitat;
}

/// <summary>A circle no plant may stand in: the boat's start, the hall's landing, a resident, a hazard.</summary>
public struct KeepClear
{
    public Vector2 centre;
    public float radius;
    public KeepClear(Vector2 centre, float radius) { this.centre = centre; this.radius = radius; }
}

public class FoliagePlan
{
    public readonly List<FoliagePlacement> placements = new List<FoliagePlacement>();
    public readonly Dictionary<FoliageHabitat, int> perHabitat = new Dictionary<FoliageHabitat, int>();
    public readonly Dictionary<FoliageGroup, int> perGroup = new Dictionary<FoliageGroup, int>();

    /// <summary>
    /// Points of open ground (no building, street, water or mission spot), one every <see cref="FoliageLibrary.grasslandGap"/>
    /// metres: where the low grass that fills every bare patch is grown (FoliageBuilder draws it as blades).
    /// </summary>
    public readonly List<Vector2> openGround = new List<Vector2>();
    public int Grassland;

    public int Count(FoliageGroup g) => perGroup.TryGetValue(g, out int n) ? n : 0;
    public int Count(FoliageHabitat h) => perHabitat.TryGetValue(h, out int n) ? n : 0;
}

/// <summary>
/// Plants the barangay. Works on a one-metre grid over the map: buildings, roads, water and the mission's clear zones
/// are marked off, every other cell is given a habitat (a green area, farmland, a creek bank, a roadside, a yard or an
/// open lot), and plants are scattered over it with natural clumps and gaps, each species where it belongs.
///
/// Like <see cref="BarangayMockPlanner"/> it reads only data: the same layout, library and seed always give the same
/// plants in the same places, so the map can be rebuilt at any time.
/// </summary>
public static class FoliagePlanner
{
    const float Cell = 1f;
    const float Step = 3f;            // the scatter's sample spacing

    enum CellKind : byte { Free, Building, Road, Water }

    sealed class Grid
    {
        public Rect area;
        public int w, h;
        public CellKind[] kind;
        public bool[] keepClear;
        public sbyte[] areaKind;
        public ushort[] distBuilding, distRoad, distWater;

        public Grid(Rect area)
        {
            this.area = area;
            w = Mathf.Max(1, Mathf.CeilToInt(area.width / Cell));
            h = Mathf.Max(1, Mathf.CeilToInt(area.height / Cell));
            int n = w * h;
            kind = new CellKind[n];
            keepClear = new bool[n];
            areaKind = new sbyte[n];
            for (int i = 0; i < n; i++) areaKind[i] = -1;
        }

        public bool Index(Vector2 p, out int i)
        {
            int x = Mathf.FloorToInt((p.x - area.xMin) / Cell), y = Mathf.FloorToInt((p.y - area.yMin) / Cell);
            i = y * w + x;
            return x >= 0 && y >= 0 && x < w && y < h;
        }

        public Vector2 Centre(int x, int y) => new Vector2(area.xMin + (x + 0.5f) * Cell, area.yMin + (y + 0.5f) * Cell);
    }

    /// <param name="region">The ground to plant: the whole map out to the levee.</param>
    /// <param name="drawn">Where buildings are drawn (null = everywhere). Past it the layout's buildings are not on the map,
    /// so their ground is open and planted like any other.</param>
    public static FoliagePlan Plan(BarangayLayout layout, Rect region, MockPlan mock, PlaceholderLibrary lib, FoliageLibrary flora,
                                   IList<KeepClear> keepClear, Rect? drawn = null, IList<(Vector2 centre, Vector2 half, float yaw)> extraBuildings = null)
    {
        var plan = new FoliagePlan();
        if (layout == null || flora == null || flora.species.Count == 0) return plan;

        var g = new Grid(region);
        MarkBuildings(g, layout, mock, drawn);
        if (extraBuildings != null)
            foreach (var b in extraBuildings) FillRotatedBox(g, b.centre, b.half, b.yaw, i => g.kind[i] = CellKind.Building);
        MarkRoads(g, layout, lib);
        MarkWater(g, layout, lib);
        MarkAreas(g, layout);
        if (keepClear != null)
            foreach (var k in keepClear) Stamp(g, k.centre, k.radius, i => g.keepClear[i] = true);

        g.distBuilding = Distance(g, CellKind.Building);
        g.distRoad = Distance(g, CellKind.Road);
        g.distWater = Distance(g, CellKind.Water);

        var random = new System.Random(flora.seed);
        var placed = new SpatialHash(8f);
        var last = new Dictionary<FoliageHabitat, int>();

        PlantRoadsides(g, layout, lib, flora, random, placed, plan);
        PlantWalls(g, mock, flora, random, placed, plan);
        Scatter(g, flora, random, placed, last, plan);
        FillGrassland(g, flora, random, placed, plan);
        return plan;
    }

    // ------------------------------------------------------------------ what each cell is

    static void MarkBuildings(Grid g, BarangayLayout layout, MockPlan mock, Rect? drawn)
    {
        foreach (var b in layout.buildings)
        {
            if (b.outline == null || b.outline.Count < 3) continue;
            if (drawn.HasValue && !drawn.Value.Contains(MapGeometry.Centroid(b.outline))) continue;
            FillPolygon(g, b.outline, i => g.kind[i] = CellKind.Building);
        }
        if (mock == null) return;
        foreach (var p in mock.placements)
        {
            switch (p.kind)
            {
                case PlacementKind.House:
                case PlacementKind.Model:
                    // A model may be bigger than the outline it stands on (a landmark scaled to its lot).
                    var half = new Vector2(p.modelSize.x * p.scale.x, p.modelSize.z * p.scale.z) * 0.5f;
                    FillRotatedBox(g, p.position, half, p.yaw, i => g.kind[i] = CellKind.Building);
                    break;
                case PlacementKind.Block:
                case PlacementKind.Shed:
                case PlacementKind.Court:
                case PlacementKind.Canopy:
                    if (p.outline != null && p.outline.Count >= 3) FillPolygon(g, p.outline, i => g.kind[i] = CellKind.Building);
                    break;
                case PlacementKind.Sign:
                case PlacementKind.Banner:
                    Stamp(g, p.position, 1.5f, i => g.kind[i] = CellKind.Building);
                    break;
            }
        }
    }

    static void MarkRoads(Grid g, BarangayLayout layout, PlaceholderLibrary lib)
    {
        foreach (var r in layout.roads)
        {
            float half = (lib != null ? lib.roadWidths.For(r.roadClass) : 6f) * 0.5f;
            StampLine(g, r.points, half, i => { if (g.kind[i] == CellKind.Free) g.kind[i] = CellKind.Road; });
        }
    }

    static void MarkWater(Grid g, BarangayLayout layout, PlaceholderLibrary lib)
    {
        foreach (var w in layout.waterways)
        {
            float half = (lib != null ? lib.WaterWidth(w.kind) : 3f) * 0.5f;
            StampLine(g, w.points, half, i => { if (g.kind[i] == CellKind.Free) g.kind[i] = CellKind.Water; });
        }
        foreach (var a in layout.areas)
            if (a.kind == AreaKind.Water && a.outline.Count >= 3)
                FillPolygon(g, a.outline, i => { if (g.kind[i] == CellKind.Free) g.kind[i] = CellKind.Water; });
    }

    static void MarkAreas(Grid g, BarangayLayout layout)
    {
        foreach (var a in layout.areas)
        {
            if (a.kind != AreaKind.Green && a.kind != AreaKind.Farmland && a.kind != AreaKind.Cemetery) continue;
            if (a.outline.Count < 3) continue;
            sbyte k = (sbyte)a.kind;
            FillPolygon(g, a.outline, i => g.areaKind[i] = k);
        }
    }

    /// <summary>What a free cell is, or None if nothing may grow there.</summary>
    static FoliageHabitat HabitatOf(Grid g, int i, FoliageLibrary flora)
    {
        if (g.kind[i] != CellKind.Free || g.keepClear[i]) return FoliageHabitat.None;
        if (g.distBuilding[i] <= Mathf.CeilToInt(flora.buildingMargin)) return FoliageHabitat.None;
        if (g.distRoad[i] <= Mathf.CeilToInt(flora.roadMargin)) return FoliageHabitat.None;
        if (g.distWater[i] <= 1) return FoliageHabitat.None;

        switch ((AreaKind)g.areaKind[i])
        {
            case AreaKind.Green: return FoliageHabitat.Green;
            case AreaKind.Farmland: return FoliageHabitat.Farm;
            case AreaKind.Cemetery: return FoliageHabitat.Cemetery;
        }
        if (g.distWater[i] <= flora.bankReach) return FoliageHabitat.Bank;
        if (g.distRoad[i] <= flora.roadMargin + flora.roadsideBand) return FoliageHabitat.Roadside;
        if (g.distBuilding[i] <= flora.yardReach) return FoliageHabitat.Yard;
        return FoliageHabitat.OpenLot;
    }

    // ------------------------------------------------------------------ planting

    /// <summary>Shade trees along the bigger roads, a random gap apart, on alternate sides.</summary>
    static void PlantRoadsides(Grid g, BarangayLayout layout, PlaceholderLibrary lib, FoliageLibrary flora, System.Random random,
                               SpatialHash placed, FoliagePlan plan)
    {
        var trees = new List<int>();
        foreach (int s in flora.StandingIn(FoliageHabitat.Roadside))
        {
            var grp = flora.species[s].group;
            if (grp == FoliageGroup.CanopyTree || grp == FoliageGroup.SmallTree) trees.Add(s);
        }
        if (trees.Count == 0) return;

        foreach (var r in layout.roads)
        {
            if (r.roadClass < RoadClass.Residential || r.points.Count < 2) continue;
            float half = (lib != null ? lib.roadWidths.For(r.roadClass) : 6f) * 0.5f;
            float offset = half + flora.roadMargin + 1.5f;
            float next = Range(random, flora.roadsideTreeGap) * 0.5f;
            int side = random.Next(2) == 0 ? 1 : -1;
            float travelled = 0f;
            for (int k = 1; k < r.points.Count; k++)
            {
                Vector2 a = r.points[k - 1], b = r.points[k];
                float len = Vector2.Distance(a, b);
                if (len < 0.01f) continue;
                Vector2 dir = (b - a) / len, normal = new Vector2(-dir.y, dir.x);
                while (next <= travelled + len)
                {
                    Vector2 at = a + dir * (next - travelled) + normal * offset * side;
                    TryPlant(g, flora, random, placed, plan, at, trees, FoliageHabitat.Roadside, null);
                    side = -side;
                    next += Range(random, flora.roadsideTreeGap) / flora.density.Clamp(0.25f, 3f);
                }
                travelled += len;
            }
        }
    }

    /// <summary>Vine curtains against some of the long blank walls of the blocks.</summary>
    static void PlantWalls(Grid g, MockPlan mock, FoliageLibrary flora, System.Random random, SpatialHash placed, FoliagePlan plan)
    {
        if (mock == null) return;
        var vines = new List<int>();
        for (int s = 0; s < flora.species.Count; s++)
            if (flora.species[s].prefab != null && (flora.species[s].habitats & FoliageHabitat.Wall) != 0) vines.Add(s);
        if (vines.Count == 0) return;

        foreach (var p in mock.placements)
        {
            if (p.kind != PlacementKind.Block || p.outline == null || p.outline.Count < 3) continue;
            var ring = MapGeometry.CounterClockwise(p.outline);
            for (int k = 0; k < ring.Count; k++)
            {
                Vector2 a = ring[k], b = ring[(k + 1) % ring.Count];
                float len = Vector2.Distance(a, b);
                if (len < 8f || random.NextDouble() > flora.wallVineChance) continue;
                Vector2 dir = (b - a) / len, outward = new Vector2(dir.y, -dir.x);     // a counter-clockwise ring's outside is on the right
                Vector2 at = (a + b) * 0.5f + outward * 0.8f;
                if (!g.Index(at, out int i) || g.kind[i] == CellKind.Road || g.kind[i] == CellKind.Water || g.keepClear[i]) continue;
                if (g.distRoad[i] <= Mathf.CeilToInt(flora.roadMargin)) continue;
                int s = vines[random.Next(vines.Count)];
                var sp = flora.species[s];
                if (placed.AnyWithin(at, sp.Spacing)) continue;
                Add(plan, placed, flora, new FoliagePlacement
                {
                    species = s, position = at, yaw = MapGeometry.Heading(dir), scale = Range(random, sp.scale) * 0.95f,
                    habitat = FoliageHabitat.Wall
                }, sp.Spacing);
            }
        }
    }

    /// <summary>The main planting: samples every 3 m, kept or skipped by the place's density and the clumping.</summary>
    static void Scatter(Grid g, FoliageLibrary flora, System.Random random, SpatialHash placed, Dictionary<FoliageHabitat, int> last, FoliagePlan plan)
    {
        var byHabitat = new Dictionary<FoliageHabitat, List<int>>();
        foreach (FoliageHabitat h in new[] { FoliageHabitat.Green, FoliageHabitat.Farm, FoliageHabitat.Cemetery, FoliageHabitat.Bank,
                                             FoliageHabitat.OpenLot, FoliageHabitat.Yard, FoliageHabitat.Roadside })
            byHabitat[h] = flora.StandingIn(h);

        float noiseX = (flora.seed % 997) * 1.37f, noiseY = (flora.seed % 613) * 2.11f;
        int cols = Mathf.CeilToInt(g.area.width / Step), rows = Mathf.CeilToInt(g.area.height / Step);
        for (int row = 0; row < rows; row++)
        for (int col = 0; col < cols; col++)
        {
            // jittered inside its 3 m square, so the plants never line up
            var at = new Vector2(g.area.xMin + (col + (float)random.NextDouble()) * Step,
                                 g.area.yMin + (row + (float)random.NextDouble()) * Step);
            double roll = random.NextDouble();
            if (!g.Index(at, out int i)) continue;
            var h = HabitatOf(g, i, flora);
            if (h == FoliageHabitat.None) continue;

            float n = Mathf.PerlinNoise(noiseX + at.x / flora.clumpSize, noiseY + at.y / flora.clumpSize);
            float clump = Mathf.Lerp(1f, Mathf.Clamp01((n - 0.3f) * 2.2f), flora.clumpContrast);
            float chance = flora.DensityFor(h) * flora.density * clump;
            if (roll > chance) continue;

            var pool = byHabitat[h];
            if (pool.Count == 0) continue;
            last.TryGetValue(h, out int previous);
            if (TryPlant(g, flora, random, placed, plan, at, pool, h, pool.Count > 1 ? previous : -1, out int planted)) last[h] = planted;
        }
    }

    /// <summary>
    /// No bare ground: wherever nothing grows within <see cref="FoliageLibrary.grasslandGap"/> of an open spot, a stand of the
    /// tall cogon and talahib that rises above the flood - now and then a bush, more rarely a tree - and every open spot is
    /// noted for the low grass drawn under it all.
    /// </summary>
    static void FillGrassland(Grid g, FoliageLibrary flora, System.Random random, SpatialHash placed, FoliagePlan plan)
    {
        float gap = Mathf.Max(1.5f, flora.grasslandGap);
        var grass = new List<int>();
        var bushes = new List<int>();
        var trees = new List<int>();
        for (int s = 0; s < flora.species.Count; s++)
        {
            var sp = flora.species[s];
            if (sp.prefab == null || sp.weight <= 0f || sp.size.y < flora.minStandingHeight) continue;
            if (sp.group == FoliageGroup.TallGrass) grass.Add(s);
            else if (sp.group == FoliageGroup.Bush) bushes.Add(s);
            else if ((sp.group == FoliageGroup.SmallTree || sp.group == FoliageGroup.Banana) && (sp.habitats & FoliageHabitat.OpenLot) != 0) trees.Add(s);
        }
        if (grass.Count == 0) return;

        int cols = Mathf.CeilToInt(g.area.width / gap), rows = Mathf.CeilToInt(g.area.height / gap);
        for (int row = 0; row < rows; row++)
        for (int col = 0; col < cols; col++)
        {
            var at = new Vector2(g.area.xMin + (col + 0.15f + 0.7f * (float)random.NextDouble()) * gap,
                                 g.area.yMin + (row + 0.15f + 0.7f * (float)random.NextDouble()) * gap);
            double roll = random.NextDouble();
            if (!g.Index(at, out int i)) continue;
            // Low grass grows right up to a path, a kerb or a wall; the bigger stands keep the usual room. A square only
            // partly open (beside a street or a house) gets its grass on the open part: a few spots in it are tried.
            for (int tryAt = 0; tryAt < 5; tryAt++)
            {
                var spot = tryAt == 0 ? at : new Vector2(g.area.xMin + (col + (float)random.NextDouble()) * gap, g.area.yMin + (row + (float)random.NextDouble()) * gap);
                if (g.Index(spot, out int si) && OpenForGrass(g, si)) { plan.openGround.Add(spot); break; }
            }
            var h = HabitatOf(g, i, flora);
            if (h == FoliageHabitat.None) continue;
            if (placed.AnyWithin(at, gap * 0.8f)) continue;

            var pool = roll < flora.grasslandTreeShare && trees.Count > 0 ? trees
                     : roll < flora.grasslandTreeShare + flora.grasslandBushShare && bushes.Count > 0 ? bushes : grass;
            if (TryPlant(g, flora, random, placed, plan, at, pool, h, -1, out _)) plan.Grassland++;
        }
    }

    /// <summary>Open ground low grass can grow on: not built on, paved, water or kept clear, a metre off any of them.</summary>
    static bool OpenForGrass(Grid g, int i) =>
        g.kind[i] == CellKind.Free && !g.keepClear[i] && g.distRoad[i] >= 1 && g.distBuilding[i] >= 1 && g.distWater[i] >= 2;

    static bool TryPlant(Grid g, FoliageLibrary flora, System.Random random, SpatialHash placed, FoliagePlan plan, Vector2 at,
                         List<int> pool, FoliageHabitat habitat, int? avoid) =>
        TryPlant(g, flora, random, placed, plan, at, pool, habitat, avoid ?? -1, out _);

    static bool TryPlant(Grid g, FoliageLibrary flora, System.Random random, SpatialHash placed, FoliagePlan plan, Vector2 at,
                         List<int> pool, FoliageHabitat habitat, int avoid, out int planted)
    {
        planted = -1;
        if (!g.Index(at, out int i) || HabitatOf(g, i, flora) == FoliageHabitat.None) return false;

        int s = PickWeighted(flora, pool, random, avoid);
        if (s < 0) return false;
        var sp = flora.species[s];
        float scale = Range(random, sp.scale) * flora.GameScale(sp);

        // A big crown needs room from the walls, or it grows through the roof next door.
        bool big = sp.group == FoliageGroup.CanopyTree || sp.group == FoliageGroup.Bamboo;
        if (big && g.distBuilding[i] < Mathf.Min(sp.size.x, sp.size.z) * scale * 0.35f) return false;

        float spacing = sp.Spacing * scale;
        if (placed.AnyWithin(at, spacing)) return false;

        float lean = sp.maxLean;
        Add(plan, placed, flora, new FoliagePlacement
        {
            species = s, position = at, yaw = (float)random.NextDouble() * 360f, scale = scale,
            leanX = ((float)random.NextDouble() * 2f - 1f) * lean, leanZ = ((float)random.NextDouble() * 2f - 1f) * lean,
            habitat = habitat
        }, spacing);
        planted = s;
        return true;
    }

    static void Add(FoliagePlan plan, SpatialHash placed, FoliageLibrary flora, FoliagePlacement p, float spacing)
    {
        plan.placements.Add(p);
        placed.Add(p.position, spacing);
        plan.perHabitat.TryGetValue(p.habitat, out int nh);
        plan.perHabitat[p.habitat] = nh + 1;
        var grp = flora.species[p.species].group;
        plan.perGroup.TryGetValue(grp, out int ng);
        plan.perGroup[grp] = ng + 1;
    }

    static int PickWeighted(FoliageLibrary flora, List<int> pool, System.Random random, int avoid)
    {
        float total = 0f;
        foreach (int s in pool) if (s != avoid) total += flora.species[s].weight;
        if (total <= 0f) return -1;
        double pick = random.NextDouble() * total;
        foreach (int s in pool)
        {
            if (s == avoid) continue;
            pick -= flora.species[s].weight;
            if (pick <= 0) return s;
        }
        return pool[pool.Count - 1] != avoid ? pool[pool.Count - 1] : pool[0];
    }

    static float Range(System.Random random, Vector2 range) => Mathf.Lerp(range.x, range.y, (float)random.NextDouble());

    static float Clamp(this float v, float min, float max) => Mathf.Clamp(v, min, max);

    // ------------------------------------------------------------------ the grid

    static void FillPolygon(Grid g, IList<Vector2> ring, Action<int> mark)
    {
        var b = MapGeometry.BoundsOf(ring);
        ForCellsIn(g, b, (x, y, i) => { if (MapGeometry.Contains(ring, g.Centre(x, y))) mark(i); });
    }

    static void FillRotatedBox(Grid g, Vector2 centre, Vector2 half, float yawDegrees, Action<int> mark)
    {
        float r = Mathf.Deg2Rad * yawDegrees;
        // Unity's yaw turns +z (north) towards +x (east): local x runs (cos, -sin), local z runs (sin, cos).
        Vector2 u = new Vector2(Mathf.Cos(r), -Mathf.Sin(r)), v = new Vector2(Mathf.Sin(r), Mathf.Cos(r));
        float reach = half.magnitude;
        var b = new Rect(centre.x - reach, centre.y - reach, reach * 2f, reach * 2f);
        ForCellsIn(g, b, (x, y, i) =>
        {
            Vector2 d = g.Centre(x, y) - centre;
            if (Mathf.Abs(Vector2.Dot(d, u)) <= half.x && Mathf.Abs(Vector2.Dot(d, v)) <= half.y) mark(i);
        });
    }

    static void Stamp(Grid g, Vector2 centre, float radius, Action<int> mark)
    {
        var b = new Rect(centre.x - radius, centre.y - radius, radius * 2f, radius * 2f);
        ForCellsIn(g, b, (x, y, i) => { if ((g.Centre(x, y) - centre).sqrMagnitude <= radius * radius) mark(i); });
    }

    static void StampLine(Grid g, IList<Vector2> line, float half, Action<int> mark)
    {
        for (int k = 1; k < line.Count; k++)
        {
            Vector2 a = line[k - 1], c = line[k];
            var b = Rect.MinMaxRect(Mathf.Min(a.x, c.x) - half, Mathf.Min(a.y, c.y) - half, Mathf.Max(a.x, c.x) + half, Mathf.Max(a.y, c.y) + half);
            ForCellsIn(g, b, (x, y, i) => { if (DistanceToSegment(g.Centre(x, y), a, c) <= half) mark(i); });
        }
    }

    static void ForCellsIn(Grid g, Rect b, Action<int, int, int> each)
    {
        int x0 = Mathf.Max(0, Mathf.FloorToInt((b.xMin - g.area.xMin) / Cell)), x1 = Mathf.Min(g.w - 1, Mathf.CeilToInt((b.xMax - g.area.xMin) / Cell));
        int y0 = Mathf.Max(0, Mathf.FloorToInt((b.yMin - g.area.yMin) / Cell)), y1 = Mathf.Min(g.h - 1, Mathf.CeilToInt((b.yMax - g.area.yMin) / Cell));
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
            each(x, y, y * g.w + x);
    }

    static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector2.Distance(p, a + ab * t);
    }

    /// <summary>Cells to the nearest cell of a kind (a breadth-first sweep in eight directions; metres at one cell per metre).</summary>
    static ushort[] Distance(Grid g, CellKind of)
    {
        var d = new ushort[g.w * g.h];
        var queue = new Queue<int>();
        for (int i = 0; i < d.Length; i++)
        {
            if (g.kind[i] == of) { d[i] = 0; queue.Enqueue(i); }
            else d[i] = ushort.MaxValue;
        }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            int x = i % g.w, y = i / g.w;
            ushort next = (ushort)(d[i] + 1);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= g.w || ny >= g.h) continue;
                int j = ny * g.w + nx;
                if (d[j] <= next) continue;
                d[j] = next;
                queue.Enqueue(j);
            }
        }
        return d;
    }

    /// <summary>The plants placed so far, bucketed by position, for the spacing check.</summary>
    sealed class SpatialHash
    {
        readonly float size;
        readonly Dictionary<long, List<(Vector2 p, float r)>> cells = new Dictionary<long, List<(Vector2, float)>>();
        float largest;

        public SpatialHash(float size) { this.size = size; }

        long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

        public void Add(Vector2 p, float radius)
        {
            long k = Key(Mathf.FloorToInt(p.x / size), Mathf.FloorToInt(p.y / size));
            if (!cells.TryGetValue(k, out var list)) cells[k] = list = new List<(Vector2, float)>();
            list.Add((p, radius));
            largest = Mathf.Max(largest, radius);
        }

        /// <summary>True if a plant already stands closer than either one's spacing.</summary>
        public bool AnyWithin(Vector2 p, float radius)
        {
            float reach = Mathf.Max(radius, largest);
            int x0 = Mathf.FloorToInt((p.x - reach) / size), x1 = Mathf.FloorToInt((p.x + reach) / size);
            int y0 = Mathf.FloorToInt((p.y - reach) / size), y1 = Mathf.FloorToInt((p.y + reach) / size);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                if (!cells.TryGetValue(Key(x, y), out var list)) continue;
                foreach (var (q, r) in list)
                {
                    float need = Mathf.Max(radius, r);
                    if ((q - p).sqrMagnitude < need * need) return true;
                }
            }
            return false;
        }
    }
}
