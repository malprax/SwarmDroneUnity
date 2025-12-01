using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class Drone : MonoBehaviour
{
    // =========================================================
    //  LOCAL WALL TOPOLOGY ENUM
    // =========================================================
    enum LocalTopo
    {
        Open,
        LeftWall,
        RightWall,
        Corridor,
        NarrowCorridor,
        CornerLeft,
        CornerRight,
        DeadEnd,
        BoxTrap
    }

    // =========================================================
    //  360 CAMERA (EYES)
    // =========================================================
    [Header("360 Camera (Eyes)")]
    public Transform camera360;          // drag child "Camera360" di Inspector
    public float visionRange = 2.0f;     // radius penglihatan
    public LayerMask targetLayer;        // layer untuk SearchTarget (mis. layer "Target")

    // =========================================================
    //  ROLE & LED MARKER
    // =========================================================
    [Header("Role")]
    public bool isLeader = false;
    public string droneName = "Drone";

    [Tooltip("SpriteRenderer LED kecil penanda leader/member (child: LEDMarker).")]
    public SpriteRenderer ledMarker;

    // =========================================================
    //  VISUAL: BODY & PROPELLERS
    // =========================================================
    [Header("Body Visual (DroneBody)")]
    [Tooltip("Transform untuk body drone (child: DroneBody). Akan di-tilt & digetarkan.")]
    public Transform bodyTransform;

    [Tooltip("Maksimum sudut tilt (derajat) saat belok.")]
    public float maxTiltAngle = 15f;

    [Tooltip("Kecepatan smoothing tilt.")]
    public float tiltLerpSpeed = 7f;

    [Header("Idle Hover Vibration")]
    public float idleVibrationAmplitude = 0.03f;
    public float idleVibrationFrequency = 18f;

    Vector3 bodyBaseLocalPos;

    [Header("Propellers")]
    public Transform propeller1;
    public Transform propeller2;
    public Transform propeller3;
    public Transform propeller4;

    [Tooltip("Kecepatan putar dasar baling-baling (deg/s).")]
    public float propBaseSpeed = 360f;

    [Tooltip("Tambahan kecepatan putar per unit kecepatan drone.")]
    public float propSpeedPerUnitVelocity = 720f;

    // =========================================================
    //  MOVEMENT & MOTOR
    // =========================================================
    [Header("Movement / Motor")]
    public float baseMoveSpeed = 2f;
    public float maxMoveSpeed = 3.5f;
    public float avoidanceRadius = 0.3f;

    [Header("Anti-Stuck / Wall Avoidance")]
    public float wallCheckDistance = 0.7f;
    public float avoidanceWeight = 1f;
    public float randomJitterStrength = 0.4f;
    public float minMoveDistance = 0.25f;
    public float stuckTimeThreshold = 0.6f;
    public float pushAwayFromWall = 0.05f;
    public float slideAlongWallWeight = 0.8f;

    [Header("Motor Noise / PWM Simulation")]
    public float throttleResponse = 3f;
    public float motorNoiseAmplitude = 0.15f;

    [Header("Backoff Logic (mundur kalau sangat dekat dinding depan)")]
    public float frontVeryNearDistance = 0.25f;
    public float backOffDuration = 0.35f;
    float backOffTimer;

    // =========================================================
    //  RANGE SENSORS (RSensor1–4)
    // =========================================================
    [Header("Range Sensors")]
    public RangeSensor2D rSensorFront;
    public RangeSensor2D rSensorRight;
    public RangeSensor2D rSensorBack;
    public RangeSensor2D rSensorLeft;

    // =========================================================
    //  EXPLORATION BIAS (MENUJU TENGAH RUANGAN)
    // =========================================================
    [Header("Exploration / Center Bias")]
    [Tooltip("Titik tengah ruangan (Empty GameObject di tengah arena).")]
    public Transform explorationCenter;

    [Tooltip("Mulai tarik ke tengah jika semua sensor > jarak ini.")]
    public float centerBiasDistance = 3f;

    [Tooltip("Seberapa kuat dorongan ke tengah ruangan (bias biasa).")]
    public float centerBiasStrength = 1.2f;

    // =========================================================
    //  BATAS MENYISIRI PINGGIR RUANGAN
    // =========================================================
    [Header("Edge-follow Limit (anti keliling pinggir terus)")]
    [Tooltip("Sensor dianggap dekat dinding jika jarak < nilai ini.")]
    public float edgeNearDistance = 1.0f;

    [Tooltip("Jika tepat 1 sensor dekat dinding selama durasi ini, drone akan dipaksa ke tengah.")]
    public float edgeTimeThreshold = 2.0f;

    [Tooltip("Kekuatan dorong ke tengah saat edgeEscape aktif.")]
    public float edgeCenterPushStrength = 2.5f;

    float edgeNearTimer;
    bool edgeEscapeMode;

    // =========================================================
    //  POSITION SENSOR (UNTUK PULANG)
    // =========================================================
    [Header("Position Sensor (Home Navigation)")]
    [Tooltip("Child 'PositionSensors' yang punya komponen PositionSensor.")]
    public PositionSensor positionSensor;

    [Tooltip("Ambang jarak sensor ke home untuk dianggap sudah sampai.")]
    public float homeReachThreshold = 0.12f;

    // Optional camera-sensor script (kalau ada)
    public Camera360Sensor camera360Sensor;

    // =========================================================
    //  DEBUG LOG
    // =========================================================
    [Header("Debug Navigation Logs")]
    public bool enableDebugLogs = false;
    public float debugLogInterval = 0.5f;
    float debugLogTimer;

    // =========================================================
    //  INTERNAL DRONE STATE
    // =========================================================
    Rigidbody2D rb;
    SimManager manager;

    Vector2 homePosition;      // backup home, kalau tidak pakai PositionSensor
    Vector2 currentDir;

    bool searching;
    bool returningHome;
    bool atHome;

    float throttle;
    float stuckTimer;
    Vector2 lastPos;

    int wallLayer;
    int droneLayer;

    // Public read-only
    public bool IsSearching => searching;
    public bool IsReturningHome => returningHome;
    public bool IsAtHome => atHome;

    public Vector2 HomePosition => (positionSensor != null) ? positionSensor.homePosition : homePosition;

    // =========================================================
    //  INITIALIZATION
    // =========================================================
    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        manager = FindFirstObjectByType<SimManager>();

        homePosition = transform.position;
        lastPos = homePosition;
        atHome = true;

        wallLayer = LayerMask.NameToLayer("Wall");
        droneLayer = LayerMask.NameToLayer("Drone");

        // Auto-detect LEDMarker
        if (ledMarker == null)
        {
            Transform led = transform.Find("LEDMarker");
            if (led != null) ledMarker = led.GetComponent<SpriteRenderer>();
        }

        // Auto-detect body (DroneBody)
        if (bodyTransform == null)
        {
            Transform body = transform.Find("DroneBody");
            bodyTransform = body != null ? body : transform;
        }
        bodyBaseLocalPos = bodyTransform.localPosition;

        // Auto-detect propellers
        AutoDetectPropellers();

        // Auto-detect Position Sensor
        if (positionSensor == null)
            positionSensor = GetComponentInChildren<PositionSensor>();

        // Set home di PositionSensor (kalau ada)
        if (positionSensor != null)
            positionSensor.SetHome(transform.position);

        // AUTO-DETECT RANGE SENSORS
        AutoDetectRangeSensors();

        // Auto detect Camera360 (Transform)
        if (camera360 == null)
        {
            var cam = transform.Find("Camera360");
            if (cam != null) camera360 = cam;
        }

        // Optional: auto detect Camera360Sensor kalau ada
        if (camera360Sensor == null)
            camera360Sensor = GetComponentInChildren<Camera360Sensor>();
    }

    void AutoDetectRangeSensors()
    {
        if (rSensorFront != null && rSensorRight != null && rSensorBack != null && rSensorLeft != null)
            return;

        Transform rangeSensors = transform.Find("RangeSensors");
        if (rangeSensors == null)
        {
            Debug.LogWarning($"[Drone] RangeSensors object not found in {name}");
            return;
        }

        RangeSensor2D[] all = rangeSensors.GetComponentsInChildren<RangeSensor2D>();
        if (all.Length < 4)
        {
            Debug.LogWarning($"[Drone] RangeSensors must contain at least 4 sensors (found {all.Length})");
            return;
        }

        rSensorFront = all[0];
        rSensorRight = all[1];
        rSensorBack  = all[2];
        rSensorLeft  = all[3];

        Debug.Log($"[Drone] Auto-detected 4 RangeSensors for {name}");
    }

    void AutoDetectPropellers()
    {
        Transform props = transform.Find("Propellers");
        if (props == null) return;

        if (propeller1 == null)
        {
            Transform p = props.Find("Propeller1");
            if (p != null) propeller1 = p;
        }
        if (propeller2 == null)
        {
            Transform p = props.Find("Propeller2");
            if (p != null) propeller2 = p;
        }
        if (propeller3 == null)
        {
            Transform p = props.Find("Propeller3");
            if (p != null) propeller3 = p;
        }
        if (propeller4 == null)
        {
            Transform p = props.Find("Propeller4");
            if (p != null) propeller4 = p;
        }
    }

    // =========================================================
    //  ROLE VISUAL
    // =========================================================
    public void ApplyRoleVisual(Color leaderColor, Color memberColor)
    {
        if (ledMarker != null)
            ledMarker.color = isLeader ? leaderColor : memberColor;
    }

    // =========================================================
    //  COMMANDS CALLED BY SIMMANAGER
    // =========================================================
    public void StartSearch()
    {
        searching = true;
        returningHome = false;
        atHome = false;

        currentDir = Random.insideUnitCircle.normalized;
        stuckTimer = 0f;
        throttle = 0f;
        backOffTimer = 0f;
        edgeNearTimer = 0f;
        edgeEscapeMode = false;
    }

    public void ResetDrone()
    {
        searching = false;
        returningHome = false;
        atHome = true;

        transform.position = HomePosition;          // pakai home dari sensor
        if (positionSensor != null)
            positionSensor.SetHome(transform.position);

#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = Vector2.zero;
#else
        rb.velocity = Vector2.zero;
#endif

        currentDir = Vector2.zero;
        stuckTimer = 0f;
        throttle = 0f;
        backOffTimer = 0f;
        edgeNearTimer = 0f;
        edgeEscapeMode = false;
        lastPos = transform.position;

        ResetVisual();
    }

    public void ReturnHome()
    {
        searching = false;
        returningHome = true;
        atHome = false;
        stuckTimer = 0f;
        backOffTimer = 0f;
        edgeNearTimer = 0f;
        edgeEscapeMode = false;
    }

    // =========================================================
    //  MAIN PHYSICS LOOP
    // =========================================================
    void FixedUpdate()
    {
        // Safety: kalau manager belum ketemu (mis. scene baru di-load)
        if (manager == null)
            manager = FindFirstObjectByType<SimManager>();

        if (debugLogTimer > 0f)
            debugLogTimer -= Time.fixedDeltaTime;

        Vector2 physPos   = rb.position;                                      // posisi fisik
        Vector2 sensedPos = (positionSensor != null)
            ? (Vector2)positionSensor.transform.position   // bacaan sensor
            : physPos;

        // Tidak ada misi
        if (!searching && !returningHome)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector2.zero;
#else
            rb.velocity = Vector2.zero;
#endif
            throttle = Mathf.MoveTowards(throttle, 0f, throttleResponse * Time.fixedDeltaTime);
            UpdateVisual(Vector2.zero);
            return;
        }

        Vector2 desiredDir;

        // ===========================================
        //  RETURN HOME MODE (PAKAI POSITION SENSOR + RANGE SENSORS)
        // ===========================================
        if (returningHome)
        {
            Vector2 home = HomePosition;          // dari PositionSensor kalau ada
            Vector2 toHome = home - sensedPos;    // sensedPos = posisi sensor
            float dist = toHome.magnitude;

            // Sudah sampai Home Base (berdasar jarak sensor)
            if (dist < homeReachThreshold)
            {
                returningHome = false;
                searching = false;
                atHome = true;

#if UNITY_6000_0_OR_NEWER
                rb.linearVelocity = Vector2.zero;
#else
                rb.velocity = Vector2.zero;
#endif
                throttle = 0f;

                manager?.OnDroneReachedHome(this);
                UpdateVisual(Vector2.zero);
                LogNav($"[ReturnHome] Reached home. dist={dist:F3}");
                return;
            }

            // --- Baca sensor jarak ke dinding ---
            float dFront = GetSensorDistance(rSensorFront);
            float dRight = GetSensorDistance(rSensorRight);
            float dBack  = GetSensorDistance(rSensorBack);
            float dLeft  = GetSensorDistance(rSensorLeft);

            LocalTopo topoHome = DetectLocalTopology(dFront, dRight, dBack, dLeft);
            Vector2 topoSteer  = SteerByTopology(topoHome, toHome, dFront, dRight, dBack, dLeft);

            // Kalau depan sangat dekat dinding -> aktifkan mode backoff
            if (dFront < frontVeryNearDistance)
                backOffTimer = backOffDuration;

            Vector2 avoidance = ComputeAvoidance(physPos);
            Vector2 toHomeDir = toHome.normalized;

            if (backOffTimer > 0f)
            {
                // MODE MUNDUR SAAT PULANG
                backOffTimer -= Time.fixedDeltaTime;

                desiredDir = -toHomeDir; // mundur
                // geser sedikit ke kiri/kanan supaya bisa belok di tikungan
                if (dLeft < dRight)
                    desiredDir += (Vector2)transform.right * 0.4f;   // geser ke kanan
                else
                    desiredDir -= (Vector2)transform.right * 0.4f;   // geser ke kiri

                desiredDir = (desiredDir + avoidance * avoidanceWeight + topoSteer).normalized;
                if (desiredDir == Vector2.zero)
                    desiredDir = -toHomeDir;

                LogNav($"[ReturnHome-Backoff] topo={topoHome} dist={dist:F2} dF={dFront:F2} dR={dRight:F2} dB={dBack:F2} dL={dLeft:F2} " +
                       $"avoid={avoidance} topoSteer={topoSteer} desired={desiredDir}");
            }
            else
            {
                // MODE NORMAL PULANG
                Vector2 sensorSteer = ComputeSensorSteer(
                    dFront, dRight, dBack, dLeft,
                    toHomeDir,
                    false           // jangan pakai center bias saat pulang
                );

                desiredDir = (toHomeDir + sensorSteer + avoidance * avoidanceWeight + topoSteer).normalized;
                if (desiredDir == Vector2.zero)
                    desiredDir = toHomeDir;

                LogNav($"[ReturnHome-Normal] topo={topoHome} dist={dist:F2} dF={dFront:F2} dR={dRight:F2} dB={dBack:F2} dL={dLeft:F2} " +
                       $"steer={sensorSteer} topoSteer={topoSteer} avoid={avoidance} desired={desiredDir}");
            }

            // Anti-stuck khusus saat pulang (pakai posisi fisik)
            float movedHome = (physPos - lastPos).magnitude / Time.fixedDeltaTime;
            stuckTimer = movedHome < minMoveDistance ? stuckTimer + Time.fixedDeltaTime : 0f;
            if (stuckTimer > stuckTimeThreshold)
            {
                // kalau lama tidak maju, campur arah home dengan random supaya lepas dari geometri
                Vector2 rand = Random.insideUnitCircle.normalized;
                Vector2 toHomeDir2 = toHome.normalized;
                desiredDir = (toHomeDir2 + rand * 0.7f).normalized;
                stuckTimer = 0f;
                backOffTimer = 0f;

                LogNav($"[ReturnHome-AntiStuck] moved={movedHome:F3} -> newDesired={desiredDir}");
            }
        }
        // ===========================================
        //  SEARCH MODE
        // ===========================================
        else
        {
            if (currentDir == Vector2.zero)
                currentDir = Random.insideUnitCircle.normalized;

            // Noise motor
            currentDir = (currentDir + Random.insideUnitCircle * randomJitterStrength * Time.fixedDeltaTime).normalized;

            // Baca sensor jarak
            float dFront = GetSensorDistance(rSensorFront);
            float dRight = GetSensorDistance(rSensorRight);
            float dBack  = GetSensorDistance(rSensorBack);
            float dLeft  = GetSensorDistance(rSensorLeft);

            LocalTopo topoSearch = DetectLocalTopology(dFront, dRight, dBack, dLeft);
            Vector2 topoSteer    = SteerByTopology(topoSearch, currentDir, dFront, dRight, dBack, dLeft);

            // Kalau depan sangat dekat → backoff
            if (dFront < frontVeryNearDistance)
                backOffTimer = backOffDuration;

            Vector2 avoidance = ComputeAvoidance(physPos);

            if (backOffTimer > 0f)
            {
                // Mode mundur
                backOffTimer -= Time.fixedDeltaTime;
                desiredDir = -currentDir;

                if (dLeft < dRight)
                    desiredDir += (Vector2)transform.right * 0.4f; // geser kanan
                else
                    desiredDir -= (Vector2)transform.right * 0.4f; // geser kiri

                desiredDir = (desiredDir + avoidance * avoidanceWeight + topoSteer).normalized;
                if (desiredDir == Vector2.zero)
                    desiredDir = -currentDir;

                LogNav($"[Search-Backoff] topo={topoSearch} dF={dFront:F2} dR={dRight:F2} dB={dBack:F2} dL={dLeft:F2} " +
                       $"avoid={avoidance} topoSteer={topoSteer} desired={desiredDir}");
            }
            else
            {
                // ----- DETEKSI TERLALU LAMA DI PINGGIR -----
                int sideNearCount = 0;
                if (dFront < edgeNearDistance) sideNearCount++;
                if (dRight < edgeNearDistance) sideNearCount++;
                if (dBack  < edgeNearDistance) sideNearCount++;
                if (dLeft  < edgeNearDistance) sideNearCount++;

                bool exactlyOneSideNear = (sideNearCount == 1);

                if (exactlyOneSideNear)
                {
                    edgeNearTimer += Time.fixedDeltaTime;
                    if (edgeNearTimer > edgeTimeThreshold)
                        edgeEscapeMode = true;
                }
                else
                {
                    edgeNearTimer = 0f;
                    edgeEscapeMode = false;
                }

                // Steering sensor
                bool useCenterBias = !edgeEscapeMode;

                Vector2 sensorSteer = ComputeSensorSteer(
                    dFront, dRight, dBack, dLeft,
                    currentDir,
                    useCenterBias
                );

                // Dorong kuat ke tengah jika edgeEscape aktif
                Vector2 centerPush = Vector2.zero;
                if (edgeEscapeMode && explorationCenter != null)
                {
                    Vector2 toCenter = (Vector2)explorationCenter.position - (Vector2)transform.position;
                    if (toCenter.sqrMagnitude > 0.001f)
                        centerPush = toCenter.normalized * edgeCenterPushStrength;
                }

                desiredDir = (currentDir + sensorSteer + centerPush + avoidance * avoidanceWeight + topoSteer).normalized;
                if (desiredDir == Vector2.zero)
                    desiredDir = currentDir;

                LogNav($"[Search-Normal] topo={topoSearch} dF={dFront:F2} dR={dRight:F2} dB={dBack:F2} dL={dLeft:F2} " +
                       $"edgeEscape={edgeEscapeMode} steer={sensorSteer} topoSteer={topoSteer} centerPush={centerPush} avoid={avoidance} desired={desiredDir}");
            }

            // ANTI STUCK eksplorasi (berdasar posisi fisik)
            float moved = (physPos - lastPos).magnitude / Time.fixedDeltaTime;
            stuckTimer = moved < minMoveDistance ? stuckTimer + Time.fixedDeltaTime : 0f;

            if (stuckTimer > stuckTimeThreshold)
            {
                currentDir = Random.insideUnitCircle.normalized;
                stuckTimer = 0f;
                backOffTimer = 0f;
                edgeNearTimer = 0f;
                edgeEscapeMode = false;

                LogNav($"[Search-AntiStuck] moved={moved:F3} -> newDir={currentDir}");
            }

            // --- MATA 360: cek target di sekitar kamera ---
            ScanWithCamera360();
        }

        // Anti lengket di tembok (pakai posisi fisik)
        desiredDir = AvoidWallSlide(desiredDir, physPos);

        // ===================================================
        //  PWM MOTOR & KECEPATAN
        // ===================================================
        float congestion = ComputeAvoidanceMagnitude(physPos);
        float targetSpeed = Mathf.Lerp(maxMoveSpeed, baseMoveSpeed * 0.4f, congestion);

        float targetThrottle = Mathf.Clamp01(targetSpeed / maxMoveSpeed);
        throttle = Mathf.Lerp(throttle, targetThrottle, throttleResponse * Time.fixedDeltaTime);

        float noise = (Random.value - 0.5f) * 2f * motorNoiseAmplitude;
        float effectiveThrottle = Mathf.Clamp01(throttle + noise);

        float speed = Mathf.Lerp(0f, maxMoveSpeed, effectiveThrottle);
        Vector2 vel = desiredDir * speed;

#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = vel;
#else
        rb.velocity = vel;
#endif

        lastPos = physPos;

        // UPDATE VISUAL
        UpdateVisual(vel);
    }

    // =========================================================
    //  360 CAMERA SCAN
    // =========================================================
    void ScanWithCamera360()
    {
        if (!searching) return;
        if (camera360 == null) return;
        if (targetLayer == 0) return; // belum diset

        Collider2D hit = Physics2D.OverlapCircle(
            camera360.position,
            visionRange,
            targetLayer
        );

        if (hit == null) return;

        SearchTarget target = hit.GetComponent<SearchTarget>();
        if (target == null) return;

        // Drone menemukan target dengan kamera
        manager?.OnDroneFoundTarget(this);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (camera360 == null)
        {
            var cam = transform.Find("Camera360");
            if (cam != null) camera360 = cam;
        }

        if (camera360 != null)
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.4f);
            Gizmos.DrawWireSphere(camera360.position, visionRange);
        }
    }
#endif

    // =========================================================
    //  SENSOR UTILITIES + LOCAL TOPOLOGY
    // =========================================================
    float GetSensorDistance(RangeSensor2D sensor)
    {
        return sensor ? sensor.distance : Mathf.Infinity;
    }

    LocalTopo DetectLocalTopology(float dFront, float dRight, float dBack, float dLeft)
    {
        const float near     = 0.8f;
        const float midNear  = 0.5f;
        const float veryNear = 0.35f;

        bool fNear = dFront < near;
        bool rNear = dRight < near;
        bool lNear = dLeft  < near;
        bool bNear = dBack  < near;

        // Box / jebakan kecil
        if (fNear && rNear && lNear && bNear)
            return LocalTopo.BoxTrap;

        // Dead-end (buntu)
        if (fNear && rNear && lNear)
            return LocalTopo.DeadEnd;

        // Sudut kiri: depan & kiri dekat, kanan relatif bebas
        if (fNear && lNear && !rNear)
            return LocalTopo.CornerLeft;

        // Sudut kanan
        if (fNear && rNear && !lNear)
            return LocalTopo.CornerRight;

        // Lorong sempit: kiri & kanan dekat
        if (lNear && rNear)
        {
            if (dFront < midNear)
                return LocalTopo.NarrowCorridor;   // lorong buntu di depan
            else
                return LocalTopo.Corridor;         // lorong memanjang
        }

        // Hanya kiri dekat
        if (lNear && !rNear && !fNear)
            return LocalTopo.LeftWall;

        // Hanya kanan dekat
        if (rNear && !lNear && !fNear)
            return LocalTopo.RightWall;

        return LocalTopo.Open;
    }

    Vector2 SteerByTopology(LocalTopo topo, Vector2 baseDir,
                            float dFront, float dRight, float dBack, float dLeft)
    {
        Vector2 forward = baseDir.normalized;
        if (forward == Vector2.zero) forward = Vector2.up;

        Vector2 right = new Vector2(forward.y, -forward.x);
        Vector2 left  = -right;
        Vector2 back  = -forward;

        switch (topo)
        {
            case LocalTopo.DeadEnd:
            case LocalTopo.BoxTrap:
                // mundur kuat
                return back * 2.5f;

            case LocalTopo.CornerLeft:
                // dorong ke kanan (keluar dari sudut kiri)
                return right * 2.0f;

            case LocalTopo.CornerRight:
                // dorong ke kiri
                return left * 2.0f;

            case LocalTopo.Corridor:
                // align sepanjang lorong
                return forward * 0.8f;

            case LocalTopo.NarrowCorridor:
                // dorong sedikit mundur lalu cari celah
                return back * 1.2f;

            case LocalTopo.LeftWall:
                // geser menjauh dari kiri
                return right * 0.8f;

            case LocalTopo.RightWall:
                // geser menjauh dari kanan
                return left * 0.8f;

            case LocalTopo.Open:
            default:
                return Vector2.zero;
        }
    }

    Vector2 ComputeSensorSteer(
        float dFront, float dRight, float dBack, float dLeft,
        Vector2 baseDir,
        bool useCenterBias)
    {
        if (baseDir == Vector2.zero)
            baseDir = Vector2.up;

        Vector2 forward = baseDir.normalized;
        Vector2 right   = new Vector2(forward.y, -forward.x);
        Vector2 left    = -right;
        Vector2 back    = -forward;

        const float near     = 0.8f;
        const float veryNear = 0.35f;

        Vector2 steer = Vector2.zero;

        bool fNear = dFront < near;
        bool lNear = dLeft  < near;
        bool rNear = dRight < near;
        bool bNear = dBack  < near;

        bool fVery = dFront < veryNear;

        // Sudut / dead-end (versi ringan, topo lebih detail di fungsi lain)
        if (fNear && lNear && rNear)
        {
            steer += back * 2.5f;
        }
        else if (fNear && lNear && dRight > dLeft)
        {
            steer += right * 1.8f;
        }
        else if (fNear && rNear && dLeft > dRight)
        {
            steer += left * 1.8f;
        }
        else if (fVery)
        {
            steer += back * 1.5f;
        }

        if (lNear && !rNear)
            steer += right * 0.8f;

        if (rNear && !lNear)
            steer += left * 0.8f;

        if (bNear && !fNear)
            steer += forward * 0.5f;

        // Bias ke tengah ruangan
        if (useCenterBias && explorationCenter != null)
        {
            bool allFar =
                dFront > centerBiasDistance &&
                dRight > centerBiasDistance &&
                dBack  > centerBiasDistance &&
                dLeft  > centerBiasDistance;

            if (allFar)
            {
                Vector2 toCenter = (Vector2)explorationCenter.position - (Vector2)transform.position;
                if (toCenter.sqrMagnitude > 0.001f)
                    steer += toCenter.normalized * centerBiasStrength;
            }
        }

        return steer;
    }

    // =========================================================
    //  AVOIDANCE & WALL SLIDE
    // =========================================================
    Vector2 ComputeAvoidance(Vector2 pos)
    {
        Vector2 avoidance = Vector2.zero;

        int mask = (1 << wallLayer) | (1 << droneLayer);
        Collider2D[] hits = Physics2D.OverlapCircleAll(pos, avoidanceRadius, mask);

        foreach (Collider2D h in hits)
        {
            if (h.attachedRigidbody == rb) continue;

            Vector2 away = pos - (Vector2)h.transform.position;
            if (away.sqrMagnitude > 0.0001f)
                avoidance += away.normalized;
        }
        return avoidance;
    }

    float ComputeAvoidanceMagnitude(Vector2 pos)
    {
        int mask = (1 << wallLayer) | (1 << droneLayer);
        float sum = 0;
        foreach (var h in Physics2D.OverlapCircleAll(pos, avoidanceRadius, mask))
        {
            if (h.attachedRigidbody == rb) continue;
            sum += 1;
        }
        return Mathf.Clamp01(sum / 4f);
    }

    Vector2 AvoidWallSlide(Vector2 desiredDir, Vector2 pos)
    {
        RaycastHit2D hit = Physics2D.Raycast(pos, desiredDir, wallCheckDistance, 1 << wallLayer);
        if (!hit) return desiredDir;

        Vector2 n = hit.normal;
        Vector2 tangent = new Vector2(-n.y, n.x);

        if (Vector2.Dot(tangent, desiredDir) < 0)
            tangent = -tangent;

        return Vector2.Lerp(desiredDir, tangent, slideAlongWallWeight).normalized;
    }

    void OnCollisionEnter2D(Collision2D col)
    {
        if (col.gameObject.layer != wallLayer) return;

        Vector2 normal = col.contacts[0].normal;
        transform.position += (Vector3)(normal * pushAwayFromWall);

        Vector2 v = rb.linearVelocity;
        if (v.sqrMagnitude > 0.0001f)
        {
            Vector2 reflected = Vector2.Reflect(v.normalized, normal).normalized;
            currentDir = reflected;
            rb.linearVelocity = reflected * (baseMoveSpeed * 0.8f);
        }

        LogNav($"[Collision] normal={normal} newDir={currentDir}");
    }

    // =========================================================
    //  TARGET TRIGGER (CADANGAN)
    // =========================================================
    void OnTriggerEnter2D(Collider2D other)
    {
        if (!searching) return;

        SearchTarget t = other.GetComponent<SearchTarget>();
        if (t != null)
            manager?.OnDroneFoundTarget(this);
    }

    // =========================================================
    //  DEBUG LOG UTIL
    // =========================================================
    void LogNav(string msg)
    {
        if (!enableDebugLogs) return;
        if (debugLogTimer > 0f) return;

        debugLogTimer = debugLogInterval;
        Debug.Log($"[DroneNav:{droneName}] {msg}");
    }

    // =========================================================
    //  VISUAL (TILT + VIBRATION + PROPELLERS)
    // =========================================================
    void ResetVisual()
    {
        if (bodyTransform == null) return;
        bodyTransform.localPosition = bodyBaseLocalPos;
        bodyTransform.localRotation = Quaternion.identity;
    }

    void UpdateVisual(Vector2 velocity)
    {
        if (bodyTransform == null) return;

        float speed = velocity.magnitude;

        // Hover vibration
        float t = Time.time * idleVibrationFrequency;
        float nX = Mathf.PerlinNoise(t, 0f) - 0.5f;
        float nY = Mathf.PerlinNoise(0f, t) - 0.5f;

        float speedNorm = Mathf.Clamp01(speed / maxMoveSpeed);
        float ampScale = Mathf.Lerp(1.0f, 0.5f, speedNorm);

        Vector3 offset = new Vector3(nX, nY, 0f) * idleVibrationAmplitude * ampScale;
        bodyTransform.localPosition = bodyBaseLocalPos + offset;

        // Tilt menghadap arah gerak
        if (speed > 0.01f)
        {
            Vector2 dir = velocity.normalized;
            float heading = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;

            float tiltSide = -dir.x * maxTiltAngle;
            float tiltForwardBack = -dir.y * (maxTiltAngle * 0.5f);
            float finalTilt = tiltSide + tiltForwardBack * 0.5f;

            float finalAngle = heading + finalTilt * 0.2f;

            Quaternion targetRot = Quaternion.Euler(0f, 0f, finalAngle);
            bodyTransform.localRotation = Quaternion.Lerp(
                bodyTransform.localRotation,
                targetRot,
                tiltLerpSpeed * Time.deltaTime
            );
        }
        else
        {
            Quaternion targetRot = Quaternion.identity;
            bodyTransform.localRotation = Quaternion.Lerp(
                bodyTransform.localRotation,
                targetRot,
                tiltLerpSpeed * Time.deltaTime
            );
        }

        // Propeller spin
        float spinBase = 120f + speed * 60f;
        spinBase *= Time.deltaTime;

        if (propeller1 != null)
            propeller1.Rotate(0f, 0f, -spinBase, Space.Self);
        if (propeller3 != null)
            propeller3.Rotate(0f, 0f, -spinBase, Space.Self);
        if (propeller2 != null)
            propeller2.Rotate(0f, 0f,  spinBase, Space.Self);
        if (propeller4 != null)
            propeller4.Rotate(0f, 0f,  spinBase, Space.Self);
    }
}