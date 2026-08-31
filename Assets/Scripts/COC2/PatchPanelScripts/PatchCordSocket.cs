// PatchCordSocket.cs
using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// One RJ45 socket a patch cord can be plugged into - normally a port on the switch.
    ///
    /// The patch-cord twin of PunchStagePin, and deliberately the same shape so there is nothing new
    /// to learn: an empty marker parented to the port on the model, picked by a radius rather than a
    /// collider, with an optional highlight child.
    ///
    /// NO COLLIDER. Like everything else on this stage it is hit-tested against Renderer.bounds or a
    /// radius box, so there is no physics to go stale and no layer to get wrong.
    ///
    /// Setup:
    ///   1. Create an empty child of the switch port, sitting where the plug's body should end up.
    ///   2. Put this on it and set Port Number to match the panel port it pairs with.
    ///   3. Leave Pick Renderers empty - Pick Radius is what you want on a shared switch mesh.
    /// </summary>
    [DisallowMultipleComponent]
    public class PatchCordSocket : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Which switch port this is. A cord is only accepted here when its own Port Number " +
                 "matches - that one-to-one mapping is the whole lesson of the stage.")]
        [Range(1, 24)] public int portNumber = 1;

        [Header("Seating")]
        [Tooltip("Where the plug comes to rest, position AND rotation. Leave empty to use this " +
                 "object's own transform, which is usually right if you placed it carefully.")]
        public Transform seatAnchor;

        [Header("Picking")]
        [Tooltip("Renderers used for the hit test. Leave EMPTY when the ports are all one mesh - " +
                 "which they are on a switch model - and Pick Radius is used instead.")]
        public Renderer[] pickRenderers;

        [Tooltip("Grows the renderer bounds by this many world units. Ignored when Pick Radius is " +
                 "in use.")]
        public float pickPadding = 0.004f;

        [Tooltip("WORLD units. The half-size of the invisible box this socket is hit-tested with " +
                 "when it has no renderers of its own.\n\n" +
                 "Set it against the real port spacing on your switch. Too big and a cord lands in " +
                 "the port next door; too small and nothing can be dropped. The board measures the " +
                 "true spacing on Play and tells you if this is wrong.")]
        public float pickRadius = 0.01f;

        [Header("Feedback (optional)")]
        [Tooltip("Switched on while this is the socket the dragged cord belongs in.")]
        public GameObject highlight;

        [Tooltip("Switched on once a cord is seated here - a link LED, a small glow, anything.")]
        public GameObject occupiedMark;

        /// <summary>True once a cord is plugged in here.</summary>
        public bool IsOccupied { get; private set; }

        /// <summary>The cord currently in this socket, or null.</summary>
        public PatchCordPlug Occupant { get; private set; }

        public Transform Seat { get { return seatAnchor != null ? seatAnchor : transform; } }

        /// <summary>True when there are no renderers to test against, so the radius box is used.
        /// This is the normal case: the switch ports are part of one shared mesh.</summary>
        public bool UsesRadius { get { return pickRenderers == null || pickRenderers.Length == 0; } }

        void Awake()
        {
            SetHighlighted(false);
            if (occupiedMark != null) occupiedMark.SetActive(false);
        }

        public float DistanceTo(Vector3 worldPosition)
        {
            return Vector3.Distance(Seat.position, worldPosition);
        }

        /// <summary>Is this socket under the given ray, and how far along it?</summary>
        public bool IsUnderRay(Ray ray, out float distance)
        {
            distance = float.MaxValue;

            if (UsesRadius)
            {
                var box = new Bounds(Seat.position, Vector3.one * (pickRadius * 2f));
                if (box.IntersectRay(ray, out float rd)) { distance = rd; return true; }
                return false;
            }

            bool hit = false;
            foreach (var r in pickRenderers)
            {
                if (r == null || !r.enabled) continue;
                Bounds b = r.bounds;
                b.Expand(pickPadding);
                if (b.IntersectRay(ray, out float d) && d < distance) { distance = d; hit = true; }
            }
            return hit;
        }

        public void SetHighlighted(bool on)
        {
            if (highlight != null) highlight.SetActive(on);
        }

        /// <summary>Records that a cord is now in here. Named with the underscore for the same
        /// reason PunchStagePin.Seat_ is: Seat is already the anchor property.</summary>
        public void Seat_(PatchCordPlug plug)
        {
            IsOccupied = true;
            Occupant = plug;
            if (occupiedMark != null) occupiedMark.SetActive(true);
            SetHighlighted(false);
        }

        public void ResetSocket()
        {
            IsOccupied = false;
            Occupant = null;
            if (occupiedMark != null) occupiedMark.SetActive(false);
            SetHighlighted(false);
        }
    }
}
