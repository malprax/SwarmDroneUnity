using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SimManager : MonoBehaviour
{
    // =========================================================
    //  GLOBAL INSTANCE & STATE
    // =========================================================
    public static SimManager Instance { get; private set; }

    [Header("Simulation Control")]
    [Tooltip("Set true saat simulasi berjalan. Drone akan bergerak hanya jika IsPlaying == true.")]
    [SerializeField] private bool isPlaying = false;
    public bool IsPlaying => isPlaying;

    [Tooltip("Daftar semua drone di arena (boleh diisi manual atau auto-detect).")]
    public List<Drone> drones = new List<Drone>();

    [Header("Home Base & Grid Navigation")]
    [Tooltip("Transform HomeBase di tengah base (dipakai Drone & flood-fill).")]
    public Transform homeBase;

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

    // distanceField[x, y] = jarak langkah dari HomeBase (0 = home cell, -1 = tak terjangkau / wall)
    private int[,] distanceField;


    // Informasi memori untuk 1 sel grid
    // =========================================================
    //  MICROMOUSE-STYLE CELL MEMORY
    // =========================================================
    public class CellMemory
    {
        public bool visited = false;
        public int visitCount = 0;
        public int roomId = -1; // belum dikluster
    }

    private Dictionary<Vector2Int, CellMemory> cellMemory = new();
    // Debug: berapa cell yang sudah tersimpan di memori micromouse
[SerializeField] private int debugCellMemoryCount = 0;

    [Header("Room / Region Stats (Micromouse-style)")]
    [Tooltip("Jumlah room (cluster) yang berhasil dibentuk dari sel-sel visited.")]
    public int discoveredRoomCount = 0;
        [Header("Micromouse Runtime")]
    [Tooltip("Jika true, SimManager akan membentuk cluster room (roomId) secara berkala saat simulasi berjalan.")]
    public bool autoClusterRooms = true;

    [Tooltip("Interval (detik) untuk rebuild cluster room.")]
    public float clusterRebuildInterval = 0.5f;

    
    [Tooltip("Ringkasan jumlah sel di setiap roomId.")]
    public class RoomStats
    {
        public int roomId;
        public int cellCount;
    }

    // roomId → info ringkas (berapa sel dalam room)
    private Dictionary<int, RoomStats> roomStats =
        new Dictionary<int, RoomStats>();

    [Header("Debug Grid Logging")]
    [Tooltip("Jika true, setiap langkah drone akan di-log detail.")]
    public bool debugGridSteps = false;

    // =========================================================
    //  UNITY LIFECYCLE
    // =========================================================
    void Awake()
    {
        // Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Auto-collect drone kalau belum diisi (pakai API baru Unity)
        if (drones == null || drones.Count == 0)
        {
            var found = FindObjectsByType<Drone>(FindObjectsSortMode.None);
            drones = new List<Drone>(found);
        }

        // Build distance field flood-fill dari HomeBase
        BuildDistanceFieldFromHome();
    }

    void Start()
    {
        // Optional: mulai dalam keadaan berhenti
        isPlaying = false;
    }

    // =========================================================
    //  PUBLIC CONTROL (dipanggil dari UI / tombol / keyboard)
    // =========================================================
    public void StartSimulation()
    {
        isPlaying = true;

        // semua drone mulai search
        foreach (var d in drones)
        {
            if (d == null) continue;
            d.StartSearch();
        }

        Debug.Log("[SimManager] StartSimulation()");
    }

    public void StopSimulation()
    {
        isPlaying = false;

        foreach (var d in drones)
        {
            if (d == null) continue;
            d.StopDrone();
        }

        // Setelah simulasi berhenti, bentuk cluster room dari memori jelajah
        RebuildRoomClusters();

        Debug.Log("[SimManager] StopSimulation()");
    }

    public void ResetAllDrones()
    {
        foreach (var d in drones)
        {
            if (d == null) continue;
            d.ResetDrone();
        }

        Debug.Log("[SimManager] ResetAllDrones()");
    }


    public void CommandAllReturnHome()
    {
        foreach (var d in drones)
        {
            if (d == null) continue;
            d.StartReturnMission();
        }

        Debug.Log("[SimManager] CommandAllReturnHome()");
    }


    private float clusterTimer = 0f;

    void Update()
    {
        // Rebuild cluster room Micromouse secara berkala
        if (autoClusterRooms)
        {
            clusterTimer += Time.deltaTime;
            if (clusterTimer >= clusterRebuildInterval)
            {
                clusterTimer = 0f;
                AssignRoomIds();  // ini akan mengubah mem.roomId untuk sel yang visited
            }
        }
    }

    // OPTIONAL (dipakai RoomZone)
    public void OnDroneEnterRoom(Drone drone, int roomIndex)
    {
        if (drone == null) return;
        Debug.Log($"[SimManager] Drone {drone.droneName} memasuki Room {roomIndex}");
        // Nanti bisa dikembangkan: statistik waktu per room, dsb.
    }

    // =========================================================
    //  GRID COORD UTILITIES
    // =========================================================
    Vector2Int WorldToGrid(Vector2 worldPos)
    {
        // Geser supaya grid center jadi (0,0)
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

        // Sel dianggap wall jika ada collider pada wallLayerMask di radius kecil
        Vector2 center = GridToWorldCenter(c);
        Collider2D col = Physics2D.OverlapCircle(center, wallCheckRadius, wallLayerMask);
        return col != null;
    }

    // =========================================================
    //  BUILD FLOOD-FILL DISTANCE FIELD (HOME AS GOAL)
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
                distanceField[x, y] = -1; // -1 = belum dikunjungi / tak terjangkau
            }
        }

        Vector2Int homeCell = WorldToGrid(homeBase.position);
        if (!IsInsideGrid(homeCell))
        {
            Debug.LogWarning($"[SimManager] HomeBase di luar grid: {homeCell}");
            return;
        }

        // BFS queue
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

        // 4-neighborhood (up, down, left, right)
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
                if (distanceField[n.x, n.y] != -1) continue; // sudah dikunjungi
                if (IsCellWall(n)) continue;

                distanceField[n.x, n.y] = cd + 1;
                q.Enqueue(n);
            }
        }

        Debug.Log("[SimManager] distanceField flood-fill selesai dibangun.");
    }

    // =========================================================
    //  ARAH PULANG UNTUK DRONE (DIPAKAI DI Drone.HandleReturnHome)
    // =========================================================
    /// <summary>
    /// Mengembalikan vektor arah menuju home berdasarkan distanceField.
    /// Hanya dipakai saat fase Returning.
    /// </summary>
    public Vector2 GetReturnDirectionFor(Drone drone, Vector2 sensedPos)
    {
        if (distanceField == null || homeBase == null)
            return Vector2.zero;

        Vector2Int cell = WorldToGrid(sensedPos);
        if (!IsInsideGrid(cell))
            return Vector2.zero;

        int curDist = distanceField[cell.x, cell.y];
        if (curDist < 0)
            return Vector2.zero;

        // Cari neighbor dengan distance lebih kecil (mendekati home)
        Vector2Int bestNeighbor = cell;
        int bestDist = curDist;

        Vector2Int[] dirs = new Vector2Int[]
        {
            new Vector2Int( 1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int( 0, 1),
            new Vector2Int( 0,-1)
        };

        foreach (var d in dirs)
        {
            Vector2Int n = cell + d;
            if (!IsInsideGrid(n)) continue;

            int nd = distanceField[n.x, n.y];
            if (nd >= 0 && nd < bestDist)
            {
                bestDist = nd;
                bestNeighbor = n;
            }
        }

        if (bestNeighbor == cell)
        {
            // Tidak ada neighbor yang lebih dekat (mungkin sudah di home cell)
            return Vector2.zero;
        }

        Vector2 worldBest = GridToWorldCenter(bestNeighbor);
        Vector2 dir = worldBest - sensedPos;
        if (dir.sqrMagnitude < 1e-4f)
            return Vector2.zero;

        return dir.normalized;
    }

    // =========================================================
    //  MICROMOUSE MEMORY API
    // =========================================================
    /// <summary>
    /// Tandai cell grid yang sesuai posisi dunia sebagai "visited".
    /// Dipanggil oleh ReportGridStep() setiap kali drone melaporkan langkah.
    /// </summary>
    public void MarkVisitedCell(Vector2 worldPos)
{
    Vector2Int cell = WorldToGrid(worldPos);

    if (!IsInsideGrid(cell)) {
        Debug.Log($"[Micromouse] OUTSIDE GRID: world={worldPos} cell={cell}");
        return;
    }

    if (IsCellWall(cell)) {
        Debug.Log($"[Micromouse] CELL IS WALL: {cell}");
        return;
    }

    if (!cellMemory.TryGetValue(cell, out var mem))
    {
        mem = new CellMemory();
        cellMemory[cell] = mem;
        Debug.Log($"[Micromouse] NEW CELL: {cell}");
    }

    mem.visited = true;
    mem.visitCount++;

    debugCellMemoryCount = cellMemory.Count;
}
           /// <summary>
    /// Update memori sel berdasarkan posisi drone dan pembacaan sensor.
    /// 
    /// - Argumen WAJIB:
    ///     sensedPos : posisi drone (world space)
    ///     dFront    : jarak sensor depan
    ///     dRight    : jarak sensor kanan
    ///     dBack     : jarak sensor belakang
    ///     dLeft     : jarak sensor kiri
    ///
    /// - Argumen OPSIONAL (tambahan dari versi lama):
    ///     extra     : bisa berisi apa saja (string topo, state, dsb).
    ///                 Kita abaikan dulu, supaya kompatibel dengan semua
    ///                 versi Drone.cs yang pernah memanggil fungsi ini.
    ///
    /// Dipanggil dari Drone.FixedUpdate(). Saat ini minimal:
    ///     → menandai cell tempat drone berada sebagai visited.
    /// Nanti bisa dikembangkan untuk:
    ///     → menyimpan info dinding di 4 arah (N/E/S/W) ala micromouse.
    /// </summary>
    public void UpdateCellFromSensors(
        Vector2 sensedPos,
        float dFront, float dRight, float dBack, float dLeft,
        params object[] extra
    )
    {
        // Versi sederhana: cukup tandai cell yang sedang dioccupy sebagai visited.
        MarkVisitedCell(sensedPos);

        // NOTE:
        // 'extra' kita abaikan dulu. Di masa depan kita bisa pakai:
        // - extra[0] = topology (Open/Corridor/LeftWall/RightWall)
        // - extra[1] = mission state / semacamnya
        // - dst.
    }

    /// <summary>
    /// Mengambil roomId di posisi dunia tertentu.
    /// -1 jika belum pernah dikunjungi atau belum dibentuk cluster.
    /// </summary>
    public int GetRoomIdAtWorldPos(Vector2 worldPos)
    {
        Vector2Int cell = WorldToGrid(worldPos);
        if (!cellMemory.TryGetValue(cell, out var mem)) return -1;
        if (!mem.visited) return -1;
        return mem.roomId;
    }
    /// <summary>
/// Mengambil berapa kali cell di posisi dunia ini sudah dikunjungi drone.
/// Jika belum pernah, akan mengembalikan 0.
/// </summary>
public int GetVisitCountAtWorldPos(Vector2 worldPos)
{
    Vector2Int cell = WorldToGrid(worldPos);
    if (!cellMemory.TryGetValue(cell, out var mem)) return 0;
    return mem.visitCount;
}
    private void AssignRoomIds()
{
    // Jangan cluster kalau belum ada cell yang dikunjungi
    if (cellMemory.Count == 0)
    {
        discoveredRoomCount = 0;
        return;
    }

    // reset semua roomId dulu
    foreach (var kv in cellMemory)
    {
        kv.Value.roomId = -1;
    }

    // cluster id berjalan
    int nextRoomId = 0;

    // flood-fill manual
    foreach (var kv in cellMemory)
    {
        Vector2Int start = kv.Key;
        CellMemory mem = kv.Value;

        if (!mem.visited) continue;
        if (mem.roomId != -1) continue; // sudah dikluster

        // buat cluster baru
        FloodFillRoom(start, nextRoomId);
        nextRoomId++;
    }

    discoveredRoomCount = nextRoomId;
    Debug.Log($"[Micromouse] Room clustering selesai. totalRoom={discoveredRoomCount}");
}
private void FloodFillRoom(Vector2Int start, int roomId)
{
    Queue<Vector2Int> q = new();
    q.Enqueue(start);

    // assign ke cell awal
    if (cellMemory.TryGetValue(start, out var m0))
        m0.roomId = roomId;

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

        foreach (var d in dirs)
        {
            Vector2Int n = c + d;
            if (!cellMemory.TryGetValue(n, out var mem)) continue;
            if (!mem.visited) continue;
            if (mem.roomId != -1) continue;

            mem.roomId = roomId;
            q.Enqueue(n);
        }
    }
}

        // =========================================================
    //  ROOM CLUSTERING (MICROMOUSE-STYLE)
    // =========================================================
    /// <summary>
    /// Membentuk cluster room dari sel-sel visited.
    /// Setiap cluster diberi roomId (0,1,2,...) dan dihitung jumlah selnya.
    /// </summary>
    public void RebuildRoomClusters()
    {
        // Reset hitungan room & summary
        discoveredRoomCount = 0;
        roomStats.Clear();

        if (cellMemory == null || cellMemory.Count == 0)
        {
            Debug.Log("[SimManager] RebuildRoomClusters: belum ada sel visited.");
            return;
        }

        // 1) Reset semua roomId ke -1 dulu
        foreach (var kvp in cellMemory)
        {
            CellMemory mem = kvp.Value;
            if (mem != null)
            {
                mem.roomId = -1;
            }
        }

        // Neighbor 4-arah (atas, bawah, kiri, kanan)
        Vector2Int[] dirs = new Vector2Int[]
        {
            new Vector2Int( 1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int( 0, 1),
            new Vector2Int( 0,-1)
        };

        // 2) Scan semua sel visited, bentuk cluster BFS
        foreach (var kvp in cellMemory)
        {
            Vector2Int startCell = kvp.Key;
            CellMemory startMem = kvp.Value;

            // Hanya mulai cluster dari sel yang visited dan belum punya roomId
            if (startMem == null) continue;
            if (!startMem.visited) continue;
            if (startMem.roomId >= 0) continue;

            int currentRoomId = discoveredRoomCount;
            discoveredRoomCount++;

            // Buat RoomStats baru
            RoomStats stats = new RoomStats();
            stats.roomId = currentRoomId;
            stats.cellCount = 0;
            roomStats[currentRoomId] = stats;

            // BFS queue
            Queue<Vector2Int> q = new Queue<Vector2Int>();
            startMem.roomId = currentRoomId;
            q.Enqueue(startCell);
            stats.cellCount++;

            while (q.Count > 0)
            {
                Vector2Int c = q.Dequeue();

                foreach (var d in dirs)
                {
                    Vector2Int n = c + d;

                    if (!cellMemory.TryGetValue(n, out var nMem)) continue;
                    if (nMem == null) continue;
                    if (!nMem.visited) continue;
                    if (nMem.roomId >= 0) continue;

                    nMem.roomId = currentRoomId;
                    q.Enqueue(n);
                    stats.cellCount++;
                }
            }
        }

        Debug.Log($"[SimManager] RebuildRoomClusters selesai. discoveredRoomCount={discoveredRoomCount}");

        foreach (var kvp in roomStats)
        {
            Debug.Log($"[SimManager] Room {kvp.Key} cellCount={kvp.Value.cellCount}");
        }
    }

    // =========================================================
    //  GRID STEP LOGGING (MICROMOUSE-STYLE)
    // =========================================================
    /// <summary>
    /// Dipanggil Drone setiap step untuk mencatat sensor & keputusan ke grid.
    /// </summary>
    public void ReportGridStep(
    Drone drone,
    Vector2 sensedPos,
    float dFront, float dRight, float dBack, float dLeft,
    string decisionLabel)
{
    // WAJIB: selalu tandai cell visited dulu
    MarkVisitedCell(sensedPos);

    // Jika debug dimatikan, jangan log
    if (!debugGridSteps) return;

    string name = drone != null ? drone.droneName : "Unknown";

    Debug.Log(
        $"[GridStep:{name}] pos=({sensedPos.x:F2},{sensedPos.y:F2}) " +
        $"dF={dFront:F2} dR={dRight:F2} dB={dBack:F2} dL={dLeft:F2} " +
        $"dec={decisionLabel}"
    );
}

    // =========================================================
    //  EVENTS FROM DRONE
    // =========================================================
    public void OnDroneFoundTarget(Drone drone)
    {
        if (drone == null) return;

        Debug.Log($"[SimManager] Drone {drone.droneName} menemukan target.");
        // Nanti bisa: tandai waktu, suruh drone lain pulang, dsb.
    }

    public void OnDroneReachedHome(Drone drone)
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
            // Optional: auto stop
            // isPlaying = false;
        }
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

        // Gambarkan outline grid kasar (tidak semua sel, biar tidak berat)
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