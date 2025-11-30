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
    public TMP_Text objectText;   // <-- teks "Object: Room X"

    [Header("Role Colors")]
    public Color leaderColor = Color.red;
    public Color memberColor = Color.cyan;

    [Header("Buttons")]
    public Button playButton;
    public Image playButtonImage;
    public Color playIdleColor = new Color(1f, 0.92f, 0.016f); // kuning

    public Button resetButton;
    public Image resetButtonImage;
    public Color resetIdleColor = Color.blue;                   // biru

    public Button randomButton;
    public Image randomButtonImage;
    public Color randomIdleColor = new Color(1f, 0.92f, 0.016f); // kuning

    public Color pressedColor = new Color(0.6f, 0.6f, 0.6f); // abu-abu

    // ======= TIMER STATE =======
    bool searchingPhase;
    bool targetFound;
    bool returnPhase;

    float foundTimer;
    float returnTimer;

    // ======= FLAG ANTI-SPAM BUTTON =======
    bool isPlayRunning;
    bool isResetRunning;
    bool isRandomRunning;

    void Start()
    {
        AssignDroneNames();
        InitRoles();
        ResetSimulationState();      // reset timer & drone

        // Set warna awal tombol
        if (playButtonImage  != null) playButtonImage.color  = playIdleColor;
        if (resetButtonImage != null) resetButtonImage.color = resetIdleColor;
        if (randomButtonImage!= null) randomButtonImage.color= randomIdleColor;

        UpdateObjectUI();            // sync teks Object pertama kali
    }

    // =========================================================
    //  BUTTON EVENTS
    // =========================================================

    public void Play()
    {
        if (!isPlayRunning)
            StartCoroutine(PlayButtonRoutine());
    }

    IEnumerator PlayButtonRoutine()
    {
        isPlayRunning = true;

        if (playButtonImage) playButtonImage.color = pressedColor;
        yield return null;

        // Reset waktu & drone ke home, tapi target tetap di posisi sekarang
        ResetSimulationState();
        UpdateObjectUI();   // <-- setelah reset, update teks Object sesuai posisi target

        searchingPhase = true;
        targetFound = false;
        returnPhase = false;

        if (statusText)
            statusText.text = "Searching...";

        foreach (var d in drones)
            if (d != null)
                d.StartSearch();

        InitRoles();

        yield return new WaitForSeconds(0.15f);
        if (playButtonImage) playButtonImage.color = playIdleColor;

        isPlayRunning = false;
    }

    public void ResetButton()
    {
        if (!isResetRunning)
            StartCoroutine(ResetButtonRoutine());
    }

    IEnumerator ResetButtonRoutine()
    {
        isResetRunning = true;

        if (resetButtonImage) resetButtonImage.color = pressedColor;
        yield return null;

        ResetSimulationState();
        InitRoles();
        UpdateObjectUI();   // <-- teks Object ikut di-refresh

        yield return new WaitForSeconds(0.15f);
        if (resetButtonImage) resetButtonImage.color = resetIdleColor;

        isResetRunning = false;
    }

    public void RandomButton()
    {
        if (!isRandomRunning)
            StartCoroutine(RandomButtonRoutine());
    }

    IEnumerator RandomButtonRoutine()
    {
        isRandomRunning = true;

        if (randomButtonImage) randomButtonImage.color = pressedColor;
        yield return null;

        DoRandom();

        yield return new WaitForSeconds(0.15f);
        if (randomButtonImage) randomButtonImage.color = randomIdleColor;

        isRandomRunning = false;
    }

    void DoRandom()
    {
        // Random leader
        if (drones != null && drones.Length > 0)
        {
            int idx = Random.Range(0, drones.Length);
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
            int spawnIdx = Random.Range(0, targetSpawns.Length);
            targetObject.position = targetSpawns[spawnIdx].position;
        }

        // Reset waktu & posisi drone ke home
        ResetSimulationState();

        // Setelah posisi target di-update, update label Object
        UpdateObjectUI();
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
        if (timeFoundText)
            timeFoundText.text = $"Found: {foundTimer:0.0} s";

        if (timeReturnText)
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

        if (statusText)
            statusText.text = "Press Play to start.";
        // NOTE: posisi target tidak diubah di sini
    }

    // =========================================================
    //  ROLE / LEADER
    // =========================================================

    void AssignDroneNames()
    {
        if (drones == null) return;

        for (int i = 0; i < drones.Length; i++)
        {
            if (drones[i] != null)
                drones[i].droneName = $"Drone {i + 1}";
        }
    }

    void InitRoles()
    {
        if (drones == null || drones.Length == 0)
        {
            if (leaderText) leaderText.text = "Leader: None";
            return;
        }

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

        if (leaderText != null)
            leaderText.text = $"Leader: Drone {leaderIndex + 1}";
    }

    // =========================================================
    //  OBJECT LABEL (ROOM 1 / 2 / 3)
    // =========================================================

    int GetActiveRoomIndex()
    {
        if (targetObject == null || targetSpawns == null || targetSpawns.Length == 0)
            return -1;

        int bestIndex = -1;
        float bestSqr = float.PositiveInfinity;

        Vector3 tp = targetObject.position;
        for (int i = 0; i < targetSpawns.Length; i++)
        {
            if (targetSpawns[i] == null) continue;
            float sqr = (tp - targetSpawns[i].position).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                bestIndex = i;
            }
        }

        // Kalau mau, bisa kasih threshold jarak di sini.
        return bestIndex;
    }

    void UpdateObjectUI()
    {
        if (objectText == null) return;

        int idx = GetActiveRoomIndex();
        if (idx < 0)
        {
            objectText.text = "Object: None";
        }
        else
        {
            objectText.text = $"Object: Room {idx + 1}";
        }
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