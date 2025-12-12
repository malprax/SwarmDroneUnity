using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public partial class Drone : MonoBehaviour
{
    [Header("Identity")]
    public string droneName = "Drone";

    [Header("Core Components")]
    public Rigidbody2D rb;
    public Collider2D bodyCollider;

    [Header("References")]
    public SimManager simManager;

    [Tooltip("Drag HomeBase transform ke sini (lebih aman daripada akses simManager.homeBase yang belum tentu ada).")]
    public Transform homeTransform;

    [Header("Movement State (shared)")]
    public Vector2 desiredDirection = Vector2.right;
    public float currentSpeed = 0f;

    protected bool isPaused = false;

    private void Awake()
    {
        if (rb == null) rb = GetComponent<Rigidbody2D>();
        if (bodyCollider == null) bodyCollider = GetComponent<Collider2D>();

        // 2D physics settings (Unity Rigidbody2D pakai drag, bukan linearDrag)
        rb.gravityScale = 0f;
        rb.angularDamping = 0f;
        rb.linearDamping = 0f;
        rb.freezeRotation = false;

        if (simManager == null)
            simManager = Object.FindFirstObjectByType<SimManager>();

        Debug.Log($"[Drone:{droneName}] Awake()");
    }

    private void Start()
    {
        Debug.Log($"[Drone:{droneName}] Start()");
        if (homeTransform == null && simManager != null)
        {
            // kalau SimManager punya transform home di scene, kamu set manual di inspector.
            Debug.LogWarning($"[Drone:{droneName}] homeTransform belum di-set. Set di Inspector untuk fitur ReturnHome.");
        }
    }

    private void Update()
    {
        if (isPaused) return;

        // SEARCH (harus ada di Drone.Search.cs)
        // Kalau file Search belum beres, minimal tidak bikin compile error:
        TryHandleSearchUpdate();

        // AVOIDANCE (harus ada di Drone.Avoidance.cs)
        HandleAvoidanceUpdate();
    }

    private void FixedUpdate()
    {
        if (isPaused) return;
        HandleMovementFixedUpdate();
    }

    // =========================================================
    // SAFE WRAPPER (biar compile aman)
    // =========================================================
    private void TryHandleSearchUpdate()
    {
        // Jika kamu sudah punya HandleSearchUpdate() di Drone.Search.cs,
        // hapus isi wrapper ini dan langsung panggil HandleSearchUpdate() dari Update().
        // Untuk sekarang: kita panggil metode internal yang pasti ada namanya.
        HandleSearchUpdate_Internal();
    }

    // Default kosong, supaya tidak error kalau Search belum lengkap
    // Nanti Drone.Search.cs akan override dengan partial method pattern (lihat catatan di bawah).
    private void HandleSearchUpdate_Internal()
    {
        // Jika kamu sudah implement SEARCH di Drone.Search.cs, hapus ini.
        // Default: tetap jalan lurus (desiredDirection sudah diolah Avoidance).
        if (desiredDirection.sqrMagnitude < 0.0001f)
            desiredDirection = transform.right;

        if (currentSpeed <= 0.01f)
            currentSpeed = 2.0f; // fallback speed sementara
    }

    // =========================================================
    // PUBLIC CONTROL API
    // =========================================================
    public void StopDrone()
    {
        isPaused = true;
        currentSpeed = 0f;
        if (rb != null) rb.linearVelocity = Vector2.zero;
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
    // HOME STATUS (tanpa bergantung ke simManager.homeBase)
    // =========================================================
    public bool IsAtHome()
    {
        Vector2 home = GetHomePosition();
        float d = Vector2.Distance(rb.position, home);
        return d < 0.25f;
    }

    public Vector2 GetHomePosition()
    {
        if (homeTransform != null) return homeTransform.position;
        // fallback kalau belum di-set
        return rb != null ? rb.position : (Vector2)transform.position;
    }
}