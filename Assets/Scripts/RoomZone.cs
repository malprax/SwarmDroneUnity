using UnityEngine;

/// <summary>
/// Zone ruangan sederhana. Untuk sekarang hanya mencatat di log
/// kalau ada Drone yang masuk, tanpa berhubungan dengan SimManager.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class RoomZone : MonoBehaviour
{
    [Tooltip("ID ruangan (misalnya 1, 2, 3). Saat ini hanya untuk debug/log.")]
    public int roomId = 0;

    private void Reset()
    {
        // Pastikan collider jadi trigger
        var col = GetComponent<Collider2D>();
        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        var drone = other.GetComponent<Drone>();
        if (drone == null) return;

        Debug.Log($"[RoomZone] Drone {drone.droneName} memasuki Room {roomId}");

        // DULUNYA: SimManager.Instance.OnDroneEnterRoom(this, drone, roomId);
        // Sekarang sengaja dimatikan, nanti kalau analisis per-ruang sudah siap baru kita hidupkan lagi.
    }
}