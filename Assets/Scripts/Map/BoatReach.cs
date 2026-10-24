using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where a boat can go. The water is flat and everything that is not a wall is open, so this lays a grid over the
/// flood and asks the physics engine, cell by cell, whether a hull-sized disc at the waterline fits without touching a
/// fixed collider. From a start point it floods outwards through the clear cells; a place is reachable if the flood got
/// there. It reads only the colliders in the loaded scene, so it works the same on a placeholder map and on the final
/// one, and it knows nothing of how either looks.
///
/// It is the check that every resident in a level can be got to, and the route a test or a bot drives along.
/// </summary>
public sealed class BoatReach
{
    /// <summary>Half the boat's width plus a margin: a corridor narrower than twice this is closed to the boat.</summary>
    public const float DefaultRadius = 1.5f;

    public Rect Area { get; }
    public float Cell { get; }
    public float Radius { get; }
    public int Columns { get; }
    public int Rows { get; }
    /// <summary>Cells the flood has reached.</summary>
    public int ReachedCount { get; private set; }
    /// <summary>
    /// Optional: places that are open water but not somewhere to go (live water, say). Asked afresh every time, never remembered, so
    /// it can change from one flood to the next.
    /// </summary>
    public System.Func<Vector2, bool> Avoid;

    const byte Unknown = 0, Clear = 1, Blocked = 2;

    readonly byte[] state;          // what the physics engine said about each cell
    readonly bool[] reached;
    readonly int[] cameFrom;        // for walking a route back to the start
    readonly float[] along;         // how far each reached cell is from the start by water: the shortest way there
    readonly float waterY;
    readonly int mask;
    readonly Collider[] hits = new Collider[32];
    int startCell = -1;

    public BoatReach(Rect area, float cell = 1f, float radius = DefaultRadius, float waterY = 0f, int layerMask = ~0)
    {
        Area = area;
        Cell = Mathf.Max(0.25f, cell);
        Radius = radius;
        this.waterY = waterY;
        mask = layerMask;
        Columns = Mathf.CeilToInt(area.width / Cell);
        Rows = Mathf.CeilToInt(area.height / Cell);
        state = new byte[Columns * Rows];
        reached = new bool[Columns * Rows];
        cameFrom = new int[Columns * Rows];
        along = new float[Columns * Rows];
        for (int i = 0; i < along.Length; i++) along[i] = float.PositiveInfinity;
    }

    // ------------------------------------------------------------------ the grid

    public bool InArea(Vector2 xz) => xz.x >= Area.xMin && xz.x < Area.xMax && xz.y >= Area.yMin && xz.y < Area.yMax;

    int CellOf(Vector2 xz)
    {
        int c = Mathf.Clamp(Mathf.FloorToInt((xz.x - Area.xMin) / Cell), 0, Columns - 1);
        int r = Mathf.Clamp(Mathf.FloorToInt((xz.y - Area.yMin) / Cell), 0, Rows - 1);
        return r * Columns + c;
    }

    public Vector2 CentreOf(int index)
    {
        int c = index % Columns, r = index / Columns;
        return new Vector2(Area.xMin + (c + 0.5f) * Cell, Area.yMin + (r + 0.5f) * Cell);
    }

    /// <summary>Does a hull-sized disc at the waterline fit here, touching nothing fixed? (Asks the physics engine, once per cell.)</summary>
    public bool IsClear(Vector2 xz) => IsClear(CellOf(xz));

    bool IsClear(int index)
    {
        if (state[index] == Unknown) state[index] = Test(CentreOf(index)) ? Clear : Blocked;
        if (state[index] != Clear) return false;
        return Avoid == null || !Avoid(CentreOf(index));
    }

    bool Test(Vector2 xz)
    {
        // A flat box a little deeper than the hull, centred on the waterline: it meets walls and posts, not the road under the flood.
        var centre = new Vector3(xz.x, waterY, xz.y);
        var half = new Vector3(Radius, 0.18f, Radius);
        int n = Physics.OverlapBoxNonAlloc(centre, half, hits, Quaternion.identity, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var c = hits[i];
            if (c == null) continue;
            if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue;      // the boat, the swimmer, wreckage: they move
            if (c.GetComponent<WaterSurface>() != null) continue;                                // the flood's own plane is not a wall
            return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ flooding

    /// <summary>
    /// Floods outwards from the nearest clear cell to <paramref name="start"/> (within 6 m of it), always taking the cell that is
    /// nearest the start by water next, so every distance it records is the shortest way there. Returns how many cells were reached;
    /// 0 means the start itself is hemmed in.
    /// </summary>
    public int Flood(Vector3 start)
    {
        System.Array.Clear(reached, 0, reached.Length);
        for (int i = 0; i < along.Length; i++) along[i] = float.PositiveInfinity;
        ReachedCount = 0;
        startCell = NearestClear(new Vector2(start.x, start.z), 6f);
        if (startCell < 0) return 0;

        var open = new MinHeap(1024);
        along[startCell] = 0f;
        cameFrom[startCell] = -1;
        open.Push(0f, startCell);

        while (open.Count > 0)
        {
            int at = open.Pop(out float cost);
            if (reached[at] || cost > along[at]) continue;          // an older, longer way to a cell already settled
            reached[at] = true;
            ReachedCount++;

            int c = at % Columns, r = at / Columns;
            for (int dr = -1; dr <= 1; dr++)
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    int nc = c + dc, nr = r + dr;
                    if (nc < 0 || nr < 0 || nc >= Columns || nr >= Rows) continue;
                    int next = nr * Columns + nc;
                    if (reached[next] || !IsClear(next)) continue;
                    // A diagonal step must not cut a corner: both cells beside it have to be clear too.
                    if (dr != 0 && dc != 0 && (!IsClear(r * Columns + nc) || !IsClear(nr * Columns + c))) continue;

                    float through = cost + (dr != 0 && dc != 0 ? 1.4142f : 1f) * Cell;
                    if (through >= along[next]) continue;
                    along[next] = through;
                    cameFrom[next] = at;
                    open.Push(through, next);
                }
        }
        return ReachedCount;
    }

    /// <summary>A small binary heap of cells by distance, for the flood. (System.Collections.Generic.PriorityQueue is not in this runtime.)</summary>
    sealed class MinHeap
    {
        float[] keys;
        int[] items;
        public int Count { get; private set; }

        public MinHeap(int capacity)
        {
            keys = new float[capacity];
            items = new int[capacity];
        }

        public void Push(float key, int item)
        {
            if (Count == keys.Length)
            {
                System.Array.Resize(ref keys, Count * 2);
                System.Array.Resize(ref items, Count * 2);
            }
            int i = Count++;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (keys[parent] <= key) break;
                keys[i] = keys[parent];
                items[i] = items[parent];
                i = parent;
            }
            keys[i] = key;
            items[i] = item;
        }

        public int Pop(out float key)
        {
            int top = items[0];
            key = keys[0];
            Count--;
            if (Count > 0)
            {
                float lastKey = keys[Count];
                int lastItem = items[Count];
                int i = 0;
                while (true)
                {
                    int child = 2 * i + 1;
                    if (child >= Count) break;
                    if (child + 1 < Count && keys[child + 1] < keys[child]) child++;
                    if (keys[child] >= lastKey) break;
                    keys[i] = keys[child];
                    items[i] = items[child];
                    i = child;
                }
                keys[i] = lastKey;
                items[i] = lastItem;
            }
            return top;
        }
    }

    int NearestClear(Vector2 xz, float within)
    {
        if (!InArea(xz)) return -1;
        int best = -1;
        float bestDistance = float.MaxValue;
        int reach = Mathf.CeilToInt(within / Cell);
        int centre = CellOf(xz);
        int cc = centre % Columns, cr = centre / Columns;
        for (int dr = -reach; dr <= reach; dr++)
            for (int dc = -reach; dc <= reach; dc++)
            {
                int c = cc + dc, r = cr + dr;
                if (c < 0 || r < 0 || c >= Columns || r >= Rows) continue;
                int index = r * Columns + c;
                float d = (CentreOf(index) - xz).sqrMagnitude;
                if (d >= bestDistance || d > within * within) continue;
                if (!IsClear(index)) continue;
                best = index;
                bestDistance = d;
            }
        return best;
    }

    // ------------------------------------------------------------------ asking

    /// <summary>Did the flood reach this cell?</summary>
    public bool Reached(Vector2 xz) => InArea(xz) && reached[CellOf(xz)];

    /// <summary>How far to drive from the start to this cell, in metres: the shortest way by water the grid allows. Infinity if it was not reached.</summary>
    public float DistanceFromStart(Vector2 xz) => Reached(xz) ? along[CellOf(xz)] : float.PositiveInfinity;

    /// <summary>How far (metres) the nearest cell the flood reached is from this point; infinity if none is within <paramref name="search"/>.</summary>
    public float DistanceToReached(Vector2 xz, float search = 40f) => Nearest(xz, search, out _);

    /// <summary>The nearest reached cell's centre to a point, and how far it is. Infinity if none is within <paramref name="search"/>.</summary>
    public float Nearest(Vector2 xz, float search, out Vector2 where)
    {
        where = xz;
        float best = float.PositiveInfinity;
        int reachCells = Mathf.CeilToInt(search / Cell);
        int centre = CellOf(xz);
        int cc = centre % Columns, cr = centre / Columns;
        for (int dr = -reachCells; dr <= reachCells; dr++)
            for (int dc = -reachCells; dc <= reachCells; dc++)
            {
                int c = cc + dc, r = cr + dr;
                if (c < 0 || r < 0 || c >= Columns || r >= Rows) continue;
                int index = r * Columns + c;
                if (!reached[index]) continue;
                Vector2 p = CentreOf(index);
                float d = Vector2.Distance(p, xz);
                if (d < best) { best = d; where = p; }
            }
        return best > search ? float.PositiveInfinity : best;
    }

    /// <summary>
    /// The cells to drive through from the start to this point, thinned to the corners that matter. Empty if the point is not
    /// reached. The first point is the start, the last is the reached cell nearest to the target.
    /// </summary>
    public List<Vector2> RouteTo(Vector2 xz, float search = 40f)
    {
        var route = new List<Vector2>();
        if (startCell < 0 || Nearest(xz, search, out Vector2 end) == float.PositiveInfinity) return route;

        var cells = new List<int>();
        for (int at = CellOf(end); at >= 0; at = cameFrom[at]) cells.Add(at);
        cells.Reverse();

        // Keep a point only where the straight run from the last kept point to the next one would clip something.
        int from = 0;
        route.Add(CentreOf(cells[0]));
        for (int i = 2; i < cells.Count; i++)
        {
            if (!StraightRunIsClear(CentreOf(cells[from]), CentreOf(cells[i])))
            {
                route.Add(CentreOf(cells[i - 1]));
                from = i - 1;
            }
        }
        route.Add(CentreOf(cells[cells.Count - 1]));
        return route;
    }

    /// <summary>Can a hull go in a straight line between two points without leaving the cells that are clear?</summary>
    public bool StraightRunIsClear(Vector2 a, Vector2 b)
    {
        float length = Vector2.Distance(a, b);
        int steps = Mathf.CeilToInt(length / (Cell * 0.5f));
        for (int i = 0; i <= steps; i++)
        {
            Vector2 p = Vector2.Lerp(a, b, steps == 0 ? 0f : (float)i / steps);
            if (!InArea(p) || !IsClear(p)) return false;
        }
        return true;
    }

    /// <summary>
    /// The water, reachable from the start, from which a line can be thrown to somebody: within <paramref name="range"/> of the
    /// lookout's hands (which are <paramref name="handsHeight"/> above the waterline) and in plain view of their head. The lookout
    /// stands at the bow, <paramref name="bowOffset"/> metres from the boat's middle and <paramref name="lateral"/> to one side, and a
    /// crew turns the bow to face whoever they throw to, so the view is tried from there. When there are several such places the one
    /// nearest the start by water is chosen, but not at the very edge of a throw. <paramref name="reject"/> rules places out.
    /// </summary>
    public bool ThrowPointFor(Vector3 head, float handsHeight, float range, out Vector2 where,
                              float bowOffset = 1.8f, float lateral = 0.85f, System.Func<Vector2, bool> reject = null)
    {
        var target = new Vector2(head.x, head.z);
        where = target;
        float best = float.PositiveInfinity;
        for (float r = 1.5f; r <= range; r += 1f)
        {
            for (int a = 0; a < 32; a++)
            {
                float angle = a * Mathf.PI * 2f / 32f;
                var xz = target + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * r;
                if (!Reached(xz) || (reject != null && reject(xz))) continue;

                Vector2 toward = (target - xz).normalized;
                Vector2 side = new Vector2(-toward.y, toward.x);
                bool ok = true;
                for (int k = -1; k <= 1 && ok; k++)
                {
                    Vector2 at = xz + toward * bowOffset + side * (k * lateral);
                    var hands = new Vector3(at.x, waterY + handsHeight, at.y);
                    ok = Vector3.Distance(hands, head) <= range && LineOfSight.Clear(hands, head);
                }
                if (!ok) continue;

                float cost = along[CellOf(xz)] + r * 2f;
                if (cost < best) { best = cost; where = xz; }
            }
        }
        return !float.IsInfinity(best);
    }

    /// <summary>How many cells of the flood are inside a rectangle: a sense of how open the water is there.</summary>
    public int ReachedIn(Rect r)
    {
        int n = 0;
        for (int i = 0; i < reached.Length; i++)
            if (reached[i] && r.Contains(CentreOf(i))) n++;
        return n;
    }

    /// <summary>The flood as rows of reached cells, left to right, for drawing: (row start x, row z, run length in cells).</summary>
    public IEnumerable<(float x, float z, int length)> Runs()
    {
        for (int r = 0; r < Rows; r++)
        {
            int c = 0;
            while (c < Columns)
            {
                if (!reached[r * Columns + c]) { c++; continue; }
                int start = c;
                while (c < Columns && reached[r * Columns + c]) c++;
                yield return (Area.xMin + start * Cell, Area.yMin + r * Cell, c - start);
            }
        }
    }
}
