using UnityEngine;

/// <summary>
/// Generic autosave hook: checkpoints the save file every time ANY
/// TaskManager task reaches completion, whatever its taskId is.
///
/// Drop this once per gameplay scene (COC1, COC2, COC3, COC4...) and it
/// covers every task registered in that scene automatically — including
/// ones you add later. You never need to edit this file, or any part
/// script, just to wire up autosave for a new task.
/// </summary>
public class TaskCheckpointAutosaver : MonoBehaviour
{
    private void Start()
    {
        if (TaskManager.Instance != null)
            TaskManager.Instance.OnTaskCompleted += HandleTaskCompleted;
    }

    private void OnDisable()
    {
        if (TaskManager.Instance != null)
            TaskManager.Instance.OnTaskCompleted -= HandleTaskCompleted;
    }

    private void HandleTaskCompleted(string completedTaskId)
    {
        if (SaveManager.Instance == null) return;

        Debug.Log($"[TaskCheckpointAutosaver] '{completedTaskId}' completed — checkpointing.");
        SaveManager.Instance.SaveCheckpoint();
    }
}