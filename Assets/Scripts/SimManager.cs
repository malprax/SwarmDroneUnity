using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SimManager : MonoBehaviour
{
    // =========================================================
    //  SINGLETON
    // =========================================================
    public static SimManager Instance { get; private set; }

    // =========================================================
    //  SIMULATION CONTROL
    // =========================================================
    [Header("Visual Visit Markers")]
    [Tooltip("Prefab sprite kecil (kotak) untuk menandai sel yang pernah dikunjungi.")]
    public GameObject visitedCellPrefab;

    [Tooltip("Warna saat sel baru sekali dikunjungi.")]
    public Color visitColorMin = Color.cyan;

    [Tooltip("Warna saat sel sering dikunjungi.")]
    public Color visitColorMax = Color.red;

    [Header("Simulation Control")]
    [SerializeField] private bool isPlaying = false;
    public bool IsPlaying => isPlaying;

    [Tooltip("Daftar semua drone di arena (boleh diisi manual atau auto-detect).")]
    public List<Drone> drones = new List<Drone>();

    [Header("Home Base & Grid Navigation")]
    [Tooltip("Transform HomeBase di tengah base (dipakai Drone & flood-fill).")]
    public Transform homeBase;

    public Vector2 HomePosition => homeBase != null ? (Vector2)homeBase.position : Vector2.zero;

    [Tooltip("Ukuran sel grid (meter). Mis: 0.25–0.5")]
    public float cellSize = 0.25f;

    [Tooltip("Jumlah sel grid arah X (lebar arena).")]
    public int gridWidth = 80;

    [Tooltip("Jumlah sel grid arah Y (tinggi arena).")]
    public int gridHeight = 80;

    [Tooltip("Titik pusat (0,0) grid dalam koordinat dunia.")]
    public Vector2 gridWorldCenter = Vector2.zero;

    [Tooltip("Radius deteksi dinding saat membangun grid (untuk menandai sel sebagai wall).")]
    public float wallCheckRadius = 0.1f;

    [Tooltip("Layer yang dianggap dinding (harus sama dengan layer Wall pada Drone).")]
    public LayerMask wallLayerMask;

    [Header("Frontier / SLAM Settings")]
    [Tooltip("Perkiraan jarak maksimal sensor jarak (m). Dipakai untuk SLAM occupancy.")]
    public float maxSensorRange = 3.0f;

    // distanceField[x, y] = jarak langkah dari HomeBase (0 = home cell, -1 = tak terjangkau / wall)
    private int[,] distanceField;

    // =========================================================
    //  OCCUPANCY GRID UNTUK SLAM / FRONTIER
    // =========================================================
    public enum CellState
    {
        Unknown = 0,
        Free = 1,
        Occupied = 2
    }

    // occupancyGrid[x, y] = Unknown/Free/Occupied (dibangun runtime dari sensor)
    private CellState[,] occupancyGrid;

    // =========================================================
    //  MICROMOUSE-STYLE CELL MEMORY (untuk logging & room graph)
    // =========================================================
    public class CellMemory
    {
        public bool isFree = true;   // bukan wall
        public bool isDoor = false;  // cell ini pintu sempit
        public bool visited = false; // dikunjungi drone (runtime)
        public int visitCount = 0;   // berapa kali dilewati
        public int roomId = -1;      // id room; -1 = belum di-assign

        // objek visual di scene (kotak warna)
        public GameObject markerObj;
    }

    // key = grid coord; value = cell info
    private Dictionary<Vector2Int, CellMemory> cellMemory = new Dictionary<Vector2Int, CellMemory>();

    [SerializeField] private int debugCellMemoryCount = 0;

    // Parent untuk semua visited cell (agar Hierarchy tidak penuh)
    private Transform visitedParent;

    [Header("Room / Region Stats")]
    [Tooltip("Jumlah room (cluster) yang berhasil dibentuk.")]
    public int discoveredRoomCount = 0;

    // =========================================================
    //  ROOM GRAPH & WAYPOINT EXPLORATION
    // =========================================================
    [System.Serializable]
    public class RoomSummary
    {
        public int roomId;
        public Vector2 centroid;
        public int cellCount;
        public List<int> doorIndices = new List<int>();
    }

    [System.Serializable]
    public class DoorSummary
    {
        public int doorId;
        public int roomA;
        public int roomB;
        public Vector2 worldPos;   // titik tengah pintu
    }

    [SerializeField] private List<RoomSummary> roomSummaries = new List<RoomSummary>();
    [SerializeField] private List<DoorSummary> doorSummaries = new List<DoorSummary>();

    private bool roomGraphBuilt = false;

    [Header("Debug Grid Logging")]
    [Tooltip("Jika true, setiap langkah drone akan di-log detail.")]
    public bool debugGridSteps = false;

    // =========================================================
    //  UNITY LIFECYCLE
    // =========================================================
    void Awake()
    {
        // Singleton
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Auto-collect drone kalau belum diisi
        if (drones == null || drones.Count == 0)
        {
            var found = FindObjectsByType<Drone>(FindObjectsSortMode.None);
            drones = new List<Drone>(found);
        }

        // Parent untuk visited cells (agar Hierarchy rapi)
        if (visitedParent == null)
        {
            GameObject p = new GameObject("VisitedCells");
            visitedParent = p.transform;
        }

        InitOccupancyGrid();

        // Optional: flood-fill distance field (grid → home)
        BuildDistanceFieldFromHome();
    }

    void Start()
    {
        isPlaying = false;
    }

    // =========================================================
    //  PUBLIC CONTROL (UI / KEYBOARD)
    // =========================================================
    public void StartSimulation()
    {
        Debug.Log("[SimManager] StartSimulation()");

        // STEP 1: Build static grid occupancy & cellMemory (global peta statis)
        BuildStaticGridMemory();

        // STEP 2: Build room clusters + doors dari grid
        BuildRoomsAndDoorsFromGrid();

        // STEP 3: Build graph-based exploration route (DFS)
        int startRoomId = GetRoomIdAtWorldPosition(HomePosition);
        if (startRoomId < 0 && discoveredRoomCount > 0)
        {
            Debug.LogWarning("[SimManager] HomeBase tidak berada di room manapun, fallback roomId=0.");
            startRoomId = 0;
        }

        List<Vector2> route = BuildExplorationRouteDFS(startRoomId);
        if (route.Count == 0 && roomSummaries.Count > 0)
        {
            // fallback: minimal kunjungi centroid room pertama
            route.Add(roomSummaries[0].centroid);
        }

        Vector2[] routeArray = route.ToArray();
        Debug.Log($"[SimManager] Final route has {routeArray.Length} waypoints.");

        // STEP 4: Assign route ke semua drone
        foreach (var d in drones)
        {
            if (d == null) continue;
            d.SetWaypoints(routeArray);
        }

        // STEP 5: Play
        isPlaying = true;
    }

    public void StopSimulation()
    {
        Debug.Log("[SimManager] StopSimulation()");
        isPlaying = false;

        foreach (var d in drones)
        {
            if (d == null) continue;
            d.StopDrone();
        }
    }

    public void ResetAllDrones()
    {
        Debug.Log("[SimManager] ResetAllDrones()");

        isPlaying = false;

        foreach (var d in drones)
        {
            if (d == null) continue;

            // manual reset transform ke HomeBase
            if (homeBase != null)
                d.transform.position = homeBase.position;

            d.transform.up = Vector2.up;
            d.StopDrone();
        }
    }

    public void CommandAllReturnHome()
    {
        Debug.Log("[SimManager] CommandAllReturnHome()");

        foreach (var d in drones)
        {
            if (d == null) continue;
            d.StartReturnHome();
        }
    }

    // =========================================================
    //  GRID UTILITIES
    // =========================================================
    Vector2Int WorldToGrid(Vector2 worldPos)
    {
        Vector2 local = worldPos - gridWorldCenter;

        int gx = Mathf.RoundToInt(local.x / cellSize) + gridWidth / 2;
        int gy = Mathf.RoundToInt(local.y / cellSize) + gridHeight / 2;

        return new Vector2Int(gx, gy);
    }

    Vector2 GridToWorldCenter(Vector2Int cell)
    {
        float x = (cell.x - gridWidth / 2) * cellSize;
        float y = (cell.y - gridHeight / 2) * cellSize;

        return gridWorldCenter + new Vector2(x, y);
    }

    bool IsInsideGrid(Vector2Int c)
    {
        return c.x >= 0 && c.x < gridWidth && c.y >= 0 && c.y < gridHeight;
    }

    bool IsCellWall(Vector2Int c)
    {
        if (!IsInsideGrid(c)) return true;

        Vector2 center = GridToWorldCenter(c);
        Collider2D col = Physics2D.OverlapCircle(center, wallCheckRadius, wallLayerMask);
        return col != null;
    }

    // =========================================================
    //  OCCUPANCY GRID HELPERS (SLAM)
    // =========================================================
    void InitOccupancyGrid()
    {
        occupancyGrid = new CellState[gridWidth, gridHeight];
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                occupancyGrid[x, y] = CellState.Unknown;
            }
        }
    }

    void EnsureOccupancyGrid()
    {
        if (occupancyGrid == null ||
            occupancyGrid.GetLength(0) != gridWidth ||
            occupancyGrid.GetLength(1) != gridHeight)
        {
            InitOccupancyGrid();
        }
    }

    void CastSensorRay(Vector2 origin, Vector2 dir, float measuredDist, float maxDist, float step)
    {
        EnsureOccupancyGrid();

        float rayLen = Mathf.Min(measuredDist, maxDist);
        float freeLen = rayLen - cellSize * 0.5f;
        if (freeLen < 0f) freeLen = 0f;

        // tandai sel FREE sepanjang ray
        for (float t = 0; t < freeLen; t += step)
        {
            Vector2 p = origin + dir * t;
            Vector2Int c = WorldToGrid(p);
            if (!IsInsideGrid(c)) continue;

            if (occupancyGrid[c.x, c.y] == CellState.Unknown)
                occupancyGrid[c.x, c.y] = CellState.Free;
        }

        // kalau ray kena sesuatu (jarak < maxDist) tandai sel terakhir sebagai OCCUPIED
        if (measuredDist < maxDist)
        {
            Vector2 pWall = origin + dir * rayLen;
            Vector2Int cWall = WorldToGrid(pWall);
            if (IsInsideGrid(cWall))
                occupancyGrid[cWall.x, cWall.y] = CellState.Occupied;
        }
    }

    void UpdateOccupancyFromSensors(
        Drone drone,
        Vector2 sensedPos,
        float dFront, float dRight, float dBack, float dLeft)
    {
        EnsureOccupancyGrid();

        // fallback orientasi kalau drone null
        Vector2 forward = drone != null ? (Vector2)drone.transform.up : Vector2.up;
        Vector2 right   = drone != null ? (Vector2)drone.transform.right : Vector2.right;
        Vector2 back    = -forward;
        Vector2 left    = -right;

        float maxDist = maxSensorRange;
        float step = cellSize * 0.5f;

        // depan
        CastSensorRay(sensedPos, forward, dFront, maxDist, step);
        // kanan
        CastSensorRay(sensedPos, right, dRight, maxDist, step);
        // belakang
        CastSensorRay(sensedPos, back, dBack, maxDist, step);
        // kiri
        CastSensorRay(sensedPos, left, dLeft, maxDist, step);
    }

    // =========================================================
    //  BUILD STATIC GRID MEMORY (FREE CELL + DOOR CELL + ROOM ID)
    // =========================================================
    void BuildStaticGridMemory()
    {
        Debug.Log("[SimManager] BuildStaticGridMemory() start...");
        cellMemory.Clear();

        // 1) Isi free cell (bukan wall)
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                Vector2Int c = new Vector2Int(x, y);
                if (IsCellWall(c)) continue;

                CellMemory mem = new CellMemory
                {
                    isFree = true,
                    isDoor = false,
                    visited = false,
                    visitCount = 0,
                    roomId = -1
                };
                cellMemory[c] = mem;
            }
        }

        // 2) Deteksi cell yang merupakan "pintu sempit"
        foreach (var kv in new Dictionary<Vector2Int, CellMemory>(cellMemory))
        {
            Vector2Int c = kv.Key;
            CellMemory mem = kv.Value;

            bool wallUp = IsCellWall(c + new Vector2Int(0, 1));
            bool wallDown = IsCellWall(c + new Vector2Int(0, -1));
            bool wallLeft = IsCellWall(c + new Vector2Int(-1, 0));
            bool wallRight = IsCellWall(c + new Vector2Int(1, 0));

            // vertical door (dinding kiri-kanan, terbuka atas-bawah)
            bool verticalDoor = wallLeft && wallRight && !wallUp && !wallDown;
            // horizontal door (dinding atas-bawah, terbuka kiri-kanan)
            bool horizontalDoor = wallUp && wallDown && !wallLeft && !wallRight;

            if (verticalDoor || horizontalDoor)
            {
                mem.isDoor = true;
            }
        }

        debugCellMemoryCount = cellMemory.Count;
        Debug.Log($"[SimManager] BuildStaticGridMemory() done. freeCells={cellMemory.Count}");
    }

    // =========================================================
    //  BUILD ROOMS & DOORS FROM GRID
    // =========================================================
    void BuildRoomsAndDoorsFromGrid()
    {
        Debug.Log("[SimManager] BuildRoomsAndDoorsFromGrid() start...");

        roomSummaries.Clear();
        doorSummaries.Clear();
        discoveredRoomCount = 0;
        roomGraphBuilt = false;

        if (cellMemory == null || cellMemory.Count == 0)
        {
            Debug.LogWarning("[SimManager] cellMemory kosong, tidak bisa build room graph.");
            return;
        }

        // 1) Cluster room dengan BFS, TIDAK lewat cell dengan isDoor=true
        Dictionary<int, RoomSummary> roomMap = new Dictionary<int, RoomSummary>();

        Vector2Int[] dirs4 = new Vector2Int[]
        {
            new Vector2Int( 1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int( 0, 1),
            new Vector2Int( 0,-1)
        };

        HashSet<Vector2Int> visitedCell = new HashSet<Vector2Int>();

        foreach (var kv in cellMemory)
        {
            Vector2Int start = kv.Key;
            CellMemory mem = kv.Value;

            if (!mem.isFree) continue;
            if (mem.isDoor) continue;    // pintu tidak dihitung sebagai isi room
            if (visitedCell.Contains(start)) continue;

            int currentRoomId = discoveredRoomCount++;
            RoomSummary summary = new RoomSummary
            {
                roomId = currentRoomId,
                centroid = Vector2.zero,
                cellCount = 0,
                doorIndices = new List<int>()
            };

            Queue<Vector2Int> q = new Queue<Vector2Int>();
            q.Enqueue(start);
            visitedCell.Add(start);

            while (q.Count > 0)
            {
                Vector2Int c = q.Dequeue();
                CellMemory cm = cellMemory[c];

                cm.roomId = currentRoomId;
                Vector2 worldPos = GridToWorldCenter(c);
                summary.centroid += worldPos;
                summary.cellCount++;

                foreach (var d in dirs4)
                {
                    Vector2Int nb = c + d;
                    if (visitedCell.Contains(nb)) continue;
                    if (!cellMemory.TryGetValue(nb, out var nbMem)) continue;
                    if (!nbMem.isFree) continue;
                    if (nbMem.isDoor) continue;

                    visitedCell.Add(nb);
                    q.Enqueue(nb);
                }
            }

            if (summary.cellCount > 0)
            {
                summary.centroid /= summary.cellCount;
            }
            roomMap[currentRoomId] = summary;
        }

        roomSummaries = new List<RoomSummary>(roomMap.Values);

        // 2) Deteksi door cell: hubungkan dua room yang berbeda
        int doorIdCounter = 0;

        foreach (var kv in cellMemory)
        {
            Vector2Int c = kv.Key;
            CellMemory mem = kv.Value;

            if (!mem.isFree || !mem.isDoor) continue;

            HashSet<int> neighborRooms = new HashSet<int>();

            foreach (var d in dirs4)
            {
                Vector2Int nb = c + d;
                if (!cellMemory.TryGetValue(nb, out var nbMem)) continue;
                if (!nbMem.isFree) continue;
                if (nbMem.isDoor) continue;
                if (nbMem.roomId >= 0)
                {
                    neighborRooms.Add(nbMem.roomId);
                }
            }

            if (neighborRooms.Count == 2)
            {
                var enumerator = neighborRooms.GetEnumerator();
                enumerator.MoveNext();
                int a = enumerator.Current;
                enumerator.MoveNext();
                int b = enumerator.Current;

                if (a > b)
                {
                    int tmp = a; a = b; b = tmp;
                }

                // Cek apakah door ini sudah terdaftar
                bool exists = false;
                foreach (var dsum in doorSummaries)
                {
                    if (dsum.roomA == a && dsum.roomB == b)
                    {
                        exists = true;
                        break;
                    }
                }
                if (exists) continue;

                Vector2 doorPos = GridToWorldCenter(c);

                DoorSummary doorSummary = new DoorSummary
                {
                    doorId = doorIdCounter++,
                    roomA = a,
                    roomB = b,
                    worldPos = doorPos
                };
                doorSummaries.Add(doorSummary);

                if (roomMap.TryGetValue(a, out var roomA))
                    roomA.doorIndices.Add(doorSummary.doorId);
                if (roomMap.TryGetValue(b, out var roomB))
                    roomB.doorIndices.Add(doorSummary.doorId);
            }
        }

        // Re-sync roomSummaries from roomMap (doorIndices updated)
        roomSummaries = new List<RoomSummary>(roomMap.Values);

        roomGraphBuilt = true;

        Debug.Log($"[SimManager] BuildRoomsAndDoorsFromGrid() done. rooms={roomSummaries.Count}, doors={doorSummaries.Count}");
    }

    // =========================================================
    //  ROOM ROUTE (DFS)
    // =========================================================
    int GetRoomIdAtWorldPosition(Vector2 worldPos)
    {
        Vector2Int cell = WorldToGrid(worldPos);

        if (cellMemory.TryGetValue(cell, out var mem))
        {
            return mem.roomId;
        }
        return -1;
    }

    // =========================================================
    //  BUILD ROUTE (DFS) DARI ROOM GRAPH
    // =========================================================
    List<Vector2> BuildExplorationRouteDFS(int startRoomId)
    {
        var route = new List<Vector2>();

        if (roomSummaries == null || roomSummaries.Count == 0)
        {
            Debug.LogWarning("[SimManager] BuildExplorationRouteDFS: no rooms.");
            return route;
        }

        // fallback jika tidak ada pintu
        if (doorSummaries == null || doorSummaries.Count == 0)
        {
            if (startRoomId < 0 || startRoomId >= roomSummaries.Count)
                startRoomId = 0;

            // 1) room start dulu
            route.Add(roomSummaries[startRoomId].centroid);

            // 2) lalu semua room lain
            for (int i = 0; i < roomSummaries.Count; i++)
            {
                if (i == startRoomId) continue;
                route.Add(roomSummaries[i].centroid);
            }

            Debug.Log($"[SimManager] Fallback route (no doors). rooms={roomSummaries.Count}, waypoints={route.Count}");
            return route;
        }

        var visited = new HashSet<int>();

        void DfsRoom(int roomId, int parentRoomId)
        {
            if (visited.Contains(roomId)) return;
            visited.Add(roomId);

            var room = roomSummaries[roomId];

            // Masuk ke tengah ruangan
            route.Add(room.centroid);

            // Telusuri semua pintu dari ruangan ini
            foreach (int doorIndex in room.doorIndices)
            {
                if (doorIndex < 0 || doorIndex >= doorSummaries.Count) continue;
                var door = doorSummaries[doorIndex];

                int neighborRoomId = (door.roomA == roomId) ? door.roomB : door.roomA;
                if (neighborRoomId == parentRoomId)
                    continue; // jangan langsung balik ke parent

                if (!visited.Contains(neighborRoomId))
                {
                    // Pergi ke pintu
                    route.Add(door.worldPos);

                    // DFS ke ruangan tetangga
                    DfsRoom(neighborRoomId, roomId);

                    // Setelah selesai eksplor ruangan tetangga:
                    // kembali ke pintu lalu ke center ruangan ini
                    route.Add(door.worldPos);
                    route.Add(room.centroid);
                }
            }
        }

        if (startRoomId < 0 || startRoomId >= roomSummaries.Count)
            startRoomId = 0;

        DfsRoom(startRoomId, -1);

        Debug.Log($"[SimManager] DFS route built: roomsVisited={visited.Count}, waypoints={route.Count}");
        return route;
    }

    // =========================================================
    //  FRONTIER-BASED HELPERS (BELUM DIPAKAI DRONE, OPSIONAL)
    // =========================================================
    List<Vector2Int> FindFrontiers()
    {
        EnsureOccupancyGrid();

        var result = new List<Vector2Int>();

        Vector2Int[] dirs4 = new Vector2Int[]
        {
            new Vector2Int( 1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int( 0, 1),
            new Vector2Int( 0,-1)
        };

        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                if (occupancyGrid[x, y] != CellState.Free) continue;

                var c = new Vector2Int(x, y);
                bool hasUnknownNeighbor = false;

                foreach (var d in dirs4)
                {
                    var n = c + d;
                    if (!IsInsideGrid(n)) continue;
                    if (occupancyGrid[n.x, n.y] == CellState.Unknown)
                    {
                        hasUnknownNeighbor = true;
                        break;
                    }
                }

                if (hasUnknownNeighbor)
                    result.Add(c);
            }
        }

        return result;
    }

    List<Vector2Int> FindPathBFS(Vector2Int start, Vector2Int goal)
    {
        EnsureOccupancyGrid();

        if (!IsInsideGrid(start) || !IsInsideGrid(goal))
            return null;

        if (occupancyGrid[start.x, start.y] == CellState.Occupied)
            return null;

        var queue = new Queue<Vector2Int>();
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();

        queue.Enqueue(start);
        cameFrom[start] = start;

        Vector2Int[] dirs4 = new Vector2Int[]
        {
            new Vector2Int( 1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int( 0, 1),
            new Vector2Int( 0,-1)
        };

        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            if (c == goal) break;

            foreach (var d in dirs4)
            {
                var n = c + d;
                if (!IsInsideGrid(n)) continue;
                if (occupancyGrid[n.x, n.y] == CellState.Occupied) continue;
                if (cameFrom.ContainsKey(n)) continue;

                cameFrom[n] = c;
                queue.Enqueue(n);
            }
        }

        if (!cameFrom.ContainsKey(goal))
            return null;

        var path = new List<Vector2Int>();
        var cur = goal;
        while (true)
        {
            path.Add(cur);
            if (cur == start) break;
            cur = cameFrom[cur];
        }
        path.Reverse();
        return path;
    }

    // Untuk nanti: bisa dipanggil dari Drone kalau rute habis.
    public Vector2[] BuildRouteToNextFrontier(Drone drone)
    {
        if (drone == null) return null;

        var frontiers = FindFrontiers();
        if (frontiers == null || frontiers.Count == 0)
            return null;

        Vector2Int startCell = WorldToGrid(drone.transform.position);

        // pilih frontier terdekat
        Vector2Int best = frontiers[0];
        float bestDist = (GridToWorldCenter(best) - (Vector2)drone.transform.position).sqrMagnitude;

        for (int i = 1; i < frontiers.Count; i++)
        {
            var f = frontiers[i];
            float d = (GridToWorldCenter(f) - (Vector2)drone.transform.position).sqrMagnitude;
            if (d < bestDist)
            {
                best = f;
                bestDist = d;
            }
        }

        var pathCells = FindPathBFS(startCell, best);
        if (pathCells == null || pathCells.Count == 0)
            return null;

        Vector2[] waypoints = new Vector2[pathCells.Count];
        for (int i = 0; i < pathCells.Count; i++)
            waypoints[i] = GridToWorldCenter(pathCells[i]);

        return waypoints;
    }

    // =========================================================
    //  FLOOD-FILL DISTANCE FIELD (HOME AS GOAL) — OPSIONAL
    // =========================================================
    public void BuildDistanceFieldFromHome()
    {
        if (homeBase == null)
        {
            Debug.LogWarning("[SimManager] homeBase belum di-assign. Flood-fill skip.");
            distanceField = null;
            return;
        }

        distanceField = new int[gridWidth, gridHeight];
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                distanceField[x, y] = -1;
            }
        }

        Vector2Int homeCell = WorldToGrid(homeBase.position);
        if (!IsInsideGrid(homeCell))
        {
            Debug.LogWarning($"[SimManager] HomeBase di luar grid: {homeCell}");
            return;
        }

        Queue<Vector2Int> q = new Queue<Vector2Int>();

        if (!IsCellWall(homeCell))
        {
            distanceField[homeCell.x, homeCell.y] = 0;
            q.Enqueue(homeCell);
        }
        else
        {
            Debug.LogWarning("[SimManager] HomeBase berada di dalam wall cell menurut deteksi grid.");
            return;
        }

        Vector2Int[] dirs = new Vector2Int[]
        {
            new Vector2Int( 1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int( 0, 1),
            new Vector2Int( 0,-1)
        };

        while (q.Count > 0)
        {
            Vector2Int c = q.Dequeue();
            int cd = distanceField[c.x, c.y];

            foreach (var d in dirs)
            {
                Vector2Int n = c + d;
                if (!IsInsideGrid(n)) continue;
                if (distanceField[n.x, n.y] != -1) continue;
                if (IsCellWall(n)) continue;

                distanceField[n.x, n.y] = cd + 1;
                q.Enqueue(n);
            }
        }

        Debug.Log("[SimManager] distanceField flood-fill selesai dibangun.");
    }

    // =========================================================
    //  MICROMOUSE MEMORY API (RUNTIME LOGGING)
    // =========================================================
    public void MarkVisitedCell(Vector2 worldPos)
    {
        Vector2Int cell = WorldToGrid(worldPos);

        if (!IsInsideGrid(cell)) return;
        if (IsCellWall(cell)) return;

        if (!cellMemory.TryGetValue(cell, out var mem))
        {
            mem = new CellMemory
            {
                isFree = true,
                isDoor = false,
                visited = false,
                visitCount = 0,
                roomId = -1,
                markerObj = null
            };
            cellMemory[cell] = mem;
        }

        if (mem.markerObj == null && visitedCellPrefab != null)
        {
            Vector2 center = GridToWorldCenter(cell);
            mem.markerObj = Instantiate(visitedCellPrefab, center, Quaternion.identity);

            // Penting: parent-kan ke visitedParent agar Hierarchy tidak penuh
            if (visitedParent != null)
            {
                mem.markerObj.transform.SetParent(visitedParent, true);
            }
        }

        mem.visited = true;
        mem.visitCount++;

        if (mem.markerObj != null)
        {
            var sr = mem.markerObj.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                int c = Mathf.Clamp(mem.visitCount, 1, 10);
                float t = (c - 1) / 9f;
                sr.color = Color.Lerp(visitColorMin, visitColorMax, t);
            }
        }
    }

    // Versi API lama (kalau ada script lain yang pakai)
    public void UpdateCellFromSensors(
        Vector2 sensedPos,
        float dFront, float dRight, float dBack, float dLeft,
        params object[] extra
    )
    {
        MarkVisitedCell(sensedPos);
        // Pakai orientasi default (up/right) karena tidak ada referensi drone
        UpdateOccupancyFromSensors(null, sensedPos, dFront, dRight, dBack, dLeft);
    }

    public int GetRoomIdAtWorldPos(Vector2 worldPos)
    {
        Vector2Int cell = WorldToGrid(worldPos);
        if (!cellMemory.TryGetValue(cell, out var mem)) return -1;
        if (!mem.isFree) return -1;
        return mem.roomId;
    }

    public int GetVisitCountAtWorldPos(Vector2 worldPos)
    {
        Vector2Int cell = WorldToGrid(worldPos);
        if (!cellMemory.TryGetValue(cell, out var mem)) return 0;
        return mem.visitCount;
    }

    // =========================================================
    //  GRID STEP LOGGING
    // =========================================================
    private Vector2Int? lastLoggedCell = null;

    public void ReportGridStep(
        Drone drone,
        Vector2 worldPos,
        float dFront,
        float dRight,
        float dBack,
        float dLeft,
        string decisionLabel)
    {
        Vector2Int cell = WorldToGrid(worldPos);

        // hanya log kalau pindah cell
        if (lastLoggedCell.HasValue && lastLoggedCell.Value == cell)
            return;

        lastLoggedCell = cell;

        Debug.Log(
            $"[LangkahGrid:{drone.name}] cell=({cell.x},{cell.y}) " +
            $"jarakDepan={dFront:F2} jarakKanan={dRight:F2} " +
            $"jarakBelakang={dBack:F2} jarakKiri={dLeft:F2} " +
            $"keputusan={decisionLabel}"
        );

        MarkVisitedCell(worldPos);
    }

    // =========================================================
    //  EVENTS FROM DRONE
    // =========================================================
    public void NotifyDroneReachedWaypoint(Drone drone)
    {
        if (drone == null) return;
        // Saat ini hanya logging, bisa dikembangkan
        Debug.Log($"[SimManager] Drone {drone.droneName} reached a waypoint.");
    }

    public void NotifyDroneReachedHome(Drone drone)
    {
        if (drone == null) return;

        Debug.Log($"[SimManager] Drone {drone.droneName} sudah sampai HomeBase.");

        // Cek apakah semua drone sudah di rumah
        bool allHome = true;
        foreach (var d in drones)
        {
            if (d == null) continue;
            if (!d.IsAtHome)
            {
                allHome = false;
                break;
            }
        }

        if (allHome)
        {
            Debug.Log("[SimManager] Semua drone sudah kembali ke HomeBase. Simulasi dapat dihentikan.");
            isPlaying = false;
        }
    }

    public void OnDroneFoundTarget(Drone drone)
    {
        if (drone == null) return;
        Debug.Log($"[SimManager] Drone {drone.droneName} menemukan target.");
        // Bisa dikembangkan: suruh semua pulang, dsb.
    }

    public void OnDroneEnterRoom(Drone drone, int roomIndex)
    {
        if (drone == null) return;
        Debug.Log($"[SimManager] Drone {drone.droneName} memasuki Room {roomIndex}");
    }

#if UNITY_EDITOR
    // =========================================================
    //  GIZMOS UNTUK GRID & HOME
    // =========================================================
    void OnDrawGizmosSelected()
    {
        if (homeBase != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawSphere(homeBase.position, 0.1f);
        }

        Gizmos.color = new Color(1f, 1f, 0f, 0.15f);

        float w = gridWidth * cellSize;
        float h = gridHeight * cellSize;

        Vector3 bottomLeft = new Vector3(
            gridWorldCenter.x - w / 2f,
            gridWorldCenter.y - h / 2f,
            0f
        );
        Vector3 topLeft = bottomLeft + new Vector3(0f, h, 0f);
        Vector3 topRight = bottomLeft + new Vector3(w, h, 0f);
        Vector3 bottomRight = bottomLeft + new Vector3(w, 0f, 0f);

        Gizmos.DrawLine(bottomLeft, topLeft);
        Gizmos.DrawLine(topLeft, topRight);
        Gizmos.DrawLine(topRight, bottomRight);
        Gizmos.DrawLine(bottomRight, bottomLeft);
    }
#endif
}