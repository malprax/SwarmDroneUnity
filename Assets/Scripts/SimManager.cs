using UnityEngine;

public class SimManager : MonoBehaviour
{
    [Header("Step-1 Settings (Non-Physics, 1 Drone)")]
    [Tooltip("Jika kosong, SimManager akan auto-detect Drone pertama di scene.")]
    public Drone drone;

    [Tooltip("Auto start saat Play.")]
    public bool autoStart = true;

    [Tooltip("Optional: untuk validasi wall layer/mask dari manager (tidak wajib).")]
    public LayerMask wallMask;

    [Header("Debug")]
    public bool verboseLog = true;

    private bool isPlaying = false;

    private void Awake()
    {
        if (drone == null)
        {
            // Unity 2023+ (lebih cepat dibanding FindObjectOfType lama)
            drone = Object.FindAnyObjectByType<Drone>();
        }

        if (verboseLog)
        {
            Debug.Log($"[SimManager] Awake. drone={(drone ? drone.name : "NULL")}");
        }
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

        // Step-1: NON-PHYSICS
        // Stop/Resume cukup dengan enable/disable script Drone (karena movement ada di Update()).
        drone.enabled = true;

        isPlaying = true;

        if (verboseLog)
        {
            Debug.Log($"[SimManager] Step-1 START. Drone={drone.name} id={drone.GetInstanceID()} pos={drone.transform.position}");
            Debug.Log("[SimManager] Step-1: No grid, no rooms, no waypoints, no target. Only avoidance+movement (raycast).");
        }

        // Optional: validasi wallMask (tidak wajib)
        if (verboseLog && wallMask.value == 0)
        {
            Debug.LogWarning("[SimManager] wallMask masih 0 (None). Ini tidak masalah kalau Drone sudah punya wallLayerMask sendiri.");
        }
    }

    public void StopSimulation()
    {
        if (!isPlaying) return;

        if (drone != null) drone.enabled = false;
        isPlaying = false;

        if (verboseLog) Debug.Log("[SimManager] STOP.");
    }

#if UNITY_EDITOR
    private void OnGUI()
    {
        const int w = 180;
        const int h = 36;
        int x = 10;
        int y = 10;

        if (!isPlaying)
        {
            if (GUI.Button(new Rect(x, y, w, h), "Start Step-1"))
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