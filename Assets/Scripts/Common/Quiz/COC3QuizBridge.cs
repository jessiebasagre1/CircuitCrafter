using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fires your existing quiz system at learning-outcome boundaries in COC 3, and feeds the
/// points it awards back into the COC 3 score.
///
/// ATTACH THIS TO: the same GameObject as your COC3Manager.
///
/// WHY A BRIDGE IS NEEDED AT ALL
/// QuizManager triggers itself from TaskManager.OnTaskCompleted, matching a QuizSetData's
/// taskId. COC 3 does not use TaskManager - it is driven by COC3Manager's step list - so
/// that automatic path never fires here. QuizManager.StartQuiz() is public though, so this
/// watches which step panel is active (the same trick COC3Tutorial uses) and calls it
/// directly when a chosen step is completed. No quiz script is modified.
///
/// THE SCORE PROBLEM IT ALSO SOLVES
/// QuizManager awards points to ScoreManager.Instance - the shared one in Common/Quiz.
/// COC 3 keeps its own COC3ScoreManager, which is what the on-screen score and the final
/// report read from. Without this bridge, quiz points would be invisible in COC 3. It
/// subscribes to OnQuizCompleted and adds the same points to COC3ScoreManager, so they
/// count toward the run.
/// </summary>
public class COC3QuizBridge : MonoBehaviour
{
    [System.Serializable]
    public class QuizTrigger
    {
        [Tooltip("Quiz fires once this step NUMBER has been completed (1-based, as the learner sees it).")]
        public int afterStepNumber = 11;

        [Tooltip("The QuizSetData asset to run. Generate these with Tools > COC3 > Generate Quiz Assets.")]
        public QuizSetData quizSet;

        [HideInInspector] public bool fired;
    }

    [Header("References")]
    [Tooltip("Leave empty to use the COC3Manager on this same GameObject.")]
    public COC3Manager manager;

    [Tooltip("Leave empty to take the one assigned on COC3Manager.")]
    public COC3ScoreManager scoreManager;

    [Header("Quizzes")]
    [Tooltip("The learning-outcome boundaries: 11 ends LO2, 17 ends LO1, 30 ends the five tests, " +
             "35 ends the activity.\n\n" +
             "A quiz every few steps would break the flow of a practical task. At the end of a " +
             "competency is where underpinning knowledge is assessed anyway.")]
    public List<QuizTrigger> triggers = new List<QuizTrigger>
    {
        new QuizTrigger { afterStepNumber = 11 },
        new QuizTrigger { afterStepNumber = 17 },
        new QuizTrigger { afterStepNumber = 30 },
        new QuizTrigger { afterStepNumber = 35 },
    };

    [Header("Behaviour")]
    [Tooltip("Add the quiz's points to the COC 3 score, so they appear on screen and in the final report.")]
    public bool addPointsToCOC3Score = true;

    [Tooltip("Objects hidden while a quiz is on screen - normally StepsRoot and the step HUD, so the learner cannot keep working behind it.")]
    public GameObject[] hideWhileQuizOpen;

    [Tooltip("Allow the same quiz to fire again if the learner restarts the activity.")]
    public bool resetOnRestart = true;

    [Tooltip("Run the quizzes during an assessment too.\n\n" +
             "On: underpinning knowledge is assessed alongside the practical work, and the marks " +
             "count toward the run.\n" +
             "Off: an assessment is the practical task only, and the clock is not stopped by " +
             "multiple-choice questions.")]
    public bool runInAssessment = true;

    // --- runtime state -------------------------------------------------
    private int lastStepNumber = -1;
    private bool wasQuizOpen;

    private void Awake()
    {
        if (manager == null) manager = GetComponent<COC3Manager>();
        if (scoreManager == null && manager != null) scoreManager = manager.scoreManager;
    }

    private void Start()
    {
        if (QuizManager.Instance != null)
            QuizManager.Instance.OnQuizCompleted += HandleQuizCompleted;
    }

    private void OnDisable()
    {
        if (QuizManager.Instance != null)
            QuizManager.Instance.OnQuizCompleted -= HandleQuizCompleted;
    }

    private void Update()
    {
        if (manager == null || manager.steps == null) return;

        bool quizOpen = QuizManager.Instance != null && QuizManager.Instance.IsQuizOpen;

        if (quizOpen != wasQuizOpen)
        {
            SetHidden(quizOpen);
            wasQuizOpen = quizOpen;
        }

        if (quizOpen) return;   // never evaluate step changes mid-quiz

        int current = CurrentStepNumber();
        if (current == lastStepNumber) return;

        // The step we were on has just been completed. Note this also catches the very last
        // step: when step 35 passes, no panel is active and CurrentStepNumber() returns -1,
        // so the final quiz fires as the completion screen comes up.
        if (lastStepNumber > 0) TryFire(lastStepNumber);

        // Restarting drops us back to step 1 from a later step - re-arm so a second attempt
        // gets quizzed too.
        if (resetOnRestart && current == 1 && lastStepNumber > 1)
            foreach (var t in triggers) t.fired = false;

        lastStepNumber = current;
    }

    /// <summary>1-based number of the active step, or -1 when none is showing.</summary>
    private int CurrentStepNumber()
    {
        for (int i = 0; i < manager.steps.Count; i++)
        {
            var s = manager.steps[i];
            if (s != null && s.gameObject.activeInHierarchy) return i + 1;
        }
        return -1;
    }

    private void TryFire(int completedStepNumber)
    {
        foreach (var t in triggers)
        {
            if (t == null || t.fired) continue;
            if (t.afterStepNumber != completedStepNumber) continue;

            if (t.quizSet == null)
            {
                Debug.LogWarning($"[COC3QuizBridge] No QuizSetData assigned for step {completedStepNumber}.", this);
                t.fired = true;
                continue;
            }

            if (QuizManager.Instance == null)
            {
                Debug.LogWarning("[COC3QuizBridge] No QuizManager in this scene - copy the QuizManager " +
                                 "and QuizView setup over from COC1.", this);
                t.fired = true;
                continue;
            }

            if (!runInAssessment && AssessmentMode.IsAssessing("COC3"))
            {
                // Marked fired so it cannot ambush the learner later in the same run.
                t.fired = true;
                Debug.Log("[COC3QuizBridge] Assessment run - quiz after step " +
                          completedStepNumber + " skipped.");
                continue;
            }

            t.fired = true;
            QuizManager.Instance.StartQuiz(t.quizSet);
            return;
        }
    }

    private void HandleQuizCompleted(QuizResult result)
    {
        if (!addPointsToCOC3Score || scoreManager == null || result == null) return;
        if (result.pointsEarned <= 0) return;

        scoreManager.AddScore(result.pointsEarned);
        Debug.Log($"[COC3QuizBridge] Quiz '{result.taskId}': {result.correctAnswers}/{result.totalQuestions} " +
                  $"correct, {result.pointsEarned} points added to the COC 3 score.");
    }

    private void SetHidden(bool hidden)
    {
        if (hideWhileQuizOpen == null) return;

        foreach (var go in hideWhileQuizOpen)
            if (go != null) go.SetActive(!hidden);
    }
}
