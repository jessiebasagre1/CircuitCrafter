using UnityEngine;

/// <summary>
/// Fault setup for a "this part isn't installed" COC4 ticket -- generic
/// twin of RAMRemovedFaultSetup, for a single part (GPU, SSD, etc.) instead
/// of RAM's "remove several sticks" case. Author the scene with the part
/// seated NORMALLY (so its socket correctly records an installed offset in
/// PCPartItem.Awake() before this runs), then this pulls it into an empty
/// hotbar slot at Start() -- but only for the ticket it belongs to.
///
/// See RAMRemovedFaultSetup's class comment for the full reasoning on why
/// this uses PickupInstant() + HotbarManager instead of just authoring the
/// socket as empty from the start.
/// </summary>
public class PartRemovedFaultSetup : MonoBehaviour
{
    [Tooltip("The part to remove at scene start, e.g. the GPU or the SSD. Must start normally seated in its socket in the editor.")]
    [SerializeField] private PCPartItem part;

    [Tooltip("Only remove the part if this is the ticket currently active, e.g. \"ticket_coc1_gpu_missing\". Leave blank ONLY if this scene will never be shared with another ticket.")]
    [SerializeField] private string activeForTicketId;

    private void Start()
    {
        if (!RepairTicketMode.IsActive) return;

        if (!string.IsNullOrEmpty(activeForTicketId) && RepairTicketMode.ActiveTicketId != activeForTicketId)
            return; // a different ticket sent the player here -- leave this part alone

        if (part == null)
        {
            Debug.LogWarning("[PartRemovedFaultSetup] No Part assigned.", this);
            return;
        }

        var slot = HotbarManager.Instance != null ? HotbarManager.Instance.GetFirstEmptySlot() : null;
        if (slot == null)
        {
            Debug.LogWarning($"[PartRemovedFaultSetup] No empty hotbar slot available to hold '{part.name}'.", this);
            return;
        }

        part.PickupInstant();
        slot.SetItem(part);

        Debug.Log($"[PartRemovedFaultSetup] Moved '{part.name}' into the hotbar for ticket '{RepairTicketMode.ActiveTicketId}'.", this);
    }
}
