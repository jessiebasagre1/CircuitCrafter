// PatchCordPlug.cs
using UnityEngine;
using UnityEngine.EventSystems;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// The free end of a patch cord - the RJ45 plug the player drags from the panel down into the
    /// switch.
    ///
    /// This is PunchStageConductor's twin, and for the same three hard-won reasons:
    ///
    ///   1. PointerInput, not OnMouseDown/OnMouseDrag - those go dead on mobile the moment anything
    ///      sets Input.simulateMouseWithTouches = false.
    ///   2. Renderer bounds, not a collider raycast - nothing to go stale, no layer mask to get
    ///      wrong, nothing to set up.
    ///   3. The board is asked before a drag starts, so two plugs whose bounds overlap can never
    ///      both be picked up by one press.
    ///
    /// Setup:
    ///   1. Put this on PatchCord_XX_Plug_B - the free plug, NOT the one already in the panel.
    ///   2. Set Port Number to the panel port that cord comes out of.
    ///   3. That's all. The board finds it and hands it the camera.
    /// </summary>
    [DisallowMultipleComponent]
    public class PatchCordPlug : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Which panel port this cord runs from. It will only be accepted in the switch " +
                 "socket with the same number.")]
        [Range(1, 24)] public int portNumber = 1;

        [Header("Picking")]
        [Tooltip("Renderers used for the pick test. Leave empty to use every renderer under this " +
                 "object.")]
        public Renderer[] pickRenderers;

        [Tooltip("Grows the pick area by this many world units, so a small plug is still easy to " +
                 "grab on a phone.")]
        public float pickPadding = 0.006f;

        [Tooltip("Multiplies Pick Padding while the input is a TOUCH. A mouse cursor is a point; a " +
                 "fingertip covers several millimetres and hides what is under it. 2 is about right.")]
        public float touchPickBoost = 2f;

        [Header("Feel")]
        [Tooltip("Seconds the plug takes to ease into the socket. 0 for an instant snap.")]
        public float seatDuration = 0.12f;

        public bool IsDragging { get; private set; }
        /// <summary>True once seated in a socket.</summary>
        public bool IsSeated { get; private set; }
        /// <summary>True once the board has locked it in. A locked plug cannot be picked up again.</summary>
        public bool IsLocked { get; private set; }

        /// <summary>The socket this plug ended up in, or null.</summary>
        public PatchCordSocket SeatedIn { get; private set; }

        PatchCordBoard board;
        Renderer[] renderers;
        Vector3 homePosition;
        Quaternion homeRotation;
        Vector3 dragOffset;
        Coroutine seatRoutine;
        bool captured;

        /// <summary>
        /// Records where this plug hangs before anyone touches it. Same reasoning as
        /// PunchStageConductor.Capture(): if the stage set is ever switched off at Awake, nothing
        /// under it runs Awake until the stage first opens - while the board may already have called
        /// ReturnHome(). With the pose unrecorded that would fling every plug to the world origin.
        /// </summary>
        void Capture()
        {
            if (captured) return;
            captured = true;
            homePosition = transform.position;
            homeRotation = transform.rotation;
        }

        void OnEnable()
        {
            Capture();
        }

        void Awake()
        {
            Capture();

            if (pickRenderers == null || pickRenderers.Length == 0)
                pickRenderers = GetComponentsInChildren<Renderer>(true);
            renderers = pickRenderers;
        }

        /// <summary>Records the CURRENT pose as home, replacing whatever was captured before.</summary>
        public void RecaptureHome()
        {
            captured = true;
            homePosition = transform.position;
            homeRotation = transform.rotation;
        }

        /// <summary>The board calls this on itself, so a plug never has to be wired by hand.</summary>
        public void Bind(PatchCordBoard owner)
        {
            board = owner;
            Capture();
        }

        /// <summary>Points the pick test at renderers that live on another object - needed if you
        /// ever drive the cord with a runtime-built rope the way the conductors are.</summary>
        public void SetPickRenderers(Renderer[] rs)
        {
            pickRenderers = rs;
            renderers = rs;
        }

        // ------------------------------------------------------------------

        void Update()
        {
            if (board == null || IsLocked) return;
            if (board.StageCamera == null || !board.AcceptsInput) return;
            if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return;

            if (!IsDragging)
            {
                // The board decides which plug a press meant - see PatchCordBoard.PlugForPress.
                // Letting each plug claim for itself makes the winner depend on component order,
                // which is arbitrary, so a touch between two cords picks up an unpredictable one.
                if (PointerInput.Down &&
                    board.PlugForPress(PointerInput.Position) == this &&
                    board.TryClaimDrag(this))
                    BeginDrag();
                return;
            }

            transform.position = RayHitOnDragPlane() + dragOffset;
            board.HighlightForPlug(this);

            if (PointerInput.Up) EndDrag();
        }

        /// <summary>Is this plug a CANDIDATE for a press here? Coarse - several plugs can say yes to
        /// the same press, and the board picks between them with GrabScore.</summary>
        public bool IsPointerOverBounds(Vector2 screenPoint)
        {
            if (board == null || board.StageCamera == null) return false;

            if (renderers == null || renderers.Length == 0)
                renderers = GetComponentsInChildren<Renderer>(true);

            float pad = pickPadding * (Input.touchCount > 0 ? Mathf.Max(1f, touchPickBoost) : 1f);

            Ray ray = board.StageCamera.ScreenPointToRay(screenPoint);
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                Bounds b = r.bounds;
                b.Expand(pad);
                if (b.IntersectRay(ray)) return true;
            }
            return false;
        }

        /// <summary>How far, in screen pixels, this plug is from the given point. Lower wins.</summary>
        public float GrabScore(Vector2 screenPoint)
        {
            Camera cam = board != null ? board.StageCamera : null;
            if (cam == null) return float.MaxValue;

            Vector3 p = cam.WorldToScreenPoint(transform.position);
            if (p.z <= 0f) return float.MaxValue;          // behind the camera
            return Vector2.Distance(screenPoint, new Vector2(p.x, p.y));
        }

        void BeginDrag()
        {
            if (seatRoutine != null) { StopCoroutine(seatRoutine); seatRoutine = null; }

            IsDragging = true;
            dragOffset = transform.position - RayHitOnDragPlane();
            board.SetHint(null);
        }

        void EndDrag()
        {
            IsDragging = false;
            board.ReleaseDrag(this);

            // The board owns the decision - this plug only reports where it was let go.
            if (!board.HandlePlugDropped(this, PointerInput.Position))
                ReturnHome();
        }

        // Camera-facing plane through the plug's current position - the same drag model the
        // conductors, the patch cords in COC1 and the crimping tool all use.
        Vector3 RayHitOnDragPlane()
        {
            Camera cam = board.StageCamera;
            Plane plane = new Plane(cam.transform.forward, transform.position);
            Ray ray = cam.ScreenPointToRay(PointerInput.Position);
            return plane.Raycast(ray, out float enter) ? ray.GetPoint(enter) : transform.position;
        }

        // ------------------------------------------------------------------

        /// <summary>Seats this plug in a socket. Called by the board.</summary>
        public void SeatInto(PatchCordSocket socket)
        {
            IsSeated = true;
            IsDragging = false;
            SeatedIn = socket;

            Transform target = socket.Seat;
            if (seatDuration <= 0f || !gameObject.activeInHierarchy)
                transform.SetPositionAndRotation(target.position, target.rotation);
            else
                seatRoutine = StartCoroutine(EaseInto(target));
        }

        System.Collections.IEnumerator EaseInto(Transform target)
        {
            Vector3 p0 = transform.position;
            Quaternion r0 = transform.rotation;
            float t = 0f;
            while (t < seatDuration)
            {
                // Unscaled, because the stage can be open while a dialog has stopped the clock -
                // with Time.deltaTime at 0 the plug would hang halfway into the port forever.
                t += Time.unscaledDeltaTime;
                float f = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seatDuration));
                transform.position = Vector3.Lerp(p0, target.position, f);
                transform.rotation = Quaternion.Slerp(r0, target.rotation, f);
                yield return null;
            }
            transform.SetPositionAndRotation(target.position, target.rotation);
            seatRoutine = null;
        }

        /// <summary>Plugged in and left there. Not coming back out by hand.</summary>
        public void Lock()
        {
            IsLocked = true;
            IsDragging = false;
        }

        public void ReturnHome()
        {
            Capture();
            if (seatRoutine != null) { StopCoroutine(seatRoutine); seatRoutine = null; }
            transform.SetPositionAndRotation(homePosition, homeRotation);
        }

        /// <summary>Back hanging at the panel, unlocked, ready to be dragged again.</summary>
        public void ResetPlug()
        {
            IsDragging = false;
            IsSeated = false;
            IsLocked = false;
            SeatedIn = null;
            gameObject.SetActive(true);
            ReturnHome();
        }
    }
}