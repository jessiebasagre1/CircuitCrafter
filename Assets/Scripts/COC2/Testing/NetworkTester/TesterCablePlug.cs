// TesterCablePlug.cs
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// The draggable RJ45 end of the cable at the network tester. Same interaction as COC1's
    /// CableConnector: pick the plug up, drag it toward the socket, release, and it snaps into the
    /// socket's exact position and rotation.
    ///
    /// Two deliberate differences from CableConnector, both learned the hard way in COC2:
    ///
    ///   1. Input goes through PointerInput (mouse OR touch) instead of OnMouseDown/OnMouseUp.
    ///      Those legacy callbacks go dead on mobile the moment anything sets
    ///      Input.simulateMouseWithTouches = false - see Mobile/PointerInput.cs.
    ///   2. Picking up the plug tests the RENDERER BOUNDS, not a collider. Unity only pushes moved
    ///      Transforms into the physics engine during FixedUpdate, so a collider on something that
    ///      gets repositioned outside that step sits at a stale position and every raycast at it
    ///      misses - while the mesh is right there on screen. Renderer bounds come straight off the
    ///      Transform and can't drift.
    ///
    /// There's also no separate socket component: with a single jack, a snapPoint Transform and a
    /// distance check do the whole job, and there's no OverlapSphere to be caught out by physics
    /// sync either.
    ///
    /// Setup:
    ///   1. Put this on the RJ45 connector object at the tester - the part the player grabs. It
    ///      should be a CHILD of NetworkTestStation.stationCableVisual so it appears with the cable.
    ///   2. Create an empty GameObject inside the tester's socket, positioned and rotated exactly
    ///      how the plug should sit when fully seated. Name it "TesterSnapPoint" and assign it to
    ///      snapPoint.
    ///   3. Create another empty where the plug should rest before being dragged, name it
    ///      "PlugHome", and assign it to homeAnchor.
    ///   4. Assign dragCamera = your tester camera.
    ///   5. UNTICK this component - NetworkTestStation enables it when the view opens.
    ///   6. Assign this component to NetworkTestStation > Cable Plug.
    /// </summary>
    public class TesterCablePlug : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("The camera rendering the tester close-up.")]
        public Camera dragCamera;

        [Tooltip("Empty GameObject at the exact position AND rotation the plug sits at when fully " +
                 "seated in the tester's socket.")]
        public Transform snapPoint;

        [Header("Snap")]
        [Tooltip("How close (world units) the plug must be released to snapPoint to seat. Match this to " +
                 "the scale of your scene - 0.05 suits a metre-scale bench.")]
        public float snapDistance = 0.05f;

        [Tooltip("How closely aligned (degrees) the plug must be with snapPoint to seat. Same idea as " +
                 "CableConnector.snapAngle.")]
        public float snapAngle = 25f;

        [Tooltip("Tick to ignore rotation entirely and snap on distance alone. Easier for the player, " +
                 "and worth using if your plug's pivot isn't aligned the way you expect.")]
        public bool ignoreAngle = false;

        [Header("Feel")]
        [Tooltip("Where the plug rests before being dragged, and where it returns to on a missed drop. " +
                 "Leave empty to use wherever it sits in the scene.")]
        public Transform homeAnchor;

        [Tooltip("Seconds the plug takes to ease into the socket once it seats. 0 for an instant snap, " +
                 "the way CableConnector does it.")]
        public float snapDuration = 0.12f;

        [Tooltip("Untick to leave the plug wherever it was dropped instead of springing back.")]
        public bool returnHomeIfMissed = true;

        [Header("Highlight (optional)")]
        [Tooltip("Shown while the plug is close enough that releasing would seat it - an outline, a glow, " +
                 "an arrow. Same role as PortSocket.SetHighlighted() in COC1.")]
        public GameObject highlightVisual;

        [Header("Parenting")]
        [Tooltip("Tick to parent the plug under snapPoint once seated, so it follows the tester if the " +
                 "tester itself animates. Leave off unless you need that - it moves the plug out of the " +
                 "cable's hierarchy, so hiding the cable would no longer hide the plug.")]
        public bool parentToSnapPoint = false;

        /// <summary>True once the plug is seated in the socket.</summary>
        public bool IsPlugged { get; private set; }

        /// <summary>True while the player is holding the plug.</summary>
        public bool IsDragging { get; private set; }

        /// <summary>Fires the moment the plug seats. NetworkTestStation listens for this and starts the test.</summary>
        public System.Action OnPlugged;

        Renderer[] renderers;
        Vector3 dragOffset;
        Vector3 homePosition;
        Quaternion homeRotation;
        Transform originalParent;
        Coroutine snapRoutine;
        bool homeCaptured;

        void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            originalParent = transform.parent;
            CaptureHome();
            SetHighlight(false);
        }

        void CaptureHome()
        {
            if (homeCaptured) return;
            homePosition = homeAnchor != null ? homeAnchor.position : transform.position;
            homeRotation = homeAnchor != null ? homeAnchor.rotation : transform.rotation;
            homeCaptured = true;
        }

        void OnEnable()
        {
            CaptureHome();
            IsDragging = false;

            // Don't yank a seated plug back out just because the view reopened.
            if (!IsPlugged) transform.SetPositionAndRotation(homePosition, homeRotation);
        }

        void Update()
        {
            if (IsPlugged || dragCamera == null) return;

            if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return;

            if (!IsDragging)
            {
                if (PointerInput.Down && PointerOverPlug()) BeginDrag();
                return;
            }

            transform.position = RayHitOnDragPlane() + dragOffset;
            SetHighlight(WithinSnapRange());

            if (PointerInput.Up) EndDrag();
        }

        // ------------------------------------------------------------------

        bool PointerOverPlug()
        {
            // A tap on a mobile button can also raycast-hit the plug underneath it, since none of
            // this knows about uGUI. The parameterless IsPointerOverGameObject() only checks the
            // mouse, so touch needs the active finger's id.
            if (EventSystem.current != null)
            {
                bool overUI = Input.touchCount > 0
                    ? EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId)
                    : EventSystem.current.IsPointerOverGameObject();
                if (overUI) return false;
            }

            if (renderers == null || renderers.Length == 0)
                renderers = GetComponentsInChildren<Renderer>(true);

            Ray ray = dragCamera.ScreenPointToRay(PointerInput.Position);
            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (renderer.bounds.IntersectRay(ray)) return true;
            }
            return false;
        }

        void BeginDrag()
        {
            IsDragging = true;
            if (snapRoutine != null) { StopCoroutine(snapRoutine); snapRoutine = null; }
            dragOffset = transform.position - RayHitOnDragPlane();
        }

        void EndDrag()
        {
            IsDragging = false;
            SetHighlight(false);

            if (WithinSnapRange())
            {
                Plug();
                return;
            }

            if (returnHomeIfMissed)
                transform.SetPositionAndRotation(homePosition, homeRotation);
        }

        /// <summary>Distance AND angle test, same pair of conditions CableConnector.TryAutoPlug() uses.</summary>
        bool WithinSnapRange()
        {
            if (snapPoint == null) return false;

            if (Vector3.Distance(transform.position, snapPoint.position) > snapDistance) return false;
            if (ignoreAngle) return true;

            return Quaternion.Angle(transform.rotation, snapPoint.rotation) <= snapAngle;
        }

        // Camera-facing plane through the plug's current position - identical to
        // CableConnector.RayHitOnDragPlane(), just reading PointerInput instead of Input.mousePosition.
        Vector3 RayHitOnDragPlane()
        {
            Plane dragPlane = new Plane(dragCamera.transform.forward, transform.position);
            Ray ray = dragCamera.ScreenPointToRay(PointerInput.Position);
            return dragPlane.Raycast(ray, out float enter) ? ray.GetPoint(enter) : transform.position;
        }

        void SetHighlight(bool on)
        {
            if (highlightVisual != null && highlightVisual.activeSelf != on)
                highlightVisual.SetActive(on);
        }

        // ------------------------------------------------------------------

        /// <summary>Seats the plug. Also callable directly if you ever want to skip the drag.</summary>
        public void Plug()
        {
            if (IsPlugged || snapPoint == null) return;

            IsPlugged = true;
            IsDragging = false;
            SetHighlight(false);

            if (parentToSnapPoint) transform.SetParent(snapPoint, true);

            if (snapDuration > 0f && gameObject.activeInHierarchy)
                snapRoutine = StartCoroutine(EaseIntoSocket());
            else
                transform.SetPositionAndRotation(snapPoint.position, snapPoint.rotation);

            Debug.Log("[TesterCablePlug] Cable plugged into the tester.", this);
            OnPlugged?.Invoke();
        }

        IEnumerator EaseIntoSocket()
        {
            Vector3 startPos = transform.position;
            Quaternion startRot = transform.rotation;
            float t = 0f;

            while (t < snapDuration)
            {
                t += Time.deltaTime;
                float f = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / snapDuration));
                transform.position = Vector3.Lerp(startPos, snapPoint.position, f);
                transform.rotation = Quaternion.Slerp(startRot, snapPoint.rotation, f);
                yield return null;
            }

            transform.SetPositionAndRotation(snapPoint.position, snapPoint.rotation);
            snapRoutine = null;
        }

        /// <summary>Pulls the plug back out and returns it home. Wire to a Retry button, or let
        /// NetworkTestStation.ResetStation() call it.</summary>
        public void Unplug()
        {
            if (snapRoutine != null) { StopCoroutine(snapRoutine); snapRoutine = null; }

            IsPlugged = false;
            IsDragging = false;
            SetHighlight(false);

            if (parentToSnapPoint) transform.SetParent(originalParent, true);

            transform.SetPositionAndRotation(homePosition, homeRotation);
        }
    }
}
