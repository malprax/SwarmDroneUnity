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

    [Header("UI Object Info")]
    public TMP_Text objectText;   // Text "Object : Room X"

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

    void Start()
    {
        // --- AUTO-DETECT objectText kalau belum diassign di Inspector ---
        if (objectText == null)
        {
            // cari semua TMP_Text dan ambil yang mengandung "Object"
            TMP_Text[] texts = FindObjectsOfType<TMP_Text>();
            foreach (var t in texts)
            {
                if (t.text.Contains("Object"))
                {
                    objectText = t;
                    Debug.Log($"[SimManager] Auto-linked objectText to '{t.gameObject.name}'");
                    break;
                }
            }
        }

        AssignDroneNames();
        InitRoles();
        ResetSimulationState();

        if (playButtonImage  != null) playButtonImage.color  = playIdleColor;
        if (resetButtonImage != null) resetButtonImage.color = resetIdleColor;
        if (randomButtonImage!= null) randomButtonImage.color= randomIdleColor;

        if (objectText != null)
            objectText.text = "Object : None";
    }

    // =========================================================
    //  BUTTON EVENTS
    // =========================================================

    public void Play()
    {
        StartCoroutine(PlayButtonRoutine());
    }

    IEnumerator PlayButtonRoutine()
    {
        if (playButtonImage) playButtonImage.color = pressedColor;
        yield return null;

        // Reset dulu waktu & posisi drone
        ResetSimulationState();

        // --- PILIH / ACAK ROOM UNTUK TARGET ---
        if (targetObject != null && targetSpawns != null && targetSpawns.Length > 0)
        {
            int spawnIdx = Random.Range(0, targetSpawns.Length);
            targetObject.position = targetSpawns[spawnIdx].position;
        }

        // Update label Object : Room X
        UpdateObjectRoomName();

        // Mulai fase searching
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
    }

    public void ResetButton()
    {
        StartCoroutine(ResetButtonRoutine());
    }

    IEnumerator ResetButtonRoutine()
    {
        if (resetButtonImage) resetButtonImage.color = pressedColor;
        yield return null;

        ResetSimulationState();
        InitRoles();

        if (objectText != null)
            objectText.text = "Object : None";

        yield return new WaitForSeconds(0.15f);
        if (resetButtonImage) resetButtonImage.color = resetIdleColor;
    }

    public void RandomButton()
    {
        StartCoroutine(RandomButtonRoutine());
    }

    IEnumerator RandomButtonRoutine()
    {
        if (randomButtonImage) randomButtonImage.color = pressedColor;
        yield return null;

        DoRandom();

        yield return new WaitForSeconds(0.15f);
        if (randomButtonImage) randomButtonImage.color = randomIdleColor;
    }

    void DoRandom()
    {
        // Reset waktu & posisi drone
        ResetSimulationState();

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

        // Update label Object : Room X
        UpdateObjectRoomName();
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
            timeFoundText.text = $"Found : {foundTimer:0.0}s";

        if (timeReturnText)
        {
            float shown = returnPhase ? returnTimer : 0f;
            timeReturnText.text = $"Return : {shown:0.0}s";
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
            if (leaderText) leaderText.text = "Leader : None";
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
            leaderText.text = $"Leader : Drone {leaderIndex + 1}";
    }

    // =========================================================
    //  OBJECT ROOM LABEL
    // =========================================================

    void UpdateObjectRoomName()
    {
        if (objectText == null || targetObject == null || targetSpawns == null || targetSpawns.Length == 0)
        {
            if (objectText != null)
                objectText.text = "Object : None";
            Debug.LogWarning("[SimManager] UpdateObjectRoomName skipped (missing reference).");
            return;
        }

        int closestIndex = -1;
        float bestDist = Mathf.Infinity;
        Vector3 pos = targetObject.position;

        for (int i = 0; i < targetSpawns.Length; i++)
        {
            if (targetSpawns[i] == null) continue;

            float d = Vector3.Distance(pos, targetSpawns[i].position);
            if (d < bestDist)
            {
                bestDist = d;
                closestIndex = i;
            }
        }

        if (closestIndex >= 0)
        {
            objectText.text = $"Object : Room {closestIndex + 1}";
            Debug.Log($"[SimManager] Object now in Room {closestIndex + 1}");
        }
        else
        {
            objectText.text = "Object : None";
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