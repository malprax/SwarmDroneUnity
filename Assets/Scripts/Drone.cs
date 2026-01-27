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
    // Adaptive Start Dispersion (SMART) ✅
    // =========================
    [Header("Adaptive Start Dispersion (SMART)")]
    [Tooltip("Jika true, drone akan menyebar dulu dari home base secara adaptif berdasarkan posisi awal drone lain.")]
    public bool enableLaunchDispersal = true;

    [Tooltip("Maks durasi fase menyebar (detik).")]
    public float launchDisperseSeconds = 2.0f;

    [Tooltip("Jika jarak minimum ke drone lain >= ini, fase menyebar selesai lebih cepat.")]
    public float launchMinInterDroneDistance = 1.6f;

    [Tooltip("Bobot dorongan menjauh dari home base.")]
    public float launchAwayFromHomeWeight = 1.2f;

    [Tooltip("Bobot repulsion antar drone pada fase launch (lebih kuat dari separation normal).")]
    public float launchRepulsionWeight = 3.0f;

    [Tooltip("Bias arah unik per drone (0/120/240 deg) supaya tidak simetris.")]
    public float launchHeadingBiasWeight = 0.7f;

    [Tooltip("Random kecil agar tidak grid-lock.")]
    public float launchJitterWeight = 0.20f;

    [Tooltip("Index tim (0..2). Jika -1 maka auto dari instance id.")]
    public int teamIndex = -1;

    private float missionStartTime = -999f;
    private Vector2 preferredBiasDir = Vector2.zero; // dipakai setelah launch untuk “arah eksplorasi” ringan

    public void MarkMissionStarted()
    {
        missionStartTime = Time.time;
        preferredBiasDir = ComputeInitialBiasDir();
    }

    private bool IsInLaunchDispersal()
    {
        if (!enableLaunchDispersal) return false;
        if (missionStartTime < -100f) return false;

        float elapsed = Time.time - missionStartTime;
        if (elapsed > Mathf.Max(0.01f, launchDisperseSeconds)) return false;

        // selesai lebih cepat jika sudah cukup jauh dari drone lain
        float minD = GetMinDistanceToOtherDrones(Mathf.Max(launchMinInterDroneDistance, separationRadius * 1.25f));
        if (minD >= launchMinInterDroneDistance) return false;

        return true;
    }

    private Vector2 ComputeInitialBiasDir()
    {
        int idx = teamIndex;
        if (idx < 0)
        {
            // deterministik tapi beda-beda
            idx = Mathf.Abs(gameObject.GetInstanceID()) % 3;
        }

        float a = idx * (Mathf.PI * 2f / 3f); // 0, 120, 240 deg
        Vector2 b = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        if (b.sqrMagnitude < 0.0001f) b = StableRandomDir(999);
        return b.normalized;
    }

    private Vector2 DecideLaunchDispersalDirection()
    {
        Vector2 pos = rb.position;
        Vector2 home = homeBase ? (Vector2)homeBase.position : returnHomePos;

        Vector2 awayHome = pos - home;
        if (awayHome.sqrMagnitude < 0.0001f) awayHome = StableRandomDir(123);
        awayHome.Normalize();

        // repulsion kuat dari drone lain
        float scanR = Mathf.Max(launchMinInterDroneDistance, separationRadius * 1.25f);
        int count = Physics2D.OverlapCircle(pos, scanR, neighborFilter, neighborHits);

        Vector2 repulse = Vector2.zero;
        int n = 0;

        for (int i = 0; i < count; i++)
        {
            var other = neighborHits[i];
            if (!other) continue;
            if (other == col) continue;

            Vector2 delta = pos - (Vector2)other.transform.position;
            float d = delta.magnitude;

            if (d < 0.001f)
            {
                delta = StableRandomDir(i + 77);
                d = 0.001f;
            }

            float w = Mathf.Clamp01((launchMinInterDroneDistance - d) / Mathf.Max(0.001f, launchMinInterDroneDistance));
            repulse += (delta / d) * w;
            n++;
        }

        if (n > 0 && repulse.sqrMagnitude > 0.0001f)
            repulse = repulse.normalized;

        // bias unik per drone
        Vector2 bias = preferredBiasDir.sqrMagnitude > 0.0001f ? preferredBiasDir : ComputeInitialBiasDir();

        // jitter kecil
        Vector2 jitter = StableRandomDir((int)(Time.time * 10f) + 555) * Mathf.Clamp01(launchJitterWeight);

        Vector2 desired =
            awayHome * Mathf.Max(0f, launchAwayFromHomeWeight) +
            repulse * Mathf.Max(0f, launchRepulsionWeight) +
            bias * Mathf.Max(0f, launchHeadingBiasWeight) +
            jitter;

        if (desired.sqrMagnitude < 0.0001f) desired = awayHome;
        return desired.normalized;
    }

    private float GetMinDistanceToOtherDrones(float scanR)
    {
        float minD = float.PositiveInfinity;
        int count = Physics2D.OverlapCircle(rb.position, scanR, neighborFilter, neighborHits);
        for (int i = 0; i < count; i++)
        {
            var other = neighborHits[i];
            if (!other) continue;
            if (other == col) continue;

            float d = Vector2.Distance(rb.position, other.transform.position);
            if (d < minD) minD = d;
        }
        return (minD == float.PositiveInfinity) ? 999f : minD;
    }

    // =========================
    // Assigned Room (LEGACY / OPTIONAL)
    // =========================
    [Header("Assigned Room (Legacy / Optional)")]
    [Tooltip("Anchor/center ruangan yang dituju dulu (opsional). Kalau arena berubah, lebih aman pakai Launch Dispersal.")]
    public Transform assignedRoomAnchor;

    public float assignedRoomArriveDistance = 0.8f;
    public bool mustGoToAssignedRoomFirst = false; // default OFF (lebih adaptif)

    private bool reachedAssignedRoom = false;

    public void AssignRoom(Transform roomAnchor)
    {
        assignedRoomAnchor = roomAnchor;
        reachedAssignedRoom = false;

        if (navigator != null && map != null && assignedRoomAnchor != null)
        {
            if (map.WorldToCell((Vector2)assignedRoomAnchor.position, out var cell))
                navigator.ReplanToCell(cell, rb.position);
        }

        if (verbose)
            Debug.Log($"[Drone:{droneName}] ASSIGNED ROOM -> {(assignedRoomAnchor ? assignedRoomAnchor.name : "NULL")}");
    }

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

    public float separationRadius = 0.90f;
    public float separationSteerWeight = 1.8f;

    [Header("Depenetration (Anti Overlap)")]
    public float depenetrationStrength = 1.4f;
    public float depenetrationMaxStep = 0.25f;
    public float depenetrationSlop = 0.015f;
    public int depenetrationIterations = 3;

    // =========================
    // Avoidance (Drone Ahead)
    // =========================
    [Header("Avoidance (Drone Ahead)")]
    public bool enableDroneAheadAvoid = true;
    public float droneAheadProbe = 0.85f;
    public float droneAheadSkin = 0.03f;
    public float droneAvoidTurnBias = 1.2f;
    public float droneSideProbe = 0.95f;

    // =========================
    // Corner Anti-Jam
    // =========================
    [Header("Corner Anti-Jam")]
    public bool enableCornerAntiJam = true;
    public float cornerBackoffStep = 0.10f;
    public float cornerHoldSeconds = 0.15f;
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
    // Home / Return
    // =========================
    [Header("Home / Return")]
    public Transform homeBase; // legacy/base (dipakai SimManager)
    [HideInInspector] public Vector2 returnHomePos;
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
    // Mission Controller link
    // =========================
    [HideInInspector] public SimManager simManager;

    // =========================
    // Standby gate
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

    [HideInInspector] public int wallCollisionCount = 0;
    [HideInInspector] public int droneCollisionCount = 0;

    private float cornerHoldUntil = 0f;

    // untuk adaptive radius yang benar (bukan always-1)
    private float lastStepDist = 0f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        circleCol = GetComponent<CircleCollider2D>();

        if (ledMarker == null)
        {
            var t = transform.Find("LEDMarker");
            if (t != null) ledMarker = t;
        }
        if (ledRenderer == null && ledMarker != null)
            ledRenderer = ledMarker.GetComponent<SpriteRenderer>();
        ApplyLedColor();

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

        rb.gravityScale = 0f;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        returnHomePos = rb.position;
        smoothedDir = transform.right;

        wallCastFilter = new ContactFilter2D();
        wallCastFilter.useTriggers = false;
        wallCastFilter.SetLayerMask(obstacleLayerMask);

        droneCastFilter = new ContactFilter2D();
        droneCastFilter.useTriggers = false;
        droneCastFilter.SetLayerMask(droneLayerMask);

        neighborFilter = new ContactFilter2D();
        neighborFilter.useTriggers = false;
        neighborFilter.SetLayerMask(droneLayerMask);

        isStandby = startStandby;

        // bias dir siap meski MarkMissionStarted belum dipanggil
        preferredBiasDir = ComputeInitialBiasDir();
    }

    private void Update()
    {
        if (isStopped || isStandby) return;

        if (navigator != null)
            navigator.TickMap(rb.position);

        Vector2 desired = DecideDesiredDirection();
        desired = ApplyCornerAvoid(desired);

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

        if (Time.time < cornerHoldUntil)
            return;

        if (enableSeparation) ApplyDepenetrationIterative();

        float rotStep = fixedTurnVelDeg * Time.fixedDeltaTime;
        rb.MoveRotation(rb.rotation + rotStep);

        if (enableCornerAntiJam && IsBlockedAhead() && HasCloseDrone())
        {
            rb.MovePosition(rb.position - (Vector2)transform.right * Mathf.Max(0.01f, cornerBackoffStep));

            Vector2 side = ChooseSaferSide();
            Vector2 newDir = (side * 1.3f + (Vector2)transform.right * 0.1f).normalized;

            smoothedDir = newDir;
            float a = Vector2.SignedAngle(transform.right, smoothedDir);
            float stepA = Mathf.Clamp(a, -60f, 60f);
            rb.MoveRotation(rb.rotation + stepA);

            cornerHoldUntil = Time.time + Mathf.Max(0.01f, cornerHoldSeconds);
            return;
        }

        if (enableBlockedAheadEscape && IsBlockedAhead())
        {
            float turn = (Random.value > 0.5f) ? 90f : -90f;
            rb.MoveRotation(rb.rotation + turn);
        }

        Vector2 forward = transform.right;
        float stepDist = moveSpeed * Time.fixedDeltaTime;
        lastStepDist = stepDist;

        int wallHits = rb.Cast(forward, wallCastFilter, wallCastHits, stepDist + skin);

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
            }
            else
            {
                rb.MovePosition(rb.position + forward * safeDist);
            }
        }

        if (navigator != null)
            navigator.AdvanceWaypointIfArrived(rb.position);
    }

    private Vector2 StableRandomDir(int salt = 0)
    {
        int h = (gameObject.GetInstanceID() * 73856093) ^ (salt * 19349663);
        float a = (h & 1023) / 1023f * Mathf.PI * 2f;
        return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
    }

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
            correction = Vector2.ClampMagnitude(
                correction * Mathf.Clamp(depenetrationStrength, 0.2f, 3.0f),
                maxStep
            );

            rb.MovePosition(rb.position + correction);
        }
    }

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

    private Vector2 ChooseSaferSide()
    {
        Vector2 origin = rb.position;
        Vector2 left = transform.up;
        Vector2 right = -transform.up;

        float probe = Mathf.Max(0.05f, droneSideProbe);

        float dl = RayFreeDistance(origin, left, probe, obstacleLayerMask, droneLayerMask);
        float dr = RayFreeDistance(origin, right, probe, obstacleLayerMask, droneLayerMask);

        if (Mathf.Abs(dl - dr) < 0.0001f)
            return (StableRandomDir(99).x >= 0f) ? left : right;

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

    public void ForceReturnToHome()
    {
        if (status == DroneStatus.ARRIVED) return;

        SetStatus(DroneStatus.RETURN);

        if (navigator != null && map != null && map.WorldToCell(returnHomePos, out var homeCell))
            navigator.ReplanToCell(homeCell, rb.position);
    }

    private float GetAdaptiveRadius()
    {
        // FIX: dulu s01 selalu 1 (bug). Sekarang pakai lastStepDist agar adaptif benar.
        float intendedSpeed = moveSpeed;
        float actualSpeed = (Time.fixedDeltaTime > 0.0001f) ? (lastStepDist / Time.fixedDeltaTime) : 0f;

        float s01 = (intendedSpeed > 0.001f) ? Mathf.Clamp01(actualSpeed / intendedSpeed) : 0f;
        float extra = enableAdaptiveRadius ? (maxExtraRadius * Mathf.Pow(s01, Mathf.Max(0.1f, radiusPower))) : 0f;

        return Mathf.Max(0.01f, colRadius + extra);
    }

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

        // OPTIONAL LEGACY: assigned room (kalau kamu memang butuh)
        if (mustGoToAssignedRoomFirst && assignedRoomAnchor != null && !reachedAssignedRoom)
        {
            float dRoom = Vector2.Distance(rb.position, assignedRoomAnchor.position);
            if (dRoom <= assignedRoomArriveDistance)
            {
                reachedAssignedRoom = true;
                if (verbose) Debug.Log($"[Drone:{droneName}] ARRIVED ASSIGNED ROOM -> {assignedRoomAnchor.name}");
            }
            else
            {
                if (navigator != null && map != null)
                {
                    bool needPlan = !navigator.HasPath || navigator.ShouldReplan();
                    if (needPlan && map.WorldToCell((Vector2)assignedRoomAnchor.position, out var roomCell))
                        navigator.ReplanToCell(roomCell, rb.position);

                    if (navigator.HasPath)
                    {
                        Vector2 wp = navigator.GetCurrentWaypointWorld();
                        Vector2 toWp = wp - rb.position;
                        if (toWp.sqrMagnitude > 0.0001f) return toWp.normalized;
                    }
                }

                Vector2 direct = (Vector2)assignedRoomAnchor.position - rb.position;
                return (direct.sqrMagnitude > 0.0001f) ? direct.normalized : (Vector2)transform.right;
            }
        }

        // ✅ SMART: fase launch dispersal (tanpa asumsi ruangan statis)
        if (IsInLaunchDispersal())
        {
            SetStatus(DroneStatus.SEARCH);
            return DecideLaunchDispersalDirection();
        }

        // SEARCH normal (frontier)
        SetStatus(DroneStatus.SEARCH);

        if (navigator != null)
        {
            bool needPlan = !navigator.HasPath || navigator.ShouldReplan();
            if (needPlan) navigator.ReplanToFrontier(rb.position);

            if (navigator.HasPath)
            {
                Vector2 wp = navigator.GetCurrentWaypointWorld();
                Vector2 toWp = wp - rb.position;

                // sedikit bias ke arah unik drone agar eksplorasi lebih “tersebar”
                if (preferredBiasDir.sqrMagnitude > 0.0001f)
                {
                    Vector2 blended = (toWp.normalized * 0.85f + preferredBiasDir * 0.15f);
                    if (blended.sqrMagnitude > 0.0001f) return blended.normalized;
                }

                if (toWp.sqrMagnitude > 0.0001f) return toWp.normalized;
            }
        }

        // fallback: arah depan + bias ringan
        Vector2 fallback = (Vector2)transform.right;
        if (preferredBiasDir.sqrMagnitude > 0.0001f)
            fallback = (fallback * 0.85f + preferredBiasDir * 0.15f).normalized;

        return fallback;
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

        string mode =
            (mustGoToAssignedRoomFirst && assignedRoomAnchor != null && !reachedAssignedRoom) ? "ASSIGNED_ROOM" :
            IsInLaunchDispersal() ? "LAUNCH_DISPERSE" :
            "NORMAL";

        string roomStr = assignedRoomAnchor ? assignedRoomAnchor.name : "none";
        Debug.Log($"[Drone:{droneName}] {tag} role={role} status={status} mode={mode} room={roomStr} reachedRoom={reachedAssignedRoom} bias={Fmt2(preferredBiasDir)} {cellStr} pos={Fmt2(rb.position)} desired={Fmt2(desired)} smooth={Fmt2(smoothed)}");
    }

    private static string Fmt2(Vector2 v) => $"({v.x:F2}, {v.y:F2})";

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

        reachedAssignedRoom = false;

        // reset launch
        missionStartTime = -999f;
        preferredBiasDir = ComputeInitialBiasDir();
    }
}