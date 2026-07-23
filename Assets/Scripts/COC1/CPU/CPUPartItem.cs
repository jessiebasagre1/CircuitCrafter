/// <summary>
/// CPU version of PCPartItem — pickup is blocked until both CPU locks are
/// open (main lock, then case lock). Everything else (hotbar pickup/drop,
/// snapping back into its socket, scale tween) is inherited unchanged from
/// PCPartItem.
/// </summary>
public class CPUPartItem : PCPartItem
{
    public CPULock mainLock;
    public CPULock caseLock;

    protected override string PickupBlockedReason()
    {
        if (mainLock == null || !mainLock.IsOpen) return "main CPU lock is not open.";
        if (caseLock == null || !caseLock.IsOpen) return "CPU case lock is not open.";
        return null;
    }
}