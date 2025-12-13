using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Drone : MonoBehaviour
{
    public enum DroneStatus { SEARCH, AVOID, CHASE, FOUND, RETURN, STUCK, ARRIVED }

    [Header("Identity")]
    public string droneName = "Drone1";

    [Header("Movement")]
    public float moveSpeed = 2.0f;
    public float turnSpeedDeg = 220f;
    [Tooltip("6-14 recommended. Bigger = smoother.")]
    public float steeringSmoothing = 10f;

    [Header("Collision Masks")]
    [Tooltip("Mask untuk wall saja (dipakai untuk LOS target).")]
    public LayerMask wallLayerMask;

    [Tooltip("Mask obstacle untuk avoidance & collision (REKOMENDASI: Wall + Target layer). Jika 0, fallback ke wallLayerMask.")]
    public LayerMask obstacleLayerMask;

    [Header("Wall Avoidance")]
    public float sensorRange = 1.6f;
    public float wallHardDistance = 0.45f;
    public float wallSoftDistance = 0.95f;
    public float skin = 0.02f;

    [Header("Exploration")]
    [Range(0f, 1f)]
    public float randomSteerStrength = 0.08f;

    [Header("Anti-Room-Lock (Open Space Bias)")]
    [Range(0f, 1f)]
    public float openSpaceBias = 0.9f;
    public float probeRangeMultiplier = 1.6f;

    [Header("Anti-Stuck")]
    public float stuckTimeThreshold = 1.2f;
    public float minMoveDelta = 0.003f;

    [Header("Target Detection (LOS)")]
    public Transform target;
    public float detectRange = 6f;
    public float foundDistance = 0.35f;

    [Header("Return Home (to START position)")]
    public Transform homeBase;

    [Tooltip("Jarak dianggap sampai (tanpa SNAP).")]
    public float homeArriveDistance = 0.55f;

    [Tooltip("Radius pendekatan akhir (lebih fokus ke home).")]
    public float returnFinalApproachRadius = 1.6f;

    [Range(0.05f, 1f)]
    public float returnMinSpeedFactor = 0.25f;

    [Header("Return Anti-Stuck Boost")]
    [Range(0f, 1f)]
    public float returnDoorBias = 0.85f;

    public float returnSideStep = 0.10f;

    [Header("Return Wall Follow")]
    [Tooltip("Kalau RETURN depan buntu, aktifkan wall-follow selama beberapa saat (detik).")]
    public float returnWallFollowSeconds = 0.85f;

    [Tooltip("Seberapa kuat wall-follow (0.6-1.2 enak).")]
    public float returnWallFollowStrength = 0.95f;

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
    private bool hasFoundTarget = false;
    private bool isReturning = false;
    private bool isStopped = false;

    // start pose = HOME real
    private Vector2 startHomePos;
    private float startHomeRotZ;
    private bool hasCapturedStartHome = false;

    private bool arrivedLogged = false;
    private float currentReturnDist = 999f;

    // RETURN helpers
    private Vector2 currentReturnDir = Vector2.right;

    // cache sensor
    private float lastFrontHitDist = 999f;
    private float lastLeftHitDist = 999f;
    private float lastRightHitDist = 999f;
    private int returnStuckCount = 0;

    // wall-follow state
    private float returnWallFollowTimer = 0f;
    private int returnFollowSign = 1; // +1 follow left, -1 follow right

    // cache mask
    private LayerMask ObstacleMask => (obstacleLayerMask.value != 0) ? obstacleLayerMask : wallLayerMask;

    // filters (biar query stabil + ignore trigger)
    private ContactFilter2D obstacleFilter;
    private ContactFilter2D wallFilter;

    // non-alloc buffers
    private readonly RaycastHit2D[] hitBuf = new RaycastHit2D[1];
    private readonly Collider2D[] overlapBuf = new Collider2D[16];

    // ===================== Public API =====================
    public void SetTarget(Transform t) => target = t;
    public void SetHome(Transform h) => homeBase = h;

    public void CaptureStartHomeNow()
    {
        startHomePos = transform.position;
        startHomeRotZ = transform.eulerAngles.z;
        hasCapturedStartHome = true;

        if (verbose)
            Debug.Log($"[Drone:{droneName}] CaptureStartHomeNow pos={startHomePos} rotZ={startHomeRotZ:F1}");
    }

    public void ResetMission()
    {
        hasFoundTarget = false;
        isReturning = false;
        isStopped = false;
        arrivedLogged = false;
        stuckTimer = 0f;

        desiredDirection = transform.right;
        smoothedDirection = desiredDirection;

        returnWallFollowTimer = 0f;
        returnStuckCount = 0;

        SetStatus(DroneStatus.SEARCH);
    }

    public void StopMission()
    {
        isStopped = true;
        SetStatus(DroneStatus.STUCK);
    }

    private void Awake()
    {
        col = GetComponent<Collider2D>();
        bodyRadius = EstimateBodyRadius(col) + skin;

        desiredDirection = transform.right;
        smoothedDirection = desiredDirection;

        lastPos = transform.position;

        // setup filters
        obstacleFilter = new ContactFilter2D();
        obstacleFilter.SetLayerMask(ObstacleMask);
        obstacleFilter.useTriggers = false;

        wallFilter = new ContactFilter2D();
        wallFilter.SetLayerMask(wallLayerMask);
        wallFilter.useTriggers = false;

        if (verbose)
            Debug.Log($"[Drone:{droneName}] Awake() bodyRadius={bodyRadius:F3}");

        // ✅ depenetrate yang benar (bukan random)
        for (int i = 0; i < 6; i++) ResolveOverlaps(1);

        // fallback: kalau SimManager belum sempat capture
        if (!hasCapturedStartHome)
        {
            startHomePos = transform.position;
            startHomeRotZ = transform.eulerAngles.z;
            hasCapturedStartHome = true;
        }
    }

    private void Update()
    {
        if (isStopped) return;

        if (returnWallFollowTimer > 0f)
            returnWallFollowTimer -= Time.deltaTime;

        // 1) decide intention
        Vector2 intentionDir = DecideIntentionDirection();

        // 2) avoidance + smoothing
        desiredDirection = ComputeAvoidedDirection(intentionDir);

        float t = 1f - Mathf.Exp(-steeringSmoothing * Time.deltaTime);
        smoothedDirection = Vector2.Lerp(smoothedDirection, desiredDirection, t);
        if (smoothedDirection.sqrMagnitude < 0.0001f) smoothedDirection = desiredDirection;
        smoothedDirection.Normalize();

        // 3) rotate
        RotateTowards(smoothedDirection);

        // speed factor saat RETURN dan dekat home (clamp)
        float speedFactor = 1f;
        if (status == DroneStatus.RETURN)
        {
            float k = Mathf.InverseLerp(homeArriveDistance, returnFinalApproachRadius, currentReturnDist);
            k = Mathf.Clamp01(k);
            speedFactor = Mathf.Lerp(returnMinSpeedFactor, 1f, k);

            // ✅ kalau lagi wall-follow / dekat dinding, jangan “ngebut”
            if (returnWallFollowTimer > 0f || lastFrontHitDist < wallSoftDistance * 0.9f)
                speedFactor = Mathf.Min(speedFactor, 0.75f);
        }

        // 4) move (NO PENETRATION) against obstacle mask
        Vector2 delta = (Vector2)transform.right * (moveSpeed * speedFactor * Time.deltaTime);
        bool moved = MoveWithCollision(delta);

        // 5) stuck
        HandleStuck(moved);
    }

    // =========================================================
    // MODE DECISION
    // =========================================================
    private Vector2 DecideIntentionDirection()
    {
        // RETURN
        if (isReturning)
        {
            SetStatus(DroneStatus.RETURN);

            Vector2 toHome = startHomePos - (Vector2)transform.position;
            currentReturnDist = toHome.magnitude;
            currentReturnDir = (toHome.sqrMagnitude > 0.0001f) ? toHome.normalized : (Vector2)transform.right;

            if (currentReturnDist <= homeArriveDistance)
            {
                isReturning = false;
                isStopped = true;

                SetStatus(DroneStatus.ARRIVED);

                if (!arrivedLogged)
                {
                    arrivedLogged = true;
                    Debug.Log($"[Drone:{droneName}] ARRIVED HOME (no snap) at {transform.position} dist={currentReturnDist:F2}");
                }

                return transform.right;
            }

            return currentReturnDir;
        }

        // TARGET logic
        if (target != null)
        {
            bool visible = HasLineOfSightToTarget(out float dist);

            if (visible)
            {
                if (!hasFoundTarget && dist <= foundDistance)
                {
                    hasFoundTarget = true;
                    SetStatus(DroneStatus.FOUND);
                    Debug.Log($"[Drone:{droneName}] FOUND target at {target.position} dist={dist:F2}");

                    isReturning = true;
                    SetStatus(DroneStatus.RETURN);

                    currentReturnDist = (startHomePos - (Vector2)transform.position).magnitude;
                    currentReturnDir = (startHomePos - (Vector2)transform.position).normalized;

                    return transform.right;
                }

                SetStatus(DroneStatus.CHASE);
                Vector2 toT = (Vector2)target.position - (Vector2)transform.position;
                return toT.normalized;
            }
        }

        if (status != DroneStatus.AVOID && status != DroneStatus.STUCK)
            SetStatus(DroneStatus.SEARCH);

        return transform.right;
    }

    private bool HasLineOfSightToTarget(out float dist)
    {
        dist = 999f;
        if (target == null) return false;

        Vector2 origin = transform.position;
        Vector2 toT = (Vector2)target.position - origin;
        dist = toT.magnitude;

        if (dist > detectRange) return false;

        // LOS hanya wall
        int n = Physics2D.Raycast(origin, toT.normalized, wallFilter, hitBuf, dist);
        return n == 0;
    }

    // =========================================================
    // AVOIDANCE + OPEN SPACE BIAS + RETURN WALL-FOLLOW
    // =========================================================
    private Vector2 ComputeAvoidedDirection(Vector2 intentionDir)
    {
        Vector2 origin = transform.position;
        Vector2 forward = transform.right;
        Vector2 left = transform.up;
        Vector2 right = -transform.up;

        float f = CastDistance(origin, forward, sensorRange);
        float l = CastDistance(origin, left, sensorRange * 0.9f);
        float r = CastDistance(origin, right, sensorRange * 0.9f);

        lastFrontHitDist = f;
        lastLeftHitDist = l;
        lastRightHitDist = r;

        // === RETURN: kalau depan buntu → aktifkan wall-follow sebentar
        if (status == DroneStatus.RETURN)
        {
            bool frontBlockedHard = (f < wallHardDistance * 1.05f);
            if (frontBlockedHard)
            {
                float scoreL = l + Vector2.Dot(left, currentReturnDir) * (0.35f + 0.35f * returnDoorBias);
                float scoreR = r + Vector2.Dot(right, currentReturnDir) * (0.35f + 0.35f * returnDoorBias);
                returnFollowSign = (scoreL >= scoreR) ? 1 : -1;
                returnWallFollowTimer = returnWallFollowSeconds;
            }
        }

        float avoidStrength = 0f;
        if (f < wallHardDistance) avoidStrength = 1f;
        else if (f < wallSoftDistance) avoidStrength = Mathf.InverseLerp(wallSoftDistance, wallHardDistance, f);

        Vector2 avoid = Vector2.zero;
        if (avoidStrength > 0f)
        {
            float turnSign = (l > r) ? 1f : -1f;
            Vector2 side = (turnSign > 0) ? (Vector2)transform.up : -(Vector2)transform.up;
            avoid = (side * 1.0f + (-forward) * 0.35f).normalized;

            if (status != DroneStatus.RETURN)
                SetStatus(DroneStatus.AVOID);
        }

        Vector2 openDir = GetBestOpenDirection(origin, probeRangeMultiplier * sensorRange);

        float randomFactor = (status == DroneStatus.SEARCH) ? 1f : 0.20f;
        Vector2 random = Random.insideUnitCircle.normalized * (randomSteerStrength * randomFactor);

        Vector2 baseDir = (intentionDir.sqrMagnitude > 0.0001f) ? intentionDir : forward;

        // === RETURN wall-follow vector
        Vector2 follow = Vector2.zero;
        if (status == DroneStatus.RETURN && returnWallFollowTimer > 0f)
        {
            Vector2 side = (returnFollowSign > 0) ? (Vector2)transform.up : -(Vector2)transform.up;
            follow = (forward * 0.75f + side * 0.85f).normalized * returnWallFollowStrength;
        }

        float openWeight = (0.90f * openSpaceBias);
        float randomWeight = 0.60f;
        float baseWeight = 1.10f;
        float followWeight = 0f;

        if (status == DroneStatus.RETURN)
        {
            float k = Mathf.InverseLerp(homeArriveDistance, returnFinalApproachRadius, currentReturnDist);
            k = Mathf.Clamp01(k);

            bool frontBlocked = lastFrontHitDist < wallSoftDistance * 0.85f;

            // ✅ RETURN: kalau dekat dinding/blocked -> hindari dulu, baru pulang
            baseWeight = frontBlocked ? Mathf.Lerp(1.10f, 1.25f, k) : Mathf.Lerp(1.60f, 1.30f, k);
            openWeight = frontBlocked ? Mathf.Lerp(0.55f, 0.85f, k) : Mathf.Lerp(0.10f, 0.45f, k);
            randomWeight = frontBlocked ? 0.04f : Mathf.Lerp(0.02f, 0.06f, k);

            followWeight = (returnWallFollowTimer > 0f) ? 1.35f : 0f;
        }

        Vector2 finalDir =
            baseDir * baseWeight +
            avoid * 2.05f +
            openDir * openWeight +
            random * randomWeight +
            follow * followWeight;

        if (finalDir.sqrMagnitude < 0.0001f) finalDir = forward;
        return finalDir.normalized;
    }

    private Vector2 GetBestOpenDirection(Vector2 origin, float probeDist)
    {
        Vector2[] dirs =
        {
            (Vector2)transform.right,
            ((Vector2)transform.right + (Vector2)transform.up).normalized,
            (Vector2)transform.up,
            (-(Vector2)transform.right + (Vector2)transform.up).normalized,
            -(Vector2)transform.right,
            (-(Vector2)transform.right - (Vector2)transform.up).normalized,
            -(Vector2)transform.up,
            ((Vector2)transform.right - (Vector2)transform.up).normalized
        };

        float bestScore = -999f;
        Vector2 bestDir = transform.right;

        for (int i = 0; i < dirs.Length; i++)
        {
            float d = CastDistance(origin, dirs[i], probeDist);
            float score = d;

            score += Vector2.Dot(dirs[i], (Vector2)transform.right) * 0.10f;

            if (status == DroneStatus.RETURN)
                score += Vector2.Dot(dirs[i], currentReturnDir) * (0.55f + 0.35f * returnDoorBias);

            if (score > bestScore)
            {
                bestScore = score;
                bestDir = dirs[i];
            }
        }

        return bestDir.normalized;
    }

    private float CastDistance(Vector2 origin, Vector2 dir, float dist)
    {
        hitBuf[0] = default;
        int n = Physics2D.CircleCast(origin, bodyRadius, dir, obstacleFilter, hitBuf, dist);

        if (n > 0 && hitBuf[0].collider != null)
        {
            // kalau start di dalam collider, distance sering 0 -> anggap super dekat
            return Mathf.Max(0f, hitBuf[0].distance);
        }

        return dist;
    }

    // =========================================================
    // MOVEMENT
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

        // ✅ kalau sudah overlap, keluar dulu
        ResolveOverlaps(2);

        Vector2 origin = transform.position;
        Vector2 dir = delta.normalized;
        float dist = delta.magnitude;

        hitBuf[0] = default;
        int n = Physics2D.CircleCast(origin, bodyRadius, dir, obstacleFilter, hitBuf, dist);

        if (n == 0 || hitBuf[0].collider == null)
        {
            transform.position = (Vector2)transform.position + delta;
            return true;
        }

        RaycastHit2D hit = hitBuf[0];

        float safeMove = Mathf.Max(0f, hit.distance - (skin + 0.001f));
        if (safeMove > 0f)
            transform.position = (Vector2)transform.position + dir * safeMove;

        // slide 2 sisi
        Vector2 normal = hit.normal;
        Vector2 slideA = Vector2.Perpendicular(normal).normalized;
        if (Vector2.Dot(slideA, dir) < 0f) slideA = -slideA;
        Vector2 slideB = -slideA;

        float remaining = dist - safeMove;
        if (remaining > 0.0001f)
        {
            // A
            hitBuf[0] = default;
            int nA = Physics2D.CircleCast((Vector2)transform.position, bodyRadius, slideA, obstacleFilter, hitBuf, remaining);
            float moveA = (nA == 0 || hitBuf[0].collider == null) ? remaining : Mathf.Max(0f, hitBuf[0].distance - (skin + 0.001f));
            if (moveA > 0.0001f)
            {
                transform.position = (Vector2)transform.position + slideA * moveA;
                return true;
            }

            // B
            hitBuf[0] = default;
            int nB = Physics2D.CircleCast((Vector2)transform.position, bodyRadius, slideB, obstacleFilter, hitBuf, remaining);
            float moveB = (nB == 0 || hitBuf[0].collider == null) ? remaining : Mathf.Max(0f, hitBuf[0].distance - (skin + 0.001f));
            if (moveB > 0.0001f)
            {
                transform.position = (Vector2)transform.position + slideB * moveB;
                return true;
            }
        }

        // micro reverse khusus RETURN
        if (status == DroneStatus.RETURN)
        {
            Vector2 back = -dir;
            float backDist = Mathf.Min(0.14f, dist);

            hitBuf[0] = default;
            int nb = Physics2D.CircleCast((Vector2)transform.position, bodyRadius, back, obstacleFilter, hitBuf, backDist);
            float backMove = (nb == 0 || hitBuf[0].collider == null) ? backDist : Mathf.Max(0f, hitBuf[0].distance - (skin + 0.001f));
            if (backMove > 0.0001f)
            {
                transform.position = (Vector2)transform.position + back * backMove;
                return true;
            }
        }

        return false;
    }

    // ✅ depenetrate yang benar pakai ColliderDistance2D (keluar dari overlap)
    private void ResolveOverlaps(int iterations)
    {
        for (int it = 0; it < iterations; it++)
        {
            int count = Physics2D.OverlapCircleNonAlloc(transform.position, bodyRadius, overlapBuf, ObstacleMask);
            if (count <= 0) return;

            bool pushed = false;

            for (int i = 0; i < count; i++)
            {
                Collider2D other = overlapBuf[i];
                if (other == null) continue;
                if (other == col) continue;

                ColliderDistance2D cd = col.Distance(other);
                if (!cd.isOverlapped) continue;

                // cd.distance negatif kalau overlap
                float pushDist = (-cd.distance) + (skin + 0.002f);
                Vector2 push = cd.normal * pushDist;

                // clamp biar tidak “teleport”
                float maxPush = 0.25f;
                if (push.magnitude > maxPush) push = push.normalized * maxPush;

                transform.position = (Vector2)transform.position + push;
                pushed = true;
            }

            if (!pushed) return;
        }
    }

    // =========================================================
    // STUCK (RETURN lebih longgar, adaptif + PRIORITAS LEPAS DINDING)
    // =========================================================
    private void HandleStuck(bool movedThisFrame)
    {
        float movedDist = Vector2.Distance(transform.position, lastPos);

        float adaptiveMin = minMoveDelta;
        if (status == DroneStatus.RETURN)
        {
            float k = Mathf.InverseLerp(homeArriveDistance, returnFinalApproachRadius, currentReturnDist);
            k = Mathf.Clamp01(k);
            adaptiveMin = Mathf.Lerp(minMoveDelta * 0.20f, minMoveDelta * 0.60f, k);
        }

        if (!movedThisFrame || movedDist < adaptiveMin)
        {
            stuckTimer += Time.deltaTime;
            if (stuckTimer >= stuckTimeThreshold)
            {
                if (status == DroneStatus.RETURN)
                {
                    returnStuckCount++;

                    // ✅ PRIORITAS: lepas overlap dulu
                    ResolveOverlaps(6);

                    Debug.Log($"[Drone:{droneName}] RETURN-STUCK#{returnStuckCount} distHome={currentReturnDist:F2} f={lastFrontHitDist:F2} l={lastLeftHitDist:F2} r={lastRightHitDist:F2}");

                    // paksa wall-follow biar keluar pintu/corner
                    returnWallFollowTimer = Mathf.Max(returnWallFollowTimer, returnWallFollowSeconds);

                    // sidestep kecil sesuai arah follow
                    Vector2 side = (returnFollowSign > 0) ? (Vector2)transform.up : -(Vector2)transform.up;
                    TryNudge(side * returnSideStep);

                    // putar sedikit menjauh dari dinding jika depan benar-benar mepet
                    if (lastFrontHitDist < wallHardDistance * 1.1f)
                    {
                        float ang = (returnFollowSign > 0) ? 85f : -85f;
                        transform.Rotate(0f, 0f, ang);
                    }
                    else
                    {
                        // kalau tidak terlalu mepet, baru bias ke home
                        float ang = Vector2.SignedAngle(transform.right, currentReturnDir);
                        transform.Rotate(0f, 0f, Mathf.Clamp(ang, -75f, 75f));
                    }

                    desiredDirection = transform.right;
                    smoothedDirection = desiredDirection;
                }
                else if (status != DroneStatus.ARRIVED)
                {
                    SetStatus(DroneStatus.STUCK);
                    ForceEscapeTurn();
                }

                stuckTimer = 0f;
            }
        }
        else
        {
            stuckTimer = 0f;
        }

        lastPos = transform.position;
    }

    private void TryNudge(Vector2 nudge)
    {
        if (nudge.sqrMagnitude < 0.000001f) return;

        Vector2 origin = transform.position;
        Vector2 dir = nudge.normalized;
        float dist = nudge.magnitude;

        hitBuf[0] = default;
        int n = Physics2D.CircleCast(origin, bodyRadius, dir, obstacleFilter, hitBuf, dist);
        if (n == 0 || hitBuf[0].collider == null)
        {
            transform.position = (Vector2)transform.position + nudge;
            return;
        }

        float safe = Mathf.Max(0f, hitBuf[0].distance - (skin + 0.001f));
        if (safe > 0.0001f)
            transform.position = (Vector2)transform.position + dir * safe;
    }

    private void ForceEscapeTurn()
    {
        float ang = Random.Range(110f, 180f);
        if (Random.value > 0.5f) ang = -ang;
        transform.Rotate(0f, 0f, ang);

        desiredDirection = transform.right;
        smoothedDirection = desiredDirection;

        if (verbose) Debug.Log($"[Drone:{droneName}] ESCAPE turn={ang:F1}");
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
        Bounds b = c.bounds;
        float r = Mathf.Max(b.extents.x, b.extents.y);
        return Mathf.Max(0.05f, r);
    }
}