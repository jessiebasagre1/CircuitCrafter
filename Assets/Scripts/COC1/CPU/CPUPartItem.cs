using UnityEngine;

/// <summary>
/// CPU version of PCPartItem.
///
/// TWO THINGS STAND BETWEEN THE LEARNER AND THE PROCESSOR, and they have to be
/// dealt with in this order, because that is the order they are stacked in:
///
///   1. THE HEATSINK, bolted on top. Nothing about the CPU can be touched until
///      the cooler is off.
///   2. THE RETENTION LOCKS, which must both be OPEN before the CPU can be
///      lifted out or lowered in.
///
/// Both gates feed BOTH directions - pickup and install - from one method, so the
/// two can never drift apart and leave the CPU removable but not re-installable.
///
/// WHY THE HEATSINK CHECK LIVES HERE rather than in the Disassembly Order fields:
/// PickupBlockedReason is also what drives the Pickup-Ready Highlight. Put the
/// heatsink rule in Disassembly Order instead and the CPU would sit there GLOWING
/// "ready to remove" underneath a heatsink that has to come off first - which is
/// exactly the highlight conflict this is meant to avoid.
///
/// Correct sequence, and the sequence this enforces:
///   REMOVE:  heatsink off -> open main lock -> open case lock -> lift CPU out
///   INSTALL: place CPU -> close case lock -> close main lock -> heatsink on
///
/// Everything else (hotbar pickup/drop, snapping back into its socket, scale tween)
/// is inherited unchanged from PCPartItem.
/// </summary>
public class CPUPartItem : PCPartItem
{
    public CPULock mainLock;
    public CPULock caseLock;

    [Tooltip("OPTIONAL. The heatsink's PartSocket. While a heatsink is seated there the " +
             "CPU cannot be removed or installed, and its pickup-ready highlight stays off.\n\n" +
             "Leave empty on a build with no heatsink and this component behaves exactly as before.")]
    public PartSocket heatsinkSocket;

    /// <summary>
    /// Catches the one wiring mistake whose symptom points at completely the wrong thing.
    ///
    /// If this CPU knows about the heatsink socket but its LOCKS do not, the levers stay
    /// operable underneath a mounted cooler. The learner then has to open both CPU locks
    /// before the heatsink will come off - the reverse of the real procedure - and nothing
    /// in the Console says why. Said out loud here, with the fix.
    /// </summary>
    private void Start()
    {
        if (heatsinkSocket == null) return;

        if (mainLock != null && mainLock.heatsinkSocket == null)
            Debug.LogWarning($"[CPUPartItem] '{name}' has a Heatsink Socket but '{mainLock.name}' " +
                             "does not. Assign the SAME socket to that CPULock's Heatsink Socket " +
                             "field, or the lever can still be operated with the cooler on top.", mainLock);

        if (caseLock != null && caseLock.heatsinkSocket == null)
            Debug.LogWarning($"[CPUPartItem] '{name}' has a Heatsink Socket but '{caseLock.name}' " +
                             "does not. Assign the SAME socket to that CPULock's Heatsink Socket " +
                             "field, or the lever can still be operated with the cooler on top.", caseLock);
    }

    protected override string PickupBlockedReason()
    {
        return BlockedReason();
    }

    public override string InstallBlockedReason(PartSocket socket)
    {
        // Deliberately ignores which socket it is. The CPU has exactly one socket,
        // and if a second CPU socket is ever added, this part's own heatsink and
        // locks are still the things standing in the way.
        return BlockedReason();
    }

    /// <summary>
    /// Shared by both gates - one rule, one order of reporting.
    ///
    /// Heatsink first because it is physically on top. Then the main lock, because
    /// CPULock refuses to open the case lock until the main lock is already open, so
    /// naming the case lock first would send the learner to the wrong lever.
    /// </summary>
    private string BlockedReason()
    {
        if (heatsinkSocket != null && !heatsinkSocket.IsEmpty)
            return "the heatsink is still mounted on top of it.";

        if (mainLock == null) return "no Main Lock is assigned on this CPU.";
        if (caseLock == null) return "no Case Lock is assigned on this CPU.";

        if (!mainLock.IsOpen) return "the main CPU lock is still closed.";
        if (!caseLock.IsOpen) return "the CPU case lock is still closed.";

        return null;
    }
}