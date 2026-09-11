using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runs one quiz when a GROUP of tasks is finished, rather than when a single task is.
///
/// ATTACH THIS TO: any always-active object in the scene - Managers is the obvious one. One per
/// quiz, so COC2 has three of them side by side.
///
/// WHY THIS EXISTS
/// QuizManager already fires a quiz the moment a task with a matching QuizSetData completes, and
/// for a one-task quiz that is all anyone needs. It cannot express "after the learner has made
/// BOTH cables and tested them", because that is two tasks and neither one alone means the
/// cabling is done. Naming a winner would mean guessing an order the learner does not have to
/// follow - they may crimp both ends and then test both, or crimp and test one cable at a time.
///
/// It is the same shape as COC1's PCBuildQuizTrigger, which does exactly this for assemble +
/// disassemble, generalised so any module can say which tasks make up one stage of the job.
///
/// HOW TO USE IT
///   1. Make a QuizSetData and LEAVE ITS TASK ID BLANK. A blank id never matches a completed
///      task, so QuizManager will not also fire it by itself - this component owns it.
///   2. Put the ids of every task that has to be finished into Required Task Ids.
///   3. Assign the set. Nothing else.
///
/// WHAT ELSE READS THIS
/// StoryCompleteTrigger asks AnyPending() before showing the end-of-module panel, so the panel
/// cannot appear over a quiz the learner is still answering.
/// </summary>
public class QuizTrigger : MonoBehaviour
{
    /// <summary>Every trigger alive in the current scene. Used by StoryCompleteTrigger to hold
    /// the ending back until the quizzes are done.</summary>
    public static readonly List<QuizTrigger> All = new List<QuizTrigger>();

    [Header("Identity")]
    [Tooltip("For the Console only - 'Cabling', 'Patch panel', 'Connectivity'. Makes a log line " +
             "about a quiz that is still outstanding say which one.")]
    [SerializeField] private string label = "";

    [Header("What triggers it")]
    [Tooltip("Every task id that must be complete before this quiz runs. The quiz fires when the " +
             "LAST of them finishes, whichever order the learner does them in.\n\n" +
             "COC2:\n" +
             "  Cabling      - crimp_cable, test_cable\n" +
             "  Patch panel  - punch_patch_panel, patch_to_switch\n" +
             "  Connectivity - verify_connectivity\n\n" +
             "These must match the ids registered with TaskManager exactly. An id that is never " +
             "registered can never complete, and this quiz would then never run.")]
    [SerializeField] private string[] requiredTaskIds = new string[0];

    [Header("The quiz")]
    [Tooltip("The question set to run. Leave its own Task Id BLANK so QuizManager does not fire " +
             "it a second time on its own.")]
    [SerializeField] private QuizSetData quizSet;

    [Header("Safety")]
    [Tooltip("Give up waiting for the quiz to come back after this many seconds and treat it as " +
             "done, so a misconfigured quiz cannot leave the module unable to end. It warns in " +
             "the Console if it ever fires.")]
    [Min(10f)]
    [SerializeField] private float abandonAfterSeconds = 300f;

    [Tooltip("Leave on. Prints which tasks this quiz is still waiting for whenever one of them " +
             "completes, so a quiz that refuses to run says which id it wants instead of staying " +
             "silent.")]
    [SerializeField] private bool logWhatIsMissing = true;

    // ======================================================================

    private readonly HashSet<string> completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private bool hasFired;
    private bool hasFinished;
    private bool subscribedToTasks;
    private bool subscribedToQuiz;

    /// <summary>The name used in Console lines.</summary>
    public string Label => string.IsNullOrWhiteSpace(label) ? name : label;

    /// <summary>True once the quiz has run and the learner has finished answering - or once this
    /// trigger has given up on it.</summary>
    public bool HasFinished => hasFinished;

    /// <summary>
    /// True when this quiz is DUE but not yet done: every task it waits on is complete, and the
    /// learner has not finished answering.
    ///
    /// Due, rather than simply unfinished, on purpose. A quiz whose tasks the learner never got to
    /// is not something to wait for - it is something that is not happening, and treating it as
    /// pending would leave the module unable to end.
    /// </summary>
    public bool IsPending => !hasFinished && quizSet != null && AllRequiredComplete();

    /// <summary>Any quiz in this scene that is due and not yet answered, naming them.</summary>
    public static bool AnyPending(out string names)
    {
        names = null;
        foreach (QuizTrigger t in All)
        {
            if (t == null || !t.IsPending) continue;
            names = names == null ? t.Label : names + ", " + t.Label;
        }
        return names != null;
    }

    /// <summary>
    /// Statics survive a scene load; a List of MonoBehaviours does not survive it INTACT - it
    /// keeps entries pointing at objects Unity has already destroyed. OnDestroy clears them one by
    /// one in the normal case, and this covers a domain reload with Enter Play Mode Options on,
    /// where OnDestroy may never have run at all.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        All.Clear();
    }

    // ======================================================================

    private void Awake()
    {
        if (!All.Contains(this)) All.Add(this);
    }

    private void OnDestroy()
    {
        All.Remove(this);
        UnsubscribeFromQuiz();
    }

    private void Start()
    {
        StartCoroutine(SubscribeWhenReady());
    }

    private void OnDisable()
    {
        if (subscribedToTasks && TaskManager.Instance != null)
        {
            TaskManager.Instance.OnTaskCompleted -= HandleTaskCompleted;
            subscribedToTasks = false;
        }
    }

    /// <summary>
    /// TaskManager.Instance is assigned in its own Awake, and Unity gives no order between Awake
    /// calls on different objects - so subscribing in Start is only safe when both sit on the same
    /// object. Waiting a few frames costs nothing and removes a failure whose only symptom is a
    /// quiz that never appears.
    /// </summary>
    private IEnumerator SubscribeWhenReady()
    {
        for (int i = 0; i < 30 && TaskManager.Instance == null; i++)
            yield return null;

        if (TaskManager.Instance == null)
        {
            Debug.LogWarning($"[QuizTrigger] '{Label}' found no TaskManager in this scene, so its " +
                             "quiz can never run by itself. Wire a button to Fire() instead.", this);
            hasFinished = true;      // not going to happen; do not hold the module's ending back
            yield break;
        }

        TaskManager.Instance.OnTaskCompleted += HandleTaskCompleted;
        subscribedToTasks = true;

        // Catch up on anything already finished - resuming a save part-way through the module, or
        // simply subscribing after another listener reported a task.
        foreach (string id in Ids())
        {
            if (TaskManager.Instance.IsTaskComplete(id)) completed.Add(id);
        }

        Debug.Log($"[QuizTrigger] '{Label}' is watching {Describe()}.", this);

        if (AllRequiredComplete()) Fire();
    }

    // ======================================================================

    private void HandleTaskCompleted(string taskId)
    {
        if (hasFired || string.IsNullOrEmpty(taskId)) return;
        if (!IsRequired(taskId)) return;

        // A HashSet, so a task reported complete twice cannot count twice and push the tally past
        // the finish line early. Stored trimmed, to match how Ids() compares.
        if (!completed.Add(taskId.Trim())) return;

        if (!AllRequiredComplete())
        {
            if (logWhatIsMissing)
                Debug.Log($"[QuizTrigger] '{Label}': '{taskId}' done - still waiting on {Missing()}.", this);
            return;
        }

        Debug.Log($"[QuizTrigger] '{Label}': every task is complete - running the quiz.", this);
        Fire();
    }

    /// <summary>Runs the quiz now. Also on the component's right-click menu, for testing without
    /// replaying the whole stage.</summary>
    [ContextMenu("Fire now (test)")]
    public void Fire()
    {
        if (hasFired) return;
        hasFired = true;

        // An assessment grades the practical task. The quizzes are a teaching aid that belongs to
        // story mode: they stop the clock, hand out explanations after every answer, and mix
        // knowledge evidence into a score that is meant to measure procedure. Marked finished
        // rather than merely skipped, so nothing downstream sits waiting for a quiz that is never
        // going to run.
        if (AssessmentMode.IsActive)
        {
            hasFinished = true;
            Debug.Log($"[QuizTrigger] '{Label}' stood down - this is an assessment run.", this);
            return;
        }

        if (quizSet == null)
        {
            Debug.LogWarning($"[QuizTrigger] '{Label}' has no Quiz Set assigned - nothing to run.", this);
            hasFinished = true;
            return;
        }

        if (QuizManager.Instance == null)
        {
            Debug.LogWarning($"[QuizTrigger] '{Label}' is ready to run but there is no QuizManager " +
                             "in this scene.", this);
            hasFinished = true;
            return;
        }

        // Subscribed BEFORE starting, not after: QuizManager defers the quiz by a coroutine today,
        // but a set with an empty pool comes straight back, and a callback that arrives before the
        // subscription would leave this waiting forever on a quiz that has already been answered.
        SubscribeToQuiz();

        QuizManager.Instance.StartQuiz(quizSet);
        StartCoroutine(AbandonIfItNeverComesBack());
    }

    private void SubscribeToQuiz()
    {
        if (subscribedToQuiz || QuizManager.Instance == null) return;
        QuizManager.Instance.OnQuizCompleted += HandleQuizCompleted;
        subscribedToQuiz = true;
    }

    private void UnsubscribeFromQuiz()
    {
        if (!subscribedToQuiz || QuizManager.Instance == null) return;
        QuizManager.Instance.OnQuizCompleted -= HandleQuizCompleted;
        subscribedToQuiz = false;
    }

    private void HandleQuizCompleted(QuizResult result)
    {
        if (hasFinished) return;

        hasFinished = true;
        UnsubscribeFromQuiz();

        Debug.Log($"[QuizTrigger] '{Label}' quiz answered: " +
                  $"{result.correctAnswers}/{result.totalQuestions} correct.", this);
    }

    /// <summary>
    /// Releases the hold if the quiz never reports back.
    ///
    /// Unscaled, because the quiz itself may have stopped the clock. This should never fire; it
    /// exists so that a set with a broken reference cannot leave a learner stuck at the end of a
    /// module with no panel and nothing in the Console to explain it.
    /// </summary>
    private IEnumerator AbandonIfItNeverComesBack()
    {
        float t = 0f;
        while (t < abandonAfterSeconds && !hasFinished)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        if (hasFinished) yield break;

        hasFinished = true;
        UnsubscribeFromQuiz();
        Debug.LogWarning($"[QuizTrigger] '{Label}' never reported a result after " +
                         $"{abandonAfterSeconds:F0}s. Letting the module carry on without it - " +
                         "check the QuizSetData's question pool and the QuizView reference.", this);
    }

    // ======================================================================

    /// <summary>
    /// The ids to watch, TRIMMED.
    ///
    /// A stray space at the end of an Inspector string is invisible - the field looks exactly
    /// right - and an exact comparison then rejects the very task it was typed for, leaving the
    /// quiz waiting forever on something that has already happened. Absorbed here rather than left
    /// for someone to hunt down.
    /// </summary>
    private IEnumerable<string> Ids()
    {
        if (requiredTaskIds == null) yield break;
        foreach (string id in requiredTaskIds)
        {
            if (!string.IsNullOrWhiteSpace(id)) yield return id.Trim();
        }
    }

    private bool IsRequired(string taskId)
    {
        string wanted = taskId.Trim();
        foreach (string id in Ids())
        {
            if (string.Equals(id, wanted, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private bool AllRequiredComplete()
    {
        bool any = false;
        foreach (string id in Ids())
        {
            any = true;
            if (!completed.Contains(id)) return false;
        }

        // An empty list would otherwise mean "all complete" and fire immediately, which is never
        // what an unfilled Inspector field means.
        return any;
    }

    private string Missing()
    {
        string missing = null;
        foreach (string id in Ids())
        {
            if (completed.Contains(id)) continue;
            missing = missing == null ? id : missing + ", " + id;
        }
        return missing ?? "nothing";
    }

    private string Describe()
    {
        string all = null;
        int n = 0;
        foreach (string id in Ids())
        {
            n++;
            all = all == null ? id : all + ", " + id;
        }
        if (n == 0) return "NOTHING - Required Task Ids is empty, so this quiz will never run";
        return n == 1 ? $"'{all}'" : $"{n} tasks: {all}";
    }
}