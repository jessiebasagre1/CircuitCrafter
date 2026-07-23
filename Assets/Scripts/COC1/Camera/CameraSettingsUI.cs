using UnityEngine;
using UnityEngine.UI;

// Wires a settings panel (Slider/Toggle/Button built via the Editor's GameObject > UI menu)
// to CameraZoom. See the setup notes for which UI elements to create.
public class CameraSettingsUI : MonoBehaviour
{
    [Header("Target")]
    public CameraZoom cameraZoom;

    [Header("Panel")]
    public GameObject settingsPanel;   // the panel that gets shown/hidden
    public Button openButton;          // optional - opens the panel
    public Button closeButton;         // optional - closes the panel

    [Header("Controls")]
    public Slider panSpeedSlider;      // configure as 0-1 range in the Inspector
    public Slider zoomSpeedSlider;     // configure as 0-1 range in the Inspector
    public Toggle invertToggle;        // flips both X and Y drag together

    void Start()
    {
        if (cameraZoom == null)
        {
            cameraZoom = FindAnyObjectByType<CameraZoom>();
        }
        if (cameraZoom == null)
        {
            Debug.LogWarning("CameraSettingsUI: no CameraZoom found in the scene.");
            return;
        }

        // Push the camera's current (already-loaded) settings into the UI without
        // letting that trigger the listeners below and immediately re-save them.
        if (panSpeedSlider != null)
        {
            panSpeedSlider.minValue = 0f;
            panSpeedSlider.maxValue = 1f;
            panSpeedSlider.SetValueWithoutNotify(cameraZoom.GetPanSensitivity01());
            panSpeedSlider.onValueChanged.AddListener(OnPanSpeedChanged);
        }
        if (zoomSpeedSlider != null)
        {
            zoomSpeedSlider.minValue = 0f;
            zoomSpeedSlider.maxValue = 1f;
            zoomSpeedSlider.SetValueWithoutNotify(cameraZoom.GetZoomSensitivity01());
            zoomSpeedSlider.onValueChanged.AddListener(OnZoomSpeedChanged);
        }
        if (invertToggle != null)
        {
            // invertPanX and invertPanY are kept in sync from here on;
            // use invertPanX as the source of truth for the initial state.
            invertToggle.SetIsOnWithoutNotify(cameraZoom.invertPanX);
            invertToggle.onValueChanged.AddListener(OnInvertChanged);
        }

        if (openButton != null) openButton.onClick.AddListener(OpenPanel);
        if (closeButton != null) closeButton.onClick.AddListener(ClosePanel);

        if (settingsPanel != null) settingsPanel.SetActive(false);
    }

    void OnPanSpeedChanged(float t)
    {
        cameraZoom.SetPanSensitivity01(t);
        cameraZoom.SaveSettings();
    }

    void OnZoomSpeedChanged(float t)
    {
        cameraZoom.SetZoomSensitivity01(t);
        cameraZoom.SaveSettings();
    }

    void OnInvertChanged(bool value)
    {
        cameraZoom.invertPanX = value;
        cameraZoom.invertPanY = value;
        cameraZoom.SaveSettings();
    }

    public void OpenPanel()
    {
        if (settingsPanel != null) settingsPanel.SetActive(true);
    }

    public void ClosePanel()
    {
        if (settingsPanel != null) settingsPanel.SetActive(false);
    }
}