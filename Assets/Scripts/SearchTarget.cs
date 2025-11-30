using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class SearchTarget : MonoBehaviour
{
    private SimManager manager;
    private Collider2D targetCollider;

    void Awake()
    {
        manager = FindFirstObjectByType<SimManager>();
        targetCollider = GetComponent<Collider2D>();

        // Pastikan collider jadi trigger supaya tidak tabrakan fisik
        if (targetCollider != null && !targetCollider.isTrigger)
        {
            targetCollider.isTrigger = true;
#if UNITY_EDITOR
            Debug.LogWarning($"[SearchTarget] Collider on {name} was not trigger. Set to isTrigger = true.");
#endif
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (manager == null) return;

        // Hanya respons ke Drone
        Drone d = other.GetComponent<Drone>();
        if (d == null) return;

        // Hanya kalau drone masih fase searching
        if (d.IsSearching)
        {
            manager.OnDroneFoundTarget(d);
        }
    }

#if UNITY_EDITOR
    // Biar kelihatan radius collider di Scene view
    void OnDrawGizmos()
    {
        var col = GetComponent<Collider2D>();
        if (col == null) return;

        Gizmos.color = Color.white;

        if (col is CircleCollider2D c)
        {
            float r = c.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y);
            Gizmos.DrawWireSphere(transform.position + (Vector3)c.offset, r);
        }
        else if (col is BoxCollider2D b)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(b.offset, b.size);
        }
    }
#endif
}