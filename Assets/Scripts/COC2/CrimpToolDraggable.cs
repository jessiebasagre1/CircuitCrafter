using UnityEngine;
using UnityEngine.EventSystems;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Lets the player pick up the crimping tool and drag it onto the cable to strip it.
    /// This is the same GameObject as CrimpingViewController.crimpingToolVisual - it's used
    /// both here (stripping) and afterward for the final crimp click - so it stays in the
    /// scene the whole time the player has the tool, just enabled/disabled depending on stage.
    ///
    /// Once the tool is dragged within dropDistance of CrimpToolStripController.ToolIdleAnchor,
    /// this hands off to TryDropOnStripSocket(), which snap-lerps it the rest of the way and
    /// plays CrimpTool_Strip_UTP. ToolIdleAnchor doubles as the "drop zone" here rather than a
    /// separate socket component, since it's already the exact pose the baked clip expects.
    ///
    /// Self-contained (no separate drag-controller singleton like WireDragController) - there's
    /// only ever one crimping tool active at a time, so a raycast against this object's own
    /// collider is enough. Reads input via PointerInput (mouse OR touch) from the start, same
    /// reasoning as the WireDraggable/WireDragController mobile fix: OnMouseDown alone goes
    /// dead once Input.simulateMouseWithTouches is off (see Mobile/PointerInput.cs).
    ///
    /// Setup:
    ///   1. Attach to the same GameObject as CrimpingViewController.crimpingToolVisual.
    ///   2. Assign dragCamera - the stripping-stage camera (StrippingViewController.strippingCamera
    ///      once that exists; the crimping close-up camera works too if you haven't split them out yet).
    ///   3. Optionally assign holsterAnchor for where the tool rests before being picked up and
    ///      where it snaps back to on a failed/short drop. Leave empty to just use its starting
    ///      transform in the scene.
    ///   4. In CrimpToolStripController's Inspector, set draggedToolTransform to point at THIS
    ///      object's transform.
    ///   5. Only enable this component while the player is actually in the stripping stage -
    ///      the same way CrimpingViewController already toggles WireDragController.enabled.
    ///      Left enabled outside that stage, the tool would be draggable while just standing
    ///      at the station looking at the crimping (pin-arranging) view.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class CrimpToolDraggable : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("Camera used while the tool is draggable (the stripping-stage camera).")]
        public Camera dragCamera;

        [Header("Drop")]
        [Tooltip("How close (world units) the tool must be dragged to CrimpToolStripController.ToolIdleAnchor to trigger the strip.")]
        public float dropDistance = 0.15f;
        public float raycastDistance = 100f;

        [Header("Idle Pose")]
        [Tooltip("Where this tool rests before being picked up, and where it snaps back to if dropped somewhere invalid. Leave empty to use wherever it starts in the scene.")]
        public Transform holsterAnchor;

        Collider myCollider;
        bool isDragging;
        Vector3 dragOffset;
        Vector3 idlePosition;
        Quaternion idleRotation;

        void Awake()
        {
            myCollider = GetComponent<Collider>();
            idlePosition = holsterAnchor != null ? holsterAnchor.position : transform.position;
            idleRotation = holsterAnchor != null ? holsterAnchor.rotation : transform.rotation;
        }

        void OnEnable()
        {
            // Every time this pops back into view (CrimpingViewController.SetCrimpingViewActive
            // reactivates crimpingToolVisual), make sure it's not still mid-drag from a stage
            // that already ended, and start it back at its holster position.
            isDragging = false;
            transform.SetPositionAndRotation(idlePosition, idleRotation);
        }

        void Update()
        {
            if (dragCamera == null) return;

            // Already stripping (animation playing) or done - not draggable anymore. Normally
            // StrippingViewController disables this component once the stage ends; this is a
            // belt-and-suspenders guard in case that hasn't happened yet on the exact frame
            // the animation starts.
            var stripController = CrimpToolStripController.Instance;
            if (stripController != null && (stripController.IsStripping || stripController.IsComplete))
                return;

            if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return;

            if (!isDragging)
            {
                if (PointerInput.Down && IsPointerOverThisTool())
                    BeginDrag();
                return;
            }

            transform.position = RayHitOnDragPlane() + dragOffset;

            if (PointerInput.Up)
                EndDrag();
        }

        bool IsPointerOverThisTool()
        {
            // Same reasoning as WireDraggable's over-UI guard: a mobile button tap can
            // simultaneously raycast-hit this collider underneath it, since physics raycasts
            // don't know about uGUI. The parameterless IsPointerOverGameObject() only checks
            // the mouse pointer, so it needs the active finger's ID on touch.
            if (EventSystem.current != null)
            {
                bool overUI = Input.touchCount > 0
                    ? EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId)
                    : EventSystem.current.IsPointerOverGameObject();
                if (overUI) return false;
            }

            Ray ray = dragCamera.ScreenPointToRay(PointerInput.Position);
            return myCollider.Raycast(ray, out _, raycastDistance);
        }

        void BeginDrag()
        {
            isDragging = true;
            dragOffset = transform.position - RayHitOnDragPlane();
        }

        void EndDrag()
        {
            isDragging = false;

            var stripController = CrimpToolStripController.Instance;
            Transform anchor = stripController != null ? stripController.ToolIdleAnchor : null;

            if (stripController != null && anchor != null &&
                Vector3.Distance(transform.position, anchor.position) <= dropDistance)
            {
                stripController.TryDropOnStripSocket();
                // From here CrimpToolStripController's SnapAndPlay() coroutine takes over this
                // same transform (draggedToolTransform) and eases it the rest of the way in,
                // so we deliberately don't move/reset it ourselves after this point.
            }
            else
            {
                transform.SetPositionAndRotation(idlePosition, idleRotation);
            }
        }

        // Drags along a plane facing the camera, passing through the tool's current position -
        // same approach as CableConnector.RayHitOnDragPlane() in COC1.
        Vector3 RayHitOnDragPlane()
        {
            Plane dragPlane = new Plane(dragCamera.transform.forward, transform.position);
            Ray ray = dragCamera.ScreenPointToRay(PointerInput.Position);
            return dragPlane.Raycast(ray, out float enter) ? ray.GetPoint(enter) : transform.position;
        }
    }
}