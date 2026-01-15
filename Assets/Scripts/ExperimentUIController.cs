using UnityEngine;
using UnityEngine.UI;

public class ExperimentUIController : MonoBehaviour
{
    [Header("Refs")]
    public SimManager sim;
    public Transform target;

    [Header("Buttons (Drag from Hierarchy)")]
    public Button btnRunOne;
    public Button btnRunBatch;
    public Button btnStop;
    public Button btnOpenCsv;

    [Header("Inputs (Drag from Hierarchy)")]
    public InputField inputRuns; // ✅ input jumlah run (Legacy InputField)

    [Header("Monte Carlo Settings")]
    [Min(2)] public int batchRuns = 30;
    [Min(0.1f)] public float batchTimeScale = 5f;
    public bool randomLeaderEachRun = true;
    public bool randomTargetEachRun = true;

    [Header("Target Random Area")]
    public Vector2 targetMin = new Vector2(-1f, -3f);
    public Vector2 targetMax = new Vector2(8f, 3f);

    private void Awake()
    {
        if (sim == null) sim = FindFirstObjectByType<SimManager>();

        if (btnRunOne != null) btnRunOne.onClick.AddListener(OnClickRunOne);
        if (btnRunBatch != null) btnRunBatch.onClick.AddListener(OnClickRunBatch);
        if (btnStop != null) btnStop.onClick.AddListener(OnClickStop);
        if (btnOpenCsv != null) btnOpenCsv.onClick.AddListener(OnClickOpenCsv);

        // Setup input default + numeric only
        if (inputRuns != null)
        {
            inputRuns.contentType = InputField.ContentType.IntegerNumber;
            inputRuns.characterLimit = 4; // max 1000
            inputRuns.text = Mathf.Clamp(batchRuns, 2, 1000).ToString();

            // Optional: jika user edit, simpan ke batchRuns (biar konsisten)
            inputRuns.onEndEdit.AddListener(OnRunsEndEdit);
        }
    }

    private void OnDestroy()
    {
        if (btnRunOne != null) btnRunOne.onClick.RemoveListener(OnClickRunOne);
        if (btnRunBatch != null) btnRunBatch.onClick.RemoveListener(OnClickRunBatch);
        if (btnStop != null) btnStop.onClick.RemoveListener(OnClickStop);
        if (btnOpenCsv != null) btnOpenCsv.onClick.RemoveListener(OnClickOpenCsv);

        if (inputRuns != null)
            inputRuns.onEndEdit.RemoveListener(OnRunsEndEdit);
    }

    private void OnRunsEndEdit(string _)
    {
        // normalize + simpan balik
        int runs = ReadRunsAndNormalize(writeBack: true);
        batchRuns = runs;
    }

    /// <summary>
    /// Baca input runs, aman untuk kasus input kosong / belum commit.
    /// </summary>
    private int ReadRunsAndNormalize(bool writeBack)
    {
        int runs = batchRuns;

        if (inputRuns != null)
        {
            // paksa UI update dari input yang sedang fokus
            inputRuns.ForceLabelUpdate();

            string s = (inputRuns.text ?? "").Trim();
            if (!string.IsNullOrEmpty(s) && int.TryParse(s, out int parsed))
                runs = parsed;
        }

        runs = Mathf.Clamp(runs, 2, 1000);

        if (writeBack && inputRuns != null)
            inputRuns.text = runs.ToString();

        return runs;
    }

    private void OnClickRunOne()
    {
        if (sim == null) { Debug.LogError("[ExperimentUI] SimManager not set."); return; }

        // De-focus input supaya UI tidak “nahan” nilai
        if (inputRuns != null) inputRuns.DeactivateInputField();

        Debug.Log("[ExperimentUI] Run One (Manual)...");
        sim.StartManualRun();
    }

    private void OnClickRunBatch()
    {
        if (sim == null) { Debug.LogError("[ExperimentUI] SimManager not set."); return; }

        // penting: de-focus dulu biar nilai input benar-benar kebaca
        if (inputRuns != null) inputRuns.DeactivateInputField();

        int runs = ReadRunsAndNormalize(writeBack: true);
        batchRuns = runs;

        float ts = Mathf.Max(0.1f, batchTimeScale);

        Debug.Log($"[ExperimentUI] Run Batch runs={runs} timeScale={ts} rndLeader={randomLeaderEachRun} rndTarget={randomTargetEachRun}");
        sim.StartBatchRun(runs, ts, randomLeaderEachRun, randomTargetEachRun, target, targetMin, targetMax);
    }

    private void OnClickStop()
    {
        if (sim == null) return;

        if (inputRuns != null) inputRuns.DeactivateInputField();

        Debug.Log("[ExperimentUI] Stop");
        sim.StopRun();
    }

    private void OnClickOpenCsv()
    {
        if (sim == null) return;

        if (inputRuns != null) inputRuns.DeactivateInputField();

        Debug.Log("[ExperimentUI] Open CSV");
        sim.OpenLastCsvFolder();
    }
}