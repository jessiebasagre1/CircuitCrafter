/// <summary>
/// RAM version of PCPartItem. Pickup requires every retention clip on
/// whichever socket this stick currently occupies to be open — not locks
/// fixed to this specific stick, since sticks can move between sockets.
/// Reinserting a stick automatically closes that socket's clips again.
/// </summary>
public class RAMPartItem : PCPartItem
{
    protected override string PickupBlockedReason()
    {
        var ramSocket = HomeSocket as RAMSocket;
        if (ramSocket == null)
            return $"HomeSocket ('{HomeSocket.name}') is not a RAMSocket. Did you replace the old PartSocket component with RAMSocket on this socket, or just add RAMSocket alongside it?";

        if (ramSocket.locks == null || ramSocket.locks.Length == 0)
            return $"'{ramSocket.name}' has no locks assigned in its Locks array.";

        foreach (var ramLock in ramSocket.locks)
        {
            if (ramLock == null)
                return $"'{ramSocket.name}' has an empty (unassigned) slot in its Locks array.";
            if (!ramLock.IsOpen)
                return $"'{ramLock.name}' is still closed.";
        }

        return null;
    }

    protected override void OnReturnedToSocket(PartSocket socket)
    {
        var ramSocket = socket as RAMSocket;
        if (ramSocket == null || ramSocket.locks == null) return;

        foreach (var ramLock in ramSocket.locks)
        {
            if (ramLock != null) ramLock.Close();
        }
    }
}