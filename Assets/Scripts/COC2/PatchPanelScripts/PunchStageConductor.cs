// PunchStageConductor.cs
using UnityEngine;
using UnityEngine.EventSystems;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// One draggable conductor on the punch-down stage - a real 3D object the player picks up with
    /// the pointer and drops onto a pin.
    ///
    /// Three things here are lessons already paid for in this project rather than style choices:
    ///
    ///   1. PointerInput, not OnMouseDown/OnMouseDrag. Those legacy callbacks go dead on mobile the
    ///      moment anything sets Input.simulateMouseWithTouches = false.
    ///   2. Renderer bounds, not a collider raycast, for the pick test - so there is no collider to
    ///      go stale, no layer mask to get wrong, and nothing to set up.
    ///   3. The board is asked before a drag starts, so two overlapping conductors can never both be
    ///      picked up by one press.
    ///
    /// Setup:
    ///   1. A conductor mesh on the stage. No collider needed.
    ///   2. Put this on it and set Color.
    ///   3. That's all - the board finds it and hands it the camera.
    /// </summary>
    public class PunchStageConductor : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Which conductor this is. The termination is judged on these, so it has to match " +
                 "the wire the player thinks they're holding.")]
        public WireColor color = WireColor.WhiteOrange;

        [Header("Appearance")]
        [Tooltip("Tint this conductor's renderer from WireColorPalette at Awake.\n\n" +
                 "Leave OFF if the meshes already come out of Blender with the right materials, " +
                 "which is the usual case. Turn ON if you built the stage from eight identical " +
                 "primitives and want the colours filled in for you.")]
        public bool tintFromColor = false;
        [Tooltip("Renderer to tint. Leave empty to use the first one under this object.")]
        public Renderer tintTarget;

        [Header("Picking")]
        [Tooltip("Renderers used for the pick test. Leave empty to use every renderer under this " +
                 "object.")]
        public Renderer[] pickRenderers;
        [Tooltip("Grows the pick area by this many world units, so a thin wire is still easy to grab.")]
        public float pickPadding = 0.008f;

        [Header("Feel")]
        [Tooltip("Seconds the conductor takes to ease into a pin. 0 for an instant snap.")]
        public float seatDuration = 0.12f;

        public bool IsDragging { get; private set; }
        /// <summary>True once seated in a pin.</summary>
        public bool IsSeated { get; private set; }
        /// <summary>True once struck. A locked conductor can never be picked up again.</summary>
        public bool IsLocked { get; private set; }

        PunchStageBoard board;
        Renderer[] renderers;
        Vector3 homePosition;
        Quaternion homeRotation;
        Vector3 dragOffset;
        Coroutine seatRoutine;
        bool captured;

        /// <summary>
        /// Records where this conductor lives in the tray. Same reasoning as PunchToolIcon.Capture():
        /// if Stage Root is ever assigned on the station, the whole set is switched off at Awake and
        /// nothing under it runs Awake until the stage first opens - while the board resets on its
        /// own Awake and calls ReturnHome() here. With the pose unrecorded that would send every
        /// conductor to the world origin, half a kilometre from the set.
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

        /// <summary>
        /// Records the CURRENT pose as home, replacing whatever was captured before.
        ///
        /// Needed because a multi-port run slides the whole conductor tray along to the next port.
        /// Home is stored in world space, so without this the conductors would keep returning to
        /// where the tray used to be - which after port 2 is somewhere off the side of the set.
        ///
        /// Call it only when the conductors are actually AT home; calling it mid-drag or while
        /// seated would record the wrong place as home.
        /// </summary>
        public void RecaptureHome()
        {
            captured = true;
            homePosition = transform.position;
            homeRotation = transform.rotation;
        }

        void Awake()
        {
            Capture();

            if (pickRenderers == null || pickRenderers.Length == 0)
                pickRenderers = GetComponentsInChildren<Renderer>(true);
            renderers = pickRenderers;

            if (tintFromColor) ApplyTint();
        }

        /// <summary>
        /// Hands this conductor the renderers it should be picked by, after Awake has run.
        ///
        /// Needed because PunchStageConductorRig builds the wire at runtime: the draggable object it
        /// creates has no geometry of its own - the rope does - so the pick test has to be pointed at
        /// a renderer that lives on a different object entirely. Setting the public field alone would
        /// not do, because Awake has already copied it into the working array by then.
        /// </summary>
        public void SetPickRenderers(Renderer[] rs)
        {
            pickRenderers = rs;
            renderers = rs;
        }

        /// <summary>The board calls this on itself, so a conductor never has to be wired by hand.</summary>
        public void Bind(PunchStageBoard owner)
        {
            board = owner;
            Capture();
        }

        [ContextMenu("Apply colour")]
        public void ApplyTint()
        {
            Renderer target = tintTarget != null ? tintTarget : GetComponentInChildren<Renderer>(true);
            if (target == null) return;

            // A property block instead of touching .material, which would instantiate a new material
            // per conductor and quietly leak eight of them every time the stage is rebuilt.
            var block = new MaterialPropertyBlock();
            target.GetPropertyBlock(block);
            Color c = WireColorPalette.ToUnityColor(color);
            block.SetColor("_BaseColor", c);   // URP / Lit
            block.SetColor("_Color", c);       // Built-in, and anything still using the old name
            target.SetPropertyBlock(block);
        }

        // ------------------------------------------------------------------

        void Update()
        {
            if (board == null || IsLocked) return;
            if (board.StageCamera == null || !board.AcceptsInput) return;
            if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return;

            if (!IsDragging)
            {
                if (PointerInput.Down && PointerOverSelf() && board.TryClaimDrag(this))
                    BeginDrag();
                return;
            }

            transform.position = RayHitOnDragPlane() + dragOffset;
            board.HighlightForConductor(this);

            if (PointerInput.Up) EndDrag();
        }

        bool PointerOverSelf()
        {
            // A tap on the tool icon can also line up with a conductor behind it, since none of this
            // knows about uGUI. The parameterless call only checks the mouse, so touch needs the
            // active finger's id.
            if (EventSystem.current != null)
            {
                bool overUI = Input.touchCount > 0
                    ? EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId)
                    : EventSystem.current.IsPointerOverGameObject();
                if (overUI) return false;
            }

            if (renderers == null || renderers.Length == 0)
                renderers = GetComponentsInChildren<Renderer>(true);

            Ray ray = board.StageCamera.ScreenPointToRay(PointerInput.Position);
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
            if (seatRoutine != null) { StopCoroutine(seatRoutine); seatRoutine = null; }

            IsDragging = true;
            dragOffset = transform.position - RayHitOnDragPlane();
            board.SetHint(null);
        }

        void EndDrag()
        {
            IsDragging = false;
            board.ReleaseDrag(this);

            // The board owns the decision - this conductor only reports where it was let go.
            if (!board.HandleConductorDropped(this, PointerInput.Position))
                ReturnHome();
        }

        // Camera-facing plane through the conductor's current position - the same drag model
        // PatchCord and the crimping tool both use.
        Vector3 RayHitOnDragPlane()
        {
            Camera cam = board.StageCamera;
            Plane plane = new Plane(cam.transform.forward, transform.position);
            Ray ray = cam.ScreenPointToRay(PointerInput.Position);
            return plane.Raycast(ray, out float enter) ? ray.GetPoint(enter) : transform.position;
        }

        // ------------------------------------------------------------------

        /// <summary>Seats this conductor in a pin. Called by the board.</summary>
        public void SeatInto(PunchStagePin pin)
        {
            IsSeated = true;
            IsDragging = false;

            Transform target = pin.Seat;
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
                // Unscaled, because the stage can be entered while something else has stopped time -
                // with Time.deltaTime at 0 the conductor would hang halfway into the pin forever.
                t += Time.unscaledDeltaTime;
                float f = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seatDuration));
                transform.position = Vector3.Lerp(p0, target.position, f);
                transform.rotation = Quaternion.Slerp(r0, target.rotation, f);
                yield return null;
            }
            transform.SetPositionAndRotation(target.position, target.rotation);
            seatRoutine = null;
        }

        /// <summary>Struck. It is not coming back out by hand, right or wrong.</summary>
        public void Lock()
        {
            IsLocked = true;
            IsDragging = false;
        }

        public void ReturnHome()
        {
            Capture();
            if (!captured) return;
            if (seatRoutine != null) { StopCoroutine(seatRoutine); seatRoutine = null; }
            transform.SetPositionAndRotation(homePosition, homeRotation);
        }

        /// <summary>Back in the tray, unlocked, ready to be dragged again.</summary>
        public void ResetConductor()
        {
            IsDragging = false;
            IsSeated = false;
            IsLocked = false;
            gameObject.SetActive(true);
            ReturnHome();
        }
    }
}