using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Powers on the target PC with a keypress OR an on-screen button.
/// Attach this to your Managers object (or the target PC's station camera)
/// and assign targetStationCamera so the key/button only work while the
/// player is actually looking at the target PC's monitor.
///
/// Wiring: 3.5 in the OS Install guide said "wire it to call
/// OSInstallManager.Instance.BeginInstall()"  this script IS that wiring.
///
/// MOBILE: assign mobilePowerButton in the Inspector to an on-screen UI
/// Button. It's auto shown/hidden each frame based on the same station-view
/// check the keyboard path uses, mirroring the pattern in PlayerInteractor.
/// Wire its OnClick to RequestPowerOn() -- or leave both unassigned entirely
/// for a PC-only build, in which case the T key behaves exactly as before.
///
/// The button hides the instant it's pressed and STAYS hidden -- it doesn't
/// just track OSInstallManager.IsRunning, because IsRunning flips back to
/// false again once the install finishes, which would make the button
/// silently reappear later even though there's nothing left to power on.
/// </summary>
public class PowerButtonInput : MonoBehaviour
{
    [Tooltip("Key that powers on the target PC.")]
    [SerializeField] private KeyCode powerKey = KeyCode.T;

    [Tooltip("The target PC's station camera GameObject (the one StationCameraSwitch enables when the player enters that view). Only listens for the key/button while this is active. Leave empty to listen everywhere.")]
    [SerializeField] private GameObject targetStationCamera;

    [Tooltip("Optional prompt text, e.g. 'Press T to power on'.")]
    [SerializeField] private TMPro.TMP_Text promptText;

    [Header("Mobile Button (optional)")]
    [Tooltip("On-screen button that powers on the PC, same as pressing the key. Wire its OnClick to RequestPowerOn(). Auto shown/hidden based on station view, and permanently hidden once used.")]
    [SerializeField] private Button mobilePowerButton;

    [Tooltip("TEMPORARY: logs which check is blocking the key each frame. Turn off once T works.")]
    [SerializeField] private bool debugLogging = false;

    private bool powerOnRequested;

    /// <summary>True forever once the PC has actually been powered on this
    /// session -- latched, not derived from IsRunning, so it doesn't flip
    /// back on when the install finishes.</summary>
    private bool hasPoweredOn;

    private void Start()
    {
        // Catch a save that was already past PowerOn when this scene loaded
        // (e.g. resumed mid-install or later) so the button doesn't show up
        // for a PC that's already been dealt with.
        if (OSInstallManager.Instance != null &&
            OSInstallManager.Instance.CurrentStage != OSInstallManager.InstallStage.PowerOn)
        {
            hasPoweredOn = true;
        }
    }

    private void Update()
    {
        bool inStationView = targetStationCamera == null || targetStationCamera.activeInHierarchy;
        bool alreadyRunning = IsAlreadyRunning();

        if (debugLogging && Input.GetKeyDown(powerKey))
            Debug.Log($"[PowerButtonInput] T pressed. inStationView={inStationView}, " +
                $"targetStationCamera={(targetStationCamera == null ? "unassigned (listens everywhere)" : targetStationCamera.name)}, " +
                $"dialogOpen={(DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen)}, " +
                $"managerInstance={(OSInstallManager.Instance != null)}");

        bool shouldShow = inStationView && !alreadyRunning && !hasPoweredOn;

        if (promptText != null)
            promptText.text = shouldShow ? $"Press {powerKey} to power on" : "";

        if (mobilePowerButton != null)
            mobilePowerButton.gameObject.SetActive(shouldShow);

        if (!inStationView) return;

        // Don't let the key/button fire while a tutorial/warning popup is open.
        if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return;

        bool pressed = Input.GetKeyDown(powerKey);

        // Mobile button press, consumed same as a keyboard press.
        if (powerOnRequested)
        {
            pressed = true;
            powerOnRequested = false;
        }

        if (pressed)
            TryPowerOn();
    }

    /// <summary>Wire this to the mobile power button's OnClick.</summary>
    public void RequestPowerOn()
    {
        powerOnRequested = true;
    }

    private void TryPowerOn()
    {
        if (OSInstallManager.Instance != null)
        {
            OSInstallManager.Instance.BeginInstall();
            hasPoweredOn = true; // latch immediately -- next Update() hides the button for good
        }
        else
        {
            Debug.LogWarning("PowerButtonInput: OSInstallManager.Instance is null. Is OSInstallManager in the scene?");
        }
    }

    private bool IsAlreadyRunning()
    {
        return OSInstallManager.Instance != null && OSInstallManager.Instance.IsRunning;
    }
}