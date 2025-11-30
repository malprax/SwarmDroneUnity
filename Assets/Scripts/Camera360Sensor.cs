using UnityEngine;

public class Camera360Sensor : MonoBehaviour
{
    [Header("360 Scan")]
    public int rayCount = 16;
    public float maxDistance = 5f;
    public LayerMask detectMask;

    [Header("Output")]
    public float[] distances;  // jarak di tiap arah (0..360)

    void Awake()
    {
        if (rayCount < 1) rayCount = 1;
        distances = new float[rayCount];
    }

    void Update()
    {
        Vector2 origin = transform.position;
        float deltaAngle = 360f / rayCount;

        for (int i = 0; i < rayCount; i++)
        {
            float angle = deltaAngle * i * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

            RaycastHit2D hit = Physics2D.Raycast(origin, dir, maxDistance, detectMask);
            if (hit.collider != null)
            {
                distances[i] = hit.distance;
                Debug.DrawLine(origin, hit.point, Color.cyan);
            }
            else
            {
                distances[i] = maxDistance;
                Debug.DrawLine(origin, origin + dir * maxDistance, Color.blue);
            }
        }
    }
}