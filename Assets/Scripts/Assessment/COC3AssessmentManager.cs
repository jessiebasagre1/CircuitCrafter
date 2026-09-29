// COC3AssessmentManager.cs
using System.Collections.Generic;
using System.Text;
using CSSSimulation.WindowsSim;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Runs COC 3 as a graded assessment: timer, score, mistakes, and a report at the end.
/// Same shape as COC1AssessmentManager and COC2AssessmentManager, so the wiring carries over.
///
/// ATTACH THIS TO: the always-active Managers object, beside COC3Manager.
/// LEAVE THE COMPONENT UNTICKED and put it in AssessmentIntroPanel ▸ Begin On Start, exactly as
/// you did for COC 2. Start() does not run on a disabled component, which is what holds the timer
/// at zero until the learner presses Start.
///
/// IT DOES NOTHING UNLESS ASSESSMENT MODE IS ON.
/// Everything is behind AssessmentMode.IsAssessing("COC3"). Entering COC 3 from the campaign menu
/// leaves the scene exactly as it is - same hints, same dialogs, same scoring.
///
/// HOW COC 3 DIFFERS FROM COC 1 AND COC 2
/// Those two are built from stages this component owns. COC 3 already has a 35-step list, a score
/// and a mistake count of its own, and duplicating them would give you two numbers that disagree
/// the first time one of them is wrong.
///
/// So this OWNS NOTHING it does not have to. COC3ScoreManager stays the single source of truth for
/// score and mistakes; this watches that counter, and attributes each increase to whichever step
/// was on screen when it happened. That is where the per-step breakdown in the report comes from,
/// and it cannot drift from the HUD because it is the same number.
///
/// THE ONE THING IT ADDS TO THE COUNT
/// A rejected dialog on the simulated desktop - a wrong IP, the wrong container selected, Full
/// control instead of Modify - never reaches COC3Manager, because WinTaskGate refuses it inside
/// the window and the step never gets submitted. Those were free. During an assessment they are
/// reported to COC3ScoreManager like any other mistake, so the desktop steps are graded the same
/// way the typed ones are.
///
/// NOTHING IN COC3Manager, ScreenshotStep OR COC3ScoreManager CHANGES.
/// </summary>
public class COC3AssessmentManager : MonoBehaviour
{
    public static COC3AssessmentManager Instance { get; private set; }

    private const string ModeId = "COC3";

    /// <summary>What happened on one step. Built as the run goes, read by the report.</summary>
    [System.Serializable]
    public class StepRecord
    {
        public string stepId = "";
        public int number;            // 1-based, so it matches "Step 14 of 35" on screen
        public int mistakes;
        public float seconds;
        public bool completed;
        public int hints;             // hints opened on this step (assessment with hints allowed)
    }

    [HeaderAttribute("Module")]
    [TooltipAttribute("Left empty, the scene's COC3Manager is found automatically.")]
    public COC3Manager manager;

    [TooltipAttribute("Optional. Hints and intro dialogs are switched off through this for the run.")]
    public COC3Tutorial tutorial;

    [TooltipAttribute("Left empty, the manager's own score manager is used. This stays the one " +
             "place score and mistakes are counted - nothing here keeps a second tally.")]
    public COC3ScoreManager scoreManager;

    [HeaderAttribute("HUD - your existing assessment panel")]
    public GameObject assessmentHud;
    public TMP_Text timerText;
    public TMP_Text scoreText;
    public TMP_Text mistakesText;

    [TooltipAttribute("Optional. Shows \"Step 14 of 35\" so the learner knows where they are " +
             "without the campaign progress bar.")]
    public TMP_Text stepText;

    [TooltipAttribute("Optional, and the reason you asked for this. Shows how many mistakes have " +
             "been made ON THE STEP CURRENTLY ON SCREEN, which is the number that tells a learner " +
             "to stop and re-read rather than keep guessing.")]
    public TMP_Text stepMistakesText;

    [HeaderAttribute("Scoring")]
    [TooltipAttribute("Points lost for a dialog the simulated desktop refuses. COC3Manager's own " +
             "penalty still applies to the typed steps, untouched.")]
    public int desktopMistakePenalty = 5;

    [TooltipAttribute("Count a refusal from a guard - the wrong tree node, the wrong container - " +
             "as a mistake too.\n\n" +
             "On: navigating to the wrong place is assessed, which is what the desktop is for.\n" +
             "Off: only the dialog that carries the step's actual answer is graded.")]
    public bool countGuardRejections = true;

    [TooltipAttribute("Added at the end if the whole run was clean.")]
    public int perfectRunBonus = 50;

    [HeaderAttribute("Timer")]
    [TooltipAttribute("0 = count up with no limit.")]
    public float timeLimitSeconds = 0f;

    public bool endOnTimeUp = true;

    [HeaderAttribute("Scene changes for assessment")]
    [TooltipAttribute("Switched off for the run - your campaign HUD, the hint button, the tutorial " +
             "prompt.")]
    public GameObject[] disableInAssessment;

    [TooltipAttribute("Switched on for the run.")]
    public GameObject[] enableInAssessment;

    public bool suppressTutorialDialogs = true;

    [TooltipAttribute("Ticked: COC3Tutorial's tutorial mode is turned off, so the Hint button gives nothing " +
             "away.\n\nUnticked: the learner may open hints during the assessment. The hint opens only when " +
             "asked for (see COC3Tutorial > Auto Hint In Assessment), and every step where one was opened " +
             "is noted in the report. If the Hint button is in Disable In Assessment above, take it out.")]
    public bool disableHints = true;

    [HeaderAttribute("Report")]
    public GameObject reportPanel;
    public TMP_Text reportText;

    [TooltipAttribute("Optional. Wired automatically if assigned.")]
    public Button returnToMenuButton;

    public string mainMenuSceneName = "MainMenu";

    // ------------------------------------------------------------------

    public int Score { get { return scoreManager != null ? scoreManager.Score : localScore; } }
    public int Mistakes { get { return scoreManager != null ? scoreManager.Mistakes : localMistakes; } }
    public float ElapsedSeconds { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsFinished { get; private set; }

    /// <summary>True when the run ended because the time limit was reached, not because the
    /// learner finished or pressed Finish. Read by the report.</summary>
    public bool TimedOut { get; private set; }

    // True while THIS manager is holding the player still, so it can give the lock back.
    private bool inputLocked;

    private readonly List<StepRecord> records = new List<StepRecord>();
    private StepRecord current;

    private int lastMistakeCount;
    private int hintsUsed;
    private int localScore, localMistakes;
    private int stepCount;
    private bool sawAnyStep;
    private bool reportPending;

    // ==================================================================

    private void Awake()
    {
        Instance = this;

        if (manager == null) manager = FindAnyObjectByType<COC3Manager>();
        if (manager != null && scoreManager == null) scoreManager = manager.scoreManager;
        if (tutorial == null) tutorial = FindAnyObjectByType<COC3Tutorial>();
    }

    private void Start()
    {
        if (!AssessmentMode.IsAssessing(ModeId))
        {
            // Campaign run. Leave the scene completely alone and shut ourselves down.
            if (assessmentHud != null) assessmentHud.SetActive(false);
            if (reportPanel != null) reportPanel.SetActive(false);

            foreach (var go in enableInAssessment) if (go != null) go.SetActive(false);

            enabled = false;
            return;
        }

        BeginAssessment();
    }

    private void OnDestroy()
    {
        WinActionBus.OnAction -= HandleDesktopAction;

        // Never leave the flag set for whatever scene loads next.
        DialogManager.SuppressTutorials = false;

        // A lock still held here would follow the learner into the next scene.
        ReleaseInput();

        if (Instance == this) Instance = null;
    }

    private void BeginAssessment()
    {
        foreach (var go in disableInAssessment) if (go != null) go.SetActive(false);
        foreach (var go in enableInAssessment) if (go != null) go.SetActive(true);

        if (assessmentHud != null) assessmentHud.SetActive(true);
        if (reportPanel != null) reportPanel.SetActive(false);

        DialogManager.SuppressTutorials = suppressTutorialDialogs;

        if (disableHints && tutorial != null) tutorial.SetTutorialMode(false);
        if (tutorial != null) tutorial.HintsAllowedInAssessment = !disableHints;
        hintsUsed = 0;

        records.Clear();
        current = null;
        sawAnyStep = false;
        ElapsedSeconds = 0f;

        // GUIDANCE: a fresh journal for this run's mistakes and their withheld explanations.
        CircuitCrafter.Guidance.MistakeJournal.BeginRun(ModeId);
        localScore = 0;
        localMistakes = 0;
        IsFinished = false;
        TimedOut = false;
        IsRunning = true;

        stepCount = manager != null && manager.steps != null ? manager.steps.Count : 0;
        lastMistakeCount = Mistakes;

        WinActionBus.OnAction -= HandleDesktopAction;
        WinActionBus.OnAction += HandleDesktopAction;

        if (returnToMenuButton != null)
        {
            returnToMenuButton.onClick.RemoveListener(ReturnToMenu);
            returnToMenuButton.onClick.AddListener(ReturnToMenu);
        }

        RefreshHud();
        Debug.Log("[COC3AssessmentManager] Assessment started. " + stepCount + " steps.");
    }

    // ==================================================================

    private void Update()
    {
        // BEFORE the IsRunning guard, deliberately. Finish() has already cleared that flag by the
        // time a report can be pending, so a check after it would never run.
        if (reportPending && !DialogIsOpen()) ShowReport();

        if (!IsRunning) return;

        ElapsedSeconds += Time.deltaTime;

        TrackStep();
        TrackMistakes();

        if (current != null) current.seconds += Time.deltaTime;

        if (timeLimitSeconds > 0f && ElapsedSeconds >= timeLimitSeconds)
        {
            ElapsedSeconds = timeLimitSeconds;
            if (endOnTimeUp)
            {
                TimedOut = true;
                Finish();
                return;
            }
        }

        if (ModuleFinished())
        {
            Finish();
            return;
        }

        RefreshHud();
    }

    /// <summary>
    /// Opens a record when the step on screen changes, and closes the one before it.
    ///
    /// Polled rather than driven by an event, because COC3Manager exposes neither the current
    /// index nor a step-changed callback - the same reason COC3Tutorial and COC3StepGate both read
    /// the active panel this way. One more reader of the same fact, not a new assumption.
    /// </summary>
    private void TrackStep()
    {
        ScreenshotStep active = ActiveStep();

        if (active == null || active.stepData == null) return;

        sawAnyStep = true;

        string id = active.stepData.stepId;
        if (current != null && current.stepId == id) return;

        // The step being left is finished with - a step you have moved on from was completed.
        if (current != null) current.completed = true;

        current = new StepRecord
        {
            stepId = id,
            number = IndexOf(active) + 1
        };

        records.Add(current);
    }

    /// <summary>
    /// Attributes every increase in COC3ScoreManager's mistake count to the step on screen.
    ///
    /// Watching the counter rather than intercepting the failure is what keeps the report and the
    /// HUD agreeing. There is one number; this only records WHERE it went up.
    /// </summary>
    private void TrackMistakes()
    {
        int now = Mistakes;
        if (now <= lastMistakeCount)
        {
            lastMistakeCount = now;
            return;
        }

        int added = now - lastMistakeCount;
        lastMistakeCount = now;

        if (current != null) current.mistakes += added;
    }

    /// <summary>
    /// A dialog the simulated desktop refused.
    ///
    /// These never reach COC3Manager: WinTaskGate shows the Windows error and the step is never
    /// submitted, so nothing was ever counted. Reported here so a desktop step is graded the same
    /// way a typed one is - and reported THROUGH COC3ScoreManager, so it lands in the same counter
    /// as everything else rather than in a second tally of my own.
    /// </summary>
    private void HandleDesktopAction(string actionId, bool success)
    {
        if (!IsRunning || success) return;

        if (!countGuardRejections && IsGuardAction(actionId)) return;

        if (scoreManager != null)
        {
            scoreManager.AddMistake(desktopMistakePenalty);   // TrackMistakes picks this up
        }
        else
        {
            localMistakes++;
            localScore -= desktopMistakePenalty;
            if (current != null) current.mistakes++;
        }

        Debug.Log($"[COC3AssessmentManager] '{actionId}' refused on " +
                  (current != null ? current.stepId : "no step") + " - counted as a mistake.");
    }

    /// <summary>
    /// A guard is an action bound to no step - the tree node, the container, the "open this"
    /// checks. Everything the desktop reports is either one of those or a step's real answer.
    /// </summary>
    private static bool IsGuardAction(string actionId)
    {
        return !string.IsNullOrEmpty(actionId) && actionId.StartsWith("open_");
    }

    // ==================================================================

    /// <summary>
    /// True once the module is over.
    ///
    /// Two signals, because either alone is wrong: the completion screen is the honest one but is
    /// optional, and "no step is on screen" is true for a frame before the first step appears as
    /// well as after the last one leaves - hence sawAnyStep.
    /// </summary>
    private bool ModuleFinished()
    {
        if (manager == null) return false;

        if (manager.completionScreen != null && manager.completionScreen.activeSelf) return true;

        // WALKING BETWEEN MACHINES IS NOT THE END OF THE MODULE.
        //
        // COC3StationController switches its Step UI Root off whenever the learner stands up -
        // including the automatic exit when the next step belongs to the other PC - and every
        // step panel goes with it. "No step panel on screen" was read as "finished": the run
        // ended on the spot, the timer stopped, the player was locked for a report that sat
        // inside the hidden UI, and the learner was left frozen on the way to the client.
        if (COC3StationController.Instance != null && !COC3StationController.Instance.InStepView)
            return false;

        return sawAnyStep && ActiveStep() == null;
    }

    public void Finish()
    {
        if (!IsRunning) return;

        IsRunning = false;
        IsFinished = true;

        // The run is over. Hold the player still and give them the mouse, so the report can be
        // read and Back to Menu clicked - and nothing they do from here can change the result.
        LockInput();

        // A step still in progress when the clock ran out was NOT finished, so it must not be
        // counted as complete in the report.
        if (current != null && !TimedOut) current.completed = true;

        bool perfect = Mistakes == 0;
        if (perfect && perfectRunBonus > 0)
        {
            if (scoreManager != null) scoreManager.AddScore(perfectRunBonus);
            else localScore += perfectRunBonus;
        }

        DialogManager.SuppressTutorials = false;

        Debug.Log($"[COC3AssessmentManager] Finished. Score {Score}, mistakes {Mistakes}, " +
                  $"time {FormatTime(ElapsedSeconds)}.");

        RefreshHud();

        // The run is over the moment this is entered - score, bonus and timer are final. Only the
        // REPORT waits, so it never lands on top of a dialog still being read.
        if (DialogIsOpen())
        {
            reportPending = true;
            return;
        }

        ShowReport();
    }

    private static bool DialogIsOpen()
    {
        return DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen;
    }

    private void ShowReport()
    {
        reportPending = false;

        if (reportPanel != null) reportPanel.SetActive(true);
        if (reportText != null) reportText.text = BuildReport();

        // A report inside the station's Step UI Root cannot be seen while the learner is walking
        // (a run that times out between machines). Say where it is rather than leave them frozen.
        if (reportPanel != null && !reportPanel.activeInHierarchy)
        {
            Transform off = reportPanel.transform.parent;
            while (off != null && off.gameObject.activeSelf) off = off.parent;
            Debug.LogWarning("[COC3AssessmentManager] The report is switched on but cannot be seen: its " +
                             "parent '" + (off != null ? off.name : "?") + "' is switched off. Move Report " +
                             "Panel out of the station's Step UI Root, onto a canvas that is always on.", reportPanel);
        }

        if (reportPanel == null && reportText == null)
            Debug.LogWarning("[COC3AssessmentManager] Finished, but neither Report Panel nor " +
                             "Report Text is assigned - the learner sees nothing. Wire them on " +
                             "this component.\n\n" + BuildReport(), this);
    }

    private void LockInput()
    {
        if (inputLocked) return;

        inputLocked = true;
        PlayerInputLock.Acquire();   // PlayerMovement and MouseLook stop reading input
        CursorControl.Free();        // the report's buttons need a visible, usable cursor
    }

    private void ReleaseInput()
    {
        if (!inputLocked) return;

        inputLocked = false;
        PlayerInputLock.Release();
    }

    /// <summary>Called by COC3Tutorial the first time a hint is opened on a step. Recorded, not
    /// penalised - the report shows where help was used.</summary>
    public void NoteHintUsed()
    {
        if (!IsRunning) return;

        hintsUsed++;
        if (current != null) current.hints++;
        Debug.Log("[COC3AssessmentManager] Hint opened on " +
                  (current != null ? "step " + current.number : "no step") + ".", this);
    }

    private string BuildReport()
    {
        int completed = 0;
        int cleanSteps = 0;

        foreach (var r in records)
        {
            if (r.completed) completed++;
            if (r.mistakes == 0) cleanSteps++;
        }

        var sb = new StringBuilder();
        sb.AppendLine("=== COC 3 Assessment Report ===");
        sb.AppendLine();
        if (TimedOut)
        {
            sb.AppendLine("*** TIME'S UP ***");
            sb.AppendLine($"The assessment ended because the time limit of {FormatTime(timeLimitSeconds)} was reached.");
            sb.AppendLine("Stages that were not finished by then are marked incomplete.");
            sb.AppendLine();
        }
        sb.AppendLine($"Time taken:     {FormatTime(ElapsedSeconds)}");
        sb.AppendLine($"Final score:    {Score}");
        sb.AppendLine($"Mistakes:       {Mistakes}");
        sb.AppendLine($"Steps reached:  {records.Count} of {stepCount}");
        sb.AppendLine($"Steps complete: {completed} of {stepCount}");
        sb.AppendLine($"Clean first go: {cleanSteps} of {records.Count}");
        if (!disableHints)
            sb.AppendLine($"Hints used:     {hintsUsed} step" + (hintsUsed == 1 ? "" : "s"));
        sb.AppendLine();

        // The part that matters to whoever reads this: not the total, but which steps cost them.
        var trouble = new List<StepRecord>();
        foreach (var r in records) if (r.mistakes > 0) trouble.Add(r);

        if (trouble.Count == 0)
        {
            sb.AppendLine("No mistakes on any step.");
        }
        else
        {
            sb.AppendLine("Where the mistakes were:");
            foreach (var r in trouble)
                sb.AppendLine($"  Step {r.number,2}  {Readable(r.stepId),-34} " +
                              $"{r.mistakes} mistake" + (r.mistakes == 1 ? "" : "s"));
        }

        sb.AppendLine();
        sb.AppendLine("Every step:");

        foreach (var r in records)
            sb.AppendLine($"  {r.number,2}. {Readable(r.stepId),-34} " +
                          $"{FormatTime(r.seconds)}   " +
                          (r.mistakes == 0 ? "clean" : r.mistakes + " wrong") +
                          (r.hints > 0 ? "   (hint used)" : ""));

        return sb.ToString();
    }

    /// <summary>"Step_14_AccessLevels" -> "Access Levels". The stepId is a filename, not a title.</summary>
    private static string Readable(string stepId)
    {
        if (string.IsNullOrEmpty(stepId)) return "(unnamed)";

        int second = stepId.IndexOf('_', stepId.IndexOf('_') + 1);
        string tail = second >= 0 && second + 1 < stepId.Length ? stepId.Substring(second + 1) : stepId;

        var sb = new StringBuilder();
        for (int i = 0; i < tail.Length; i++)
        {
            if (i > 0 && char.IsUpper(tail[i]) && !char.IsUpper(tail[i - 1])) sb.Append(' ');
            sb.Append(tail[i]);
        }
        return sb.ToString();
    }

    // ==================================================================

    private void RefreshHud()
    {
        if (timerText != null)
        {
            timerText.text = timeLimitSeconds > 0f
                ? FormatTime(Mathf.Max(0f, timeLimitSeconds - ElapsedSeconds))
                : FormatTime(ElapsedSeconds);
        }

        if (scoreText != null) scoreText.text = $"Score: {Score}";
        if (mistakesText != null) mistakesText.text = $"Mistakes: {Mistakes}";

        if (stepText != null)
            stepText.text = current != null ? $"Step {current.number} of {stepCount}" : "";

        if (stepMistakesText != null)
        {
            int onThisStep = current != null ? current.mistakes : 0;
            stepMistakesText.text = onThisStep == 0
                ? "This step: clean"
                : $"This step: {onThisStep} mistake" + (onThisStep == 1 ? "" : "s");
        }
    }

    public void ReturnToMenu()
    {
        // Tearing the scene down: zero the counter rather than release one, because a station the
        // learner was standing in may hold a lock of its own.
        inputLocked = false;
        PlayerInputLock.ReleaseAll();

        AssessmentMode.End();
        DialogManager.SuppressTutorials = false;

        if (!string.IsNullOrEmpty(mainMenuSceneName)) SceneManager.LoadScene(mainMenuSceneName);
    }

    // ==================================================================

    private ScreenshotStep ActiveStep()
    {
        if (manager == null || manager.steps == null) return null;

        for (int i = 0; i < manager.steps.Count; i++)
        {
            var s = manager.steps[i];
            if (s != null && s.gameObject.activeInHierarchy) return s;
        }
        return null;
    }

    private int IndexOf(ScreenshotStep step)
    {
        if (manager == null || manager.steps == null) return -1;

        for (int i = 0; i < manager.steps.Count; i++)
            if (manager.steps[i] == step) return i;

        return -1;
    }

    private static string FormatTime(float seconds)
    {
        int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
        return $"{total / 60:00}:{total % 60:00}";
    }
}