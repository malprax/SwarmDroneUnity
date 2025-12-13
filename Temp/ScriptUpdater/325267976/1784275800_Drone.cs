using System.Collections.Generic;
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
    public float returnWallFollowSeconds = 0.85f;
    public float returnWallFollowStrength = 0.95f;

    // ===================== Breadcrumb =====================
    [Header("Trail Memory (Breadcrumb)")]
    public bool useTrailMemory = true;
    public float trailRecordDistance = 0.60f;
    public int trailMaxPoints = 450;
    public float trailArriveDistance = 0.55f;
    [Range(0, 40)]
    public int trailShortcutLookback = 16;
    public float trailSafeWallDistance = 0.28f;

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

    // start/home
    private Vector2 startHomePos;
    private float startHomeRotZ;
    private bool hasCapturedStartHome = false;

    private bool arrivedLogged = false;
    private float currentReturnDist = 999f;

    // RETURN
    private Vector2 currentReturnDir = Vector2.right;

    // sensor cache
    private float lastFrontHitDist = 999f;
    private float lastLeftHitDist = 999f;
    private float lastRightHitDist = 999f;
    private int returnStuckCount = 0;

    // wall-follow
    private float returnWallFollowTimer = 0f;
    private int returnFollowSign = 1;

    // cache mask
    private LayerMask ObstacleMask => (obstacleLayerMask.value != 0) ? obstacleLayerMask : wallLayerMask;

    // filters
    private ContactFilter2D obstacleFilter;
    private ContactFilter2D wallFilter;

    // non-alloc buffers
    private readonly RaycastHit2D[] hitBuf = new RaycastHit2D[1];
    private readonly Collider2D[] overlapCols = new Collider2D[24];

    // trail
    private readonly List<Vector2> trail = new List<Vector2>(512);
    private Vector2 lastTrailPos;

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

        trail.Clear();
        trail.Add(startHomePos);
        lastTrailPos = startHomePos;

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

        obstacleFilter = new ContactFilter2D();
        obstacleFilter.SetLayerMask(ObstacleMask);
        obstacleFilter.useTriggers = false;

        wallFilter = new ContactFilter2D();
        wallFilter.SetLayerMask(wallLayerMask);
        wallFilter.useTriggers = false;

        // depenetrate kuat
        ResolveOverlapsPenetration(10);

        if (!hasCapturedStartHome)
        {
            startHomePos = transform.position;
            startHomeRotZ = transform.eulerAngles.z;
            hasCapturedStartHome = true;
        }

        trail.Clear();
        trail.Add(startHomePos);
        lastTrailPos = startHomePos;
    }

    private void Update()
    {
        if (isStopped) return;

        if (returnWallFollowTimer > 0f)
            returnWallFollowTimer -= Time.deltaTime;

        // ✅ selalu pastikan tidak nancep (terutama setelah FOUND/RETURN)
        ResolveOverlapsPenetration(isReturning ? 2 : 1);

        // 1) intention
        Vector2 intentionDir = DecideIntentionDirection();

        // 2) avoidance+smoothing
        desiredDirection = ComputeAvoidedDirection(intentionDir);

        float t = 1f - Mathf.Exp(-steeringSmoothing * Time.deltaTime);
        smoothedDirection = Vector2.Lerp(smoothedDirection, desiredDirection, t);
        if (smoothedDirection.sqrMagnitude < 0.0001f) smoothedDirection = desiredDirection;
        smoothedDirection.Normalize();

        // 3) rotate
        RotateTowards(smoothedDirection);

        // speed factor
        float speedFactor = 1f;
        if (status == DroneStatus.RETURN)
        {
            float k = Mathf.InverseLerp(homeArriveDistance, returnFinalApproachRadius, currentReturnDist);
            k = Mathf.Clamp01(k);
            speedFactor = Mathf.Lerp(returnMinSpeedFactor, 1f, k);

            if (returnWallFollowTimer > 0f || lastFrontHitDist < wallSoftDistance * 0.9f)
                speedFactor = Mathf.Min(speedFactor, 0.75f);
        }

        // 4) move
        Vector2 delta = (Vector2)transform.right * (moveSpeed * speedFactor * Time.deltaTime);
        bool moved = MoveWithCollision(delta);

        // 4.5) record trail
        RecordTrailIfNeeded();

        // 5) stuck
        HandleStuck(moved);
    }

    // =========================================================
    // MODE DECISION
    // =========================================================
    private Vector2 DecideIntentionDirection()
    {
        if (isReturning)
        {
            SetStatus(DroneStatus.RETURN);

            currentReturnDist = Vector2.Distance(transform.position, startHomePos);

            Vector2 goal = useTrailMemory ? GetReturnGoalFromTrail() : startHomePos;
            Vector2 toGoal = goal - (Vector2)transform.position;
            currentReturnDir = (toGoal.sqrMagnitude > 0.0001f) ? toGoal.normalized : (Vector2)transform.right;

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

                    // ✅ sebelum RETURN: paksa keluar sedikit dari target (anti nancep)
                    Vector2 away = ((Vector2)transform.position - (Vector2)target.position);
                    if (away.sqrMagnitude > 0.0001f)
                    {
                        TryNudge(away.normalized * 0.18f);
                        ResolveOverlapsPenetration(6);
                    }

                    isReturning = true;
                    SetStatus(DroneStatus.RETURN);

                    if (useTrailMemory) ForceAddTrailPoint(transform.position);

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

        int n = Physics2D.Raycast(origin, toT.normalized, wallFilter, hitBuf, dist);
        return n == 0;
    }

    // =========================================================
    // BREADCRUMB
    // =========================================================
    private void RecordTrailIfNeeded()
    {
        if (!useTrailMemory) return;
        if (isReturning || status == DroneStatus.RETURN || status == DroneStatus.ARRIVED) return;

        Vector2 p = transform.position;

        float f = CastDistance(p, transform.right, trailSafeWallDistance);
        float l = CastDistance(p, transform.up, trailSafeWallDistance);
        float r = CastDistance(p, -transform.up, trailSafeWallDistance);

        if (f < trailSafeWallDistance || l < trailSafeWallDistance || r < trailSafeWallDistance)
            return;

        if (Vector2.Distance(p, lastTrailPos) >= trailRecordDistance)
        {
            trail.Add(p);
            lastTrailPos = p;

            if (trail.Count > trailMaxPoints)
                trail.RemoveAt(1);
        }
    }

    private void ForceAddTrailPoint(Vector2 p)
    {
        if (trail.Count == 0)
        {
            trail.Add(startHomePos);
            lastTrailPos = startHomePos;
        }

        if (Vector2.Distance(p, trail[trail.Count - 1]) >= 0.15f)
        {
            trail.Add(p);
            lastTrailPos = p;
        }
    }

    private Vector2 GetReturnGoalFromTrail()
    {
        if (trail.Count == 0) return startHomePos;

        Vector2 curr = transform.position;
        int last = trail.Count - 1;

        if (last > 0 && Vector2.Distance(curr, trail[last]) <= trailArriveDistance)
        {
            trail.RemoveAt(last);
            last = trail.Count - 1;
        }

        if (last <= 0) return startHomePos;

        int bestIndex = last;
        int minIndex = Mathf.Max(0, last - trailShortcutLookback);

        for (int i = last; i >= minIndex; i--)
        {
            Vector2 wp = trail[i];
            Vector2 diff = wp - curr;
            float dist = diff.magnitude;
            if (dist < 0.001f) { bestIndex = i; continue; }

            hitBuf[0] = default;
            int n = Physics2D.CircleCast(curr, bodyRadius, diff.normalized, obstacleFilter, hitBuf, dist);
            if (n == 0 || hitBuf[0].collider == null)
            {
                bestIndex = i;
                break;
            }
        }

        return trail[bestIndex];
    }

    // =========================================================
    // AVOIDANCE + RETURN WALL-FOLLOW
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

        // RETURN: kalau depan buntu → wall-follow
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

            // RETURN: lepas dinding dulu
            baseWeight = frontBlocked ? Mathf.Lerp(1.00f, 1.15f, k) : Mathf.Lerp(1.55f, 1.30f, k);
            openWeight = frontBlocked ? Mathf.Lerp(0.65f, 0.95f, k) : Mathf.Lerp(0.12f, 0.45f, k);
            randomWeight = frontBlocked ? 0.03f : Mathf.Lerp(0.02f, 0.06f, k);

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
                score += Vector2.Dot(dirs[i], currentReturnDir) * (0.45f + 0.35f * returnDoorBias);

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
            return Mathf.Max(0f, hitBuf[0].distance);

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

        // ✅ keluar overlap dulu
        ResolveOverlapsPenetration(3);

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

        // slide
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

        // micro reverse RETURN
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

    // ✅ depenetrate SUPER KUAT: ComputePenetration
    private void ResolveOverlapsPenetration(int iterations)
    {
        for (int it = 0; it < iterations; it++)
        {
            int count = col.Overlap(obstacleFilter, overlapCols);
            if (count <= 0) return;

            bool separatedAny = false;

            for (int i = 0; i < count; i++)
            {
                Collider2D other = overlapCols[i];
                if (other == null) continue;
                if (other == col) continue;

                if (Physics2D.ComputePenetration(
                        col, transform.position, transform.rotation,
                        other, other.transform.position, other.transform.rotation,
                        out Vector2 sepDir, out float sepDist))
                {
                    // sepDist = jarak minimal untuk pisah
                    float push = sepDist + (skin + 0.003f);

                    // clamp biar tidak teleport
                    float maxPush = 0.35f;
                    if (push > maxPush) push = maxPush;

                    transform.position = (Vector2)transform.position + sepDir * push;
                    separatedAny = true;
                }
            }

            if (!separatedAny) return;
        }
    }

    // =========================================================
    // STUCK (RETURN: kalau sensor semua 0 -> pasti nancep)
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

                    // emergency: kalau f/l/r 0 -> overlap parah, depenetrate besar
                    if (lastFrontHitDist <= 0.001f && lastLeftHitDist <= 0.001f && lastRightHitDist <= 0.001f)
                        ResolveOverlapsPenetration(18);
                    else
                        ResolveOverlapsPenetration(8);

                    if (useTrailMemory) ForceAddTrailPoint(transform.position);

                    Debug.Log($"[Drone:{droneName}] RETURN-STUCK#{returnStuckCount} distHome={currentReturnDist:F2} f={lastFrontHitDist:F2} l={lastLeftHitDist:F2} r={lastRightHitDist:F2} trail={trail.Count}");

                    returnWallFollowTimer = Mathf.Max(returnWallFollowTimer, returnWallFollowSeconds);

                    Vector2 side = (returnFollowSign > 0) ? (Vector2)transform.up : -(Vector2)transform.up;
                    TryNudge(side * returnSideStep);

                    float ang = (returnFollowSign > 0) ? 85f : -85f;
                    transform.Rotate(0f, 0f, ang);

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

    private void SetStatus(DroneStatus s)
    {
        if (status == s) return;
        status = s;
        Debug.Log($"[Drone:{droneName}] STATUS -> {status}");
    }

    private float EstimateBodyRadius(Collider2D c)
    {
        Bounds b = c.bounds;
        float r = Mathf.Max(b.extents.x, b.extents.y);
        return Mathf.Max(0.05f, r);
    }
}