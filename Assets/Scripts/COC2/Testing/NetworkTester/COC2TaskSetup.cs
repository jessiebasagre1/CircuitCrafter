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

        [Header("Steps")]
        [Tooltip("How many crimps count as the crimping task. 1 = the task completes when the whole " +
                 "cable is crimped (CrimpingStationController reports once, after the second end).")]
        public int crimpSteps = 1;
        [Tooltip("How many passing tests count as the testing task.")]
        public int testSteps = 1;
        [Tooltip("How many correctly terminated ports count as the patch panel task. Raise this if " +
                 "you add more ports later - PunchDownStation reports once per passing punch-down.")]
        public int patchPanelSteps = 1;

        void Start()
        {
            if (TaskManager.Instance == null)
            {
                Debug.LogWarning("[COC2TaskSetup] No TaskManager in this scene - COC2's tasks won't be " +
                                 "registered. Add one, or clear the taskId fields on the station and tester.", this);
                return;
            }

            if (!string.IsNullOrEmpty(crimpTaskId))
                TaskManager.Instance.RegisterTask(crimpTaskId, Mathf.Max(1, crimpSteps));

            if (!string.IsNullOrEmpty(testTaskId))
                TaskManager.Instance.RegisterTask(testTaskId, Mathf.Max(1, testSteps));

            if (!string.IsNullOrEmpty(patchPanelTaskId))
                TaskManager.Instance.RegisterTask(patchPanelTaskId, Mathf.Max(1, patchPanelSteps));

            Debug.Log($"[COC2TaskSetup] Registered '{crimpTaskId}', '{testTaskId}' and " +
                      $"'{patchPanelTaskId}' with TaskManager.");
        }
    }
}