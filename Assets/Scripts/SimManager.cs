using UnityEngine;
using TMPro;

public class SimManager : MonoBehaviour
{
    [Header("Scene References")]
    public Drone[] drones;
    public Transform[] roomSpawnPoints;   // 3 spawn points, satu per ruangan
    public SearchTarget target;

    [Header("UI")]
    public TMP_Text timerText;
    public TMP_Text statusText;

    float timer;
    bool running;
    bool found;
    bool hasRandomized;      // sudah tekan Random atau belum
    Drone leader;

    void Start()
    {
        AssignLeader();
        ResetSim();
    }

    void Update()
    {
        if (running && !found)
        {
            timer += Time.deltaTime;
            if (timerText != null)
                timerText.text = "Time: " + timer.ToString("F1") + " s";
        }
    }

    void AssignLeader()
    {
        if (drones == null || drones.Length == 0) return;

        int index = Random.Range(0, drones.Length);
        for (int i = 0; i < drones.Length; i++)
        {
            bool isL = (i == index);
            drones[i].isLeader = isL;
            drones[i].droneName = "Drone " + (i + 1);
            if (isL) leader = drones[i];
        }
    }

    // ================== DIPANGGIL DARI TOMBOL UI ==================

    // 🟨 Random
    public void RandomizeObject()
    {
        // hanya boleh sebelum Play dan sebelum ditemukan
        if (running || found || hasRandomized) return;
        if (roomSpawnPoints == null || roomSpawnPoints.Length == 0 || target == null) return;

        Transform p = roomSpawnPoints[Random.Range(0, roomSpawnPoints.Length)];
        target.transform.position = p.position;
        target.gameObject.SetActive(true);
        hasRandomized = true;

        if (statusText != null)
            statusText.text = "Target placed. Press Play to start.";

        Debug.Log("[SimManager] RandomizeObject → " + p.name);
    }

    // 🟩 Play
    public void Play()
    {
        if (running) return; // jangan bisa double start

        if (!hasRandomized)
        {
            if (statusText != null)
                statusText.text = "Press Random first to place the target.";
            Debug.LogWarning("[SimManager] Play pressed but target not randomized.");
            return;
        }

        timer = 0f;
        found = false;
        running = true;

        if (drones != null)
        {
            foreach (var d in drones)
                d.StartSearch();
        }

        if (statusText != null)
            statusText.text = "Searching...";

        Debug.Log("[SimManager] Simulation started.");
    }

    // 🟪 Reset
    public void ResetSim()
    {
        running = false;
        found = false;
        hasRandomized = false;
        timer = 0f;

        if (timerText != null)
            timerText.text = "Time: 0.0 s";

        if (drones != null)
        {
            foreach (var d in drones)
                d.ResetDrone();
        }

        if (target != null)
            target.gameObject.SetActive(false);

        AssignLeader();

        if (statusText != null)
            statusText.text = "Ready. Press Random to place the target.";

        Debug.Log("[SimManager] ResetSim() done.");
    }

    // Dipanggil Drone saat menemukan target
    public void ObjectFound(Drone d)
    {
        if (found) return;
        found = true;
        running = false;

        string msg = (d == leader)
            ? "Leader found the object"
            : "Object found by member";

        if (statusText != null)
        {
            statusText.text =
                "Object Found In: " + timer.ToString("F1") + " s\n" +
                msg + "\nAll drones returning to Home Base";
        }

        if (drones != null)
        {
            foreach (var dr in drones)
                dr.ReturnHome();
        }

        Debug.Log("[SimManager] ObjectFound by " + d.droneName);
    }
}