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
    public TMP_Text objectText;        // "Object: Room X"

    [Header("Role Colors")]
    public Color leaderColor = Color.red;
    public Color memberColor = Color.cyan;

    [Header("Buttons")]
    public Button playButton;
    public Image  playButtonImage;
    public Color  playIdleColor  = new Color(0.4f, 1f, 0.4f);      // hijau

    public Button resetButton;
    public Image  resetButtonImage;
    public Color  resetIdleColor = Color.blue;                     // biru

    public Button randomButton;
    public Image  randomButtonImage;
    public Color  randomIdleColor = new Color(1f, 0.92f, 0.016f);  // kuning

    public Color  pressedColor    = new Color(0.6f, 0.6f, 0.6f);   // abu-abu

    // ======= TIMER STATE =======
    bool searchingPhase;
    bool targetFound;
    bool returnPhase;

    float foundTimer;
    float returnTimer;

    void Start()
    {
        AssignDroneNames();

        // random sekali saat awal Play Mode (leader + room)
        InitialRandomSetup();
        InitRoles();
        ResetSimulationState();

        // pastikan label object sesuai posisi target awal
        UpdateObjectLabelFromTarget();

        // warna awal tombol
        if (playButtonImage  != null) playButtonImage.color  = playIdleColor;
        if (resetButtonImage != null) resetButtonImage.color = resetIdleColor;
        if (randomButtonImage!= null) randomButtonImage.color= randomIdleColor;
    }

    // =========================================================
    //  BUTTON EVENTS
    // =========================================================

    // ==== PLAY ====
    // Hook: Button_Play → SimManager.PlayButton
    public void PlayButton()
    {
        // efek warna
        if (playButtonImage) StartCoroutine(ButtonFlashRoutine(playButtonImage, playIdleColor));

        // logika utama
        ResetSimulationState();

        searchingPhase = true;
        targetFound    = false;
        returnPhase    = false;

        if (statusText)
            statusText.text = "Searching...";

        foreach (var d in drones)
            if (d != null)
                d.StartSearch();

        InitRoles();
        UpdateObjectLabelFromTarget();
    }

    // ==== RESET ====
    // Hook: Button_Reset → SimManager.ResetButton
    public void ResetButton()
    {
        if (resetButtonImage) StartCoroutine(ButtonFlashRoutine(resetButtonImage, resetIdleColor));

        ResetSimulationState();
        InitRoles();
        UpdateObjectLabelFromTarget();
    }

    // ==== RANDOM ====
    // Hook: Button_Random → SimManager.RandomButton
    public void RandomButton()
    {
        if (randomButtonImage) StartCoroutine(ButtonFlashRoutine(randomButtonImage, randomIdleColor));

        DoRandom();  // random sekali per klik
    }

    // Helper umum untuk kedip warna tombol
    IEnumerator ButtonFlashRoutine(Image img, Color idleColor)
    {
        if (img == null) yield break;

        Color original = idleColor;
        img.color = pressedColor;
        yield return new WaitForSeconds(0.15f);
        img.color = original;
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

        // Random posisi target + update label Object
        if (targetObject != null && targetSpawns != null && targetSpawns.Length > 0)
        {
            int spawnIdx = Random.Range(0, targetSpawns.Length);
            targetObject.position = targetSpawns[spawnIdx].position;
            UpdateObjectLabel(spawnIdx);
        }

        // Reset waktu & posisi drone ke home
        ResetSimulationState();
    }

    // =========================================================
    //  INITIAL RANDOM (saat baru masuk Play Mode Unity)
    // =========================================================

    void InitialRandomSetup()
    {
        // leader awal
        if (drones != null && drones.Length > 0)
        {
            int idx = Random.Range(0, drones.Length);
            for (int i = 0; i < drones.Length; i++)
            {
                if (drones[i] == null) continue;
                drones[i].isLeader = (i == idx);
            }
        }

        // room target awal
        if (targetObject != null && targetSpawns != null && targetSpawns.Length > 0)
        {
            int spawnIdx = Random.Range(0, targetSpawns.Length);
            targetObject.position = targetSpawns[spawnIdx].position;
            UpdateObjectLabel(spawnIdx);
        }
        else
        {
            if (objectText) objectText.text = "Object: None";
        }
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
    }

    // =========================================================
    //  ROLE / LEADER
    // =========================================================

    void AssignDroneNames()
    {
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
    //  OBJECT LABEL HELPERS
    // =========================================================

    void UpdateObjectLabelFromTarget()
    {
        if (targetObject == null || targetSpawns == null || targetSpawns.Length == 0)
        {
            if (objectText) objectText.text = "Object: None";
            return;
        }

        int index = -1;
        for (int i = 0; i < targetSpawns.Length; i++)
        {
            if (targetSpawns[i] == null) continue;
            if (Vector2.Distance(targetObject.position, targetSpawns[i].position) < 0.01f)
            {
                index = i;
                break;
            }
        }

        UpdateObjectLabel(index);
    }

    void UpdateObjectLabel(int roomIndex)
    {
        if (objectText == null) return;

        if (roomIndex < 0)
            objectText.text = "Object: None";
        else
            objectText.text = $"Object: Room {roomIndex + 1}";
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