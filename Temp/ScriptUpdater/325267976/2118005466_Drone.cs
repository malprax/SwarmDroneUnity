using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class Drone : MonoBehaviour
{
    [Header("Role")]
    public bool isLeader = false;
    public string droneName = "Drone";

    [Header("Movement")]
    public float moveSpeed = 2f;
    public float directionChangeInterval = 1.5f;
    public float avoidanceRadius = 0.3f;

    Rigidbody2D rb;
    Vector2 currentDir;
    float dirTimer;

    bool searching = false;
    bool returningHome = false;
    Vector2 homePosition;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        homePosition = transform.position;   // posisi awal = home base
    }

    void Update()
    {
        // Kalau tidak sedang mencari dan tidak pulang → diam
        if (!searching && !returningHome)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        if (returningHome)
        {
            MoveToHome();
        }
        else if (searching)
        {
            Wander();
        }
    }

    // ---- PUBLIC API yang dipakai SimManager ----

    public void StartSearch()
    {
        searching = true;
        returningHome = false;
        dirTimer = 0f;
    }

    public void ResetDrone()
    {
        searching = false;
        returningHome = false;
        rb.linearVelocity = Vector2.zero;
        transform.position = homePosition;
    }

    public void ReturnHome()
    {
        searching = false;
        returningHome = true;
    }

    // ---- Behaviour internal ----

    void Wander()
    {
        // ganti arah tiap beberapa detik
        dirTimer -= Time.deltaTime;
        if (dirTimer <= 0f || currentDir == Vector2.zero)
        {
            currentDir = Random.insideUnitCircle.normalized;
            dirTimer = directionChangeInterval;
        }

        Vector2 dir = ApplySimpleAvoidance(currentDir);
        rb.linearVelocity = dir * moveSpeed;
    }

    void MoveToHome()
    {
        Vector2 toHome = homePosition - (Vector2)transform.position;

        // sudah sampai home
        if (toHome.magnitude < 0.05f)
        {
            returningHome = false;
            rb.linearVelocity = Vector2.zero;
            return;
        }

        Vector2 dir = toHome.normalized;
        dir = ApplySimpleAvoidance(dir);
        rb.linearVelocity = dir * moveSpeed;
    }

    // Hindari objek lain secara sederhana (kalau nanti ada collider dinding/drone)
    Vector2 ApplySimpleAvoidance(Vector2 dir)
    {
        // cast pendek ke depan, kalau nabrak → belok sedikit
        RaycastHit2D hit = Physics2D.CircleCast(
            transform.position,
            avoidanceRadius,
            dir,
            0.2f
        );

        if (hit.collider != null)
        {
            Vector2 away = (Vector2)transform.position - hit.point;
            dir = (dir + away.normalized).normalized;
        }

        return dir;
    }

    public override string ToString()
    {
        return droneName + (isLeader ? " (Leader)" : " (Member)");
    }
}