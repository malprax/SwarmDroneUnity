using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class SearchTarget : MonoBehaviour
{
    private Collider2D targetCollider;

    [Header("Settings")]
    [Tooltip("Jika aktif, collider otomatis di-set sebagai trigger saat Awake/Reset.")]
    public bool autoSetTrigger = true;

    void Awake()
    {
        EnsureTriggerCollider();
    }

    // Kalau komponen baru di-add di Inspector, Unity akan panggil Reset()
    void Reset()
    {
        EnsureTriggerCollider();
    }

    /// <summary>
    /// Pastikan collider adalah trigger sehingga drone bisa melewati
    /// tanpa tabrakan fisik, hanya memicu event trigger.
    /// </summary>
    void EnsureTriggerCollider()
    {
        if (targetCollider == null)
            targetCollider = GetComponent<Collider2D>();

        if (targetCollider == null) return;

        if (autoSetTrigger && !targetCollider.isTrigger)
        {
            targetCollider.isTrigger = true;
#if UNITY_EDITOR
            Debug.LogWarning($"[SearchTarget] Collider on {name} was not trigger. Set to isTrigger = true.");
#endif
        }
    }

    // ---------------------------------------------------------
    //  GIZMOS: Biar kelihatan radius/shape collider di Scene View
    // ---------------------------------------------------------
#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        var col = GetComponent<Collider2D>();
        if (col == null) return;

        Gizmos.color = Color.white;

        if (col is CircleCollider2D c)
        {
            float r = c.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y);
            Vector3 center = transform.position + (Vector3)c.offset;
            Gizmos.DrawWireSphere(center, r);
        }
        else if (col is BoxCollider2D b)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(b.offset, b.size);
        }
        else if (col is PolygonCollider2D p)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            var points = p.points;
            if (points.Length > 1)
            {
                for (int i = 0; i < points.Length; i++)
                {
                    Vector3 a = points[i];
                    Vector3 bPt = points[(i + 1) % points.Length];
                    Gizmos.DrawLine(a, bPt);
                }
            }
        }
    }
#endif
}