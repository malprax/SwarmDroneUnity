using UnityEngine;

public class SimManager : MonoBehaviour
{
    [Header("Step-2 Settings (Non-Physics, 1 Drone)")]
    [Tooltip("Jika kosong, SimManager akan auto-detect Drone pertama di scene.")]
    public Drone drone;

    public Transform target;
    public Transform homeBase;

    [Tooltip("Auto start saat Play.")]
    public bool autoStart = true;

    [Header("Debug")]
    public bool verboseLog = true;

    private bool isPlaying = false;

    private void Awake()
    {
        if (drone == null)
        {
            drone = Object.FindAnyObjectByType<Drone>();
        }

        if (verboseLog)
            Debug.Log($"[SimManager] Awake. drone={(drone ? drone.name : "NULL")}");
    }

    private void Start()
    {
        if (!autoStart) return;
        StartSimulation();
    }

    public void StartSimulation()
    {
        if (isPlaying)
        {
            if (verboseLog) Debug.Log("[SimManager] StartSimulation() ignored, already playing.");
            return;
        }

        if (drone == null)
        {
            Debug.LogError("[SimManager] StartSimulation FAILED: drone is NULL. Pastikan ada object dengan script Drone di scene.");
            return;
        }

        // reset mission state
        drone.ResetMission();

        // ✅ capture start position sebagai HOME sebenarnya
        drone.CaptureStartHomeNow();

        // inject target/home (homeBase opsional — drone pulang ke startPos)
        if (target != null) drone.SetTarget(target);
        if (homeBase != null) drone.SetHome(homeBase);

        isPlaying = true;

        if (verboseLog)
        {
            Debug.Log($"[SimManager] Step-2 START. Drone={drone.name} pos={drone.transform.position}");
            Debug.Log($"[SimManager] Inject target={(target ? target.name : "NULL")} home={(homeBase ? homeBase.name : "NULL")}");
            Debug.Log("[SimManager] Step-2: Avoidance + smooth movement + target detection (LOS) + return to START position.");
        }
    }

    public void StopSimulation()
    {
        if (!isPlaying) return;

        if (drone != null) drone.StopMission();
        isPlaying = false;

        if (verboseLog) Debug.Log("[SimManager] STOP.");
    }

#if UNITY_EDITOR
    private void OnGUI()
    {
        const int w = 220;
        const int h = 36;
        int x = 10;
        int y = 10;

        if (!isPlaying)
        {
            if (GUI.Button(new Rect(x, y, w, h), "Start Step-2"))
                StartSimulation();
        }
        else
        {
            if (GUI.Button(new Rect(x, y, w, h), "Stop"))
                StopSimulation();
        }
    }
#endif
}