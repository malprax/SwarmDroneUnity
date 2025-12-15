using UnityEngine;

public class SimManager : MonoBehaviour
{
    [Header("Mission State")]
    public bool missionTargetFound = false;
    public Drone leaderDrone = null;
    public int arrivedCount = 0;

    [Header("Refs (Scene)")]
    public GridMap2D map;

    [Header("Team (3 drones)")]
    public Drone[] drones = new Drone[3]; // isi Drone1, Drone2, Drone3 di inspector
    public bool randomLeaderOnPlay = true;
    public int seed = 0; // 0 = random

    [Header("Fixed Start Positions (No Overlap)")]
    public bool forceRepositionOnStart = true;

    // Drone1 (-5.7,1.4), Drone2 (-3.7,1.4), Drone3 (-1.7,1.4)
    public Vector2[] startPositions = new Vector2[3]
    {
        new Vector2(-5.7f, 1.4f),
        new Vector2(-3.7f, 1.4f),
        new Vector2(-1.7f, 1.4f),
    };

    [Header("Objects (Scene)")]
    public Transform target;

    [Header("Debug")]
    public bool verbose = true;

    private void Start()
    {
        StartSimulation();
    }

    public void StartSimulation()
    {
        // reset mission state
        missionTargetFound = false;
        leaderDrone = null;
        arrivedCount = 0;

        // Map fallback
        if (map == null)
        {
            map = FindFirstObjectByType<GridMap2D>();
            if (map == null)
            {
                Debug.LogError("[SimManager] GridMap2D map is null (and not found in scene)!");
                return;
            }
        }

        // Pastikan 3 drone ada
        bool has3 = drones != null && drones.Length == 3 && drones[0] != null && drones[1] != null && drones[2] != null;
        if (!has3)
        {
            var found = FindObjectsByType<Drone>(FindObjectsSortMode.None);
            if (found.Length < 3)
            {
                Debug.LogError("[SimManager] Need 3 drones. Please assign drones[0..2] in Inspector.");
                return;
            }

            drones = new Drone[3] { found[0], found[1], found[2] };
        }

        // 1) Force start positions (no overlap)
        if (forceRepositionOnStart)
        {
            for (int i = 0; i < 3; i++)
            {
                var d = drones[i];
                if (d == null) continue;

                Vector2 startPos = startPositions[i];

                var rb = d.GetComponent<Rigidbody2D>();
                if (rb != null) rb.position = startPos;
                else d.transform.position = startPos;
            }
        }

        // 2) Inject map/nav/target/simManager + set returnPoint for each drone
        for (int i = 0; i < 3; i++)
        {
            var d = drones[i];
            if (d == null) continue;

            DroneNavigator nav = d.GetComponent<DroneNavigator>();
            if (nav == null)
            {
                Debug.LogError($"[SimManager] DroneNavigator missing on {d.name}!");
                continue;
            }

            nav.map = map;

            d.map = map;
            d.navigator = nav;
            d.target = target;
            d.simManager = this;

            // 🔥 ini penting: masing-masing drone pulang ke startnya sendiri
            d.SetReturnPoint(startPositions[i]);

            // reset state drone (biar aman jika play ulang)
            d.ResetMissionState();
        }

        // 3) Random leader + LED
        if (randomLeaderOnPlay)
        {
            int s = (seed == 0) ? System.Environment.TickCount : seed;
            Random.InitState(s);

            int leaderIndex = Random.Range(0, 3);
            for (int i = 0; i < 3; i++)
                drones[i].SetRole(i == leaderIndex ? Drone.DroneRole.Leader : Drone.DroneRole.Member);

            if (verbose) Debug.Log($"[SimManager] Leader={drones[leaderIndex].droneName} seed={s}");
        }

        if (verbose)
        {
            Debug.Log("[SimManager] START. 3 drones injected.");
            Debug.Log($"[SimManager] Target={(target ? target.name : "NULL")} Map={(map ? map.name : "NULL")}");
        }
    }

    // =========================
    // MISSION COMMAND
    // =========================
    public void OnAnyDroneFoundTarget(Drone finder)
    {
        if (missionTargetFound) return;

        missionTargetFound = true;
        leaderDrone = finder;

        Debug.Log($"[SimManager] TARGET FOUND by {finder.droneName}. All drones RETURN to their START points.");

        for (int i = 0; i < drones.Length; i++)
        {
            var d = drones[i];
            if (d == null) continue;
            d.ForceReturnToStart();
        }
    }

    public void NotifyDroneArrived(Drone d)
    {
        arrivedCount++;
        int total = (drones != null) ? drones.Length : 0;

        Debug.Log($"[SimManager] {d.droneName} ARRIVED ({arrivedCount}/{total})");

        if (total > 0 && arrivedCount >= total)
        {
            Debug.Log("[SimManager] ALL DRONES ARRIVED. MISSION COMPLETE.");
            Time.timeScale = 0f; // stop simulation
        }
    }
}