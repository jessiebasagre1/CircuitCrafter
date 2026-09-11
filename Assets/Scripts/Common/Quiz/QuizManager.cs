using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

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

    [Header("Leaving a station first")]
    [Tooltip("Called just before the quiz appears, to step the player out of whatever close-up " +
             "view they finished the task in.\n\n" +
             "COC1 needs nothing here - its stations all run through StationCameraSwitch, which " +
             "this handles by itself. EVERY OTHER MODULE DOES need it: COC2's crimping bench, " +
             "cable tester, punch-down stage and PC desktops each own their own camera, and none " +
             "of them is a StationCameraSwitch.\n\n" +
             "Wire one entry per station in the scene, each calling that station's own exit " +
             "method - NetworkTestStation.RequestExit, PunchDownStation.RequestExit, and so on. " +
             "They are safe to call blind: a station that is not open ignores it, so all of them " +
             "can be listed and only the one the player is actually standing in does anything.\n\n" +
             "Without this the station camera is still rendering when the quiz camera switches " +
             "on, and the quiz ends up drawn over a close-up of a patch panel - or behind it.")]
    [SerializeField] private UnityEvent onBeforeQuiz;

    [Tooltip("Beat after stepping out of the station, before the quiz appears. Long enough for a " +
             "station camera to finish moving back.")]
    [Min(0f)]
    [SerializeField] private float delayAfterLeavingStation = 0.6f;

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
        // The backstop for assessment runs. QuizTrigger already stands itself down, but a quiz can
        // also arrive from a QuizSetData whose Task Id matches a completed task, or from a button
        // wired straight to this method - and either would put a teaching aid in the middle of a
        // graded run and stop the assessment clock while the learner reads explanations.
        if (AssessmentMode.IsActive)
        {
            Debug.Log("[QuizManager] Quiz suppressed - this is an assessment run.", this);
            return;
        }

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

        // Marked open before anything is shown, not after. Leaving a station takes a moment, and
        // in that gap the player is back in the walking view with the quiz not yet up - HotbarVisibilityController
        // and anything else reading IsQuizOpen should already be treating the quiz as running.
        IsQuizOpen = true;

        StartCoroutine(OpenQuiz(set));
    }

    /// <summary>
    /// Gets the player out of whatever close-up view they finished in, then puts the quiz on
    /// screen.
    ///
    /// A coroutine because leaving a station is not instant: the camera may be moving back, and a
    /// station often raises a line as it closes. Showing the quiz in the middle of either looks
    /// like a bug.
    /// </summary>
    private IEnumerator OpenQuiz(QuizSetData set)
    {
        LeaveAnyOpenStation();

        if (delayAfterLeavingStation > 0f)
            yield return new WaitForSecondsRealtime(delayAfterLeavingStation);

        // Checked again: stepping out of a station can itself raise a dialog, and that one would
        // otherwise be buried under the quiz.
        while (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen)
            yield return null;

        List<QuizQuestionData> picked = PickQuestions(set);

        // If the player is currently standing inside a station's own camera
        // view (e.g. a checklist/diagnostics station), exit that FIRST so the
        // player camera is what's active right before ActivateQuizCamera()
        // snapshots things below. Otherwise QuizCameraController has no idea
        // the station camera even exists: it'd never switch it off, leaving
        // it active underneath the quiz camera the whole time, and
        // NotifyQuizFinished() would have nothing to restore to afterward --
        // the station camera would just still be sitting there once the quiz
        // camera turns off. Same pattern PCDisassemblySetup already uses for
        // its own quiz, just generalized here so every station gets it for
        // free instead of needing a manual ForceReturnToPlayerCamera() call
        // wired up per station.
        //
        // MOVED OUT of here into LeaveAnyOpenStation() below, because StationCameraSwitch is a
        // COC1 component and the other modules' stations are nothing to do with it.

        // Switch to the quiz's own dedicated camera/background instead of just
        // overlaying the quiz panel on whatever the player camera happens to be
        // looking at -- fixes text contrast issues against unpredictable gameplay
        // backgrounds/lighting.
        if (QuizCameraController.Instance != null)
            QuizCameraController.Instance.ActivateQuizCamera();

        quizView.BeginQuiz(set.taskId, picked, HandleQuizFinished);
    }

    /// <summary>
    /// Steps the player out of any close-up view before the quiz appears.
    ///
    /// Two paths, because the modules do not share one station system:
    ///   COC1 - every station is a StationCameraSwitch, so it is handled here with no wiring.
    ///   Everything else - each station owns its own camera and its own way out, so the scene
    ///                     says what to call through On Before Quiz.
    ///
    /// Calling both is deliberate. A scene with no StationCameraSwitch in it leaves the static
    /// null and the first half does nothing; a scene with an empty On Before Quiz skips the
    /// second. Neither costs anything when it does not apply.
    /// </summary>
    private void LeaveAnyOpenStation()
    {
        if (StationCameraSwitch.NearbyOrActive != null &&
            StationCameraSwitch.NearbyOrActive.IsUsingStationCamera)
        {
            StationCameraSwitch.NearbyOrActive.ForceReturnToPlayerCamera();
        }

        onBeforeQuiz?.Invoke();
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

        // Switch back to the player's normal camera/cursor state now that the
        // quiz is done, mirroring ActivateQuizCamera() above.
        if (QuizCameraController.Instance != null)
            QuizCameraController.Instance.NotifyQuizFinished();

        if (ScoreManager.Instance != null)
            ScoreManager.Instance.AddScore(result.pointsEarned, $"quiz:{result.taskId}");

        Debug.Log($"[QuizManager] Quiz for '{result.taskId}' finished: {result.correctAnswers}/{result.totalQuestions} correct, +{result.pointsEarned} pts.");

        OnQuizCompleted?.Invoke(result);
    }
}