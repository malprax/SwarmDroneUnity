using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public class Drone : MonoBehaviour
{
    [Header("Identity")]
    public string droneName = "Drone";

    [Header("Movement")]
    [Tooltip("Kecepatan linear drone (m/s).")]
    public float moveSpeed = 2f;

    [Header("Wall Centering & Random Walk")]
    [Tooltip("Jarak aman dari dinding. Kalau lebih kecil dari ini, drone akan terdorong menjauh.")]
    public float avoidDistance = 1.0f;       // coba 1.0–1.2

    [Tooltip("Gain dorongan menjauh dari dinding.")]
    public float wallRepelGain = 2.0f;

    [Tooltip("Seberapa kuat komponen random (0 = tanpa random).")]
    public float randomSteerGain = 0.25f;

    [Tooltip("Kecepatan perubahan arah maksimum (deg/s).")]
    public float maxTurnSpeedDeg = 180f;

    [Header("Sensor (Raycast)")]
    [Tooltip("Jarak maksimum sensor (m). Harus sama / mirip maxSensorRange di SimManager.")]
    public float sensorRange = 3f;

    [Tooltip("LayerMask untuk tembok / obstacle.")]
    public LayerMask obstacleMask;  // set ke layer Wall

    // --- runtime state ---
    private SimManager sim;
    private Rigidbody2D rb;

    // nilai jarak hasil sensor
    private float dFront, dRight, dBack, dLeft;

    // random walk state
    private float randomAngleVel = 0f;   // kecepatan putar random (deg/s) yang di-filter

    // home state (untuk nanti)
    private bool returningHome = false;
    public bool IsAtHome { get; private set; } = false;

    // =========================================================
    //  UNITY LIFECYCLE
    // =========================================================
    void Awake()
    {
        sim = SimManager.Instance;
        rb = GetComponent<Rigidbody2D>();
    }

    void Start()
    {
        transform.up = Vector2.up;
        StopDrone();
    }

    // =========================================================
    //  API DARI SimManager (kompatibilitas)
    // =========================================================
    public void SetWaypoints(Vector2[] newWaypoints)
    {
        // Versi random-centering: tidak memakai waypoints lagi.
    }

    public void StartReturnHome()
    {
        returningHome = true;
        IsAtHome = false;
    }

    public void StopDrone()
    {
        if (rb == null) return;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
    }

    // =========================================================
    //  SENSOR RAYCAST
    // =========================================================
    void SampleDistances()
    {
        dFront = CastDir(transform.up);
        dBack  = CastDir(-transform.up);
        dRight = CastDir(transform.right);
        dLeft  = CastDir(-transform.right);
    }

    float CastDir(Vector2 dir)
    {
        RaycastHit2D hit = Physics2D.Raycast(transform.position, dir, sensorRange, obstacleMask);
        if (hit.collider != null)
            return hit.distance;
        else
            return sensorRange;
    }

    // =========================================================
    //  PHYSICS UPDATE (SMOOTH RANDOM + WALL CENTERING)
    // =========================================================
    void FixedUpdate()
    {
        // >>> DI SINI SUDAH TIDAK ADA LAGI CEK sim.IsPlaying <<<

        // Pastikan referensi sim ada (jaga-jaga kalau scene baru)
        if (sim == null)
            sim = SimManager.Instance;

        // 1. Baca sensor
        SampleDistances();

        Vector2 forward = transform.up;
        Vector2 right   = transform.right;
        Vector2 left    = -right;

        // 2. Vektor dasar: selalu maju
        Vector2 steering = forward;

        // 3. Gaya tolak dari dinding samping (supaya cenderung di tengah koridor)
        float leftInfluence  = Mathf.Clamp01((avoidDistance - dLeft)  / avoidDistance);  // >0 kalau terlalu dekat kiri
        float rightInfluence = Mathf.Clamp01((avoidDistance - dRight) / avoidDistance); // >0 kalau terlalu dekat kanan

        // dorong menjauh: kalau dekat kiri -> dorong ke kanan (right), dst.
        steering += (right  * leftInfluence  * wallRepelGain);
        steering += (left   * rightInfluence * wallRepelGain);

        // 4. Hindari dinding depan (pilih sisi yang lebih lapang)
        string decisionLabel = "ExploreStep";
        if (dFront < avoidDistance)
        {
            float sideBias = dLeft - dRight; // >0 kiri lebih lapang
            Vector2 sideDir = (sideBias >= 0f) ? left : right;
            steering += sideDir * wallRepelGain * 2f;
            decisionLabel = "AvoidFront";
        }

        // 5. Tambahkan random kecil supaya gerakan tidak terlalu kaku
float targetRandomVel = Random.Range(-1f, 1f) * maxTurnSpeedDeg * randomSteerGain;
randomAngleVel = Mathf.Lerp(randomAngleVel, targetRandomVel, 0.05f);

// hitung arah random (smooth heading noise)
Vector3 rotated = Quaternion.Euler(0f, 0f, randomAngleVel * Time.fixedDeltaTime) * (Vector3)forward;
Vector2 randomDir = new Vector2(rotated.x, rotated.y) - forward;

steering += randomDir;

        // 6. Kalau mode pulang, beri bias ke home (optional)
        if (returningHome && sim != null)
        {
            Vector2 toHome = (sim.HomePosition - (Vector2)transform.position).normalized;
            steering = Vector2.Lerp(steering, steering + toHome, 0.3f);
            decisionLabel = "ReturnStep";
        }

        // 7. Hitung arah akhir & batasi kecepatan belok
        if (steering.sqrMagnitude < 1e-6f)
            steering = forward;

        steering.Normalize();

        float currentAngle = Mathf.Atan2(forward.y, forward.x) * Mathf.Rad2Deg;
        float targetAngle  = Mathf.Atan2(steering.y, steering.x) * Mathf.Rad2Deg;
        float maxTurn = maxTurnSpeedDeg * Time.fixedDeltaTime;
        float newAngle = Mathf.MoveTowardsAngle(currentAngle, targetAngle, maxTurn);
        Vector2 finalDir = new Vector2(Mathf.Cos(newAngle * Mathf.Deg2Rad), Mathf.Sin(newAngle * Mathf.Deg2Rad));

        // 8. Terapkan gerak
        rb.linearVelocity = finalDir.normalized * moveSpeed;
        rb.MoveRotation(newAngle - 90f); // sprite menghadap ke atas

        // 9. Logging ke grid (kalau SimManager ada)
        if (sim != null)
        {
            sim.ReportGridStep(
                this,
                rb.position,
                dFront,
                dRight,
                dBack,
                dLeft,
                decisionLabel
            );
        }

        // 10. Cek Home (kalau sedang return)
        if (returningHome && sim != null)
        {
            float distHome = Vector2.Distance(rb.position, sim.HomePosition);
            if (distHome < 0.5f)
            {
                IsAtHome = true;
                returningHome = false;
                sim.NotifyDroneReachedHome(this);
            }
        }
    }

    // =========================================================
    //  TRIGGER EVENT
    // =========================================================
    void OnTriggerEnter2D(Collider2D other)
    {
        // kosong dulu, nanti diisi untuk deteksi target.
    }
}