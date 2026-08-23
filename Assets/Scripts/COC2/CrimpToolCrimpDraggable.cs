using UnityEngine;
using UnityEngine.EventSystems;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Crimping-stage twin of CrimpToolDraggable, and deliberately near-identical to it: the
    /// player grabs the crimping tool and drags it onto a connector end, and on release the
    /// baked crimp animation plays. Exactly the same feel as stripping, which is the point.
    ///
    /// The one difference: stripping has a single drop zone, crimping has two (one per connector
    /// end). Instead of a single anchor field, this asks CrimpToolCrimpController for its `ends`
    /// list and picks the nearest anchor within dropDistance.
    ///
    /// This lives on the SAME GameObject as CrimpToolDraggable (i.e.
    /// CrimpingViewController.crimpingToolVisual). Both components coexist; only one is enabled
    /// at a time - StrippingViewController enables the strip one, CrimpingViewController enables
    /// this one. Reads input through PointerInput so it works on mouse AND touch, same reasoning
    /// as CrimpToolDraggable (see Mobile/PointerInput.cs).
    ///
    /// Setup:
    ///   1. Add this to CrimpingViewController.crimpingToolVisual, alongside the existing
    ///      CrimpToolDraggable.
    ///   2. UNTICK its checkbox in the Inspector so it starts disabled - CrimpingViewController
    ///      turns it on when the crimping view opens.
    ///   3. Assign dragCamera = your crimpingCamera (NOT the stripping camera).
    ///   4. Optionally assign holsterAnchor - where the tool rests between the two crimps.
    ///      Leave empty to use wherever it sits in the scene.
    ///   5. Tune dropDistance against the spacing of your two connector ends: it must be
    ///      comfortably smaller than the distance between End 1's and End 2's anchors, or a
    ///      sloppy drop could trigger the wrong end.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class CrimpToolCrimpDraggable : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("Camera used while the tool is draggable during crimping - your crimpingCamera.")]
        public Camera dragCamera;

        [Header("Drop")]
        [Tooltip("How close (world units) the tool must be dragged to a CrimpEndTarget.toolDropAnchor " +
                 "to trigger that end's crimp. Keep this well under the distance between your two ends.")]
        public float dropDistance = 0.15f;
        public float raycastDistance = 100f;
        [Tooltip("Layers the pick-up raycast considers. Everything is fine - the hit still has to " +
                 "belong to this tool to count, so other objects on those layers are just ignored.")]
        public LayerMask pickLayerMask = ~0;

        [Header("Idle Pose")]
        [Tooltip("Where the tool rests between crimps, and where it snaps back to on an invalid drop. " +
                 "Leave empty to use wherever it starts in the scene.")]
        public Transform holsterAnchor;

        // Reused every pick test so hovering the tool doesn't allocate garbage each frame.
        readonly RaycastHit[] hitBuffer = new RaycastHit[16];
        Renderer[] renderers;

        bool isDragging;
        Vector3 dragOffset;
        Vector3 idlePosition;
        Quaternion idleRotation;

        void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            idlePosition = holsterAnchor != null ? holsterAnchor.position : transform.position;
            idleRotation = holsterAnchor != null ? holsterAnchor.rotation : transform.rotation;
        }

        void OnEnable()
        {
            // Same reset CrimpToolDraggable does: never come back mid-drag from a stage that
            // already ended, and always reappear at the holster pose.
            isDragging = false;
            transform.SetPositionAndRotation(idlePosition, idleRotation);
        }

        void Update()
        {
            if (dragCamera == null) return;

            var crimpController = CrimpToolCrimpController.Instance;

            // Not grabbable while a crimp clip is playing, or once both ends are done.
            if (crimpController != null && (crimpController.IsCrimping || crimpController.AllEndsCrimped()))
                return;

            if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return;

            // Don't fight the wire drag: if the player is mid-way through dragging a wire into a
            // pin, a stray press shouldn't also grab the tool. WireDragController owns the pointer
            // in that case.
            if (WireDragController.Instance != null && WireDragController.Instance.IsDragging) return;

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
            // Physics raycasts don't know about uGUI, so a tap on the mobile Crimp/Exit button can
            // also hit this collider underneath it. Parameterless IsPointerOverGameObject() only
            // checks the mouse, so touch needs the active finger's ID - same guard as
            // CrimpToolDraggable and WireDraggable.
            if (EventSystem.current != null)
            {
                bool overUI = Input.touchCount > 0
                    ? EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId)
                    : EventSystem.current.IsPointerOverGameObject();
                if (overUI) return false;
            }

            Ray ray = dragCamera.ScreenPointToRay(PointerInput.Position);

            // FIRST: test against what's actually on screen - the renderer bounds.
            //
            // This looks like the lazy option and is in fact the reliable one. Renderer.bounds is
            // derived straight from the Transform, so it is always exactly where the mesh is drawn.
            // A physics raycast is not: Unity only pushes moved Transforms into PhysX during
            // FixedUpdate (Physics.autoSyncTransforms defaults to FALSE), so any object repositioned
            // outside the physics step - like this one, which OnEnable() snaps to its holster pose -
            // has its collider left at the OLD pose until the next FixedUpdate. If anything has set
            // Time.timeScale to 0 (DialogManager does exactly that while a dialog is open), FixedUpdate
            // never runs and the collider never catches up at all. The mesh sits in plain sight and
            // every raycast at it sails into empty space.
            //
            // Picking off renderer bounds sidesteps that, and also stops caring about collider
            // trigger flags, layer masks, and whether the collider is on this object or a child.
            if (renderers == null || renderers.Length == 0)
                renderers = GetComponentsInChildren<Renderer>(true);

            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (renderer.bounds.IntersectRay(ray)) return true;
            }

            // SECOND: fall back to colliders, for a tool with no renderers of its own (an empty
            // parent driving a separately-parented mesh, say). QueryTriggerInteraction.Collide so a
            // trigger collider still registers regardless of the project's physics settings.
            int count = Physics.RaycastNonAlloc(ray, hitBuffer, raycastDistance, pickLayerMask,
                                                QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                Collider hit = hitBuffer[i].collider;
                if (hit == null) continue;
                if (hit.GetComponentInParent<CrimpToolCrimpDraggable>() == this) return true;
            }

            return false;
        }

        void BeginDrag()
        {
            isDragging = true;
            dragOffset = transform.position - RayHitOnDragPlane();
        }

        void EndDrag()
        {
            isDragging = false;

            int endNumber = FindEndUnderTool();
            if (endNumber > 0)
            {
                CrimpToolCrimpController.Instance.TryDropOnCrimpAnchor(endNumber);

                // If the drop was accepted, SnapAndPlay() now owns this transform and will
                // hide/reshow this object itself, so don't move it. If it was REFUSED (pins not
                // all seated, end already crimped), nothing took ownership - so put the tool back
                // rather than leaving it floating over the connector.
                if (CrimpToolCrimpController.Instance.IsCrimping) return;
            }

            transform.SetPositionAndRotation(idlePosition, idleRotation);
        }

        /// <summary>Returns the endNumber of the nearest un-crimped drop anchor within
        /// dropDistance, or 0 if the tool wasn't released near any of them.</summary>
        int FindEndUnderTool()
        {
            var controller = CrimpToolCrimpController.Instance;
            if (controller == null || controller.Ends == null) return 0;

            int bestEnd = 0;
            float bestDistance = dropDistance;

            foreach (var end in controller.Ends)
            {
                if (end == null || end.toolDropAnchor == null || end.done) continue;

                float distance = Vector3.Distance(transform.position, end.toolDropAnchor.position);
                if (distance <= bestDistance)
                {
                    bestDistance = distance;
                    bestEnd = end.endNumber;
                }
            }

            return bestEnd;
        }

        // Drags along a camera-facing plane through the tool's current position - identical to
        // CrimpToolDraggable.RayHitOnDragPlane().
        Vector3 RayHitOnDragPlane()
        {
            Plane dragPlane = new Plane(dragCamera.transform.forward, transform.position);
            Ray ray = dragCamera.ScreenPointToRay(PointerInput.Position);
            return dragPlane.Raycast(ray, out float enter) ? ray.GetPoint(enter) : transform.position;
        }
    }
}