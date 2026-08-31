using UnityEngine;

/// <summary>
/// Fault setup for the "loose PSU cable" COC4 ticket. Author this scene with
/// the cable NORMALLY PLUGGED IN -- CableConnector's "Start Already Plugged"
/// section: tick Start Plugged and assign Start Socket to wherever it
/// normally connects, exactly like a healthy PC. Then, at Start() (which
/// always runs after every object's Awake, i.e. after that normal auto-plug
/// has already happened), this unplugs it in code -- but ONLY when this
/// specific ticket is the one currently active.
///
/// IMPORTANT: CableConnector.Unplug() only clears the logical plugged state
/// (isPlugged/pluggedSocket) and detaches the parent -- it does NOT move the
/// connector's transform. That's fine for a normal player unplug, since
/// dragging with the mouse repositions it as a side effect. But calling
/// Unplug() here with no drag involved would otherwise leave the connector
/// sitting exactly at the socket's snap point: logically unplugged, visually
/// still plugged in. This script fixes that by moving the connector to
/// Disconnected Rest Point afterward, and nudging its VerletRope (if
/// assigned on the CableConnector) to rebuild for the new, much longer
/// distance between anchors.
///
/// This mirrors RAMRemovedFaultSetup's approach exactly, and exists for the
/// same reason: this scene is shared by more than one COC4 ticket (e.g. this
/// same COC1-Copy also hosts the RAM ticket), so the cable being left
/// permanently unplugged as a static scene setting would show up as a
/// confusing, irrelevant fault while the player is working on a completely
/// different ticket. Scoping the unplug to activeForTicketId keeps every
/// OTHER ticket's visit to this scene looking like a normal, healthy PC
/// aside from whatever fault is actually theirs.
/// </summary>
public class CableUnplugFaultSetup : MonoBehaviour
{
    [Tooltip("The cable to unplug. Should start normally plugged in the editor (Start Plugged checked, Start Socket assigned) -- this script does the disconnecting at runtime, only when relevant.")]
    [SerializeField] private CableConnector cable;

    [Tooltip("Where the loose connector should visually rest once unplugged -- e.g. dangling near the case, off to the side. Required for the fault to actually LOOK disconnected; without this, the connector stays sitting exactly at the socket even though it's logically unplugged.")]
    [SerializeField] private Transform disconnectedRestPoint;

    [Tooltip("Only unplug the cable if this is the ticket currently active, e.g. \"ticket_coc1_psu_cable_fix\". Must match the ticket's Ticket Id exactly. Leave blank ONLY if this scene will never be shared with another ticket.")]
    [SerializeField] private string activeForTicketId;

    private void Start()
    {
        if (!RepairTicketMode.IsActive) return;

        if (!string.IsNullOrEmpty(activeForTicketId) && RepairTicketMode.ActiveTicketId != activeForTicketId)
            return; // a different ticket sent the player here -- leave the cable connected

        if (cable == null)
        {
            Debug.LogWarning("[CableUnplugFaultSetup] No CableConnector assigned.", this);
            return;
        }

        if (!cable.isPlugged)
        {
            // Already unplugged (e.g. Start Plugged was left unchecked) -- nothing to do,
            // but this usually means the scene wasn't authored the way this script expects.
            Debug.LogWarning("[CableUnplugFaultSetup] Cable was already unplugged at scene start -- " +
                              "author it as normally PLUGGED (Start Plugged checked) and let this " +
                              "script do the disconnecting instead, so other tickets sharing this " +
                              "scene see it connected.", this);
            return;
        }

        cable.Unplug();

        if (disconnectedRestPoint != null)
        {
            cable.transform.SetPositionAndRotation(disconnectedRestPoint.position, disconnectedRestPoint.rotation);

            // Unplug() already detached the connector from its socket parent, so this is a
            // plain world-space move. The rope's endAnchor re-pins to the connector's position
            // every physics step regardless (see VerletRope.Simulate), so it will visually
            // follow even without this -- ForceRebuild() just recalculates segment count for
            // the new, much longer anchor distance immediately, instead of the existing segment
            // count stretching oddly for a frame or two before naturally settling.
            if (cable.rope != null)
                cable.rope.ForceRebuild();
        }
        else
        {
            Debug.LogWarning("[CableUnplugFaultSetup] No Disconnected Rest Point assigned -- the cable " +
                              "is now logically unplugged but its transform hasn't moved, so it will " +
                              "still visually appear plugged in. Assign a Transform marking where it " +
                              "should hang/rest instead.", this);
        }

        Debug.Log($"[CableUnplugFaultSetup] Unplugged '{cable.name}' for ticket '{RepairTicketMode.ActiveTicketId}'.", this);
    }
}