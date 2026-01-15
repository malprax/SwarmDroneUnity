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
        if (verbose) Debug.Log($"[Drone:{droneName}] ROLE -> {role}");
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
    // Collision (Walls)
    // =========================
    [Header("Collision (Walls)")]
    [Tooltip("WALL ONLY (jangan masukkan Drone layer di sini).")]
    public LayerMask obstacleLayerMask;
    public float cornerAvoidProbe = 0.35f;
    public float skin = 0.02f;

    // =========================
    // Separation + Depenetration (Drone vs Drone)
    // =========================
    [Header("Separation (Drone vs Drone)")]
    public bool enableSeparation = true;
    [Tooltip("LayerMask khusus Drone.")]
    public LayerMask droneLayerMask;

    [Tooltip("Radius awareness untuk steering menjauh.")]
    public float separationRadius = 0.90f;

    [Tooltip("Bobot steering menjauh (1.0-2.5).")]
    public float separationSteerWeight = 1.8f;

    [Header("Depenetration (Anti Overlap)")]
    [Tooltip("Dorongan keluar jika sudah overlap (0.8-2.0).")]
    public float depenetrationStrength = 1.4f;

    [Tooltip("Maks koreksi posisi per FixedUpdate (m).")]
    public float depenetrationMaxStep = 0.25f;

    [Tooltip("Tambahan jarak aman kecil agar tidak nempel lagi.")]
    public float depenetrationSlop = 0.015f;

    [Tooltip("Berapa kali iterasi depenetration per FixedUpdate (1-6).")]
    public int depenetrationIterations = 3;

    // =========================
    // Avoidance (Drone Ahead)
    // =========================
    [Header("Avoidance (Drone Ahead)")]
    public bool enableDroneAheadAvoid = true;
    public float droneAheadProbe = 0.85f;
    public float droneAheadSkin = 0.03f;
    public float droneAvoidTurnBias = 1.2f;

    [Tooltip("Probe ke samping untuk cari jalur menepi (left/right).")]
    public float droneSideProbe = 0.95f;

    // =========================
    // Corner Anti-Jam (penting untuk balik kanan di sudut)
    // =========================
    [Header("Corner Anti-Jam")]
    public bool enableCornerAntiJam = true;
    [Tooltip("Langkah mundur kecil saat macet di sudut (m).")]
    public float cornerBackoffStep = 0.10f;
    [Tooltip("Hold sebentar setelah backoff agar tidak langsung nempel lagi.")]
    public float cornerHoldSeconds = 0.15f;
    [Tooltip("Jarak dianggap 'dekat drone' untuk deteksi macet di sudut.")]
    public float closeDroneDistance = 1.0f;

    // =========================
    // Adaptive Radius
    // =========================
    [Header("Adaptive Radius (based on speed)")]
    public bool enableAdaptiveRadius = true;
    public float baseRadiusOverride = -1f;
    public float maxExtraRadius = 0.08f;
    public float radiusPower = 1.6f;

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
    // Home / Return (COMPAT: homeBase masih ada)
    // =========================
    [Header("Home / Return")]
    public Transform homeBase; // legacy/base (dipakai SimManager)
    [HideInInspector] public Vector2 returnHomePos; // tiap drone punya titik pulang sendiri
    public float homeArriveDistance = 0.6f;

    // =========================
    // Corner Escape (legacy)
    // =========================
    [Header("Corner Escape (Legacy)")]
    public bool enableBlockedAheadEscape = true;

    // =========================
    // Debug
    // =========================
    [Header("Debug")]
    public bool verbose = false;

    [Header("Debug Logs")]
    public bool logState = true;
    public bool logMotion = true;
    public bool logMapping = false;
    public bool logPlanning = true;
    public float logEverySeconds = 0.25f;

    // =========================
    // Mission Controller link (COMPAT: simManager masih ada)
    // =========================
    [HideInInspector] public SimManager simManager;

    // =========================
    // Standby gate (COMPAT: SetStandby masih ada)
    // =========================
    [Header("Standby")]
    public bool startStandby = true;

    public void SetStandby(bool v)
    {
        isStandby = v;
        if (v)
        {
            fixedTurnVelDeg = 0f;
            smoothedDir = transform.right;
        }

        if (verbose) Debug.Log($"[Drone:{droneName}] STANDBY={isStandby}");
    }

    // =========================
    // Runtime
    // =========================
    private Rigidbody2D rb;
    private Collider2D col;
    private CircleCollider2D circleCol;

    private readonly RaycastHit2D[] wallCastHits = new RaycastHit2D[8];
    private readonly RaycastHit2D[] droneCastHits = new RaycastHit2D[8];

    private ContactFilter2D wallCastFilter;
    private ContactFilter2D droneCastFilter;

    // neighbor cache (non-alloc)
    private readonly Collider2D[] neighborHits = new Collider2D[32];
    private ContactFilter2D neighborFilter;

    private float colRadius = 0.12f;

    private DroneStatus status = DroneStatus.SEARCH;
    private bool hasFoundTarget = false;
    private bool isStopped = false;
    private bool isStandby = false;

    private Vector2 smoothedDir;
    private float fixedTurnVelDeg = 0f;

    private float nextLogTime = 0f;
    private Vector2Int lastCell = new Vector2Int(int.MinValue, int.MinValue);

    // ✅ COMPAT counters (dipakai SimManager)
    [HideInInspector] public int wallCollisionCount = 0;
    [HideInInspector] public int droneCollisionCount = 0;

    // corner anti-jam hold
    private float cornerHoldUntil = 0f;

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

        // radius base
        if (baseRadiusOverride > 0f) colRadius = baseRadiusOverride;
        else if (circleCol != null)
        {
            float scale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y);
            colRadius = Mathf.Max(0.01f, circleCol.radius * scale);
        }
        else
        {
            colRadius = Mathf.Max(0.05f, Mathf.Max(col.bounds.extents.x, col.bounds.extents.y));
        }

        // physics-safe
        rb.gravityScale = 0f;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        // default return = posisi awal (ditimpa SimManager)
        returnHomePos = rb.position;

        smoothedDir = transform.right;

        // Contact filter WALL
        wallCastFilter = new ContactFilter2D();
        wallCastFilter.useTriggers = false;
        wallCastFilter.SetLayerMask(obstacleLayerMask);

        // Contact filter DRONE (cast di depan)
        droneCastFilter = new ContactFilter2D();
        droneCastFilter.useTriggers = false;
        droneCastFilter.SetLayerMask(droneLayerMask);

        // Neighbor filter (overlap circle drone)
        neighborFilter = new ContactFilter2D();
        neighborFilter.useTriggers = false;
        neighborFilter.SetLayerMask(droneLayerMask);

        isStandby = startStandby;
    }

    private void Update()
    {
        if (isStopped || isStandby) return;

        if (navigator != null)
            navigator.TickMap(rb.position);

        Vector2 desired = DecideDesiredDirection();
        desired = ApplyCornerAvoid(desired);

        // separation steering: mempengaruhi desired
        if (enableSeparation)
            desired = ApplySeparationSteering(desired);

        float t = 1f - Mathf.Exp(-steeringSmoothing * Time.deltaTime);
        smoothedDir = Vector2.Lerp(smoothedDir, desired, t);

        if (smoothedDir.sqrMagnitude < 0.0001f) smoothedDir = desired;
        if (smoothedDir.sqrMagnitude < 0.0001f) return;

        smoothedDir.Normalize();

        if (navigator != null) navigator.AdvanceWaypointIfArrived(rb.position);

        float angle = Vector2.SignedAngle(transform.right, smoothedDir);
        float maxStep = turnSpeedDeg * Time.deltaTime;
        float step = Mathf.Clamp(angle, -maxStep, maxStep);
        fixedTurnVelDeg = step / Mathf.Max(0.0001f, Time.deltaTime);

        if (CanLog()) LogStateSnapshot("UPDATE", desired, smoothedDir);
    }

    private void FixedUpdate()
    {
        if (isStopped || isStandby) return;

        // ✅ 0) kalau lagi hold (anti-jam), jangan maju dulu
        if (Time.time < cornerHoldUntil)
            return;

        // ✅ 1) depenetration dulu (kalau overlap, paksa keluar)
        if (enableSeparation) ApplyDepenetrationIterative();

        // ROTATE (normal)
        float rotStep = fixedTurnVelDeg * Time.fixedDeltaTime;
        rb.MoveRotation(rb.rotation + rotStep);

        // ✅ 2) corner anti-jam: kalau blocked + ada drone dekat -> mundur + menepi + hold
        if (enableCornerAntiJam && IsBlockedAhead() && HasCloseDrone())
        {
            // backoff sedikit
            rb.MovePosition(rb.position - (Vector2)transform.right * Mathf.Max(0.01f, cornerBackoffStep));

            // menepi
            Vector2 side = ChooseSaferSide();
            Vector2 newDir = (side * 1.3f + (Vector2)transform.right * 0.1f).normalized;

            smoothedDir = newDir;
            float a = Vector2.SignedAngle(transform.right, smoothedDir);
            float stepA = Mathf.Clamp(a, -60f, 60f);
            rb.MoveRotation(rb.rotation + stepA);

            cornerHoldUntil = Time.time + Mathf.Max(0.01f, cornerHoldSeconds);
            return;
        }

        // Escape corner legacy (optional)
        if (enableBlockedAheadEscape && IsBlockedAhead())
        {
            float turn = (Random.value > 0.5f) ? 90f : -90f;
            rb.MoveRotation(rb.rotation + turn);
        }

        // Move forward with rb.Cast (anti tembus WALL + anti tabrak DRONE)
        Vector2 forward = transform.right;
        float stepDist = moveSpeed * Time.fixedDeltaTime;

        // 1) cek wall
        int wallHits = rb.Cast(forward, wallCastFilter, wallCastHits, stepDist + skin);

        // 2) cek drone di depan
        int droneHits = 0;
        if (enableDroneAheadAvoid)
        {
            float probe = Mathf.Max(droneAheadProbe, stepDist) + droneAheadSkin;
            droneHits = rb.Cast(forward, droneCastFilter, droneCastHits, probe);
        }

        float minDist = float.PositiveInfinity;

        if (wallHits > 0)
        {
            for (int i = 0; i < wallHits; i++)
                if (wallCastHits[i].distance < minDist) minDist = wallCastHits[i].distance;
        }

        if (droneHits > 0)
        {
            for (int i = 0; i < droneHits; i++)
                if (droneCastHits[i].distance < minDist) minDist = droneCastHits[i].distance;
        }

        if (minDist == float.PositiveInfinity)
        {
            rb.MovePosition(rb.position + forward * stepDist);
        }
        else
        {
            float safeDist = Mathf.Max(0f, minDist - Mathf.Max(skin, droneAheadSkin));
            safeDist = Mathf.Min(safeDist, stepDist);

            // drone tepat di depan -> menepi (jangan maju)
            if (enableDroneAheadAvoid && droneHits > 0 && safeDist <= 0.001f)
            {
                Vector2 side = ChooseSaferSide();
                Vector2 newDir = (forward * 0.20f + side * (droneAvoidTurnBias * 1.4f)).normalized;

                if (newDir.sqrMagnitude > 0.001f)
                {
                    smoothedDir = newDir;
                    float angle = Vector2.SignedAngle(transform.right, smoothedDir);
                    float maxStep = turnSpeedDeg * Time.fixedDeltaTime;
                    float step = Mathf.Clamp(angle, -maxStep, maxStep);
                    rb.MoveRotation(rb.rotation + step);
                }
                // stop maju frame ini
            }
            else
            {
                rb.MovePosition(rb.position + forward * safeDist);
            }
        }

        if (navigator != null)
            navigator.AdvanceWaypointIfArrived(rb.position);
    }

    // =========================================================
    // ✅ Stable random direction (buat kasus posisi sama persis)
    // =========================================================
    private Vector2 StableRandomDir(int salt = 0)
    {
        int h = (gameObject.GetInstanceID() * 73856093) ^ (salt * 19349663);
        float a = (h & 1023) / 1023f * Mathf.PI * 2f;
        return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
    }

    // =========================================================
    // ✅ Separation Steering: mempengaruhi desired direction
    // =========================================================
    private Vector2 ApplySeparationSteering(Vector2 desired)
    {
        int count = Physics2D.OverlapCircle(rb.position, separationRadius, neighborFilter, neighborHits);
        if (count <= 0) return desired;

        Vector2 repulse = Vector2.zero;
        int n = 0;

        for (int i = 0; i < count; i++)
        {
            var other = neighborHits[i];
            if (!other) continue;
            if (other == col) continue;

            Vector2 delta = (Vector2)rb.position - (Vector2)other.transform.position;
            float d = delta.magnitude;

            if (d < 0.001f)
            {
                delta = StableRandomDir(i + 17);
                d = 0.001f;
            }

            float w = Mathf.Clamp01((separationRadius - d) / separationRadius);
            repulse += (delta / d) * w;
            n++;
        }

        if (n <= 0) return desired;

        repulse /= n;
        Vector2 combined = desired + repulse * separationSteerWeight;

        return (combined.sqrMagnitude > 0.0001f) ? combined.normalized : desired;
    }

    // =========================================================
    // ✅ Depenetration Iterative: benar-benar keluarkan overlap
    // =========================================================
    private void ApplyDepenetrationIterative()
    {
        int it = Mathf.Clamp(depenetrationIterations, 1, 6);
        float scanR = Mathf.Max(separationRadius, colRadius * 2.6f);

        for (int iter = 0; iter < it; iter++)
        {
            int count = Physics2D.OverlapCircle(rb.position, scanR, neighborFilter, neighborHits);
            if (count <= 0) return;

            Vector2 correction = Vector2.zero;

            for (int i = 0; i < count; i++)
            {
                var other = neighborHits[i];
                if (!other) continue;
                if (other == col) continue;

                ColliderDistance2D cd = col.Distance(other);
                if (!cd.isOverlapped) continue;

                float penetration = (-cd.distance) + depenetrationSlop;
                if (penetration <= 0f) continue;

                correction += cd.normal * penetration;
            }

            if (correction.sqrMagnitude < 0.0000001f) return;

            float maxStep = Mathf.Max(0.01f, depenetrationMaxStep);
            correction = Vector2.ClampMagnitude(correction * Mathf.Clamp(depenetrationStrength, 0.2f, 3.0f), maxStep);

            rb.MovePosition(rb.position + correction);
        }
    }

    // =========================================================
    // ✅ Anti-jam helper: cek ada drone dekat
    // =========================================================
    private bool HasCloseDrone()
    {
        float r = Mathf.Max(closeDroneDistance, separationRadius * 0.75f);
        int count = Physics2D.OverlapCircle(rb.position, r, neighborFilter, neighborHits);
        for (int i = 0; i < count; i++)
        {
            var other = neighborHits[i];
            if (!other) continue;
            if (other == col) continue;
            return true;
        }
        return false;
    }

    // =========================================================
    // Choose safer side (left/right) considering WALL + DRONE
    // =========================================================
    private Vector2 ChooseSaferSide()
    {
        Vector2 origin = rb.position;
        Vector2 left = transform.up;
        Vector2 right = -transform.up;

        float probe = Mathf.Max(0.05f, droneSideProbe);

        float dl = RayFreeDistance(origin, left, probe, obstacleLayerMask, droneLayerMask);
        float dr = RayFreeDistance(origin, right, probe, obstacleLayerMask, droneLayerMask);

        if (Mathf.Abs(dl - dr) < 0.0001f)
        {
            // tie-break stabil per drone supaya tidak zigzag bareng
            return (StableRandomDir(99).x >= 0f) ? left : right;
        }

        return (dl >= dr) ? left : right;
    }

    private float RayFreeDistance(Vector2 origin, Vector2 dir, float dist, LayerMask wallMask, LayerMask droneMask)
    {
        RaycastHit2D hw = Physics2D.Raycast(origin, dir, dist, wallMask);
        RaycastHit2D hd = Physics2D.Raycast(origin, dir, dist, droneMask);

        float dw = (hw.collider != null) ? hw.distance : dist;
        float dd = (hd.collider != null) ? hd.distance : dist;

        return Mathf.Min(dw, dd);
    }

    // =========================
    // Mission commands (COMPAT)
    // =========================
    public void ForceReturnToHome()
    {
        if (status == DroneStatus.ARRIVED) return;

        SetStatus(DroneStatus.RETURN);

        if (navigator != null && map != null && map.WorldToCell(returnHomePos, out var homeCell))
            navigator.ReplanToCell(homeCell, rb.position);
    }

    // =========================
    // Adaptive Radius
    // =========================
    private float GetAdaptiveRadius()
    {
        float intendedSpeed = moveSpeed;
        float s01 = (moveSpeed > 0.001f) ? Mathf.Clamp01(intendedSpeed / moveSpeed) : 0f;
        float extra = enableAdaptiveRadius ? (maxExtraRadius * Mathf.Pow(s01, radiusPower)) : 0f;
        return Mathf.Max(0.01f, colRadius + extra);
    }

    // =========================
    // Decision
    // =========================
    private Vector2 DecideDesiredDirection()
    {
        // RETURN
        if (status == DroneStatus.RETURN)
        {
            float distHome = Vector2.Distance(rb.position, returnHomePos);

            if (distHome <= homeArriveDistance)
            {
                SetStatus(DroneStatus.ARRIVED);
                isStopped = true;

                if (simManager != null)
                    simManager.NotifyDroneArrived(this);

                return (Vector2)transform.right;
            }

            if (navigator != null && map != null)
            {
                bool needPlan = !navigator.HasPath || navigator.ShouldReplan();
                if (needPlan && map.WorldToCell(returnHomePos, out var homeCell))
                    navigator.ReplanToCell(homeCell, rb.position);

                if (navigator.HasPath)
                {
                    Vector2 wp = navigator.GetCurrentWaypointWorld();
                    Vector2 toWp = wp - rb.position;
                    if (toWp.sqrMagnitude > 0.0001f) return toWp.normalized;
                }
            }

            Vector2 direct = returnHomePos - rb.position;
            return (direct.sqrMagnitude > 0.0001f) ? direct.normalized : (Vector2)transform.right;
        }

        // TARGET
        if (target != null && HasLOS((Vector2)target.position, out float dist))
        {
            // (tetap kompatibel, meskipun status FOUND tidak lagi dipakai SimManager)
            if (!hasFoundTarget && dist <= foundDistance)
            {
                hasFoundTarget = true;
                SetStatus(DroneStatus.FOUND);

                if (simManager != null && !simManager.missionTargetFound)
                    simManager.OnAnyDroneFoundTarget(this);

                SetStatus(DroneStatus.RETURN);

                if (navigator != null && map != null && map.WorldToCell(returnHomePos, out var homeCell))
                    navigator.ReplanToCell(homeCell, rb.position);

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

    private bool HasLOS(Vector2 targetPos, out float dist)
    {
        dist = Vector2.Distance(rb.position, targetPos);
        if (dist > detectRange) return false;

        Vector2 origin = rb.position;
        Vector2 dir = (targetPos - origin).normalized;

        RaycastHit2D hit = Physics2D.Raycast(origin, dir, dist, wallLayerMask);
        return hit.collider == null;
    }

    private Vector2 ApplyCornerAvoid(Vector2 desired)
    {
        if (cornerAvoidProbe <= 0.01f) return desired;

        Vector2 origin = rb.position;
        Vector2 fwd = transform.right;
        float r = GetAdaptiveRadius();

        RaycastHit2D hitF = Physics2D.CircleCast(origin, r, fwd, cornerAvoidProbe, obstacleLayerMask);
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
        float r = GetAdaptiveRadius();

        RaycastHit2D hit = Physics2D.CircleCast(pos, r, dir, cornerAvoidProbe + skin, obstacleLayerMask);
        return hit.collider != null;
    }

    private void SetStatus(DroneStatus s)
    {
        if (status == s) return;
        status = s;
        if (verbose) Debug.Log($"[Drone:{droneName}] STATUS -> {status}");
    }

    // =========================
    // Collision counters (CSV)
    // =========================
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (((1 << collision.gameObject.layer) & droneLayerMask) != 0)
            droneCollisionCount++;

        if (((1 << collision.gameObject.layer) & obstacleLayerMask) != 0)
            wallCollisionCount++;
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

        Debug.Log($"[Drone:{droneName}] {tag} role={role} status={status} {cellStr} pos={Fmt2(rb.position)} desired={Fmt2(desired)} smooth={Fmt2(smoothed)}");
    }

    private static string Fmt2(Vector2 v) => $"({v.x:F2}, {v.y:F2})";

    // =====================================================
    // ✅ COMPAT: ResetRuntimeState (dipakai SimManager)
    // =====================================================
    public void ResetRuntimeState()
    {
        hasFoundTarget = false;
        isStopped = false;
        status = DroneStatus.SEARCH;

        wallCollisionCount = 0;
        droneCollisionCount = 0;

        fixedTurnVelDeg = 0f;
        smoothedDir = transform.right;

        cornerHoldUntil = 0f;
    }
}