// File: PositionSensor.cs
using UnityEngine;

public class PositionSensor : MonoBehaviour
{
    [HideInInspector]
    public Vector2 homePosition;

    public Vector2 CurrentPosition => transform.position;

    // Dipanggil dari Drone.Awake()
    public void SetHome(Vector2 home)
    {
        homePosition = home;
    }

    public bool IsNearHome(float threshold = 0.05f)
    {
        return Vector2.Distance(CurrentPosition, homePosition) <= threshold;
    }
}