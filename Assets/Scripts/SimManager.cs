using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SimManager : MonoBehaviour
{
    [Header("Drones")]
    public Drone[] drones;

    [Header("Target")]
    public Transform targetObject;
    public Transform[] targetSpawns;

    [Header("UI")]
    public TMP_Text timeFoundText;
    public TMP_Text timeReturnText;
    public TMP_Text statusText;
    public TMP_Text leaderText;

    [Header("Role Colors")]
    public Color leaderColor = Color.red;
    public Color memberColor = Color.cyan;

    [Header("Buttons")]
    public Button randomButton;
    public Image randomButtonImage;

    [Tooltip("Warna tombol Random saat idle (kuning).")]
    public Color randomIdleColor = new Color(1f, 0.92f, 0.016f); // kuning emas

    [Tooltip("Warna tombol Random saat ditekan (abu-abu).")]
    public Color randomPressedColor = new Color(0.6f, 0.6f, 0.6f); // abu-abu

    // ======= TIMER STATE =======
    bool searchingPhase;
    bool targetFound;
    bool returnPhase;

    float foundTimer;
    float returnTimer;

    void Start()
    {
        InitRoles();
        ResetSimulationState();

        // Pastikan warna awal tombol Random = idle color
        if (randomButtonImage != null)
            randomButtonImage.color = randomIdleColor;
    }

    // =========================================================
    //  DIPANGGIL DARI TOMBOL UI
    // =========================================================

    public void Play()
    {
        ResetSimulationState();

        searchingPhase = true;
        targetFound = false;
        returnPhase = false;

        if (statusText != null)
            statusText.text = "Searching...";

        foreach (var d in drones)
            if (d != null)
                d.StartSearch();
    }

    public void ResetButton()
    {
        ResetSimulationState();
    }

    public void RandomButton()
    {
        StartCoroutine(RandomButtonRoutine());
    }

    // Coroutine untuk efek warna tombol Random
    private IEnumerator RandomButtonRoutine()
    {
        // Ubah warna jadi abu-abu saat ditekan
        if (randomButtonImage != null)
            randomButtonImage.color = randomPressedColor;

        // Tunggu 1 frame (biar UI sempat redraw)
        yield return null;

        // Jalankan logika random
        DoRandom();

        // Delay kecil supaya efek "tekan" kelihatan
        yield return new WaitForSeconds(0.15f);

        // Kembali ke warna kuning
        if (randomButtonImage != null)
            randomButtonImage.color = randomIdleColor;
    }

    void DoRandom()
    {
        // Random leader
        if (drones != null && drones.Length > 0)
        {
            int idx = UnityEngine.Random.Range(0, drones.Length);
            for (int i = 0; i < drones.Length; i++)
            {
                if (drones[i] == null) continue;
                drones[i].isLeader = (i == idx);
            }
        }

        InitRoles();

        // Random posisi target
        if (targetObject != null && targetSpawns != null && targetSpawns.Length > 0)
        {
            int spawnIdx = UnityEngine.Random.Range(0, targetSpawns.Length);
            targetObject.position = targetSpawns[spawnIdx].position;
        }

        // Reset waktu & posisi drone ke home
        ResetSimulationState();
    }

    // =========================================================
    //  TIMERS
    // =========================================================

    void Update()
    {
        if (searchingPhase && !targetFound)
        {
            foundTimer += Time.deltaTime;
            UpdateTimerUI();
        }

        if (returnPhase)
        {
            returnTimer += Time.deltaTime;
            UpdateTimerUI();
        }
    }

    void UpdateTimerUI()
    {
        if (timeFoundText != null)
            timeFoundText.text = $"Found: {foundTimer:0.0} s";

        if (timeReturnText != null)
        {
            float shown = returnPhase ? returnTimer : 0f;
            timeReturnText.text = $"Return: {shown:0.0} s";
        }
    }

    void ResetSimulationState()
    {
        searchingPhase = false;
        targetFound    = false;
        returnPhase    = false;

        foundTimer  = 0f;
        returnTimer = 0f;

        foreach (var d in drones)
            if (d != null)
                d.ResetDrone();

        UpdateTimerUI();

        if (statusText != null)
            statusText.text = "Press Play to start.";
    }

    // =========================================================
    //  ROLE / LEADER
    // =========================================================

    void InitRoles()
    {
        if (drones == null || drones.Length == 0)
            return;

        int leaderIndex = -1;

        // Cek apakah sudah ada isLeader = true
        for (int i = 0; i < drones.Length; i++)
        {
            if (drones[i] != null && drones[i].isLeader)
            {
                leaderIndex = i;
                break;
            }
        }

        if (leaderIndex < 0)
            leaderIndex = 0;

        for (int i = 0; i < drones.Length; i++)
        {
            if (drones[i] == null) continue;

            drones[i].isLeader = (i == leaderIndex);
            drones[i].ApplyRoleVisual(leaderColor, memberColor);
        }

        if (leaderText != null && drones[leaderIndex] != null)
            leaderText.text = $"Leader: {drones[leaderIndex].droneName}";
    }

    // =========================================================
    //  CALLBACK DARI DRONE & TARGET
    // =========================================================

    public void OnDroneFoundTarget(Drone d)
    {
        if (d == null) return;
        if (targetFound) return;   // hanya temuan pertama yang dihitung

        targetFound    = true;
        searchingPhase = false;
        returnPhase    = true;

        if (statusText != null)
            statusText.text = $"{d.droneName} found target. All drones returning to Home Base";

        returnTimer = 0f;
        UpdateTimerUI();

        foreach (var drone in drones)
            if (drone != null)
                drone.ReturnHome();
    }

    public void OnDroneReachedHome(Drone d)
    {
        if (!returnPhase) return;

        bool allHome = true;
        foreach (var drone in drones)
        {
            if (drone == null) continue;
            if (!drone.IsAtHome)
            {
                allHome = false;
                break;
            }
        }

        if (allHome)
        {
            returnPhase = false;

            if (statusText != null)
                statusText.text = "All drones at Home Base";

            UpdateTimerUI();
        }
    }
}