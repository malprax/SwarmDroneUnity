using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class Drone : MonoBehaviour
{
    [Header("Role")]
    public bool isLeader = false;
    public string droneName = "Drone";

    [Header("Movement")]
    public float moveSpeed = 2f;
    public float directionChangeInterval = 1.5f;
    public float avoidanceRadius = 0.3f;

    Rigidbody2D rb;
    Vector2 currentDir;
    float dirTimer;

    bool searching = false;
    bool returningHome = false;
    Vector2 homePos;

    void Awake()
    {
        Debug.Log("[Drone] Awake(): " + droneName);

        rb = GetComponent<Rigidbody2D>();
        homePos = transform.position;
    }

    void Update()
    {
        if (!searching && !returningHome)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        if (returningHome)
        {
            Vector2 dir = (homePos - (Vector2)transform.position).normalized;
            rb.linearVelocity = dir * moveSpeed;

            Debug.Log("[Drone] " + droneName + " returning home");

            if (Vector2.Distance(transform.position, homePos) < 0.2f)
            {
                Debug.Log("[Drone] " + droneName + " arrived home");
                returningHome = false;
                rb.linearVelocity = Vector2.zero;
            }
            return;
        }

        // Random movement for searching
        dirTimer -= Time.deltaTime;
        if (dirTimer <= 0f)
        {
            currentDir = Random.insideUnitCircle.normalized;
            dirTimer = directionChangeInterval;

            Debug.Log("[Drone] " + droneName +
                      " new direction: " + currentDir);
        }

        rb.linearVelocity = currentDir * moveSpeed;
    }

    public void StartSearch()
    {
        Debug.Log("[Drone] StartSearch(): " + droneName);
        searching = true;
        returningHome = false;
    }

    public void ResetDrone()
    {
        Debug.Log("[Drone] ResetDrone(): " + droneName);
        searching = false;
        returningHome = false;

        rb.linearVelocity = Vector2.zero;

        transform.position = homePos;
    }

    public void ReturnHome()
    {
        Debug.Log("[Drone] ReturnHome(): " + droneName);
        searching = false;
        returningHome = true;
    }
}