using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using CSSSimulation.Networking;

namespace CircuitCrafter.COC3.EditorTools
{
    /// <summary>
    /// Says, in one message, why the COC 3 waypoint arrow is not appearing.
    ///
    /// RUN IT WITH: CircuitCrafter > Audit COC3 Waypoint, with COC 3 open. NO PLAY MODE NEEDED.
    ///
    /// WHY THIS EXISTS RATHER THAN ANOTHER RUNTIME WARNING
    /// Every check written so far had the same blind spot: a component that is switched off cannot
    /// report that it is switched off. So the failure that silences the arrow is exactly the failure
    /// that silences the diagnostics, and the Console ends up saying nothing at all - which reads
    /// identical to the script not being there, to the scene being wrong, and to the build being
    /// stale. Chasing it from the Console means one Play session per guess.
    ///
    /// An Editor menu item has none of those constraints. It runs whether or not anything is active,
    /// reads the scene directly, and can name the one tickbox that is wrong on the first try.
    ///
    /// IT CHANGES NOTHING. It only reads and prints.
    /// </summary>
    public static class COC3WaypointAudit
    {
        [MenuItem("CircuitCrafter/Audit COC3 Waypoint", false, 48)]
        public static void Audit()
        {
            var sb = new StringBuilder();
            sb.AppendLine("========== COC3 WAYPOINT AUDIT ==========");

            TaskWaypointArrow[] arrows = Object.FindObjectsByType<TaskWaypointArrow>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            COC3WaypointBridge[] bridges = Object.FindObjectsByType<COC3WaypointBridge>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            COC3StationController[] controllers = Object.FindObjectsByType<COC3StationController>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            WaypointIndicator[] indicators = Object.FindObjectsByType<WaypointIndicator>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            var problems = new List<string>();

            // ---- the arrow ------------------------------------------------------------
            sb.AppendLine();
            sb.AppendLine($"TaskWaypointArrow     : {arrows.Length} found");

            if (arrows.Length == 0)
                problems.Add("There is no TaskWaypointArrow component in this scene at all. Nothing " +
                             "on this page can work until one exists - add it to an empty object " +
                             "under Managers.");

            foreach (TaskWaypointArrow a in arrows)
            {
                if (a == null) continue;
                sb.AppendLine($"  '{PathOf(a.transform)}'");
                DescribeSwitches(sb, a, problems, "TaskWaypointArrow");

                sb.AppendLine($"    World Arrow        : {Name(a.worldArrow)}");
                sb.AppendLine($"    Screen Arrow       : {Name(a.screenArrow)}");
                sb.AppendLine($"    Label Text         : {Name(a.labelText)}");

                // The Player field is FILLED AT PLAY from the object tagged 'Player', so an empty
                // one here is normal and reporting it as missing would be a false alarm. What
                // actually matters is whether that tagged object exists - which can be answered now.
                sb.AppendLine($"    Player             : {(a.player != null ? "'" + a.player.name + "'" : "empty - will be found at Play from the 'Player' tag")}");
                if (a.player == null)
                {
                    GameObject tagged = GameObject.FindGameObjectWithTag("Player");
                    sb.AppendLine($"      object tagged Player : {(tagged != null ? "'" + tagged.name + "'" : "NONE IN THIS SCENE")}");
                    if (tagged == null)
                        problems.Add("Nothing in this scene is tagged 'Player', and the Player field " +
                                     "is empty, so the arrow has no one to measure distance from. It " +
                                     "will still appear, but it will never get out of the way when the " +
                                     "learner reaches the machine and the distance readout stays at 0 m.");
                }

                if (a.labelText == null)
                    problems.Add($"'{a.name}' has no Label Text. The bridge writes 'Go to the server PC' " +
                                 "/ 'Go to the client PC' into the step every frame and nothing displays " +
                                 "it - which is most of the point in COC 3, where the two machines look " +
                                 "alike. Assign a TMP text on the HUD.");
                sb.AppendLine($"    Steps              : {(a.steps != null ? a.steps.Count : 0)}");

                if (a.worldArrow == null && a.screenArrow == null && a.labelText == null)
                    problems.Add($"'{a.name}' has no World Arrow, Screen Arrow or Label Text, so " +
                                 "even running perfectly it has no way to show the learner anything.");

                if (a.worldArrow != null && a.worldArrow.IsChildOf(a.transform))
                    problems.Add($"World Arrow '{a.worldArrow.name}' is a CHILD of '{a.name}'. It gets " +
                                 "moved around the room every frame, which would drag the bridge with " +
                                 "it. Move it to the scene root.");

                if (a.steps == null || a.steps.Count == 0)
                {
                    problems.Add($"'{a.name}' has an empty Steps list. COC 3 needs exactly one step " +
                                 "for the bridge to drive.");
                }
                else
                {
                    for (int i = 0; i < a.steps.Count; i++)
                    {
                        TaskWaypointArrow.Step s = a.steps[i];
                        if (s == null) { sb.AppendLine($"    Step {i}             : (empty)"); continue; }

                        // Print the field the condition ACTUALLY reads, not just the Target.
                        //
                        // These are two different things and the first version conflated them. Target
                        // is where the arrow points; the witness / task id / cable is what decides
                        // whether the step is finished. A step can point at the right machine and
                        // still be judged by an object nobody assigned - which is invisible if the
                        // report only ever shows the Target.
                        sb.AppendLine($"    Step {i}             : '{s.label}'   done when: {s.doneWhen} " +
                                      $"{JudgedBy(s)}   target: {Name(s.target)}");

                        if (NeedsWitness(s.doneWhen) && s.witness == null)
                            problems.Add($"Step {i} is judged by {s.doneWhen} but its Witness is empty. " +
                                         (s.doneWhen == TaskWaypointArrow.DoneWhen.ObjectActive
                                             ? "That reads as NOT done forever, so the arrow can never finish."
                                             : "That reads as done immediately, so the arrow switches off at once."));

                        // The trap worth naming by itself. With no TaskManager in the scene - and COC 3
                        // has none - TaskComplete returns TRUE, so the step is done on frame one and the
                        // arrow switches off before anyone sees it. Silently.
                        if (s.doneWhen == TaskWaypointArrow.DoneWhen.TaskComplete && !HasTaskManager())
                            problems.Add($"Step {i} is judged by Task Complete, but there is no " +
                                         "TaskManager in COC 3. That condition returns TRUE when no " +
                                         "TaskManager exists, so the step counts as finished on the " +
                                         "first frame and the arrow switches itself off immediately. " +
                                         "Use Object Active on the CompletionScreen, or Manual.");
                    }

                    if (a.steps.Count > 1)
                        sb.AppendLine("    NOTE: COC 3 expects ONE step. The bridge only drives Step Index.");
                }
            }

            // ---- the bridge -----------------------------------------------------------
            sb.AppendLine();
            sb.AppendLine($"COC3WaypointBridge    : {bridges.Length} found");

            if (bridges.Length == 0 && arrows.Length > 0)
                problems.Add("There is a TaskWaypointArrow but no COC3WaypointBridge. Without the " +
                             "bridge nothing moves the step between the server and the client, so the " +
                             "arrow points at whatever Step 0's Target happens to be and never changes.");

            foreach (COC3WaypointBridge b in bridges)
            {
                if (b == null) continue;
                sb.AppendLine($"  '{PathOf(b.transform)}'");
                DescribeSwitches(sb, b, problems, "COC3WaypointBridge");

                // The bridge's fields are private, so read them the way the Inspector does.
                var so = new SerializedObject(b);
                sb.AppendLine($"    Arrow field        : {NameOfRef(so, "arrow")}  (empty is fine - found automatically)");
                sb.AppendLine($"    Controller field   : {NameOfRef(so, "controller")}  (empty is fine - found automatically)");
                sb.AppendLine($"    Step Index         : {so.FindProperty("stepIndex").intValue}");
                sb.AppendLine($"    Hide While Using   : {so.FindProperty("hideWhileUsingAMachine").boolValue}");

                if (b.GetComponent<TaskWaypointArrow>() == null)
                    problems.Add($"The bridge on '{b.name}' has no TaskWaypointArrow on the SAME object. " +
                                 "It looks for one automatically, but keeping them together is what the " +
                                 "setup assumes - and if the arrow lives elsewhere and is inactive, the " +
                                 "bridge will silently find nothing.");
            }

            // ---- the controller -------------------------------------------------------
            sb.AppendLine();
            sb.AppendLine($"COC3StationController : {controllers.Length} found");

            foreach (COC3StationController c in controllers)
            {
                if (c == null) continue;
                sb.AppendLine($"  '{PathOf(c.transform)}'");
                sb.AppendLine($"    Use Waypoint       : {(c.useWaypoint ? "ON" : "off")}");
                sb.AppendLine($"    Server Station     : {Name(c.serverStation)}");
                sb.AppendLine($"    Client Station     : {Name(c.clientStation)}");

                if (c.useWaypoint && bridges.Length > 0)
                    problems.Add("Use Waypoint is ON and a bridge exists. Both will point at the same " +
                                 "machine and you will see two markers. Untick Use Waypoint.");

                if (!c.useWaypoint && bridges.Length == 0)
                    problems.Add("Use Waypoint is off and there is no bridge, so nothing at all is " +
                                 "driving a waypoint in this scene.");

                if (c.serverStation == null || c.clientStation == null)
                    problems.Add("Server Station and/or Client Station is empty on " +
                                 $"'{c.name}', so the bridge has no machine to point at.");
            }

            if (indicators.Length > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"WaypointIndicator     : {indicators.Length} found (the older COC 1 sprite arrow)");
                foreach (WaypointIndicator w in indicators)
                    if (w != null) sb.AppendLine($"  '{PathOf(w.transform)}'  active: {w.gameObject.activeInHierarchy}");
            }

            // ---- verdict ---------------------------------------------------------------
            sb.AppendLine();
            if (problems.Count == 0)
            {
                sb.AppendLine("VERDICT: nothing wrong found. Everything needed is present and switched on.");
                sb.AppendLine("         If the arrow still does not appear in Play, it is being hidden at");
                sb.AppendLine("         runtime rather than missing: assessment mode locks it off for the");
                sb.AppendLine("         whole run, Hide While Using hides it while you are at a machine,");
                sb.AppendLine("         and Arrival Distance hides it once you are within 2.5 m.");
            }
            else
            {
                sb.AppendLine($"VERDICT: {problems.Count} problem(s).");
                for (int i = 0; i < problems.Count; i++)
                    sb.AppendLine($"  {i + 1}. {problems[i]}");
            }

            sb.AppendLine("=========================================");
            Debug.Log(sb.ToString());
        }

        /// <summary>
        /// The four tickboxes, reported together.
        ///
        /// activeSelf and activeInHierarchy are separated on purpose: an object whose own box is
        /// ticked but whose parent's is not reads as "but it IS ticked, I just looked at it", and
        /// naming the ancestor is the difference between a two-second fix and an afternoon.
        /// </summary>
        private static void DescribeSwitches(StringBuilder sb, MonoBehaviour c, List<string> problems, string what)
        {
            bool selfOn = c.gameObject.activeSelf;
            bool inHierarchy = c.gameObject.activeInHierarchy;

            sb.AppendLine($"    GameObject ticked  : {(selfOn ? "yes" : "NO")}");
            sb.AppendLine($"    Active in scene    : {(inHierarchy ? "yes" : "NO")}");
            sb.AppendLine($"    Component ticked   : {(c.enabled ? "yes" : "NO")}");

            if (!inHierarchy)
            {
                Transform culprit = c.transform;
                while (culprit != null && culprit.gameObject.activeSelf) culprit = culprit.parent;

                string where = culprit != null && culprit != c.transform
                    ? $"the object switched off is '{PathOf(culprit)}', ABOVE it"
                    : "its own box is unticked";

                problems.Add($"{what} on '{c.name}' will never run: {where}. Awake does not run on an " +
                             "inactive GameObject, so it never registers, never validates and never " +
                             "prints a word.");
            }
            else if (!c.enabled)
            {
                problems.Add($"{what} on '{c.name}' has its COMPONENT unticked. The object is active, so " +
                             "Awake still runs and it looks present to anything that asks - but Start " +
                             "and Update do not, so it never validates itself and never does its job. " +
                             "This is the one that produces a completely empty Console.");
            }
        }

        /// <summary>The field this condition actually reads, in brackets after the condition name.</summary>
        private static string JudgedBy(TaskWaypointArrow.Step s)
        {
            switch (s.doneWhen)
            {
                case TaskWaypointArrow.DoneWhen.TaskComplete:
                    return $"[task id: {(string.IsNullOrEmpty(s.taskId) ? "BLANK" : "'" + s.taskId + "'")}]";
                case TaskWaypointArrow.DoneWhen.ObjectActive:
                case TaskWaypointArrow.DoneWhen.ObjectInactive:
                    return $"[witness: {Name(s.witness)}" +
                           (s.witness != null ? $", currently {(s.witness.activeInHierarchy ? "ACTIVE" : "inactive")}" : "") + "]";
                case TaskWaypointArrow.DoneWhen.Manual:
                    return "[nothing detects this - CompleteStep must be called]";
                default:
                    return "";
            }
        }

        private static bool NeedsWitness(TaskWaypointArrow.DoneWhen d)
        {
            return d == TaskWaypointArrow.DoneWhen.ObjectActive ||
                   d == TaskWaypointArrow.DoneWhen.ObjectInactive;
        }

        private static bool HasTaskManager()
        {
            return Object.FindAnyObjectByType<TaskManager>(FindObjectsInactive.Include) != null;
        }

        private static string Name(Object o) { return o != null ? "'" + o.name + "'" : "NOT ASSIGNED"; }

        private static string NameOfRef(SerializedObject so, string prop)
        {
            SerializedProperty p = so.FindProperty(prop);
            if (p == null) return "(field not found)";
            return p.objectReferenceValue != null ? "'" + p.objectReferenceValue.name + "'" : "empty";
        }

        private static string PathOf(Transform t)
        {
            string path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }
    }
}