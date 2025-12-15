using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class Drone : MonoBehaviour
{
    public enum DroneStatus { SEARCH, CHASE, FOUND, RETURN, ARRIVED }

    [Header("Identity")]
    public string droneName = "Drone1";

    [Header("Movement")]
    public float moveSpeed = 2.0f;
    public float turnSpeedDeg = 220f;
    [Tooltip("6-14 recommended. Bigger = smoother.")]
    public float steeringSmoothing = 10f;

    [Header("Collision")]
    [Tooltip("Obstacle mask untuk physics + safety (biasanya Wall).")]
    public LayerMask obstacleLayerMask;

    [Tooltip("Probe jarak dekat untuk anti-ngunci di pojok saat depan mentok.")]
    public float cornerAvoidProbe = 0.35f;

    [Tooltip("Skin kecil untuk ray/scan (opsional).")]
    public float skin = 0.02f;

    [Header("Planner / Map")]
    public DroneNavigator navigator;
    public GridMap2D map;

    [Header("Detection (LOS)")]
    public Transform target;
    [Tooltip("Wall mask ONLY (untuk LOS).")]
    public LayerMask wallLayerMask;
    public float detectRange = 6f;
    public float foundDistance = 0.35f;

    [Header("Home")]
    public Transform homeBase;
    public float homeArriveDistance = 0.6f;

    [Header("Corner Escape")]
    [Tooltip("Kalau benar-benar mentok, putar 90 derajat untuk lepas sudut.")]
    public bool enableBlockedAheadEscape = true;

    [Header("Debug")]
    public bool verbose = false;

    [Header("Debug Logs")]
    public bool logState = true;
    public bool logMotion = true;
    public bool logMapping = false;
    public bool logPlanning = true;
    public float logEverySeconds = 0.25f;

    private Rigidbody2D rb;
    private Collider2D col;

    // === Anti tembus dinding (shape cast) ===
    private readonly RaycastHit2D[] castHits = new RaycastHit2D[8];
    private ContactFilter2D castFilter;

    // === Anti overlap tipis (push-out) ===
    private readonly Collider2D[] overlapHits = new Collider2D[16];

    private float colRadius = 0.12f; // fallback kalau bukan circle
    private CircleCollider2D circleCol;

    private DroneStatus status = DroneStatus.SEARCH;
    private bool hasFoundTarget = false;
    private bool isStopped = false;

    private Vector2 smoothedDir;
    private Vector2 startHomePos;

    private float fixedTurnVelDeg = 0f;

    // log throttle (fixed)
    private float nextLogTime = 0f;
    private Vector2Int lastCell = new Vector2Int(int.MinValue, int.MinValue);

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        circleCol = GetComponent<CircleCollider2D>();

        // ambil radius collider (untuk CircleCast blocked-ahead)
        if (circleCol != null)
        {
            float scale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y);
            colRadius = Mathf.Max(0.01f, circleCol.radius * scale);
        }
        else
        {
            colRadius = Mathf.Max(0.05f, Mathf.Max(col.bounds.extents.x, col.bounds.extents.y));
        }

        // Physics-safe defaults (guard)
        rb.gravityScale = 0f;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        // HOME konsisten
        startHomePos = (homeBase != null) ? (Vector2)homeBase.position : rb.position;

        smoothedDir = transform.right;

        // inject ke navigator juga (kalau ada)
        if (navigator != null)
        {
            navigator.map = map;
            navigator.wallLayerMask = wallLayerMask;
            navigator.obstacleLayerMask = obstacleLayerMask;
        }

        if (verbose)
            Debug.Log($"[Drone:{droneName}] Awake startHomePos={Fmt2(startHomePos)} homeArriveDistance={homeArriveDistance:F2} colRadius={colRadius:F2}");

        // Contact filter untuk shape cast (anti tembus tembok)
        castFilter = new ContactFilter2D();
        castFilter.useTriggers = false;
        castFilter.SetLayerMask(obstacleLayerMask);
    }

    private void Update()
    {
        if (isStopped) return;

        // 1) map update
        if (navigator != null)
        {
            navigator.TickMap(rb.position);

            if (logMapping && CanLog())
            {
                if (map != null && map.WorldToCell(rb.position, out var cc))
                    Debug.Log($"[Drone:{droneName}] MAP tick at cell={cc} world={Fmt2(rb.position)}");
            }
        }

        // 2) decide direction
        Vector2 desired = DecideDesiredDirection();

        // 3) corner avoid blend (anti pojok)
        desired = ApplyCornerAvoid(desired);

        // 4) smooth direction (frame-rate independent)
        float t = 1f - Mathf.Exp(-steeringSmoothing * Time.deltaTime);
        smoothedDir = Vector2.Lerp(smoothedDir, desired, t);

        if (smoothedDir.sqrMagnitude < 0.0001f) smoothedDir = desired;
        if (smoothedDir.sqrMagnitude < 0.0001f) return;

        smoothedDir.Normalize();

        // 5) advance waypoint (before move)
        if (navigator != null) navigator.AdvanceWaypointIfArrived(rb.position);

        // 6) compute rotation velocity for FixedUpdate
        float angle = Vector2.SignedAngle(transform.right, smoothedDir);
        float maxStep = turnSpeedDeg * Time.deltaTime;
        float step = Mathf.Clamp(angle, -maxStep, maxStep);
        fixedTurnVelDeg = step / Mathf.Max(0.0001f, Time.deltaTime);

        // LOG snapshot
        if (CanLog())
            LogStateSnapshot("UPDATE", desired, smoothedDir);
    }

    private void FixedUpdate()
    {
        if (isStopped) return;

        // 1) ROTASI
        float rotStep = fixedTurnVelDeg * Time.fixedDeltaTime;
        rb.MoveRotation(rb.rotation + rotStep);

        // 2) ESCAPE JIKA BENAR-BENAR MENTOK
        if (enableBlockedAheadEscape && IsBlockedAhead())
        {
            float turn = (Random.value > 0.5f) ? 90f : -90f;
            rb.MoveRotation(rb.rotation + turn);

            if (logMotion && CanLog())
                Debug.Log($"[Drone:{droneName}] ESCAPE blockedAhead -> rotate {turn:+0;-0} deg");
        }

        // ✅ (A) DEPENETRATION sebelum maju: kalau sudah overlap tipis, dorong keluar dulu
        ResolveOverlaps();

        // 3) GERAK MAJU DENGAN SHAPE CAST (ANTI TEMBUS)
        Vector2 forward = transform.right;
        float stepDist = moveSpeed * Time.fixedDeltaTime;

        int hitCount = rb.Cast(
            forward,
            castFilter,
            castHits,
            stepDist + skin
        );

        if (hitCount == 0)
        {
            // aman → maju penuh
            rb.MovePosition(rb.position + forward * stepDist);
        }
        else
        {
            // ada tembok → maju sejauh aman
            float minDist = float.PositiveInfinity;
            RaycastHit2D best = castHits[0];

            for (int i = 0; i < hitCount; i++)
            {
                if (castHits[i].distance < minDist)
                {
                    minDist = castHits[i].distance;
                    best = castHits[i];
                }
            }

            float safeDist = Mathf.Max(0f, minDist - skin);
            rb.MovePosition(rb.position + forward * safeDist);

            if (logMotion && CanLog())
                Debug.Log($"[Drone:{droneName}] BLOCKED(CAST) by={best.collider.name} hitDist={minDist:F3} safeDist={safeDist:F3}");
        }

        // ✅ (B) DEPENETRATION setelah maju: rapikan lagi kalau solver bikin overlap tipis
        ResolveOverlaps();

        if (navigator != null)
            navigator.AdvanceWaypointIfArrived(rb.position);

        if (logMotion && CanLog())
        {
            Debug.Log($"[Drone:{droneName}] MOVE pos={Fmt2(rb.position)} step={stepDist:F3} fwd={Fmt2(forward)}");
        }
    }

    // =========================
    // DEPENETRATION (push-out)
    // =========================
    private void ResolveOverlaps()
    {
        if (col == null) return;

        // cari collider obstacle di sekitar drone
        int count = Physics2D.OverlapCircle(
            rb.position,
            colRadius + skin + 0.05f,
            castFilter,
            overlapHits
        );

        for (int i = 0; i < count; i++)
        {
            Collider2D other = overlapHits[i];
            if (other == null) continue;
            if (other == col) continue;

            ColliderDistance2D dist = Physics2D.Distance(col, other);

            if (dist.isOverlapped)
            {
                // dist.normal mengarah dari other -> drone
                float pushDist = (-dist.distance) + skin;
                rb.position += dist.normal * pushDist;

                if (logMotion && CanLog())
                    Debug.Log($"[Drone:{droneName}] DEPENETRATE from={other.name} push={pushDist:F3} dir={Fmt2(dist.normal)}");
            }
        }
    }

    // =========================
    // DECISION
    // =========================
    private Vector2 DecideDesiredDirection()
    {
        // RETURN
        if (status == DroneStatus.RETURN)
        {
            float distHome = Vector2.Distance(rb.position, startHomePos);

            if (distHome <= homeArriveDistance)
            {
                SetStatus(DroneStatus.ARRIVED);
                isStopped = true;

                Debug.Log($"[Drone:{droneName}] ARRIVED HOME at {Fmt2(rb.position)} dist={distHome:F2} <= {homeArriveDistance:F2}");
                return (Vector2)transform.right;
            }

            if (navigator != null)
            {
                bool needPlan = !navigator.HasPath || navigator.ShouldReplan();
                if (needPlan && map != null && map.WorldToCell(startHomePos, out var homeCell))
                {
                    navigator.ReplanToCell(homeCell, rb.position);

                    if (logPlanning && CanLog())
                        Debug.Log($"[Drone:{droneName}] PLAN Home -> cell={homeCell} hasPath={navigator.HasPath} len={navigator.DebugPathLen}");
                }

                if (navigator.HasPath)
                {
                    Vector2 wp = navigator.GetCurrentWaypointWorld();
                    Vector2 toWp = wp - rb.position;
                    if (toWp.sqrMagnitude > 0.0001f) return toWp.normalized;
                }
            }

            // fallback direct
            Vector2 direct = startHomePos - rb.position;
            return (direct.sqrMagnitude > 0.0001f) ? direct.normalized : (Vector2)transform.right;
        }

        // TARGET
        if (target != null && HasLOS((Vector2)target.position, out float dist))
        {
            if (!hasFoundTarget && dist <= foundDistance)
            {
                hasFoundTarget = true;

                SetStatus(DroneStatus.FOUND);
                Debug.Log($"[Drone:{droneName}] FOUND target at {Fmt2(target.position)} dist={dist:F2}");

                SetStatus(DroneStatus.RETURN);

                if (navigator != null && map != null && map.WorldToCell(startHomePos, out var homeCell))
                    navigator.ReplanToCell(homeCell, rb.position);

                return (Vector2)transform.right; // 1 frame stabilizer
            }

            SetStatus(DroneStatus.CHASE);
            Vector2 toT = (Vector2)target.position - rb.position;
            return (toT.sqrMagnitude > 0.0001f) ? toT.normalized : (Vector2)transform.right;
        }

        // SEARCH (frontier)
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
    // CORNER AVOID
    // =========================
    private Vector2 ApplyCornerAvoid(Vector2 desired)
    {
        if (cornerAvoidProbe <= 0.01f) return desired;

        Vector2 origin = rb.position;
        Vector2 fwd = transform.right;

        // depan mentok?
        RaycastHit2D hitF = Physics2D.CircleCast(origin, colRadius, fwd, cornerAvoidProbe, obstacleLayerMask);
        if (hitF.collider == null) return desired;

        Vector2 left = transform.up;
        Vector2 right = -transform.up;

        float dl = Physics2D.Raycast(origin, left, cornerAvoidProbe, obstacleLayerMask).collider ? 0f : 1f;
        float dr = Physics2D.Raycast(origin, right, cornerAvoidProbe, obstacleLayerMask).collider ? 0f : 1f;

        Vector2 side = (dl >= dr) ? left : right;
        Vector2 blended = (desired * 0.55f + side * 0.85f).normalized;

        if (logMotion && CanLog())
        {
            Debug.Log($"[Drone:{droneName}] CORNER_AVOID frontBlocked by={hitF.collider.name} hitDist={hitF.distance:F2} choose={(dl >= dr ? "LEFT" : "RIGHT")}");
        }

        return blended;
    }

    private bool IsBlockedAhead()
    {
        Vector2 pos = rb.position;
        Vector2 dir = transform.right;

        RaycastHit2D hit = Physics2D.CircleCast(
            pos,
            colRadius,
            dir,
            cornerAvoidProbe + skin,
            obstacleLayerMask
        );

        return hit.collider != null;
    }

    // =========================
    // STATUS + LOGGING
    // =========================
    private void SetStatus(DroneStatus s)
    {
        if (status == s) return;
        status = s;
        Debug.Log($"[Drone:{droneName}] STATUS -> {status}");
    }

    private bool CanLog()
    {
        if (!logState && !logMotion && !logMapping && !logPlanning) return false;
        if (logEverySeconds <= 0.01f) return true;

        if (Time.time >= nextLogTime)
        {
            nextLogTime = Time.time + logEverySeconds;
            return true;
        }
        return false;
    }

    private void LogStateSnapshot(string tag, Vector2 desired, Vector2 smoothed)
    {
        if (!logState) return;

        string cellStr = "cell=?";
        if (map != null && map.WorldToCell(rb.position, out var c))
        {
            cellStr = $"cell={c}";
            if (c != lastCell)
            {
                lastCell = c;
                Debug.Log($"[Drone:{droneName}] CELL -> {c} world={Fmt2(rb.position)}");
            }
        }

        string wpStr = "wp=none";
        if (navigator != null && navigator.HasPath)
        {
            Vector2 wp = navigator.GetCurrentWaypointWorld();
            float dwp = Vector2.Distance(rb.position, wp);
            wpStr = $"wp={Fmt2(wp)} dwp={dwp:F2} idx={navigator.DebugPathIndex} len={navigator.DebugPathLen} goal={navigator.DebugGoalCell}";
        }

        Debug.Log(
            $"[Drone:{droneName}] {tag} status={status} {cellStr} pos={Fmt2(rb.position)} " +
            $"desired={Fmt2(desired)} smooth={Fmt2(smoothed)} fwd={Fmt2((Vector2)transform.right)} {wpStr}"
        );
    }

    private static string Fmt2(Vector2 v) => $"({v.x:F2}, {v.y:F2})";
}