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
        PartSocket socket = FindValidSocketUnderCursor();
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

    /// <summary>Raycasts for a socket under the cursor that would actually accept the dragged item right now.</summary>
    PartSocket FindValidSocketUnderCursor()
    {
        Ray ray = worldCamera.ScreenPointToRay(PointerInput.Position);
        if (!Physics.Raycast(ray, out RaycastHit hit, socketRaycastDistance, socketLayerMask))
            return null;

        var socket = hit.collider.GetComponentInParent<PartSocket>();
        if (socket == null || !socket.IsEmpty || socket.acceptedType != draggedItem.partType)
            return null;

        return socket;
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
                return true;
            }
        }
        return false;
    }

    bool TryDropOnSocket()
    {
        var socket = FindValidSocketUnderCursor();
        if (socket == null) return false;

        draggedItem.ReturnToSocket(socket); // reactivates + tweens scale back to normal
        return true;
    }

    void ReturnToOrigin()
    {
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