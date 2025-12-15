using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class Drone : MonoBehaviour
{
    public enum DroneStatus { SEARCH, CHASE, FOUND, RETURN, ARRIVED }

    // =========================
    // ROLE + LED
    // =========================
    public enum DroneRole { Leader, Member }

    [Header("Role / LED")]
    public DroneRole role = DroneRole.Member;
    public Transform ledMarker;
    public SpriteRenderer ledRenderer;
    public Color leaderColor = Color.red;
    public Color memberColor = Color.cyan;

    public void SetRole(DroneRole newRole)
    {
        role = newRole;
        ApplyLedColor();
    }

    private void ApplyLedColor()
    {
        if (ledRenderer == null) return;
        ledRenderer.color = (role == DroneRole.Leader) ? leaderColor : memberColor;
    }

    // =========================
    // Identity
    // =========================
    [Header("Identity")]
    public string droneName = "Drone1";

    // =========================
    // Movement
    // =========================
    [Header("Movement")]
    public float moveSpeed = 2.0f;
    public float turnSpeedDeg = 220f;
    [Tooltip("6-14 recommended. Bigger = smoother.")]
    public float steeringSmoothing = 10f;

    // =========================
    // Collision
    // =========================
    [Header("Collision Masks")]
    [Tooltip("Walls/obstacles layer mask.")]
    public LayerMask obstacleLayerMask;

    [Tooltip("Drone layer mask (agar drone saling menghindar).")]
    public LayerMask droneLayerMask;

    [Header("Collision Params")]
    public float cornerAvoidProbe = 0.35f;
    public float skin = 0.02f;

    private bool IsSelfCollider(Collider2D c)
{
    if (c == null) return true;
    // kalau collider ada di GameObject drone sendiri / child-nya (root sama), anggap SELF
    return c.transform.root == transform.root;
}

    // =========================
    // Separation (anti tumpuk antar drone)
    // =========================
    [Header("Separation (Drone Avoid)")]
    public bool enableSeparation = true;
    public float separationRadius = 0.60f;
    public float separationStrength = 0.90f;

    // buffer collider neighbors
    private readonly Collider2D[] droneNeighbors = new Collider2D[16];

    // =========================
    // Planner / Map
    // =========================
    [Header("Planner / Map")]
    public DroneNavigator navigator;
    public GridMap2D map;

    // =========================
    // Detection (LOS)
    // =========================
    [Header("Detection (LOS)")]
    public Transform target;
    public LayerMask wallLayerMask;
    public float detectRange = 6f;
    public float foundDistance = 0.35f;

    // =========================
    // Return / Arrive
    // =========================
    [Header("Return")]
    public float arriveDistance = 0.55f;

    // =========================
    // Corner Escape
    // =========================
    [Header("Corner Escape")]
    public bool enableBlockedAheadEscape = true;

    // =========================
    // Debug
    // =========================
    [Header("Debug")]
    public bool verbose = false;

    [Header("Debug Logs")]
    public bool logState = true;
    public bool logMotion = false;
    public float logEverySeconds = 0.25f;

    // Mission controller link
    [HideInInspector] public SimManager simManager;

    private Rigidbody2D rb;
    private Collider2D col;

    // shape cast buffer
    private readonly RaycastHit2D[] castHits = new RaycastHit2D[8];
    private ContactFilter2D castFilter;

    // overlap buffer
    private readonly Collider2D[] overlapHits = new Collider2D[16];

    private CircleCollider2D circleCol;
    private float baseRadius = 0.20f;

    private DroneStatus status = DroneStatus.SEARCH;
    private bool hasFoundTarget = false;
    private bool isStopped = false;

    private Vector2 smoothedDir;
    private float fixedTurnVelDeg = 0f;

    // ✅ each drone has its own return point (start position)
    private Vector2 returnPoint;

    // logging throttle
    private float nextLogTime = 0f;

    // =========================
    // PUBLIC API (dipakai SimManager)
    // =========================
    public void SetReturnPoint(Vector2 p) => returnPoint = p;

    public void ResetMissionState()
    {
        hasFoundTarget = false;
        isStopped = false;
        status = DroneStatus.SEARCH;

        // reset heading smoother
        smoothedDir = transform.right;
    }

    public void ForceReturnToStart()
    {
        if (status == DroneStatus.ARRIVED) return;

        SetStatus(DroneStatus.RETURN);

        // plan to returnPoint (cell)
        if (navigator != null && map != null && map.WorldToCell(returnPoint, out var goal))
            navigator.ReplanToCell(goal, rb.position);

        if (verbose) Debug.Log($"[Drone:{droneName}] FORCE RETURN to {Fmt2(returnPoint)}");
    }

    // =========================
    // Unity
    // =========================
    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        circleCol = GetComponent<CircleCollider2D>();

        // LED auto-find
        if (ledMarker == null)
        {
            var t = transform.Find("LEDMarker");
            if (t != null) ledMarker = t;
        }
        if (ledRenderer == null && ledMarker != null)
            ledRenderer = ledMarker.GetComponent<SpriteRenderer>();
        ApplyLedColor();

        // radius from collider
        if (circleCol != null)
        {
            float scale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y);
            baseRadius = Mathf.Max(0.01f, circleCol.radius * scale);
        }
        else
        {
            baseRadius = Mathf.Max(0.05f, Mathf.Max(col.bounds.extents.x, col.bounds.extents.y));
        }

        // physics defaults
        rb.gravityScale = 0f;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        // cast filter walls only
        castFilter = new ContactFilter2D();
        castFilter.useTriggers = false;
        castFilter.SetLayerMask(obstacleLayerMask);

        // default returnPoint = current pos (akan dioverride SimManager)
        returnPoint = rb.position;

        smoothedDir = transform.right;

        if (verbose)
            Debug.Log($"[Drone:{droneName}] Awake baseRadius={baseRadius:F3} returnPoint={Fmt2(returnPoint)}");
    }

    private void Update()
    {
        if (isStopped) return;

        // map tick
        if (navigator != null) navigator.TickMap(rb.position);

        // decide
        Vector2 desired = DecideDesiredDirection();

        // separation (anti tumpuk)
        desired = ApplySeparation(desired);

        // corner avoid (walls)
        desired = ApplyCornerAvoid(desired);

        // smooth dir
        float t = 1f - Mathf.Exp(-steeringSmoothing * Time.deltaTime);
        smoothedDir = Vector2.Lerp(smoothedDir, desired, t);

        if (smoothedDir.sqrMagnitude < 0.0001f) smoothedDir = desired;
        if (smoothedDir.sqrMagnitude < 0.0001f) return;

        smoothedDir.Normalize();

        // compute turn vel for FixedUpdate
        float angle = Vector2.SignedAngle(transform.right, smoothedDir);
        float maxStep = turnSpeedDeg * Time.deltaTime;
        float step = Mathf.Clamp(angle, -maxStep, maxStep);
        fixedTurnVelDeg = step / Mathf.Max(0.0001f, Time.deltaTime);

        if (CanLog() && logState)
            Debug.Log($"[Drone:{droneName}] status={status} pos={Fmt2(rb.position)} desired={Fmt2(desired)}");
    }

    private void FixedUpdate()
    {
        if (isStopped) return;

        // rotate
        float rotStep = fixedTurnVelDeg * Time.fixedDeltaTime;
        rb.MoveRotation(rb.rotation + rotStep);

        // escape if blocked ahead
        if (enableBlockedAheadEscape && IsBlockedAhead())
        {
            float turn = (Random.value > 0.5f) ? 90f : -90f;
            rb.MoveRotation(rb.rotation + turn);
        }

        // depenetrate from walls + drones
        ResolveOverlaps();

        // move forward with wall cast
        Vector2 forward = transform.right;
        float stepDist = moveSpeed * Time.fixedDeltaTime;

        int hitCount = rb.Cast(forward, castFilter, castHits, stepDist + skin);

        if (hitCount == 0)
        {
            rb.MovePosition(rb.position + forward * stepDist);
        }
        else
        {
            float minDist = float.PositiveInfinity;
            for (int i = 0; i < hitCount; i++)
                if (castHits[i].distance < minDist) minDist = castHits[i].distance;

            float safeDist = Mathf.Max(0f, minDist - skin);
            rb.MovePosition(rb.position + forward * safeDist);
        }

        // depenetrate again (post move)
        ResolveOverlaps();

        // path step
        if (navigator != null) navigator.AdvanceWaypointIfArrived(rb.position);
    }

    // =========================
    // Decision
    // =========================
    private Vector2 DecideDesiredDirection()
    {
        // RETURN → ke returnPoint (start masing-masing)
        if (status == DroneStatus.RETURN)
        {
            float dist = Vector2.Distance(rb.position, returnPoint);
            if (dist <= arriveDistance)
            {
                SetStatus(DroneStatus.ARRIVED);
                isStopped = true;

                if (simManager != null) simManager.NotifyDroneArrived(this);

                if (verbose) Debug.Log($"[Drone:{droneName}] ARRIVED at start {Fmt2(returnPoint)}");
                return (Vector2)transform.right;
            }

            if (navigator != null && map != null)
            {
                bool needPlan = !navigator.HasPath || navigator.ShouldReplan();
                if (needPlan && map.WorldToCell(returnPoint, out var goal))
                    navigator.ReplanToCell(goal, rb.position);

                if (navigator.HasPath)
                {
                    Vector2 wp = navigator.GetCurrentWaypointWorld();
                    Vector2 toWp = wp - rb.position;
                    if (toWp.sqrMagnitude > 0.0001f) return toWp.normalized;
                }
            }

            Vector2 direct = returnPoint - rb.position;
            return (direct.sqrMagnitude > 0.0001f) ? direct.normalized : (Vector2)transform.right;
        }

        // TARGET
        if (target != null && HasLOS((Vector2)target.position, out float distT))
        {
            if (!hasFoundTarget && distT <= foundDistance)
            {
                hasFoundTarget = true;
                SetStatus(DroneStatus.FOUND);

                if (verbose) Debug.Log($"[Drone:{droneName}] FOUND target dist={distT:F2}");

                // mission command (sekali)
                if (simManager != null && !simManager.missionTargetFound)
                    simManager.OnAnyDroneFoundTarget(this);

                // setelah ditemukan, drone ini juga RETURN
                SetStatus(DroneStatus.RETURN);
                return (Vector2)transform.right;
            }

            SetStatus(DroneStatus.CHASE);
            Vector2 toT = (Vector2)target.position - rb.position;
            return (toT.sqrMagnitude > 0.0001f) ? toT.normalized : (Vector2)transform.right;
        }

        // SEARCH
        SetStatus(DroneStatus.SEARCH);

        if (navigator != null)
        {
            bool needPlan = !navigator.HasPath || navigator.ShouldReplan();
            if (needPlan) navigator.ReplanToFrontier(rb.position);

            if (navigator.HasPath)
            {
                Vector2 wp = navigator.GetCurrentWaypointWorld();
                Vector2 toWp = wp - rb.position;
                if (toWp.sqrMagnitude > 0.0001f) return toWp.normalized;
            }
        }

        return (Vector2)transform.right;
    }

    // =========================
    // LOS
    // =========================
    private bool HasLOS(Vector2 targetPos, out float dist)
    {
        dist = Vector2.Distance(rb.position, targetPos);
        if (dist > detectRange) return false;

        Vector2 origin = rb.position;
        Vector2 dir = (targetPos - origin).normalized;

        RaycastHit2D hit = Physics2D.Raycast(origin, dir, dist, wallLayerMask);
        return hit.collider == null;
    }

    // =========================
    // Separation (anti tumpuk drone)
    // =========================
    private Vector2 ApplySeparation(Vector2 desired)
{
    if (!enableSeparation) return desired;

    int count = Physics2D.OverlapCircleNonAlloc(rb.position, separationRadius, droneNeighbors, droneLayerMask);
    if (count <= 0) return desired;

    Vector2 push = Vector2.zero;
    int n = 0;

    for (int i = 0; i < count; i++)
    {
        Collider2D c = droneNeighbors[i];
        if (c == null) continue;

        if (IsSelfCollider(c)) continue; // ✅ penting

        Vector2 p = c.attachedRigidbody ? c.attachedRigidbody.position : (Vector2)c.transform.position;
        Vector2 away = (Vector2)rb.position - p;

        float d = away.magnitude;
        if (d < 0.0001f) continue;

        float w = Mathf.Clamp01((separationRadius - d) / separationRadius);
        push += (away / d) * w;
        n++;
    }

    if (n == 0) return desired;

    push /= n;

    Vector2 blended = desired + push * separationStrength;
    if (blended.sqrMagnitude < 0.0001f) return desired;
    return blended.normalized;
}

    // =========================
    // Wall Corner Avoid
    // =========================
    private Vector2 ApplyCornerAvoid(Vector2 desired)
    {
        if (cornerAvoidProbe <= 0.01f) return desired;

        Vector2 origin = rb.position;
        Vector2 fwd = transform.right;

        RaycastHit2D hitF = Physics2D.CircleCast(origin, baseRadius, fwd, cornerAvoidProbe, obstacleLayerMask);
        if (hitF.collider == null) return desired;

        Vector2 left = transform.up;
        Vector2 right = -transform.up;

        float dl = Physics2D.Raycast(origin, left, cornerAvoidProbe, obstacleLayerMask).collider ? 0f : 1f;
        float dr = Physics2D.Raycast(origin, right, cornerAvoidProbe, obstacleLayerMask).collider ? 0f : 1f;

        Vector2 side = (dl >= dr) ? left : right;
        return (desired * 0.55f + side * 0.85f).normalized;
    }

    private bool IsBlockedAhead()
    {
        Vector2 pos = rb.position;
        Vector2 dir = transform.right;

        RaycastHit2D hit = Physics2D.CircleCast(pos, baseRadius, dir, cornerAvoidProbe + skin, obstacleLayerMask);
        return hit.collider != null;
    }

    // =========================
    // Depenetration (walls + drones)
    // =========================
    private void ResolveOverlaps()
{
    if (col == null) return;

    float r = baseRadius + skin + 0.05f;

    // dorong keluar dari drone dulu (biar tidak ngedorong ke dinding)
    int countD = Physics2D.OverlapCircleNonAlloc(rb.position, r, overlapHits, droneLayerMask);
    PushOutFromOverlaps(countD);

    // lalu dorong keluar dari wall
    int countObs = Physics2D.OverlapCircleNonAlloc(rb.position, r, overlapHits, obstacleLayerMask);
    PushOutFromOverlaps(countObs);
}

    private void PushOutFromOverlaps(int count)
{
    for (int i = 0; i < count; i++)
    {
        Collider2D other = overlapHits[i];
        if (other == null) continue;

        if (IsSelfCollider(other)) continue; // ✅ penting

        // Optional safety: abaikan trigger
        if (other.isTrigger) continue;

        ColliderDistance2D dist = Physics2D.Distance(col, other);
        if (!dist.isOverlapped) continue;

        float pushDist = (-dist.distance) + skin;

        // batasi push supaya tidak "teleport" jauh
        pushDist = Mathf.Min(pushDist, 0.10f);

        rb.position += dist.normal * pushDist;
    }
}

    // =========================
    // Status + Logging
    // =========================
    private void SetStatus(DroneStatus s)
    {
        if (status == s) return;
        status = s;
        if (verbose) Debug.Log($"[Drone:{droneName}] STATUS -> {status}");
    }

    private bool CanLog()
    {
        if (logEverySeconds <= 0.01f) return true;
        if (Time.time >= nextLogTime)
        {
            nextLogTime = Time.time + logEverySeconds;
            return true;
        }
        return false;
    }

    private static string Fmt2(Vector2 v) => $"({v.x:F2}, {v.y:F2})";
}