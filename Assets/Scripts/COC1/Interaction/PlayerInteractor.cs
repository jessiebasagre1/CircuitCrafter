using UnityEngine;

/// <summary>
/// Detects clicks/keypresses on InteractableObjects and calls Interact() on
/// them (unchanged generic behavior, used for motherboard etc).
///
/// USB drive pickup/removal and port plugging use a SEPARATE, more forgiving
/// system: instead of requiring a precise raycast/SphereCast hit, this
/// searches for the nearest drive/port that is both within range AND
/// actually visible on screen (in the camera's viewport and not blocked by a
/// wall/object). This takes priority over the generic click system when
/// something's in range.
///
/// IMPORTANT: this forgiving search covers a drive whether it's loose OR
/// currently plugged into a port. Grabbing a plugged-in drive (to unplug it)
/// used to fall through to the generic click system instead, which uses a
/// tiny 0.05 SphereCast -- basically requiring pixel-precise aim at the
/// port. That's why plugged-in drives used to be much harder to grab than
/// loose ones. UpdateNearbyDrive now finds ANY drive in range (loose or
/// inserted), and the grab action branches on IsInsertedInPort to either
/// pick it up directly or unplug-then-pick-up via its CurrentPort.
///
/// Attach one copy to each camera (walking + assembly-station). On the
/// walking camera, being near+looking at a loose drive lets you press
/// pickup to grab it. On the assembly-station camera, being near+looking at
/// a plugged-in drive lets you press the SAME pickup input to unplug and
/// grab it, or (while holding a drive) press Q to plug it in.
///
/// MOBILE: assign interactButton / plugButton in the Inspector to on-screen
/// UI Buttons. interactButton IS the mobile "Grab" button -- it fires the
/// exact same logic as the PC grab key (currently bound via interactKey,
/// e.g. G), including the unplug-and-grab case above. They're auto
/// shown/hidden each frame based on nearbyDrive/nearbyPort/holdingDrive,
/// mirroring the existing promptText logic, and their OnClick can be wired
/// directly to RequestInteract()/RequestPlug() -- or left unassigned
/// entirely for a PC-only build, in which case keyboard/mouse behave
/// exactly as before.
/// </summary>
public class PlayerInteractor : MonoBehaviour
{
    [SerializeField] private Camera interactionCamera;
    [SerializeField] private float maxInteractDistance = 10f;
    [SerializeField] private LayerMask interactableLayer = ~0;
    [SerializeField] private KeyCode plugKey = KeyCode.Q;

    [Header("Interact input")]
    [Tooltip("ON: interact fires on this key (good for locked-cursor first-person). OFF: interact fires on left mouse click.")]
    [SerializeField] private bool useKeyboardInteract = false;
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    [Header("Aim forgiveness (generic InteractableObject clicks only)")]
    [Tooltip("SphereCast radius for the generic click system (motherboard, etc) — not used for the USB drive/port proximity system below.")]
    [SerializeField] private float castRadius = 0.05f;

    [Header("USB drive/port proximity system")]
    [Tooltip("How close the player needs to be to a loose drive to pick it up.")]
    [SerializeField] private float pickupRange = 3f;
    [Tooltip("How close the player needs to be to a port to plug in a held drive.")]
    [SerializeField] private float plugRange = 2f;
    [Tooltip("Layers that count as obstructions blocking line-of-sight to a drive/port (walls, furniture). Should NOT include the drive/port's own layer.")]
    [SerializeField] private LayerMask visibilityObstructionMask = ~0;

    [Header("UI (optional)")]
    [Tooltip("Simple prompt text, e.g. 'Press E to pick up' / 'Press Q to plug in'. Leave unassigned to skip.")]
    [SerializeField] private TMPro.TMP_Text promptText;

    [Header("Mobile Buttons (optional)")]
    [Tooltip("On-screen button for pickup/interact. Wire its OnClick to RequestInteract(). Auto shown/hidden based on nearbyDrive.")]
    [SerializeField] private UnityEngine.UI.Button interactButton;
    [Tooltip("On-screen button for plugging in a held drive. Wire its OnClick to RequestPlug(). Auto shown/hidden based on nearbyPort + holding state.")]
    [SerializeField] private UnityEngine.UI.Button plugButton;

    private USBDriveItem nearbyDrive;
    private USBPort nearbyPort;
    private bool interactRequested;
    private bool plugRequested;

    private void Reset()
    {
        interactionCamera = Camera.main;
    }

    private void Update()
    {
        // Don't let clicks/keys reach the world while a tutorial popup is open.
        if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return;

        bool holdingDrive = CarriedItemHolder.Instance != null && CarriedItemHolder.Instance.HeldDrive != null;

        UpdateNearbyDrive(holdingDrive);
        UpdateNearbyPort(holdingDrive);

        bool interactPressed = useKeyboardInteract
            ? Input.GetKeyDown(interactKey)
            : Input.GetMouseButtonDown(0);

        // Mobile button press, consumed same as a keyboard/mouse press.
        if (interactRequested)
        {
            interactPressed = true;
            interactRequested = false;
        }

        if (interactPressed)
        {
            // Proximity pickup takes priority over the generic raycast click
            // system — if a drive's in easy range (loose OR plugged in),
            // grab it without needing a precise click.
            if (!holdingDrive && nearbyDrive != null)
            {
                if (nearbyDrive.IsInsertedInPort)
                    nearbyDrive.CurrentPort?.RequestRemoveDrive();
                else
                    nearbyDrive.RequestPickup();
            }
            else
            {
                TryGenericInteract();
            }
        }

        bool plugPressed = holdingDrive && nearbyPort != null &&
                            (Input.GetKeyDown(plugKey) || plugRequested);
        plugRequested = false;

        if (plugPressed)
        {
            nearbyPort.PlugIn(CarriedItemHolder.Instance.HeldDrive);
            CarriedItemHolder.Instance.ClearHeld();
        }

        UpdatePrompt(holdingDrive);
        UpdateMobileButtons(holdingDrive);
    }

    /// <summary>Wire this to the mobile Interact/Pickup button's OnClick.</summary>
    public void RequestInteract()
    {
        interactRequested = true;
    }

    /// <summary>Wire this to the mobile Plug button's OnClick.</summary>
    public void RequestPlug()
    {
        plugRequested = true;
    }

    private void UpdateMobileButtons(bool holdingDrive)
    {
        if (interactButton != null)
            interactButton.gameObject.SetActive(!holdingDrive && nearbyDrive != null);

        if (plugButton != null)
            plugButton.gameObject.SetActive(holdingDrive && nearbyPort != null);
    }

    /// <summary>Finds the nearest grabbable drive within pickupRange and
    /// actually visible on screen, highlights it. "Grabbable" includes both
    /// loose drives (picked up directly) AND drives currently plugged into a
    /// port (unplugged then picked up) — both get the same forgiving
    /// distance/visibility check, so a plugged-in drive is no harder to grab
    /// than a loose one.</summary>
    private void UpdateNearbyDrive(bool holdingDrive)
    {
        USBDriveItem found = null;

        if (!holdingDrive)
        {
            float bestDist = pickupRange;
            foreach (var drive in USBDriveItem.AllDrives)
            {
                if (drive == null) continue;

                float dist = Vector3.Distance(interactionCamera.transform.position, drive.transform.position);
                if (dist > bestDist) continue;
                if (!IsVisible(drive.transform.position)) continue;

                found = drive;
                bestDist = dist;
            }
        }

        if (found == nearbyDrive) return;

        if (nearbyDrive != null) nearbyDrive.SetHighlighted(false);
        nearbyDrive = found;
        if (nearbyDrive != null) nearbyDrive.SetHighlighted(true);
    }

    /// <summary>Finds the nearest port within plugRange and visible on screen,
    /// only relevant while the player is actually holding a drive.</summary>
    private void UpdateNearbyPort(bool holdingDrive)
    {
        nearbyPort = null;
        if (!holdingDrive) return;

        float bestDist = plugRange;
        foreach (var port in USBPort.AllPorts)
        {
            if (port == null) continue;

            float dist = Vector3.Distance(interactionCamera.transform.position, port.transform.position);
            if (dist > bestDist) continue;
            if (!IsVisible(port.transform.position)) continue;

            nearbyPort = port;
            bestDist = dist;
        }
    }

    /// <summary>True if worldPos is within the camera's viewport (on screen,
    /// in front of it) AND not blocked by anything on visibilityObstructionMask.</summary>
    private bool IsVisible(Vector3 worldPos)
    {
        Vector3 viewportPoint = interactionCamera.WorldToViewportPoint(worldPos);
        if (viewportPoint.z <= 0f) return false; // behind the camera
        if (viewportPoint.x < 0f || viewportPoint.x > 1f || viewportPoint.y < 0f || viewportPoint.y > 1f)
            return false; // off-screen

        Vector3 origin = interactionCamera.transform.position;
        Vector3 toTarget = worldPos - origin;
        float dist = toTarget.magnitude;

        if (Physics.Raycast(origin, toTarget.normalized, out RaycastHit hit, dist, visibilityObstructionMask))
        {
            // Something on the obstruction mask sits meaningfully closer than
            // the target itself — a wall/desk/etc is in the way.
            if (hit.distance < dist - 0.05f) return false;
        }

        return true;
    }

    // ---------------------------------------------------------------
    // Generic click-to-Interact() system — unchanged, used for anything
    // that isn't a USB drive/port (motherboard, etc).
    // ---------------------------------------------------------------

    private void TryGenericInteract()
    {
        Ray ray = useKeyboardInteract
            ? interactionCamera.ScreenPointToRay(new Vector2(Screen.width / 2f, Screen.height / 2f))
            : interactionCamera.ScreenPointToRay(Input.mousePosition);

        if (Physics.SphereCast(ray, castRadius, out RaycastHit hit, maxInteractDistance, interactableLayer))
        {
            if (hit.collider.TryGetComponent(out InteractableObject interactable))
                interactable.Interact();
        }
    }

    private void UpdatePrompt(bool holdingDrive)
    {
        if (promptText == null) return;

        if (holdingDrive && nearbyPort != null)
            promptText.text = $"Press {plugKey} to plug in";
        else if (!holdingDrive && nearbyDrive != null)
        {
            string action = nearbyDrive.IsInsertedInPort ? "unplug" : "pick up";
            promptText.text = useKeyboardInteract ? $"Press {interactKey} to {action}" : $"Click to {action}";
        }
        else
            promptText.text = "";
    }

    private void OnDisable()
    {
        // Clean up so a highlight doesn't stay stuck on if this camera gets
        // deactivated (e.g. StationCameraSwitch) while something's highlighted.
        if (nearbyDrive != null)
        {
            nearbyDrive.SetHighlighted(false);
            nearbyDrive = null;
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (interactionCamera == null) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(interactionCamera.transform.position, pickupRange);
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(interactionCamera.transform.position, plugRange);
    }
#endif
}