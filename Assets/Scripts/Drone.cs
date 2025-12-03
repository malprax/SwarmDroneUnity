using UnityEngine;


[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class Drone : MonoBehaviour
{
    public SimManager simManager;
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

    [Header("Stuck Turn Escape")]
    [Tooltip("Sudut rotasi (derajat) saat drone mencoba lepas dari stuck.")]
    public float stuckTurnAngleDeg = 30f;

    [Tooltip("Banyaknya percobaan rotasi beruntun sebelum reset.")]
    public int maxStuckTurns = 4;

    int stuckTurnCounter = 0;

    [Header("Front Obstacle Hard Avoid")]
    [Tooltip("Jika dFront < nilai ini, drone akan langsung putar arah besar.")]
    public float frontAvoidDistance = 0.6f;

    [Tooltip("Sudut belok keras (derajat) saat dinding tepat di depan.")]
    public float frontAvoidTurnAngleDeg = 70f;

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
    //  VELOCITY + DISTANCE TRACKING (STATISTIK)
    // =========================================================
    public Vector2 CurrentVelocity { get; private set; } = Vector2.zero;
    public float TotalDistance { get; private set; } = 0f;

    private Vector2 _lastPos;

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
        _lastPos = homePosition;
        atHome = true;

        wallLayer = LayerMask.NameToLayer("Wall");
        droneLayer = LayerMask.NameToLayer("Drone");

        if (simManager == null)
        simManager = FindFirstObjectByType<SimManager>();  // Unity 6
    // atau FindObjectOfType<SimManager>(); di Unity lama

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
        stuckTurnCounter = 0;

        _lastPos = transform.position;
        CurrentVelocity = Vector2.zero;
        TotalDistance = 0f;

        LogNav("[Mission] StartSearch()");
    }

    public void ResetDrone()
    {
        _lastPos = transform.position;
        CurrentVelocity = Vector2.zero;
        TotalDistance = 0f;

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
        stuckTurnCounter = 0;
        lastPos = transform.position;

        ResetVisual();
        LogNav("[Mission] ResetDrone()");
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
        stuckTurnCounter = 0;

        LogNav("[Mission] ReturnHome() called");
    }

    // =========================================================
    //  MAIN PHYSICS LOOP
    // =========================================================
    void FixedUpdate()
    {
        // early return
         if (simManager == null || !simManager.IsPlaying)
    {
        if (rb != null)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector2.zero;
#else
            rb.velocity = Vector2.zero;
#endif
        }

        // (opsional) matikan animasi propeller / lampu dsb di sini

        return; // stop di sini, jangan lanjutkan navigasi
    }

        // Safety: kalau manager belum ketemu (mis. scene baru di-load)
        if (manager == null)
            manager = FindFirstObjectByType<SimManager>();

        if (debugLogTimer > 0f)
            debugLogTimer -= Time.fixedDeltaTime;

        Vector2 physPos   = rb.position;                                      // posisi fisik
        Vector2 sensedPos = (positionSensor != null)
            ? (Vector2)positionSensor.transform.position   // bacaan sensor
            : physPos;

        // Velocity estimasi (berdasarkan transform)
        Vector2 currentPos = transform.position;
        if (Time.deltaTime > 0f)
        {
            CurrentVelocity = (currentPos - _lastPos) / Time.deltaTime;
        }
        else
        {
            CurrentVelocity = Vector2.zero;
        }
        _lastPos = currentPos;

        // Tidak ada misi
        if (!searching && !returningHome)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector2.zero;
#else
            rb.velocity = Vector2.zero;
#endif
            throttle = Mathf.MoveTowards(throttle, 0f, throttleResponse * Time.fixedDeltaTime);
            CurrentVelocity = Vector2.zero;
            UpdateVisual(Vector2.zero);
            return;
        }

        Vector2 desiredDir;
        // untuk logging grid step
        float dFront = Mathf.Infinity;
        float dRight = Mathf.Infinity;
        float dBack  = Mathf.Infinity;
        float dLeft  = Mathf.Infinity;
        Vector2 decisionBaseDir = Vector2.zero;

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
            dFront = GetSensorDistance(rSensorFront);
            dRight = GetSensorDistance(rSensorRight);
            dBack  = GetSensorDistance(rSensorBack);
            dLeft  = GetSensorDistance(rSensorLeft);

            LocalTopo topoHome = DetectLocalTopology(dFront, dRight, dBack, dLeft);
            Vector2 topoSteer  = SteerByTopology(topoHome, toHome, dFront, dRight, dBack, dLeft);

            // Hard avoid kalau dinding tepat di depan saat pulang
            Vector2 frontAvoidSteerHome = SteerAroundFrontObstacle(
                dFront, dLeft, dRight, toHome.normalized, "[ReturnHome-FrontAvoid]"
            );

            // Kalau depan sangat dekat dinding -> aktifkan mode backoff
            if (dFront < frontVeryNearDistance)
                backOffTimer = backOffDuration;

            Vector2 avoidance  = ComputeAvoidance(physPos);
            Vector2 toHomeDir  = toHome.normalized;
            decisionBaseDir    = toHomeDir; // basis keputusan relatif arah ke home

            if (backOffTimer > 0f)
            {
                // MODE MUNDUR SAAT PULANG
                backOffTimer -= Time.fixedDeltaTime;

                Vector2 baseBack = -toHomeDir;
                if (dLeft < dRight)
                    baseBack += (Vector2)transform.right * 0.4f;   // geser ke kanan
                else
                    baseBack -= (Vector2)transform.right * 0.4f;   // geser ke kiri

                Vector2 combo = baseBack.normalized * 0.7f
                                + avoidance * (avoidanceWeight * 1.3f)
                                + topoSteer * 1.5f
                                + frontAvoidSteerHome * 1.2f;

                desiredDir = combo.normalized;
                if (desiredDir == Vector2.zero)
                    desiredDir = -toHomeDir;

                LogNav($"[ReturnHome-Backoff] topo={topoHome} dist={dist:F2} dF={dFront:F2} dR={dRight:F2} dB={dBack:F2} dL={dLeft:F2}");
            }
            else
            {
                // ===========================================
                //  MODE NORMAL PULANG (PAKAI LINE-OF-SIGHT KE HOME)
                // ===========================================
                bool hasLOSHome = HasLineOfSightToHome(physPos, home);

                // Kalau tidak ada LOS (home di balik tembok), kurangi bobot toHomeDir,
                // perkuat sensor & topo supaya drone cari lorong dulu.
                float wHome       = hasLOSHome ? 0.9f : 0.2f;
                float wSensor     = hasLOSHome ? 1.0f : 1.8f;
                float wTopo       = hasLOSHome ? 1.2f : 1.8f;
                float wAvoidance  = hasLOSHome ? (avoidanceWeight * 1.3f)
                                               : (avoidanceWeight * 1.6f);
                float wFrontAvoid = hasLOSHome ? 1.5f : 1.8f;

                Vector2 sensorSteer = ComputeSensorSteer(
                    dFront, dRight, dBack, dLeft,
                    toHomeDir,
                    false           // jangan pakai center bias saat pulang
                );

                // Kombinasi dengan bobot adaptif
                Vector2 combo =
                    toHomeDir * wHome +
                    sensorSteer * wSensor +
                    avoidance * wAvoidance +
                    topoSteer * wTopo +
                    frontAvoidSteerHome * wFrontAvoid;

                desiredDir = combo.normalized;
                if (desiredDir == Vector2.zero)
                    desiredDir = toHomeDir;

                LogNav($"[ReturnHome-Normal] topo={topoHome} dist={dist:F2} dF={dFront:F2} dR={dRight:F2} dB={dBack:F2} dL={dLeft:F2} LOS={hasLOSHome}");
            }

            // Anti-stuck khusus saat pulang (pakai posisi fisik)
            float movedHome = (physPos - lastPos).magnitude / Time.fixedDeltaTime;
            stuckTimer = movedHome < minMoveDistance ? stuckTimer + Time.fixedDeltaTime : 0f;
            if (stuckTimer > stuckTimeThreshold)
            {
                // Rotasi arah pulang berdasarkan posisi tembok
                float sign = 0f;

                if (dFront < edgeNearDistance && dLeft < edgeNearDistance && dRight >= dLeft)
                    sign = +1f;
                else if (dFront < edgeNearDistance && dRight < edgeNearDistance && dLeft >= dRight)
                    sign = -1f;
                else if (dLeft < edgeNearDistance && dRight >= edgeNearDistance)
                    sign = +1f;
                else if (dRight < edgeNearDistance && dLeft >= edgeNearDistance)
                    sign = -1f;
                else
                    sign = (Random.value < 0.5f) ? +1f : -1f;

                float angle = stuckTurnAngleDeg;
                if ((stuckTurnCounter % 2) == 1)
                    angle *= 1.3f;

                float finalAngle = angle * sign;

                Vector2 toHomeDir2 = toHome.normalized;
                Vector2 rotated    = RotateVec(toHomeDir2, finalAngle);
                desiredDir         = rotated.normalized;

                stuckTurnCounter = (stuckTurnCounter + 1) % Mathf.Max(1, maxStuckTurns);
                stuckTimer = 0f;
                backOffTimer = 0f;

                LogNav($"[ReturnHome-AntiStuckTurn] moved={movedHome:F3} angle={finalAngle:F1}° dF={dFront:F2} dR={dRight:F2} dB={dBack:F2} dL={dLeft:F2} newDir={desiredDir}");
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
            dFront = GetSensorDistance(rSensorFront);
            dRight = GetSensorDistance(rSensorRight);
            dBack  = GetSensorDistance(rSensorBack);
            dLeft  = GetSensorDistance(rSensorLeft);

            LocalTopo topoSearch = DetectLocalTopology(dFront, dRight, dBack, dLeft);
            Vector2 topoSteer    = SteerByTopology(topoSearch, currentDir, dFront, dRight, dBack, dLeft);

            // Hard avoid kalau dinding tepat di depan saat eksplorasi
            Vector2 frontAvoidSteerSearch = SteerAroundFrontObstacle(
                dFront, dLeft, dRight, currentDir, "[Search-FrontAvoid]"
            );

            // Kalau depan sangat dekat → backoff
            if (dFront < frontVeryNearDistance)
                backOffTimer = backOffDuration;

            Vector2 avoidance = ComputeAvoidance(physPos);

            if (backOffTimer > 0f)
            {
                // Mode mundur
                backOffTimer -= Time.fixedDeltaTime;
                Vector2 baseBack = -currentDir;

                if (dLeft < dRight)
                    baseBack += (Vector2)transform.right * 0.4f; // geser kanan
                else
                    baseBack -= (Vector2)transform.right * 0.4f; // geser kiri

                Vector2 combo = baseBack
                                + avoidance * avoidanceWeight
                                + topoSteer
                                + frontAvoidSteerSearch * 1.2f;
                desiredDir = combo.normalized;
                if (desiredDir == Vector2.zero)
                    desiredDir = -currentDir;

                LogNav($"[Search-Backoff] topo={topoSearch} dF={dFront:F2} dR={dRight:F2} dB={dBack:F2} dL={dLeft:F2}");
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

                Vector2 combo =
                    currentDir +
                    sensorSteer +
                    centerPush +
                    avoidance * avoidanceWeight +
                    topoSteer +
                    frontAvoidSteerSearch * 1.5f;

                desiredDir = combo.normalized;
                if (desiredDir == Vector2.zero)
                    desiredDir = currentDir;
            }

            // ANTI STUCK eksplorasi (berdasar posisi fisik)
            float moved = (physPos - lastPos).magnitude / Time.fixedDeltaTime;
            stuckTimer = moved < minMoveDistance ? stuckTimer + Time.fixedDeltaTime : 0f;

            if (stuckTimer > stuckTimeThreshold)
            {
                // Pilih arah rotasi berdasarkan tembok kiri/kanan/depan
                float sign = 0f;

                if (dFront < edgeNearDistance && dLeft < edgeNearDistance && dRight >= dLeft)
                    sign = +1f;
                else if (dFront < edgeNearDistance && dRight < edgeNearDistance && dLeft >= dRight)
                    sign = -1f;
                else if (dLeft < edgeNearDistance && dRight >= edgeNearDistance)
                    sign = +1f;
                else if (dRight < edgeNearDistance && dLeft >= edgeNearDistance)
                    sign = -1f;
                else
                    sign = (Random.value < 0.5f) ? +1f : -1f;

                float angle = stuckTurnAngleDeg;
                if ((stuckTurnCounter % 2) == 1)
                    angle *= 1.3f; // rotasi ke-2,4 sedikit lebih besar

                float finalAngle = angle * sign;

                currentDir = RotateVec(currentDir, finalAngle).normalized;
                stuckTurnCounter = (stuckTurnCounter + 1) % Mathf.Max(1, maxStuckTurns);

                stuckTimer = 0f;
                backOffTimer = 0f;
                edgeNearTimer = 0f;
                edgeEscapeMode = false;

                LogNav($"[Search-AntiStuckTurn] moved={moved:F3} angle={finalAngle:F1}° dF={dFront:F2} dR={dRight:F2} dB={dBack:F2} dL={dLeft:F2} newDir={currentDir}");
            }

            // basis keputusan relatif arah gerak eksplorasi
            decisionBaseDir = currentDir;

            // --- MATA 360: cek target di sekitar kamera ---
            ScanWithCamera360();
        }

        // ===================================================
        //  MICROMOUSE-STYLE GRID LOGGING
        // ===================================================
        if (manager != null && (searching || returningHome))
        {
            bool anySensorValid =
                !float.IsInfinity(dFront) ||
                !float.IsInfinity(dRight) ||
                !float.IsInfinity(dBack)  ||
                !float.IsInfinity(dLeft);

            if (anySensorValid)
            {
                string decisionLabel = ClassifyDecision(desiredDir, decisionBaseDir);
                manager.ReportGridStep(
                    this,
                    sensedPos,
                    dFront,
                    dRight,
                    dBack,
                    dLeft,
                    decisionLabel
                );
            }
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

        // Akumulasi jarak lintasan (hanya saat misi aktif)
        if (searching || returningHome)
        {
            TotalDistance += (physPos - lastPos).magnitude;
        }

        lastPos = physPos;

        // UPDATE VISUAL
        UpdateVisual(vel);
    }

    // =========================================================
    //  360 CAMERA SCAN (with Line of Sight)
    // =========================================================
    void ScanWithCamera360()
    {
        if (!searching) return;
        if (camera360 == null) return;
        if (targetLayer == 0) return; // belum diset

        Collider2D[] hits = Physics2D.OverlapCircleAll(
            camera360.position,
            visionRange,
            targetLayer
        );

        if (hits == null || hits.Length == 0) return;

        foreach (var col in hits)
        {
            if (col == null) continue;

            SearchTarget target = col.GetComponent<SearchTarget>();
            if (target == null) continue;

            if (!HasLineOfSightToTarget(col))
            {
                LogNav($"[TargetScan] Target in range but blocked by wall: {col.name}");
                continue;
            }

            LogNav($"[TargetScan] Target visible: {col.name}");
            manager?.OnDroneFoundTarget(this);
            break;
        }
    }

    bool HasLineOfSightToTarget(Collider2D targetCol)
    {
        if (camera360 == null || targetCol == null) return false;

        Vector2 origin    = camera360.position;
        Vector2 targetPos = targetCol.bounds.center;
        Vector2 dir       = targetPos - origin;
        float   dist      = dir.magnitude;

        if (dist <= 0.0001f) return true;
        dir /= dist;

        // Raycast dengan mask: Target + Wall
        int mask = targetLayer.value | (1 << wallLayer);

        RaycastHit2D hit = Physics2D.Raycast(origin, dir, dist, mask);
        if (!hit) return false;

        // LOS valid hanya jika collider pertama yang kena adalah target
        return hit.collider == targetCol;
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

    // =========================================================
    //  LINE OF SIGHT KE HOME BASE
    // =========================================================
    bool HasLineOfSightToHome(Vector2 origin, Vector2 home)
    {
        Vector2 dir = home - origin;
        float dist = dir.magnitude;
        if (dist <= 0.0001f)
            return true;

        dir /= dist;

        int mask = 1 << wallLayer;
        RaycastHit2D hit = Physics2D.Raycast(origin, dir, dist, mask);

        // Kalau raycast TIDAK kena wall, berarti LOS ke home
        return !hit;
    }

    // =========================================================
    //  ROTATE HELPER + FRONT AVOID + DECISION CLASSIFIER
    // =========================================================
    Vector2 RotateVec(Vector2 v, float degrees)
    {
        if (v == Vector2.zero) return Vector2.zero;

        float rad = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);

        return new Vector2(
            v.x * cos - v.y * sin,
            v.x * sin + v.y * cos
        );
    }

    /// <summary>
    /// Jika dinding tepat di depan (dFront < frontAvoidDistance),
    /// paksa belok besar (±frontAvoidTurnAngleDeg) ke sisi yang lebih lapang.
    /// </summary>
    Vector2 SteerAroundFrontObstacle(
        float dFront, float dLeft, float dRight,
        Vector2 baseDir, string logTag)
    {
        if (dFront >= frontAvoidDistance || baseDir == Vector2.zero)
            return Vector2.zero;

        float sign;
        // Pilih sisi yang lebih lapang: dLeft besar artinya kiri lebih jauh dari dinding
        if (Mathf.Abs(dLeft - dRight) < 0.05f)
            sign = (Random.value < 0.5f) ? +1f : -1f;
        else if (dLeft > dRight)
            sign = +1f;  // belok ke kiri (dinding lebih jauh di kiri)
        else
            sign = -1f;  // belok ke kanan

        float angle = frontAvoidTurnAngleDeg * sign;
        Vector2 rotated = RotateVec(baseDir.normalized, angle);

        LogNav($"{logTag} dF={dFront:F2} dL={dLeft:F2} dR={dRight:F2} angle={angle:F1}°");

        return rotated.normalized;
    }

    /// <summary>
    /// Mengklasifikasikan keputusan gerak: FWD / LEFT / RIGHT / BACK / IDLE
    /// berdasarkan sudut antara arah referensi (refDir) dan arah yang dipilih (chosenDir).
    /// </summary>
    string ClassifyDecision(Vector2 chosenDir, Vector2 refDir)
    {
        if (chosenDir.sqrMagnitude < 1e-4f || refDir.sqrMagnitude < 1e-4f)
            return "IDLE";

        chosenDir.Normalize();
        refDir.Normalize();

        float angle = Vector2.SignedAngle(refDir, chosenDir); // + = kiri
        float absAngle = Mathf.Abs(angle);

        if (absAngle < 25f)          return "FWD";
        if (absAngle > 155f)         return "BACK";
        if (angle > 0f)              return "LEFT";
        else                         return "RIGHT";
    }

    void OnCollisionEnter2D(Collision2D col)
    {
        if (col.gameObject.layer != wallLayer) return;

        Vector2 normal = col.contacts[0].normal;
        transform.position += (Vector3)(normal * pushAwayFromWall);

#if UNITY_6000_0_OR_NEWER
        Vector2 v = rb.linearVelocity;
#else
        Vector2 v = rb.velocity;
#endif

        // Alih-alih memantul lurus, paksa sejajar dinding (tangent)
        Vector2 tangent = new Vector2(-normal.y, normal.x);
        if (Vector2.Dot(tangent, v) < 0)
            tangent = -tangent;

        currentDir = tangent.normalized;

#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = currentDir * (baseMoveSpeed * 0.9f);
#else
        rb.velocity = currentDir * (baseMoveSpeed * 0.9f);
#endif

        LogNav($"[Collision] normal={normal} tangent={tangent.normalized} newDir={currentDir}");
    }

    // =========================================================
    //  TARGET TRIGGER (CADANGAN)
    // =========================================================
    void OnTriggerEnter2D(Collider2D other)
    {
        if (!searching) return;

        SearchTarget t = other.GetComponent<SearchTarget>();
        if (t != null)
        {
            LogNav($"[TargetTrigger] Hit trigger of {other.name}");
            manager?.OnDroneFoundTarget(this);
        }
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