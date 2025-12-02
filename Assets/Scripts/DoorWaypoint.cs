using UnityEngine;

public class DoorWaypoint : MonoBehaviour
{
    [Header("Door Connection")]
    public int fromRoomId;       // ruangan asal
    public int toRoomId;         // ruangan tujuan

    [Tooltip("Jika true, pintu bisa dilalui bolak-balik (dua arah).")]
    public bool bidirectional = true;

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(transform.position, 0.08f);

        // panah kecil arah toRoom
        Vector3 dir = Vector3.right * 0.4f;
        Gizmos.DrawLine(transform.position, transform.position + dir);
    }
#endif
}