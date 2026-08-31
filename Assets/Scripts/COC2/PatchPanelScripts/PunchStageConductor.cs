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

        [Tooltip("Multiplies Pick Padding while the input is a TOUCH.\n\n" +
                 "A mouse cursor is a point; a fingertip covers several millimetres of screen and " +
                 "hides what is under it. Padding tuned against a cursor makes a conductor feel " +
                 "slippery on a phone.\n\n" +
                 "Safe to raise: this only decides which conductors are ELIGIBLE for a press. Which " +
                 "one actually gets picked up is settled by the board, by whichever wire the touch " +
                 "landed nearest to on screen.")]
        public float touchPickBoost = 2f;

        [Tooltip("The other end of this wire, at the cable. FILLED IN FOR YOU by " +
                 "PunchStageConductorRig.\n\n" +
                 "Used to work out which conductor a touch meant. Renderer bounds are axis-aligned " +
                 "boxes, and eight wires fanning out from one cable have boxes that overlap almost " +
                 "completely - so 'is the pointer inside my box' cannot tell blue from its " +
                 "neighbour. Measuring to the LINE between this end and the tip can.")]
        public Transform fixedEnd;

        [Header("Feel")]
        [Tooltip("Seconds the conductor takes to ease into a pin. 0 for an instant snap.")]
        public float seatDuration = 0.12f;

        [Header("Physics wire")]
        [Tooltip("The VerletRope drawing this conductor. FILLED IN FOR YOU by " +
                 "PunchStageConductorRig - leave it empty unless you built the wire by hand.\n\n" +
                 "It exists so ReturnHome() can straighten the rope instantly. Without it, a " +
                 "conductor sent home after a failed termination snaps back on its own while the " +
                 "wire it is attached to stays strung out towards the pin, then crawls after it " +
                 "over the next second or two - which reads as the retry not having worked.")]
        public VerletRope rope;

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

        /// <summary>
        /// Shows or hides this conductor's wire.
        ///
        /// It toggles the same renderers the pick test uses, which is the point: a hidden conductor
        /// is also un-grabbable, because PointerOverSelf skips any renderer that is switched off.
        /// One flag, and the wire is neither seen nor reachable - no second guard to keep in step.
        ///
        /// Renderers rather than the GameObject, deliberately. Deactivating the object would stop
        /// its VerletRope simulating and, if it were done before the rig had built, would prevent
        /// the wire being created at all.
        /// </summary>
        public void SetVisible(bool on)
        {
            if (renderers == null || renderers.Length == 0)
            {
                if (pickRenderers != null && pickRenderers.Length > 0) renderers = pickRenderers;
                else renderers = GetComponentsInChildren<Renderer>(true);
            }
            foreach (var r in renderers)
            {
                if (r != null) r.enabled = on;
            }
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
                // The BOARD decides who gets picked up, not this conductor.
                //
                // It used to be "am I under the pointer, and can I claim the drag" - and with eight
                // wires whose bounding boxes overlap, that meant the winner was whichever conductor
                // Unity happened to call Update on first. Component order is arbitrary, so touching
                // blue could pick up the wire beside it. Asking the board makes the nearest wire to
                // the touch win every time, whatever order these run in.
                if (PointerInput.Down &&
                    board.ConductorForPress(PointerInput.Position) == this &&
                    board.TryClaimDrag(this))
                    BeginDrag();
                return;
            }

            transform.position = RayHitOnDragPlane() + dragOffset;
            board.HighlightForConductor(this);

            if (PointerInput.Up) EndDrag();
        }

        /// <summary>
        /// Is this conductor a CANDIDATE for a press at this screen point?
        ///
        /// A coarse test on purpose - renderer bounds are axis-aligned boxes and a fan of wires has
        /// boxes that overlap heavily, so this will usually say yes for several conductors at once.
        /// Narrowing that down to one is GrabScore's job, and the board's.
        ///
        /// Public so the board can run this over every conductor before deciding. The UI check that
        /// used to live here has moved to the board too, so it runs once per press instead of eight
        /// times.
        /// </summary>
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

        /// <summary>
        /// How far, IN SCREEN PIXELS, this wire is from the given point. Lower wins.
        ///
        /// Measured to the LINE from the cable end to the tip rather than to a single point, because
        /// the player grabs a wire anywhere along it - near the cable, halfway, at the end - and a
        /// tip-only measurement would hand every press near the cable to whichever tip happened to
        /// be closest, which is exactly the wrong answer.
        ///
        /// The line is a straight approximation of a rope that actually sags, so it is not perfect
        /// in the middle. It does not have to be: it only has to separate eight wires from each
        /// other, and they are far enough apart on screen that it does.
        /// </summary>
        public float GrabScore(Vector2 screenPoint)
        {
            Camera cam = board != null ? board.StageCamera : null;
            if (cam == null) return float.MaxValue;

            Vector3 tip3 = cam.WorldToScreenPoint(transform.position);
            var tip = new Vector2(tip3.x, tip3.y);
            if (tip3.z <= 0f) return float.MaxValue;          // behind the camera

            if (fixedEnd == null) return Vector2.Distance(screenPoint, tip);

            Vector3 root3 = cam.WorldToScreenPoint(fixedEnd.position);
            if (root3.z <= 0f) return Vector2.Distance(screenPoint, tip);
            var root = new Vector2(root3.x, root3.y);

            return DistanceToSegment(screenPoint, root, tip);
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 0.0001f) return Vector2.Distance(p, a);

            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            return Vector2.Distance(p, a + ab * t);
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

            // Straighten the wire in the same frame the tip moves. The rope's points are still
            // strung out to wherever the pin was; ForceRebuild re-lays them between the two anchors
            // at their CURRENT positions, so the conductor and its wire arrive home together.
            if (rope != null && rope.startAnchor != null && rope.endAnchor != null)
                rope.ForceRebuild();
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