using UnityEngine;

/// <summary>
/// Drone NON-PHYSICS (Transform-based)
/// - Collision: Physics2D.CircleCast + sliding
/// - Avoidance: smooth (no zig-zag)
/// - Target detection: distance + LOS raycast (non-physics logic)
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class Drone : MonoBehaviour
{
    public enum DroneStatus { SEARCH, AVOID, STUCK, FOUND }

    [Header("Identity")]
    public string droneName = "Drone1";

    [Header("Movement")]
    public float moveSpeed = 2.0f;
    public float turnSpeedDeg = 220f;

    [Tooltip("Semakin besar, arah lebih halus (tidak zig-zag). 6-14 recommended.")]
    public float steeringSmoothing = 10f;

    [Header("Wall Avoidance")]
    public LayerMask wallLayerMask;
    public float sensorRange = 1.5f;
    public float wallHardDistance = 0.55f;
    public float wallSoftDistance = 1.10f;

    [Tooltip("Tambahan jarak aman supaya body tidak masuk tembok.")]
    public float skin = 0.01f;

    [Header("Exploration")]
    [Range(0f, 1f)]
    public float randomSteerStrength = 0.12f;

    [Header("Anti-Stuck")]
    public float stuckTimeThreshold = 1.2f;
    public float minMoveDelta = 0.003f;

    [Header("Target Detection (LOS)")]
    public Transform target;
    public float detectRange = 6f;
    public float foundDistance = 0.35f;

    [Header("Debug")]
    public bool verbose = false;

    // internal
    private Collider2D col;
    private float bodyRadius;
    private Vector2 desiredDirection;
    private Vector2 smoothedDirection;

    private Vector2 lastPos;
    private float stuckTimer;

    private DroneStatus status = DroneStatus.SEARCH;
    private bool found = false;

    private void Awake()
    {
        col = GetComponent<Collider2D>();
        bodyRadius = EstimateBodyRadius(col) + skin;

        desiredDirection = transform.right;
        smoothedDirection = desiredDirection;

        lastPos = transform.position;

        Debug.Log($"[Drone:{droneName}] Awake() bodyRadius={bodyRadius:F3}");

        // depenetrate ringan di awal (kalau spawn mepet tembok)
        for (int i = 0; i < 6; i++) DepenetrateFromWalls();
    }

    private void Start()
    {
        Debug.Log($"[Drone:{droneName}] Start()");
    }

    private void Update()
    {
        if (found) return;

        // 1) target detection (distance + LOS)
        if (TryDetectTargetLOS())
        {
            found = true;
            SetStatus(DroneStatus.FOUND);
            return;
        }

        // 2) compute desired direction (avoidance smooth)
        ComputeDesiredDirection();

        // 3) smooth direction to avoid zig-zag
        float t = 1f - Mathf.Exp(-steeringSmoothing * Time.deltaTime);
        smoothedDirection = Vector2.Lerp(smoothedDirection, desiredDirection, t);
        if (smoothedDirection.sqrMagnitude < 0.0001f) smoothedDirection = desiredDirection;
        smoothedDirection.Normalize();

        // 4) move with collision (circlecast + slide)
        Vector2 delta = (Vector2)transform.right * (moveSpeed * Time.deltaTime);

        // rotate towards smoothedDirection
        RotateTowards(smoothedDirection);

        // move
        bool moved = MoveWithCollision(delta);

        // 5) stuck handling
        HandleStuck(moved);
    }

    // =========================================================
    // AVOIDANCE (smooth)
    // =========================================================
    private void ComputeDesiredDirection()
    {
        Vector2 origin = transform.position;
        Vector2 forward = transform.right;
        Vector2 left = transform.up;
        Vector2 right = -transform.up;

        // CircleCast supaya memperhitungkan "badan", bukan titik
        float f = CastDistance(origin, forward, sensorRange);
        float l = CastDistance(origin, left, sensorRange * 0.9f);
        float r = CastDistance(origin, right, sensorRange * 0.9f);

        // avoidance strength (0..1)
        float avoidStrength = 0f;
        if (f < wallHardDistance) avoidStrength = 1f;
        else if (f < wallSoftDistance) avoidStrength = Mathf.InverseLerp(wallSoftDistance, wallHardDistance, f);

        Vector2 avoid = Vector2.zero;

        if (avoidStrength > 0f)
        {
            // pilih sisi yang lebih “lapang”
            float turnSign = (l > r) ? 1f : -1f;
            Vector2 side = (turnSign > 0) ? (Vector2)transform.up : -(Vector2)transform.up;

            // hindar ke samping, plus sedikit mundur kalau terlalu dekat
            avoid = side * 1.0f + (-forward) * 0.35f;
            avoid = avoid.normalized;

            SetStatus(DroneStatus.AVOID);
        }
        else
        {
            SetStatus(DroneStatus.SEARCH);
        }

        // random exploration kecil biar tidak “lurus terkunci”
        Vector2 random = Random.insideUnitCircle.normalized * randomSteerStrength;

        Vector2 finalDir = forward + avoid * 1.6f + random * 0.8f;
        if (finalDir.sqrMagnitude < 0.0001f) finalDir = forward;

        desiredDirection = finalDir.normalized;
    }

    private float CastDistance(Vector2 origin, Vector2 dir, float dist)
    {
        RaycastHit2D hit = Physics2D.CircleCast(origin, bodyRadius, dir, dist, wallLayerMask);
        if (hit.collider != null) return hit.distance;
        return dist;
    }

    // =========================================================
    // MOVEMENT (no penetration) + sliding
    // =========================================================
    private void RotateTowards(Vector2 dir)
    {
        if (dir.sqrMagnitude < 0.0001f) return;

        float angle = Vector2.SignedAngle(transform.right, dir);
        float maxStep = turnSpeedDeg * Time.deltaTime;
        float step = Mathf.Clamp(angle, -maxStep, maxStep);
        transform.Rotate(0f, 0f, step);
    }

    private bool MoveWithCollision(Vector2 delta)
    {
        if (delta.sqrMagnitude < 0.0000001f) return false;

        Vector2 origin = transform.position;
        Vector2 dir = delta.normalized;
        float dist = delta.magnitude;

        // cast maju
        RaycastHit2D hit = Physics2D.CircleCast(origin, bodyRadius, dir, dist, wallLayerMask);

        if (hit.collider == null)
        {
            transform.position = (Vector2)transform.position + delta;
            return true;
        }

        // ada tembok: maju sampai dekat tembok (minus skin)
        float safeMove = Mathf.Max(0f, hit.distance - (skin + 0.001f));
        if (safeMove > 0f)
        {
            transform.position = (Vector2)transform.position + dir * safeMove;
        }

        // slide: gerak sepanjang tangent tembok
        Vector2 normal = hit.normal;
        Vector2 slideDir = Vector2.Perpendicular(normal).normalized;

        // pilih arah slide yang paling mendekati arah awal
        if (Vector2.Dot(slideDir, dir) < 0f) slideDir = -slideDir;

        float remaining = dist - safeMove;
        if (remaining > 0.0001f)
        {
            RaycastHit2D hit2 = Physics2D.CircleCast((Vector2)transform.position, bodyRadius, slideDir, remaining, wallLayerMask);
            float slideMove = (hit2.collider == null) ? remaining : Mathf.Max(0f, hit2.distance - (skin + 0.001f));
            if (slideMove > 0f)
            {
                transform.position = (Vector2)transform.position + slideDir * slideMove;
                return true;
            }
        }

        // kalau tidak bisa gerak, berarti benar-benar mentok
        return false;
    }

    private void DepenetrateFromWalls()
    {
        // cek overlap
        Collider2D hit = Physics2D.OverlapCircle(transform.position, bodyRadius, wallLayerMask);
        if (hit == null) return;

        // dorong keluar sedikit ke arah random sampai bebas
        Vector2 push = Random.insideUnitCircle.normalized * 0.05f;
        transform.position = (Vector2)transform.position + push;
    }

    // =========================================================
    // STUCK
    // =========================================================
    private void HandleStuck(bool movedThisFrame)
    {
        float movedDist = Vector2.Distance(transform.position, lastPos);

        if (!movedThisFrame || movedDist < minMoveDelta)
        {
            stuckTimer += Time.deltaTime;
            if (stuckTimer >= stuckTimeThreshold)
            {
                SetStatus(DroneStatus.STUCK);
                ForceEscapeTurn();
                stuckTimer = 0f;
            }
        }
        else
        {
            stuckTimer = 0f;
        }

        lastPos = transform.position;
    }

    private void ForceEscapeTurn()
    {
        // putar 90-180 derajat supaya keluar sudut
        float ang = Random.Range(95f, 175f);
        if (Random.value > 0.5f) ang = -ang;
        transform.Rotate(0f, 0f, ang);

        // reset arah
        desiredDirection = transform.right;
        smoothedDirection = desiredDirection;

        if (verbose) Debug.Log($"[Drone:{droneName}] ESCAPE turn={ang:F1}");
    }

    // =========================================================
    // TARGET (LOS)
    // =========================================================
    private bool TryDetectTargetLOS()
    {
        if (target == null) return false;

        Vector2 origin = transform.position;
        Vector2 toT = (Vector2)target.position - origin;
        float d = toT.magnitude;

        if (d > detectRange) return false;

        // LOS raycast: kalau ada wall di antara drone-target, anggap tidak terlihat
        RaycastHit2D hit = Physics2D.Raycast(origin, toT.normalized, d, wallLayerMask);
        if (hit.collider != null) return false;

        if (d <= foundDistance)
        {
            Debug.Log($"[Drone:{droneName}] FOUND target at {target.position} dist={d:F2}");
            return true;
        }

        return true; // terlihat (LOS), tapi belum dekat enough -> bisa dipakai Step-3 untuk chasing
    }

    // =========================================================
    // STATUS LOG
    // =========================================================
    private void SetStatus(DroneStatus s)
    {
        if (status == s) return;
        status = s;
        Debug.Log($"[Drone:{droneName}] STATUS -> {status}");
    }

    // =========================================================
    // UTIL
    // =========================================================
    private float EstimateBodyRadius(Collider2D c)
    {
        // estimasi radius dari bounds
        Bounds b = c.bounds;
        float r = Mathf.Max(b.extents.x, b.extents.y);
        // fallback minimal
        return Mathf.Max(0.05f, r);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Vector3 p = transform.position;

        // arah desired
        Gizmos.DrawLine(p, p + (Vector3)(Application.isPlaying ? (Vector3)smoothedDirection : transform.right) * 1.0f);

        // sensor forward
        Gizmos.color = Color.red;
        Gizmos.DrawLine(p, p + transform.right * sensorRange);

        // body radius
        Gizmos.color = new Color(1f, 1f, 0f, 0.25f);
        float r = (Application.isPlaying ? bodyRadius : 0.3f);
        Gizmos.DrawWireSphere(p, r);
    }
#endif
}