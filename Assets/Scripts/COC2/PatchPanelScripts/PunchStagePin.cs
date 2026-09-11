// PunchStagePin.cs
using System.Collections.Generic;
using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// One pin on the punch-down STAGE - the small 3D set that lives off on its own somewhere in the
    /// scene, far from the real patch panel model, and is only ever seen through the stage camera.
    ///
    /// This is a real 3D GameObject with a real mesh, not a UI element. What it is NOT is a copy of
    /// anything on the trainer FBX: the stage is its own little diorama, built at whatever size
    /// reads clearly on screen. Because the stage camera is the only thing that ever looks at it,
    /// you can make the pins as big and as far apart as you like without it looking wrong anywhere
    /// else.
    ///
    /// NO COLLIDER. Picking is done against Renderer.bounds, not a physics raycast. Unity only
    /// pushes a moved Transform into PhysX during FixedUpdate, so a collider on anything that gets
    /// repositioned outside that step sits at a stale pose and every ray misses it while the mesh is
    /// plainly on screen - the bug that made the crimping tool unclickable for an evening. Renderer
    /// bounds come straight off the Transform and cannot drift.
    ///
    /// Setup:
    ///   1. A mesh for the pin or channel, somewhere on the stage. No collider needed.
    ///   2. Put this on it and set Pin Number, 1 through 8.
    ///   3. Add an empty child where a seated conductor should sit, and assign it to Seat Anchor.
    ///   4. Everything else is optional.
    /// </summary>
    public class PunchStagePin : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("1-8. This is the position the termination is judged against.")]
        [Range(1, 8)] public int pinNumber = 1;

        [Header("Seating")]
        [Tooltip("Empty child marking where a conductor sits once dropped here. The conductor snaps " +
                 "to this transform's position AND rotation, so point it the way the wire should lie. " +
                 "Leave empty to seat at this object's own transform.")]
        public Transform seatAnchor;

        [Header("Picking")]
        [Tooltip("Renderers used to work out whether the pointer or a dragged conductor is over this " +
                 "pin. Leave empty to use every renderer under this object.\n\n" +
                 "IF THIS PIN HAS NO RENDERER AT ALL, that is fine and often correct: when you reuse " +
                 "an existing panel model, the whole IDC block is one mesh and the eight channels are " +
                 "just positions on it. Make each pin an empty GameObject sitting in its channel and " +
                 "the Pick Radius below is used instead.")]
        public Renderer[] pickRenderers;

        [Tooltip("Grows the pick area by this many world units. Pins on a stage set are usually " +
                 "small on screen even when the set is scaled up, so a little padding makes them far " +
                 "easier to hit - especially on touch.")]
        public float pickPadding = 0.01f;

        [Tooltip("Used INSTEAD of renderer bounds when this pin has no renderer of its own - an " +
                 "invisible cube of this half-size around the pin's position becomes its hit area.\n\n" +
                 "Set it to a little under half the gap between two channels, in world units, so " +
                 "neighbouring pins can't be hit by mistake. On a panel reused at real size the " +
                 "channels are about 2.2 mm apart, so 0.001 - and if you scaled the set up by ten, " +
                 "0.01.")]
        public float pickRadius = 0.01f;

        [Header("Feedback (optional)")]
        [Tooltip("Shown when this is the pin the player should be working on next, or when a drag " +
                 "is hovering somewhere it would be accepted.")]
        public GameObject highlight;
        [Tooltip("Shown once the conductor here has been struck.")]
        public GameObject punchedMark;

        /// <summary>The conductor sitting in this pin, or null.</summary>
        public PunchStageConductor Seated { get; private set; }
        public bool IsSeated { get { return Seated != null; } }
        /// <summary>True once struck. A struck conductor never comes back out.</summary>
        public bool IsPunched { get; private set; }
        public WireColor SeatedColor { get { return Seated != null ? Seated.color : default(WireColor); } }

        public Transform Seat { get { return seatAnchor != null ? seatAnchor : transform; } }

        void Awake()
        {
            if (pickRenderers == null || pickRenderers.Length == 0)
                pickRenderers = CollectPickRenderers();
            ResetPin();
        }

        /// <summary>
        /// Every renderer under this pin EXCEPT the feedback objects.
        ///
        /// WHY THE HIGHLIGHT HAS TO BE EXCLUDED
        /// This used to be a plain GetComponentsInChildren&lt;Renderer&gt;(true) - and the
        /// true means "include inactive", which is right for a pin whose mesh is
        /// switched off at author time and wrong for everything else.
        ///
        /// A pin with no mesh of its own is meant to fall back to Pick Radius, and
        /// UsesRadius decides that by asking whether pickRenderers is empty. Assign
        /// a highlight child that has a renderer and the array is no longer empty,
        /// so the pin switches to renderer bounds - but the highlight starts
        /// INACTIVE, IsUnderRay skips inactive renderers, and the pin becomes
        /// impossible to hit. Nothing errors; the pin simply stops responding, and
        /// only the pins you gave a highlight to.
        ///
        /// The highlight and the punched mark are feedback, never pick geometry, so
        /// they are dropped here and the fallback keeps working.
        /// </summary>
        Renderer[] CollectPickRenderers()
        {
            Renderer[] all = GetComponentsInChildren<Renderer>(true);
            var kept = new List<Renderer>(all.Length);

            foreach (var r in all)
            {
                if (r == null) continue;
                if (IsFeedback(r.transform)) continue;
                kept.Add(r);
            }
            return kept.ToArray();
        }

        /// <summary>True if this transform is the highlight or punched mark, or sits
        /// underneath one - a highlight built from several meshes is still all
        /// feedback.</summary>
        bool IsFeedback(Transform t)
        {
            for (Transform c = t; c != null && c != transform; c = c.parent)
            {
                if (highlight != null && c.gameObject == highlight) return true;
                if (punchedMark != null && c.gameObject == punchedMark) return true;
            }
            return false;
        }

        /// <summary>True when this pin is a bare marker on a shared mesh rather than an object with
        /// its own geometry - which is the normal case when an existing panel model is reused.</summary>
        public bool UsesRadius
        {
            get { return pickRenderers == null || pickRenderers.Length == 0; }
        }

        // ------------------------------------------------------------------

        /// <summary>Is this ray pointing at the pin? Used for the tool icon, which only has a screen
        /// position to go on.</summary>
        public bool IsUnderRay(Ray ray, out float distance)
        {
            distance = float.MaxValue;

            // No geometry of its own: test a small box around the marker instead. This is what makes
            // it possible to drop eight empty GameObjects into the channels of a panel mesh you
            // already have, rather than modelling eight separate pins.
            if (UsesRadius)
            {
                var box = new Bounds(Seat.position, Vector3.one * (pickRadius * 2f));
                if (box.IntersectRay(ray, out float rd)) { distance = rd; return true; }
                return false;
            }

            bool hit = false;
            foreach (var r in pickRenderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                Bounds b = r.bounds;
                b.Expand(pickPadding);
                if (b.IntersectRay(ray, out float d) && d < distance)
                {
                    distance = d;
                    hit = true;
                }
            }
            return hit;
        }

        /// <summary>How far a dragged conductor is from where it would sit. Used for the forgiving
        /// nearest-pin test, which reads better for a 3D drag than a strict ray hit.</summary>
        public float DistanceTo(Vector3 worldPosition)
        {
            return Vector3.Distance(Seat.position, worldPosition);
        }

        // ------------------------------------------------------------------

        public void Seat_(PunchStageConductor conductor)
        {
            Seated = conductor;
        }

        /// <summary>
        /// Lets go of the conductor resting here, leaving everything else alone.
        ///
        /// Deliberately NOT ResetPin: that also clears IsPunched and hides the punched mark, which
        /// would undo a termination. This is only for lifting an unstruck conductor back out, so a
        /// struck pin refuses - once the tool has been through it, the conductor is trimmed and
        /// seated and nothing short of a full reset brings it back.
        /// </summary>
        public void ClearSeat()
        {
            if (IsPunched) return;
            Seated = null;
        }

        public void MarkPunched()
        {
            IsPunched = true;
            if (punchedMark != null) punchedMark.SetActive(true);
            SetHighlighted(false);
            if (Seated != null) Seated.Lock();
        }

        public void SetHighlighted(bool on)
        {
            if (highlight != null && highlight.activeSelf != on) highlight.SetActive(on);
        }

        /// <summary>Empties the pin. The conductor is sent home by the board, not from here.</summary>
        public void ResetPin()
        {
            Seated = null;
            IsPunched = false;
            if (punchedMark != null) punchedMark.SetActive(false);
            SetHighlighted(false);
        }
    }
}