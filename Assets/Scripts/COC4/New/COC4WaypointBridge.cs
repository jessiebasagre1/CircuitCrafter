// COC4WaypointBridge.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using CSSSimulation.Networking;
using CircuitCrafter.Networking.Unity;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// Drives the TaskWaypointArrow (the arrow, screen arrow and step label used in COC 1, 2 and 3) at a
    /// COC 4 repair site, so it always points at the place for the learner's current step:
    ///
    ///   CONFIRM  - the monitor              "Go to the monitor and switch the computer on"
    ///   EXAMINE  - the system unit          "Open the system unit and look inside"
    ///   DIAGNOSE - (no arrow)               "Diagnose: press F (or click Diagnose) and choose the cause"
    ///              network: the client PC   "Check the network settings on CLIENT01, then press F ..."
    ///   REPAIR   - the brush first (dust),  "Pick up the cleaning brush"
    ///              then the system unit     "Repair the fault inside the system unit"
    ///              or the network PC        "Correct the network settings on CLIENT01"
    ///   TEST     - the monitor              "Test it: switch the computer on again at the monitor"
    ///
    /// ATTACH THIS TO: the same object as TaskWaypointArrow in each COC 4 repair-site scene. It also adds
    /// itself there when a Story Mode ticket loads a scene that has a TaskWaypointArrow and no bridge -
    /// add it yourself only to change the labels.
    ///
    /// WHY ONE STEP, NOT A STEP LIST (the same reason as COC3WaypointBridge)
    /// TaskWaypointArrow walks a fixed, ordered list. A COC 4 job is not fixed: a return visit starts at
    /// the repair, a dust ticket adds a trip to fetch the brush, a failed test sends the learner back
    /// inside, and a network ticket has no monitor at all. COC4StepGuide already works out the current
    /// step from what the learner has actually done, so the arrow uses ONE step and this moves it. The
    /// arrow keeps everything it is good at - the world marker, the off-screen screen arrow, the label,
    /// the distance, hiding when the player arrives - and TaskWaypointArrow is not modified.
    ///
    /// TWO WAYS TO USE IT (Step Mode):
    ///   One Moving Step (default) - leave TaskWaypointArrow's Steps empty. This component makes one step
    ///     and moves it for every stage above, with its own labels.
    ///   My Three Steps - you write THREE steps yourself in TaskWaypointArrow's Steps list, each with
    ///     Done When = Manual, your own Label and Target. This component only ticks them off:
    ///       Step 1 (e.g. "Go to the PC", Target: monitor)        done once the computer has been switched on
    ///       Step 2 (e.g. "Go to the system unit", Target: unit)  done once the fault is repaired
    ///       Step 3 (e.g. "Go to the PC to check", Target: monitor) done once the test start-up passes
    ///     A step goes back to not-done if the state goes back (a part pulled out again after the repair),
    ///     so the arrow never points at the wrong place. A bridge that adds itself picks this mode when it
    ///     finds exactly three Manual steps that all have a Target.
    ///
    /// NEEDS COC4StepGuide running (it adds itself to every Story Mode ticket site). To turn off the
    /// guide's dialogs but keep the arrow, untick the guide's Site Steps and Site Replies - not the
    /// whole component.
    ///
    /// STORY MODE ONLY. In the COC 4 assessment this does nothing, and TaskWaypointArrow switches itself
    /// off for the graded run (unless its Allow In Assessment is ticked), exactly as in COC 1 and COC 3.
    /// Outside a ticket (normal campaign play in a shared scene) it does nothing either, so a scene's own
    /// step list is left alone.
    /// </summary>
    [DefaultExecutionOrder(250)]   // before TaskWaypointArrow (300), so the arrow uses the target set this frame
    [DisallowMultipleComponent]
    public class COC4WaypointBridge : MonoBehaviour
    {
        // ==================================================================
        // Adds itself to repair sites that have a TaskWaypointArrow
        // ==================================================================

        /// <summary>Set false before a scene loads to stop the bridge adding itself.</summary>
        public static bool AutoInstall = true;

        /// <summary>True while a bridge is pointing the TaskWaypointArrow. The guide and the old
        /// WaypointIndicator step aside while this is true.</summary>
        public static bool IsDriving { get { return driver != null; } }

        static COC4WaypointBridge driver;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            driver = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void InstallHook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            AddIfNeeded(SceneManager.GetActiveScene());
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            AddIfNeeded(scene);
        }

        static void AddIfNeeded(Scene scene)
        {
            if (!AutoInstall || !scene.IsValid() || !scene.isLoaded) return;
            if (!IsStoryTicket()) return;
            if (FindAnyObjectByType<COC4WaypointBridge>(FindObjectsInactive.Include) != null) return;

            TaskWaypointArrow arrow = FindAnyObjectByType<TaskWaypointArrow>(FindObjectsInactive.Include);
            if (arrow == null) return;   // no arrow in this scene - nothing to drive

            COC4WaypointBridge bridge = arrow.gameObject.AddComponent<COC4WaypointBridge>();

            // Three hand-written Manual steps with targets: the designer's own list - keep it.
            if (LooksLikeThreeSteps(arrow)) bridge.stepMode = StepMode.MyThreeSteps;
        }

        static bool LooksLikeThreeSteps(TaskWaypointArrow arrow)
        {
            if (arrow.steps == null || arrow.steps.Count != 3) return false;
            foreach (TaskWaypointArrow.Step s in arrow.steps)
                if (s == null || s.doneWhen != TaskWaypointArrow.DoneWhen.Manual || s.target == null) return false;
            return true;
        }

        static bool IsStoryTicket()
        {
            return RepairTicketMode.IsActive && !COC4AssessmentRun.IsActive && !AssessmentMode.IsActive;
        }

        // ==================================================================
        // Inspector
        // ==================================================================

        [Header("Wiring - found automatically if left empty")]
        [Tooltip("The scene's TaskWaypointArrow. Empty: the one on this object, or the one in the scene.")]
        public TaskWaypointArrow arrow;

        /// <summary>How the arrow's Steps list is used. See the notes at the top of this file.</summary>
        public enum StepMode
        {
            /// <summary>One step, moved by this component to the place for each stage (the default).</summary>
            OneMovingStep,
            /// <summary>The three steps you wrote in TaskWaypointArrow. This component only ticks them off.</summary>
            MyThreeSteps
        }

        [Header("The arrow's steps")]
        [Tooltip("ONE MOVING STEP: leave TaskWaypointArrow's Steps empty - one step is made and moved for you.\n\n" +
                 "MY THREE STEPS: write three steps in TaskWaypointArrow, each Done When = Manual:\n" +
                 "  1. Go to the PC            (Target: the monitor)      - done once the computer is switched on\n" +
                 "  2. Go to the system unit   (Target: the system unit)  - done once the fault is repaired\n" +
                 "  3. Go to the PC to check   (Target: the monitor)      - done once the test start-up passes\n" +
                 "Your labels and targets are used as written.")]
        public StepMode stepMode = StepMode.OneMovingStep;

        [Tooltip("One Moving Step only. During a ticket, replace the arrow's Steps list with the single step COC 4 needs. Leave on - " +
                 "a list copied from another COC points at the wrong things here. Only for this visit; " +
                 "nothing is saved into the scene.")]
        public bool useOnlyOneStep = true;

        [Tooltip("One Moving Step only, with Use Only One Step off: which entry of the Steps list this drives.")]
        [Min(0)] public int stepIndex = 0;

        [Header("Labels for One Moving Step (My Three Steps uses your own)")]
        [Tooltip("Tokens in any label: {interact}, {leave}, {diagnose}, {grab}, {click} - worded for PC or touch.")]
        public string confirmLabel = "Go to the monitor and switch the computer on";
        public string examineLabel = "Open the system unit and look inside";
        public string diagnoseLabel = "Diagnose: {diagnose} and choose the cause";
        [Tooltip("Step 3 at a network site, where the arrow points at the computer to check. Tokens: {host}.")]
        public string diagnoseNetworkLabel = "Check the network settings on {host}, then {diagnose}";
        [Tooltip("Tokens: {tool}.")]
        public string getToolLabel = "Pick up {tool}";
        public string repairLabel = "Repair the fault inside the system unit";
        [Tooltip("Tokens: {host}.")]
        public string repairNetworkLabel = "Correct the network settings on {host}";
        public string testLabel = "Test it: switch the computer on again at the monitor";
        [Tooltip("Once the repair has passed its test. The arrow itself is hidden.")]
        public string doneLabel = "Job complete";

        [Header("Hiding the arrow")]
        [Tooltip("Hide the arrow (not the label) while the learner is using the monitor or inside the system " +
                 "unit - a marker hovering over the thing they are already looking at is clutter.")]
        public bool hideWhileAtAStation = true;

        [Tooltip("Hide the arrow while the diagnosis panel is open.")]
        public bool hideWhileDiagnosing = true;

        [Tooltip("Keep the old COC 1 WaypointIndicator hidden, so there are never two arrows. The guide and " +
                 "the ticket intro panel still move it; this switches it off again every frame.")]
        public bool hideOldWaypointIndicator = true;

        [Header("Diagnostics")]
        public bool logChanges = true;

        // ==================================================================
        // Runtime
        // ==================================================================

        TaskWaypointArrow.Step step;
        COC4StepGuide guide;
        TicketDiagnosisPanel diagnosisPanel;
        SystemUnitInspection[] inspections = new SystemUnitInspection[0];
        RepairStation anyStation;
        ToolBelt toolBelt;

        string lastStepName;
        Transform lastTarget;
        bool lastTouch;
        string cachedLabel = "";
        bool hideApplied;
        bool lastHide;
        bool warnedNoGuide;
        int lastNumber = -1;

        /// <summary>The label currently given to the arrow. For tests and debugging.</summary>
        public string CurrentLabel { get { return cachedLabel; } }

        // ==================================================================

        void Start()
        {
            if (!IsStoryTicket())
            {
                // Normal campaign play in a shared scene, or an assessment: the arrow keeps its own
                // steps, and in an assessment it switches itself off.
                enabled = false;
                return;
            }

            if (driver != null && driver != this)
            {
                Debug.LogWarning("[COC4WaypointBridge] A second bridge on '" + name + "' - the one on '" +
                                 driver.name + "' is already driving the arrow. This one stands by.", this);
                enabled = false;
                return;
            }

            if (arrow == null) arrow = GetComponent<TaskWaypointArrow>();
            if (arrow == null) arrow = TaskWaypointArrow.Instance;
            if (arrow == null) arrow = FindAnyObjectByType<TaskWaypointArrow>(FindObjectsInactive.Include);
            if (arrow == null)
            {
                Debug.LogWarning("[COC4WaypointBridge] No TaskWaypointArrow in this scene, so there is no arrow to " +
                                 "drive. Add one (see the setup notes at the top of COC4WaypointBridge).", this);
                enabled = false;
                return;
            }

            if (arrow.steps == null) arrow.steps = new List<TaskWaypointArrow.Step>();

            if (stepMode == StepMode.MyThreeSteps)
            {
                if (!SetUpMySteps())
                {
                    enabled = false;
                    return;
                }
                FindSiteParts();
                driver = this;
                if (logChanges)
                    Debug.Log("[COC4WaypointBridge] Ticking off your three steps on TaskWaypointArrow '" + arrow.name +
                              "' for ticket '" + RepairTicketMode.ActiveTicketId + "': 1 '" + arrow.steps[0].label +
                              "', 2 '" + arrow.steps[1].label + "', 3 '" + arrow.steps[2].label + "'.", this);
                return;
            }

            if (useOnlyOneStep)
            {
                if (arrow.steps.Count != 1 && logChanges)
                    Debug.Log("[COC4WaypointBridge] The arrow had " + arrow.steps.Count + " step(s); using one step " +
                              "for this COC 4 visit instead. The scene itself is not changed.", this);
                if (arrow.steps.Count != 1 || arrow.steps[0] == null)
                    arrow.steps = new List<TaskWaypointArrow.Step> { new TaskWaypointArrow.Step() };
                stepIndex = 0;
            }
            else
            {
                while (arrow.steps.Count <= stepIndex) arrow.steps.Add(new TaskWaypointArrow.Step());
                if (arrow.steps[stepIndex] == null) arrow.steps[stepIndex] = new TaskWaypointArrow.Step();
            }

            // A step that is never "done": this component decides where it points, and the arrow hides
            // itself when there is no target.
            step = arrow.steps[stepIndex];
            step.doneWhen = TaskWaypointArrow.DoneWhen.Manual;
            step.manualDone = false;
            step.latched = false;
            step.latchWhenDone = false;
            step.hideArrowDuringStep = false;
            step.label = "";
            step.target = FirstTarget();   // so the arrow's setup check has something to look at

            FindSiteParts();
            driver = this;

            if (logChanges)
                Debug.Log("[COC4WaypointBridge] Driving TaskWaypointArrow '" + arrow.name + "' for ticket '" +
                          RepairTicketMode.ActiveTicketId + "'. (Its one Manual step is never completed on " +
                          "purpose - this component moves it.)", this);
        }

        void FindSiteParts()
        {
            guide = COC4StepGuide.Instance;
            diagnosisPanel = FindAnyObjectByType<TicketDiagnosisPanel>(FindObjectsInactive.Include);
            inspections = FindObjectsByType<SystemUnitInspection>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            anyStation = FindAnyObjectByType<RepairStation>(FindObjectsInactive.Include);
            toolBelt = ToolBelt.Instance != null ? ToolBelt.Instance : FindAnyObjectByType<ToolBelt>(FindObjectsInactive.Include);
        }

        /// <summary>Checks the three hand-written steps. Labels and targets are left exactly as written.</summary>
        bool SetUpMySteps()
        {
            int count = arrow.steps.Count;
            if (count < 3)
            {
                Debug.LogWarning("[COC4WaypointBridge] Step Mode is My Three Steps, but TaskWaypointArrow '" + arrow.name +
                                 "' has " + count + " step(s). Add three: Go to the PC (monitor), Go to the system " +
                                 "unit, Go to the PC to check - each with Done When = Manual. The arrow is left alone.", this);
                return false;
            }
            if (count > 3)
                Debug.LogWarning("[COC4WaypointBridge] TaskWaypointArrow '" + arrow.name + "' has " + count + " steps. " +
                                 "Only the first three are ticked off here; the rest would be pointed at after the job " +
                                 "is done. Remove them.", this);

            for (int i = 0; i < 3; i++)
            {
                TaskWaypointArrow.Step s = arrow.steps[i];
                if (s == null)
                {
                    Debug.LogWarning("[COC4WaypointBridge] Step " + (i + 1) + " of TaskWaypointArrow '" + arrow.name +
                                     "' is empty. The arrow is left alone.", this);
                    return false;
                }
                if (s.doneWhen != TaskWaypointArrow.DoneWhen.Manual)
                {
                    Debug.LogWarning("[COC4WaypointBridge] Step " + (i + 1) + " ('" + s.label + "') has Done When = " +
                                     s.doneWhen + ". COC 4 ticks it off from here, so it is used as Manual. Set it " +
                                     "to Manual in the Inspector to stop this message.", this);
                    s.doneWhen = TaskWaypointArrow.DoneWhen.Manual;
                }
                if (s.target == null)
                    Debug.LogWarning("[COC4WaypointBridge] Step " + (i + 1) + " ('" + s.label + "') has no Target, so " +
                                     "the arrow has nothing to point at on that step. Drag the " +
                                     (i == 1 ? "system unit" : "monitor") + " into it.", this);
                s.manualDone = false;
                s.latched = false;
            }
            return true;
        }

        /// <summary>
        /// My Three Steps: ticks the designer's steps off from the step the guide says the learner is on.
        /// Set every frame, not latched, so a step comes back if the learner undoes it.
        /// </summary>
        void UpdateMySteps()
        {
            if (driver == null) driver = this;
            if (guide == null) guide = COC4StepGuide.Instance;
            int n = guide != null && guide.isActiveAndEnabled ? guide.CurrentStepNumber : 0;

            if (n == 0)
            {
                if (!warnedNoGuide)
                {
                    warnedNoGuide = true;
                    Debug.LogWarning("[COC4WaypointBridge] COC4StepGuide is not running here, so the steps cannot be " +
                                     "ticked off and the arrow stays hidden. To silence the guide but keep the arrow, " +
                                     "untick its Site Steps and Site Replies rather than the whole component.", this);
                }
                ApplyHide(true);
                return;
            }

            // The guide's step: 1 Confirm, 2 Examine, 3 Diagnose, 4 Repair, 5 Test, 6 done.
            SetDone(0, n >= 2);   // Go to the PC            - the computer has been switched on
            SetDone(1, n >= 5);   // Go to the system unit   - the fault is repaired
            SetDone(2, n >= 6);   // Go to the PC to check   - the test start-up passed

            if (n != lastNumber)
            {
                if (logChanges)
                    Debug.Log("[COC4WaypointBridge] Job step " + n + " - the arrow is on " +
                              (n >= 6 ? "nothing (all three steps done)" : "step " + (n >= 5 ? 3 : n >= 2 ? 2 : 1)) + ".", this);
                lastNumber = n;
            }

            ApplyHide(HideNow());
        }

        void SetDone(int index, bool done)
        {
            TaskWaypointArrow.Step s = arrow.steps[index];
            if (s != null) s.manualDone = done;
        }

        bool HideNow()
        {
            return COC4TicketIntroPanel.IsShowing ||
                   (hideWhileAtAStation && AtAStation()) ||
                   (hideWhileDiagnosing && diagnosisPanel != null && diagnosisPanel.IsOpen);
        }

        void OnDisable()
        {
            if (driver == this) driver = null;
        }

        void OnDestroy()
        {
            if (driver == this) driver = null;
        }

        void Update()
        {
            if (arrow == null) return;
            if (stepMode == StepMode.MyThreeSteps)
            {
                UpdateMySteps();
                return;
            }
            if (step == null) return;
            if (driver == null) driver = this;   // re-enabled after being switched off

            if (guide == null) guide = COC4StepGuide.Instance;
            string stepName = guide != null && guide.isActiveAndEnabled ? guide.CurrentStepName : "";

            if (string.IsNullOrEmpty(stepName))
            {
                if (!warnedNoGuide)
                {
                    warnedNoGuide = true;
                    Debug.LogWarning("[COC4WaypointBridge] COC4StepGuide is not running here, so the current step is " +
                                     "unknown and the arrow stays hidden. To silence the guide but keep the arrow, " +
                                     "untick its Site Steps and Site Replies rather than the whole component.", this);
                }
                step.target = null;
                SetLabel("");
                ApplyHide(true);
                return;
            }

            Transform target = stepName == "Done" ? null : guide.CurrentTarget;
            step.target = target;

            bool touch = COC4Controls.IsTouch;
            if (stepName != lastStepName || target != lastTarget || touch != lastTouch)
            {
                if (logChanges && stepName != lastStepName)
                    Debug.Log("[COC4WaypointBridge] Step: " + stepName + " -> " +
                              (target != null ? target.name : "no target (arrow hidden)") + ".", this);
                lastStepName = stepName;
                lastTarget = target;
                lastTouch = touch;
                SetLabel(LabelFor(stepName, target));
            }

            ApplyHide(HideNow());
        }

        void LateUpdate()
        {
            // The old COC 1 arrow is still moved by the guide and the intro panel; switch it straight
            // back off so only the TaskWaypointArrow shows. Nothing is drawn between Update and here.
            if (hideOldWaypointIndicator && WaypointIndicator.Instance != null)
                WaypointIndicator.Instance.ClearTarget();
        }

        // ==================================================================

        /// <summary>Only on a change, so a station that suppresses the arrow itself is not overruled every frame.</summary>
        void ApplyHide(bool hide)
        {
            if (hideApplied && hide == lastHide) return;
            hideApplied = true;
            lastHide = hide;
            arrow.SetSuppressed(hide);
        }

        void SetLabel(string label)
        {
            cachedLabel = label ?? "";
            step.label = cachedLabel;
        }

        bool AtAStation()
        {
            if (RepairStation.Current != null) return true;
            foreach (SystemUnitInspection i in inspections)
                if (i != null && i.IsOpen) return true;
            return false;
        }

        string LabelFor(string stepName, Transform target)
        {
            var vars = new Dictionary<string, string>();
            string line;
            switch (stepName)
            {
                case "Confirm": line = confirmLabel; break;
                case "Examine": line = examineLabel; break;
                case "Diagnose":
                    {
                        NetworkedWorkstation w = target != null ? target.GetComponent<NetworkedWorkstation>() : null;
                        if (w != null) { vars["host"] = HostName(w); line = diagnoseNetworkLabel; }
                        else line = diagnoseLabel;
                        break;
                    }
                case "GetTool":
                    {
                        ToolPickup tool = target != null ? target.GetComponent<ToolPickup>() : null;
                        vars["tool"] = tool != null ? tool.DisplayName : "the tool";
                        line = getToolLabel;
                        break;
                    }
                case "Repair":
                    {
                        NetworkedWorkstation w = target != null ? target.GetComponent<NetworkedWorkstation>() : null;
                        if (w != null) { vars["host"] = HostName(w); line = repairNetworkLabel; }
                        else line = repairLabel;
                        break;
                    }
                case "Test": line = testLabel; break;
                case "Done": line = doneLabel; break;
                default: line = ""; break;
            }
            return COC4Controls.Fill(line, vars, COC4Controls.Tokens(anyStation, toolBelt));
        }

        static string HostName(NetworkedWorkstation w)
        {
            return w != null && w.Host != null && !string.IsNullOrWhiteSpace(w.Host.HostName)
                ? w.Host.HostName.Trim() : "the computer";
        }

        /// <summary>Somewhere sensible before the guide has worked out the step: the monitor, or the network PC.</summary>
        Transform FirstTarget()
        {
            PCPowerStation power = FindAnyObjectByType<PCPowerStation>(FindObjectsInactive.Include);
            RepairStation monitor = power != null ? COC4Controls.Field<RepairStation>(power, "monitorStation") : null;
            if (monitor != null)
            {
                Transform p = COC4Controls.Field<Transform>(monitor, "stationPoint");
                return p != null ? p : monitor.transform;
            }

            foreach (NetworkFault f in FindObjectsByType<NetworkFault>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                NetworkedWorkstation w = f != null ? COC4Controls.Field<NetworkedWorkstation>(f, "station") : null;
                if (w != null) return w.transform;
            }
            return transform;
        }
    }
}