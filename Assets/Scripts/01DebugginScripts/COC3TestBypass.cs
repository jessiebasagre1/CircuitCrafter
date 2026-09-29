// COC3TestBypass.cs
using UnityEngine;

/// <summary>
/// TESTING ONLY. Skips COC 3 steps so the end of the module can be reached in seconds - to check
/// that the ASSESSMENT REPORT appears, without doing all 35 steps first.
///
/// ATTACH THIS TO: any object in the COC 3 scene - the Managers object is a good place.
///
/// PUT THE FILE IN: Assets/Scripts/Assessment/  (the same folder as COC3AssessmentManager.cs).
/// It needs to see COC3AssessmentManager, and in this project not every folder can.
///
/// HOW TO USE IT
///   1. Start the COC 3 assessment from the Assessment menu and press Start on the briefing.
///   2. Walk to the server and press E, so you are sitting at a PC.
///   3. Press one of:
///        F8  Jump to step 35 (Jump To Step Number). Finish that step yourself - this is the
///            most faithful test, because the module then ends exactly the way a learner's does.
///        F9  Finish COC 3 now. Skips every remaining step; the report should appear at once.
///   Both are also on the component's right-click menu.
///
/// WHAT THE REPORT WILL SAY
/// Skipped steps earn no points and were never "seen" by the assessment, so expect a low score and
/// "Steps reached: 2 of 35" or similar. That is correct - the test is whether the report SHOWS.
///
/// ---------------------------------------------------------------------------------------------
/// IT IS OFF IN A REAL BUILD, AND THAT IS THE POINT
/// A skip button in an assessment is the opposite of what this project is for. It does nothing
/// outside the Editor and Development Builds unless Allow In Builds is ticked, and a run it has
/// touched is kept OUT of the learner's saved results (see Keep Test Run Out Of Results), so a
/// test in the Editor never shows up as evidence on the Trainee profile.
/// ---------------------------------------------------------------------------------------------
/// </summary>
[DisallowMultipleComponent]
public class COC3TestBypass : MonoBehaviour
{
    private const string ModeId = "COC3";

    [Header("References")]
    [Tooltip("Left empty, the scene's COC3Manager is found automatically.")]
    [SerializeField] private COC3Manager manager;

    [Header("Keys")]
    [Tooltip("Jumps to the step below. None disables the shortcut.")]
    [SerializeField] private KeyCode jumpKey = KeyCode.F8;

    [Tooltip("The step number F8 jumps to, 1-based as the HUD shows it. 35 is the service report - " +
             "the last step. Use 11 to test walking from the server to the client.")]
    [Min(1)]
    [SerializeField] private int jumpToStepNumber = 35;

    [Tooltip("Skips every remaining step, so the module ends straight away. None disables it.")]
    [SerializeField] private KeyCode finishKey = KeyCode.F9;

    [Header("Safety")]
    [Tooltip("ON (recommended): only works during a COC 3 assessment.\n\n" +
             "OFF: also works in Story Mode - where finishing marks COC 3 complete in the learner's " +
             "save and unlocks COC 4, exactly as finishing normally would.")]
    [SerializeField] private bool assessmentOnly = true;

    [Tooltip("ON (recommended): you must be sitting at a PC. The module always ends while the learner " +
             "is seated, so that is the situation worth testing - and skipping while walking leaves " +
             "the waypoint pointing at the wrong machine until you sit down.")]
    [SerializeField] private bool requireSeated = true;

    [Tooltip("ON (recommended): the first skip switches off COC3AssessmentBridge, so this run is never " +
             "written to the learner's results or competency record. The on-screen report still " +
             "appears - that comes from COC3AssessmentManager, which saves nothing.")]
    [SerializeField] private bool keepTestRunOutOfResults = true;

    [Tooltip("OFF (correct): does nothing outside the Editor and Development Builds.\n\n" +
             "ON: also works in a normal build. Only for testing on a device - never for a build " +
             "anyone is assessed on.")]
    [SerializeField] private bool allowInBuilds;

    [Tooltip("Shows the two keys in the bottom-left corner while the bypass is available, so it " +
             "cannot be forgotten in the scene.")]
    [SerializeField] private bool showOnScreenHint = true;

    private bool usedThisRun;
    private GUIStyle hintStyle;

    // ==================================================================

    private void Awake()
    {
        if (manager == null) manager = FindAnyObjectByType<COC3Manager>();
    }

    private void Update()
    {
        if (jumpKey != KeyCode.None && Input.GetKeyDown(jumpKey)) JumpToStep();
        if (finishKey != KeyCode.None && Input.GetKeyDown(finishKey)) FinishNow();
    }

    // ==================================================================

    /// <summary>Skips forward to Jump To Step Number. Finish that step yourself to end the module.</summary>
    [ContextMenu("Jump to step (Jump To Step Number)")]
    public void JumpToStep()
    {
        if (!Ready()) return;

        int total = manager.steps.Count;
        int target = Mathf.Clamp(jumpToStepNumber, 1, total);
        int current = CurrentStepNumber();

        if (current < 0)
        {
            Debug.Log("[COC3TestBypass] There is no step on the go - COC 3 has already finished.", this);
            return;
        }

        if (target <= current)
        {
            Debug.Log(current == total
                ? "[COC3TestBypass] Already on the last step - finish it yourself, or press " + finishKey + "."
                : "[COC3TestBypass] Already on step " + current + ", and steps cannot be undone. " +
                  "Set Jump To Step Number higher than " + current + ".", this);
            return;
        }

        int skipped = Skip(target - current);

        Debug.LogWarning("[COC3TestBypass] TEST: skipped steps " + current + " to " + (current + skipped - 1) +
                         ". Now on step " + CurrentStepNumber() + " of " + total +
                         " - finish it normally to reach the end of the module.", this);
    }

    /// <summary>Skips every remaining step. COC3Manager then raises its completion screen, and the
    /// assessment ends on its report.</summary>
    [ContextMenu("Finish COC3 now")]
    public void FinishNow()
    {
        if (!Ready()) return;

        int current = CurrentStepNumber();
        if (current < 0)
        {
            Debug.Log("[COC3TestBypass] COC 3 has already finished.", this);
            return;
        }

        int total = manager.steps.Count;
        Skip(total - current + 1);

        Debug.LogWarning("[COC3TestBypass] TEST: skipped steps " + current + " to " + total +
                         ". COC 3 is finished - the assessment report should appear now.", this);
    }

    // ==================================================================

    /// <summary>
    /// Presses COC3Manager's own Next, up to count times - the same call an Informational step's
    /// NEXT button makes, so nothing in COC3Manager is bypassed or edited. Stops the moment there is
    /// no step left: SkipToNextStep has no end-of-list guard and would throw if called once more.
    /// </summary>
    private int Skip(int count)
    {
        UseUpThisRun();

        int done = 0;
        for (int i = 0; i < count; i++)
        {
            if (CurrentStepNumber() < 0) break;
            manager.SkipToNextStep();
            done++;
        }
        return done;
    }

    /// <summary>
    /// The step COC3Manager is on, 1-based, or -1 when none is (finished, or not yet started).
    ///
    /// activeSelf, not activeInHierarchy: the station controller hides the whole step screen while
    /// the learner walks, but the current step's own panel stays switched on underneath.
    /// </summary>
    private int CurrentStepNumber()
    {
        if (manager.completionScreen != null && manager.completionScreen.activeSelf) return -1;

        for (int i = 0; i < manager.steps.Count; i++)
        {
            ScreenshotStep step = manager.steps[i];
            if (step != null && step.gameObject.activeSelf) return i + 1;
        }
        return -1;
    }

    /// <summary>The first skip of a run takes COC3AssessmentBridge out of it, so the recorder never
    /// finishes and nothing is saved to the learner's results. Found by name, not type, for the
    /// same reason COC3CompletionNotifier does it that way.</summary>
    private void UseUpThisRun()
    {
        if (usedThisRun) return;
        usedThisRun = true;

        if (!keepTestRunOutOfResults) return;

        var all = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (all == null) return;

        foreach (MonoBehaviour mb in all)
        {
            if (mb == null || !mb.enabled || mb.GetType().Name != "COC3AssessmentBridge") continue;

            mb.enabled = false;
            Debug.Log("[COC3TestBypass] COC3AssessmentBridge switched off - this test run will not be " +
                      "saved to the learner's results or competency record.", mb);
        }
    }

    // ==================================================================

    /// <summary>Every reason not to skip, each with a sentence saying what to do about it.</summary>
    private bool Ready()
    {
        if (!Allowed(quiet: false)) return false;

        if (manager == null) manager = FindAnyObjectByType<COC3Manager>();
        if (manager == null || manager.steps == null || manager.steps.Count == 0)
        {
            Debug.LogWarning("[COC3TestBypass] No COC3Manager with steps in this scene.", this);
            return false;
        }

        bool assessing = AssessmentMode.IsAssessing(ModeId);

        if (assessmentOnly && !assessing)
        {
            Debug.LogWarning("[COC3TestBypass] Ignored - this is not a COC 3 assessment. Start COC 3 from " +
                             "the Assessment menu, or untick Assessment Only.", this);
            return false;
        }

        if (assessing)
        {
            COC3AssessmentManager assessment = COC3AssessmentManager.Instance;

            if (assessment == null)
            {
                Debug.LogWarning("[COC3TestBypass] Ignored - there is no COC3AssessmentManager in this " +
                                 "scene, so there is no report to test.", this);
                return false;
            }

            if (assessment.IsFinished)
            {
                Debug.Log("[COC3TestBypass] The assessment has already finished.", this);
                return false;
            }

            if (!assessment.IsRunning)
            {
                Debug.LogWarning("[COC3TestBypass] Ignored - press Start on the assessment briefing first. " +
                                 "Until then the assessment has not begun, and there is no report to show.", this);
                return false;
            }
        }

        if (requireSeated && COC3StationController.Instance != null && !COC3StationController.Instance.InStepView)
        {
            Debug.LogWarning("[COC3TestBypass] Ignored - sit down at a PC first (walk up to it and press E). " +
                             "The module always ends while the learner is seated, so that is what this " +
                             "test should reproduce. Untick Require Seated to skip anyway.", this);
            return false;
        }

        return true;
    }

    private bool Allowed(bool quiet)
    {
        if (Application.isEditor || Debug.isDebugBuild || allowInBuilds) return true;

        if (!quiet)
            Debug.LogWarning("[COC3TestBypass] Ignored - this is a normal build. Skipping steps here would " +
                             "let a run count as evidence when it should not.", this);
        return false;
    }

    // ==================================================================

    private void OnGUI()
    {
        if (!showOnScreenHint || !Allowed(quiet: true)) return;
        if (assessmentOnly && !AssessmentMode.IsAssessing(ModeId)) return;

        if (hintStyle == null)
        {
            hintStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 10, 4, 4)
            };
            hintStyle.normal.textColor = new Color(1f, 0.85f, 0.3f);
        }

        string text = "TEST BYPASS   " + jumpKey + ": jump to step " + jumpToStepNumber +
                      "   " + finishKey + ": finish COC 3 now" +
                      (usedThisRun ? "   (used - this run is not saved)" : "");

        GUI.Box(new Rect(10f, Screen.height - 40f, 620f, 30f), text, hintStyle);
    }
}
