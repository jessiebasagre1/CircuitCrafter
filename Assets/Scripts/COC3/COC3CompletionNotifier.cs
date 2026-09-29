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
///
/// IN AN ASSESSMENT IT DOES NEITHER. No Story save, no Story Complete panel - it hides its own
/// visuals and leaves the ending to COC3AssessmentManager's report.
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

    [Header("Assessment mode")]
    [Tooltip("During a COC3 assessment, hide this screen's own visuals so the ASSESSMENT REPORT " +
             "(COC3AssessmentManager > Report Panel) is the only ending on screen.\n\n" +
             "In an assessment this screen never hands over to the Story Complete panel and never " +
             "marks COC3 complete in the Story save, whatever this box says.")]
    public bool hideOwnVisualsInAssessment = true;

    [Tooltip("OPTIONAL. The assessment report panel, so it is never hidden with this screen's visuals. " +
             "Leave empty - it is read from COC3AssessmentManager > Report Panel automatically.")]
    public GameObject assessmentReportPanel;

    private void OnEnable()
    {
        // AN ASSESSMENT ENDS WITH THE ASSESSMENT REPORT, NOT THE STORY ENDING.
        //
        // COC3Manager raises this completion screen in both modes - it does not know which one it
        // is in - and this used to treat every finish as a Story finish: mark COC3 complete, then
        // Fire() the Story Complete panel. StoryCompleteTrigger switches itself off in an
        // assessment, but Fire() still ran on the switched-off component (a coroutine starts on
        // any component whose GameObject is active), so "Module complete!" landed on top of the
        // graded report.
        //
        // COC3AssessmentManager still sees this object switch on - that is its own "the module is
        // over" signal - so only the visuals go, never this object.
        if (AssessmentMode.IsActive)
        {
            if (hideOwnVisualsInAssessment) HideOwnVisuals(keepAssessmentReport: true);

            Debug.Log("[COC3CompletionNotifier] Assessment run - Story ending skipped: COC3 is not " +
                      "marked complete in the Story save, and COC3AssessmentManager shows the report.", this);
            return;
        }

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

        if (hideOwnVisualsWhenHandingOver) HideOwnVisuals(keepAssessmentReport: false);

        storyCompleteTrigger.Fire();
    }

    /// <summary>
    /// Switches off this screen's CHILDREN, not this object. This one has to stay active - its
    /// OnEnable is the signal, and COC3WaypointBridge's step and COC3AssessmentManager both watch it
    /// to know the module is over. Switching it off here would undo the very thing that just ran.
    ///
    /// keepAssessmentReport leaves alone any child that holds COC3AssessmentManager's Report Panel,
    /// in case the report was built inside this screen.
    /// </summary>
    private void HideOwnVisuals(bool keepAssessmentReport)
    {
        Transform report = null;
        if (keepAssessmentReport)
        {
            GameObject panel = assessmentReportPanel != null ? assessmentReportPanel : FindAssessmentReport();
            if (panel != null) report = panel.transform;
        }

        // This screen IS the report, or sits inside it - hiding its children would blank the report.
        if (report != null && transform.IsChildOf(report)) return;

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (report != null && report.IsChildOf(child)) continue;
            child.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// COC3AssessmentManager's Report Panel, looked up BY NAME rather than by type.
    ///
    /// Naming the type in code does not compile in every project: where this script is built into
    /// a different assembly from Assets/Scripts/Assessment, COC3AssessmentManager "does not exist in
    /// the current context". Reading the field by name works either way, and runs once, at the end
    /// of the module.
    /// </summary>
    private static GameObject FindAssessmentReport()
    {
        var all = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (all == null) return null;

        foreach (MonoBehaviour mb in all)
        {
            if (mb == null || mb.GetType().Name != "COC3AssessmentManager") continue;

            System.Reflection.FieldInfo field = mb.GetType().GetField("reportPanel");
            return field != null ? field.GetValue(mb) as GameObject : null;
        }

        return null;
    }
}