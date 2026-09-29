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

        [Tooltip("Deducted for a patch panel block that does not pass T568A. As heavy as a ruined " +
                 "cable, and for the same reason: an insulation-displacement contact is a one-shot " +
                 "connection, so a conductor punched into the wrong position costs slack the 13 mm " +
                 "untwist limit does not give back.")]
        public int terminationPenalty = 25;

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
                 "them - which is the point of an assessment.\n\n" +
                 "Safe: DialogManager still fires each dialog's completion callback when it " +
                 "silences one, so the stations' own retry and hand-off flow carries on exactly as " +
                 "it does in story mode. Nothing stalls waiting for a popup nobody saw.")]
        public bool suppressTutorialDialogs = true;

        [Tooltip("Leave OFF for a real assessment.\n\n" +
                 "This manager's own stage dialogs are shown with ignoreSuppression, so they get " +
                 "through even when Suppress Tutorial Dialogs is on - they were meant as the " +
                 "assessment's own commentary. But 'Crimping complete, now go to the patch panel' " +
                 "is guidance whoever says it, and an assessment that narrates the next step is " +
                 "not measuring whether the trainee knows it.\n\n" +
                 "Tick it only if you want a run that talks the learner through the module while " +
                 "still scoring it - a practice-with-feedback run rather than a graded one.")]
        public bool showStageCompletionDialogs = false;

        [Header("Report")]
        public GameObject reportPanel;
        public TMP_Text reportText;

        [Tooltip("Optional - wire its OnClick to ReturnToMenu().")]
        public Button returnToMenuButton;

        public string mainMenuSceneName = "MainMenu";

        // ------------------------------------------------------------------

        public int Score { get; private set; }

        /// <summary>
        /// Everything that was available this run: every stage's points, plus the perfect-run bonus.
        ///
        /// A bare score is not a result. "180" means nothing to a learner or an assessor without
        /// the number it was out of, and the same 180 is a pass on one stage list and a failure on
        /// another. The perfect-run bonus is included because it IS achievable - leaving it out is
        /// what made COC1's bridge once report "410 of 360".
        /// </summary>
        public int MaxScore
        {
            get
            {
                int total = 0;
                foreach (var s in stages)
                    if (s != null) total += Mathf.Max(0, s.points);
                return total + Mathf.Max(0, perfectRunBonus);
            }
        }

        /// <summary>Score as a percentage of MaxScore, 0-100. Zero when nothing is on offer.</summary>
        public int ScorePercent
        {
            get
            {
                int max = MaxScore;
                return max <= 0 ? 0 : Mathf.Clamp(Mathf.RoundToInt(100f * Score / max), 0, 100);
            }
        }

        public int Mistakes { get; private set; }
        public int FailedCables { get; private set; }
        public float ElapsedSeconds { get; private set; }
        public bool IsRunning { get; private set; }
        public bool IsFinished { get; private set; }

        /// <summary>True when the run ended because the time limit was reached, not because the
        /// learner finished or pressed Finish. Read by the report.</summary>
        public bool TimedOut { get; private set; }

        // True while THIS manager is holding the player still, so it can give the lock back.
        private bool inputLocked;

        private readonly List<string> log = new List<string>();
        private bool reportPending;

        // The cable most recently counted as failed - its crimped pinout snapshot, which is a new array
        // for every cable crimped. Lets NotifyTestFailed be reached from more than one place (the
        // tester's own event, via COC2AssessmentHooks, and an old Inspector wire from COC2StageDialogs)
        // without one failure being counted twice.
        private object lastFailedCable;
        private int lastFailedFrame = -1;

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
            FailedCables = 0;
            ElapsedSeconds = 0f;
            IsFinished = false;
            TimedOut = false;
            IsRunning = true;
            log.Clear();

            lastFailedCable = null;
            lastFailedFrame = -1;
            EnsureHooks();

            if (TaskManager.Instance != null)
                TaskManager.Instance.OnTaskCompleted += HandleTaskCompleted;

            PointWaypointAt(firstWaypointTarget);

            if (returnToMenuButton != null)
            {
                returnToMenuButton.onClick.RemoveListener(ReturnToMenu);
                returnToMenuButton.onClick.AddListener(ReturnToMenu);
            }

            ValidateStages();

            RefreshHud();
            Debug.Log($"[COC2AssessmentManager] Assessment started. {stages.Count} stage(s), " +
                      $"{MaxScore} points available.");
        }

        /// <summary>
        /// The six task ids COC 2 actually reports. Kept here so the checks below and the
        /// fill-in helper cannot drift apart from each other.
        /// </summary>
        private static readonly string[] ModuleTaskIds =
        {
            "crimp_cable", "test_cable", "punch_patch_panel",
            "patch_to_switch", "configure_network", "verify_connectivity",
        };

        private static readonly string[] ModuleStageNames =
        {
            "Crimp both cables", "Test both cables", "Terminate the patch panel",
            "Patch the panel to the switch", "Configure the server and client", "Verify connectivity",
        };

        /// <summary>
        /// Says so when the stage list cannot grade what it claims to.
        ///
        /// A stage list is the entire definition of what this assessment measures, and every way of
        /// getting it wrong is silent at runtime: a missing stage simply never appears in the
        /// report, and a mistyped id means the assessment can never finish because a stage it is
        /// waiting on can never complete.
        /// </summary>
        private void ValidateStages()
        {
            var sb = new StringBuilder();

            if (stages == null || stages.Count == 0)
            {
                Debug.LogWarning("[COC2AssessmentManager] The Stages list is EMPTY. Nothing can be " +
                                 "scored and the assessment can never finish. Right-click this " +
                                 "component > 'Fill in the standard COC2 stages'.", this);
                return;
            }

            foreach (var s in stages)
            {
                if (s == null) continue;

                if (string.IsNullOrWhiteSpace(s.taskId) && string.IsNullOrWhiteSpace(s.manualId))
                    sb.AppendLine($"  - Stage '{s.displayName}' has neither a Task Id nor a Manual " +
                                  "Id, so nothing can ever complete it. The run will never end.");

                if (!string.IsNullOrEmpty(s.taskId) && s.taskId != s.taskId.Trim())
                    sb.AppendLine($"  - Stage '{s.displayName}' has whitespace around its Task Id " +
                                  $"('{s.taskId}'). It is matched trimmed so the run still works - " +
                                  "but tidy the field.");

                if (s.points <= 0)
                    sb.AppendLine($"  - Stage '{s.displayName}' is worth {s.points} points, so " +
                                  "completing it changes nothing.");
            }

            // Which of the module's real tasks are not being graded at all.
            string missing = null;
            foreach (string id in ModuleTaskIds)
            {
                bool covered = false;
                foreach (var s in stages)
                {
                    if (s == null || string.IsNullOrWhiteSpace(s.taskId)) continue;
                    if (string.Equals(s.taskId.Trim(), id, System.StringComparison.OrdinalIgnoreCase))
                    {
                        covered = true;
                        break;
                    }
                }
                if (!covered) missing = missing == null ? id : missing + ", " + id;
            }

            if (missing != null)
                sb.AppendLine($"  - Not graded at all: {missing}. Work the learner does there earns " +
                              "nothing and is absent from the report. Deliberate for a cabling-only " +
                              "assessment; a gap otherwise.");

            if (sb.Length > 0)
                Debug.LogWarning("[COC2AssessmentManager] Stage list:\n" + sb, this);
        }

        /// <summary>
        /// Fills the Stages list with the six the module actually reports, in order.
        ///
        /// Typing six ids by hand into an Inspector list is exactly where a trailing space or a
        /// 'patch_to_switch ' comes from, and neither shows up until an assessment refuses to end.
        /// Use it in EDIT MODE - a list built during Play is discarded on Stop.
        /// </summary>
        [ContextMenu("Fill in the standard COC2 stages")]
        private void FillStandardStages()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("[COC2AssessmentManager] Stop the game first - a list built during " +
                                 "Play is thrown away when you press Stop.", this);
                return;
            }

            if (stages == null) stages = new List<Stage>();
            stages.Clear();

            for (int i = 0; i < ModuleTaskIds.Length; i++)
            {
                stages.Add(new Stage
                {
                    taskId = ModuleTaskIds[i],
                    displayName = ModuleStageNames[i],
                    points = 50,
                });
            }

            Debug.Log($"[COC2AssessmentManager] Filled in {stages.Count} stages, 50 points each - " +
                      $"{MaxScore} available including the perfect-run bonus. Adjust the points per " +
                      "stage if some are worth more, then save the scene.", this);

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
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
            // is loaded. An assessment is always a fresh run, but the guard stops a stray restore
            // from awarding points for work this player never did.
            if (SaveManager.Instance != null && SaveManager.Instance.IsRestoringSave) return;

            string done = completedTaskId.Trim();

            foreach (var stage in stages)
            {
                if (stage == null || stage.completed) continue;
                if (string.IsNullOrWhiteSpace(stage.taskId)) continue;

                // Trimmed and case-insensitive. A space typed into the Inspector is invisible, and
                // an exact comparison then rejects the very task the field was typed for - the
                // stage never completes, the assessment never ends, and nothing anywhere says why.
                // This scene has already shipped one 'crimp_cable ' to prove the point.
                if (!string.Equals(stage.taskId.Trim(), done, System.StringComparison.OrdinalIgnoreCase))
                    continue;

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
            if (showStageCompletionDialogs && stage.completionDialog != null && DialogManager.Instance != null)
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

            // SetStationTarget: allowed in an assessment - it points at a station, never at a wire
            // or a pin.
            if (target != null) WaypointIndicator.Instance.SetStationTarget(target);
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

            // One failed cable, one mistake - however many routes the news arrives by.
            var failedStation = CrimpingStationController.Instance;
            object cable = failedStation != null ? failedStation.GetCrimpedArrangement(1) : null;
            if (cable != null ? ReferenceEquals(cable, lastFailedCable) : Time.frameCount == lastFailedFrame) return;
            lastFailedCable = cable;
            lastFailedFrame = Time.frameCount;

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

        /// <summary>
        /// COC2AssessmentHooks is what feeds every station's mistakes in here - the cable tester, the
        /// punch-down, the patch cords, the device configuration, the ping and the final inspection.
        /// Without it on the scene, only a hand-wired failure would ever be counted. So if the scene
        /// does not have one, the manager adds it to itself.
        /// </summary>
        private void EnsureHooks()
        {
            if (FindAnyObjectByType<COC2AssessmentHooks>() == null)
            {
                gameObject.AddComponent<COC2AssessmentHooks>();
                Debug.Log("[COC2AssessmentManager] No COC2AssessmentHooks in the scene - added one, so every " +
                          "station's mistakes are recorded.", this);
            }

            // Same for the warnings and results the learner still sees in this assessment.
            if (FindAnyObjectByType<COC2AssessmentDialogs>() == null)
            {
                gameObject.AddComponent<COC2AssessmentDialogs>();
                Debug.Log("[COC2AssessmentManager] No COC2AssessmentDialogs in the scene - added one with its " +
                          "defaults (warnings and results shown, failure details off).", this);
            }
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
            if (Instance == null || !Instance.IsRunning)
            {
                // Not being assessed - nothing to record.
                return;
            }
            Instance.AddMistake(description, context);
        }

        /// <summary>
        /// A patch panel block that did not pass T568A. Called by COC2AssessmentHooks.
        ///
        /// Separate from AddMistake only so it can carry its own weight - see terminationPenalty.
        /// </summary>
        public void ReportTerminationFailure(string reason, Object context = null)
        {
            if (!IsRunning) return;

            Mistakes++;
            Score = Mathf.Max(0, Score - terminationPenalty);

            string description = string.IsNullOrWhiteSpace(reason)
                ? "Patch panel termination failed."
                : "Patch panel termination failed - " + reason.Trim();

            log.Add($"MISTAKE at {FormatTime(ElapsedSeconds)}: {description}");
            Debug.Log($"[COC2AssessmentManager] Mistake #{Mistakes}: {description}", context);
            RefreshHud();
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
            sb.AppendLine("=== COC 2 Assessment Report ===");
            sb.AppendLine();
            if (TimedOut)
            {
                sb.AppendLine("*** TIME'S UP ***");
                sb.AppendLine($"The assessment ended because the time limit of {FormatTime(timeLimitSeconds)} was reached.");
                sb.AppendLine("Stages that were not finished by then are marked incomplete.");
                sb.AppendLine();
            }
            sb.AppendLine($"Time taken:      {FormatTime(ElapsedSeconds)}");
            sb.AppendLine($"Final score:     {Score} of {MaxScore}  ({ScorePercent}%)");
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