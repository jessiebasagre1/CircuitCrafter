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
/// The button hides once the install has actually STARTED and stays hidden --
/// it doesn't simply track OSInstallManager.IsRunning, because IsRunning flips
/// back to false again when the install finishes, which would make the button
/// silently reappear later even though there's nothing left to power on. The
/// latch is set from IsRunning at the moment of the press, which is the only
/// instant the two questions have the same answer.
///
/// GATING: assign gate (e.g. requiredTaskIds = ["create_bootable_usb"]) to
/// keep the key/button locked until a prior step is done. Leave gate at its
/// default, empty state to keep this always available, same as before.
///
/// A LOCKED BUTTON IS SHOWN, NOT HIDDEN. It is tinted with lockedTint and
/// pressing it calls gate.ShowLockedMessage(), so the learner is told which
/// step is missing. Hiding it instead - which this used to do - left a phone
/// player looking at an empty screen with nothing to press and no explanation.
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

    [Tooltip("Tint for the power button while the prerequisite is still missing. It stays " +
             "clickable - pressing it is what shows the reason.")]
    [SerializeField] private Color lockedTint = new Color(0.62f, 0.66f, 0.72f, 1f);

    [Tooltip("Tint for the power button once it will actually work.")]
    [SerializeField] private Color unlockedTint = Color.white;

    [Header("Locking (optional)")]
    [Tooltip("Leave every sub-field empty/unchecked to keep the key/button always available (once in station view).")]
    [SerializeField] private TaskGate gate = new TaskGate();

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
        bool unlocked = gate.IsUnlocked();

        if (debugLogging && Input.GetKeyDown(powerKey))
            Debug.Log($"[PowerButtonInput] T pressed. inStationView={inStationView}, unlocked={unlocked}, " +
                $"targetStationCamera={(targetStationCamera == null ? "unassigned (listens everywhere)" : targetStationCamera.name)}, " +
                $"dialogOpen={(DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen)}, " +
                $"managerInstance={(OSInstallManager.Instance != null)}");

        // LOCKED IS NOT THE SAME AS HIDDEN, AND THIS LINE USED TO TREAT THEM AS ONE.
        // 'unlocked' was in this condition, so with no bootable USB plugged in the button did not
        // appear at all. On a keyboard that was survivable - press T and ShowLockedMessage explains
        // why - but on a phone there is no key to press, so the one control that would have told
        // the learner what to do first was the control that vanished. A button that disappears
        // teaches nothing; a button that answers "create a bootable USB drive first" teaches the
        // prerequisite, which is the point of gating it in the first place.
        //
        // So visibility no longer asks about the gate. The gate is still enforced, three lines
        // further down, where pressing it calls gate.ShowLockedMessage() instead of powering on.
        bool shouldShow = inStationView && !alreadyRunning && !hasPoweredOn;

        if (promptText != null)
        {
            if (!inStationView || alreadyRunning || hasPoweredOn) promptText.text = "";
            else if (!unlocked) promptText.text = "Create a bootable USB drive first";
            else promptText.text = $"Press {powerKey} to power on";
        }

        if (mobilePowerButton != null)
        {
            mobilePowerButton.gameObject.SetActive(shouldShow);

            // Still clickable while locked - that click is what produces the explanation. Only the
            // look changes, so the button reads as unavailable before it is pressed rather than
            // only after.
            var colors = mobilePowerButton.colors;
            colors.normalColor = unlocked ? unlockedTint : lockedTint;
            mobilePowerButton.colors = colors;
        }

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

        if (!pressed) return;

        if (!unlocked)
        {
            gate.ShowLockedMessage();
            return;
        }

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

            // LATCH ONLY IF IT ACTUALLY STARTED.
            // BeginInstall returns void and can refuse - no power, no RAM, no boot device, or the
            // USB not plugged into this PC's own port - and it says so in the Console and in a
            // dialog. Latching unconditionally treated a refusal as a success: the install never
            // began, and the button that would have let the learner try again hid itself for the
            // rest of the session. They were left looking at a dead screen with no way back.
            //
            // IsRunning is the manager's own answer to "did it start", set true inside BeginInstall
            // after every check has passed, so it cannot disagree with what actually happened.
            hasPoweredOn = OSInstallManager.Instance.IsRunning;

            if (!hasPoweredOn)
            {
                Debug.Log("[PowerButtonInput] Power-on was refused, so the button stays available. " +
                          "The reason is in the warning just above this line.", this);
            }
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