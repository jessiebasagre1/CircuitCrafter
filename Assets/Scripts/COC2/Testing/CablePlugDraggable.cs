using UnityEngine;
using UnityEngine.EventSystems;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Lets the player pick up one crimped cable end and drag it into the tester's jack. Same
    /// drag feel as CrimpToolDraggable (stripping) and CrimpToolCrimpDraggable (crimping), on
    /// purpose - by this point the gesture should be second nature to the player.
    ///
    /// Put one on each cable end. Set endNumber to 1 or 2 to match the CableTesterPlugController
    /// jack it belongs to.
    ///
    /// Setup (twice - once per end):
    ///   1. Add this to the object the player should drag: the RJ45 housing for this end, or a
    ///      proxy plug prop next to the tester (see CableTesterPlugController's notes on which
    ///      to use).
    ///   2. Make sure that object has its OWN Collider - the WirePinSlot trigger colliders on its
    ///      children don't count, this raycasts against this component's collider specifically.
    ///   3. Set endNumber (1 or 2).
    ///   4. Assign dragCamera = your testingCamera.
    ///   5. UNTICK its checkbox so it starts disabled - TestingViewController enables it when the
    ///      testing view opens.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class CablePlugDraggable : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Which cable end this is: 1 or 2. Must match a jack's endNumber on CableTesterPlugController.")]
        [Range(1, 2)] public int endNumber = 1;

        [Header("Refs")]
        [Tooltip("Camera used while this is draggable - your testingCamera.")]
        public Camera dragCamera;

        [Header("Drop")]
        [Tooltip("How close (world units) the plug must be dragged to its jack's plugDropAnchor to seat. " +
                 "Keep it comfortably smaller than the gap between the two jacks.")]
        public float dropDistance = 0.12f;
        public float raycastDistance = 100f;

        [Header("Idle Pose")]
        [Tooltip("Where this plug rests before being picked up, and where it snaps back to on a bad drop. " +
                 "Leave empty to use wherever it sits when this component first wakes.")]
        public Transform restAnchor;

        Collider myCollider;
        bool isDragging;
        Vector3 dragOffset;
        Vector3 idlePosition;
        Quaternion idleRotation;

        void Awake()
        {
            myCollider = GetComponent<Collider>();
            idlePosition = restAnchor != null ? restAnchor.position : transform.position;
            idleRotation = restAnchor != null ? restAnchor.rotation : transform.rotation;
        }

        void OnEnable()
        {
            isDragging = false;
        }

        void Update()
        {
            if (dragCamera == null) return;

            var plugs = CableTesterPlugController.Instance;
            if (plugs != null && (plugs.IsSeatingPlug || plugs.IsPlugSeated(endNumber))) return;

            var tester = CableTesterController.Instance;
            if (tester != null && tester.IsTesting) return;

            if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return;

            if (!isDragging)
            {
                if (PointerInput.Down && IsPointerOverThisPlug())
                    BeginDrag();
                return;
            }

            transform.position = RayHitOnDragPlane() + dragOffset;

            if (PointerInput.Up)
                EndDrag();
        }

        bool IsPointerOverThisPlug()
        {
            // Same over-UI guard as every other draggable here: a tap on the mobile Test/Exit
            // button can also raycast-hit this collider underneath it, and the parameterless
            // IsPointerOverGameObject() only checks the mouse, not the finger.
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

            var plugs = CableTesterPlugController.Instance;
            if (plugs != null)
            {
                var jack = FindMyJack(plugs);
                if (jack != null && jack.plugDropAnchor != null &&
                    Vector3.Distance(transform.position, jack.plugDropAnchor.position) <= dropDistance)
                {
                    plugs.TryDropOnJack(endNumber);

                    // If the drop was accepted, SeatPlug() now owns this transform. If it was
                    // refused (end not crimped), nothing took ownership - fall through and rest.
                    if (plugs.IsSeatingPlug || plugs.IsPlugSeated(endNumber)) return;
                }
            }

            transform.SetPositionAndRotation(idlePosition, idleRotation);
        }

        /// <summary>Puts the plug back at its rest pose and makes it draggable again. Called by
        /// CableTesterPlugController.ResetPlugs().</summary>
        public void ResetToHome()
        {
            isDragging = false;
            enabled = true;
            transform.SetPositionAndRotation(idlePosition, idleRotation);
        }

        CableTesterPlugController.TesterJack FindMyJack(CableTesterPlugController plugs)
        {
            if (plugs.Jacks == null) return null;
            foreach (var jack in plugs.Jacks)
            {
                if (jack != null && jack.endNumber == endNumber) return jack;
            }
            return null;
        }

        Vector3 RayHitOnDragPlane()
        {
            Plane dragPlane = new Plane(dragCamera.transform.forward, transform.position);
            Ray ray = dragCamera.ScreenPointToRay(PointerInput.Position);
            return dragPlane.Raycast(ray, out float enter) ? ray.GetPoint(enter) : transform.position;
        }
    }
}
