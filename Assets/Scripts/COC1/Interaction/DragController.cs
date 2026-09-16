using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using CircuitCrafter.Repair;
using UnityEngine.UI;

/// <summary>
/// Single hub that drives dragging in both directions:
///   world part  -> hotbar slot
///   hotbar slot -> world socket
///
/// Uses one shared UI "drag icon" that follows the cursor. On release it
/// first checks for a hotbar slot under the pointer (UI raycast), then a
/// matching PartSocket in the world (physics raycast). If neither is valid,
/// the item returns to wherever it started.
///
/// Reads pointer state via PointerInput (not Input.mousePosition /
/// Input.GetMouseButtonUp directly) - see PointerInput.cs for why: on
/// mobile, MouseLook.cs turns off Input.simulateMouseWithTouches, which
/// silently kills raw mouse APIs for everyone, not just MouseLook. Without
/// this, dragging never even started on touch devices.
///
/// Requires an EventSystem in the scene (standard Unity UI requirement).
/// </summary>
public class DragController : MonoBehaviour
{
    public static DragController Instance { get; private set; }

    /// <summary>True while a part is currently being dragged (world or hotbar), false otherwise.</summary>
    public bool IsDragging => draggedItem != null;

    /// <summary>What is currently being dragged, or null. Read by DustScrubber
    /// to tell a cleaning tool from a computer part mid-drag.</summary>
    public PCPartItem DraggedItem => draggedItem;

    [Header("Refs")]
    public Camera worldCamera;
    [Tooltip("UI Image used as the floating drag cursor. Keep Raycast Target OFF.")]
    public Image dragIcon;
    [Tooltip("Layer(s) that PartSocket colliders live on.")]
    public LayerMask socketLayerMask;
    public float socketRaycastDistance = 100f;

    PCPartItem draggedItem;
    HotbarSlot sourceSlot; // non-null only if the drag started from a hotbar slot
    PartSocket hoveredSocket; // currently-highlighted socket, if any

    void Awake()
    {
        Instance = this;
        dragIcon.raycastTarget = false;
        dragIcon.gameObject.SetActive(false);
    }

    public void BeginDragFromWorld(PCPartItem item)
    {
        // COC4: during a repair ticket nothing may be moved until the fault has
        // been diagnosed. Checked HERE rather than in PCPartItem.TryBeginDrag
        // because this is the one door every part drag comes through - world
        // clicks and hotbar drags both - so one guard covers both and cannot be
        // forgotten on a part added later. Inert outside RepairTicketMode.
        if (RepairWorkGate.Refuse(item)) return;

        draggedItem = item;
        sourceSlot = null;
        StartDragVisual(item.icon);
        item.Pickup();

        // After Pickup(), so a part with its own clip has already been resolved.
        SfxPlayer.PartPickedUp(item);
    }

    public void BeginDragFromHotbar(HotbarSlot slot)
    {
        // The item, not a string: a cleaning tool must be draggable out of the
        // hotbar before diagnosis, because picking one up is not a repair.
        if (RepairWorkGate.Refuse(slot != null ? slot.StoredItem : null)) return;

        draggedItem = slot.StoredItem;
        sourceSlot = slot;
        StartDragVisual(draggedItem.icon);
        slot.Clear();

        SfxPlayer.HotbarTaken();
    }

    void StartDragVisual(Sprite icon)
    {
        dragIcon.sprite = icon;
        dragIcon.gameObject.SetActive(true);
    }

    void Update()
    {
        if (draggedItem == null) return;

        // Screen Space - Overlay canvas assumed: screen point == canvas point.
        // If your hotbar canvas is Screen Space - Camera or World Space, convert
        // with RectTransformUtility.ScreenPointToLocalPointInRectangle instead.
        dragIcon.transform.position = PointerInput.Position;

        UpdateHoverHighlight();

        if (PointerInput.Up)
            ResolveDrop();
    }

    void UpdateHoverHighlight()
    {
        // requireInstallable: TRUE for the highlight. A socket only lights up if it
        // would actually take this part RIGHT NOW - so the heatsink's socket stays
        // dark until the CPU underneath is seated and locked, instead of glowing over
        // the top of the CPU socket and inviting a step that would be refused.
        PartSocket socket = FindSocketUnderCursor(requireInstallable: true);
        if (socket == hoveredSocket) return; // no change since last frame

        if (hoveredSocket != null) hoveredSocket.SetHighlighted(false);
        hoveredSocket = socket;
        if (hoveredSocket != null) hoveredSocket.SetHighlighted(true);
    }

    void ClearHover()
    {
        if (hoveredSocket == null) return;
        hoveredSocket.SetHighlighted(false);
        hoveredSocket = null;
    }

    // Reused buffer for the socket raycast below. Preallocated because that raycast runs
    // every frame of every drag, and on a phone a per-frame array allocation is exactly
    // the kind of thing that turns into a visible hitch when the GC eventually collects.
    readonly RaycastHit[] socketHits = new RaycastHit[16];

    /// <summary>
    /// Finds the nearest socket under the cursor that would accept the dragged item.
    ///
    /// ALL HITS, NOT JUST THE FIRST. This used to be a plain Physics.Raycast, which
    /// returns whatever collider happens to be closest and then throws the drop away if
    /// that one is the wrong type. With sockets STACKED - the heatsink mounts directly
    /// over the CPU - that meant dragging the CPU could hit the heatsink's socket in
    /// front of it, fail the type check, and return null, leaving the CPU with nowhere
    /// to go even though its own socket was right there behind it. Scanning every hit
    /// and keeping the nearest ACCEPTING one makes stacked sockets work.
    ///
    /// requireInstallable also applies the part's own InstallBlockedReason. Pass true
    /// for the hover highlight, false for the actual drop - see TryDropOnSocket for why
    /// the drop deliberately uses the looser test.
    /// </summary>
    PartSocket FindSocketUnderCursor(bool requireInstallable)
    {
        if (draggedItem == null) return null;

        Ray ray = worldCamera.ScreenPointToRay(PointerInput.Position);
        // QueryTriggerInteraction.Collide spelled out, because PartSocket's own setup notes
        // tell you to give it a TRIGGER collider - so whether sockets are found at all must
        // not depend on the project-wide Queries Hit Triggers checkbox. PartClickDetector
        // already passes this for the same reason.
        int count = Physics.RaycastNonAlloc(ray, socketHits, socketRaycastDistance, socketLayerMask,
                                            QueryTriggerInteraction.Collide);

        PartSocket best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            if (socketHits[i].distance >= bestDistance) continue;

            var socket = socketHits[i].collider.GetComponentInParent<PartSocket>();
            if (socket == null) continue;
            if (!socket.IsEmpty) continue;
            if (socket.acceptedType != draggedItem.partType) continue;

            // InstallBlockedReason is called every frame from the highlight path, which is
            // why overrides of it are required to be silent - no Debug.Log inside one.
            if (requireInstallable && draggedItem.InstallBlockedReason(socket) != null) continue;

            bestDistance = socketHits[i].distance;
            best = socket;
        }

        return best;
    }

    void ResolveDrop()
    {
        dragIcon.gameObject.SetActive(false);
        ClearHover();

        if (TryDropOnHotbar()) { draggedItem = null; return; }
        if (TryDropOnSocket()) { draggedItem = null; return; }

        ReturnToOrigin();
        draggedItem = null;
    }

    bool TryDropOnHotbar()
    {
        var pointerData = new PointerEventData(EventSystem.current) { position = PointerInput.Position };
        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointerData, results);

        foreach (var result in results)
        {
            var slot = result.gameObject.GetComponentInParent<HotbarSlot>();
            if (slot != null && slot.IsEmpty)
            {
                slot.SetItem(draggedItem); // item is already shrunk/disabled, this just shows its icon
                SfxPlayer.HotbarStored();
                return true;
            }
        }
        return false;
    }

    bool TryDropOnSocket()
    {
        // requireInstallable: FALSE here, on purpose, and it is the whole reason this is a
        // separate call from the highlight above. If the drop used the strict test too, a
        // blocked socket would simply not be found - the part would drift back to the
        // hotbar with no warning popup and, in assessment, no recorded mistake. Finding it
        // loosely and letting TryReturnToSocket refuse it is what produces the explanation.
        var socket = FindSocketUnderCursor(requireInstallable: false);
        if (socket == null) return false;

        // TryReturnToSocket, not ReturnToSocket: the part gets the last word on whether
        // this socket is ready for it right now (CPU retention lock still closed, etc).
        // A refusal returns false, so ResolveDrop falls through to ReturnToOrigin() and
        // the part goes back to the hotbar instead of vanishing.
        // Only on success. A refused install has already played its own Blocked sound from
        // inside TryReturnToSocket, and a seated-part chime on top of a refusal would tell the
        // learner the opposite of what happened.
        bool seated = draggedItem.TryReturnToSocket(socket); // reactivates + tweens scale back to normal
        if (seated) SfxPlayer.PartSeated(draggedItem);
        return seated;
    }

    void ReturnToOrigin()
    {
        SfxPlayer.DropFailed();

        if (sourceSlot != null)
        {
            sourceSlot.SetItem(draggedItem);
        }
        else
        {
            var slot = HotbarManager.Instance.GetFirstEmptySlot();
            if (slot != null) slot.SetItem(draggedItem);
            // If the hotbar is full with nowhere to fall back to, the item stays
            // shrunk/hidden. Consider re-inserting into its original socket instead.
        }
    }
}