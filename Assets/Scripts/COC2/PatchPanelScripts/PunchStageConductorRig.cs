// PunchStageConductorRig.cs
using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Turns one duplicated conductor mesh into a bendable wire, the same way COC1's PSU harness
    /// works: a VerletRope simulating between a fixed end and a free end the player drags.
    ///
    /// WHAT IT BUILDS, at Awake, with nothing to assign:
    ///
    ///   1. Reads the duplicated mesh to find where the conductor starts and ends, and borrows its
    ///      MATERIAL - so the wire comes out the right colour without you picking one.
    ///   2. Switches that mesh's renderer off. The rope is the conductor now; leaving both on gives
    ///      you a rigid wire with a floppy one inside it.
    ///   3. Creates a fixed anchor at the root end and a free tip at the other.
    ///   4. Creates a rope object carrying MeshFilter + MeshRenderer + VerletRope, wired between the
    ///      two, using the borrowed material.
    ///   5. Puts a PunchStageConductor on the tip so it can be dragged and seated - and points its
    ///      pick test at the ROPE's renderer, so the player can grab the wire anywhere along its
    ///      length rather than having to find an invisible end.
    ///
    /// THE ROPE IS NOT PARENTED TO THE TIP. It sits alongside, under the stage, because a rope
    /// parented to the thing it is chasing would drag its own mesh around with it.
    ///
    /// Setup:
    ///   1. Duplicate a conductor mesh from UTPCable_01 into the stage's Conductors group.
    ///   2. Put this on it and set Color. Remove any PunchStageConductor - this adds its own.
    ///   3. Repeat for all eight. That's the whole job.
    ///
    /// REQUIRES the VerletRope with the Use Unscaled Time option. Without it the wire freezes
    /// whenever anything stops the clock - a dialog with Pause Game While Open ticked will do it -
    /// while the drag, which runs from Update, keeps going. The plug travels, the cord stays behind.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class PunchStageConductorRig : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Which conductor this is. Copied onto the PunchStageConductor this builds, and it " +
                 "is what the termination is judged on.")]
        public WireColor color = WireColor.WhiteOrange;

        [Header("Ends")]
        [Tooltip("Where this conductor is pinned down - the end that never moves.\n\n" +
                 "Leave empty and one is created for you at the root end of the duplicated mesh, " +
                 "which is usually exactly right. Assign your own only if you want the wire to come " +
                 "from somewhere the mesh doesn't already suggest.")]
        public Transform fixedAnchor;

        [Tooltip("Which end of the mesh is the fixed one. If the wire ends up anchored at the wrong " +
                 "end - the free tip sitting where the cable should be - tick this.")]
        public bool flipEnds = false;

        [Header("Wire")]
        [Tooltip("WORLD units, not local. On a stage scaled up 10x, a real 0.5 mm conductor wants " +
                 "about 0.004 here. If the wire looks like rope, this is why.")]
        public float cableRadius = 0.004f;

        [Tooltip("WORLD units. Rest distance between simulated points - smaller bends more smoothly " +
                 "and costs more. About five times the radius is a good starting point.")]
        public float segmentLength = 0.02f;

        public int minSegments = 8;
        public int maxSegments = 40;

        [Tooltip("How hard the wire sags. A short stiff conductor wants less than a hanging cable - " +
                 "try -1.5 on Y if it droops too much.")]
        public Vector3 gravity = new Vector3(0f, -2.5f, 0f);

        [Tooltip("Leave ON. The stage can be entered while a dialog has stopped the clock, and Unity " +
                 "does not call FixedUpdate at all when Time.timeScale is 0 - so a rope driven from " +
                 "there freezes while the drag, which runs from Update, keeps moving.")]
        public bool useUnscaledTime = true;

        [Header("Extra length")]
        [Tooltip("Slack, as a fraction of the distance between the two ends. 0.15 gives a wire that " +
                 "hangs in a gentle curve instead of a taut straight line. It only affects how many " +
                 "segments are built, so raise it if the wire looks stretched.")]
        [Range(0f, 1f)] public float slack = 0.15f;

        [Header("Behaviour")]
        [Tooltip("Switch the duplicated mesh's renderer off. Leave ON - the rope replaces it, and " +
                 "with both visible you get a rigid conductor with a bending one inside it.")]
        public bool hideSourceMesh = true;

        [Tooltip("Material for the wire. Leave empty to borrow the duplicated mesh's own material, " +
                 "which is what you want - it is already the right colour.")]
        public Material wireMaterial;

        [Tooltip("Log what got built. Worth leaving on until all eight are confirmed good.")]
        public bool logResult = true;

        public VerletRope Rope { get; private set; }
        public PunchStageConductor Conductor { get; private set; }

        void Awake()
        {
            Build();
        }

        [ContextMenu("Build now")]
        public void Build()
        {
            Renderer source = GetComponentInChildren<Renderer>(true);
            if (source == null)
            {
                Debug.LogError("[PunchStageConductorRig] '" + name + "' has no renderer, so there is " +
                               "no mesh to measure the wire from.", this);
                return;
            }

            Material mat = wireMaterial != null ? wireMaterial : source.sharedMaterial;

            Vector3 rootPoint, tipPoint;
            MeasureEnds(source, out rootPoint, out tipPoint);

            // ---- the fixed end -------------------------------------------
            Transform root = fixedAnchor;
            if (root == null)
            {
                var rootGO = new GameObject(name + "_FixedEnd");
                rootGO.transform.SetParent(transform.parent, true);
                rootGO.transform.position = rootPoint;
                rootGO.transform.rotation = Quaternion.identity;
                root = rootGO.transform;
                fixedAnchor = root;
            }

            // ---- the free end --------------------------------------------
            // Parented to the stage, not to this mesh: it has to move independently of everything.
            var tipGO = new GameObject(name + "_Tip");
            tipGO.transform.SetParent(transform.parent, true);
            tipGO.transform.position = tipPoint;
            tipGO.transform.rotation = transform.rotation;

            // ---- the rope ------------------------------------------------
            // Alongside the tip, never under it. A rope parented to the object it is chasing would
            // carry its own mesh along with every move and shear the tube.
            var ropeGO = new GameObject(name + "_Rope");
            ropeGO.transform.SetParent(transform.parent, true);
            ropeGO.transform.localPosition = Vector3.zero;
            ropeGO.transform.localRotation = Quaternion.identity;
            ropeGO.transform.localScale = Vector3.one;

            ropeGO.AddComponent<MeshFilter>();          // VerletRope writes its tube into this
            var mr = ropeGO.AddComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;

            var rope = ropeGO.AddComponent<VerletRope>();
            rope.startAnchor = root;
            rope.endAnchor = tipGO.transform;
            rope.cableRadius = cableRadius;
            rope.segmentLength = segmentLength;
            rope.minSegments = minSegments;
            rope.maxSegments = maxSegments;
            rope.gravity = gravity;
            rope.useUnscaledTime = useUnscaledTime;
            Rope = rope;

            // Slack is bought with segments: the rope's rest length is segmentLength x segments, so
            // a few more than the straight-line distance needs is what lets it hang in a curve.
            if (slack > 0f)
            {
                float span = Vector3.Distance(rootPoint, tipPoint);
                int wanted = Mathf.CeilToInt(span * (1f + slack) / Mathf.Max(0.0001f, segmentLength)) + 1;
                rope.minSegments = Mathf.Clamp(wanted, minSegments, maxSegments);
            }

            // ---- the draggable -------------------------------------------
            // AddComponent runs Awake immediately, and Awake records where home is - so the tip has
            // to be in position before this line, not after.
            var conductor = tipGO.AddComponent<PunchStageConductor>();
            conductor.color = color;
            conductor.tintFromColor = false;          // the borrowed material is already right
            conductor.SetPickRenderers(new Renderer[] { mr });

            // So ReturnHome() can straighten the wire in the same frame the tip snaps back. Without
            // this the retry after a failed termination looks broken: the conductor is home but its
            // wire is still reaching for the pin.
            conductor.rope = rope;

            // So the board can tell this wire from the seven beside it: a press is scored against
            // the LINE from here to the tip, which separates eight overlapping wires when their
            // bounding boxes cannot.
            conductor.fixedEnd = root;
            Conductor = conductor;

            // ---- hide the original ---------------------------------------
            if (hideSourceMesh) source.enabled = false;

            if (logResult)
            {
                Debug.LogWarning("[PunchStageConductorRig] '" + name + "' built: " +
                                 WireColorPalette.ToDisplayName(color) +
                                 "   fixed=" + root.name + "   tip=" + tipGO.name +
                                 "   segments=" + rope.minSegments +
                                 (mat == null ? "   <-- NO MATERIAL, the wire will render pink" : ""), this);
            }
        }

        /// <summary>
        /// Works out which end of the duplicated mesh is the root and which is the free tip, by
        /// measuring along the mesh's own longest axis. A conductor is a long thin thing, so its
        /// longest axis is always its length - no naming convention or manual marking needed.
        /// </summary>
        void MeasureEnds(Renderer source, out Vector3 root, out Vector3 tip)
        {
            Bounds b = source.bounds;
            Vector3 e = b.extents;

            Vector3 axis;
            if (e.x >= e.y && e.x >= e.z) axis = Vector3.right;
            else if (e.y >= e.z) axis = Vector3.up;
            else axis = Vector3.forward;

            float half = Vector3.Dot(e, axis);
            Vector3 a = b.center - axis * half;
            Vector3 c = b.center + axis * half;

            // If an anchor was assigned, the end nearer it is obviously the root. Otherwise fall back
            // to the axis order, which flipEnds exists to correct.
            bool aIsRoot;
            if (fixedAnchor != null)
                aIsRoot = Vector3.Distance(a, fixedAnchor.position) <= Vector3.Distance(c, fixedAnchor.position);
            else
                aIsRoot = true;

            if (flipEnds) aIsRoot = !aIsRoot;

            root = aIsRoot ? a : c;
            tip = aIsRoot ? c : a;
        }
    }
}