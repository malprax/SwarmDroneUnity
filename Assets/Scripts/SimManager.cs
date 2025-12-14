using UnityEngine;

public class SimManager : MonoBehaviour
{
    [Header("Refs (Scene)")]
    public Drone drone;
    public GridMap2D map;

    [Header("Objects (Scene)")]
    public Transform target;
    public Transform homeBase;

    [Header("Debug")]
    public bool verbose = true;

    private void Awake()
    {
        if (verbose)
            Debug.Log($"[SimManager] Awake. drone={(drone != null ? drone.droneName : "NULL")}");
    }

    private void Start()
    {
        StartSimulation();
    }

    public void StartSimulation()
    {
        // 1) Drone wajib ada
        if (drone == null)
        {
            // fallback: cari drone di scene
            drone = FindFirstObjectByType<Drone>();
            if (drone == null)
            {
                Debug.LogError("[SimManager] Drone is null (and not found in scene)!");
                return;
            }
        }

        // 2) Map fallback
        if (map == null)
        {
            map = FindFirstObjectByType<GridMap2D>();
            if (map == null)
            {
                Debug.LogError("[SimManager] GridMap2D map is null (and not found in scene)!");
                return;
            }
        }

        // 3) Navigator HARUS milik Drone itu sendiri (bukan reference bebas)
        DroneNavigator nav = drone.GetComponent<DroneNavigator>();
        if (nav == null)
        {
            Debug.LogError("[SimManager] DroneNavigator component is missing on Drone object!");
            return;
        }

        // 4) Inject semua (sekali, jelas)
        nav.map = map;

        drone.map = map;
        drone.navigator = nav;
        drone.target = target;
        drone.homeBase = homeBase;

        if (verbose)
        {
            Debug.Log($"[SimManager] START. Drone={drone.droneName} pos={drone.transform.position}");
            Debug.Log($"[SimManager] Inject target={(target ? target.name : "NULL")} home={(homeBase ? homeBase.name : "NULL")} map={(map ? map.name : "NULL")} nav={(nav ? nav.name : "NULL")}");
            Debug.Log("[SimManager] Mode: frontier search + LOS detect + A* return home.");
        }
    }
}