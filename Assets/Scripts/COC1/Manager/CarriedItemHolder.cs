using UnityEngine;

/// <summary>
/// Tracks whichever USBDriveItem the player is currently carrying, independent
/// of which camera is active. Needed because pickup happens via the
/// first-person walking camera (out in the world) and plugging in happens via
/// the assembly-station camera (zoomed in on the PC) — two separate
/// PlayerInteractor instances, one shared "what am I holding" source of truth.
///
/// Put this on a persistent Managers object, same as DialogManager/OSInstallManager.
/// </summary>
public class CarriedItemHolder : MonoBehaviour
{
    public static CarriedItemHolder Instance { get; private set; }

    [Tooltip("Where a held USB drive is parented for visible carrying — a child of the FIRST-PERSON camera, not the assembly camera (you don't see your \"hands\" in the zoomed assembly view anyway).")]
    [SerializeField] private Transform handSocket;

    public USBDriveItem HeldDrive { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void PickUp(USBDriveItem drive)
    {
        if (HeldDrive != null) return; // already holding something

        HeldDrive = drive;

        if (handSocket != null)
        {
            drive.transform.SetParent(handSocket, worldPositionStays: false);
            drive.transform.localPosition = Vector3.zero;
            drive.transform.localRotation = Quaternion.identity;
        }

        var col = drive.GetComponent<Collider>();
        if (col != null) col.enabled = false; // avoid the held item blocking its own raycast
    }

    /// <summary>Call once the held drive has been plugged into a port (or otherwise let go of).</summary>
    public void ClearHeld()
    {
        HeldDrive = null;
    }
}