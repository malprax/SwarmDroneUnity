using UnityEngine;

/// <summary>
/// Versi sederhana: belum terhubung ke SimManager.
/// Untuk sementara TIDAK menggambar grafik apa pun, hanya menyiapkan LineRenderer.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class SpeedGraphLineRenderer : MonoBehaviour
{
    private LineRenderer line;

    private void Awake()
    {
        line = GetComponent<LineRenderer>();
        if (line != null)
        {
            line.positionCount = 0;  // kosongkan garis
        }
    }

    private void LateUpdate()
    {
        // TODO (nanti): ambil data kecepatan dari SimManager / Drone lalu gambar grafik.
        // Sekarang dibiarkan kosong supaya tidak error dan tidak mengganggu simulasi utama.
    }
}