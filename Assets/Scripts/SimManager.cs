using UnityEngine;

public class SimManager : MonoBehaviour
{
    public Drone drone;
    public bool autoStart = true;

    [Header("Target (optional)")]
    public Transform target;

    private bool isPlaying;

    private void Awake()
    {
        if (drone == null) drone = Object.FindAnyObjectByType<Drone>();
        Debug.Log($"[SimManager] Awake. drone={(drone ? drone.name : "NULL")}");
    }

    private void Start()
    {
        if (autoStart) StartSimulation();
    }

    public void StartSimulation()
    {
        if (isPlaying) return;
        if (drone == null)
        {
            Debug.LogError("[SimManager] StartSimulation FAILED: drone is NULL.");
            return;
        }

        if (target != null) drone.target = target;

        isPlaying = true;
        Debug.Log($"[SimManager] Step-2 START. Drone={drone.name} pos={drone.transform.position}");
        Debug.Log("[SimManager] Step-2: Avoidance + smooth movement + target detection (LOS).");
    }
}