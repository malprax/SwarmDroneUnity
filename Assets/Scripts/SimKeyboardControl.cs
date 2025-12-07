using UnityEngine;

public class SimKeyboardControl : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            SimManager.Instance?.StartSimulation();
        }

        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            SimManager.Instance?.CommandAllReturnHome();
        }

        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            SimManager.Instance?.StopSimulation();
        }

        if (Input.GetKeyDown(KeyCode.Alpha4))
        {
            SimManager.Instance?.ResetAllDrones();
        }
    }
}