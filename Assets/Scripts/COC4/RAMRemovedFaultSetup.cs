using UnityEngine;

/// <summary>
/// Fault setup for the "no RAM installed" COC4 ticket. Author this scene
/// with all RAM sticks seated NORMALLY, exactly like the base game -- that
/// way every RAMSocket correctly records its installed offset in
/// PCPartItem.Awake() before this component touches anything. Then, at
/// Start() (which always runs after every object's Awake), this instantly
/// removes each assigned stick into an empty hotbar slot via
/// PCPartItem.PickupInstant() -- the same method your project already uses
/// to restore a save on scene load, just repurposed here for fault setup
/// instead. This avoids the two pitfalls of authoring the sockets as empty
/// from the start: a part can't be dragged out of a socket it never started
/// in (PCPartItem.TryBeginDrag requires a non-null HomeSocket), and a socket
/// with no part ever recorded in it has no known "installed" position for
/// the first part later dropped into it.
///
/// Only runs if RepairTicketMode's active ticket matches activeForTicketId,
/// so this scene can also be reused for OTHER tickets (e.g. the PSU cable
/// fault) without RAM getting yanked out during those visits too. Does
/// nothing during normal Story Mode / Assessment play.
/// </summary>
public class RAMRemovedFaultSetup : MonoBehaviour
{
    [Tooltip("Every RAM stick to remove at scene start, e.g. all 4. Each must start normally seated in its socket in the editor.")]
    [SerializeField] private PCPartItem[] ramSticks;

    [Tooltip("Only remove the RAM if this is the ticket currently active, e.g. \"ticket_coc1_ram_missing\". Leave blank to always run (fine if this scene is ONLY ever used for the RAM ticket).")]
    [SerializeField] private string activeForTicketId;

    private void Start()
    {
        if (!RepairTicketMode.IsActive) return;

        if (!string.IsNullOrEmpty(activeForTicketId) && RepairTicketMode.ActiveTicketId != activeForTicketId)
            return; // a different ticket sent the player here -- leave RAM alone

        if (ramSticks == null || ramSticks.Length == 0)
        {
            Debug.LogWarning("[RAMRemovedFaultSetup] No RAM sticks assigned.", this);
            return;
        }

        int movedCount = 0;
        foreach (var stick in ramSticks)
        {
            if (stick == null) continue;

            var slot = HotbarManager.Instance != null ? HotbarManager.Instance.GetFirstEmptySlot() : null;
            if (slot == null)
            {
                Debug.LogWarning($"[RAMRemovedFaultSetup] No empty hotbar slot available to hold '{stick.name}' -- add more hotbar slots, or reduce ramSticks.", this);
                continue;
            }

            stick.PickupInstant();
            slot.SetItem(stick);
            movedCount++;
        }

        Debug.Log($"[RAMRemovedFaultSetup] Moved {movedCount}/{ramSticks.Length} RAM stick(s) into the hotbar for ticket '{RepairTicketMode.ActiveTicketId}'.", this);
    }
}
