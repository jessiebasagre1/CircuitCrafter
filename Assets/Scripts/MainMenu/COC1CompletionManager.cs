using UnityEngine;

/// <summary>
/// Watches for everything that makes up COC1 -- assemble, disassemble, OS
/// install, PLUS the post-install configuration checklist (install drivers,
/// install application software, install updates/patches, test the
/// computer, document the completed work) -- and calls
/// SaveManager.MarkModeComplete("COC1") the moment all of it is done. Drop
/// this on any persistent-ish GameObject in the COC1 scene (e.g. alongside
/// PCDisassemblySetup or OSInstallManager).
///
/// requiredTaskIds replaces the old one-bool-per-item approach: every entry
/// is a TaskManager taskId that must be complete. Add or remove entries here
/// as the module's scope changes -- no other code in this class needs to
/// change. OS install is checked separately since it isn't a TaskManager
/// task (see osInstallDone below).
///
/// Handles two cases:
///   1. Live completion during this session -- via TaskManager.OnTaskCompleted
///      and OSInstallManager.OnInstallComplete events.
///   2. Resuming a save where some items were already finished in an earlier
///      session -- checked directly on Start() so this doesn't depend on
///      load order between this script and TaskManager/OSInstallManager's
///      own save restore.
/// </summary>
public class COC1CompletionManager : MonoBehaviour
{
    private const string ModeId = "COC1";

    [Tooltip("Every TaskManager taskId that must be complete for COC1 to count as finished, besides the OS install itself (checked separately below).")]
    [SerializeField]
    private string[] requiredTaskIds = new string[]
    {
        PCDisassemblySetup.AssembleTaskId,
        PCDisassemblySetup.TaskId,
        SystemConfigTaskIds.InstallDrivers,
        SystemConfigTaskIds.InstallAppSoftware,
        SystemConfigTaskIds.InstallUpdates,
        SystemConfigTaskIds.TestComputer,
        SystemConfigTaskIds.DocumentWork,
    };

    private bool osInstallDone;

    private void Start()
    {
        if (TaskManager.Instance != null)
            TaskManager.Instance.OnTaskCompleted += HandleTaskCompleted;

        if (OSInstallManager.Instance != null)
            OSInstallManager.Instance.OnInstallComplete += HandleInstallComplete;

        // Catch state that was already finished before this listener existed
        // this session (e.g. resumed a save mid-way through COC1).
        if (OSInstallManager.Instance != null)
            osInstallDone = OSInstallManager.Instance.CurrentStage == OSInstallManager.InstallStage.Complete;

        CheckAllComplete();
    }

    private void OnDisable()
    {
        if (TaskManager.Instance != null)
            TaskManager.Instance.OnTaskCompleted -= HandleTaskCompleted;

        if (OSInstallManager.Instance != null)
            OSInstallManager.Instance.OnInstallComplete -= HandleInstallComplete;
    }

    private void HandleTaskCompleted(string completedTaskId)
    {
        // Any completed task might be one we're waiting on -- cheap enough to
        // just recheck everything rather than tracking each id individually.
        CheckAllComplete();
    }

    private void HandleInstallComplete()
    {
        osInstallDone = true;
        CheckAllComplete();
    }

    private void CheckAllComplete()
    {
        if (!osInstallDone) return;

        // Finishing the ASSESSMENT is not finishing Story Mode - it must not unlock the
        // next COC or mark COC1 complete. The assessment's record lives in the CompetencyLedger.
        if (AssessmentMode.IsActive) return;
        if (TaskManager.Instance == null) return;

        foreach (var taskId in requiredTaskIds)
        {
            if (string.IsNullOrEmpty(taskId)) continue;
            if (!TaskManager.Instance.IsTaskComplete(taskId)) return;
        }

        if (SaveManager.Instance != null && !SaveManager.Instance.IsModeCompleted(ModeId))
        {
            Debug.Log("[COC1CompletionManager] Assembly + disassembly + OS install + system configuration all complete - marking COC1 done.");
            SaveManager.Instance.MarkModeComplete(ModeId);
        }
    }
}