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
        if (completedTaskId == taskId && completionDialog != null)
            DialogManager.Instance.ShowDialog(completionDialog);
    }
}