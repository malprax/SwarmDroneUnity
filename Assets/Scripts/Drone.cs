using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class Drone : MonoBehaviour
{
    [Header("Role")]
    public bool isLeader = false;
    public string droneName = "Drone";

    [Header("Movement")]
    public float moveSpeed = 2f;   // kecepatan gerak

    Rigidbody2D rb;
    Vector2 moveDir;              // arah utama drone

    bool searching = false;
    bool returningHome = false;
    Vector2 homePosition;

    SimManager manager;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        homePosition = transform.position;
        manager = FindObjectOfType<SimManager>();

        Debug.Log($"[Drone] Awake {name}, home = {homePosition}");
    }

    void FixedUpdate()
    {
        // Tidak melakukan apa-apa
        if (!searching && !returningHome)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        // ===== MODE PULANG KE HOME BASE =====
        if (returningHome)
        {
            Vector2 toHome = homePosition - (Vector2)transform.position;
            if (toHome.magnitude < 0.1f)
            {
                returningHome = false;
                rb.linearVelocity = Vector2.zero;
                Debug.Log($"[Drone] {droneName} reached home");
                return;
            }

            rb.linearVelocity = toHome.normalized * moveSpeed;
            return;
        }

        // ===== MODE MENCARI (SEARCHING) =====

        // kalau belum punya arah, random sekali saja
        if (moveDir == Vector2.zero)
        {
            moveDir = Random.insideUnitCircle.normalized;
            Debug.Log($"[Drone] {droneName} initial dir = {moveDir}");
        }

        rb.linearVelocity = moveDir * moveSpeed;
    }

    // Dipanggil SimManager saat simulasi dimulai
    public void StartSearch()
    {
        searching = true;
        returningHome = false;
        moveDir = Random.insideUnitCircle.normalized;
        Debug.Log($"[Drone] StartSearch() {droneName}, dir = {moveDir}");
    }

    // Dipanggil SimManager saat Reset
    public void ResetDrone()
    {
        searching = false;
        returningHome = false;
        transform.position = homePosition;
        rb.linearVelocity = Vector2.zero;
        moveDir = Vector2.zero;
        Debug.Log($"[Drone] ResetDrone() {droneName}");
    }

    // Dipanggil SimManager saat semua drone harus pulang
    public void ReturnHome()
    {
        searching = false;
        returningHome = true;
        Debug.Log($"[Drone] ReturnHome() {droneName}");
    }

    // TABRAKAN FISIK: Wall, drone lain, atau target
    void OnCollisionEnter2D(Collision2D col)
    {
        // 1) Cek apakah ini TARGET?
        SearchTarget target = col.collider.GetComponent<SearchTarget>();
        if (target != null)
        {
            Debug.Log($"[Drone] {droneName} COLLISION with TARGET {target.name}");

            if (searching && manager != null)
            {
                manager.ObjectFound(this);
            }

            searching = false;
            rb.linearVelocity = Vector2.zero;
            return;
        }

        // 2) Selain target → anggap dinding / drone lain → mantul
        if (col.contacts.Length > 0)
        {
            Vector2 normal = col.contacts[0].normal;

            if (moveDir == Vector2.zero)
                moveDir = rb.linearVelocity.normalized;

            moveDir = Vector2.Reflect(moveDir, normal).normalized;

            Debug.Log($"[Drone] {droneName} bounce from {col.collider.name}, new dir = {moveDir}");
        }
    }
}