using UnityEngine;

namespace CircuitCrafter.Assessment
{
    /// <summary>
    /// Registers the OS installation as a real TaskManager task, so it can be
    /// listed as evidence on a CompetencyDefinition like everything else.
    ///
    /// ATTACH THIS TO: the Managers object in the COC1 scene, beside
    /// OSInstallManager.
    ///
    /// WHY IT'S NEEDED
    /// Installing the operating system is the single biggest thing that happens
    /// in COC1, and it is the ONE major step that never becomes a TaskManager
    /// task. OSInstallManager announces itself with its own OnInstallComplete
    /// event instead, which is why COC1CompletionManager has to check
    /// osInstallDone separately from its requiredTaskIds list.
    ///
    /// That's fine for COC1CompletionManager, which knows about both. It is not
    /// fine for a CompetencyDefinition, which can only name task ids - without
    /// this bridge, "Install the operating system" is an outcome with no
    /// possible evidence, and a learner who installs Windows perfectly stays at
    /// Practised forever.
    ///
    /// Twenty lines here beats editing OSInstallManager. It also picks up an
    /// install finished in an EARLIER session: Start() checks CurrentStage
    /// directly, so resuming a save where the OS was already installed still
    /// reports the task rather than waiting for an event that will never fire
    /// again. Same two-case handling COC1CompletionManager already does.
    /// </summary>
    public class OSInstallTaskBridge : MonoBehaviour
    {
        /// <summary>The task id the OS install reports to. Put this exact string
        /// in the evidence list of your COC1 "install the operating system"
        /// outcome.</summary>
        public const string TaskId = "install_os";

        [Tooltip("The task id reported when the OS install finishes. Only change this if you also change it in the CompetencyDefinition asset.")]
        [SerializeField] private string taskId = TaskId;

        private bool reported;

        private void Start()
        {
            if (TaskManager.Instance == null)
            {
                Debug.LogWarning("[OSInstallTaskBridge] No TaskManager in this scene - the OS install won't be recorded as evidence.", this);
                return;
            }

            TaskManager.Instance.RegisterTask(taskId, 1);

            if (OSInstallManager.Instance == null)
            {
                Debug.LogWarning("[OSInstallTaskBridge] No OSInstallManager in this scene - nothing to bridge.", this);
                return;
            }

            OSInstallManager.Instance.OnInstallComplete += HandleInstallComplete;

            // Case 2: the install already finished in an earlier session and the
            // save has just been restored. The event fired long ago, so check the
            // stage directly rather than waiting for something that won't happen.
            if (OSInstallManager.Instance.CurrentStage == OSInstallManager.InstallStage.Complete)
                HandleInstallComplete();
        }

        private void OnDisable()
        {
            if (OSInstallManager.Instance != null)
                OSInstallManager.Instance.OnInstallComplete -= HandleInstallComplete;
        }

        private void HandleInstallComplete()
        {
            // ReportProgress on an already-complete task is a harmless no-op, but
            // the guard keeps the log clean when both the event and the Start()
            // catch-up fire in the same frame.
            if (reported) return;
            reported = true;

            TaskManager.Instance.ReportProgress(taskId);
            Debug.Log($"[OSInstallTaskBridge] OS install complete - reported '{taskId}'.");
        }
    }
}