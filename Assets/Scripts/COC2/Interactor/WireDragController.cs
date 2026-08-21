using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Scene-local equivalent of DragController, scoped to this networking/crimping scene.
    /// Same overall shape as DragController (singleton, raycast + hover highlight, resolve
    /// on mouse-up) but with no hotbar step � wires just move directly in 3D world space
    /// while dragged, since watching the wire travel into the correct pin is the point.
    ///
    /// Add one of these to a GameObject in the crimping scene and assign worldCamera +
    /// slotLayerMask (create a dedicated "WireSlot" physics layer for WirePinSlot colliders,
    /// same idea as your existing socketLayerMask for PartSocket).
    /// </summary>
    public class WireDragController : MonoBehaviour
    {
        public static WireDragController Instance { get; private set; }

        public bool IsDragging => draggedWire != null;

        [Header("Refs")]
        public Camera worldCamera;
        [Tooltip("Layer(s) that WirePinSlot colliders live on.")]
        public LayerMask slotLayerMask;
        public float slotRaycastDistance = 100f;
        [Tooltip("Layer(s) that WireDraggable colliders live on. Defaults to Everything so this " +
                 "works with no extra setup; narrow it to a dedicated 'Wire' layer (same idea as " +
                 "slotLayerMask) if loose wires ever need to be excluded from other raycasts.")]
        public LayerMask wireLayerMask = ~0;
        public float wireRaycastDistance = 100f;

        WireDraggable draggedWire;
        WireIdentity draggedIdentity;
        Plane dragPlane;
        WirePinSlot hoveredSlot;

        void Awake()
        {
            Instance = this;
        }

        /// <summary>Starts dragging a wire. The wire keeps its current depth relative to the camera while dragged.</summary>
        public void BeginDrag(WireDraggable wire)
        {
            draggedWire = wire;
            draggedIdentity = wire.GetComponent<WireIdentity>();
            // Camera-facing plane through the wire's current position � moving the mouse
            // slides the wire around on this plane, keeping it at a consistent depth.
            dragPlane = new Plane(-worldCamera.transform.forward, wire.transform.position);
        }

        void Update()
        {
            // A dialog covering the screen (e.g. the "no tool yet" warning) shouldn't let a
            // stray tap underneath it start a new drag - same guard PlayerInteractor uses.
            if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return;

            if (draggedWire == null)
            {
                // Nothing being dragged yet: watch for the initial tap/click ourselves instead
                // of relying on WireDraggable.OnMouseDown(), which goes dead on mobile the
                // moment Input.simulateMouseWithTouches is off (see PointerInput.cs). This
                // mirrors PartClickDetector's raycast-on-PointerInput.Down pattern from COC1.
                if (PointerInput.Down)
                    TryBeginDragUnderPointer();
                return;
            }

            Ray ray = worldCamera.ScreenPointToRay(PointerInput.Position);
            if (dragPlane.Raycast(ray, out float distance))
            {
                draggedWire.transform.position = ray.GetPoint(distance);
            }

            UpdateHoverHighlight();

            if (PointerInput.Up)
                ResolveDrop();
        }

        /// <summary>Raycasts for a WireDraggable under the pointer (mouse or touch, via
        /// PointerInput) and hands off to its own TryBeginDrag() - which still applies the
        /// locked/no-tool/over-UI checks, so this is purely "what did the player tap on".</summary>
        void TryBeginDragUnderPointer()
        {
            Ray ray = worldCamera.ScreenPointToRay(PointerInput.Position);
            if (!Physics.Raycast(ray, out RaycastHit hit, wireRaycastDistance, wireLayerMask))
                return;

            WireDraggable wire = hit.collider.GetComponentInParent<WireDraggable>();
            wire?.TryBeginDrag();
        }

        void UpdateHoverHighlight()
        {
            WirePinSlot slot = FindValidSlotUnderCursor();
            if (slot == hoveredSlot) return; // no change since last frame

            if (hoveredSlot != null) hoveredSlot.SetHighlighted(false);
            hoveredSlot = slot;
            if (hoveredSlot != null) hoveredSlot.SetHighlighted(true);
        }

        void ClearHover()
        {
            if (hoveredSlot == null) return;
            hoveredSlot.SetHighlighted(false);
            hoveredSlot = null;
        }

        /// <summary>Raycasts for a WirePinSlot under the cursor that would actually accept the dragged wire right now.</summary>
        WirePinSlot FindValidSlotUnderCursor()
        {
            Ray ray = worldCamera.ScreenPointToRay(PointerInput.Position);
            if (!Physics.Raycast(ray, out RaycastHit hit, slotRaycastDistance, slotLayerMask))
                return null;

            var slot = hit.collider.GetComponentInParent<WirePinSlot>();
            if (slot == null || slot.IsFilled || slot.endNumber != draggedIdentity.endNumber)
                return null;

            return slot;
        }

        void ResolveDrop()
        {
            ClearHover();

            WirePinSlot slot = FindValidSlotUnderCursor();
            if (slot != null)
                draggedWire.SnapIntoSlot(slot);
            else
                draggedWire.ReturnHome();

            draggedWire = null;
            draggedIdentity = null;
        }
    }
}