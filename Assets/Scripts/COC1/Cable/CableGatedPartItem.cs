using UnityEngine;

/// <summary>
/// Base for any part that can only be picked up once every cable assigned
/// to it has been unplugged — a component with cables coming out of it
/// (like a PSU), or a standalone cable with connectors on both ends (like a
/// SATA data cable, where "cables" here would just be its own 2 connectors).
/// Assign every relevant CableConnector in the Inspector.
/// </summary>
public class CableGatedPartItem : PCPartItem
{
    [Header("Cables")]
    [Tooltip("Every CableConnector that must be unplugged before this part can be picked up.")]
    public CableConnector[] cables;

    [Tooltip("If true, each cable connector (and its VerletRope, if assigned on the connector) is hidden the moment this part is picked up, and shown again once it's placed back. Turn this ON for parts the cables are physically attached to (a PSU, or a standalone cable's own two ends) — leave it OFF for parts that cables merely plug INTO from elsewhere (e.g. an SSD), since those cables don't belong to this part and shouldn't vanish with it.")]
    public bool hideCablesWhilePickedUp = false;

    protected override string PickupBlockedReason()
    {
        if (cables == null || cables.Length == 0)
            return "no cables assigned in the Cables array.";

        foreach (var cable in cables)
        {
            if (cable == null)
                return "the Cables array has an empty (unassigned) slot.";
            if (cable.isPlugged)
                return $"'{cable.name}' is still plugged in.";
        }
        return null;
    }

    protected override void OnPickedUp()
    {
        if (hideCablesWhilePickedUp) SetCablesVisible(false);
    }

    protected override void OnReturnedToSocket(PartSocket socket)
    {
        if (hideCablesWhilePickedUp) SetCablesVisible(true);
    }

    void SetCablesVisible(bool visible)
    {
        if (cables == null) return;

        foreach (var cable in cables)
        {
            if (cable == null) continue;
            cable.gameObject.SetActive(visible);

            // Also hide the rope mesh if this connector references it — the rope
            // is often its own separate object, not a child of the connector.
            if (cable.rope != null)
                cable.rope.gameObject.SetActive(visible);
        }
    }
}