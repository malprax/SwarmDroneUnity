using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class Drone : MonoBehaviour
{
    // =========================================================
    //  360 CAMERA (EYES)
    // =========================================================
    [Header("360 Camera (Eyes)")]
    public Transform camera360;      // drag child "Camera360" di Inspector
    public float visionRange = 2.0f; // radius penglihatan
    public LayerMask targetLayer;    // layer untuk SearchTarget (mis. layer "Target")

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

    // Optional sensors
    public Camera360Sensor camera360Sensor; // tidak dipakai langsung, disiapkan kalau mau pakai script terpisah
    public PositionSensor positionSensor;

    // =========================================================
    //  INTERNAL DRONE STATE
    // =========================================================
    Rigidbody2D rb;
    SimManager manager;

    Vector2 homePosition;
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

    public Vector2 HomePosition => homePosition;

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
        if (positionSensor != null)
            positionSensor.SetHome(homePosition);

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

        transform.position = homePosition;

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
        lastPos = homePosition;

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

        Vector2 pos = rb.position;

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
        //  RETURN HOME MODE
        // ===========================================
        if (returningHome)
        {
            Vector2 home = (positionSensor != null) ? positionSensor.homePosition : homePosition;
            Vector2 toHome = home - pos;
            float dist = toHome.magnitude;

            // Sudah sampai Home Base (sedikit dilonggarkan)
            if (dist < 0.12f)
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
                return;
            }

            // Baca sensor
            float dFront = GetSensorDistance(rSensorFront);
            float dRight = GetSensorDistance(rSensorRight);
            float dBack  = GetSensorDistance(rSensorBack);
            float dLeft  = GetSensorDistance(rSensorLeft);

            // Catatan: untuk mode pulang kita TIDAK pakai backOff agresif,
            // supaya drone tidak maju-mundur di koridor sempit.
            Vector2 avoidance = ComputeAvoidance(pos);

            Vector2 sensorSteer = ComputeSensorSteer(
                dFront, dRight, dBack, dLeft,
                toHome,
                false  // tidak perlu centerBias ketika pulang
            );

            desiredDir = (toHome.normalized + sensorSteer + avoidance * avoidanceWeight).normalized;
            if (desiredDir == Vector2.zero)
                desiredDir = toHome.normalized;

            // Anti-stuck khusus saat pulang
            float movedHome = (pos - lastPos).magnitude / Time.fixedDeltaTime;
            stuckTimer = movedHome < minMoveDistance ? stuckTimer + Time.fixedDeltaTime : 0f;
            if (stuckTimer > stuckTimeThreshold)
            {
                Vector2 rand = Random.insideUnitCircle.normalized;
                currentDir = (toHome.normalized + rand * 0.7f).normalized;
                stuckTimer = 0f;
                backOffTimer = 0f;
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

            // Baca sensor
            float dFront = GetSensorDistance(rSensorFront);
            float dRight = GetSensorDistance(rSensorRight);
            float dBack  = GetSensorDistance(rSensorBack);
            float dLeft  = GetSensorDistance(rSensorLeft);

            // Kalau depan sangat dekat → backoff
            if (dFront < frontVeryNearDistance)
                backOffTimer = backOffDuration;

            Vector2 avoidance = ComputeAvoidance(pos);

            if (backOffTimer > 0f)
            {
                // Mode mundur
                backOffTimer -= Time.fixedDeltaTime;
                desiredDir = -currentDir;

                if (dLeft < dRight)
                    desiredDir += (Vector2)transform.right * 0.4f; // geser kanan
                else
                    desiredDir -= (Vector2)transform.right * 0.4f; // geser kiri

                desiredDir = (desiredDir + avoidance * avoidanceWeight).normalized;
                if (desiredDir == Vector2.zero)
                    desiredDir = -currentDir;
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

                desiredDir = (currentDir + sensorSteer + centerPush + avoidance * avoidanceWeight).normalized;
                if (desiredDir == Vector2.zero)
                    desiredDir = currentDir;
            }

            // ANTI STUCK eksplorasi
            float moved = (pos - lastPos).magnitude / Time.fixedDeltaTime;
            stuckTimer = moved < minMoveDistance ? stuckTimer + Time.fixedDeltaTime : 0f;

            if (stuckTimer > stuckTimeThreshold)
            {
                currentDir = Random.insideUnitCircle.normalized;
                stuckTimer = 0f;
                backOffTimer = 0f;
                edgeNearTimer = 0f;
                edgeEscapeMode = false;
            }

            // --- MATA 360: cek target di sekitar kamera ---
            ScanWithCamera360();
        }

        // Anti lengket di tembok
        desiredDir = AvoidWallSlide(desiredDir, pos);

        // ===================================================
        //  PWM MOTOR & KECEPATAN
        // ===================================================
        float congestion = ComputeAvoidanceMagnitude(rb.position);
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

        lastPos = rb.position;

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
    //  SENSOR UTILITIES
    // =========================================================
    float GetSensorDistance(RangeSensor2D sensor)
    {
        return sensor ? sensor.distance : Mathf.Infinity;
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
        bool lVery = dLeft  < veryNear;
        bool rVery = dRight < veryNear;

        // Sudut / dead-end
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

#if UNITY_6000_0_OR_NEWER
        Vector2 v = rb.linearVelocity;
#else
        Vector2 v = rb.velocity;
#endif

        if (v.sqrMagnitude > 0.0001f)
        {
            Vector2 reflected = Vector2.Reflect(v.normalized, normal).normalized;
            currentDir = reflected;

#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = reflected * (baseMoveSpeed * 0.8f);
#else
            rb.velocity = reflected * (baseMoveSpeed * 0.8f);
#endif
        }
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