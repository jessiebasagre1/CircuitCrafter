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

    [Header("The panel")]
    [Tooltip("Found automatically - including while it is switched off - if left empty.")]
    [SerializeField] private StoryCompletePanel panel;

    // ======================================================================

    private readonly HashSet<string> completed = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
    private bool hasFired;
    private bool subscribed;

    private void Start()
    {
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
    }

    // ======================================================================

    private void HandleTaskCompleted(string taskId)
    {
        if (hasFired || string.IsNullOrEmpty(taskId)) return;
        if (!IsRequired(taskId)) return;

        // A HashSet, so a task reported complete twice does not count twice and
        // cannot push the tally past the finish line early.
        if (!completed.Add(taskId)) return;

        Debug.Log($"[StoryCompleteTrigger] '{taskId}' done - {completed.Count} of {RequiredCount} for this module.", this);

        if (completed.Count < RequiredCount) return;

        Debug.Log("[StoryCompleteTrigger] Every required task is complete - ending the module.", this);
        Fire();
    }

    private bool IsRequired(string taskId)
    {
        if (requiredTaskIds == null) return false;

        foreach (string id in requiredTaskIds)
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (string.Equals(id, taskId, System.StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
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

        StoryCompletePanel target = Panel();
        if (target == null)
        {
            Debug.LogWarning("[StoryCompleteTrigger] No StoryCompletePanel in this scene. Run CircuitCrafter → Build Story Complete Panel.", this);
            yield break;
        }

        target.Show();
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