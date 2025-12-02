using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class RoomZone : MonoBehaviour
{
    public int roomId = 0;
    public string roomName = "Room";
    [HideInInspector] public bool visited = false;

    SimManager manager;

    void Awake()
    {
        manager = FindFirstObjectByType<SimManager>();

        // collider harus trigger
        var col = GetComponent<Collider2D>();
        if (col != null) col.isTrigger = true;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        var drone = other.GetComponent<Drone>();
        if (drone == null) return;

        manager?.OnDroneEnterRoom(this, drone);
    }
}