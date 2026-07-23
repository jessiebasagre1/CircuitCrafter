using System.Collections;
using UnityEngine;

/// <summary>
/// Attach to any PC part that can be pulled off the motherboard/case and
/// stored in the hotbar. Caches the part's "assembled" scale on Awake so it
/// can be restored exactly when the part is placed back into a socket.
/// </summary>
[RequireComponent(typeof(Collider))]
public class PCPartItem : MonoBehaviour
{
    [Header("Identity")]
    public string partId;
    public PartType partType;
    public Sprite icon; // shown in the hotbar slot
    [TextArea(3, 6)]
    [Tooltip("Shown in the description panel when this part is pressed in the hotbar.")]
    public string description;

    [Header("Tween")]
    public float scaleDuration = 0.15f;

    [Header("Alignment Correction")]
    [Tooltip("Manual nudge applied on top of a socket's recorded offset, in case this specific part's pivot doesn't perfectly match the part the socket originally learned its offset from. Leave at zero if this part already aligns correctly.")]
    public Vector3 installPositionCorrection = Vector3.zero;
    [Tooltip("Rotation correction in degrees, same idea as above.")]
    public Vector3 installRotationCorrectionEuler = Vector3.zero;

    [Header("Pickup-Ready Highlight")]
    [Tooltip("Optional visual (outline, glow, etc.) shown whenever this part is currently able to be picked up — e.g. once all its cables are unplugged, or its lock(s) are open. Leave unassigned for parts that don't need this. Automatically kept in sync every frame.")]
    public GameObject pickupReadyVisual;
    bool pickupReadyVisualActive;

    public PartSocket HomeSocket { get; private set; }

    Vector3 assembledWorldScale;
    Coroutine scaleRoutine;
    //Dialog
    [Header("Tutorial")]
    [Tooltip("Optional — shown the moment this specific part is removed. Leave unassigned for parts that don't need one.")]
    [SerializeField] private DialogSequence removalDialog;
    [SerializeField] private DialogSequence blockedWarningDialog;
    [Tooltip("Optional — shown the moment this specific part is reinstalled. Leave unassigned for parts that don't need one.")]
    [SerializeField] private DialogSequence installDialog;

    [Header("Disassembly Order (optional)")]
    [Tooltip("Enable to require lower-numbered ordered parts be removed first. Leave OFF for parts removable anytime.")]
    [SerializeField] private bool enforceRemovalOrder = false;
    [SerializeField] private int removalOrderStep = 0;
    [Tooltip("Shown if the player tries to remove this before earlier-step parts are removed.")]
    [SerializeField] private DialogSequence outOfOrderDialog;

    private static readonly System.Collections.Generic.List<PCPartItem> orderedParts = new System.Collections.Generic.List<PCPartItem>();

    public bool IsRemoved => hasReportedRemoval;

    private void OnEnable()
    {
        if (enforceRemovalOrder) orderedParts.Add(this);
    }

    private void OnDisable()
    {
        orderedParts.Remove(this);
    }

    private PCPartItem FindEarlierUnremovedPart()
    {
        foreach (var other in orderedParts)
        {
            if (other == this) continue;
            if (other.removalOrderStep < removalOrderStep && !other.IsRemoved)
                return other;
        }
        return null;
    }

    void Awake()
    {
        // Capture WORLD scale, not local — local scale only means "normal size"
        // relative to whichever socket this part happens to start in. If it's
        // later placed under a differently-scaled socket, reusing the old local
        // value would grow it to the wrong actual size.
        assembledWorldScale = transform.lossyScale;

        HomeSocket = GetComponentInParent<PartSocket>();
        if (HomeSocket != null)
        {
            HomeSocket.Occupant = this;
            HomeSocket.RecordInstalledOffset(transform.localPosition, transform.localRotation);
        }
    }

   

    void Update()
    {
        if (pickupReadyVisual == null) return;

        // Only relevant while actually installed in a socket — HomeSocket is null
        // while mid-drag or sitting in the hotbar, where "ready to pick up" doesn't apply.
        bool ready = HomeSocket != null && CanPickup();
        if (ready == pickupReadyVisualActive) return;

        Debug.Log($"{name}: pickup-ready visual changing to {ready} (HomeSocket={(HomeSocket != null ? HomeSocket.name : "null")})", this);

        pickupReadyVisualActive = ready;
        pickupReadyVisual.SetActive(ready);
    }

    /// <summary>
    /// Starts a drag out of this part's socket. Public so a child object's
    /// own collider (e.g. ForwardClickToParent) can trigger it too.
    /// </summary>
    /// <summary>Override to false for parts that don't live in a PartSocket
    /// (e.g. a standalone data cable that hangs between two connectors).</summary>
    public void TryBeginDrag()
    {
        if (HomeSocket == null)
        {
            Debug.Log($"{name}: can't drag — HomeSocket is null (this part isn't currently nested under a socket in the Hierarchy).", this);
            return;
        }

        if (enforceRemovalOrder)
        {
            PCPartItem blocker = FindEarlierUnremovedPart();
            if (blocker != null)
            {
                Debug.Log($"{name}: can't drag — must remove '{blocker.name}' (step {blocker.removalOrderStep}) first.", this);
                if (outOfOrderDialog != null)
                    DialogManager.Instance.ShowDialog(outOfOrderDialog);
                return;
            }
        }

        string blockReason = PickupBlockedReason();
        if (blockReason != null)
        {
            Debug.Log($"{name}: can't drag — {blockReason}", this);
            if (blockedWarningDialog != null)
                DialogManager.Instance.ShowDialog(blockedWarningDialog);
            return;
        }

        DragController.Instance.BeginDragFromWorld(this);
    }

    /// <summary>
    /// Override to add extra conditions before this part can be picked up
    /// (e.g. a retention latch must be open, or cables must be unplugged).
    /// Return null if pickup is currently allowed, or a short human-readable
    /// reason string if it's blocked.
    ///
    /// IMPORTANT: this is called both once per click (and logged then) AND
    /// silently every frame by the pickup-ready highlight below — do not put
    /// Debug.Log calls inside your override, or they'll spam the Console
    /// every frame. TryBeginDrag() above handles logging on actual clicks.
    /// </summary>
    protected virtual string PickupBlockedReason() => null;

    /// <summary>Silent, per-frame-safe version of the pickup check — no logging.</summary>
    bool CanPickup() => PickupBlockedReason() == null;
    private bool hasReportedRemoval;
    private bool hasReportedInstall;

    /// <summary>Detach from its socket and shrink out of view. Called the moment a drag starts.</summary>
    public void Pickup()
    {
        if (HomeSocket != null)
        {
            HomeSocket.Occupant = null;
            HomeSocket = null;
        }

        if (!hasReportedRemoval)
        {
            hasReportedRemoval = true;

            bool wasAlreadyComplete = TaskManager.Instance.IsTaskComplete(PCDisassemblySetup.TaskId);
            TaskManager.Instance.ReportProgress(PCDisassemblySetup.TaskId);
            bool justCompleted = !wasAlreadyComplete && TaskManager.Instance.IsTaskComplete(PCDisassemblySetup.TaskId);

            if (removalDialog != null && !justCompleted)
                DialogManager.Instance.ShowDialog(removalDialog);
        }

        Restart(ShrinkThenDisable());

        OnPickedUp();
    }

    /// <summary>
    /// Called right after this part starts being picked up. Override to add
    /// part-specific side effects — e.g. hiding cables that are physically
    /// attached to this part so they disappear along with it.
    /// </summary>
    protected virtual void OnPickedUp() { }

    /// <summary>Snap into a socket and grow back to assembled size.</summary>
    public void ReturnToSocket(PartSocket socket)
    {
        HomeSocket = socket;
        socket.Occupant = this;

        transform.SetParent(socket.transform, worldPositionStays: false);

        if (socket.HasInstalledOffset)
        {
            transform.localPosition = socket.InstalledLocalPosition + installPositionCorrection;
            transform.localRotation = socket.InstalledLocalRotation * Quaternion.Euler(installRotationCorrectionEuler);
        }
        else
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            Debug.LogWarning($"{socket.name} has no recorded installed offset (no part started here at scene load) — falling back to zero offset.", socket);
        }

        transform.localScale = Vector3.zero;
        gameObject.SetActive(true);

        Debug.Log($"{name}: returned to socket '{socket.name}'", this);

        Restart(ScaleTo(TargetLocalScaleFor(socket.transform)));

        if (!hasReportedInstall)
        {
            hasReportedInstall = true;

            bool wasAlreadyComplete = TaskManager.Instance.IsTaskComplete(PCDisassemblySetup.AssembleTaskId);
            TaskManager.Instance.ReportProgress(PCDisassemblySetup.AssembleTaskId);
            bool justCompleted = !wasAlreadyComplete && TaskManager.Instance.IsTaskComplete(PCDisassemblySetup.AssembleTaskId);

            if (installDialog != null && !justCompleted)
                DialogManager.Instance.ShowDialog(installDialog);
        }

        OnReturnedToSocket(socket);
    }


    /// <summary>
    /// Called right after this part successfully snaps into a socket.
    /// Override to add part-specific side effects — e.g. auto-closing a
    /// retention clip once the part is reinstalled.
    /// </summary>
    protected virtual void OnReturnedToSocket(PartSocket socket) { }

    /// <summary>
    /// Local scale needed under the given parent to reproduce this part's
    /// original world-space size, correcting for that parent's own scale.
    /// </summary>
    Vector3 TargetLocalScaleFor(Transform parent)
    {
        Vector3 parentWorldScale = parent.lossyScale;
        return new Vector3(
            parentWorldScale.x != 0 ? assembledWorldScale.x / parentWorldScale.x : assembledWorldScale.x,
            parentWorldScale.y != 0 ? assembledWorldScale.y / parentWorldScale.y : assembledWorldScale.y,
            parentWorldScale.z != 0 ? assembledWorldScale.z / parentWorldScale.z : assembledWorldScale.z
        );
    }

    IEnumerator ShrinkThenDisable()
    {
        yield return ScaleTo(Vector3.zero);
        gameObject.SetActive(false);
    }

    IEnumerator ScaleTo(Vector3 target)
    {
        Vector3 start = transform.localScale;
        float t = 0f;
        while (t < scaleDuration)
        {
            t += Time.deltaTime;
            transform.localScale = Vector3.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t / scaleDuration));
            yield return null;
        }
        transform.localScale = target;
    }

    void Restart(IEnumerator routine)
    {
        if (scaleRoutine != null) StopCoroutine(scaleRoutine);
        scaleRoutine = StartCoroutine(routine);
    }
}