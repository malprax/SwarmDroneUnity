// using UnityEngine;
// using TMPro;
// using UnityEngine.UI;   // ⬅️ penting untuk akses Button & Image

// public class SimManager : MonoBehaviour
// {
//     [Header("Scene References")]
//     public Drone[] drones;
//     public Transform[] roomSpawnPoints;   // 3 spawn points, one per room
//     public SearchTarget target;

//     [Header("UI")]
//     public TMP_Text timerText;
//     public TMP_Text statusText;
//     public Button playButton;            // ⬅️ drag Button_Play ke sini di Inspector

//     float timer = 0f;
//     bool running = false;
//     bool found = false;
//     Drone leader;

//     void Start()
//     {
//         Debug.Log("[SimManager] Start()");
//         Time.timeScale = 1f; // pastikan waktu normal

//         AssignLeader();
//         ResetSim();          // ini juga akan meng-update warna tombol
//     }

//     void Update()
//     {
//         // ⬅️ TIDAK ADA INPUT MOUSE / KEYBOARD di sini
//         // Timer jalan murni pakai running & found saja
//         if (running && !found)
//         {
//             timer += Time.deltaTime;

//             if (timerText != null)
//                 timerText.text = "Time: " + timer.ToString("F1") + " s";
//         }
//     }

//     public void AssignLeader()
//     {
//         if (drones == null || drones.Length == 0)
//         {
//             Debug.LogWarning("[SimManager] AssignLeader() but drones array is empty");
//             return;
//         }

//         int index = Random.Range(0, drones.Length);

//         for (int i = 0; i < drones.Length; i++)
//         {
//             bool isL = (i == index);
//             drones[i].isLeader = isL;
//             drones[i].droneName = "Drone " + (i + 1);

//             if (isL) leader = drones[i];

//             Debug.Log($"[SimManager] Drone {i + 1} Leader={isL}");
//         }

//         Debug.Log($"[SimManager] Leader is: {leader.droneName}");
//     }

//     public void RandomizeObject()
//     {
//         Debug.Log("[SimManager] RandomizeObject()");

//         if (roomSpawnPoints == null || roomSpawnPoints.Length == 0 || target == null)
//         {
//             Debug.LogWarning("[SimManager] RandomizeObject(): missing references");
//             return;
//         }

//         Transform p = roomSpawnPoints[Random.Range(0, roomSpawnPoints.Length)];
//         target.transform.position = p.position;
//         target.gameObject.SetActive(true);

//         if (statusText != null)
//             statusText.text = "Object randomized";
//     }

//     public void Play()
//     {
//         // ⬅️ Cegah Play dipencet lebih dari sekali
//         if (running || found)
//         {
//             Debug.Log("[SimManager] Play() ignored (already running or finished)");
//             return;
//         }

//         Debug.Log("[SimManager] Play() CALLED");

//         timer = 0f;
//         found = false;
//         running = true;

//         if (timerText != null)
//             timerText.text = "Time: 0.0 s";

//         if (drones != null)
//         {
//             foreach (var d in drones)
//             {
//                 if (d != null)
//                     d.StartSearch();
//             }
//         }

//         if (statusText != null)
//             statusText.text = "Searching...";

//         UpdatePlayButtonState();   // ⬅️ ubah warna & kunci tombol
//     }

//     public void ResetSim()
//     {
//         Debug.Log("[SimManager] ResetSim()");

//         running = false;
//         found = false;
//         timer = 0f;

//         if (timerText != null)
//             timerText.text = "Time: 0.0 s";

//         if (drones != null)
//         {
//             foreach (var d in drones)
//             {
//                 if (d != null)
//                     d.ResetDrone();
//             }
//         }

//         if (target != null)
//             target.gameObject.SetActive(false);

//         AssignLeader();

//         if (statusText != null)
//             statusText.text = "press Play";

//         UpdatePlayButtonState();   // ⬅️ aktifkan lagi tombol Play (hijau)
//     }

//     public void ObjectFound(Drone d)
//     {
//         Debug.Log($"[SimManager] ObjectFound() by {d.droneName}");

//         if (found) return;

//         found = true;
//         running = false; // ⬅️ stop timer

//         string msg = (d == leader)
//             ? "Leader found the object"
//             : "Object found by member";

//         if (statusText != null)
//         {
//             statusText.text =
//                 "Object Found\n" +
//                 "Time: " + timer.ToString("F1") + " s\n" +
//                 msg;
//         }

//         if (drones != null)
//         {
//             foreach (var dr in drones)
//             {
//                 if (dr != null)
//                     dr.ReturnHome();
//             }
//         }

//         UpdatePlayButtonState();   // ⬅️ setelah mission selesai, Play tetap non-aktif
//     }

//     // 🔧 Atur warna & bisa/tidaknya tombol Play diklik
//     void UpdatePlayButtonState()
//     {
//         if (playButton == null) return;

//         var img = playButton.GetComponent<Image>();

//         // Kondisi: hanya boleh klik Play jika TIDAK sedang running dan TIDAK sudah found
//         bool canClick = !running && !found;

//         playButton.interactable = canClick;

//         if (img != null)
//         {
//             if (canClick)
//             {
//                 // Sebelum ditekan → hijau
//                 img.color = Color.green;
//             }
//             else
//             {
//                 // Setelah ditekan / sedang run / sudah selesai → abu-abu
//                 img.color = Color.gray;
//             }
//         }
//     }
// }


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