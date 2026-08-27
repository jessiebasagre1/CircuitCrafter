// PatchCordDiagnostics.cs
using System.Text;
using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Read-only instrumentation for "the cord doesn't follow the plug". Changes no behaviour;
    /// it only measures and reports.
    ///
    /// There are exactly four ways a VerletRope can sit still while its plug moves, and every one
    /// of them is invisible in the Inspector:
    ///
    ///   1. Both anchor slots hold the SAME transform. Both plugs' anchors share a name, so the
    ///      field text is identical either way.
    ///   2. An anchor belongs to a different cord entirely - again, identical field text.
    ///   3. Physics isn't stepping. VerletRope pins its endpoints in FixedUpdate; PatchCord drags
    ///      in Update. Time.timeScale = 0 freezes the rope while the plug keeps moving.
    ///   4. VerletRope disabled itself at Start, or its object is inactive.
    ///
    /// This tells you which one, by instance ID and hierarchy path, instead of by guessing.
    ///
    /// Setup: drop it on any always-active object, press Play, press F2.
    /// </summary>
    public class PatchCordDiagnostics : MonoBehaviour
    {
        [Header("Report")]
        public KeyCode reportKey = KeyCode.F2;

        [Tooltip("While a plug is being dragged, log the plug position against the rope's two " +
                 "endpoints every few frames. This is what proves whether the rope is tracking.")]
        public bool logWhileDragging = true;
        [Tooltip("Seconds between live samples.")]
        public float sampleInterval = 0.25f;

        [Header("Scene view")]
        [Tooltip("Cyan = plug to its own rope anchor. Yellow = the rope's two anchors joined. " +
                 "If yellow doesn't move with the plug, the anchor is the problem.")]
        public bool drawLines = true;

        int fixedSteps;
        int stepsAtLastReport;
        float lastSample;
        Vector3 lastMeshCentre;
        bool lastMeshCentreValid;

        void FixedUpdate() { fixedSteps++; }

        /// <summary>
        /// Announces itself the way InteractButtonProbe does, and counts what's actually in the
        /// scene. Without this there was no way to tell an attached probe from an unattached one -
        /// silence looked identical either way, which wasted two rounds.
        /// </summary>
        void Start()
        {
            int cords = Object.FindObjectsByType<PatchCord>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            int ropes = Object.FindObjectsByType<VerletRope>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            int socks = Object.FindObjectsByType<RJ45Socket>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

            Debug.LogWarning(string.Format(
                "[PatchCordDiagnostics] ACTIVE on '{0}'. Press {1} for a report, or right-click the " +
                "component header in the Inspector and choose 'Report now' - that works even when " +
                "the Game view doesn't have keyboard focus.\n" +
                "    PatchCord components in this scene : {2}{3}\n" +
                "    VerletRope components              : {4}\n" +
                "    RJ45Socket components              : {5}",
                gameObject.name, reportKey, cords,
                cords == 0 ? "   <-- NONE. This scene has no patch cords at all, so whatever you are " +
                             "dragging is not a PatchCord. If it is UTPCable_XX_RJ45Plug, that cable's " +
                             "jacket is a baked static mesh with no rope behind it and it will never follow."
                           : "",
                ropes, socks), this);

            // Reporting straight from Start is misleading: physics has not stepped yet and
            // VerletRope.Start may not have run, so "0 physics steps" and "mesh: none yet" read as
            // faults when they are just frame zero. Wait a second for the scene to settle.
            if (cords > 0) autoReportFrame = Time.frameCount + 60;
        }

        int autoReportFrame = -1;

        void Update()
        {
            if (autoReportFrame > 0 && Time.frameCount >= autoReportFrame)
            {
                autoReportFrame = -1;
                Report();
            }
            if (Input.GetKeyDown(reportKey)) Report();
            if (logWhileDragging) SampleDrag();
            if (drawLines) DrawLines();
        }

        static string Path(Transform t)
        {
            if (t == null) return "<none>";
            var sb = new StringBuilder(t.name);
            Transform p = t.parent;
            while (p != null) { sb.Insert(0, p.name + "/"); p = p.parent; }
            return sb.ToString();
        }

        static PatchCord OwnerOf(Transform t)
        {
            return t == null ? null : t.GetComponentInParent<PatchCord>();
        }

        // ------------------------------------------------------------------

        void SampleDrag()
        {
            if (Time.unscaledTime - lastSample < sampleInterval) return;

            PatchCord[] cords = Object.FindObjectsByType<PatchCord>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var c in cords)
            {
                if (c == null || !c.IsDragging || c.rope == null) continue;
                lastSample = Time.unscaledTime;

                Vector3 plug = c.transform.position;
                Vector3 a = c.rope.startAnchor != null ? c.rope.startAnchor.position : Vector3.zero;
                Vector3 b = c.rope.endAnchor != null ? c.rope.endAnchor.position : Vector3.zero;

                // The three numbers that separate the remaining possibilities:
                //   anchors not tracking the plug  -> wiring
                //   anchors track but mesh frozen  -> rope not simulating or not rebuilding
                //   all three move                 -> the cord IS working and you're looking at
                //                                     something else (e.g. PatchCords_Static)
                MeshFilter mf = c.rope.GetComponent<MeshFilter>();
                Mesh m = mf != null ? mf.sharedMesh : null;
                Vector3 meshCentre = m != null ? c.rope.transform.TransformPoint(m.bounds.center) : Vector3.zero;
                float meshMoved = lastMeshCentreValid ? Vector3.Distance(meshCentre, lastMeshCentre) : -1f;
                lastMeshCentre = meshCentre;
                lastMeshCentreValid = true;

                // LogWarning, not Log. The Console's Info filter is off in this project, so plain
                // Debug.Log output is invisible - which is exactly why the first report never showed.
                Debug.LogWarning(string.Format(
                    "[PatchCordDiag] DRAGGING {0}\n" +
                    "    plug         {1}\n" +
                    "    startAnchor  {2}   dist to plug {3:F4}\n" +
                    "    endAnchor    {4}   dist to plug {5:F4}\n" +
                    "    cord mesh    verts {6}   centre {7}   moved since last sample {8:F4}\n" +
                    "    timeScale {9}   physics steps since last report {10}",
                    c.name, plug, a, Vector3.Distance(a, plug), b, Vector3.Distance(b, plug),
                    m == null ? 0 : m.vertexCount, meshCentre, meshMoved,
                    Time.timeScale, fixedSteps - stepsAtLastReport), c);
            }
        }

        void DrawLines()
        {
            PatchCord[] cords = Object.FindObjectsByType<PatchCord>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var c in cords)
            {
                if (c == null || c.rope == null) continue;
                if (c.rope.startAnchor != null && c.rope.endAnchor != null)
                    Debug.DrawLine(c.rope.startAnchor.position, c.rope.endAnchor.position, Color.yellow);

                Transform mine = null;
                if (OwnerOf(c.rope.startAnchor) == c) mine = c.rope.startAnchor;
                else if (OwnerOf(c.rope.endAnchor) == c) mine = c.rope.endAnchor;
                if (mine != null) Debug.DrawLine(c.transform.position, mine.position, Color.cyan);
                else Debug.DrawLine(c.transform.position, c.transform.position + Vector3.up * 0.05f, Color.red);
            }
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Verdict-only pass: one line per cord, and only the broken ones get explained. A 240-line
        /// dump is easy to lose and hard to read; a short list of what is wrong is neither.
        /// </summary>
        [ContextMenu("Report now")]
        public void Report()
        {
            try { ReportInner(); }
            catch (System.Exception e)
            {
                Debug.LogError("[PatchCordDiag] the report itself threw - " + e, this);
            }
        }

        void ReportInner()
        {
            var sb = new StringBuilder();
            sb.AppendLine("========== PATCH CORD VERDICT ==========");
            sb.AppendLine("Time.timeScale " + Time.timeScale + "   physics steps +" +
                          (fixedSteps - stepsAtLastReport) + " since last report");
            if (Time.timeScale == 0f || fixedSteps == stepsAtLastReport)
                sb.AppendLine(">>> PHYSICS IS NOT STEPPING. VerletRope pins its endpoints in " +
                              "FixedUpdate while PatchCord drags in Update, so every cord in the " +
                              "scene is frozen. THIS IS THE BUG - nothing below matters until it's fixed.");
            stepsAtLastReport = fixedSteps;

            PatchCord[] all = Object.FindObjectsByType<PatchCord>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            int ok = 0;
            var bad = new StringBuilder();

            foreach (var c in all)
            {
                string fault = FaultOf(c);
                if (fault == null) { ok++; continue; }
                bad.AppendLine("  " + Path(c.transform) + "\n      " + fault);
            }

            sb.AppendLine(ok + " of " + all.Length + " plugs wired correctly.");
            if (bad.Length > 0) { sb.AppendLine("BROKEN:"); sb.Append(bad); }
            else sb.AppendLine("No wiring fault found - if a cord still lags, it is the physics line above.");
            sb.AppendLine("========================================");
            Debug.LogWarning(sb.ToString(), this);
        }

        /// <summary>Null when this plug is wired correctly, otherwise the specific reason.</summary>
        string FaultOf(PatchCord c)
        {
            if (c.rope == null) return "rope is NULL - this plug has no cord attached to it.";
            VerletRope r = c.rope;
            if (!r.enabled) return "its VerletRope is DISABLED - it switched itself off at Start, which " +
                                   "it only does when an anchor was unassigned.";
            if (!r.gameObject.activeInHierarchy) return "its VerletRope object is INACTIVE.";
            if (r.startAnchor == null || r.endAnchor == null)
                return "an anchor slot is EMPTY, so the rope never simulates.";
            if (r.startAnchor == r.endAnchor)
                return "Start Anchor and End Anchor are the SAME transform (" + r.startAnchor.name +
                       ", id " + r.startAnchor.GetInstanceID() + "). Both plugs' anchors share that " +
                       "name, so the Inspector cannot show you this.";
            PatchCord so = OwnerOf(r.startAnchor), eo = OwnerOf(r.endAnchor);
            if (so != c && eo != c)
                return "neither rope anchor sits under this plug (they belong to " +
                       (so == null ? "<none>" : so.name) + " and " + (eo == null ? "<none>" : eo.name) +
                       "), so dragging it cannot move the cord.";
            if (so != null && so == eo)
                return "both rope anchors belong to the same plug (" + so.name + "), so the other " +
                       "plug drags free of the cord.";
            if (r.transform.localScale != Vector3.one)
                return "its Rope object is SCALED " + r.transform.localScale +
                       " - the tube mesh is built in that object's local space, so it renders wrong.";
            return null;
        }

    }
}