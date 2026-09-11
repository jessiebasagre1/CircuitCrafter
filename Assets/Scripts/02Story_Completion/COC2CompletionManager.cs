using UnityEngine;

/// <summary>
/// Marks COC2 finished in the save the moment every part of it is done, so the main menu unlocks
/// COC3.
///
/// ATTACH THIS TO: any always-active object in the COC2 scene - Managers is the obvious one.
///
/// WHY THIS EXISTS
/// COC1 has COC1CompletionManager and COC3 has COC3CompletionManager, and both call
/// SaveManager.MarkModeComplete. COC2 has neither, so a learner can finish the whole module -
/// crimp, test, punch down, cord through to the switch, configure both machines, ping
/// successfully - and completedModes never gains "COC2". The next module stays locked with
/// nothing anywhere to explain why.
///
/// It is deliberately NOT the same thing as StoryCompleteTrigger. That one shows a panel; this
/// one writes to the save file. Keeping them apart means the panel can be restyled, moved or
/// removed without touching what the save records, and a module that is marked complete stays
/// marked complete even if the learner closes the panel without reading it.
///
/// TWO CASES, BOTH HANDLED
///   1. Finishing live this session - via TaskManager.OnTaskCompleted.
///   2. Resuming a save where some of it was already done - checked directly in Start(), so this
///      does not depend on load order between this script and TaskManager's own save restore.
/// </summary>
public class COC2CompletionManager : MonoBehaviour
{
    private const string ModeId = "COC2";

    [Tooltip("Every TaskManager taskId that must be complete for COC2 to count as finished.\n\n" +
             "These must match the ids actually registered in the scene - the cabling and patch " +
             "panel ones live on COC2TaskSetup, and configure_network / verify_connectivity on the " +
             "stations themselves. An id that is never registered can never complete, and COC2 " +
             "would then never be marked done.\n\n" +
             "patch_to_switch matters as much as punch_patch_panel: the punching is reported the " +
             "moment the termination passes, which is before a single cord reaches the switch.")]
    [SerializeField]
    private string[] requiredTaskIds = new string[]
    {
        "crimp_cable",
        "test_cable",
        "punch_patch_panel",
        "patch_to_switch",
        "configure_network",
        "verify_connectivity",
    };

    [Tooltip("Leave on. Prints which tasks are still outstanding whenever one completes, so a " +
             "module that refuses to finish tells you which id it is still waiting for instead of " +
             "staying silent.")]
    [SerializeField] private bool logWhatIsMissing = true;

    private void Start()
    {
        if (TaskManager.Instance != null)
            TaskManager.Instance.OnTaskCompleted += HandleTaskCompleted;
        else
            Debug.LogWarning("[COC2CompletionManager] No TaskManager in this scene, so COC2 can never be marked complete.", this);

        // Catch work that was already finished before this listener existed - resuming a save
        // part-way through COC2.
        CheckAllComplete();
    }

    private void OnDisable()
    {
        if (TaskManager.Instance != null)
            TaskManager.Instance.OnTaskCompleted -= HandleTaskCompleted;
    }

    private void HandleTaskCompleted(string completedTaskId)
    {
        // Any completed task might be one we are waiting on - cheap enough to recheck everything
        // rather than tracking each id separately.
        CheckAllComplete();
    }

    private void CheckAllComplete()
    {
        if (TaskManager.Instance == null) return;
        if (SaveManager.Instance == null) return;
        if (SaveManager.Instance.IsModeCompleted(ModeId)) return;

        string missing = null;

        foreach (string taskId in requiredTaskIds)
        {
            if (string.IsNullOrWhiteSpace(taskId)) continue;

            // Trimmed: a space typed into the Inspector is invisible, and an id that does not
            // match exactly can never be found complete - COC2 would then never be marked done.
            string id = taskId.Trim();
            if (TaskManager.Instance.IsTaskComplete(id)) continue;

            missing = missing == null ? id : missing + ", " + id;
        }

        if (missing != null)
        {
            if (logWhatIsMissing)
                Debug.Log("[COC2CompletionManager] Not finished yet - still waiting on: " + missing, this);
            return;
        }

        Debug.Log("[COC2CompletionManager] Cabling, patch panel, configuration and connectivity all complete - marking COC2 done.", this);
        SaveManager.Instance.MarkModeComplete(ModeId);
    }
}