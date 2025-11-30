using UnityEngine;

public class PropellerRotator : MonoBehaviour
{
    [Tooltip("True = searah jarum jam, False = berlawanan.")]
    public bool clockwise = true;

    [Tooltip("Kecepatan dasar putar (derajat per detik).")]
    public float baseSpeed = 360f;

    [Tooltip("Tambahan kecepatan berdasarkan kecepatan drone.")]
    public float speedPerUnitVelocity = 720f;

    Rigidbody2D parentRb;

    void Awake()
    {
        parentRb = GetComponentInParent<Rigidbody2D>();
    }

    void Update()
    {
        float velMag = parentRb != null ? parentRb.linearVelocity.magnitude : 0f;

        float spinSpeed = baseSpeed + velMag * speedPerUnitVelocity;
        if (!clockwise) spinSpeed = -spinSpeed;

        transform.Rotate(0f, 0f, spinSpeed * Time.deltaTime, Space.Self);
    }
}