// PatchCord.cs
using UnityEngine;
using UnityEngine.EventSystems;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// The draggable RJ45 plug on the end of a patch cord. Pick it up, drag it, and the cord bends
    /// and sags behind it; release it near a jack and it snaps in.
    ///
    /// The bending is COC1's VerletRope, reused UNCHANGED - it has no cable-type dependency and no
    /// COC1-specific logic, it just simulates a line between two Transforms and builds a tube mesh.
    /// There was no reason to write a second one. This script is the COC2 replacement for
    /// CableConnector, and it differs in three ways that all come from bugs already paid for:
    ///
    ///   1. PointerInput, not OnMouseDown/OnMouseUp. Those legacy callbacks go dead on mobile the
    ///      moment anything sets Input.simulateMouseWithTouches = false - the exact bug that broke
    ///      dragging in COC1 before PartClickDetector fixed it.
    ///   2. Renderer bounds, not a collider raycast, for the pick test. Unity only pushes a moved
    ///      Transform into PhysX during FixedUpdate, so a collider on something repositioned outside
    ///      that step sits at a stale pose and every ray misses it while the mesh is on screen.
    ///   3. Sockets come from RJ45Socket.All, not Physics.OverlapSphere. No socket colliders, no
    ///      layer mask to get wrong, and immune to the same stale-pose problem.
    ///
    /// Setup:
    ///   1. Put this on the PatchCordPlug object (its pivot is the mating face, so it lines up with
    ///      a Socket_* anchor just by matching transforms).
    ///   2. Assign dragCamera - whichever camera renders the view the player patches in.
    ///   3. Assign rope - the VerletRope for this cord. Its startAnchor is the far end; set its
    ///      endAnchor to this plug's PatchCordPlug_RopeAnchor child.
    ///   4. Optionally set startSocket + startPlugged so the cord begins patched.
    /// </summary>
    public class PatchCord : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("Camera rendering the view this cord is dragged in. Assign it explicitly - " +
                 "Camera.main finds whichever camera happens to be tagged MainCamera, which on a " +
                 "multi-camera station is rarely the one actually rendering.")]
        public Camera dragCamera;

        [Tooltip("The VerletRope that draws this cord. Leave empty for a plug with no cord body.")]
        public VerletRope rope;

        [Header("Snap")]
        [Tooltip("How close (world units) the plug must be released to a socket to seat. 0.03 suits " +
                 "a board where the ports are 20 mm apart.")]
        public float snapDistance = 0.030f;
        [Tooltip("How closely aligned (degrees) with the socket the plug must be. Every socket on " +
                 "the shipped board faces the same way, so this rarely rejects anything.")]
        public float snapAngle = 45f;
        [Tooltip("Tick to snap on distance alone. Worth using on touch, where fine rotation control " +
                 "isn't available.")]
        public bool ignoreAngle = true;

        [Header("Feel")]
        [Tooltip("Where the plug hangs when it isn't patched. Leave empty to leave it wherever it " +
                 "was dropped, which lets the rope's own gravity do the work.")]
        public Transform homeAnchor;
        [Tooltip("Seconds the plug takes to ease into a socket. 0 for an instant snap.")]
        public float snapDuration = 0.10f;
        [Tooltip("How far the plug must travel away from a socket it was just pulled out of before " +
                 "it may re-seat. Stops a click-without-drag instantly plugging back in.")]
        public float minDetachDistance = 0.035f;

        [Header("Start state")]
        public RJ45Socket startSocket;
        public bool startPlugged;
        [Tooltip("Tick for a cord the player must never unplug - e.g. a permanently patched uplink.")]
        public bool isLocked;

        [Header("Pick")]
        [Tooltip("Grow the plug's pick area by this many world units. The plug is only 12 mm across, " +
                 "so a little padding makes it far easier to grab, especially on touch.")]
        public float pickPadding = 0.006f;

        public bool IsPlugged { get; private set; }
        public bool IsDragging { get; private set; }
        public RJ45Socket PluggedSocket { get; private set; }

        /// <summary>Fires whenever this cord seats or leaves a socket.</summary>
        public System.Action<RJ45Socket, bool> OnConnectionChanged;

        Renderer[] renderers;
        Vector3 dragOffset;
        RJ45Socket highlighted;
        Vector3 detachOrigin;
        bool justDetached;
        Coroutine snapRoutine;

        void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
        }

        void Start()
        {
            if (startPlugged && startSocket != null) Plug(startSocket, instant: true);
            else if (homeAnchor != null)
                transform.SetPositionAndRotation(homeAnchor.position, homeAnchor.rotation);
        }

        void Update()
        {
            if (dragCamera == null || isLocked) return;
            if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return;

            if (!IsDragging)
            {
                if (PointerInput.Down && PointerOverPlug()) BeginDrag();
                return;
            }

            transform.position = RayHitOnDragPlane() + dragOffset;
            UpdateHighlight();

            if (PointerInput.Up) EndDrag();
        }

        // ------------------------------------------------------------------

        bool PointerOverPlug()
        {
            // A tap on a mobile button can also hit the plug underneath it, since none of this knows
            // about uGUI. The parameterless IsPointerOverGameObject() only checks the mouse, so
            // touch needs the active finger's id.
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
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                Bounds b = r.bounds;
                b.Expand(pickPadding);
                if (b.IntersectRay(ray)) return true;
            }
            return false;
        }

        void BeginDrag()
        {
            if (snapRoutine != null) { StopCoroutine(snapRoutine); snapRoutine = null; }

            if (IsPlugged)
            {
                detachOrigin = transform.position;
                justDetached = true;
                Unplug();
            }

            IsDragging = true;
            dragOffset = transform.position - RayHitOnDragPlane();
        }

        void EndDrag()
        {
            IsDragging = false;
            ClearHighlight();

            RJ45Socket target = FindTarget();
            if (target != null) { Plug(target, instant: false); return; }

            // Nothing in range: leave it where it was dropped and let the rope hang. That is what
            // CableConnector does too, and it reads better than teleporting the plug away.
            if (homeAnchor != null && !IsPlugged)
                transform.SetPositionAndRotation(homeAnchor.position, homeAnchor.rotation);
        }

        /// <summary>Nearest free socket that would actually accept this plug right now.</summary>
        RJ45Socket FindTarget()
        {
            if (justDetached)
            {
                justDetached = false;
                if (Vector3.Distance(transform.position, detachOrigin) < minDetachDistance)
                    return null;         // barely moved - treat as a mis-click, not a re-patch
            }

            RJ45Socket s = RJ45Socket.NearestFree(transform.position, snapDistance);
            if (s == null) return null;
            if (ignoreAngle) return s;
            return Quaternion.Angle(transform.rotation, s.Snap.rotation) <= snapAngle ? s : null;
        }

        void UpdateHighlight()
        {
            RJ45Socket s = RJ45Socket.NearestFree(transform.position, snapDistance);
            if (s == highlighted) return;
            ClearHighlight();
            highlighted = s;
            if (highlighted != null) highlighted.SetHighlighted(true);
        }

        void ClearHighlight()
        {
            if (highlighted != null) highlighted.SetHighlighted(false);
            highlighted = null;
        }

        // ------------------------------------------------------------------

        /// <summary>Seats the plug. Also callable directly to patch a cord from script.</summary>
        public void Plug(RJ45Socket socket, bool instant)
        {
            if (socket == null || socket.IsOccupied) return;

            IsPlugged = true;
            IsDragging = false;
            PluggedSocket = socket;
            socket.SetOccupied(this, true);
            socket.SetHighlighted(false);

            if (instant || snapDuration <= 0f || !gameObject.activeInHierarchy)
                transform.SetPositionAndRotation(socket.Snap.position, socket.Snap.rotation);
            else
                snapRoutine = StartCoroutine(EaseInto(socket.Snap));

            OnConnectionChanged?.Invoke(socket, true);
        }

        System.Collections.IEnumerator EaseInto(Transform target)
        {
            Vector3 p0 = transform.position;
            Quaternion r0 = transform.rotation;
            float t = 0f;
            while (t < snapDuration)
            {
                t += Time.deltaTime;
                float f = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / snapDuration));
                transform.position = Vector3.Lerp(p0, target.position, f);
                transform.rotation = Quaternion.Slerp(r0, target.rotation, f);
                yield return null;
            }
            transform.SetPositionAndRotation(target.position, target.rotation);
            snapRoutine = null;
        }

        /// <summary>Pulls the plug out of whatever it's in.</summary>
        public void Unplug()
        {
            if (!IsPlugged) return;
            RJ45Socket was = PluggedSocket;
            if (was != null) was.SetOccupied(this, false);
            IsPlugged = false;
            PluggedSocket = null;
            OnConnectionChanged?.Invoke(was, false);
        }

        /// <summary>Call after moving the whole cord a long way - the rope sizes its segment count
        /// from the anchor distance it saw at Start, so a big jump needs a rebuild.</summary>
        public void RebuildRope()
        {
            if (rope != null) rope.ForceRebuild();
        }

        // Camera-facing plane through the plug's current position - the same drag model
        // CableConnector uses, reading PointerInput instead of Input.mousePosition.
        Vector3 RayHitOnDragPlane()
        {
            Plane dragPlane = new Plane(dragCamera.transform.forward, transform.position);
            Ray ray = dragCamera.ScreenPointToRay(PointerInput.Position);
            return dragPlane.Raycast(ray, out float enter) ? ray.GetPoint(enter) : transform.position;
        }
    }
}
