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

    [Header("Buttons")]
    public Button playButton;
    public Button pauseButton;
    public Button randomButton;

    public Image playImage;
    public Image pauseImage;
    public Image randomImage;

    public TMP_Text playText;
    public TMP_Text pauseText;

    [Header("Colors")]
    public Color playIdle = new Color(0.4f, 1f, 0.4f);
    public Color pauseIdle = Color.blue;
    public Color randomIdle = new Color(1f, 0.92f, 0.016f);
    public Color pressed = new Color(0.6f, 0.6f, 0.6f);

    [Header("Role Colors")]
    public Color leaderColor = Color.red;
    public Color memberColor = Color.cyan;

    // =========================================================
    //  INTERNAL STATE
    // =========================================================
    bool isPlaying = false;
    bool isPaused = false;
    bool searching = false;
    bool returning = false;
    bool targetFound = false;

    float foundTimer = 0f;
    float returnTimer = 0f;

    // =========================================================
    //  UNITY START
    // =========================================================
    void Start()
    {
        Time.timeScale = 1f;

        AutoAssignTexts();
        AssignDroneNames();

        RandomizeAll();
        ResetSimulation(true);

        InitUI();
    }

    void Update()
    {
        UpdateTimers();
    }

    // =========================================================
    //  BUTTON EVENTS
    // =========================================================

    public void OnPlayButton()
    {
        if (!isPlaying)
            StartSimulation();
        else
            StopSimulation();
    }

    public void OnPauseButton()
    {
        TogglePause();
    }

    public void OnRandomButton()
    {
        RandomizeAll();
        ResetSimulation(true);
    }

    // =========================================================
    //  SUBROUTINE: UI INIT
    // =========================================================
    void AutoAssignTexts()
    {
        if (playText == null && playButton != null)
            playText = playButton.GetComponentInChildren<TMP_Text>();

        if (pauseText == null && pauseButton != null)
            pauseText = pauseButton.GetComponentInChildren<TMP_Text>();
    }

    void InitUI()
    {
        playImage.color = playIdle;
        pauseImage.color = pauseIdle;
        randomImage.color = randomIdle;

        playText.text = "Play";
        pauseText.text = "Pause";
    }

    // =========================================================
    //  SUBROUTINE: SIMULATION LIFECYCLE
    // =========================================================

    void StartSimulation()
    {
        Flash(playImage, playIdle);

        ResetSimulation(false);

        isPlaying = true;
        isPaused = false;

        searching = true;
        targetFound = false;
        returning = false;

        StartAllSearch();

        playText.text = "Stop";
        pauseText.text = "Pause";
        SetStatus("Searching...");
    }

    void StopSimulation()
    {
        Flash(playImage, playIdle);

        isPlaying = false;
        isPaused = false;

        ResetSimulation(true);

        playText.text = "Play";
        pauseText.text = "Pause";
    }

    void TogglePause()
    {
        if (!isPlaying) return;

        Flash(pauseImage, pauseIdle);

        isPaused = !isPaused;
        Time.timeScale = isPaused ? 0f : 1f;

        if (isPaused)
        {
            pauseText.text = "Resume";
            SetStatus("Paused");
        }
        else
        {
            pauseText.text = "Pause";
            SetStatus("Searching...");
        }
    }

    // =========================================================
    //  SUBROUTINE: RANDOMIZER
    // =========================================================
    void RandomizeAll()
    {
        RandomizeLeader();
        RandomizeTarget();
        InitRoles();
    }

    void RandomizeLeader()
    {
        int idx = Random.Range(0, drones.Length);
        for (int i = 0; i < drones.Length; i++)
            drones[i].isLeader = (i == idx);
    }

    void RandomizeTarget()
    {
        int idx = Random.Range(0, targetSpawns.Length);

        targetObject.position = targetSpawns[idx].position;
        objectText.text = $"Object: Room {idx + 1}";
    }

    // =========================================================
    //  SUBROUTINE: RESET
    // =========================================================
    void ResetSimulation(bool showMessage)
    {
        Time.timeScale = 1f;

        searching = false;
        returning = false;
        targetFound = false;

        foundTimer = 0;
        returnTimer = 0;

        foreach (var d in drones)
            d.ResetDrone();

        UpdateTimerText();

        if (showMessage)
            SetStatus("Press Play to start.");
    }

    // =========================================================
    //  SUBROUTINE: DRONES
    // =========================================================
    void StartAllSearch()
    {
        foreach (var d in drones)
            d.StartSearch();
    }

    // Called from Drone script
    public void OnDroneFoundTarget(Drone d)
    {
        if (targetFound) return;

        targetFound = true;
        searching = false;
        returning = true;

        returnTimer = 0;

        SetStatus($"{d.droneName} found target. Returning...");

        foreach (var drone in drones)
            drone.ReturnHome();
    }

    public void OnDroneReachedHome(Drone d)
    {
        if (!returning) return;

        foreach (var x in drones)
            if (!x.IsAtHome)
                return;

        returning = false;

        SetStatus("All drones at Home Base");
    }

    // =========================================================
    //  SUBROUTINE: TIMERS
    // =========================================================
    void UpdateTimers()
    {
        if (isPaused) return;

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
        timeFoundText.text = $"Found: {foundTimer:0.0} s";
        timeReturnText.text = $"Return: {returnTimer:0.0} s";
    }

    // =========================================================
    //  SUBROUTINE: ROLE VISUAL
    // =========================================================
    void InitRoles()
    {
        int leader = -1;

        for (int i = 0; i < drones.Length; i++)
            if (drones[i].isLeader) leader = i;

        if (leader < 0) leader = 0;

        for (int i = 0; i < drones.Length; i++)
            drones[i].ApplyRoleVisual(leaderColor, memberColor);

        leaderText.text = $"Leader: Drone {leader + 1}";
    }

    // =========================================================
    //  HELPER UI
    // =========================================================
    void SetStatus(string msg)
    {
        if (statusText != null)
            statusText.text = msg;
    }

    void Flash(Image img, Color idle)
    {
        StartCoroutine(FlashRoutine(img, idle));
    }

    IEnumerator FlashRoutine(Image img, Color idle)
    {
        img.color = pressed;
        yield return new WaitForSeconds(0.15f);
        img.color = idle;
    }
}