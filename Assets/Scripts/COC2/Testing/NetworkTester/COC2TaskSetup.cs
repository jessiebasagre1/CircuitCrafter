using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Registers COC2's tasks with TaskManager at scene start, the same way
    /// PCDisassemblySetup does it for COC1. Without this, CrimpingStationController.taskId and
    /// CableTesterController.taskId report progress against tasks that were never registered -
    /// TaskManager.ReportProgress() silently returns and nothing ever completes. That's an easy
    /// half-hour to lose, which is why this exists.
    ///
    /// Optional: if you're not gating COC2 behind TaskManager at all, skip this script and leave
    /// both taskId fields blank instead.
    ///
    /// Setup:
    ///   1. Put this on any always-active object in the COC2 scene (the same one holding
    ///      TaskManager is fine).
    ///   2. Make sure the ids here match the taskId fields on CrimpingStationController and
    ///      CableTesterController exactly - they're plain strings, so a typo just silently does
    ///      nothing.
    /// </summary>
    public class COC2TaskSetup : MonoBehaviour
    {
        [Header("Task IDs")]
        [Tooltip("Must match CrimpingStationController.taskId.")]
        public string crimpTaskId = "crimp_cable";
        [Tooltip("Must match CableTesterController.taskId.")]
        public string testTaskId = "test_cable";
        [Tooltip("Must match PunchDownStation.taskId. Clear it if you're not running the punch " +
                 "down stage. Note PunchDownStation also GATES itself on testTaskId being " +
                 "complete, so that one has to stay registered either way.")]
        public string patchPanelTaskId = "punch_patch_panel";

        [Tooltip("The patch CORDS - the phase after the punching, where the panel is corded through " +
                 "to the switch. Set the SAME id on the PatchCordBoard phase's Task Id field, or " +
                 "nothing will report against it.\n\n" +
                 "Worth having as its own task: punch_patch_panel is reported the moment the " +
                 "termination passes, which is before a single cord is plugged in. Without this, " +
                 "'the patch panel is done' means only half the job, and anything waiting on it - " +
                 "a quiz, the module's ending - fires while the learner is still working.\n\n" +
                 "Clear it if the run has no cording phase.")]
        public string patchCordTaskId = "patch_to_switch";

        [Header("Steps")]
        [Tooltip("How many crimps count as the crimping task. 1 = the task completes when the whole " +
                 "cable is crimped (CrimpingStationController reports once, after the second end).")]
        public int crimpSteps = 1;
        [Tooltip("How many passing tests count as the testing task.")]
        public int testSteps = 1;
        [Tooltip("How many correctly terminated ports count as the patch panel task. Raise this if " +
                 "you add more ports later - PunchDownStation reports once per passing punch-down.")]
        public int patchPanelSteps = 1;

        [Tooltip("How many cording phases count as the patch cord task. PatchCordBoard reports once " +
                 "per phase that passes, so set this to the number of PatchCordBoard phases you gave " +
                 "the id to - 1 if only the last one carries it.")]
        public int patchCordSteps = 1;

        void Start()
        {
            if (TaskManager.Instance == null)
            {
                Debug.LogWarning("[COC2TaskSetup] No TaskManager in this scene - COC2's tasks won't be " +
                                 "registered. Add one, or clear the taskId fields on the station and tester.", this);
                return;
            }

            // ---- how many reports each task will ACTUALLY receive ----------
            int crimp = Mathf.Max(1, crimpSteps);
            int test = TestStepsThatCanEverBeReported();
            int panel = Mathf.Max(1, patchPanelSteps);
            int cords = Mathf.Max(1, patchCordSteps);

            if (!string.IsNullOrEmpty(crimpTaskId))
                TaskManager.Instance.RegisterTask(crimpTaskId, crimp);

            if (!string.IsNullOrEmpty(testTaskId))
                TaskManager.Instance.RegisterTask(testTaskId, test);

            if (!string.IsNullOrEmpty(patchPanelTaskId))
                TaskManager.Instance.RegisterTask(patchPanelTaskId, panel);

            if (!string.IsNullOrEmpty(patchCordTaskId))
                TaskManager.Instance.RegisterTask(patchCordTaskId, cords);

            // The STEP COUNTS are printed, not just the names.
            //
            // A step count higher than the number of reports a task will ever receive is the
            // quietest failure in this module: the task sits at 1/2 forever, OnTaskCompleted never
            // fires, and everything waiting on it - a quiz, the module's ending - simply never
            // happens, with nothing in the Console that looks like an error.
            //
            // What reports what, as the scene stands:
            //   crimp_cable       CrimpingStationController, ONCE PER CABLE      -> 2
            //   test_cable        COC2BothCablesGate, ONCE for all cables        -> 1
            //   punch_patch_panel PunchDownStation, once per passing port
            //   patch_to_switch   PatchCordBoard, once per phase carrying the id
            Debug.Log("[COC2TaskSetup] Registered with TaskManager: " +
                      $"'{crimpTaskId}' x{crimp}, " +
                      $"'{testTaskId}' x{test}, " +
                      $"'{patchPanelTaskId}' x{panel}, " +
                      $"'{patchCordTaskId}' x{cords}.");
        }

        /// <summary>
        /// How many times the testing task can actually be reported - which is not always what Test
        /// Steps says.
        ///
        /// WHY THIS OVERRIDES THE INSPECTOR
        /// COC2BothCablesGate counts the cable types itself and reports the task ONCE, when the last
        /// one passes. If it is in the scene, any Test Steps above 1 describes reports that will
        /// never arrive: the task stops at 1/2, the cabling quiz never runs, and COC2 can never end.
        ///
        /// Warning about it was not enough - the number is easy to miss, and an edit made during
        /// Play Mode is discarded the moment you press Stop, so the field can look correct in the
        /// editor and still be 2 on the next run. A value that is wrong whenever the gate exists is
        /// not really a setting, so it is corrected here and said out loud.
        ///
        /// The field still applies when there is NO gate in the scene - then the tester reports per
        /// cable and the count is yours to set.
        /// </summary>
        int TestStepsThatCanEverBeReported()
        {
            int wanted = Mathf.Max(1, testSteps);
            if (wanted <= 1) return wanted;

            COC2BothCablesGate gate =
                Object.FindFirstObjectByType<COC2BothCablesGate>(FindObjectsInactive.Include);

            bool gateOwnsThisTask =
                gate != null &&
                !string.IsNullOrEmpty(gate.taskId) &&
                string.Equals(gate.taskId, testTaskId, System.StringComparison.OrdinalIgnoreCase);

            if (!gateOwnsThisTask) return wanted;

            Debug.LogWarning("[COC2TaskSetup] Test Steps is " + wanted + ", but COC2BothCablesGate on '" +
                             gate.name + "' waits for every cable type itself and reports '" + testTaskId +
                             "' ONCE. Registering it with 1 step instead, so the task can actually " +
                             "complete.\n\nSet Test Steps to 1 on this component to silence this - and note " +
                             "that an Inspector edit made while the game is PLAYING is thrown away when you " +
                             "press Stop.", this);

            return 1;
        }
    }
}