using UnityEngine;

/// <summary>
/// The CPU cooler. ATTACH THIS TO: the heatsink GameObject, in place of a plain
/// PCPartItem. Set its Part Type to Cooler and give its socket the same type.
///
/// THE HEATSINK IS THE LAST THING ON AND THE FIRST THING OFF.
///
/// INSTALL is gated on the CPU underneath being finished:
///   * a CPU actually seated in its socket, and
///   * BOTH retention locks CLOSED.
/// That is the exact inverse of CPUPartItem, which needs them OPEN. Mounting a
/// cooler over an empty socket, or over a processor that is only resting in an
/// open socket, is how a CPU ends up crushed or sitting crooked under spring
/// pressure - so it is refused outright rather than merely discouraged.
///
/// REMOVAL is unrestricted. The cooler is the top of the stack; on a real bench it
/// is the first thing that comes off, so nothing is in its way.
///
/// THE HIGHLIGHT BEHAVIOUR YOU ASKED FOR falls out of this for free.
/// DragController now only highlights a socket that would actually ACCEPT the
/// dragged part right now, so while the CPU is missing or its locks are open, the
/// heatsink socket stays dark - no competing glow stacked over the CPU socket
/// beneath it. Close the locks and the heatsink socket lights up.
///
/// THE FAN POWER CABLE is handled by extending CableGatedPartItem rather than
/// PCPartItem: assign the fan's CableConnector to the inherited Cables array and the
/// lead must be unplugged from the CPU_FAN header before the cooler will come off,
/// and plugged back in before assembly counts as finished. Leave the array EMPTY and
/// that gate simply does not apply - see PickupBlockedReason below.
///
/// Turn ON the inherited Hide Cables While Picked Up: the fan lead belongs to the
/// heatsink, so it should vanish with it rather than being left hanging in mid-air.
/// </summary>
public class HeatsinkPartItem : CableGatedPartItem
{
    [Header("What must be finished underneath")]
    [Tooltip("The CPU's PartSocket. A CPU must be seated here before this heatsink can be mounted.")]
    public PartSocket cpuSocket;

    [Tooltip("The CPU's main retention lever. Must be CLOSED before the heatsink goes on.")]
    public CPULock mainLock;

    [Tooltip("The CPU's case lock / load plate. Must be CLOSED before the heatsink goes on.")]
    public CPULock caseLock;

    /// <summary>
    /// The fan-cable gate, made optional.
    ///
    /// CableGatedPartItem treats an EMPTY Cables array as a setup mistake and refuses
    /// pickup outright - correct for a PSU, which always has leads, but a trap here:
    /// drop this component on a cooler whose cable you have not wired up yet and the
    /// heatsink becomes permanently unremovable, with the reason buried in the Console.
    /// An empty array here means "this cooler has no modelled fan lead", so the gate is
    /// skipped and the part behaves like any other.
    /// </summary>
    protected override string PickupBlockedReason()
    {
        if (cables == null || cables.Length == 0) return null;
        return base.PickupBlockedReason();
    }

    public override string InstallBlockedReason(PartSocket socket)
    {
        if (cpuSocket == null) return "no CPU Socket is assigned on this heatsink.";
        if (mainLock == null) return "no Main Lock is assigned on this heatsink.";
        if (caseLock == null) return "no Case Lock is assigned on this heatsink.";

        if (cpuSocket.IsEmpty)
            return "the CPU is not installed yet.";

        // Case lock first, then main lever - the reverse of the order they were opened
        // in, which is the order the learner has to close them in.
        if (caseLock.IsOpen)
            return "the CPU load plate is still open.";

        if (mainLock.IsOpen)
            return "the CPU retention lever is still open.";

        return null;
    }
}
