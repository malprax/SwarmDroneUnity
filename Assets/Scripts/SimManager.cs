using System;
using System.Collections;
using System.IO;
using UnityEngine;

public class SimManager : MonoBehaviour
{
    [Header("Mission State")]
    public bool missionTargetFound = false;
    public Drone leaderDrone = null;
    public int arrivedCount = 0;
    public bool missionComplete = false;

    [Header("Refs (Scene)")]
    public Drone drone;          // legacy
    public GridMap2D map;

    [Header("Team (3 drones)")]
    public Drone[] drones = new Drone[3];

    [Header("Fixed Start Positions (No Overlap)")]
    public bool useFixedStartPositions = true;

    public Vector2[] startPositions = new Vector2[3]
    {
        new Vector2(-5.7f, 1.4f),
        new Vector2(-3.7f, 1.4f),
        new Vector2(-1.7f, 1.4f),
    };

    [Header("Leader")]
    public bool randomLeaderOnPlay = true;
    public int seed = 0; // 0=random

    [Header("Objects (Scene)")]
    public Transform target;
    public Transform homeBase;

    [Header("Standby / Flow")]
    public bool startInStandby = true;

    [Header("Complete Behavior")]
    public bool autoPauseOnComplete = false; // MonteCarlo = false

    [Header("Batch Safety")]
    [Tooltip("Batas maksimum per-run (REAL TIME). Naikkan kalau ingin hampir tidak ada FAIL.")]
    public float runTimeoutSecondsReal = 120f;

    [Header("Random Target Validation")]
    [Tooltip("Wall/obstacle mask untuk cek target tidak spawn di dalam dinding.")]
    public LayerMask obstacleLayerMask;
    public float targetProbeRadius = 0.18f;
    public int targetResampleMax = 40;

    [Header("Debug")]
    public bool verbose = true;

    // CSV
    private string lastCsvPath = "";

    // batch control
    private Coroutine batchCo;
    private bool stopRequested = false;

    // per-run timing
    private float currentRunStartGameTime = 0f;
    private float currentRunStartRealTime = 0f;
    private float currentRunTimeToFind = -1f;

    private void Start()
    {
        // WAJIB biar coroutine jalan walau window tidak fokus
        Application.runInBackground = true;

        StartSimulation();
    }

    public void StartSimulation()
    {
        // reset mission state
        missionTargetFound = false;
        leaderDrone = null;
        arrivedCount = 0;
        missionComplete = false;

        // reset timing per run
        currentRunStartGameTime = Time.time;
        currentRunStartRealTime = Time.realtimeSinceStartup;
        currentRunTimeToFind = -1f;

        // map fallback
        if (map == null)
        {
            map = FindFirstObjectByType<GridMap2D>();
            if (map == null)
            {
                Debug.LogError("[SimManager] GridMap2D map is null!");
                return;
            }
        }

        // ensure 3 drones
        bool has3 = drones != null && drones.Length == 3 && drones[0] && drones[1] && drones[2];
        if (!has3)
        {
            var found = FindObjectsByType<Drone>(FindObjectsSortMode.None);
            if (found.Length < 3)
            {
                Debug.LogError("[SimManager] Need 3 drones!");
                return;
            }
            drones = new Drone[3] { found[0], found[1], found[2] };
        }

        if (drone == null) drone = drones[0]; // legacy

        // fixed start reposition + full reset physics + reset runtime drone
        for (int i = 0; i < 3; i++)
        {
            var d = drones[i];
            if (!d) continue;

            var rb = d.GetComponent<Rigidbody2D>();
            if (useFixedStartPositions && startPositions != null && startPositions.Length >= 3)
            {
                if (rb) rb.position = startPositions[i];
                else d.transform.position = startPositions[i];
            }

            // ✅ RESET runtime state drone (ini yang kemarin hilang)
            d.ResetRuntimeState();

            // ✅ reset fisika (biar tidak carry velocity/rot)
            if (rb)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
                rb.rotation = 0f;
            }
        }

        // inject refs per drone
        for (int i = 0; i < 3; i++)
        {
            var d = drones[i];
            if (!d) continue;

            DroneNavigator nav = d.GetComponent<DroneNavigator>();
            if (!nav)
            {
                Debug.LogError($"[SimManager] DroneNavigator missing on {d.name}");
                continue;
            }

            nav.map = map;

            d.map = map;
            d.navigator = nav;
            d.target = target;
            d.homeBase = homeBase;
            d.simManager = this;

            d.returnHomePos = startPositions[i];

            // standby
            d.SetStandby(startInStandby);

            // reset counters (CSV)
            d.wallCollisionCount = 0;
            d.droneCollisionCount = 0;

            // OPTIONAL: kalau kamu punya fungsi reset di navigator, panggil di sini
            // nav.ResetForNewRun();
        }

        if (randomLeaderOnPlay)
        {
            int s = (seed == 0) ? Environment.TickCount : seed;
            RandomizeLeader(s);
        }

        if (verbose)
        {
            Debug.Log($"[SimManager] READY (standby={startInStandby}).");
            Debug.Log($"[SimManager] Return points: D1={startPositions[0]} D2={startPositions[1]} D3={startPositions[2]}");
        }
    }

    // Manual Run
    public void StartManualRun()
    {
        stopRequested = false;
        if (batchCo != null) { StopCoroutine(batchCo); batchCo = null; }
        Time.timeScale = 1f;

        StartSimulation();
        BeginMission();
        Debug.Log("[SimManager] Manual Run started");
    }

    // Batch Run (legacy UI signature)
    public void StartBatchRun(
        int runs,
        float timeScale,
        bool randomLeaderEachRun,
        bool randomTargetEachRun,
        Transform targetTransform,
        Vector2 targetMin,
        Vector2 targetMax
    )
    {
        if (targetTransform != null) target = targetTransform;

        Vector2 initPos = target != null ? (Vector2)target.position : Vector2.zero;
        StartBatchRun(runs, timeScale, randomLeaderEachRun, randomTargetEachRun, initPos, targetMin, targetMax);
    }

    public void StartBatchRun(
        int runs,
        float timeScale,
        bool randomLeaderEachRun,
        bool randomTargetEachRun,
        Vector2 initialTargetPos,
        Vector2 targetMin,
        Vector2 targetMax
    )
    {
        stopRequested = false;
        if (batchCo != null) { StopCoroutine(batchCo); batchCo = null; }

        if (target != null) target.position = initialTargetPos;

        batchCo = StartCoroutine(BatchRoutine(runs, timeScale, randomLeaderEachRun, randomTargetEachRun, targetMin, targetMax));
        Debug.Log($"[SimManager] Batch Run started: runs={runs}, timeScale={timeScale}");
    }

    public void StopRun()
    {
        stopRequested = true;
        if (batchCo != null) { StopCoroutine(batchCo); batchCo = null; }
        Time.timeScale = 1f;
        Debug.Log("[SimManager] Run stopped");
    }

    public void OpenLastCsvFolder()
    {
        if (string.IsNullOrEmpty(lastCsvPath))
        {
            Debug.LogError("[SimManager] CSV not ready yet. Run Batch first!");
            return;
        }

        if (!File.Exists(lastCsvPath))
        {
            Debug.LogError($"[SimManager] CSV file NOT FOUND:\n{lastCsvPath}");
            return;
        }

        string folder = Path.GetDirectoryName(lastCsvPath);

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        System.Diagnostics.Process.Start("open", folder);
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        System.Diagnostics.Process.Start("explorer.exe", folder);
#else
        Application.OpenURL("file://" + folder);
#endif

        Debug.Log($"[SimManager] Opened CSV folder:\n{folder}");
    }

    public void BeginMission()
    {
        for (int i = 0; i < drones.Length; i++)
            if (drones[i] != null) drones[i].SetStandby(false);

        if (verbose) Debug.Log("[SimManager] MISSION START");
    }

    public void RandomizeLeader(int seedValue)
    {
        if (drones == null || drones.Length < 3) return;

        UnityEngine.Random.InitState(seedValue);
        int leaderIndex = UnityEngine.Random.Range(0, 3);

        for (int i = 0; i < 3; i++)
            drones[i].SetRole(i == leaderIndex ? Drone.DroneRole.Leader : Drone.DroneRole.Member);

        if (verbose) Debug.Log($"[SimManager] Leader={drones[leaderIndex].droneName} seed={seedValue}");
    }

    private Vector2 PickValidTarget(Vector2 min, Vector2 max)
    {
        // fallback
        if (obstacleLayerMask.value == 0)
        {
            float x0 = UnityEngine.Random.Range(min.x, max.x);
            float y0 = UnityEngine.Random.Range(min.y, max.y);
            return new Vector2(x0, y0);
        }

        for (int k = 0; k < Mathf.Max(1, targetResampleMax); k++)
        {
            float x = UnityEngine.Random.Range(min.x, max.x);
            float y = UnityEngine.Random.Range(min.y, max.y);
            Vector2 p = new Vector2(x, y);

            // ✅ tidak boleh spawn di dalam wall
            bool blocked = Physics2D.OverlapCircle(p, Mathf.Max(0.01f, targetProbeRadius), obstacleLayerMask) != null;
            if (!blocked) return p;
        }

        // kalau gagal terus, return random biasa (biar tidak hang)
        float xf = UnityEngine.Random.Range(min.x, max.x);
        float yf = UnityEngine.Random.Range(min.y, max.y);
        return new Vector2(xf, yf);
    }

    private IEnumerator BatchRoutine(
        int runs,
        float timeScale,
        bool randomLeaderEachRun,
        bool randomTargetEachRun,
        Vector2 targetMin,
        Vector2 targetMax
    )
    {
        string fileName = $"montecarlo_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid().ToString("N").Substring(0, 6)}.csv";
        lastCsvPath = Path.Combine(Application.persistentDataPath, fileName);

        Debug.Log($"[SimManager] CSV will be written to:\n{lastCsvPath}");

        StreamWriter sw = null;

        try
        {
            sw = new StreamWriter(lastCsvPath);
            sw.WriteLine("run,status,foundDrone,foundRole,timeToFind,timeTotal,targetX,targetY,wallCollisions,droneCollisions");
            sw.Flush();

            Time.timeScale = Mathf.Max(0.1f, timeScale);

            for (int r = 1; r <= runs; r++)
            {
                if (stopRequested) break;

                Debug.Log($"[SimManager] RUN {r}/{runs} START");

                StartSimulation();
                currentRunStartGameTime = Time.time;
                currentRunStartRealTime = Time.realtimeSinceStartup;
                currentRunTimeToFind = -1f;

                if (randomLeaderEachRun)
                {
                    int s = Environment.TickCount + r * 17;
                    RandomizeLeader(s);
                }

                if (randomTargetEachRun && target != null)
                {
                    Vector2 p = PickValidTarget(targetMin, targetMax);
                    target.position = p;
                }

                BeginMission();

                bool timeout = false;
                while (!missionComplete && !stopRequested)
                {
                    float realElapsed = Time.realtimeSinceStartup - currentRunStartRealTime;
                    if (realElapsed >= Mathf.Max(1f, runTimeoutSecondsReal))
                    {
                        timeout = true;
                        Debug.LogWarning($"[SimManager] RUN {r}/{runs} TIMEOUT after {realElapsed:F1}s -> mark FAIL and continue.");
                        break;
                    }
                    yield return null;
                }

                float totalTime = Time.time - currentRunStartGameTime;

                string foundDrone = leaderDrone ? leaderDrone.droneName : "NONE";
                string foundRole = leaderDrone ? leaderDrone.role.ToString() : "NONE";
                float timeToFind = (currentRunTimeToFind >= 0f) ? currentRunTimeToFind : totalTime;

                int wallCol = 0;
                int droneCol = 0;
                for (int i = 0; i < drones.Length; i++)
                {
                    if (!drones[i]) continue;
                    wallCol += drones[i].wallCollisionCount;
                    droneCol += drones[i].droneCollisionCount;
                }

                Vector2 tp = target ? (Vector2)target.position : Vector2.zero;
                string status = (!timeout && missionComplete) ? "OK" : "FAIL_TIMEOUT";

                sw.WriteLine($"{r},{status},{foundDrone},{foundRole},{timeToFind:F3},{totalTime:F3},{tp.x:F3},{tp.y:F3},{wallCol},{droneCol}");
                sw.Flush();

                Debug.Log($"[SimManager] RUN {r}/{runs} END status={status} total={totalTime:F2}s find={timeToFind:F2}s");

                yield return null;
            }
        }
        finally
        {
            if (sw != null)
            {
                sw.Flush();
                sw.Close();
                sw.Dispose();
            }

            Time.timeScale = 1f;
            batchCo = null;

            Debug.Log($"[SimManager] Batch finished. CSV saved at:\n{lastCsvPath}");
        }
    }

    // called by Drone
    public void OnAnyDroneFoundTarget(Drone finder)
    {
        if (missionTargetFound) return;

        missionTargetFound = true;
        leaderDrone = finder;

        if (currentRunTimeToFind < 0f)
            currentRunTimeToFind = Time.time - currentRunStartGameTime;

        Debug.Log($"[SimManager] TARGET FOUND by {finder.droneName}. All drones RETURN.");

        for (int i = 0; i < drones.Length; i++)
            if (drones[i] != null)
                drones[i].ForceReturnToHome();
    }

    public void NotifyDroneArrived(Drone d)
    {
        arrivedCount++;
        int total = drones != null ? drones.Length : 0;

        if (arrivedCount >= total)
        {
            missionComplete = true;
            Debug.Log("[SimManager] ALL DRONES ARRIVED. MISSION COMPLETE.");

            if (autoPauseOnComplete)
                Time.timeScale = 0f;
        }
    }
}