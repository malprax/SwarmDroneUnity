using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public partial class Drone : MonoBehaviour
{
    // =========================================================
    // IDENTITY
    // =========================================================
    [Header("Identity")]
    public string droneName = "Drone";

    // =========================================================
    // CORE COMPONENTS
    // =========================================================
    [Header("Core Components")]
    public Rigidbody2D rb;
    public Collider2D bodyCollider;

    // =========================================================
    // MOVEMENT STATE (shared across partials)
    // =========================================================
    [Header("Movement State")]
    public Vector2 desiredDirection = Vector2.right;
    public float currentSpeed = 0f;

    protected bool isPaused = false;

    // =========================================================
    // REFERENCES
    // =========================================================
    [Header("References")]
    public SimManager simManager;

    // =========================================================
    // UNITY LIFECYCLE
    // =========================================================
    private void Awake()
    {
        // Rigidbody
        if (rb == null)
            rb = GetComponent<Rigidbody2D>();

        rb.gravityScale = 0f;
        rb.angularDamping = 0f;
        rb.linearDrag = 0f;
        rb.freezeRotation = false;

        // Collider
        if (bodyCollider == null)
            bodyCollider = GetComponent<Collider2D>();

        // SimManager (aman, versi baru Unity)
        if (simManager == null)
            simManager = Object.FindFirstObjectByType<SimManager>();

        Debug.Log($"[Drone:{droneName}] Awake()");
    }

    private void Start()
    {
        Debug.Log($"[Drone:{droneName}] Start()");
    }

    private void Update()
    {
        if (isPaused) return;

        // === LOGIKA HIGH LEVEL ===
        HandleSearchUpdate();        // dari Drone.Search.cs
        HandleAvoidanceUpdate();     // dari Drone.Avoidance.cs
    }

    private void FixedUpdate()
    {
        if (isPaused) return;

        HandleMovementFixedUpdate(); // dari Drone.Movement.cs
    }

    // =========================================================
    // PUBLIC CONTROL API (dipanggil SimManager)
    // =========================================================
    public void StopDrone()
    {
        isPaused = true;
        currentSpeed = 0f;
        if (rb != null)
            rb.linearVelocity = Vector2.zero;

        Debug.Log($"[Drone:{droneName}] STOP");
    }

    public void ResumeDrone()
    {
        isPaused = false;
        Debug.Log($"[Drone:{droneName}] RESUME");
    }

    public void ResetDrone()
    {
        isPaused = false;
        currentSpeed = 0f;
        desiredDirection = transform.right;

        Debug.Log($"[Drone:{droneName}] RESET");
    }

    // =========================================================
    // HOME / STATUS
    // =========================================================
    public bool IsAtHome()
    {
        if (simManager == null || simManager.homeBase == null)
            return false;

        float d = Vector2.Distance(rb.position, simManager.homeBase.position);
        return d < 0.25f;
    }
}