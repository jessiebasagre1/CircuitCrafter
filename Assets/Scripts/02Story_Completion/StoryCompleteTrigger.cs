using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

/// <summary>
/// Ends a module by itself: the tasks that matter are done → step back out of the
/// station → the completion panel appears.
///
/// ATTACH THIS TO: the Managers object in each module scene. One per scene.
/// List the task ids that mean "this module is finished" and nothing else needs
/// wiring.
///
/// HOW IT KNOWS
/// It subscribes to TaskManager.OnTaskCompleted, exactly as TaskCheckpointAutosaver
/// does. That event already carries every task completion in the game, so no other
/// script has to be edited and no button has to be wired - and a module whose
/// ending later moves to a different task is one Inspector field to change.
///
/// WHY A LIST AND NOT A SINGLE "FINAL" TASK
/// COC1 ends on one task (document_work, the service report). COC2 ends when three
/// separate things are all done - crimp, test, punch down - and which of them the
/// learner finishes last depends on how they work. Naming the set is the honest
/// description; naming a winner means guessing an order the learner does not have
/// to follow.
///
/// WHY IT WAITS, AND WHY IT LEAVES THE STATION FIRST
/// The last thing the learner does is usually at a station, so at the moment the
/// module ends they are staring at a close-up of a monitor or a patch panel. A
/// panel appearing over that reads as an interruption. Stepping back to the
/// walking view first, with a beat either side, makes it feel like the job
/// finishing rather than the game stopping.
/// </summary>
public class StoryCompleteTrigger : MonoBehaviour
{
    [Header("What ends this module")]
    [Tooltip("Every task id that must be complete. The panel appears when the LAST of them finishes.\n\nCOC1: document_work\nCOC2: crimp_cable, test_cable, punch_patch_panel\n\nThey must match the ids registered with TaskManager exactly - COC2's live on COC2TaskSetup.")]
    [SerializeField] private string[] requiredTaskIds = new string[] { "document_work" };

    [Header("Stepping back out")]
    [Tooltip("Your station's exit / back button. This presses it for you, so it works with whatever station system the scene uses - no reference to a camera script needed.\n\nLeave empty if the module does not end inside a station.")]
    [SerializeField] private Button exitStationButton;

    [Tooltip("Anything else that should happen before the panel - hide the station UI, stop a sound, re-enable the walk controls.")]
    public UnityEvent onBeforeShow;

    [Header("Timing")]
    [Tooltip("Beat after the last task completes, before stepping out. Long enough for the learner to see their work was accepted.")]
    [Min(0f)]
    [SerializeField] private float delayBeforeExit = 1.5f;

    [Tooltip("Beat after stepping out, before the panel appears. Long enough for the camera to get back to the walking view.")]
    [Min(0f)]
    [SerializeField] private float delayAfterExit = 1.2f;

    [Header("Waiting for other things")]
    [Tooltip("Leave on. Holds the ending back while a dialog is on screen.\n\n" +
             "COC2 needs this: the last successful ping fires the machine's own success dialog AND " +
             "the round-closing one, chained. A fixed delay would drop the panel on top of them, " +
             "and no delay you can type is right for two dialogs whose length depends on how fast " +
             "the learner reads.")]
    [SerializeField] private bool waitForDialogs = true;

    [Tooltip("Leave on. Holds the ending back until every QuizTrigger in the scene whose tasks are " +
             "finished has had its quiz answered.\n\n" +
             "The quiz for the last stage of the module is triggered by the same task completion " +
             "that ends the module, so without this the two race each other and the completion " +
             "panel lands on top of the first question.\n\n" +
             "A scene with no QuizTrigger in it waits for nothing, so this is safe to leave on " +
             "everywhere.")]
    [SerializeField] private bool waitForQuizzes = true;

    [Tooltip("Extra beat after the last dialog or quiz closes, so the panel does not appear on the " +
             "same frame the thing before it vanished.")]
    [Min(0f)]
    [SerializeField] private float pauseAfterWaiting = 0.5f;

    [Header("The panel")]
    [Tooltip("Found automatically - including while it is switched off - if left empty.")]
    [SerializeField] private StoryCompletePanel panel;

    // ======================================================================

    private readonly HashSet<string> completed = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
    private bool hasFired;
    private bool subscribed;

    private void Start()
    {
        // An assessment ends with the assessment report, not the story's congratulations panel.
        // Both are triggered by the same six tasks completing, so without this the learner gets
        // "Module complete!" over the top of their graded result - and the panel's Continue button
        // would carry them on as if they had played the story.
        if (AssessmentMode.IsActive)
        {
            Debug.Log("[StoryCompleteTrigger] Assessment run - the completion panel stays down; " +
                      "COC2AssessmentManager shows the report instead.", this);
            enabled = false;
            return;
        }

        StartCoroutine(SubscribeWhenReady());
    }

    private void OnDisable()
    {
        if (subscribed && TaskManager.Instance != null)
        {
            TaskManager.Instance.OnTaskCompleted -= HandleTaskCompleted;
            subscribed = false;
        }
    }

    /// <summary>
    /// TaskCheckpointAutosaver subscribes in Start and assumes TaskManager.Instance
    /// already exists. That holds when both sit on the same Managers object, and
    /// quietly does not when they do not - Unity gives no order between Start calls
    /// on different objects. Waiting a few frames costs nothing and removes a
    /// failure whose only symptom is a module that never ends.
    /// </summary>
    private IEnumerator SubscribeWhenReady()
    {
        for (int i = 0; i < 30 && TaskManager.Instance == null; i++)
            yield return null;

        if (TaskManager.Instance == null)
        {
            Debug.LogWarning("[StoryCompleteTrigger] No TaskManager in this scene, so the module can never end by itself. Wire a button to Fire() instead.", this);
            yield break;
        }

        TaskManager.Instance.OnTaskCompleted += HandleTaskCompleted;
        subscribed = true;

        Debug.Log($"[StoryCompleteTrigger] Watching for {Describe()}.", this);
        ValidateIds();

        // Catch up on anything already finished before this subscribed - resuming a save, or a
        // task another component reported during its own Start.
        foreach (string id in requiredTaskIds)
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (TaskManager.Instance.IsTaskComplete(id.Trim())) completed.Add(id.Trim());
        }
    }

    // ======================================================================

    private void HandleTaskCompleted(string taskId)
    {
        if (hasFired || string.IsNullOrEmpty(taskId)) return;
        if (!IsRequired(taskId)) return;

        // A HashSet, so a task reported complete twice does not count twice and
        // cannot push the tally past the finish line early. Stored trimmed, to match how
        // IsRequired compares.
        if (!completed.Add(taskId.Trim())) return;

        Debug.Log($"[StoryCompleteTrigger] '{taskId}' done - {completed.Count} of {RequiredCount} for this module.", this);

        if (completed.Count < RequiredCount) return;

        Debug.Log("[StoryCompleteTrigger] Every required task is complete - ending the module.", this);
        Fire();
    }

    private bool IsRequired(string taskId)
    {
        if (requiredTaskIds == null) return false;

        // TRIMMED on both sides.
        //
        // A stray space at the end of an Inspector string is invisible - the field looks exactly
        // right - and an exact comparison then rejects the very task it was typed for. The task
        // completes, this trigger ignores it, the tally stops one short, and the completion panel
        // never appears with nothing in the Console that reads as an error.
        //
        // That is not a fault worth making anyone hunt for, so it is absorbed here. ValidateIds()
        // still names the offending entry so it can be tidied up.
        string wanted = taskId.Trim();

        foreach (string id in requiredTaskIds)
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (string.Equals(id.Trim(), wanted, System.StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>Warns about ids carrying leading or trailing whitespace. They now work anyway, but
    /// they are worth cleaning up - anything else in the project comparing them exactly will still
    /// be caught out.</summary>
    private void ValidateIds()
    {
        if (requiredTaskIds == null) return;

        foreach (string id in requiredTaskIds)
        {
            if (string.IsNullOrEmpty(id)) continue;
            if (id == id.Trim()) continue;

            Debug.LogWarning($"[StoryCompleteTrigger] Required Task Id '{id}' has a space around it. " +
                             "It is being matched trimmed, so the module still ends - but fix the field: " +
                             "another component comparing it exactly would silently never match.", this);
        }
    }

    private int RequiredCount
    {
        get
        {
            if (requiredTaskIds == null) return 0;

            int n = 0;
            foreach (string id in requiredTaskIds)
                if (!string.IsNullOrWhiteSpace(id)) n++;
            return n;
        }
    }

    private string Describe()
    {
        int n = RequiredCount;
        if (n == 0) return "NOTHING - Required Task Ids is empty, so this module will never end";
        return n == 1 ? $"'{requiredTaskIds[0]}'" : $"{n} tasks: {string.Join(", ", requiredTaskIds)}";
    }

    // ======================================================================

    /// <summary>Wire this to a button's OnClick if you would rather trigger the
    /// ending by hand. Also on the component's right-click menu, for testing
    /// without replaying the module.</summary>
    [ContextMenu("Fire now (test)")]
    public void Fire()
    {
        // Once per visit. Whatever calls this may call it more than once - a
        // button pressed twice, a task reported again - and without this the
        // learner watches the ending restart.
        if (hasFired) return;
        hasFired = true;

        StartCoroutine(Sequence());
    }

    [ContextMenu("Reset test")]
    public void ResetForTesting()
    {
        hasFired = false;
        completed.Clear();
        Debug.Log("[StoryCompleteTrigger] Reset - it will watch for the tasks again.", this);
    }

    // ======================================================================

    private IEnumerator Sequence()
    {
        // Realtime, not scaled. DialogManager sets Time.timeScale to 0 while a
        // dialog is up, and a scaled wait would simply never end if the last task
        // completed with one open.
        if (delayBeforeExit > 0f) yield return new WaitForSecondsRealtime(delayBeforeExit);

        // Before stepping out, not after. The quiz steps the learner out of the station itself and
        // runs on its own camera, so pressing the station's exit button underneath it would move
        // the world about behind a quiz the learner is in the middle of answering.
        yield return WaitForEverythingElse();

        if (exitStationButton != null)
        {
            // Invoking the button's own click is deliberately blunt: it runs
            // exactly what pressing it runs, so it stays correct however the
            // station is wired, and it keeps working the day that wiring changes.
            Debug.Log("[StoryCompleteTrigger] Stepping back out of the station.", this);
            exitStationButton.onClick.Invoke();
        }

        onBeforeShow?.Invoke();

        if (delayAfterExit > 0f) yield return new WaitForSecondsRealtime(delayAfterExit);

        // Checked again. Leaving the station can itself raise a line, and a dialog that opened
        // during the walk back would otherwise be buried by the panel.
        yield return WaitForEverythingElse();

        StoryCompletePanel target = Panel();
        if (target == null)
        {
            Debug.LogWarning("[StoryCompleteTrigger] No StoryCompletePanel in this scene. Run CircuitCrafter → Build Story Complete Panel.", this);
            yield break;
        }

        target.Show();
    }

    /// <summary>
    /// Holds until nothing else is on screen: no dialog, and no quiz still to be answered.
    ///
    /// Written as a loop rather than a longer fixed delay because both are open-ended. Two chained
    /// dialogs and a five-question quiz take exactly as long as the learner takes to read and
    /// answer them, and any number typed into the Inspector is wrong for somebody.
    ///
    /// The re-check after the pause is not paranoia: DialogManager chains a second sequence from
    /// the first one's completion callback, and QuizManager defers the quiz by a coroutine, so
    /// there is a frame where nothing is open and something is about to open. Waiting once would
    /// slip the panel into that gap.
    /// </summary>
    private IEnumerator WaitForEverythingElse()
    {
        yield return WaitOnce();

        if (pauseAfterWaiting > 0f) yield return new WaitForSecondsRealtime(pauseAfterWaiting);

        yield return WaitOnce();
    }

    private IEnumerator WaitOnce()
    {
        bool announced = false;

        while (true)
        {
            bool dialogUp = waitForDialogs &&
                            DialogManager.Instance != null &&
                            DialogManager.Instance.IsDialogOpen;

            string quizzes = null;
            bool quizUp = waitForQuizzes && QuizTrigger.AnyPending(out quizzes);

            // The quiz that has been started but is still on its way in is covered by AnyPending
            // above; this catches a quiz started by something other than a QuizTrigger.
            if (waitForQuizzes && !quizUp &&
                QuizManager.Instance != null && QuizManager.Instance.IsQuizOpen)
            {
                quizUp = true;
                quizzes = "a quiz";
            }

            if (!dialogUp && !quizUp) yield break;

            if (!announced)
            {
                announced = true;
                Debug.Log("[StoryCompleteTrigger] Holding the completion panel back - waiting for " +
                          (dialogUp ? "a dialog" : "") +
                          (dialogUp && quizUp ? " and " : "") +
                          (quizUp ? "quiz: " + quizzes : "") + ".", this);
            }

            yield return null;
        }
    }

    private StoryCompletePanel Panel()
    {
        // Include inactive: the panel hides itself in Start, so by the time the
        // module ends it is switched off and a plain search finds nothing.
        if (panel == null)
            panel = FindAnyObjectByType<StoryCompletePanel>(FindObjectsInactive.Include);

        return panel;
    }
}