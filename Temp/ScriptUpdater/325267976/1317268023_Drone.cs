using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class Drone : MonoBehaviour
{
    public float speed = 3f;
    public bool isLeader = false;
    public string droneName = "Drone";
    public Vector2 homePos;

    Rigidbody2D rb;
    Vector2 moveDir;
    bool isActive = false;
    bool isReturning = false;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
    }

    void Start()
    {
        homePos = transform.position;
        PickRandomDirection();
    }

    void Update()
    {
        if (!isActive) return;

        if (!isReturning)
        {
            // Occasionally pick new random direction
            if (Random.Range(0, 60) == 0)
                PickRandomDirection();
        }
        else
        {
            moveDir = (homePos - (Vector2)transform.position).normalized;
            if (Vector2.Distance(transform.position, homePos) < 0.3f)
            {
                isActive = false;
                rb.linearVelocity = Vector2.zero;
            }
        }
    }

    void FixedUpdate()
    {
        if (isActive)
            rb.linearVelocity = moveDir * speed;
    }

    void PickRandomDirection()
    {
        moveDir = Random.insideUnitCircle.normalized;
        if (moveDir == Vector2.zero)
            moveDir = Vector2.right;
    }

    public void StartSearch()
    {
        isActive = true;
        isReturning = false;
        PickRandomDirection();
    }

    public void ReturnHome()
    {
        isActive = true;
        isReturning = true;
    }

    public void ResetDrone()
    {
        transform.position = homePos;
        rb.linearVelocity = Vector2.zero;
        isActive = false;
        isReturning = false;
    }
}
