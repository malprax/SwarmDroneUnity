using UnityEngine;

public class RangeSensor2D : MonoBehaviour
{
    [Header("Ray Settings")]
    public float maxDistance = 5f;
    public LayerMask obstacleMask;

    [Header("Output")]
    public float distance;      // jarak terdekat
    public bool hitSomething;   // kena atau tidak

    void Update()
    {
        Vector2 origin = transform.position;
        Vector2 dir = transform.right; // gunakan axis X lokal sebagai arah sensor

        RaycastHit2D hit = Physics2D.Raycast(origin, dir, maxDistance, obstacleMask);

        if (hit.collider != null)
        {
            hitSomething = true;
            distance = hit.distance;
            Debug.DrawLine(origin, hit.point, Color.yellow);
        }
        else
        {
            hitSomething = false;
            distance = maxDistance;
            Debug.DrawLine(origin, origin + dir * maxDistance, Color.gray);
        }
    }
}

// 1. tutup mesin
// 2. Rubber Nozzle
// 3. Nozzle
// 4. Selang 
// 5. Safety Propeller
// 6. Connecter Selang
// 7. Tangki Plastik
// 8. Tray baterai
// 9. Klem pipa kaki drone
// 10. kabel brushless motor
// 11. Rubber kaki drone
// 12 safety brushless
// 13. kaki drone
// 14. plastik knob baterai