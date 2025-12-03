using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SimManager : MonoBehaviour
{
    // =========================================================
    //  PUBLIC REFERENCES
    // =========================================================

    [Header("Drones")]
    public Drone[] drones;

    [Header("Target")]
    public Transform targetObject;
    public Transform[] targetSpawns;

    [Header("Room Zones")]
    [Tooltip("Daftar ruangan (boleh dikosongkan, akan auto-find).")]
    public RoomZone[] roomZones;

    [Header("UI: Text")]
    public TMP_Text timeFoundText;
    public TMP_Text timeReturnText;
    public TMP_Text statusText;
    public TMP_Text leaderText;
    public TMP_Text objectText;
    public TMP_Text roomsVisitedText;

    [Header("Play Button")]
    public Button playButton;
    public Image playImage;
    public TMP_Text playText;

    [Header("Colors")]
    public Color playIdle = new Color(0.4f, 1f, 0.4f);
    public Color pressed  = new Color(0.6f, 0.6f, 0.6f);

    [Header("Role Colors")]
    public Color leaderColor = Color.red;
    public Color memberColor = Color.cyan;

    // =========================================================
    //  SPEED GRAPH / STATS
    // =========================================================
    [Header("Speed Graph / Stats")]
    [Tooltip("Batas atas grafik kecepatan (untuk normalisasi 0..1).")]
    public float graphMaxSpeed = 4f;

    // Riwayat waktu sample (detik sejak StartSimulation)
    public List<float> SampleTimes => _sampleTimes;

    // Riwayat kecepatan per-drone: SpeedHistory[droneIndex][sampleIndex]
    public List<List<float>> SpeedHistory => _speedHistory;

    // Riwayat jarak kumulatif per-drone (buat CSV)
    public List<List<float>> DistanceHistory => _distanceHistory;

    // =========================================================
    //  GRID / MICROMOUSE-STYLE MAPPING
    // =========================================================
    [System.Serializable]
    public class MazeCell
    {
        public bool visited;
        // dinding relatif terhadap orientasi drone saat pertama kali masuk
        public bool wallL, wallR, wallF, wallB;
    }

    [System.Serializable]
    public class GridStep
    {
        public int cellX;
        public int cellY;
        public bool wallL, wallR, wallF, wallB;
        public string decision;   // FWD / LEFT / RIGHT / BACK / IDLE
        public float time;        // waktu relatif dari start
    }

    [Header("Grid Mapping (Micromouse Style)")]
    [Tooltip("Lebar grid (jumlah kolom).")]
    public int gridWidth = 16;

    [Tooltip("Tinggi grid (jumlah baris).")]
    public int gridHeight = 10;

    [Tooltip("Ukuran 1 sel grid (unit dunia).")]
    public float cellSize = 1f;

    [Tooltip("Posisi world untuk sel (0,0) grid. Atur kira-kira di pojok kiri bawah arena.")]
    public Vector2 gridOrigin = new Vector2(-8f, -4f);

    [Tooltip("Jarak maksimum dianggap ada tembok (dari sensor).")]
    public float wallDetectDistance = 0.6f;

    MazeCell[,] _maze;
    List<GridStep> _allGridSteps = new List<GridStep>();
    Dictionary<Drone, List<GridStep>> _routeByDrone = new Dictionary<Drone, List<GridStep>>();

    // =========================================================
    //  INTERNAL STATE
    // =========================================================
    bool isPlaying   = false;
    bool searching   = false;
    bool returning   = false;
    bool targetFound = false;

    float foundTimer  = 0f;
    float returnTimer = 0f;

    float simulationStartTime = 0f;
    float missionEndTime      = 0f;

    // Room visited
    Dictionary<int, RoomZone> roomById  = new Dictionary<int, RoomZone>();
    HashSet<int> visitedRoomIds         = new HashSet<int>();

    // Speed history containers
    List<float> _sampleTimes           = new List<float>();
    List<List<float>> _speedHistory    = new List<List<float>>();
    List<List<float>> _distanceHistory = new List<List<float>>();

    // Meta info untuk CSV
    string _foundByDroneName = "";
    bool   _foundByLeader    = false;
    int    _targetRoomIndex  = -1;

    // =========================================================
    //  LOG HELPER
    // =========================================================
    void LogState(string tag)
    {
        Debug.Log($"[SimManager:{tag}] " +
                  $"isPlaying={isPlaying}, searching={searching}, returning={returning}, " +
                  $"targetFound={targetFound}, foundTimer={foundTimer:0.00}, returnTimer={returnTimer:0.00}");
    }

    // =========================================================
    //  UNITY LIFECYCLE
    // =========================================================
    void Start()
    {
        Debug.Log("[SimManager] Start()");

        Time.timeScale = 1f;

        AutoAssignTexts();
        AssignDroneNames();
        AutoCollectRooms();
        RandomizeAll();
        InitMaze();
        RebuildStatContainers();
        ResetSimulation(true);
        InitUI();

        LogState("Start-End");
    }

    void Update()
    {
        if (!isPlaying) return;

        UpdateTimers();
        SampleSpeeds();
    }

    // =========================================================
    //  PLAY BUTTON : TOGGLE PLAY / STOP
    // =========================================================
    public void OnPlayButton()
    {
        Debug.Log("[SimManager] OnPlayButton() clicked");

        if (!isPlaying)
        {
            StartSimulation();
        }
        else
        {
            StopSimulation();
        }
    }

    // =========================================================
    //  INIT / SETUP
    // =========================================================
    void AutoAssignTexts()
    {
        if (playText == null && playButton != null)
            playText = playButton.GetComponentInChildren<TMP_Text>(true);

        Debug.Log("[SimManager] AutoAssignTexts()");
    }

    void AssignDroneNames()
    {
        Debug.Log("[SimManager] AssignDroneNames()");
        if (drones == null) return;

        for (int i = 0; i < drones.Length; i++)
        {
            if (drones[i] != null)
                drones[i].droneName = $"Drone {i + 1}";
        }
    }

    void AutoCollectRooms()
    {
        // Pakai API baru: FindObjectsByType (menghindari warning CS0618)
        if (roomZones == null || roomZones.Length == 0)
        {
            roomZones = FindObjectsByType<RoomZone>(FindObjectsSortMode.None);
        }

        roomById.Clear();
        visitedRoomIds.Clear();

        if (roomZones != null)
        {
            foreach (var rz in roomZones)
            {
                if (rz == null) continue;

                if (!roomById.ContainsKey(rz.roomId))
                    roomById.Add(rz.roomId, rz);
                else
                    Debug.LogWarning($"[SimManager] Duplicate RoomId={rz.roomId} on {rz.name}");

                rz.visited = false;
            }
        }

        UpdateRoomsVisitedText();
        Debug.Log($"[SimManager] AutoCollectRooms() -> found {roomById.Count} rooms");
    }

    void InitUI()
    {
        Debug.Log("[SimManager] InitUI()");

        if (playImage != null) playImage.color = playIdle;
        if (playText  != null) playText.text  = "Play";

        SetStatus("Press Play to start.");
        UpdateTimerText();
        UpdateRoomsVisitedText();
    }

    // =========================================================
    //  MAZE / GRID INIT
    // =========================================================
    void InitMaze()
    {
        _maze = new MazeCell[gridWidth, gridHeight];
        for (int x = 0; x < gridWidth; x++)
        {
            for (int y = 0; y < gridHeight; y++)
            {
                _maze[x, y] = new MazeCell();
            }
        }

        _allGridSteps.Clear();
        _routeByDrone.Clear();
    }

    // Convert world position ke indeks grid (0..gridWidth-1, 0..gridHeight-1)
    bool WorldToCell(Vector2 worldPos, out int ix, out int iy)
    {
        float lx = (worldPos.x - gridOrigin.x) / cellSize;
        float ly = (worldPos.y - gridOrigin.y) / cellSize;

        ix = Mathf.FloorToInt(lx);
        iy = Mathf.FloorToInt(ly);

        if (ix < 0 || ix >= gridWidth || iy < 0 || iy >= gridHeight)
            return false;

        return true;
    }

    static int Bool01(bool b) => b ? 1 : 0;

    // Dipanggil oleh Drone setiap langkah (dari FixedUpdate)
    public void ReportGridStep(
        Drone d,
        Vector2 sensedPos,
        float dFront, float dRight, float dBack, float dLeft,
        string decisionLabel)
    {
        if (d == null) return;

        if (!WorldToCell(sensedPos, out int cx, out int cy))
            return; // di luar grid, abaikan

        bool wL = dLeft  < wallDetectDistance;
        bool wR = dRight < wallDetectDistance;
        bool wF = dFront < wallDetectDistance;
        bool wB = dBack  < wallDetectDistance;

        float tRel = (simulationStartTime > 0f)
            ? (Time.time - simulationStartTime)
            : 0f;

        var step = new GridStep
        {
            cellX   = cx,
            cellY   = cy,
            wallL   = wL,
            wallR   = wR,
            wallF   = wF,
            wallB   = wB,
            decision = decisionLabel,
            time    = tRel
        };

        _allGridSteps.Add(step);

        if (!_routeByDrone.TryGetValue(d, out var list))
        {
            list = new List<GridStep>();
            _routeByDrone[d] = list;
        }
        list.Add(step);

        // Simpan ke MazeCell hanya jika pertama kali dikunjungi
        var cell = _maze[cx, cy];
        if (!cell.visited)
        {
            cell.visited = true;
            cell.wallL = wL;
            cell.wallR = wR;
            cell.wallF = wF;
            cell.wallB = wB;
        }

        Debug.Log($"[GridStep] {d.droneName} cell=({cx},{cy}) " +
                  $"walls L={Bool01(wL)},R={Bool01(wR)},F={Bool01(wF)},B={Bool01(wB)} " +
                  $"decision={decisionLabel}");
    }

    // =========================================================
    //  STATS CONTAINERS (SPEED / DISTANCE)
    // =========================================================
    void RebuildStatContainers()
    {
        _sampleTimes = new List<float>();
        _speedHistory = new List<List<float>>();
        _distanceHistory = new List<List<float>>();

        int n = (drones != null) ? drones.Length : 0;
        for (int i = 0; i < n; i++)
        {
            _speedHistory.Add(new List<float>());
            _distanceHistory.Add(new List<float>());
        }
    }

    void SampleSpeeds()
    {
        if (drones == null || _speedHistory == null) return;
        if (_speedHistory.Count != drones.Length) return;

        float t = Time.time - simulationStartTime;
        float dt = (_sampleTimes.Count == 0) ? 0f : (t - _sampleTimes[_sampleTimes.Count - 1]);

        _sampleTimes.Add(t);

        for (int i = 0; i < drones.Length; i++)
        {
            float speed = 0f;
            if (drones[i] != null)
                speed = drones[i].CurrentVelocity.magnitude;

            _speedHistory[i].Add(speed);

            // jarak kumulatif = jarak sebelumnya + v * dt
            float lastDist = (_distanceHistory[i].Count > 0) ? _distanceHistory[i][_distanceHistory[i].Count - 1] : 0f;
            float newDist  = lastDist + speed * dt;
            _distanceHistory[i].Add(newDist);
        }
    }

    // =========================================================
    //  SIMULATION LIFECYCLE
    // =========================================================
    void StartSimulation()
    {
        Debug.Log("[SimManager] StartSimulation()");
        Flash(playImage, playIdle);

        InitMaze();              // reset peta untuk run baru
        RebuildStatContainers(); // siapkan statistik baru
        ResetSimulation(false);  // reset posisi/flag drone

        isPlaying   = true;
        searching   = true;
        returning   = false;
        targetFound = false;

        simulationStartTime = Time.time;
        missionEndTime      = 0f;

        _foundByDroneName = "";
        _foundByLeader    = false;

        StartAllSearch();

        if (playText != null) playText.text = "Stop";
        SetStatus("Searching...");

        LogState("StartSimulation-End");
    }

    void StopSimulation()
    {
        Debug.Log("[SimManager] StopSimulation()");
        Flash(playImage, playIdle);

        isPlaying = false;
        searching = false;
        returning = false;

        // Hentikan semua drone (tanpa reset posisi / statistik)
        if (drones != null)
        {
            foreach (var d in drones)
            {
                if (d == null) continue;

                var rb = d.GetComponent<Rigidbody2D>();
                if (rb == null) continue;

#if UNITY_6000_0_OR_NEWER
                rb.linearVelocity = Vector2.zero;
#else
                rb.velocity = Vector2.zero;
#endif
            }
        }

        if (playText != null) playText.text = "Play";
        SetStatus("Stopped. Press Play to start a new run.");

        LogState("StopSimulation-End");
    }

    void ResetSimulation(bool showMessage)
    {
        Debug.Log("[SimManager] ResetSimulation(showMessage=" + showMessage + ")");

        searching   = false;
        returning   = false;
        targetFound = false;

        foundTimer  = 0f;
        returnTimer = 0f;

        // Reset visited rooms
        visitedRoomIds.Clear();
        if (roomZones != null)
        {
            foreach (var rz in roomZones)
            {
                if (rz == null) continue;
                rz.visited = false;
            }
        }
        UpdateRoomsVisitedText();

        // Reset tiap drone ke home
        if (drones != null)
        {
            foreach (var d in drones)
                if (d != null)
                    d.ResetDrone();
        }

        UpdateTimerText();

        if (showMessage)
            SetStatus("Press Play to start.");

        LogState("ResetSimulation-End");
    }

    // =========================================================
    //  DRONE CALLBACKS
    // =========================================================
    void StartAllSearch()
    {
        if (drones == null) return;
        foreach (var d in drones)
            if (d != null)
                d.StartSearch();

        Debug.Log("[SimManager] StartAllSearch()");
    }

    public void OnDroneFoundTarget(Drone d)
    {
        if (targetFound) return;

        Debug.Log("[SimManager] OnDroneFoundTarget() by " + (d != null ? d.droneName : "null"));
        LogState("Before-OnDroneFoundTarget");

        targetFound = true;
        searching   = false;
        returning   = true;
        returnTimer = 0f;

        _foundByDroneName = d != null ? d.droneName : "Unknown";
        _foundByLeader    = (d != null && d.isLeader);

        SetStatus($"{_foundByDroneName} found target. Returning...");

        foreach (var x in drones)
            if (x != null)
                x.ReturnHome();

        LogState("After-OnDroneFoundTarget");
    }

    public void OnDroneReachedHome(Drone d)
    {
        LogState("Before-OnDroneReachedHome");
        if (!returning || drones == null) return;

        // Pastikan semua drone sudah di home
        foreach (var x in drones)
            if (!x.IsAtHome)
                return;

        returning = false;
        isPlaying = false;          // misi selesai → hentikan sampling otomatis
        missionEndTime = Time.time;

        SetStatus("All drones at Home Base (Mission Complete)");
        if (playText != null) playText.text = "Play";

        LogState("After-OnDroneReachedHome");
    }

    /// <summary>
    /// Dipanggil oleh RoomZone saat Drone masuk trigger ruangan.
    /// </summary>
    public void OnDroneEnterRoom(RoomZone zone, Drone d)
    {
        if (zone == null || d == null) return;

        if (!zone.visited)
        {
            zone.visited = true;
            visitedRoomIds.Add(zone.roomId);

            Debug.Log($"[SimManager] Room visited: {zone.roomName} (Id={zone.roomId}) by {d.droneName}");
            UpdateRoomsVisitedText();
        }
        else
        {
            Debug.Log($"[SimManager] {d.droneName} re-entered room: {zone.roomName} (Id={zone.roomId})");
        }
    }

    // =========================================================
    //  TIMERS
    // =========================================================
    void UpdateTimers()
    {
        if (searching)
        {
            foundTimer += Time.deltaTime;
            UpdateTimerText();
        }

        if (returning)
        {
            returnTimer += Time.deltaTime;
            UpdateTimerText();
        }
    }

    void UpdateTimerText()
    {
        if (timeFoundText != null)
            timeFoundText.text = $"Found: {foundTimer:0.0} s";

        if (timeReturnText != null)
            timeReturnText.text = $"Return: {returnTimer:0.0} s";
    }

    void UpdateRoomsVisitedText()
    {
        if (roomsVisitedText == null) return;

        if (roomZones == null || roomZones.Length == 0)
        {
            roomsVisitedText.text = "Rooms: (none)";
            return;
        }

        int total        = roomZones.Length;
        int visitedCount = visitedRoomIds.Count;

        roomsVisitedText.text = $"Rooms visited: {visitedCount}/{total}";
    }

    // =========================================================
    //  ROLES
    // =========================================================
    void InitRoles()
    {
        Debug.Log("[SimManager] InitRoles()");

        if (drones == null || drones.Length == 0)
        {
            if (leaderText != null)
                leaderText.text = "Leader: None";
            return;
        }

        int leaderIndex = -1;
        for (int i = 0; i < drones.Length; i++)
        {
            if (drones[i] != null && drones[i].isLeader)
            {
                leaderIndex = i;
                break;
            }
        }
        if (leaderIndex < 0) leaderIndex = 0;

        foreach (var d in drones)
            if (d != null)
                d.ApplyRoleVisual(leaderColor, memberColor);

        if (leaderText != null)
            leaderText.text = $"Leader: Drone {leaderIndex + 1}";
    }

    void RandomizeAll()
    {
        Debug.Log("[SimManager] RandomizeAll()");
        RandomizeLeader();
        RandomizeTarget();
        InitRoles();
    }

    void RandomizeLeader()
    {
        if (drones == null || drones.Length == 0) return;

        int idx = Random.Range(0, drones.Length);
        for (int i = 0; i < drones.Length; i++)
            drones[i].isLeader = (i == idx);
    }

    void RandomizeTarget()
    {
        if (targetObject == null || targetSpawns == null || targetSpawns.Length == 0)
        {
            if (objectText != null) objectText.text = "Object: None";
            _targetRoomIndex = -1;
            return;
        }

        int idx = Random.Range(0, targetSpawns.Length);
        targetObject.position = targetSpawns[idx].position;
        _targetRoomIndex = idx;

        if (objectText != null)
            objectText.text = $"Object: Room {idx + 1}";

        Debug.Log("[SimManager] RandomizeTarget() -> Room " + (idx + 1));
    }

    // =========================================================
    //  UI HELPERS
    // =========================================================
    void SetStatus(string msg)
    {
        if (statusText != null)
            statusText.text = msg;

        Debug.Log("[SimManager] STATUS: " + msg);
    }

    void Flash(Image img, Color idle)
    {
        if (img == null) return;
        StartCoroutine(FlashRoutine(img, idle));
    }

    IEnumerator FlashRoutine(Image img, Color idle)
    {
        img.color = pressed;
        yield return new WaitForSecondsRealtime(0.15f);
        img.color = idle;
    }

    // =========================================================
    //  EXPORT SPEED / DISTANCE KE CSV
    // =========================================================
    public void ExportSpeedCsv()
    {
        if (_sampleTimes == null || _sampleTimes.Count == 0)
        {
            Debug.LogWarning("[SimManager] ExportSpeedCsv(): no samples to export.");
            return;
        }

        int nDrones = (drones != null) ? drones.Length : 0;
        if (nDrones == 0)
        {
            Debug.LogWarning("[SimManager] ExportSpeedCsv(): no drones.");
            return;
        }

        string dir = Path.Combine(Application.dataPath, "SimExports");
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string fileName = $"swarm_speed_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv";
        string path = Path.Combine(dir, fileName);

        var sb = new StringBuilder();

        // Meta info (diawali #)
        float missionDuration =
            (missionEndTime > simulationStartTime && simulationStartTime > 0f)
            ? (missionEndTime - simulationStartTime)
            : (_sampleTimes[_sampleTimes.Count - 1]);

        sb.AppendLine("# Swarm drone mission log / speed & distance");
        sb.AppendLine("# FoundBy," + _foundByDroneName);
        sb.AppendLine("# FoundByIsLeader," + _foundByLeader);
        sb.AppendLine("# TargetRoomIndex," + _targetRoomIndex);
        sb.AppendLine("# MissionDuration," + missionDuration.ToString("F3") + " s");
        sb.AppendLine();

        // Header kolom
        sb.Append("time");
        for (int i = 0; i < nDrones; i++)
        {
            sb.Append($",speed_d{i + 1},dist_d{i + 1}");
        }
        sb.AppendLine();

        // Isi data per sample
        int sampleCount = _sampleTimes.Count;
        for (int k = 0; k < sampleCount; k++)
        {
            sb.Append(_sampleTimes[k].ToString("F4"));

            for (int i = 0; i < nDrones; i++)
            {
                float speed = 0f;
                float dist  = 0f;

                if (i < _speedHistory.Count && k < _speedHistory[i].Count)
                    speed = _speedHistory[i][k];

                if (i < _distanceHistory.Count && k < _distanceHistory[i].Count)
                    dist = _distanceHistory[i][k];

                sb.Append(",");
                sb.Append(speed.ToString("F4"));
                sb.Append(",");
                sb.Append(dist.ToString("F4"));
            }

            sb.AppendLine();
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        Debug.Log($"[SimManager] ExportSpeedCsv() saved to: {path}");
    }
}