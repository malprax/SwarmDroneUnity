using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SimManager : MonoBehaviour
{
    // =========================================================
    //  PUBLIC REFERENCES
    // =========================================================

    [Header("Drones")]
    public Drone[] drones;

    [Header("Target")]
    public Transform targetObject;
    public Transform[] targetSpawns;

    [Header("UI: Text")]
    public TMP_Text timeFoundText;
    public TMP_Text timeReturnText;
    public TMP_Text statusText;
    public TMP_Text leaderText;
    public TMP_Text objectText;

    [Header("Play Button")]
    public Button playButton;
    public Image playImage;
    public TMP_Text playText;

    [Header("Colors")]
    public Color playIdle = new Color(0.4f, 1f, 0.4f);
    public Color pressed  = new Color(0.6f, 0.6f, 0.6f);

    [Header("Role Colors")]
    public Color leaderColor = Color.red;
    public Color memberColor = Color.cyan;

    // =========================================================
    //  INTERNAL STATE
    // =========================================================
    bool isPlaying   = false;

    bool searching   = false;
    bool returning   = false;
    bool targetFound = false;

    float foundTimer  = 0f;
    float returnTimer = 0f;

    float simulationStartTime = 0f;

    // =========================================================
    //  LOG HELPER
    // =========================================================
    void LogState(string tag)
    {
        Debug.Log($"[SimManager:{tag}] " +
                  $"isPlaying={isPlaying}, searching={searching}, returning={returning}, " +
                  $"targetFound={targetFound}, foundTimer={foundTimer:0.00}, returnTimer={returnTimer:0.00}");
    }

    // =========================================================
    //  UNITY LIFECYCLE
    // =========================================================
    void Start()
    {
        Debug.Log("[SimManager] Start()");

        Time.timeScale = 1f;

        AutoAssignTexts();
        AssignDroneNames();

        RandomizeAll();          // pilih leader & target sekali
        ResetSimulation(true);   // kembalikan drone ke home

        InitUI();

        LogState("Start-End");
    }

    void Update()
    {
        UpdateTimers();
    }

    // =========================================================
    //  PLAY BUTTON (HANYA START, TIDAK ADA STOP)
    // =========================================================
    public void OnPlayButton()
    {
        Debug.Log("[SimManager] OnPlayButton() clicked");

        // Kalau simulasi sudah berjalan → abaikan klik apa pun
        if (isPlaying)
        {
            Debug.Log("[SimManager] OnPlayButton ignored: already playing");
            return;
        }

        LogState("Before-PlayButton");

        // Mulai simulasi satu kali
        StartSimulation();

        LogState("After-PlayButton");
    }

    // =========================================================
    //  INIT / SETUP
    // =========================================================
    void AutoAssignTexts()
    {
        if (playText == null && playButton != null)
            playText = playButton.GetComponentInChildren<TMP_Text>(true);

        Debug.Log("[SimManager] AutoAssignTexts()");
    }

    void AssignDroneNames()
    {
        Debug.Log("[SimManager] AssignDroneNames()");

        if (drones == null) return;

        for (int i = 0; i < drones.Length; i++)
        {
            if (drones[i] != null)
            {
                drones[i].droneName = $"Drone {i + 1}";
                Debug.Log($"[SimManager] Drone[{i}] name set to {drones[i].droneName}");
            }
        }
    }

    void InitUI()
    {
        Debug.Log("[SimManager] InitUI()");

        if (playImage != null) playImage.color = playIdle;
        if (playText  != null) playText.text  = "Play";

        SetStatus("Press Play to start.");
        UpdateTimerText();
    }

    // =========================================================
    //  SIMULATION LIFECYCLE
    // =========================================================
    void StartSimulation()
    {
        Debug.Log("[SimManager] StartSimulation()");
        Flash(playImage, playIdle);

        // Reset posisi & timer tapi tanpa pesan
        ResetSimulation(false);

        isPlaying   = true;
        searching   = true;
        returning   = false;
        targetFound = false;

        simulationStartTime = Time.unscaledTime;

        StartAllSearch();

        // Teks tombol boleh tetap "Play" (tidak toggle)
        if (playText != null) playText.text = "Play";
        SetStatus("Searching...");

        LogState("StartSimulation-End");
    }

    // Disimpan kalau nanti mau dipakai tombol Reset terpisah
    void StopSimulation()
    {
        Debug.Log("[SimManager] StopSimulation()");
        Flash(playImage, playIdle);

        isPlaying = false;

        ResetSimulation(true);

        if (playText != null) playText.text = "Play";

        LogState("StopSimulation-End");
    }

    // =========================================================
    //  RANDOMIZER (ONLY AT START)
    // =========================================================
    void RandomizeAll()
    {
        Debug.Log("[SimManager] RandomizeAll()");
        RandomizeLeader();
        RandomizeTarget();
        InitRoles();
    }

    void RandomizeLeader()
    {
        if (drones == null || drones.Length == 0) return;

        int idx = Random.Range(0, drones.Length);
        Debug.Log("[SimManager] RandomizeLeader() -> " + idx);

        for (int i = 0; i < drones.Length; i++)
            drones[i].isLeader = (i == idx);
    }

    void RandomizeTarget()
    {
        if (targetObject == null || targetSpawns == null || targetSpawns.Length == 0)
        {
            if (objectText != null) objectText.text = "Object: None";
            return;
        }

        int idx = Random.Range(0, targetSpawns.Length);
        targetObject.position = targetSpawns[idx].position;

        if (objectText != null)
            objectText.text = $"Object: Room {idx + 1}";

        Debug.Log("[SimManager] RandomizeTarget() -> Room " + (idx + 1));
    }

    // =========================================================
    //  RESET
    // =========================================================
    void ResetSimulation(bool showMessage)
    {
        Debug.Log("[SimManager] ResetSimulation(showMessage=" + showMessage + ")");

        searching   = false;
        returning   = false;
        targetFound = false;

        foundTimer  = 0f;
        returnTimer = 0f;

        if (drones != null)
        {
            foreach (var d in drones)
                if (d != null)
                    d.ResetDrone();
        }

        UpdateTimerText();

        if (showMessage)
            SetStatus("Press Play to start.");

        LogState("ResetSimulation-End");
    }

    // =========================================================
    //  DRONE CALLBACKS
    // =========================================================
    void StartAllSearch()
    {
        if (drones == null) return;

        foreach (var d in drones)
            if (d != null)
                d.StartSearch();

        Debug.Log("[SimManager] StartAllSearch()");
    }

    public void OnDroneFoundTarget(Drone d)
    {
        if (targetFound) return;

        Debug.Log("[SimManager] OnDroneFoundTarget() by " + (d != null ? d.droneName : "null"));
        LogState("Before-OnDroneFoundTarget");

        targetFound = true;
        searching   = false;
        returning   = true;
        returnTimer = 0f;

        SetStatus($"{d.droneName} found target. Returning...");

        foreach (var x in drones)
            if (x != null)
                x.ReturnHome();

        LogState("After-OnDroneFoundTarget");
    }

    public void OnDroneReachedHome(Drone d)
    {
        LogState("Before-OnDroneReachedHome");

        if (!returning || drones == null) return;

        foreach (var x in drones)
            if (!x.IsAtHome) return;

        returning = false;
        SetStatus("All drones at Home Base");
        LogState("After-OnDroneReachedHome");
    }

    // =========================================================
    //  TIMERS
    // =========================================================
    void UpdateTimers()
    {
        if (!isPlaying) return;

        if (searching)
        {
            foundTimer += Time.deltaTime;
            UpdateTimerText();
        }

        if (returning)
        {
            returnTimer += Time.deltaTime;
            UpdateTimerText();
        }
    }

    void UpdateTimerText()
    {
        if (timeFoundText != null)
            timeFoundText.text = $"Found: {foundTimer:0.0} s";

        if (timeReturnText != null)
            timeReturnText.text = $"Return: {returnTimer:0.0} s";
    }

    // =========================================================
    //  ROLES
    // =========================================================
    void InitRoles()
    {
        Debug.Log("[SimManager] InitRoles()");

        if (drones == null || drones.Length == 0)
        {
            if (leaderText != null)
                leaderText.text = "Leader: None";
            return;
        }

        int leaderIndex = -1;
        for (int i = 0; i < drones.Length; i++)
        {
            if (drones[i] != null && drones[i].isLeader)
            {
                leaderIndex = i;
                break;
            }
        }

        if (leaderIndex < 0) leaderIndex = 0;

        foreach (var d in drones)
            if (d != null)
                d.ApplyRoleVisual(leaderColor, memberColor);

        if (leaderText != null)
            leaderText.text = $"Leader: Drone {leaderIndex + 1}";
    }

    // =========================================================
    //  UI HELPERS
    // =========================================================
    void SetStatus(string msg)
    {
        if (statusText != null)
            statusText.text = msg;

        Debug.Log("[SimManager] STATUS: " + msg);
    }

    void Flash(Image img, Color idle)
    {
        if (img == null) return;
        StartCoroutine(FlashRoutine(img, idle));
    }

    IEnumerator FlashRoutine(Image img, Color idle)
    {
        img.color = pressed;
        yield return new WaitForSecondsRealtime(0.15f);
        img.color = idle;
    }
}