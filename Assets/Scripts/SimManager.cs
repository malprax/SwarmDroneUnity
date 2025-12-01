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

    [Header("Play / Stop Button")]
    // OnClick -> SimManager.OnPlayButton
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
    bool isPlaying = false;

    bool searching   = false;
    bool returning   = false;
    bool targetFound = false;

    float foundTimer  = 0f;
    float returnTimer = 0f;

    // hanya untuk log (optional)
    float simulationStartTime;

    // =========================================================
    //  LOG HELPER
    // =========================================================
    void LogState(string tag)
    {
        Debug.Log($"[SimManager:{tag}] " +
                  $"isPlaying={isPlaying}, " +
                  $"searching={searching}, returning={returning}, " +
                  $"targetFound={targetFound}, " +
                  $"foundTimer={foundTimer:0.00}, returnTimer={returnTimer:0.00}");
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

        RandomizeAll();          // sekali di awal
        ResetSimulation(true);   // posisi drone ke home

        InitUI();
        LogState("Start-End");
    }

    void Update()
    {
        UpdateTimers();
    }

    // =========================================================
    //  BUTTON EVENT (PLAY / STOP TOGGLE)
    // =========================================================

    public void OnPlayButton()
    {
        Debug.Log("[SimManager] OnPlayButton() clicked");
        LogState("Before-PlayButton");

        if (!isPlaying)
        {
            StartSimulation();   // Play
        }
        else
        {
            StopSimulation();    // Stop
        }

        LogState("After-PlayButton");
    }

    // =========================================================
    //  INIT / SETUP
    // =========================================================

    void AutoAssignTexts()
    {
        Debug.Log("[SimManager] AutoAssignTexts()");

        if (playText == null && playButton != null)
            playText = playButton.GetComponentInChildren<TMP_Text>(true);
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
                Debug.Log($"[SimManager]  Drone[{i}] name set to {drones[i].droneName}");
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

        // reset timer & posisi drone, TANPA pesan
        ResetSimulation(false);

        isPlaying           = true;
        searching           = true;
        targetFound         = false;
        returning           = false;
        simulationStartTime = Time.unscaledTime;

        StartAllSearch();

        if (playText != null) playText.text = "Stop";
        SetStatus("Searching...");

        LogState("StartSimulation-End");
    }

    void StopSimulation()
    {
        Debug.Log("[SimManager] StopSimulation()");
        Flash(playImage, playIdle);

        isPlaying = false;

        // kembali ke kondisi awal + pesan
        ResetSimulation(true);

        if (playText != null) playText.text = "Play";

        LogState("StopSimulation-End");
    }

    // =========================================================
    //  RANDOMIZER (DIPAKAI HANYA DI AWAL)
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
        if (drones == null || drones.Length == 0)
        {
            Debug.Log("[SimManager] RandomizeLeader() skipped (no drones).");
            return;
        }

        int idx = Random.Range(0, drones.Length);
        Debug.Log("[SimManager] RandomizeLeader() -> leader index " + idx);

        for (int i = 0; i < drones.Length; i++)
        {
            if (drones[i] == null) continue;
            drones[i].isLeader = (i == idx);
        }
    }

    void RandomizeTarget()
    {
        if (targetObject == null || targetSpawns == null || targetSpawns.Length == 0)
        {
            Debug.LogWarning("[SimManager] RandomizeTarget() skipped, targetObject/targetSpawns missing.");
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
        Time.timeScale = 1f;

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
        Debug.Log("[SimManager] StartAllSearch()");
        if (drones == null) return;

        foreach (var d in drones)
            if (d != null)
                d.StartSearch();
    }

    public void OnDroneFoundTarget(Drone d)
    {
        Debug.Log("[SimManager] OnDroneFoundTarget() from " + (d != null ? d.droneName : "null"));
        LogState("Before-OnDroneFoundTarget");

        if (targetFound) return;

        targetFound = true;
        searching   = false;
        returning   = true;
        returnTimer = 0f;

        SetStatus($"{d.droneName} found target. Returning...");

        if (drones != null)
        {
            foreach (var drone in drones)
                if (drone != null)
                    drone.ReturnHome();
        }

        LogState("After-OnDroneFoundTarget");
    }

    public void OnDroneReachedHome(Drone d)
    {
        Debug.Log("[SimManager] OnDroneReachedHome() from " + (d != null ? d.droneName : "null"));
        LogState("Before-OnDroneReachedHome");

        if (!returning || drones == null) return;

        foreach (var x in drones)
        {
            if (x == null) continue;
            if (!x.IsAtHome)
            {
                Debug.Log("[SimManager] OnDroneReachedHome() -> not all home yet.");
                return;
            }
        }

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
    //  ROLE VISUAL
    // =========================================================
    void InitRoles()
    {
        Debug.Log("[SimManager] InitRoles()");

        if (drones == null || drones.Length == 0)
        {
            if (leaderText != null) leaderText.text = "Leader: None";
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

        for (int i = 0; i < drones.Length; i++)
        {
            if (drones[i] == null) continue;
            drones[i].ApplyRoleVisual(leaderColor, memberColor);
        }

        if (leaderText != null)
            leaderText.text = $"Leader: Drone {leaderIndex + 1}";

        Debug.Log("[SimManager] InitRoles() -> leader index " + leaderIndex);
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
        yield return new WaitForSecondsRealtime(0.15f); // tidak terpengaruh Time.timeScale
        img.color = idle;
    }
}