using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class OpenCsvButtonBinder : MonoBehaviour
{
    [Header("Refs")]
    public SimManager sim;

    [Header("Colors")]
    public Color lockedColor = new Color(0.55f, 0.55f, 0.55f, 1f); // abu
    public Color readyColor = Color.black;                         // hitam

    // cached
    private Button button;
    private TMP_Text tmpLabel;
    private Text uiLabel;

    private void Awake()
    {
        AutoWire();
        Apply(false);
    }

    private void OnEnable()
    {
        AutoWire();

        // sync awal
        if (sim != null)
            Apply(sim.IsCsvReadyToOpen());

        // subscribe event
        if (sim != null)
        {
            if (sim.onCsvReadyChanged == null)
                sim.onCsvReadyChanged = new UnityEngine.Events.UnityEvent<bool>();

            sim.onCsvReadyChanged.RemoveListener(SetReady);
            sim.onCsvReadyChanged.AddListener(SetReady);
        }

        if (button != null)
        {
            button.onClick.RemoveListener(OnClickOpen);
            button.onClick.AddListener(OnClickOpen);
        }
    }

    private void OnDisable()
    {
        if (sim != null && sim.onCsvReadyChanged != null)
            sim.onCsvReadyChanged.RemoveListener(SetReady);

        if (button != null)
            button.onClick.RemoveListener(OnClickOpen);
    }

    private void AutoWire()
    {
        // SimManager
        if (sim == null)
            sim = FindFirstObjectByType<SimManager>();

        // Button
        if (button == null)
            button = GetComponent<Button>();

        // Label TMP (prioritas)
        if (tmpLabel == null)
            tmpLabel = GetComponentInChildren<TMP_Text>(true);

        // Label legacy
        if (uiLabel == null && tmpLabel == null)
            uiLabel = GetComponentInChildren<Text>(true);
    }

    public void SetReady(bool ready)
    {
        Apply(ready);
    }

    private void Apply(bool ready)
    {
        if (button != null)
            button.interactable = ready;

        var col = ready ? readyColor : lockedColor;

        if (tmpLabel != null) tmpLabel.color = col;
        if (uiLabel != null) uiLabel.color = col;

        // OPTIONAL: kalau kamu mau juga ubah tint Image button biar kelihatan lock
        // var img = button ? button.targetGraphic as Graphic : null;
        // if (img != null) img.color = ready ? Color.white : new Color(1f,1f,1f,0.85f);
    }

    private void OnClickOpen()
    {
        if (sim == null) return;
        if (!sim.IsCsvReadyToOpen()) return;

        sim.OpenLastCsvFile();
    }
}