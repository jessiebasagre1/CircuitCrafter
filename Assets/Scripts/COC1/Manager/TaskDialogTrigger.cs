using UnityEngine;

public class TaskDialogTrigger : MonoBehaviour
{
    [SerializeField] private string taskId = "disassemble_pc";
    [SerializeField] private DialogSequence completionDialog;

    private void Start()
    {
        TaskManager.Instance.OnTaskCompleted += HandleTaskCompleted;
    }

    private void OnDisable()
    {
        if (TaskManager.Instance != null)
            TaskManager.Instance.OnTaskCompleted -= HandleTaskCompleted;
    }

    private void HandleTaskCompleted(string completedTaskId)
    {
        if (completedTaskId != taskId || completionDialog == null) return;

        // TaskManager.RestoreState() re-fires OnTaskCompleted for every already-
        // complete task on every Continue (other systems rely on that replay to
        // re-route the waypoint on load). Without this guard, a player who
        // finished this task in a previous session would get the completion
        // dialog shown again on every subsequent Continue, blocking interaction
        // the same way the intro dialog did (see IntroDialogTrigger). Only show
        // it for a genuine, live completion.
        if (SaveManager.Instance != null && SaveManager.Instance.IsRestoringSave) return;

        DialogManager.Instance.ShowDialog(completionDialog);
    }
}