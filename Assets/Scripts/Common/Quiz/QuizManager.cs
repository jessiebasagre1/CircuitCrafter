using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene-local orchestrator that listens for ANY TaskManager task completion
/// and, if a QuizSetData is registered for that taskId, runs a quiz.
///
/// Drop one QuizManager per gameplay scene (COC1, COC2, COC3, COC4...),
/// same idea as TaskCheckpointAutosaver. Assign whichever QuizSetData assets
/// apply to THAT scene's tasks in the Quiz Sets list -- tasks with no
/// matching QuizSetData are simply skipped, so quizzes are entirely
/// opt-in per task, per scene.
/// </summary>
public class QuizManager : MonoBehaviour
{
    public static QuizManager Instance { get; private set; }

    [Tooltip("Every quiz available in this scene. QuizManager matches these to completed tasks automatically by TaskId -- add more here as you add more tasks, no code changes needed.")]
    [SerializeField] private List<QuizSetData> quizSets = new List<QuizSetData>();

    [SerializeField] private QuizView quizView;

    /// <summary>Fires once a quiz finishes, with the full result. Future
    /// achievement/badge systems can subscribe to this to react to quiz
    /// performance without any changes here.</summary>
    public event Action<QuizResult> OnQuizCompleted;

    /// <summary>True while a quiz is on screen. Other scripts (movement,
    /// interaction, PowerButtonInput, etc.) should check this and ignore
    /// input while true -- same idea as DialogManager.IsDialogOpen.</summary>
    public bool IsQuizOpen { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

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
        // TaskManager.RestoreState() re-fires OnTaskCompleted for every
        // already-completed task on load (so late subscribers like this one
        // can catch up on state) -- it's not a fresh, live completion, so a
        // quiz gated on this taskId shouldn't replay just because the player
        // hit Continue on a save where it was already done.
        if (SaveManager.Instance != null && SaveManager.Instance.IsRestoringSave)
            return;

        QuizSetData set = quizSets.Find(q => q != null && q.taskId == completedTaskId);
        if (set == null) return; // this task has no quiz configured - fine, entirely optional

        StartCoroutine(WaitForDialogThenStartQuiz(set));
    }

    /// <summary>
    /// Waits a frame so any other OnTaskCompleted listener (e.g. TaskDialogTrigger)
    /// has a chance to open its completion dialog first, regardless of which
    /// listener happened to subscribe first � then waits for that dialog to
    /// close (if one opened) before showing the quiz, so they never overlap.
    /// </summary>
    private IEnumerator WaitForDialogThenStartQuiz(QuizSetData set)
    {
        yield return null;

        while (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen)
            yield return null;

        StartQuiz(set);
    }

    /// <summary>Starts a quiz for a specific set directly (useful for testing,
    /// or triggering a quiz from somewhere other than task completion).</summary>
    public void StartQuiz(QuizSetData set)
    {
        if (set == null || quizView == null)
        {
            Debug.LogWarning("[QuizManager] StartQuiz called with a null set or no QuizView assigned.");
            return;
        }

        if (set.questionPool == null || set.questionPool.Count == 0)
        {
            Debug.LogWarning($"[QuizManager] QuizSetData '{set.name}' has an empty question pool - skipping.");
            return;
        }

        List<QuizQuestionData> picked = PickQuestions(set);

        IsQuizOpen = true;
        quizView.BeginQuiz(set.taskId, picked, HandleQuizFinished);
    }

    private List<QuizQuestionData> PickQuestions(QuizSetData set)
    {
        int count = Mathf.Clamp(set.questionsToAsk, 1, set.questionPool.Count);
        var pool = new List<QuizQuestionData>(set.questionPool);

        if (set.randomizeSelection)
        {
            // Fisher-Yates shuffle, then take the first `count`.
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
        }

        return pool.GetRange(0, count);
    }

    private void HandleQuizFinished(QuizResult result)
    {
        IsQuizOpen = false;

        if (ScoreManager.Instance != null)
            ScoreManager.Instance.AddScore(result.pointsEarned, $"quiz:{result.taskId}");

        Debug.Log($"[QuizManager] Quiz for '{result.taskId}' finished: {result.correctAnswers}/{result.totalQuestions} correct, +{result.pointsEarned} pts.");

        OnQuizCompleted?.Invoke(result);
    }
}