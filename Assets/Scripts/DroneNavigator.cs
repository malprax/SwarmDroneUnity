using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Navigator:
/// - TickMap(worldPos): raycast N arah untuk update occupancy (free/occupied)
/// - ReplanToFrontier(worldPos): cari frontier (Unknown yang bertetangga Free)
/// - ReplanToCell(goal, worldPos): A* ke cell tujuan
/// - Waypoint: world center per cell
/// </summary>
public class DroneNavigator : MonoBehaviour
{
    [Header("Refs")]
    public GridMap2D map;

    [Header("Sensing")]
    public LayerMask wallLayerMask;      // raycast sensing
    public LayerMask obstacleLayerMask;  // planning safety (biasanya Wall)
    public float senseRange = 2.2f;
    public int senseRays = 8;

    [Header("Planning")]
    public float replanEverySeconds = 0.35f;
    public int maxAStarNodes = 20000;
    public int frontierSearchLimit = 3000;

    [Header("Frontier Scoring")]
    [Tooltip("Ambil maksimal N kandidat frontier terdekat, lalu pilih yang skornya terbaik.")]
    public int frontierCandidates = 30;

    [Tooltip("Cooldown agar frontier tidak dipilih ulang terlalu cepat (detik).")]
    public float frontierCooldownSeconds = 2.5f;

    [Header("Waypoint")]
    [Tooltip("Saran: <= 0.7*cellSize (misal cellSize 0.3 => 0.21)")]
    public float waypointArriveDistance = 0.35f;

    [Header("Debug Logs")]
    public bool verbose = false;
    public bool logSensing = false;
    public bool logFrontier = true;
    public bool logAStar = true;

    public bool HasPath => pathCells != null && pathCells.Count > 0;

    // debug exposure for Drone
    public int DebugPathLen => pathCells.Count;
    public int DebugPathIndex => pathIndex;
    public Vector2Int DebugGoalCell => debugGoalCell;

    private float replanTimer = 0f;

    private readonly List<Vector2Int> pathCells = new List<Vector2Int>(512);
    private int pathIndex = 0;

    private Vector2Int debugGoalCell = new Vector2Int(int.MinValue, int.MinValue);

    // frontier cooldown
    private readonly Dictionary<Vector2Int, float> frontierCooldown = new Dictionary<Vector2Int, float>(256);

    // ❗ penting untuk anti-self-frontier
    private Vector2Int lastStartCell = new Vector2Int(int.MinValue, int.MinValue);

    // A* buffers
    private readonly Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>(4096);
    private readonly Dictionary<Vector2Int, float> gScore = new Dictionary<Vector2Int, float>(4096);
    private readonly SimplePriorityQueue<Vector2Int> open = new SimplePriorityQueue<Vector2Int>(4096);

    // =========================
    // Public
    // =========================
    public void TickMap(Vector2 worldPos)
    {
        if (map == null) return;

        // mark current cell free
        if (map.WorldToCell(worldPos, out var curr))
            map.SetFree(curr);

        if (logSensing)
        {
            if (map.WorldToCell(worldPos, out var cc))
                Debug.Log($"[Navigator] TickMap world={Fmt2(worldPos)} currCell={cc} range={senseRange:F2} rays={senseRays}");
        }

        // raycast N directions
        for (int i = 0; i < senseRays; i++)
        {
            float ang = (360f / senseRays) * i;
            Vector2 dir = Quaternion.Euler(0, 0, ang) * Vector2.right;

            RaycastHit2D hit = Physics2D.Raycast(worldPos, dir, senseRange, wallLayerMask);

            float endDist = (hit.collider != null) ? hit.distance : senseRange;

            if (logSensing)
            {
                string hitStr = (hit.collider != null)
                    ? $"HIT {hit.collider.name} d={hit.distance:F2} p={Fmt2(hit.point)}"
                    : "NO HIT";
                Debug.Log($"[Navigator] Ray#{i} dir={Fmt2(dir)} {hitStr}");
            }

            // mark free cells along ray
            int steps = Mathf.CeilToInt(endDist / map.CellSize);
            for (int s = 1; s <= steps; s++)
            {
                Vector2 p = worldPos + dir * Mathf.Min(endDist, s * map.CellSize);
                if (map.WorldToCell(p, out var c))
                    map.SetFree(c);
            }

            // if hit wall -> mark occupied near hit point
            if (hit.collider != null)
            {
                Vector2 hp = hit.point + (-dir) * 0.01f;
                if (map.WorldToCell(hp, out var occ))
                    map.SetOccupied(occ);
            }
        }

        // inflate after update
        map.RebuildInflation();

        // decay frontier cooldown
        if (frontierCooldown.Count > 0)
        {
            var keys = new List<Vector2Int>(frontierCooldown.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                var k = keys[i];
                frontierCooldown[k] -= Time.deltaTime;
                if (frontierCooldown[k] <= 0f) frontierCooldown.Remove(k);
            }
        }
    }

    public bool ShouldReplan()
    {
        replanTimer -= Time.deltaTime;
        return replanTimer <= 0f;
    }

    public void ReplanToFrontier(Vector2 worldPos)
    {
        replanTimer = replanEverySeconds;
        if (map == null) return;

        if (!map.WorldToCell(worldPos, out var start))
            return;

        // simpan start cell (dipakai untuk anti-self-frontier)
        lastStartCell = start;

        Vector2Int? frontier = FindFrontierScored(start);
        if (frontier == null)
        {
            if (logFrontier)
                Debug.Log($"[Navigator] Frontier NOT found start={start} limit={frontierSearchLimit}");

            pathCells.Clear();
            pathIndex = 0;
            return;
        }

        if (logFrontier)
            Debug.Log($"[Navigator] Frontier chosen={frontier.Value} start={start}");

        // set cooldown
        frontierCooldown[frontier.Value] = frontierCooldownSeconds;

        ReplanToCell(frontier.Value, worldPos);
    }

    public void ReplanToCell(Vector2Int goal, Vector2 worldPos)
    {
        replanTimer = replanEverySeconds;

        if (map == null) return;
        if (!map.WorldToCell(worldPos, out var start))
            return;

        // update start cell cache juga (biar IsFrontier aman)
        lastStartCell = start;

        ReplanToCellInternal(goal, start);
    }

    public void AdvanceWaypointIfArrived(Vector2 worldPos)
    {
        if (!HasPath) return;
        if (map == null) return;

        Vector2 wp = GetCurrentWaypointWorld();
        if (Vector2.Distance(worldPos, wp) <= waypointArriveDistance)
        {
            pathIndex++;
            if (pathIndex >= pathCells.Count)
            {
                pathCells.Clear();
                pathIndex = 0;
            }
        }
    }

    public Vector2 GetCurrentWaypointWorld()
    {
        if (!HasPath || map == null) return transform.position;
        return map.CellToWorldCenter(pathCells[pathIndex]);
    }

    // =========================
    // Frontier (Scored)
    // =========================
    private Vector2Int? FindFrontierScored(Vector2Int start)
    {
        // BFS kumpulkan kandidat frontier terdekat (hingga frontierCandidates)
        var q = new Queue<Vector2Int>(256);
        var visited = new HashSet<Vector2Int>();

        q.Enqueue(start);
        visited.Add(start);

        int expanded = 0;
        var candidates = new List<Vector2Int>(frontierCandidates);

        while (q.Count > 0 && expanded < frontierSearchLimit)
        {
            var c = q.Dequeue();
            expanded++;

            if (IsFrontier(c))
            {
                // skip kalau sedang cooldown
                if (!frontierCooldown.ContainsKey(c))
                {
                    candidates.Add(c);
                    if (candidates.Count >= frontierCandidates) break;
                }
            }

            foreach (var n in Neighbors4(c))
            {
                if (!map.InBounds(n)) continue;
                if (visited.Contains(n)) continue;

                // BFS lewat Free (inflated)
                if (map.GetCellInflated(n) != GridMap2D.Free) continue;

                visited.Add(n);
                q.Enqueue(n);
            }
        }

        if (candidates.Count == 0) return null;

        // scoring sederhana:
        // - prefer frontier yang banyak Unknown di sekitar (lebih informatif)
        // - sedikit penalti jarak dari start (biar gak jauh banget)
        float bestScore = float.NegativeInfinity;
        Vector2Int best = candidates[0];

        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];

            int unknownAround = CountUnknownNeighbors8(c);
            float dist = Mathf.Abs(c.x - start.x) + Mathf.Abs(c.y - start.y);

            float score = unknownAround * 2.0f - dist * 0.15f;

            if (score > bestScore)
            {
                bestScore = score;
                best = c;
            }
        }

        if (logFrontier)
            Debug.Log($"[Navigator] Frontier candidates={candidates.Count} pick={best} score={bestScore:F2}");

        return best;
    }

    private int CountUnknownNeighbors8(Vector2Int c)
    {
        int cnt = 0;
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        {
            if (dx == 0 && dy == 0) continue;
            var n = new Vector2Int(c.x + dx, c.y + dy);
            if (!map.InBounds(n)) continue;
            if (map.GetCell(n) == GridMap2D.Unknown) cnt++;
        }
        return cnt;
    }

    private bool IsFrontier(Vector2Int c)
    {
        if (map == null) return false;

        // HARUS free
        if (map.GetCellInflated(c) != GridMap2D.Free)
            return false;

        // ❗ JANGAN frontier kalau terlalu dekat dengan start (anti self-frontier)
        // Vector2Int.Distance pakai float; aman untuk threshold 1
        if (Vector2Int.Distance(c, lastStartCell) <= 1f)
            return false;

        foreach (var n in Neighbors4(c))
        {
            if (!map.InBounds(n)) continue;
            if (map.GetCell(n) == GridMap2D.Unknown)
                return true;
        }
        return false;
    }

    // =========================
    // A*
    // =========================
    private void ReplanToCellInternal(Vector2Int goal, Vector2Int start)
    {
        if (map == null) return;

        pathCells.Clear();
        pathIndex = 0;

        debugGoalCell = goal;

        if (logAStar)
            Debug.Log($"[Navigator] A* start={start} goal={goal} inflate={map.inflateCells} maxNodes={maxAStarNodes}");

        bool ok = AStar(start, goal, out var result);
        if (!ok)
        {
            if (logAStar)
                Debug.Log($"[Navigator] A* FAILED start={start} goal={goal} (blocked/unknown grid?)");
            return;
        }

        pathCells.AddRange(result);
        pathIndex = 0;

        if (logAStar)
            Debug.Log($"[Navigator] A* OK pathLen={pathCells.Count} first={pathCells[0]} last={pathCells[pathCells.Count - 1]}");
    }

    private bool AStar(Vector2Int start, Vector2Int goal, out List<Vector2Int> result)
    {
        result = new List<Vector2Int>(256);

        cameFrom.Clear();
        gScore.Clear();
        open.Clear();

        gScore[start] = 0f;
        open.Enqueue(start, Heuristic(start, goal));

        int expanded = 0;

        while (open.Count > 0 && expanded < maxAStarNodes)
        {
            var current = open.Dequeue();
            expanded++;

            if (current == goal)
            {
                ReconstructPath(start, goal, result);
                return true;
            }

            foreach (var n in Neighbors4(current))
            {
                if (!map.InBounds(n)) continue;

                // pakai inflated occupancy supaya gak mepet wall
                if (map.GetCellInflated(n) != GridMap2D.Free) continue;

                float tentative = gScore[current] + 1f;

                if (!gScore.TryGetValue(n, out float old) || tentative < old)
                {
                    cameFrom[n] = current;
                    gScore[n] = tentative;

                    float f = tentative + Heuristic(n, goal);
                    open.EnqueueOrUpdate(n, f);
                }
            }
        }

        return false;
    }

    private void ReconstructPath(Vector2Int start, Vector2Int goal, List<Vector2Int> outPath)
    {
        outPath.Clear();

        var cur = goal;
        outPath.Add(cur);

        int safety = 0;
        while (cur != start && safety++ < 100000)
        {
            if (!cameFrom.TryGetValue(cur, out var prev))
                break;
            cur = prev;
            outPath.Add(cur);
        }

        outPath.Reverse();
    }

    private float Heuristic(Vector2Int a, Vector2Int b)
        => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    private IEnumerable<Vector2Int> Neighbors4(Vector2Int c)
    {
        yield return new Vector2Int(c.x + 1, c.y);
        yield return new Vector2Int(c.x - 1, c.y);
        yield return new Vector2Int(c.x, c.y + 1);
        yield return new Vector2Int(c.x, c.y - 1);
    }

    private static string Fmt2(Vector2 v) => $"({v.x:F2}, {v.y:F2})";

    // =========================
    // Small priority queue (min-heap)
    // =========================
    private class SimplePriorityQueue<T>
    {
        private readonly List<(T item, float prio)> heap;
        private readonly Dictionary<T, int> index;

        public int Count => heap.Count;

        public SimplePriorityQueue(int cap)
        {
            heap = new List<(T, float)>(cap);
            index = new Dictionary<T, int>(cap);
        }

        public void Clear()
        {
            heap.Clear();
            index.Clear();
        }

        public void Enqueue(T item, float prio)
        {
            if (index.ContainsKey(item))
            {
                EnqueueOrUpdate(item, prio);
                return;
            }

            heap.Add((item, prio));
            int i = heap.Count - 1;
            index[item] = i;
            SiftUp(i);
        }

        public void EnqueueOrUpdate(T item, float prio)
        {
            if (!index.TryGetValue(item, out int i))
            {
                Enqueue(item, prio);
                return;
            }

            float old = heap[i].prio;
            heap[i] = (item, prio);

            if (prio < old) SiftUp(i);
            else SiftDown(i);
        }

        public T Dequeue()
        {
            var root = heap[0].item;
            Swap(0, heap.Count - 1);

            heap.RemoveAt(heap.Count - 1);
            index.Remove(root);

            if (heap.Count > 0) SiftDown(0);

            return root;
        }

        private void SiftUp(int i)
        {
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (heap[i].prio >= heap[p].prio) break;
                Swap(i, p);
                i = p;
            }
        }

        private void SiftDown(int i)
        {
            int n = heap.Count;
            while (true)
            {
                int l = i * 2 + 1;
                int r = l + 1;
                int smallest = i;

                if (l < n && heap[l].prio < heap[smallest].prio) smallest = l;
                if (r < n && heap[r].prio < heap[smallest].prio) smallest = r;

                if (smallest == i) break;
                Swap(i, smallest);
                i = smallest;
            }
        }

        private void Swap(int a, int b)
        {
            var tmp = heap[a];
            heap[a] = heap[b];
            heap[b] = tmp;

            index[heap[a].item] = a;
            index[heap[b].item] = b;
        }
    }
}