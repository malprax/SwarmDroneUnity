using UnityEngine;

/// <summary>
/// Drone sederhana: Search → temukan target → Return ke HomeBase.
/// Hindari dinding pakai 4 ray (front, right, back, left),
/// dan gunakan "PWM" kecepatan: cepat di ruang kosong, lambat dekat obstacle.
/// + Left-wall-follow + eksplorasi + anti-stuck sederhana.
/// </summary>
[DisallowMultipleComponent]
public class Drone : MonoBehaviour
{
    public enum DroneMissionState
    {
        Idle,
        Searching,
        Returning
    }

    [Header("Body / Collision")]
    [Tooltip("Perkiraan radius badan drone (meter).")]
    [SerializeField] private float bodyRadius = 0.25f;

    [Header("Identity")]
    public string droneName = "Drone";

    [Header("Movement")]
    [Tooltip("Kecepatan minimum (saat dekat dinding).")]
    public float baseMoveSpeed = 1.5f;

    [Tooltip("Kecepatan maksimum (saat ruang kosong).")]
    public float maxMoveSpeed = 3.5f;

    [Tooltip("Kecepatan putar (derajat per detik). (Belum dipakai eksplisit, rotasi langsung.)")]
    public float turnSpeedDeg = 120f;

    [Tooltip("Clearance depan yang dianggap bahaya (meter).")]
    public float frontAvoidThreshold = 0.3f;

    [Tooltip("Sudut belok saat hindari obstacle depan (derajat).")]
    public float avoidTurnAngleDeg = 70f;

    [Header("Motor / PWM")]
    [Tooltip("Jika clearance >= nilai ini → PWM=1 (full speed).")]
    [SerializeField] private float obstacleSlowdownDistance = 1.0f;

    [Tooltip("Respons throttle menuju target PWM (semakin besar semakin responsif).")]
    [SerializeField] private float throttleResponse = 4f;

    [Header("Exploration / Wall-Follow")]
    [Tooltip("Jarak dinding yang ingin dijaga saat menyusuri tembok (meter).")]
    public float wallFollowDistance = 0.4f;

    [Tooltip("Toleransi jarak dinding (meter).")]
    public float wallDistanceTolerance = 0.15f;

    [Tooltip("Berapa frame berturut-turut di open space sebelum memutuskan belok kiri.")]
    public int openSpaceTurnFrames = 8;

    [Tooltip("Sudut putar saat belok di open space (derajat).")]
    public float openSpaceTurnAngleDeg = 90f;

    [Tooltip("Sudut putar saat corner / dead-end (derajat).")]
    public float cornerTurnAngleDeg = 90f;

    [Header("Anti Stuck Sederhana")]
    [Tooltip("Jika posisi berubah kurang dari ini (meter) selama beberapa frame, dianggap stuck.")]
    public float simpleStuckPosTolerance = 0.05f;

    [Tooltip("Berapa frame kecil geraknya sebelum dianggap stuck.")]
    public int simpleStuckFrameThreshold = 25;

    [Header("Sensors (Config)")]
    [Tooltip("Layer yang dihitung sebagai obstacle/wall.")]
    public LayerMask obstacleMask;

    [Tooltip("Jarak maksimum sensor (meter) untuk raycast internal.")]
    [SerializeField] private float sensorMaxDistance = 5f;

    [Tooltip("Jarak maksimum sensor (meter) untuk gizmo/visual.")]
    public float sensorRange = 5f;

    [Header("Sensor Transforms (Optional)")]
    public Transform sensorFront;
    public Transform sensorLeft;
    public Transform sensorRight;
    public Transform sensorBack;  // opsional

    [Header("Mission State (Simple)")]
    public DroneMissionState missionState = DroneMissionState.Idle;

    [Tooltip("Kecepatan drone saat fase kembali ke HomeBase.")]
    public float returnSpeed = 2f;

    [Tooltip("Jarak maksimum ke HomeBase agar dianggap sudah sampai.")]
    public float homeReachedThreshold = 0.25f;

    public bool IsAtHome { get; private set; }

    /// <summary>
    /// Kecepatan saat ini (dipakai SimManager untuk grafik).
    /// </summary>
    public Vector2 CurrentVelocity
    {
        get
        {
            if (rb != null)
                return rb.linearVelocity;
            else
                return currentMoveDir * baseMoveSpeed;
        }
    }

    // Internal
    private Rigidbody2D rb;
    private Vector3 startPos;
    private Quaternion startRot;
    private Vector2 currentMoveDir = Vector2.up;

    // Nilai PWM 0..1 (0 = lambat, 1 = cepat)
    private float currentPwm = 1f;

    // Micromouse last known room id
    private int lastRoomId = -1;

    // Hitungan berapa kali masing-masing room pernah dikunjungi
    private System.Collections.Generic.Dictionary<int, int> roomVisitCounts =
        new System.Collections.Generic.Dictionary<int, int>();

    // Counter berapa step sudah di room yang sama (opsional)
    private int stepsInCurrentRoom = 0;

    // Flag hit sensor (langsung dari raycast)
    private bool hitFront, hitRight, hitBack, hitLeft;

    // --- Explorasi / anti-stuck internal ---
    private int openSpaceFrameCount = 0;
    private Vector2 lastPosForSimpleStuck;
    private int simpleStuckFrameCount = 0;

    // =========================================================
    //  UNITY LIFECYCLE
    // =========================================================
    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        startPos = transform.position;
        startRot = transform.rotation;

        missionState = DroneMissionState.Idle;
        IsAtHome = true;

        lastPosForSimpleStuck = transform.position;
    }

    void Start()
    {
        currentMoveDir = transform.up;

        // Kalau belum di-set di Inspector (0 atau minus), auto pakai bodyRadius
        if (sensorMaxDistance <= 0f)
            sensorMaxDistance = bodyRadius;

        if (sensorRange <= 0f)
            sensorRange = sensorMaxDistance;
    }

    // =========================================================
    //  PUBLIC API (dipanggil SimManager)
    // =========================================================
    public void StartSearch()
    {
        LogNav("[Mission] StartSearch()");
        missionState = DroneMissionState.Searching;
        IsAtHome = false;
    }

    public void StartReturnMission()
    {
        LogNav("[Mission] StartReturnHome()");
        missionState = DroneMissionState.Returning;
    }

    public void StopDrone()
    {
        LogNav("[Mission] StopDrone()");
        missionState = DroneMissionState.Idle;
        if (rb != null)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    public void ResetDrone()
    {
        LogNav("[Mission] ResetDrone()");
        StopDrone();
        IsAtHome = true;

        transform.position = startPos;
        transform.rotation = startRot;
        currentMoveDir = transform.up;
        currentPwm = 1f;

        lastRoomId = -1;
        roomVisitCounts.Clear();
        stepsInCurrentRoom = 0;

        openSpaceFrameCount = 0;
        simpleStuckFrameCount = 0;
        lastPosForSimpleStuck = transform.position;
    }

    // Rotasi di tempat (tanpa translasi), sudut dalam derajat
    void RotateInPlace(float angleDeg)
    {
        if (rb == null) return;

        // berhentikan gerak translasi
        rb.linearVelocity = Vector2.zero;

        // tambah rotasi relatif terhadap sudut sekarang
        float newAngle = rb.rotation + angleDeg;
        rb.MoveRotation(newAngle);
    }

    // =========================================================
    //  MAIN UPDATE
    // =========================================================
    void FixedUpdate()
    {
        var sim = SimManager.Instance;
        if (sim == null) return;
        if (!sim.IsPlaying) return;

        // ==========================================
        // 0) State mission: Returning / Searching
        // ==========================================
        if (missionState == DroneMissionState.Returning)
        {
            HandleReturnHome();
            return;
        }

        if (missionState != DroneMissionState.Searching)
            return;

        // ==========================================
        // 1) Baca sensor (4 arah) + hit flag
        // ==========================================
        float dF, dR, dB, dL;
        ReadSensors(out dF, out dR, out dB, out dL);
        // dF,dR,dB,dL ∈ [0 .. sensorMaxDistance]
        // hitFront / hitLeft / hitRight / hitBack = true kalau raycast kena collider

        // ==========================================
        // 2) Micromouse: cek roomId di posisi sekarang + memori Room
        // ==========================================
        int newRoomId = sim.GetRoomIdAtWorldPos(transform.position);

        if (newRoomId >= 0)
        {
            if (newRoomId != lastRoomId)
            {
                // Pindah ruangan
                lastRoomId = newRoomId;
                stepsInCurrentRoom = 0;

                // Update hitungan kunjungan room
                if (!roomVisitCounts.TryGetValue(newRoomId, out int visitCount))
                {
                    visitCount = 0;
                }
                visitCount++;
                roomVisitCounts[newRoomId] = visitCount;

                LogNav($"[Micromouse] Enter Room {newRoomId} visit={visitCount}");
            }
            else
            {
                // Masih di ruangan yang sama
                stepsInCurrentRoom++;
            }
        }
        else
        {
            // Di luar definisi room
            lastRoomId = -1;
            stepsInCurrentRoom = 0;
        }

        // ==========================================
        // 3) Hitung clearance + PWM → currentSpeed
        // ==========================================
        float clearanceFront = Mathf.Max(0f, dF - bodyRadius);
        float clearanceLeft  = Mathf.Max(0f, dL - bodyRadius);
        float clearanceRight = Mathf.Max(0f, dR - bodyRadius);

        float nearestClearance = Mathf.Min(clearanceFront, clearanceLeft, clearanceRight);
        float targetPwm = Mathf.Clamp01(nearestClearance / obstacleSlowdownDistance);
        currentPwm = Mathf.Lerp(currentPwm, targetPwm, throttleResponse * Time.fixedDeltaTime);

        // Konversi PWM → kecepatan dasar
        float currentSpeed = Mathf.Lerp(baseMoveSpeed, maxMoveSpeed, currentPwm);

        // ==========================================
        // 4) Deteksi target (kamera 360 sederhana)
        // ==========================================
        ScanWithCamera360();

        // ==========================================
        // 5) Log ke grid (statistik + tandai cell visited)
        // ==========================================
        sim.ReportGridStep(
            this,
            (Vector2)transform.position,
            dF, dR, dB, dL,
            "SearchStep"
        );

        // ==========================================
// 6) Navigasi baru: Left-wall-follow + eksplorasi + anti-stuck
// ==========================================

bool hasFront = hitFront;
bool hasLeft  = hitLeft;
bool hasRight = hitRight;

// --- 6.0) Aturan khusus dekat HomeBase (supaya tidak muter-muter di start) ---
float distFromHome = Mathf.Infinity;
bool inHomeZone = false;
if (sim.homeBase != null)
{
    distFromHome = Vector2.Distance(transform.position, sim.homeBase.position);
    // radius home zone bisa disesuaikan (1.0–2.0 meter)
    inHomeZone = distFromHome < 1.5f;
}

if (inHomeZone)
{
    // Di dekat home: aturan super sederhana
    if (!hasFront)
    {
        LogNav($"[HomeZone] Front free → Forward (dist={distFromHome:F2})");
        MoveForward(maxMoveSpeed); // kabur secepatnya dari area home
    }
    else
    {
        LogNav($"[HomeZone] Front wall → Turn left & forward (dist={distFromHome:F2})");
        RotateInPlace(-cornerTurnAngleDeg);
        MoveForward(baseMoveSpeed);
    }

    // Jangan pakai rule lain dulu
    openSpaceFrameCount = 0;
    simpleStuckFrameCount = 0;
    lastPosForSimpleStuck = transform.position;
    return;
}

// --- 6.a) Anti-stuck sangat sederhana (di luar home zone) ---
Vector2 curPos = transform.position;
float movedDist = (curPos - lastPosForSimpleStuck).magnitude;
if (movedDist < simpleStuckPosTolerance)
{
    simpleStuckFrameCount++;
    if (simpleStuckFrameCount >= simpleStuckFrameThreshold)
    {
        float sign = (Random.value < 0.5f) ? -1f : 1f;
        float escapeAngle = 135f * sign;

        LogNav($"[AntiStuckSimple] ESCAPE turn {escapeAngle:F1} deg, movedDist={movedDist:F3}");
        RotateInPlace(escapeAngle);
        MoveForward(baseMoveSpeed);

        simpleStuckFrameCount = 0;
        lastPosForSimpleStuck = transform.position;
        openSpaceFrameCount = 0;
        return;
    }
}
else
{
    simpleStuckFrameCount = 0;
    lastPosForSimpleStuck = curPos;
}

// --- 6.b) Hitung kedekatan dinding untuk wall-follow ---
bool nearLeftWall  = hasLeft  && (dL <= wallFollowDistance + wallDistanceTolerance);
bool nearRightWall = hasRight && (dR <= wallFollowDistance + wallDistanceTolerance);

// 6.c) DEAD-END: depan + kiri + kanan tembok → putar balik
if (hasFront && hasLeft && hasRight)
{
    LogNav("[NavNew] DeadEnd → RotateInPlace 180°");
    RotateInPlace(180f);
    MoveForward(baseMoveSpeed);
    openSpaceFrameCount = 0;
    return;
}

// 6.d) CORNER: depan + kanan, kiri kosong → belok kiri
if (hasFront && hasRight && !hasLeft)
{
    LogNav("[NavNew] Corner Front+Right → Turn left");
    RotateInPlace(-cornerTurnAngleDeg);
    MoveForward(baseMoveSpeed);
    openSpaceFrameCount = 0;
    return;
}

// 6.e) CORNER: depan + kiri, kanan kosong → belok kanan
if (hasFront && hasLeft && !hasRight)
{
    LogNav("[NavNew] Corner Front+Left → Turn right");
    RotateInPlace(+cornerTurnAngleDeg);
    MoveForward(baseMoveSpeed);
    openSpaceFrameCount = 0;
    return;
}

// 6.f) Open space: tidak ada tembok depan, kiri, kanan
if (!hasFront && !hasLeft && !hasRight)
{
    openSpaceFrameCount++;

    if (openSpaceFrameCount < openSpaceTurnFrames)
    {
        LogNav($"[NavNew] OpenSpace (frame={openSpaceFrameCount}) → Forward");
        MoveForward(currentSpeed);
        return;
    }

    LogNav("[NavNew] OpenSpace too long → Turn left & forward");
    RotateInPlace(-openSpaceTurnAngleDeg);
    MoveForward(currentSpeed);
    openSpaceFrameCount = 0;
    return;
}
else
{
    openSpaceFrameCount = 0;
}

// 6.g) Left-wall-follow utama
if (!nearLeftWall)
{
    if (!hasFront)
    {
        LogNav("[NavNew] Lost left wall, front free → Turn left sedikit & maju");
        RotateInPlace(-30f);
        MoveForward(currentSpeed * 0.8f);
        return;
    }
    else
    {
        LogNav("[NavNew] Lost left wall, front blocked → Turn right");
        RotateInPlace(+cornerTurnAngleDeg);
        MoveForward(baseMoveSpeed);
        return;
    }
}

if (nearLeftWall && !hasFront)
{
    LogNav("[NavNew] Follow left wall → Forward FULL SPEED");
    MoveForward(maxMoveSpeed);
    return;
}

if (nearLeftWall && hasFront)
{
    LogNav("[NavNew] Left wall + Front blocked → small right turn");
    RotateInPlace(+30f);
    MoveForward(currentSpeed * 0.8f);
    return;
}

if (!hasFront && hasLeft && hasRight)
{
    LogNav("[NavNew] Corridor (Left+Right walls, Front free) → Forward");
    MoveForward(currentSpeed);
    return;
}

if (!hasFront && !hasLeft && hasRight)
{
    LogNav("[NavNew] Follow right wall (fallback) → Forward");
    MoveForward(currentSpeed * 0.9f);
    return;
}

if (hasFront && !hasLeft && !hasRight)
{
    LogNav("[NavNew] Front wall only → Turn left");
    RotateInPlace(-cornerTurnAngleDeg);
    MoveForward(baseMoveSpeed);
    return;
}

LogNav("[NavNew] Fallback → Forward slow");
MoveForward(currentSpeed * 0.5f);
    }

    // =========================================================
    //  SENSORS
    // =========================================================
    void ReadSensors(out float dF, out float dR, out float dB, out float dL)
    {
        int mask = obstacleMask;
        float maxDist = sensorMaxDistance;

        // Origin ray dari posisi child (kalau null fallback ke badan)
        Vector2 originF = sensorFront ? (Vector2)sensorFront.position : (Vector2)transform.position;
        Vector2 originR = sensorRight ? (Vector2)sensorRight.position : (Vector2)transform.position;
        Vector2 originB = sensorBack  ? (Vector2)sensorBack.position  : (Vector2)transform.position;
        Vector2 originL = sensorLeft  ? (Vector2)sensorLeft.position  : (Vector2)transform.position;

        // Arah ray mengikuti orientasi drone:
        Vector2 dirF = transform.up;
        Vector2 dirR = transform.right;
        Vector2 dirB = -transform.up;
        Vector2 dirL = -transform.right;

        // Raycast depan
        RaycastHit2D hF = Physics2D.Raycast(originF, dirF, maxDist, mask);
        if (hF.collider != null)
        {
            dF = hF.distance;
            hitFront = true;
        }
        else
        {
            dF = maxDist;
            hitFront = false;
        }

        // Raycast kanan
        RaycastHit2D hR = Physics2D.Raycast(originR, dirR, maxDist, mask);
        if (hR.collider != null)
        {
            dR = hR.distance;
            hitRight = true;
        }
        else
        {
            dR = maxDist;
            hitRight = false;
        }

        // Raycast belakang
        RaycastHit2D hB = Physics2D.Raycast(originB, dirB, maxDist, mask);
        if (hB.collider != null)
        {
            dB = hB.distance;
            hitBack = true;
        }
        else
        {
            dB = maxDist;
            hitBack = false;
        }

        // Raycast kiri
        RaycastHit2D hL = Physics2D.Raycast(originL, dirL, maxDist, mask);
        if (hL.collider != null)
        {
            dL = hL.distance;
            hitLeft = true;
        }
        else
        {
            dL = maxDist;
            hitLeft = false;
        }

        // Debug garis di Scene view (pakai jarak terukur)
        Debug.DrawRay(originF, dirF * dF, Color.red);
        Debug.DrawRay(originR, dirR * dR, Color.green);
        Debug.DrawRay(originB, dirB * dB, Color.yellow);
        Debug.DrawRay(originL, dirL * dL, Color.blue);
    }

    // Masih disimpan kalau nanti mau dipakai lagi
    float CastSensor(Vector2 dir)
    {
        RaycastHit2D hit = Physics2D.Raycast(transform.position, dir, sensorMaxDistance, obstacleMask);
        if (hit.collider != null)
            return hit.distance;
        return sensorMaxDistance;
    }

    string ClassifyTopology(float dF, float dR, float dB, float dL)
    {
        float wallThresh = 0.7f;

        bool leftWall  = dL < wallThresh;
        bool rightWall = dR < wallThresh;

        if (leftWall && rightWall) return "Corridor";
        if (leftWall)  return "LeftWall";
        if (rightWall) return "RightWall";
        return "Open";
    }

    // =========================================================
    //  SEARCH MOVEMENT
    // =========================================================
    void MoveForward(float speed)
    {
        currentMoveDir = transform.up;

        if (rb != null)
        {
            rb.linearVelocity = currentMoveDir * speed;
        }
        else
        {
            transform.position += (Vector3)(currentMoveDir * speed * Time.fixedDeltaTime);
        }
    }

    void SteerAroundFrontObstacle(
        float dF,
        float dL,
        float dR,
        float speed,
        string contextLabel,
        float angleDeg
    )
    {
        transform.Rotate(0f, 0f, angleDeg * Time.fixedDeltaTime);

        currentMoveDir = transform.up;

        if (rb != null)
        {
            rb.linearVelocity = currentMoveDir * speed;
        }
        else
        {
            transform.position += (Vector3)(currentMoveDir * speed * Time.fixedDeltaTime);
        }
    }

    // =========================================================
    //  TARGET DETECTION
    // =========================================================
    void ScanWithCamera360()
    {
        float scanRadius = 1.5f;
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, scanRadius);

        foreach (var h in hits)
        {
            if (h == null) continue;

            if (h.CompareTag("Target"))
            {
                LogNav("[TargetScan] Target visible: " + h.name);

                var sim = SimManager.Instance;
                if (sim != null)
                {
                    sim.OnDroneFoundTarget(this);
                }

                if (missionState != DroneMissionState.Returning)
                {
                    StartReturnMission();
                }

                break;
            }
        }
    }

    // =========================================================
    //  RETURN HOME USING GRID
    // =========================================================
    void HandleReturnHome()
    {
        var sim = SimManager.Instance;
        if (sim == null || sim.homeBase == null) return;

        Vector2 currentPos = transform.position;
        Vector2 dir = sim.GetReturnDirectionFor(this, currentPos);

        float distToHome = Vector2.Distance(currentPos, sim.homeBase.position);
        if (distToHome <= homeReachedThreshold)
        {
            if (!IsAtHome)
            {
                IsAtHome = true;
                LogNav("[Mission] Reached HomeBase");
                sim.OnDroneReachedHome(this);
            }
            StopDrone();
            return;
        }

        // Kalau distanceField tidak memberi arah valid, fallback arah langsung
        if (dir.sqrMagnitude <= 1e-4f)
        {
            dir = (sim.homeBase.position - (Vector3)currentPos).normalized;
        }

        currentMoveDir = dir.normalized;
        transform.up = currentMoveDir;

        if (rb != null)
        {
            rb.linearVelocity = currentMoveDir * returnSpeed;
        }
        else
        {
            transform.position += (Vector3)(currentMoveDir * returnSpeed * Time.fixedDeltaTime);
        }
    }

    // =========================================================
    //  COLLISION HANDLING
    // =========================================================
    void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.contacts.Length == 0) return;

        Vector2 normal = collision.contacts[0].normal;
        Vector2 tangent = new Vector2(-normal.y, normal.x);
        Vector2 newDir = tangent.normalized;

        LogNav($"[Collision] normal=({normal.x:F2}, {normal.y:F2}) " +
               $"tangent=({tangent.x:F2}, {tangent.y:F2}) " +
               $"newDir=({newDir.x:F2}, {newDir.y:F2})");

        transform.up = newDir;
        currentMoveDir = newDir;

        if (rb != null)
        {
            rb.linearVelocity = currentMoveDir * baseMoveSpeed;
        }
    }

    // =========================================================
    //  DEBUG LOG
    // =========================================================
    public void LogNav(string msg)
    {
        Debug.Log($"[DroneNav:{droneName}] {msg}");
    }

#if UNITY_EDITOR
    // =========================================================
    //  GIZMOS (opsional, hanya visual di editor)
    // =========================================================
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;

        Vector2 up = transform.up;
        Vector2 right = transform.right;

        DrawSensorGizmo(up);         // depan
        DrawSensorGizmo(right);      // kanan
        DrawSensorGizmo(-up);        // belakang
        DrawSensorGizmo(-right);     // kiri
    }

    void DrawSensorGizmo(Vector2 dir)
    {
        Vector3 origin = transform.position;
        Vector3 to = origin + (Vector3)(dir.normalized * sensorRange);
        Gizmos.DrawLine(origin, to);
    }
#endif
}