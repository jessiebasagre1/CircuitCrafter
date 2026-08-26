// COC2AssessmentManager.cs
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Runs COC 2 as a graded assessment: timer, score, mistakes, and a report at the end.
    /// Same shape as COC1AssessmentManager, so what you learned wiring that one carries over.
    ///
    /// ATTACH THIS TO: any always-active object in the COC 2 scene - the Managers object.
    ///
    /// IT DOES NOTHING UNLESS ASSESSMENT MODE IS ON.
    /// Everything is behind AssessmentMode.IsAssessing("COC2"). Entering COC 2 from the campaign
    /// menu leaves the scene exactly as it is - same tutorials, same retry loop, same saving.
    /// One scene, two modes, no duplicate to keep in sync.
    ///
    /// HOW COC 2 DIFFERS FROM COC 1
    /// COC 1 is a sequence of stations, so its stages come straight off TaskManager. COC 2 is one
    /// procedure done twice, and the thing worth grading is not "did they finish" - the retry loop
    /// means everyone eventually finishes - it is HOW MANY CABLES THEY RUINED getting there.
    ///
    /// So the headline mistake here is a failed tester result, and it is recorded with the
    /// tester's own diagnosis: which pins were wrong, or which fault was found. That is read from
    /// CableTestAnalyzer at the moment of failure, not guessed, so the report always agrees with
    /// what the LEDs did.
    ///
    /// A trainee who builds both cables right first time scores full marks. One who needs four
    /// attempts finishes with the same tasks complete and a report that says exactly why.
    /// </summary>
    public class COC2AssessmentManager : MonoBehaviour
    {
        public static COC2AssessmentManager Instance { get; private set; }

        private const string ModeId = "COC2";

        [System.Serializable]
        public class Stage
        {
            [Tooltip("A TaskManager taskId that ends this stage, e.g. \"crimp_cable\". Leave blank " +
                     "for a stage you complete yourself by calling CompleteStage(\"<id>\").")]
            public string taskId;

            [Tooltip("Used by CompleteStage() when this stage has no taskId. Any short string.")]
            public string manualId;

            [Tooltip("Shown in the report, e.g. \"Crimp both ends\".")]
            public string displayName = "Stage";

            [Tooltip("Played when this stage completes.")]
            public DialogSequence completionDialog;

            [Tooltip("Where the waypoint arrow points AFTER this stage completes.")]
            public Transform nextWaypointTarget;

            [Tooltip("Points awarded for completing this stage.")]
            public int points = 50;

            [HideInInspector] public bool completed;
        }

        // ------------------------------------------------------------------

        [Header("Stages")]
        [Tooltip("In order. A sensible COC 2 set is: crimping tool collected, crimp_cable, " +
                 "test_cable. With COC2BothCablesGate installed, test_cable only completes once " +
                 "BOTH cable types have passed - so that one stage covers the whole exercise.")]
        public List<Stage> stages = new List<Stage>();

        [Tooltip("Where the waypoint points when the assessment starts - normally the crimping tool.")]
        public Transform firstWaypointTarget;

        [Header("HUD")]
        public TMP_Text timerText;
        public TMP_Text scoreText;
        public TMP_Text mistakesText;

        [Tooltip("Switched on for the run, off otherwise.")]
        public GameObject assessmentHud;

        [Header("Scoring")]
        [Tooltip("Deducted for a general mistake.")]
        public int mistakePenalty = 10;

        [Tooltip("Deducted for a cable that fails the tester. Heavier than a general mistake on " +
                 "purpose - in the field that is a wasted connector and a wasted length of cable.")]
        public int failedCablePenalty = 25;

        [Tooltip("Awarded if the run finishes with no mistakes at all.")]
        public int perfectRunBonus = 50;

        [Header("Timer")]
        [Tooltip("0 = no limit. TESDA allows a set time per competency; put it here in seconds.")]
        public float timeLimitSeconds = 0f;

        public bool endOnTimeUp = true;

        [Header("Scene changes for assessment")]
        [Tooltip("Switched OFF for the run. Put your quiz manager and autosaver here.")]
        public GameObject[] disableInAssessment;

        [Tooltip("Switched ON for the run.")]
        public GameObject[] enableInAssessment;

        [Tooltip("Silence every tutorial dialog. COC2StageDialogs' stage briefings go quiet with " +
                 "them - which is the point of an assessment. The pass/fail result dialogs still " +
                 "play, because those report the outcome rather than teach.")]
        public bool suppressTutorialDialogs = true;

        [Header("Report")]
        public GameObject reportPanel;
        public TMP_Text reportText;

        [Tooltip("Optional - wire its OnClick to ReturnToMenu().")]
        public Button returnToMenuButton;

        public string mainMenuSceneName = "MainMenu";

        // ------------------------------------------------------------------

        public int Score { get; private set; }
        public int Mistakes { get; private set; }
        public int FailedCables { get; private set; }
        public float ElapsedSeconds { get; private set; }
        public bool IsRunning { get; private set; }
        public bool IsFinished { get; private set; }

        private readonly List<string> log = new List<string>();
        private bool reportPending;

        // ==================================================================

        private void Awake()
        {
            Instance = this;
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
            if (TaskManager.Instance != null)
                TaskManager.Instance.OnTaskCompleted -= HandleTaskCompleted;

            // Never leave the flag set for whatever scene loads next.
            DialogManager.SuppressTutorials = false;

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
            FailedCables = 0;
            ElapsedSeconds = 0f;
            IsFinished = false;
            IsRunning = true;
            log.Clear();

            if (TaskManager.Instance != null)
                TaskManager.Instance.OnTaskCompleted += HandleTaskCompleted;

            PointWaypointAt(firstWaypointTarget);

            if (returnToMenuButton != null)
            {
                returnToMenuButton.onClick.RemoveListener(ReturnToMenu);
                returnToMenuButton.onClick.AddListener(ReturnToMenu);
            }

            RefreshHud();
            Debug.Log("[COC2AssessmentManager] Assessment started.");
        }

        private void Update()
        {
            // BEFORE the IsRunning guard, deliberately. Finish() has already set IsRunning false
            // by the time a report can be pending, so a check after the guard would never run and
            // the report would never appear.
            if (reportPending && !DialogIsOpen())
                ShowReport();

            if (!IsRunning) return;

            ElapsedSeconds += Time.deltaTime;

            if (timeLimitSeconds > 0f && ElapsedSeconds >= timeLimitSeconds)
            {
                ElapsedSeconds = timeLimitSeconds;
                if (endOnTimeUp)
                {
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
            // is loaded. An assessment is always a fresh run, but the guard stops a stray restore
            // from awarding points for work this player never did.
            if (SaveManager.Instance != null && SaveManager.Instance.IsRestoringSave) return;

            foreach (var stage in stages)
            {
                if (stage == null || stage.completed) continue;
                if (string.IsNullOrEmpty(stage.taskId) || stage.taskId != completedTaskId) continue;

                Complete(stage);
                return;
            }
        }

        /// <summary>
        /// Completes a stage that has no TaskManager task behind it. Wire this to any UnityEvent -
        /// CrimpingToolPickup, CableTypeSelectionUI, whatever marks the moment - passing the
        /// stage's Manual Id.
        /// </summary>
        public void CompleteStage(string manualId)
        {
            if (!IsRunning || string.IsNullOrEmpty(manualId)) return;

            foreach (var stage in stages)
            {
                if (stage == null || stage.completed) continue;
                if (stage.manualId != manualId) continue;

                Complete(stage);
                return;
            }
        }

        private void Complete(Stage stage)
        {
            stage.completed = true;
            Score += stage.points;
            log.Add($"{stage.displayName}: complete (+{stage.points}) at {FormatTime(ElapsedSeconds)}");

            // Waypoint and HUD first, then the dialog. If this is the last stage its callback ends
            // the assessment, and Finish() clears the waypoint - doing it the other way round
            // re-points the arrow at something already finished.
            PointWaypointAt(stage.nextWaypointTarget);
            RefreshHud();

            // CheckAllStagesDone is handed over as the dialog's completion callback rather than
            // called on the next line. On the final stage those orderings diverge badly: called
            // directly, the report panel switches on top of the dialog the learner is still
            // reading. DialogManager clears IsDialogOpen before invoking the callback, so there is
            // no frame of overlap.
            if (stage.completionDialog != null && DialogManager.Instance != null)
                DialogManager.Instance.ShowDialog(stage.completionDialog, CheckAllStagesDone,
                                                  ignoreSuppression: true);
            else
                CheckAllStagesDone();
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

            if (target != null) WaypointIndicator.Instance.SetTarget(target);
            else WaypointIndicator.Instance.ClearTarget();
        }

        // ==================================================================
        // Mistakes
        // ==================================================================

        /// <summary>
        /// Wire this to COC2StageDialogs > On Test Failed ().
        ///
        /// The diagnosis is read HERE rather than stored earlier, because the crimped pinout
        /// snapshots are still intact at this moment - COC2StageDialogs shows its fail dialog
        /// first and only resets once the learner closes it. Read them after the reset and they
        /// are already null.
        /// </summary>
        public void NotifyTestFailed()
        {
            if (!IsRunning) return;

            FailedCables++;

            string reason = DiagnoseFailure();
            string description = string.IsNullOrEmpty(reason)
                ? "Cable failed the tester."
                : "Cable failed the tester - " + reason;

            Mistakes++;
            Score = Mathf.Max(0, Score - failedCablePenalty);
            log.Add($"FAILED CABLE #{FailedCables} at {FormatTime(ElapsedSeconds)}: {description}");

            Debug.Log($"[COC2AssessmentManager] {description}", this);
            RefreshHud();
        }

        /// <summary>Optional. Wire to COC2StageDialogs > On Test Passed () for a log entry.</summary>
        public void NotifyTestPassed()
        {
            if (!IsRunning) return;

            var station = CrimpingStationController.Instance;
            string cable = station != null && station.targetCableType != null
                ? CableName(station.targetCableType)
                : "cable";

            log.Add($"{cable} passed the tester at {FormatTime(ElapsedSeconds)}");
        }

        /// <summary>Called from anywhere. Silently does nothing outside assessment mode, so a
        /// call left in a gameplay script has no effect on campaign play.</summary>
        public static void ReportMistake(string description, Object context = null)
        {
            if (Instance == null || !Instance.IsRunning) return;
            Instance.AddMistake(description, context);
        }

        /// <summary>Public so you can wire a mistake to any UnityEvent in the Inspector.</summary>
        public void AddMistake(string description, Object context = null)
        {
            if (!IsRunning) return;

            Mistakes++;
            Score = Mathf.Max(0, Score - mistakePenalty);
            log.Add($"MISTAKE at {FormatTime(ElapsedSeconds)}: {description}");

            Debug.Log($"[COC2AssessmentManager] Mistake #{Mistakes}: {description}", context);
            RefreshHud();
        }

        /// <summary>
        /// The tester's own verdict, in words. Continuity faults outrank colour-order mistakes -
        /// fix the open before worrying about which standard it is.
        /// </summary>
        private string DiagnoseFailure()
        {
            var station = CrimpingStationController.Instance;
            if (station == null) return null;

            WireColor[] endOne = station.GetCrimpedArrangement(1);
            WireColor[] endTwo = station.GetCrimpedArrangement(2);
            if (endOne == null || endTwo == null) return "both ends were not crimped";

            CableTestReport report = CableTestAnalyzer.Analyze(endOne, endTwo, station.targetCableType);
            return CableTestAnalyzer.FailureHint(report);
        }

        private static string CableName(CableTypeSO type)
        {
            if (type == null) return "cable";
            return string.IsNullOrEmpty(type.displayName) ? type.name : type.displayName;
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

            bool perfect = Mistakes == 0;
            if (perfect && perfectRunBonus > 0)
            {
                Score += perfectRunBonus;
                log.Add($"Perfect run bonus: +{perfectRunBonus}");
            }

            PointWaypointAt(null);
            DialogManager.SuppressTutorials = false;

            Debug.Log($"[COC2AssessmentManager] Finished. Score {Score}, mistakes {Mistakes}, " +
                      $"cables failed {FailedCables}, time {FormatTime(ElapsedSeconds)}.");

            // The run is over the moment this is entered - score, bonus and timer are final. Only
            // the REPORT waits, so it never lands on top of a dialog still being read.
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

            if (reportPanel == null && reportText == null)
                Debug.LogWarning("[COC2AssessmentManager] Finished, but neither Report Panel nor " +
                                 "Report Text is assigned - the learner sees nothing. Wire them on " +
                                 "this component.\n\n" + BuildReport(), this);
        }

        private string BuildReport()
        {
            int completed = 0;
            foreach (var s in stages) if (s != null && s.completed) completed++;

            var sb = new StringBuilder();
            sb.AppendLine("=== COC 2 Assessment Report ===");
            sb.AppendLine();
            sb.AppendLine($"Time taken:      {FormatTime(ElapsedSeconds)}");
            sb.AppendLine($"Final score:     {Score}");
            sb.AppendLine($"Mistakes:        {Mistakes}");
            sb.AppendLine($"Cables failed:   {FailedCables}");

            // Only meaningful when the both-cables gate is installed; skipped silently otherwise
            // so this works either way.
            if (COC2BothCablesGate.Instance != null)
                sb.AppendLine($"Cable types built: {COC2BothCablesGate.Instance.PassedCount}");

            sb.AppendLine($"Stages complete: {completed} of {stages.Count}");
            sb.AppendLine();
            sb.AppendLine("Log:");

            foreach (var line in log) sb.AppendLine("  " + line);

            return sb.ToString();
        }

        /// <summary>Wire the report panel's "Back to Menu" button here.</summary>
        public void ReturnToMenu()
        {
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

            if (scoreText != null) scoreText.text = $"Score: {Score}";
            if (mistakesText != null) mistakesText.text = $"Mistakes: {Mistakes}";
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }
    }
}
