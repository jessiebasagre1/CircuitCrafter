using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A pool of questions tied to one TaskManager taskId. When that task
/// completes, QuizManager automatically finds the QuizSetData with a
/// matching TaskId (if one is registered in the scene) and quizzes the
/// player on a random subset of its pool.
///
/// Reusable for COC2/COC3/COC4: just create new QuizSetData assets with
/// those scenes' task IDs and drop them into that scene's QuizManager --
/// no code changes needed anywhere.
/// </summary>
[CreateAssetMenu(menuName = "COC/Quiz/Question Set", fileName = "NewQuizSet")]
public class QuizSetData : ScriptableObject
{
    [Tooltip("Must exactly match a taskId used in TaskManager.RegisterTask()/ReportProgress() for this scene, e.g. \"disassemble_pc\".")]
    public string taskId;

    [Tooltip("The full pool of questions this quiz can draw from.")]
    public List<QuizQuestionData> questionPool = new List<QuizQuestionData>();

    [Range(1, 10)]
    [Tooltip("How many questions to ask each time this quiz triggers. Default range shown is 1-5-ish but you can drag past that if a set ever needs more. Automatically clamped down to the pool size if the pool has fewer questions than this.")]
    public int questionsToAsk = 3;

    [Tooltip("If true, a random subset of the pool is picked (and shuffled) each time. If false, always asks the first N questions in list order.")]
    public bool randomizeSelection = true;
}