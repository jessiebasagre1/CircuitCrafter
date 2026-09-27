using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using CircuitCrafter.Guidance;

/// <summary>
/// Runs COC 1 as a graded assessment: timer, score, mistakes, a waypoint pointing at the next
/// station, and one dialog after each task is completed.
///
/// ATTACH THIS TO: any always-active object in the COC 1 scene - the Managers object is ideal.
///
/// IT DOES NOTHING UNLESS ASSESSMENT MODE IS ON.
/// Every behaviour below is behind AssessmentMode.IsAssessing("COC1"). Entering COC 1 from the
/// campaign menu leaves the scene exactly as it is today - same tutorials, same quizzes, same
/// saving. That is deliberate: one scene, two modes, no duplicate copy to keep in sync.
///
/// WHAT IT SUPPRESSES, AND HOW
///   Quizzes and autosaving  - by switching off the objects you list in Disable In Assessment.
///                             QuizManager listens to TaskManager itself, so removing it from
///                             the scene is cleaner than trying to intercept it.
///   Tutorial dialogs        - via DialogManager.SuppressTutorials. Every ShowDialog call in
///                             the project becomes a no-op, EXCEPT the ones this manager makes,
///                             which pass ignoreSuppression. That is what lets the per-part
///                             removal dialogs go quiet while the stage dialogs still play.
///
/// MISTAKES
/// PCPartItem.TryBeginDrag() already works out whether a part can be picked up, and both cases
/// you care about surface there: a cable-gated part reports "still plugged in", a RAM stick
/// reports "still closed". One reporting call in that blocked branch catches both, so no
/// per-part components and no duplicated rules.
/// </summary>
public class COC1AssessmentManager : MonoBehaviour
{
    public static COC1AssessmentManager Instance { get; private set; }

    private const string ModeId = "COC1";

    [System.Serializable]
    public class Stage
    {
        [Tooltip("A TaskManager taskId that ends this stage, e.g. \"assemble_pc\".")]
        public string taskId;

        [Tooltip("Shown in the report, e.g. \"Assemble and disassemble\".")]
        public string displayName = "Stage";

        [Tooltip("Played when this stage completes - the 'now go to the next station' dialog.")]
        public DialogSequence completionDialog;

        [Tooltip("Where the waypoint arrow points AFTER this stage completes - normally the next station.")]
        public Transform nextWaypointTarget;

        [Tooltip("Points awarded for completing this stage.")]
        public int points = 50;

        [HideInInspector] public bool completed;
    }

    // ------------------------------------------------------------------
    [Header("Stages, in order")]
    [Tooltip("Each entry ends when its taskId completes. The last one finishes the assessment.")]
    public List<Stage> stages = new List<Stage>();

    [Tooltip("Where the arrow points at the very start, before any stage is done - normally the PC.")]
    public Transform firstWaypointTarget;

    // ------------------------------------------------------------------
    [Header("HUD")]
    public TMP_Text timerText;
    public TMP_Text scoreText;
    public TMP_Text mistakesText;

    [Tooltip("Shown only in assessment mode - the timer/score bar. Left off in campaign mode.")]
    public GameObject assessmentHud;

    // ------------------------------------------------------------------
    [Header("Scoring")]
    [Tooltip("Points removed per mistake. The score never drops below zero.")]
    public int mistakePenalty = 10;

    [Tooltip("Bonus if the whole assessment is finished with no mistakes at all.")]
    public int perfectRunBonus = 50;

    // ------------------------------------------------------------------
    [Header("Timer")]
    [Tooltip("Seconds allowed. 0 = count up with no limit.")]
    public float timeLimitSeconds = 0f;

    [Tooltip("End the assessment automatically when the time limit is reached.")]
    public bool endOnTimeUp = true;

    // ------------------------------------------------------------------
    [Header("Mode Switching")]
    [Tooltip("Switched OFF in assessment mode - QuizManager, TaskCheckpointAutosaver, IntroDialogTrigger, any TaskDialogTrigger.")]
    public GameObject[] disableInAssessment;

    [Tooltip("Switched ON in assessment mode only - the HUD, a Finish button, anything assessment-specific.")]
    public GameObject[] enableInAssessment;

    [Tooltip("Silence every tutorial dialog in the scene. This manager's own stage dialogs still play.")]
    public bool suppressTutorialDialogs = true;

    // ------------------------------------------------------------------
    [Header("Report")]
    public GameObject reportPanel;
    public TMP_Text reportText;

    [Tooltip("Optional - returns to the Main Menu. Wire its OnClick to ReturnToMenu().")]
    public Button returnToMenuButton;

    [Tooltip("Scene loaded by the Return To Menu button.")]
    public string mainMenuSceneName = "MainMenu";

    // ------------------------------------------------------------------
    public int Score { get; private set; }
    public int Mistakes { get; private set; }
    public float ElapsedSeconds { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsFinished { get; private set; }

    /// <summary>True when the run ended because the time limit was reached, not because the
    /// learner finished or pressed Finish. Read by the report.</summary>
    public bool TimedOut { get; private set; }

    // True while THIS manager is holding the player still, so it can give the lock back.
    private bool inputLocked;

    private readonly List<string> log = new List<string>();

    // ==================================================================

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (!AssessmentMode.IsAssessing(ModeId))
        {
            // Campaign run. Leave the scene completely alone and shut ourselves down, so
            // nothing here can ever affect normal play.
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
        if (TaskManager.Instance != null)
            TaskManager.Instance.OnTaskCompleted -= HandleTaskCompleted;

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

        foreach (var s in stages) s.completed = false;
        Score = 0;
        Mistakes = 0;
        ElapsedSeconds = 0f;
        IsFinished = false;
        TimedOut = false;
        IsRunning = true;
        log.Clear();

        // GUIDANCE: a fresh journal for this run's mistakes and their withheld explanations.
        MistakeJournal.BeginRun(ModeId);

        if (TaskManager.Instance != null)
            TaskManager.Instance.OnTaskCompleted += HandleTaskCompleted;

        PointWaypointAt(firstWaypointTarget);

        if (returnToMenuButton != null)
        {
            returnToMenuButton.onClick.RemoveListener(ReturnToMenu);
            returnToMenuButton.onClick.AddListener(ReturnToMenu);
        }

        RefreshHud();
        Debug.Log("[COC1AssessmentManager] Assessment started.");
    }

    private void Update()
    {
        if (!IsRunning) return;

        ElapsedSeconds += Time.deltaTime;

        if (timeLimitSeconds > 0f && ElapsedSeconds >= timeLimitSeconds)
        {
            ElapsedSeconds = timeLimitSeconds;
            if (endOnTimeUp)
            {
                TimedOut = true;
                log.Add("TIME LIMIT REACHED");
                Finish();
                return;
            }
        }

        RefreshHud();
    }

    // ==================================================================
    // Stages
    // ==================================================================

    private void HandleTaskCompleted(string completedTaskId)
    {
        if (!IsRunning) return;

        // TaskManager.RestoreState() re-fires this for every already-complete task when a save
        // is loaded. An assessment is always a fresh run, but the guard costs nothing and stops
        // a stray restore from awarding points for work this player never did.
        if (SaveManager.Instance != null && SaveManager.Instance.IsRestoringSave) return;

        foreach (var stage in stages)
        {
            if (stage == null || stage.completed) continue;
            if (stage.taskId != completedTaskId) continue;

            stage.completed = true;
            Score += stage.points;
            log.Add($"{stage.displayName}: complete (+{stage.points}) at {FormatTime(ElapsedSeconds)}");

            // The stage dialog must ignore suppression - it is the assessment's own guidance,
            // not one of the tutorials we just switched off.
            if (stage.completionDialog != null && DialogManager.Instance != null)
                DialogManager.Instance.ShowDialog(stage.completionDialog, null, ignoreSuppression: true);

            PointWaypointAt(stage.nextWaypointTarget);

            RefreshHud();
            CheckAllStagesDone();
            return;
        }
    }

    private void CheckAllStagesDone()
    {
        foreach (var s in stages)
            if (s != null && !s.completed) return;

        Finish();
    }

    private void PointWaypointAt(Transform target)
    {
        if (WaypointIndicator.Instance == null) return;

        // SetStationTarget: allowed in an assessment only while GuidanceSettings > Assessment
        // Station Markers is ticked - it points at a station, never at a part.
        if (target != null) WaypointIndicator.Instance.SetStationTarget(target);
        else WaypointIndicator.Instance.ClearTarget();
    }

    // ==================================================================
    // Mistakes
    // ==================================================================

    /// <summary>
    /// Called from PCPartItem when a pickup is refused. Silently does nothing outside
    /// assessment mode, so the one line added there has no effect on campaign play.
    /// </summary>
    public static void ReportMistake(string description, Object context = null)
    {
        if (Instance == null || !Instance.IsRunning)
        {
            // GUIDANCE: another module's assessment is running in this scene - COC4's repair sites
            // reuse COC1's parts. Count it for that run (and keep it for its review) instead of
            // dropping it.
            if (GuidancePolicy.IsAssessment)
            {
                CircuitCrafter.Assessment.AssessmentRecorder.Mistake(CategoryFor(description), description);
                return;
            }

            // GUIDANCE: not being assessed, so this is a learning moment. Every mistake COC1 already
            // reports (removal order, cables still plugged in, RAM clips, CPU lever, partitions,
            // Rufus, drivers) now gets a short "what went wrong + how to fix it" toast in Story
            // Mode, with the fix looked up in the ReviewLibrary. Silent in an assessment.
            CoachFeedback.Mistake(description, null, null, ModeId, context);
            return;
        }
        Instance.AddMistake(description, context);
    }

    /// <summary>Also public so you can wire a mistake to any UnityEvent in the Inspector.</summary>
    public void AddMistake(string description, Object context = null)
    {
        if (!IsRunning) return;

        Mistakes++;
        Score = Mathf.Max(0, Score - mistakePenalty);
        log.Add($"MISTAKE at {FormatTime(ElapsedSeconds)}: {description}");

        // GUIDANCE: kept with its explanation for the review screen (the log above is private).
        MistakeJournal.Record(CategoryFor(description), description);

        Debug.Log($"[COC1AssessmentManager] Mistake #{Mistakes}: {description}", context);
        RefreshHud();
    }

    /// <summary>A category for the review screen, read from the wording the COC1 call sites already
    /// use. The legacy manager never had categories; this recovers them without touching any caller.</summary>
    private static string CategoryFor(string description)
    {
        string d = (description ?? "").ToLowerInvariant();
        if (d.Contains("out of order")) return "sequence";
        if (d.Contains("plugged in") || d.Contains("still closed") || d.Contains("tried to install") ||
            d.Contains("tried to remove") || d.Contains("tried to move")) return "procedure";
        if (d.Contains("partition") || d.Contains("os setup") || d.Contains("rufus") ||
            d.Contains("bootable usb")) return "configuration";
        return "general";
    }

    // ==================================================================
    // Finish
    // ==================================================================

    /// <summary>Wire a "Finish" button here if you want the learner to be able to end early.</summary>
    public void Finish()
    {
        if (!IsRunning) return;

        IsRunning = false;
        IsFinished = true;

        // The run is over. Hold the player still and give them the mouse, so the report can be
        // read and Back to Menu clicked - and nothing they do from here can change the result.
        LockInput();

        bool perfect = Mistakes == 0;
        if (perfect && perfectRunBonus > 0)
        {
            Score += perfectRunBonus;
            log.Add($"Perfect run bonus: +{perfectRunBonus}");
        }

        PointWaypointAt(null);
        DialogManager.SuppressTutorials = false;

        if (reportPanel != null) reportPanel.SetActive(true);
        if (reportText != null) reportText.text = BuildReport();

        Debug.Log($"[COC1AssessmentManager] Finished. Score {Score}, mistakes {Mistakes}, " +
                  $"time {FormatTime(ElapsedSeconds)}.");
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

    private string BuildReport()
    {
        int completed = 0;
        foreach (var s in stages) if (s != null && s.completed) completed++;

        var sb = new StringBuilder();
        sb.AppendLine("=== COC 1 Assessment Report ===");
        sb.AppendLine();
        if (TimedOut)
        {
            sb.AppendLine("*** TIME'S UP ***");
            sb.AppendLine($"The assessment ended because the time limit of {FormatTime(timeLimitSeconds)} was reached.");
            sb.AppendLine("Stages that were not finished by then are marked incomplete.");
            sb.AppendLine();
        }
        sb.AppendLine($"Time taken:      {FormatTime(ElapsedSeconds)}");
        sb.AppendLine($"Final score:     {Score}");
        sb.AppendLine($"Mistakes:        {Mistakes}");
        sb.AppendLine($"Stages complete: {completed} of {stages.Count}");
        sb.AppendLine();
        sb.AppendLine("Log:");

        foreach (var line in log) sb.AppendLine("  " + line);

        return sb.ToString();
    }

    /// <summary>Wire the report panel's "Back to Menu" button here.</summary>
    public void ReturnToMenu()
    {
        // Tearing the scene down: zero the counter rather than release one, because a station the
        // learner was standing in may hold a lock of its own.
        inputLocked = false;
        PlayerInputLock.ReleaseAll();

        AssessmentMode.End();
        DialogManager.SuppressTutorials = false;

        if (!string.IsNullOrEmpty(mainMenuSceneName))
            UnityEngine.SceneManagement.SceneManager.LoadScene(mainMenuSceneName);
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

        if (scoreText != null) scoreText.text = $"{Score}";
        if (mistakesText != null) mistakesText.text = $"{Mistakes}";
    }

    private static string FormatTime(float seconds)
    {
        int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
        return $"{total / 60:00}:{total % 60:00}";
    }
}