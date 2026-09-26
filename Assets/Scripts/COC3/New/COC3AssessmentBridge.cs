// COC3AssessmentBridge.cs
using System;
using CircuitCrafter.Assessment;
using CSSSimulation.WindowsSim;
using UnityEngine;

/// <summary>
/// Feeds a COC 3 run into AssessmentRecorder, and through it into the CompetencyLedger.
///
/// ATTACH THIS TO: the COC 3 Managers object, beside COC3Manager and an AssessmentRecorder.
///
/// WHY A BRIDGE IS NEEDED AT ALL
/// AssessmentRecorder closes a stage when TaskManager reports a task complete. COC 3 has no
/// TaskManager - it runs on COC3Manager's step list and on WinTaskGate on the simulated desktop -
/// so that path never fires here and a COC 3 run would record nothing. The recorder already
/// anticipates this: a StageSpec with only a Manual Id waits for CompleteStage(), which is what
/// this calls.
///
/// A STAGE IS A SUB-OUTCOME, NOT A STEP.
/// COC 3's 35 steps do not run in outcome order - LO1.1 is steps 12, 13, 16, 20, 23 and 27, with
/// LO3.1's tests threaded between them. So a milestone cannot be "everything up to step N"; it has
/// to name the steps that belong to it. Each milestone closes when the LAST of its steps is passed,
/// and a refusal is attributed to the milestone CONTAINING the step on screen. Ten stages, one per
/// sub-outcome, is the granularity TESDA's Evidence Plan is written at, and it is what the
/// CompetencyDefinition's Evidence Task Ids want to cite.
///
/// IT ALSO FIRES EVERY STEP ID.
/// CloseStage ignores an id that is not in the recorder's Stages list, so reporting
/// "Step_14_AccessLevels" as well costs nothing and lets you grade per step later without touching
/// this script.
///
/// MISTAKES COME FROM THE DESKTOP, NOT FROM COC3ScoreManager.
/// Every gradeable step in COC 3 is a desktop step; the six that are not are Informational and have
/// no wrong answer. So WinActionBus carries the whole picture, and reading it directly avoids the
/// double count that watching COC3ScoreManager would cause during an assessment - where
/// COC3AssessmentManager is already reporting those same refusals into that counter.
/// </summary>
public class COC3AssessmentBridge : MonoBehaviour
{
    [Serializable]
    public class Milestone
    {
        [Tooltip("The recorder Stage's Manual Id to close. Must match a row in " +
                 "AssessmentRecorder > Stages, or it is silently ignored.")]
        public string stageId = "";

        [Tooltip("For your own reading. Not used at runtime.")]
        public string displayName = "";

        [Tooltip("The step NUMBERS this sub-outcome is made of, 1-based as the HUD shows them.\n\n" +
                 "The stage closes when the highest of them is passed. A refused dialog while any " +
                 "of them is on screen counts as a failed attempt at this stage.")]
        public int[] stepNumbers = new int[0];

        public int LastStep()
        {
            int last = -1;
            if (stepNumbers == null) return last;

            foreach (int n in stepNumbers)
                if (n > last) last = n;

            return last;
        }

        public bool Contains(int stepNumber)
        {
            if (stepNumbers == null) return false;

            foreach (int n in stepNumbers)
                if (n == stepNumber) return true;

            return false;
        }
    }

    [Header("References")]
    [Tooltip("Left empty, the COC3Manager on this object or in the scene is used.")]
    public COC3Manager manager;

    [Tooltip("Left empty, the scene's active AssessmentRecorder is used.")]
    public AssessmentRecorder recorder;

    [Header("What closes a stage")]
    [Tooltip("The ten COC 3 sub-outcomes. These are the ids to put in AssessmentRecorder > Stages " +
             "and in the CompetencyDefinition's Evidence Task Ids.")]
    public Milestone[] milestones =
    {
        new Milestone { stageId = "coc3_lo2_1", displayName = "LO2.1 Job order and host name",
                        stepNumbers = new[] { 1, 2 } },
        new Milestone { stageId = "coc3_lo2_2", displayName = "LO2.2 Static addressing and DNS",
                        stepNumbers = new[] { 3, 4 } },
        new Milestone { stageId = "coc3_lo2_3", displayName = "LO2.3 Server roles and services",
                        stepNumbers = new[] { 5, 6, 7, 8, 9, 10 } },
        new Milestone { stageId = "coc3_lo2_4", displayName = "LO2.4 Join the client to the domain",
                        stepNumbers = new[] { 11 } },
        new Milestone { stageId = "coc3_lo1_1", displayName = "LO1.1 Create accounts and shares",
                        stepNumbers = new[] { 12, 13, 16, 20, 23, 27 } },
        new Milestone { stageId = "coc3_lo1_2", displayName = "LO1.2 Assign access levels",
                        stepNumbers = new[] { 14, 15, 17, 21, 24, 25, 28 } },
        new Milestone { stageId = "coc3_lo1_3", displayName = "LO1.3 Verify access levels hold",
                        stepNumbers = new[] { 32, 33, 34 } },
        new Milestone { stageId = "coc3_lo3_1", displayName = "LO3.1 Test plan and the five tests",
                        stepNumbers = new[] { 18, 19, 22, 26, 29, 30 } },
        new Milestone { stageId = "coc3_lo3_2", displayName = "LO3.2 Diagnose the fault",
                        stepNumbers = new[] { 31 } },
        new Milestone { stageId = "coc3_lo3_3", displayName = "LO3.3 Service report",
                        stepNumbers = new[] { 35 } }
    };

    [Tooltip("Also report every completed step's own stepId as a stage id.\n\n" +
             "Leave on. An id the recorder does not list is ignored, so this costs nothing and it " +
             "lets a CompetencyDefinition cite individual steps as evidence later.")]
    public bool reportStepIds = true;

    [Header("Mistakes")]
    [Tooltip("Category recorded against a refused dialog. Keep the set small so an instructor can " +
             "count patterns rather than read prose.")]
    public string mistakeCategory = "configuration";

    [Tooltip("Count a refusal from a GUARD - the wrong tree node, the wrong container - as a " +
             "mistake too. Off records only the dialogs carrying a step's real answer.")]
    public bool countGuardRefusals = true;

    [Header("Starting")]
    [Tooltip("Begin the recorder's run the first time a step is actually on screen - i.e. when the " +
             "learner sits down at the first machine.\n\n" +
             "LEAVE THIS ON AND LEAVE THE RECORDER'S OWN 'Begin On Start' OFF. It is what makes a " +
             "STORY run record: there is no assessment intro panel in story mode to press Start on, " +
             "so a recorder waiting to be begun by hand would never begin at all. Sitting down is " +
             "also a better moment than scene load in Assessment mode, because the timer then does " +
             "not run while the intro panel is still up.")]
    public bool beginWhenFirstStepAppears = true;

    [Header("Finishing")]
    [Tooltip("Call Finish() on the recorder when COC3Manager's completion screen appears.\n\n" +
             "A safety net rather than the main path: the tenth stage closes at step 35, so the " +
             "recorder's own Finish When All Stages Complete normally ends the run first.")]
    public bool finishWhenModuleEnds = true;

    [Tooltip("Clear the quiz tally the moment the run starts.\n\n" +
             "KnowledgeCheck's own comment says the recorder does this in Begin(); it does not. " +
             "Without it, a quiz answered in an earlier scene this session still counts as this " +
             "run's underpinning-knowledge evidence.")]
    public bool resetKnowledgeTally = true;

    public bool verbose = true;

    // ------------------------------------------------------------------

    private string currentStepId = "";
    private int currentStepNumber = -1;
    private bool sawAnyStep;
    private bool finished;
    private bool knowledgeReset;

    private void Awake()
    {
        if (manager == null) manager = GetComponent<COC3Manager>();
        if (manager == null) manager = FindAnyObjectByType<COC3Manager>();

        if (manager == null)
            Debug.LogWarning("[COC3AssessmentBridge] No COC3Manager found - nothing will be " +
                             "recorded. Put this on the COC 3 Managers object.", this);
    }

    private void OnEnable()
    {
        WinActionBus.OnAction += HandleDesktopAction;
    }

    private void OnDisable()
    {
        WinActionBus.OnAction -= HandleDesktopAction;
    }

    private AssessmentRecorder Recorder()
    {
        if (recorder == null) recorder = AssessmentRecorder.Active;
        return recorder;
    }

    // ------------------------------------------------------------------

    private void Update()
    {
        if (manager == null || manager.steps == null) return;

        // Done here rather than in Start() because the recorder is normally begun by the assessment
        // intro panel, not on load - so "the run has started" is not a moment Start() can see.
        if (resetKnowledgeTally && !knowledgeReset)
        {
            AssessmentRecorder r = Recorder();
            if (r != null && r.IsRunning)
            {
                KnowledgeCheck.Begin(r.ModuleId);
                knowledgeReset = true;
            }
        }

        int number = -1;
        string id = "";

        for (int i = 0; i < manager.steps.Count; i++)
        {
            ScreenshotStep step = manager.steps[i];
            if (step == null || !step.gameObject.activeInHierarchy) continue;

            number = i + 1;
            id = step.stepData != null ? step.stepData.stepId : step.name;
            break;
        }

        if (number > 0)
        {
            if (number == currentStepNumber) return;

            // The step we were on has left the screen, which in COC3Manager means it was passed.
            if (currentStepNumber > 0) CloseStep(currentStepNumber, currentStepId);

            currentStepNumber = number;
            currentStepId = id;

            if (!sawAnyStep)
            {
                sawAnyStep = true;
                BeginRun();
            }
            return;
        }

        // NO STEP ON SCREEN IS NOT THE END OF THE MODULE.
        //
        // COC3StationController.SetStepViewActive toggles stepUIRoot, so every step panel is
        // inactive in the hierarchy the whole time the learner is WALKING - which they do at
        // steps 11, 19, 22, 26 and 29, and any time they stand up to look around. Treating that
        // as the end would close the current step as passed and finish the run at step 11.
        //
        // The completion screen is the only honest signal, and COC3Manager always raises it -
        // COC3CompletionNotifier is the component sitting on it.
        if (finished || !sawAnyStep) return;
        if (manager.completionScreen == null || !manager.completionScreen.activeSelf) return;

        if (currentStepNumber > 0) CloseStep(currentStepNumber, currentStepId);
        finished = true;

        if (!finishWhenModuleEnds) return;

        AssessmentRecorder rec = Recorder();
        if (rec == null || !rec.IsRunning) return;

        if (verbose) Debug.Log("[COC3AssessmentBridge] Completion screen up - closing the run.", this);
        rec.Finish();
    }

    /// <summary>
    /// Starts the recorder's run, if nothing else has.
    ///
    /// This is the piece that makes a STORY run record at all. AssessmentRecorder only produces an
    /// AssessmentResult if Begin() was called, and in story mode nothing calls it: there is no
    /// assessment intro panel to press Start on, and StoryRunRecorder cannot help because it polls
    /// TaskManager task ids, which COC 3 does not use - AllRequiredComplete() returns false on an
    /// empty list, so it waits forever. Recorder + this bridge is the whole story path.
    /// </summary>
    private void BeginRun()
    {
        if (!beginWhenFirstStepAppears) return;

        AssessmentRecorder r = Recorder();
        if (r == null || r.IsRunning || r.IsFinished) return;

        if (verbose)
            Debug.Log("[COC3AssessmentBridge] First step on screen - beginning the run.", this);

        r.Begin();
    }

    private void CloseStep(int number, string stepId)
    {
        if (reportStepIds && !string.IsNullOrEmpty(stepId))
            AssessmentRecorder.Stage(stepId);

        if (milestones == null) return;

        foreach (Milestone milestone in milestones)
        {
            if (milestone == null || string.IsNullOrEmpty(milestone.stageId)) continue;
            if (milestone.LastStep() != number) continue;

            AssessmentRecorder.Stage(milestone.stageId);

            if (verbose)
                Debug.Log($"[COC3AssessmentBridge] Step {number} was the last of " +
                          $"'{milestone.stageId}' - stage reported complete.", this);
        }
    }

    /// <summary>
    /// A dialog the simulated desktop refused.
    ///
    /// Reported as an ATTEMPT as well as a mistake, because the two answer different questions: the
    /// mistake count says how much went wrong, first-attempt accuracy says whether they knew it
    /// before being corrected. The second is the one worth putting in front of an assessor.
    ///
    /// The attempt goes against the MILESTONE, not only the step, because ReportAttempt silently
    /// ignores a stage id the recorder does not list - so on a ledger graded per sub-outcome a
    /// refusal reported only as "Step_14_AccessLevels" would land nowhere, and CloseStage's
    /// "no attempts recorded means right first time" rule would then score the outcome 100%.
    /// </summary>
    private void HandleDesktopAction(string actionId, bool success)
    {
        if (success || finished) return;
        if (currentStepNumber <= 0 || string.IsNullOrEmpty(currentStepId)) return;
        if (!countGuardRefusals && IsGuard(actionId)) return;

        AssessmentRecorder.Attempt(currentStepId, false);

        string milestone = MilestoneFor(currentStepNumber);
        if (!string.IsNullOrEmpty(milestone)) AssessmentRecorder.Attempt(milestone, false);

        AssessmentRecorder.Mistake(mistakeCategory, $"'{actionId}' refused on {currentStepId}.");
    }

    /// <summary>The stage id of the sub-outcome this step belongs to, or null.</summary>
    private string MilestoneFor(int stepNumber)
    {
        if (milestones == null) return null;

        foreach (Milestone milestone in milestones)
        {
            if (milestone == null || string.IsNullOrEmpty(milestone.stageId)) continue;
            if (milestone.Contains(stepNumber)) return milestone.stageId;
        }

        return null;
    }

    /// <summary>A guard is an action that only opens something - the "did you click the right
    /// node" checks - rather than one carrying a step's answer.</summary>
    private static bool IsGuard(string actionId)
    {
        return !string.IsNullOrEmpty(actionId) && actionId.StartsWith("open_");
    }

#if UNITY_EDITOR
    /// <summary>
    /// Catches the two ways the milestone table can be wrong in a way nothing else would notice: a
    /// step claimed by two sub-outcomes, and a step claimed by none. A step in no milestone is not
    /// an error - the run still records it by stepId - but it means a refusal there is attributed
    /// to no outcome, which is worth knowing before an assessment rather than after.
    /// </summary>
    private void OnValidate()
    {
        if (milestones == null) return;

        var owner = new System.Collections.Generic.Dictionary<int, string>();

        foreach (Milestone milestone in milestones)
        {
            if (milestone == null || milestone.stepNumbers == null) continue;

            foreach (int n in milestone.stepNumbers)
            {
                if (owner.TryGetValue(n, out string already) && already != milestone.stageId)
                {
                    Debug.LogWarning($"[COC3AssessmentBridge] Step {n} is claimed by both " +
                                     $"'{already}' and '{milestone.stageId}'. A refusal there will " +
                                     "be attributed to the first one only.", this);
                    continue;
                }
                owner[n] = milestone.stageId;
            }
        }

        int total = manager != null && manager.steps != null ? manager.steps.Count : 35;
        string missing = "";

        for (int n = 1; n <= total; n++)
            if (!owner.ContainsKey(n)) missing += (missing.Length > 0 ? ", " : "") + n;

        if (missing.Length > 0)
            Debug.LogWarning($"[COC3AssessmentBridge] These steps belong to no sub-outcome: " +
                             $"{missing}. Refusals there will be counted as mistakes but will not " +
                             "affect any outcome's first-attempt accuracy.", this);
    }
#endif
}