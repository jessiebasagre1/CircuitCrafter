using UnityEngine;

/// <summary>
/// Put this on the COMPLETION SCREEN GameObject (the one assigned to
/// COC3Manager.completionScreen).
///
/// COC3Manager has no "activity finished" event, and the goal was to build COC3
/// without editing the scripts you already have. So instead of an event, this
/// component uses the one signal that already exists: COC3Manager calls
/// completionScreen.SetActive(true) when the last step is done, which fires this
/// component's OnEnable().
///
/// That single line is the whole trick - it turns "the report screen appeared"
/// into "record COC3 as finished".
///
/// IT CAN ALSO HAND THE ENDING TO THE STORY COMPLETE PANEL, so COC3 finishes the way COC1 and COC2
/// do instead of on its own screen. See Story Complete Trigger below.
/// </summary>
[DisallowMultipleComponent]
public class COC3CompletionNotifier : MonoBehaviour
{
    [Tooltip("The COC3CompletionManager, normally on the same GameObject as COC3Manager. Found automatically if left empty.")]
    public COC3CompletionManager completionManager;

    [Header("Story mode ending")]
    [Tooltip("OPTIONAL. Assign the scene's StoryCompleteTrigger and COC3 ends with the same panel " +
             "COC1 and COC2 use, instead of its own completion screen.\n\n" +
             "StoryCompleteTrigger normally watches TaskManager task ids, which COC3 does not use - " +
             "it has its own step list. So it is fired by hand from here, which is exactly what its " +
             "public Fire() is for. Leave its Required Task Ids EMPTY.\n\n" +
             "Leave this empty to keep COC3\'s own completion screen.")]
    public StoryCompleteTrigger storyCompleteTrigger;

    [Tooltip("Hides this object\'s own visuals when the Story Complete panel takes over, so the two " +
             "endings do not stack. Leave ON when you are using the trigger above.")]
    public bool hideOwnVisualsWhenHandingOver = true;

    private void OnEnable()
    {
        if (completionManager == null)
        {
#if UNITY_2023_1_OR_NEWER
            // FindAnyObjectByType, not FindFirstObjectByType: the "First" variant was
            // deprecated because it depends on instance-ID ordering, and there is only
            // ever one COC3CompletionManager in the scene, so "any" is what we mean.
            completionManager = Object.FindAnyObjectByType<COC3CompletionManager>();
#else
            completionManager = Object.FindObjectOfType<COC3CompletionManager>();
#endif
        }

        if (completionManager == null)
        {
            Debug.LogWarning("[COC3CompletionNotifier] No COC3CompletionManager found - COC3 completion will not be saved.", this);
            return;
        }

        completionManager.MarkCompleted();

        HandOverToStoryPanel();
    }

    /// <summary>
    /// Passes the ending to the shared Story Complete panel.
    ///
    /// AFTER MarkCompleted, never before. The panel offers "Take the assessment" and a return to the
    /// menu, and both of those read progress that MarkCompleted is what writes - hand over first and
    /// the learner is offered an assessment for a module the save does not yet believe they
    /// finished.
    /// </summary>
    private void HandOverToStoryPanel()
    {
        if (storyCompleteTrigger == null) return;

        if (hideOwnVisualsWhenHandingOver)
        {
            // The CHILDREN, not this object. This one has to stay active - its OnEnable is the
            // signal, and COC3WaypointBridge's step may be watching it to know the module is over.
            // Switching it off here would undo the very thing that just ran.
            for (int i = 0; i < transform.childCount; i++)
                transform.GetChild(i).gameObject.SetActive(false);
        }

        storyCompleteTrigger.Fire();
    }
}