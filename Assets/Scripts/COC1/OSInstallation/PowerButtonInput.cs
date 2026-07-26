using UnityEngine;

/// <summary>
/// Powers on the target PC with a keypress instead of a clickable UI button.
/// Attach this to your Managers object (or the target PC's station camera)
/// and assign targetStationCamera so the key only works while the player is
/// actually looking at the target PC's monitor.
///
/// Wiring: 3.5 in the OS Install guide said "wire it to call
/// OSInstallManager.Instance.BeginInstall()" — this script IS that wiring.
/// </summary>
public class PowerButtonInput : MonoBehaviour
{
    [Tooltip("Key that powers on the target PC.")]
    [SerializeField] private KeyCode powerKey = KeyCode.T;

    [Tooltip("The target PC's station camera GameObject (the one StationCameraSwitch enables when the player enters that view). Only listens for the key while this is active. Leave empty to listen everywhere.")]
    [SerializeField] private GameObject targetStationCamera;

    [Tooltip("Optional prompt text, e.g. 'Press T to power on'.")]
    [SerializeField] private TMPro.TMP_Text promptText;

    [Tooltip("TEMPORARY: logs which check is blocking the key each frame. Turn off once T works.")]
    [SerializeField] private bool debugLogging = false;

    private void Update()
    {
        bool inStationView = targetStationCamera == null || targetStationCamera.activeInHierarchy;

        if (debugLogging && Input.GetKeyDown(powerKey))
            Debug.Log($"[PowerButtonInput] T pressed. inStationView={inStationView}, " +
                $"targetStationCamera={(targetStationCamera == null ? "unassigned (listens everywhere)" : targetStationCamera.name)}, " +
                $"dialogOpen={(DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen)}, " +
                $"managerInstance={(OSInstallManager.Instance != null)}");

        if (promptText != null)
            promptText.text = inStationView && !IsAlreadyRunning() ? $"Press {powerKey} to power on" : "";

        if (!inStationView) return;

        // Don't let the key fire while a tutorial/warning popup is open.
        if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return;

        if (Input.GetKeyDown(powerKey))
        {
            if (OSInstallManager.Instance != null)
                OSInstallManager.Instance.BeginInstall();
            else
                Debug.LogWarning("PowerButtonInput: OSInstallManager.Instance is null. Is OSInstallManager in the scene?");
        }
    }

    private bool IsAlreadyRunning()
    {
        return OSInstallManager.Instance != null && OSInstallManager.Instance.IsRunning;
    }
}