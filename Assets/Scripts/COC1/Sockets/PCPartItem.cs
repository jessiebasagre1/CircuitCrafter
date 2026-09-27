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
    public bool IsPickupReady { get; private set; }
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

    [Header("Sound (optional)")]
    [Tooltip("Overrides SfxPlayer's shared 'part pickup' clip for THIS part only. A PSU coming " +
             "loose should not sound like a stick of RAM. Leave empty to use the shared one.")]
    public AudioClip pickupSound;

    [Tooltip("Overrides SfxPlayer's shared 'part seated' clip for THIS part only. Leave empty to " +
             "use the shared one.")]
    public AudioClip installSound;

    [Header("Pickup-Ready Highlight")]
    [Tooltip("Optional visual (outline, glow, etc.) shown whenever this part is currently able to be picked up � e.g. once all its cables are unplugged, or its lock(s) are open. Leave unassigned for parts that don't need this. Automatically kept in sync every frame.")]
    public GameObject pickupReadyVisual;
    bool pickupReadyVisualActive;

    public PartSocket HomeSocket { get; private set; }

    Vector3 assembledWorldScale;
    Coroutine scaleRoutine;
    //Dialog
    [Header("Tutorial")]
    [Tooltip("Optional � shown the moment this specific part is removed. Leave unassigned for parts that don't need one.")]
    [SerializeField] private DialogSequence removalDialog;
    [SerializeField] private DialogSequence blockedWarningDialog;
    [Tooltip("Optional � shown the moment this specific part is reinstalled. Leave unassigned for parts that don't need one.")]
    [SerializeField] private DialogSequence installDialog;
    [Tooltip("Optional - shown when the player tries to SEAT this part into a socket that is not " +
             "ready for it yet (e.g. the CPU retention lock is still closed). Leave unassigned for " +
             "parts that can always be installed.")]
    [SerializeField] private DialogSequence installBlockedDialog;

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

    /// <summary>
    /// The ordered part that has to come off before this one, or null.
    ///
    /// Pulled out of TryBeginDrag so the pickup-ready highlight can ask the same
    /// question. It previously could not: 'ready' was computed from CanPickup()
    /// alone, which ignores Disassembly Order entirely, so an ordered part sat
    /// there GLOWING "take me off" while something on top of it still had to be
    /// removed first. The learner is told to grab it and then refused when they do.
    /// </summary>
    private PCPartItem RemovalOrderBlocker()
    {
        return enforceRemovalOrder ? FindEarlierUnremovedPart() : null;
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
        // Capture WORLD scale, not local � local scale only means "normal size"
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
        bool ready = HomeSocket != null && CanPickup() && RemovalOrderBlocker() == null;
        IsPickupReady = ready;

        if (pickupReadyVisual == null) return;      // was the first line

        // GUIDANCE: the glow says "this part can come off NOW" - which is exactly the removal order
        // an assessment is marking. Story Mode only. IsPickupReady above is unchanged.
        if (!CircuitCrafter.Guidance.GuidancePolicy.Allows(CircuitCrafter.Guidance.GuidanceKind.ObjectHighlights))
            ready = false;

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
            Debug.Log($"{name}: can't drag � HomeSocket is null (this part isn't currently nested under a socket in the Hierarchy).", this);
            return;
        }

        {
            PCPartItem blocker = RemovalOrderBlocker();
            if (blocker != null)
            {
                Debug.Log($"{name}: can't drag � must remove '{blocker.name}' (step {blocker.removalOrderStep}) first.", this);
                if (outOfOrderDialog != null)
                    DialogManager.Instance.ShowDialog(outOfOrderDialog);
                // ASSESSMENT: removing parts out of order is a procedural error, so it is
                // recorded as a mistake. No-ops entirely outside assessment mode.
                COC1AssessmentManager.ReportMistake(
                    $"Removed '{name}' out of order - '{blocker.name}' must come off first.", this);

                SfxPlayer.Blocked();
                return;
            }
        }

        string blockReason = PickupBlockedReason();
        if (blockReason != null)
        {
            Debug.Log($"{name}: can't drag � {blockReason}", this);
            if (blockedWarningDialog != null)
                DialogManager.Instance.ShowDialog(blockedWarningDialog);
            // ASSESSMENT: this is the single place both of the mistakes we grade surface.
            // CableGatedPartItem returns "'X' is still plugged in" and RAMPartItem returns
            // "'Y' is still closed", so one call covers dragging a part with its cable still
            // attached AND dragging RAM with its retention lock shut - no per-part components
            // and no duplicated rules. No-ops entirely outside assessment mode.
            COC1AssessmentManager.ReportMistake($"Tried to remove '{name}' - {blockReason}", this);

            SfxPlayer.Blocked();
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
    /// silently every frame by the pickup-ready highlight below � do not put
    /// Debug.Log calls inside your override, or they'll spam the Console
    /// every frame. TryBeginDrag() above handles logging on actual clicks.
    /// </summary>
    protected virtual string PickupBlockedReason() => null;

    /// <summary>Silent, per-frame-safe version of the pickup check � no logging.</summary>
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

            // Checkpointing on task completion is now handled generically by
            // TaskCheckpointAutosaver (listens to TaskManager.OnTaskCompleted
            // for ANY task) � no save call needed here anymore.
        }

        Restart(ShrinkThenDisable());

        OnPickedUp();
    }

    /// <summary>
    /// Snaps straight to hidden, WITHOUT the shrink tween AND WITHOUT reporting
    /// task progress or showing the removal dialog. Use this when restoring a
    /// save on scene load.
    ///
    /// Deliberately does NOT call TaskManager.ReportProgress() or
    /// DialogManager.ShowDialog(): those are first-time, player-facing side
    /// effects, and during a restore they're both wrong to replay -
    /// TaskManager.RestoreState() (called separately, straight from the save
    /// data) is the single source of truth for task progress on load, so
    /// reporting here again would race/duplicate it. And popping the removal
    /// dialog back up for every previously-removed part would re-show
    /// tutorials the player already saw last session - worse, since
    /// DialogManager only supports one open dialog at a time, restoring
    /// several removed parts in a row would repeatedly stomp the dialog
    /// queue, leaving it stuck open (and, if pauseGameWhileOpen is on,
    /// Time.timeScale stuck at 0) since nothing ever properly closes it.
    ///
    /// hasReportedRemoval is still set here so a live Pickup() later in this
    /// session won't double-report this same part.
    /// </summary>
    public void PickupInstant()
    {
        if (HomeSocket != null)
        {
            HomeSocket.Occupant = null;
            HomeSocket = null;
        }

        hasReportedRemoval = true;

        if (scaleRoutine != null) StopCoroutine(scaleRoutine);
        transform.localScale = Vector3.zero;
        gameObject.SetActive(false);

        OnPickedUp();
    }

    /// <summary>
    /// Called right after this part starts being picked up. Override to add
    /// part-specific side effects � e.g. hiding cables that are physically
    /// attached to this part so they disappear along with it.
    /// </summary>
    protected virtual void OnPickedUp() { }

    /// <summary>
    /// Override to add extra conditions before this part may be SEATED into a
    /// socket - the install-side twin of PickupBlockedReason above. Return null
    /// if installing is currently allowed, or a short human-readable reason if
    /// it is blocked.
    ///
    /// This is what keeps the CSS procedure in order in the assembly direction.
    /// Removal order is already enforced (Disassembly Order + PickupBlockedReason),
    /// but until now ANY part could be dropped back into ANY empty socket of the
    /// right type, no matter what state the socket was in - so a learner could
    /// drop the CPU straight through a closed retention lock, which on a real
    /// LGA board bends pins and is exactly the mistake this module exists to
    /// prevent.
    ///
    /// IMPORTANT: same rule as PickupBlockedReason - no Debug.Log inside an
    /// override. TryReturnToSocket below does the logging, once, on the actual
    /// drop.
    /// </summary>
    public virtual string InstallBlockedReason(PartSocket socket) => null;

    /// <summary>
    /// The single door every install goes through. Checks InstallBlockedReason,
    /// and either seats the part or refuses it with a message.
    ///
    /// Returns false when the install was refused, which tells DragController to
    /// send the part back where it came from (its hotbar slot) rather than
    /// leaving it shrunk and invisible in the world.
    /// </summary>
    public bool TryReturnToSocket(PartSocket socket)
    {
        if (socket == null) return false;

        string blockReason = InstallBlockedReason(socket);
        if (blockReason != null)
        {
            Debug.Log($"{name}: can't install into '{socket.name}' - {blockReason}", this);

            if (installBlockedDialog != null && DialogManager.Instance != null)
                DialogManager.Instance.ShowDialog(installBlockedDialog);

            // ASSESSMENT: forcing a part into a socket that is not prepared for it is a
            // procedural error, and the same kind the removal-side checks already grade.
            // No-ops entirely outside assessment mode.
            COC1AssessmentManager.ReportMistake(
                $"Tried to install '{name}' into '{socket.name}' - {blockReason}", this);

            SfxPlayer.Blocked();
            return false;
        }

        ReturnToSocket(socket);
        return true;
    }

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
            Debug.LogWarning($"{socket.name} has no recorded installed offset (no part started here at scene load) � falling back to zero offset.", socket);
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

            // Checkpointing on task completion is now handled generically by
            // TaskCheckpointAutosaver (listens to TaskManager.OnTaskCompleted
            // for ANY task) � no save call needed here anymore.
        }

        OnReturnedToSocket(socket);
    }


    /// <summary>
    /// Called right after this part successfully snaps into a socket.
    /// Override to add part-specific side effects � e.g. auto-closing a
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