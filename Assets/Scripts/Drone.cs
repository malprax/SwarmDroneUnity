// using UnityEngine;

// [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
// public class Drone : MonoBehaviour
// {
//     [Header("Role")]
//     public bool isLeader = false;
//     public string droneName = "Drone";

//     [Header("Movement")]
//     public float moveSpeed = 2f;
//     public float avoidanceRadius = 0.3f;      // radius untuk menghindar dari wall + drone lain

//     [Header("Target Detection")]
//     public float targetDetectRadius = 0.35f;  // jarak "sapu" untuk mendeteksi Target

//     Rigidbody2D rb;
//     Vector2 currentDir;        // arah utama drone (diubah hanya saat start & tabrakan)

//     bool searching = false;
//     bool returningHome = false;
//     Vector2 homePosition;

//     SimManager manager;

//     int wallLayer;
//     int droneLayer;

//     void Awake()
//     {
//         rb = GetComponent<Rigidbody2D>();
//         homePosition = transform.position;
//         manager = FindObjectOfType<SimManager>();

//         wallLayer  = LayerMask.NameToLayer("Wall");
//         droneLayer = LayerMask.NameToLayer("Drone");

//         Debug.Log($"[Drone] Awake {name}, home={homePosition}");
//     }

//     void FixedUpdate()
//     {
//         // Tidak sedang mencari / pulang → diam
//         if (!searching && !returningHome)
//         {
//             rb.linearVelocity = Vector2.zero;
//             return;
//         }

//         // ------------------- MODE PULANG -------------------
//         if (returningHome)
//         {
//             Vector2 toHome = homePosition - (Vector2)transform.position;
//             if (toHome.magnitude < 0.1f)
//             {
//                 returningHome = false;
//                 rb.linearVelocity = Vector2.zero;
//                 Debug.Log($"[Drone] {droneName} reached home");
//                 return;
//             }

//             rb.linearVelocity = toHome.normalized * moveSpeed;
//             return;
//         }

//         // ------------------- MODE MENCARI -------------------

//         // Jika belum punya arah, random sekali saja
//         if (currentDir == Vector2.zero)
//         {
//             currentDir = Random.insideUnitCircle.normalized;
//             Debug.Log($"[Drone] {droneName} initial dir {currentDir}");
//         }

//         // Hindari wall + drone lain (belok sedikit, tapi tidak ganti arah utama)
//         Vector2 avoidance = Vector2.zero;
//         int avoidMask = (1 << wallLayer) | (1 << droneLayer);

//         Collider2D[] avoidHits = Physics2D.OverlapCircleAll(transform.position,
//                                                             avoidanceRadius,
//                                                             avoidMask);
//         foreach (Collider2D h in avoidHits)
//         {
//             if (h == null) continue;
//             if (h.attachedRigidbody != null && h.attachedRigidbody != rb)
//             {
//                 Vector2 away = (Vector2)transform.position - (Vector2)h.transform.position;
//                 if (away.sqrMagnitude > 0.0001f)
//                     avoidance += away.normalized;
//             }
//         }

//         Vector2 moveDir = (currentDir + avoidance).normalized;
//         if (moveDir.sqrMagnitude < 0.0001f)
//             moveDir = currentDir;   // jaga-jaga biar tidak berhenti

//         rb.linearVelocity = moveDir * moveSpeed;

//         // ------------------- DETEKSI TARGET (backup) -------------------
//         if (searching)
//         {
//             // Visual bantu di Scene
//             Debug.DrawLine(transform.position,
//                            (Vector2)transform.position + Vector2.right * targetDetectRadius,
//                            Color.red, 0.1f);

//             // Cari semua collider di radius target
//             Collider2D[] targetHits = Physics2D.OverlapCircleAll(transform.position,
//                                                                  targetDetectRadius);
//             foreach (Collider2D c in targetHits)
//             {
//                 if (c == null) continue;

//                 // Pakai Tag "Target" supaya tidak pusing layer
//                 if (c.CompareTag("Target"))
//                 {
//                     Debug.Log($"[Drone] {droneName} FOUND TARGET via OverlapCircle: {c.name}");
//                     if (manager != null)
//                         manager.ObjectFound(this);
//                     return;
//                 }
//             }
//         }
//     }

//     // ------------------- PUBLIC API -------------------

//     public void StartSearch()
//     {
//         searching = true;
//         returningHome = false;

//         if (currentDir == Vector2.zero)
//             currentDir = Random.insideUnitCircle.normalized;

//         Debug.Log($"[Drone] StartSearch() {droneName}, dir={currentDir}");
//     }

//     public void ResetDrone()
//     {
//         searching = false;
//         returningHome = false;
//         transform.position = homePosition;
//         rb.linearVelocity = Vector2.zero;
//         currentDir = Vector2.zero;   // supaya StartSearch berikutnya random lagi
//         Debug.Log($"[Drone] ResetDrone() {droneName}");
//     }

//     public void ReturnHome()
//     {
//         searching = false;
//         returningHome = true;
//         Debug.Log($"[Drone] ReturnHome() {droneName}");
//     }

//     // ------------------- COLLISION / TRIGGER -------------------

//     // Dipanggil kalau collider Target bertipe Trigger dan disentuh
//     void OnTriggerEnter2D(Collider2D other)
//     {
//         if (!searching) return;

//         if (other != null && other.CompareTag("Target"))
//         {
//             Debug.Log($"[Drone] {droneName} OnTriggerEnter2D → FOUND TARGET: {other.name}");
//             if (manager != null)
//                 manager.ObjectFound(this);
//         }
//     }

//     // Pantul kalau kena wall atau drone lain
//     void OnCollisionEnter2D(Collision2D col)
//     {
//         int layer = col.gameObject.layer;

//         if (layer == wallLayer || layer == droneLayer)
//         {
//             Vector2 normal = col.contacts[0].normal;
//             currentDir = Vector2.Reflect(currentDir, normal).normalized;
//             Debug.Log($"[Drone] {droneName} bounce from {col.gameObject.name}, new dir={currentDir}");
//         }
//     }

//     // ------------------- GIZMOS (visual bantu di Scene) -------------------
//     void OnDrawGizmosSelected()
//     {
//         Gizmos.color = Color.cyan;
//         Gizmos.DrawWireSphere(transform.position, avoidanceRadius);

//         Gizmos.color = Color.yellow;
//         Gizmos.DrawWireSphere(transform.position, targetDetectRadius);
//     }
// }

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