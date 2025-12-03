using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Menarik grafik speed per-drone menggunakan LineRenderer per drone.
/// - Pasang di GameObject "SpeedGraph"
/// - Isi array lineRenderers di Inspector (misal 3 untuk 3 drone)
/// - Graph digambar di local space objek ini (boleh dipindah / diparent ke kamera).
/// </summary>
public class SpeedGraphLineRenderer : MonoBehaviour
{
    [Header("References")]
    [Tooltip("SimManager yang menyimpan SpeedHistory & SampleTimes.")]
    public SimManager simManager;

    [Tooltip("LineRenderer untuk tiap drone (index 0 = Drone1, 1 = Drone2, 2 = Drone3).")]
    public LineRenderer[] lineRenderers;

    [Header("Graph Layout (Local Space)")]
    [Tooltip("Lebar graph (unit world / local).")]
    public float graphWidth = 5f;

    [Tooltip("Tinggi graph (unit world / local).")]
    public float graphHeight = 2f;

    void Awake()
    {
        // Kalau lupa di-assign di Inspector, coba cari otomatis 1x
        if (simManager == null)
        {
            simManager = FindFirstObjectByType<SimManager>();
            if (simManager == null)
            {
                Debug.LogWarning("[SpeedGraph] SimManager tidak ditemukan di scene.");
            }
        }
    }

    void LateUpdate()
    {
        if (simManager == null) return;
        if (simManager.SpeedHistory == null) return;
        if (simManager.SampleTimes == null) return;

        List<float> times = simManager.SampleTimes;
        if (times.Count < 2) return; // belum ada sampel cukup

        var histories = simManager.SpeedHistory;
        if (histories.Count == 0) return;

        int frameCount = times.Count;
        float tMax = times[frameCount - 1];
        if (tMax <= 0f) tMax = 1f;

        int droneCount = Mathf.Min(histories.Count, lineRenderers.Length);

        for (int d = 0; d < droneCount; d++)
        {
            LineRenderer lr = lineRenderers[d];
            if (lr == null) continue;

            List<float> speeds = histories[d];
            if (speeds == null || speeds.Count < 2)
            {
                lr.positionCount = 0;
                continue;
            }

            int n = Mathf.Min(frameCount, speeds.Count);
            lr.positionCount = n;

            for (int i = 0; i < n; i++)
            {
                // Normalisasi waktu (0..1) → posisi X
                float tNorm = times[i] / tMax;
                float x = -graphWidth * 0.5f + tNorm * graphWidth;

                // Normalisasi kecepatan (0..1) → posisi Y
                float s = speeds[i];
                float yNorm = (simManager.graphMaxSpeed > 0f)
                    ? Mathf.Clamp01(s / simManager.graphMaxSpeed)
                    : 0f;

                float y = -graphHeight * 0.5f + yNorm * graphHeight;

                // Posisi lokal terhadap GameObject SpeedGraph
                lr.SetPosition(i, new Vector3(x, y, 0f));
            }
        }
    }
}