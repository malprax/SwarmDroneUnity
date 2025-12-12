using UnityEngine;

[RequireComponent(typeof(CircleCollider2D))]
public class Drone : MonoBehaviour
{
    [Header("Identity")]
    public string droneName = "Drone1";

    [Header("Movement")]
    public float moveSpeed = 2.0f;
    public float turnSpeedDeg = 220f;

    [Header("Wall Avoidance")]
    public LayerMask wallLayerMask;   // SET: hanya Wall
    public float sensorRange = 1.4f;
    public float wallHardDistance = 0.35f;
    public float wallSoftDistance = 0.85f;
    public float skin = 0.02f;

    [Header("Exploration")]
    [Range(0f, 1f)] public float randomSteerStrength = 0.25f;

    [Header("Anti-Stuck")]
    public float stuckTimeThreshold = 1.2f;
    public float minMoveDelta = 0.003f;

    [Header("Debug")]
    public bool verbose = false;

    private Vector2 desiredDirection;
    private Vector2 lastPos;
    private float stuckTimer;

    private CircleCollider2D circle;
    private float bodyRadiusWorld;

    private void Awake()
    {
        circle = GetComponent<CircleCollider2D>();

        // radius WORLD = radius lokal * scale (ambil scale terbesar)
        float s = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y);
        bodyRadiusWorld = circle.radius * s;

        desiredDirection = transform.right;
        lastPos = transform.position;

        Debug.Log($"[Drone:{droneName}] Awake() bodyRadiusWorld={bodyRadiusWorld:F3}");
    }

    private void Start()
    {
        Debug.Log($"[Drone:{droneName}] Start()");
    }

    private void Update()
    {
        HandleStuck();
        ComputeDesiredDirection();
        ApplyMoveWithCollision();
    }

    private void ComputeDesiredDirection()
    {
        Vector2 origin = transform.position;
        Vector2 forward = transform.right;
        Vector2 left = transform.up;
        Vector2 right = -transform.up;

        float f = CastDistance(origin, forward, sensorRange);
        float l = CastDistance(origin, left, sensorRange * 0.75f);
        float r = CastDistance(origin, right, sensorRange * 0.75f);

        Vector2 avoid = Vector2.zero;

        if (f < wallHardDistance)
        {
            avoid = (l > r) ? (Vector2)transform.up : -(Vector2)transform.up;
            avoid += -(Vector2)transform.right * 0.8f;
        }
        else if (f < wallSoftDistance)
        {
            avoid = (l > r) ? (Vector2)transform.up : -(Vector2)transform.up;
            avoid *= 0.6f;
        }

        Vector2 randomSteer = Random.insideUnitCircle * randomSteerStrength;

        Vector2 finalDir = (Vector2)transform.right + avoid + randomSteer;
        if (finalDir.sqrMagnitude < 0.0001f) finalDir = transform.right;

        desiredDirection = finalDir.normalized;

        if (verbose)
            Debug.Log($"[Drone:{droneName}] f={f:F2} l={l:F2} r={r:F2} dir={desiredDirection}");
    }

    private void ApplyMoveWithCollision()
    {
        if (desiredDirection.sqrMagnitude < 0.0001f) return;

        // ROTATE
        float angle = Vector2.SignedAngle(transform.right, desiredDirection);
        float maxStep = turnSpeedDeg * Time.deltaTime;
        float step = Mathf.Clamp(angle, -maxStep, maxStep);
        transform.Rotate(0f, 0f, step);

        // MOVE
        Vector2 moveDir = transform.right;
        float dist = moveSpeed * Time.deltaTime;

        Vector2 origin = transform.position;
        float radius = Mathf.Max(0.04f, bodyRadiusWorld);

        // cek maju
        RaycastHit2D hit = Physics2D.CircleCast(origin, radius, moveDir, dist + skin, wallLayerMask);

        if (!hit.collider)
        {
            transform.position = origin + moveDir * dist;
            return;
        }

        // SLIDE di sepanjang tembok
        Vector2 n = hit.normal;
        Vector2 tangent = new Vector2(-n.y, n.x);
        float sign = Mathf.Sign(Vector2.Dot(tangent, moveDir));
        tangent *= sign;

        RaycastHit2D hit2 = Physics2D.CircleCast(origin, radius, tangent, dist + skin, wallLayerMask);

        if (!hit2.collider)
        {
            transform.position = origin + tangent.normalized * dist;
        }
        else
        {
            ForceEscape("corner-hit");
        }
    }

    private float CastDistance(Vector2 origin, Vector2 dir, float range)
    {
        float probeRadius = Mathf.Max(0.02f, bodyRadiusWorld * 0.2f);
        RaycastHit2D hit = Physics2D.CircleCast(origin, probeRadius, dir.normalized, range, wallLayerMask);
        return hit.collider ? hit.distance : range;
    }

    private void HandleStuck()
    {
        float moved = Vector2.Distance(transform.position, lastPos);

        if (moved < minMoveDelta)
        {
            stuckTimer += Time.deltaTime;
            if (stuckTimer >= stuckTimeThreshold)
            {
                ForceEscape("stuck");
                stuckTimer = 0f;
            }
        }
        else
        {
            stuckTimer = 0f;
        }

        lastPos = transform.position;
    }

    private void ForceEscape(string reason)
    {
        float angle = Random.Range(110f, 180f);
        if (Random.value > 0.5f) angle = -angle;
        transform.Rotate(0f, 0f, angle);

        // dorong kecil biar lepas dari nempel collider
        transform.position += transform.right * 0.06f;

        if (verbose) Debug.LogWarning($"[Drone:{droneName}] ESCAPE ({reason})");
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Vector3 p = transform.position;
        Gizmos.DrawLine(p, p + (Vector3)desiredDirection * 0.8f);

        Gizmos.color = Color.red;
        Gizmos.DrawLine(p, p + transform.right * sensorRange);
    }
#endif
}