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
    Vector2 homePosition;

    SimManager manager;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        homePosition = transform.position;
        manager = FindObjectOfType<SimManager>();

        Debug.Log($"[Drone] Awake {name}, home={homePosition}");
    }

    void Update()
    {
        // Kalau tidak sedang apa-apa, diam
        if (!searching && !returningHome)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        // Mode pulang ke home base
        if (returningHome)
        {
            Vector2 toHome = homePosition - (Vector2)transform.position;

            if (toHome.magnitude < 0.1f)
            {
                returningHome = false;
                rb.linearVelocity = Vector2.zero;
                Debug.Log($"[Drone] {droneName} reached home");
                return;
            }

            rb.linearVelocity = toHome.normalized * moveSpeed;
            return;
        }

        // Mode mencari (searching)
        dirTimer -= Time.deltaTime;
        if (dirTimer <= 0f || currentDir == Vector2.zero)
        {
            currentDir = Random.insideUnitCircle.normalized;
            dirTimer = directionChangeInterval;
            Debug.Log($"[Drone] {droneName} new dir {currentDir}");
        }

        // Hindari tabrakan (sederhana)
        Vector2 avoidance = Vector2.zero;
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, avoidanceRadius);
        foreach (Collider2D h in hits)
        {
            if (h.attachedRigidbody != null && h.attachedRigidbody != rb)
            {
                Vector2 away = (Vector2)transform.position - (Vector2)h.transform.position;
                if (away.sqrMagnitude > 0.0001f)
                    avoidance += away.normalized;
            }
        }

        Vector2 moveDir = (currentDir + avoidance).normalized;
        rb.linearVelocity = moveDir * moveSpeed;
    }

    public void StartSearch()
    {
        searching = true;
        returningHome = false;
        dirTimer = 0f;
        Debug.Log($"[Drone] StartSearch() {droneName}");
    }

    public void ResetDrone()
    {
        searching = false;
        returningHome = false;
        transform.position = homePosition;
        rb.linearVelocity = Vector2.zero;
        Debug.Log($"[Drone] ResetDrone() {droneName}");
    }

    public void ReturnHome()
    {
        searching = false;
        returningHome = true;
        Debug.Log($"[Drone] ReturnHome() {droneName}");
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!searching) return;

        SearchTarget t = other.GetComponent<SearchTarget>();
        if (t != null && manager != null)
        {
            Debug.Log($"[Drone] {droneName} touched target");
            manager.ObjectFound(this);
        }
    }
}