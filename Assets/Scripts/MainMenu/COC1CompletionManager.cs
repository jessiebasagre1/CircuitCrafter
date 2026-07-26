using UnityEngine;

/// <summary>
/// Watches for the three things that make up COC1 (assemble, disassemble,
/// OS install) and calls SaveManager.MarkModeComplete("COC1") the moment
/// all three are done. Drop this on any persistent-ish GameObject in the
/// COC1 scene (e.g. alongside PCDisassemblySetup or OSInstallManager).
///
/// Handles two cases:
///   1. Live completion during this session — via TaskManager.OnTaskCompleted
///      and OSInstallManager.OnInstallComplete events.
///   2. Resuming a save where one or two of the three were already finished
///      in an earlier session — checked directly on Start() so this doesn't
///      depend on load order between this script and TaskManager/
///      OSInstallManager's own save restore.
/// </summary>
public class COC1CompletionManager : MonoBehaviour
{
    private const string ModeId = "COC1";

    private bool assembleDone;
    private bool disassembleDone;
    private bool osInstallDone;

    private void Start()
    {
        if (TaskManager.Instance != null)
            TaskManager.Instance.OnTaskCompleted += HandleTaskCompleted;

        if (OSInstallManager.Instance != null)
            OSInstallManager.Instance.OnInstallComplete += HandleInstallComplete;

        // Catch state that was already finished before this listener existed
        // this session (e.g. resumed a save mid-way through COC1).
        if (TaskManager.Instance != null)
        {
            assembleDone = TaskManager.Instance.IsTaskComplete(PCDisassemblySetup.AssembleTaskId);
            disassembleDone = TaskManager.Instance.IsTaskComplete(PCDisassemblySetup.TaskId);
        }

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
        if (completedTaskId == PCDisassemblySetup.AssembleTaskId)
            assembleDone = true;
        else if (completedTaskId == PCDisassemblySetup.TaskId)
            disassembleDone = true;
        else
            return; // not one of the two tasks we care about

        CheckAllComplete();
    }

    private void HandleInstallComplete()
    {
        osInstallDone = true;
        CheckAllComplete();
    }

    private void CheckAllComplete()
    {
        if (!assembleDone || !disassembleDone || !osInstallDone) return;

        if (SaveManager.Instance != null && !SaveManager.Instance.IsModeCompleted(ModeId))
        {
            Debug.Log("[COC1CompletionManager] Assembly + disassembly + OS install all complete — marking COC1 done.");
            SaveManager.Instance.MarkModeComplete(ModeId);
        }
    }
}