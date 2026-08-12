using UnityEngine;

/// <summary>
/// Fires ONE quiz once the player has both fully disassembled AND fully
/// reassembled the PC (PCDisassemblySetup.TaskId + AssembleTaskId), instead
/// of relying on QuizManager's default one-quiz-per-task auto-match.
///
/// IMPORTANT: do NOT add a QuizSetData with taskId "disassemble_pc" or
/// "assemble_pc" to QuizManager's own Quiz Sets list -- if you do, that quiz
/// will fire separately (and early, right after disassembly) on top of this
/// one. Assign your combined quiz asset only in the field below; its
/// `taskId` field is never used for auto-matching here since we call
/// QuizManager.StartQuiz() directly.
///
/// Drop this on the same GameObject as PCDisassemblySetup / QuizManager
/// (same idea as COC1CompletionManager).
/// </summary>
public class PCBuildQuizTrigger : MonoBehaviour
{
    [Tooltip("Quiz to run once the player has removed AND reinstalled every part.")]
    [SerializeField] private QuizSetData combinedQuizSet;

    private bool assembleDone;
    private bool disassembleDone;
    private bool quizFired;

    private void Start()
    {
        if (TaskManager.Instance != null)
            TaskManager.Instance.OnTaskCompleted += HandleTaskCompleted;

        // Catch up on a resumed save where both were already finished in an
        // earlier session. TaskManager.RestoreState re-invokes
        // OnTaskCompleted for tasks that are already complete (so late
        // subscribers can catch up) -- without this guard that would pop
        // the quiz again every time the player reloads a finished save.
        if (TaskManager.Instance != null)
        {
            assembleDone = TaskManager.Instance.IsTaskComplete(PCDisassemblySetup.AssembleTaskId);
            disassembleDone = TaskManager.Instance.IsTaskComplete(PCDisassemblySetup.TaskId);
            if (assembleDone && disassembleDone)
                quizFired = true; // already both done before this session -- don't replay
        }
    }

    private void OnDisable()
    {
        if (TaskManager.Instance != null)
            TaskManager.Instance.OnTaskCompleted -= HandleTaskCompleted;
    }

    private void HandleTaskCompleted(string completedTaskId)
    {
        if (completedTaskId == PCDisassemblySetup.AssembleTaskId)
            assembleDone = true;
        else if (completedTaskId == PCDisassemblySetup.TaskId)
            disassembleDone = true;
        else
            return; // not one of the two tasks we care about

        TryFireQuiz();
    }

    private void TryFireQuiz()
    {
        if (quizFired || !assembleDone || !disassembleDone) return;

        if (QuizManager.Instance == null)
        {
            Debug.LogWarning("[PCBuildQuizTrigger] Both tasks complete but no QuizManager in scene.");
            return;
        }
        if (combinedQuizSet == null)
        {
            Debug.LogWarning("[PCBuildQuizTrigger] Both tasks complete but no combinedQuizSet assigned.");
            return;
        }

        quizFired = true;
        QuizManager.Instance.StartQuiz(combinedQuizSet);
    }
}