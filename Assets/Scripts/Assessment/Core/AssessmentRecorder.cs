using System;
using System.Collections.Generic;
using UnityEngine;

namespace CircuitCrafter.Assessment
{
    /// <summary>
    /// Watches a run and turns it into an AssessmentResult. This is the piece
    /// that lets a module start producing real records TODAY, without inheriting
    /// from anything or restructuring its managers.
    ///
    /// ATTACH THIS TO: the Managers object in any gameplay scene - COC1, COC2,
    /// COC3, COC4. One per scene, same idea as TaskCheckpointAutosaver and
    /// QuizManager.
    ///
    /// WHAT IT DOES ON ITS OWN, WITH NO OTHER WIRING
    ///   - starts a timer when the scene loads
    ///   - listens to TaskManager.OnTaskCompleted and closes any stage whose
    ///     stageId matches, exactly like the existing assessment managers do
    ///   - finishes automatically when every stage is done
    ///   - writes a result and folds it into the CompetencyLedger
    ///
    /// So for a module whose stages are all TaskManager tasks, filling in the
    /// Stages list is the entire setup.
    ///
    /// WHAT IT NEEDS TELLING ABOUT
    ///   ReportAttempt(stageId, correct)  - a graded try. This is what produces
    ///                                      first-attempt accuracy, which is the
    ///                                      figure actually worth reporting.
    ///   ReportMistake(category, text)    - anything worth counting against them.
    ///   CompleteStage(stageId)           - for stages with no TaskManager task.
    ///
    /// All three are safe to call from anywhere, including from scripts that run
    /// in scenes with no recorder - the statics no-op when nothing is listening,
    /// the same trick COC1AssessmentManager.ReportMistake already uses. That is
    /// what makes it safe to sprinkle calls into gameplay code.
    ///
    /// IT DOES NOT FIGHT THE EXISTING ASSESSMENT MANAGERS
    /// COC1AssessmentManager and COC2AssessmentManager keep their own score,
    /// timer, HUD and report and are completely unaware of this class. In those
    /// two scenes use LegacyAssessmentBridge instead, which reads their public
    /// state and produces the same AssessmentResult without either script
    /// changing. Do not put both on the same scene.
    /// </summary>
    public class AssessmentRecorder : MonoBehaviour
    {
        /// <summary>The recorder for the scene currently running, or null.</summary>
        public static AssessmentRecorder Active { get; private set; }

        [Serializable]
        public class StageSpec
        {
            [Tooltip("A TaskManager task id, e.g. \"assemble_pc\". Leave blank for a stage you close yourself with CompleteStage(\"<manual id>\").")]
            public string taskId = "";

            [Tooltip("Used by CompleteStage() when there is no taskId. Any short unique string.")]
            public string manualId = "";

            [Tooltip("Shown in the report.")]
            public string displayName = "Stage";

            [Tooltip("Points awarded when this stage completes.")]
            public int points = 50;

            /// <summary>The id this stage answers to - the task id where there is
            /// one, otherwise the manual id.</summary>
            public string Key => !string.IsNullOrEmpty(taskId) ? taskId : manualId;
        }

        [Header("Identity")]
        [Tooltip("Which module this scene is. Must match the moduleId on the matching CompetencyDefinition asset.")]
        [SerializeField] private string moduleId = "COC1";

        [Header("Stages")]
        [Tooltip("In order. A stage with a taskId closes itself when that task completes; one with only a manualId waits for CompleteStage().")]
        [SerializeField] private List<StageSpec> stages = new List<StageSpec>();

        [Header("Scoring")]
        [Tooltip("Deducted per mistake. The score never goes below zero, matching the existing assessment managers.")]
        [SerializeField] private int mistakePenalty = 10;

        [Tooltip("Awarded if the run finishes with no mistakes at all.")]
        [SerializeField] private int perfectRunBonus = 50;

        [Tooltip("Also add the final score to the global ScoreManager, so the running campaign total still moves. Turn this off for a module that already awards its own score, or the points get counted twice.")]
        [SerializeField] private bool contributeToGlobalScore = true;

        [Header("Timing")]
        [Tooltip("Seconds allowed in Assessment mode. 0 = no limit. Ignored in Guided and Practice, which are never timed.")]
        [SerializeField] private float assessmentTimeLimitSeconds = 0f;

        [Tooltip("Finish the run automatically when the time limit is reached.")]
        [SerializeField] private bool endOnTimeUp = true;

        [Header("Behaviour")]
        [Tooltip("Start the timer as soon as the scene loads. Turn off if the run should start on a button instead, then call Begin() yourself.")]
        [SerializeField] private bool beginOnStart = true;

        [Tooltip("Finish automatically once every stage is complete. Turn off if the module has its own end-of-run flow and will call Finish().")]
        [SerializeField] private bool finishWhenAllStagesComplete = true;

        [Tooltip("Record Guided (campaign) runs too. Recommended: a guided run can never award Competent, but it is still useful evidence that the learner has been through the material.")]
        [SerializeField] private bool recordGuidedRuns = true;

        // ------------------------------------------------------------------

        public bool IsRunning { get; private set; }
        public bool IsFinished { get; private set; }
        public float ElapsedSeconds { get; private set; }
        public int Score { get; private set; }
        public int MistakeCount { get; private set; }

        /// <summary>The module this recorder is running, e.g. "COC3". Read-only - the field is
        /// serialized and belongs to the Inspector; this just lets a HUD ask which module it is
        /// sitting in without being told twice.</summary>
        public string ModuleId => moduleId;

        /// <summary>Fires as each stage closes, with its stage id.
        ///
        /// WHY IT EXISTS. OnRunFinished reports the whole run at the end, which is right for
        /// saving a result and useless for anything watching progress as it happens - a
        /// checklist on screen needs to know at the moment a stage closes, not six minutes
        /// later. COC 3 and COC 4 close most of their stages through CompleteStage() rather
        /// than through TaskManager, so TaskManager's own event never fires for them and this
        /// is the only way to hear about it.</summary>
        public event Action<string> OnStageCompleted;

        /// <summary>True if that stage has closed in the current run. Unknown ids answer false,
        /// which is deliberate: a HUD row naming a stage this recorder has never heard of should
        /// sit there unticked rather than throw.</summary>
        public bool IsStageComplete(string stageId)
        {
            if (string.IsNullOrEmpty(stageId)) return false;

            StageOutcome outcome = FindOutcome(stageId.Trim());
            return outcome != null && outcome.completed;
        }

        /// <summary>The stage ids and names this recorder is set up to grade, in order. For the
        /// task-list builder, so a checklist does not have to have them typed in again.</summary>
        public List<StageSpec> StageSpecs => stages;

        /// <summary>Fires once, with the completed record, after it has been saved
        /// and folded into the ledger. A report screen should subscribe to this
        /// rather than polling IsFinished.</summary>
        public event Action<AssessmentResult> OnRunFinished;

        private readonly List<StageOutcome> outcomes = new List<StageOutcome>();
        private readonly List<MistakeEntry> mistakes = new List<MistakeEntry>();
        private DateTime startedUtc;
        private bool subscribed;

        // ==================================================================

        private void Awake()
        {
            Active = this;
        }

        private void Start()
        {
            if (beginOnStart) Begin();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (Active == this) Active = null;
        }

        private void Update()
        {
            if (!IsRunning) return;

            ElapsedSeconds += Time.deltaTime;

            // Only Assessment is timed. A practice run that ends because a clock
            // ran out isn't practice, and a guided run is a tutorial.
            if (TrainingMode.IsGraded && assessmentTimeLimitSeconds > 0f &&
                ElapsedSeconds >= assessmentTimeLimitSeconds)
            {
                ElapsedSeconds = assessmentTimeLimitSeconds;
                if (endOnTimeUp)
                {
                    ReportMistake("time", "Time limit reached before the run was finished.");
                    Finish(completed: false);
                }
            }
        }

        // ==================================================================
        // Lifecycle
        // ==================================================================

        public void Begin()
        {
            if (IsRunning) return;

            if (!recordGuidedRuns && TrainingMode.Current == SessionMode.Guided)
            {
                enabled = false;
                return;
            }

            outcomes.Clear();
            mistakes.Clear();

            foreach (var stage in stages)
            {
                if (stage == null || string.IsNullOrEmpty(stage.Key)) continue;

                outcomes.Add(new StageOutcome
                {
                    stageId = stage.Key,
                    displayName = stage.displayName,
                    completed = false,
                    points = 0,
                    completedAtSeconds = -1f
                });
            }

            Score = 0;
            MistakeCount = 0;
            ElapsedSeconds = 0f;
            IsFinished = false;
            IsRunning = true;
            startedUtc = DateTime.UtcNow;

            Subscribe();

            if (ProfileManager.Instance != null) ProfileManager.Instance.TouchActiveProfile();

            Debug.Log($"[AssessmentRecorder] {moduleId} run started in {TrainingMode.Current} mode with {outcomes.Count} stage(s).");
        }

        private void Subscribe()
        {
            if (subscribed || TaskManager.Instance == null) return;
            TaskManager.Instance.OnTaskCompleted += HandleTaskCompleted;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || TaskManager.Instance == null) return;
            TaskManager.Instance.OnTaskCompleted -= HandleTaskCompleted;
            subscribed = false;
        }

        private void HandleTaskCompleted(string completedTaskId)
        {
            if (!IsRunning) return;

            // TaskManager.RestoreState() re-fires this for every already-complete
            // task when a save loads. Those are not things this run achieved, and
            // crediting them would hand a learner points and a timestamp for work
            // done in a previous session. Same guard QuizManager and both
            // assessment managers already use.
            if (SaveManager.Instance != null && SaveManager.Instance.IsRestoringSave) return;

            CloseStage(completedTaskId);
        }

        // ==================================================================
        // Reporting - call these from gameplay
        // ==================================================================

        /// <summary>Closes a stage that has no TaskManager task behind it. Wire it
        /// to any UnityEvent, passing the stage's Manual Id.</summary>
        public void CompleteStage(string stageId) => CloseStage(stageId);

        private void CloseStage(string stageId)
        {
            if (!IsRunning || string.IsNullOrEmpty(stageId)) return;

            StageSpec spec = FindSpec(stageId);
            StageOutcome outcome = FindOutcome(stageId);
            if (spec == null || outcome == null || outcome.completed) return;

            outcome.completed = true;
            outcome.points = spec.points;
            outcome.completedAtSeconds = ElapsedSeconds;

            // A stage nobody ever reported an attempt against was completed without
            // a graded try - so by definition it wasn't got wrong first time.
            if (outcome.attempts == 0)
            {
                outcome.attempts = 1;
                outcome.firstAttemptCorrect = true;
            }

            Score += spec.points;

            Debug.Log($"[AssessmentRecorder] Stage '{stageId}' complete (+{spec.points}) at {AssessmentResult.FormatTime(ElapsedSeconds)}.");

            // Raised BEFORE the finish check. Finish() can end the run and switch the scene's UI
            // over to the report in the same call, and a listener told about the last stage only
            // after that has happened gets to tick a checklist nobody can see any more.
            OnStageCompleted?.Invoke(stageId);

            if (finishWhenAllStagesComplete && AllStagesComplete()) Finish();
        }

        /// <summary>
        /// Records one graded try at a stage. Call it on every submit, whether
        /// right or wrong - the first call for a stage is what sets its
        /// first-attempt flag, which is the number worth graphing.
        ///
        /// A wrong attempt does NOT automatically count as a mistake; call
        /// ReportMistake as well if it should. Keeping them separate lets a module
        /// count a wrong answer on a quiz differently from a ruined cable or an
        /// ungrounded component.
        /// </summary>
        public void ReportAttempt(string stageId, bool correct)
        {
            if (!IsRunning) return;

            StageOutcome outcome = FindOutcome(stageId);
            if (outcome == null) return;

            bool isFirst = outcome.attempts == 0;
            outcome.attempts++;

            if (isFirst) outcome.firstAttemptCorrect = correct;
        }

        /// <summary>Records a mistake with a category, and applies the penalty.
        /// Keep the category set small - "sequence", "identification", "safety",
        /// "configuration", "cable" - so an instructor can count patterns rather
        /// than read prose.</summary>
        public void ReportMistake(string category, string description)
        {
            ReportMistake(category, description, null);
        }

        /// <summary>As above, with the correct procedure attached. The correction is never shown
        /// during an assessment - it is saved with the result for the review screen.</summary>
        public void ReportMistake(string category, string description, string correction)
        {
            if (!IsRunning) return;

            MistakeCount++;
            Score = Mathf.Max(0, Score - mistakePenalty);

            mistakes.Add(new MistakeEntry
            {
                atSeconds = ElapsedSeconds,
                category = string.IsNullOrEmpty(category) ? "general" : category,
                description = description,
                correction = correction ?? ""
            });

            Debug.Log($"[AssessmentRecorder] Mistake #{MistakeCount} ({category}) at {AssessmentResult.FormatTime(ElapsedSeconds)}: {description}", this);
        }

        /// <summary>
        /// Static conveniences, so a gameplay script can report without holding a
        /// reference and without caring whether a recorder exists in this scene.
        /// Same pattern as COC1AssessmentManager.ReportMistake - a call left in
        /// gameplay code costs nothing in a scene with no recorder.
        /// </summary>
        public static void Mistake(string category, string description, string correction = null)
        {
            if (Active != null && Active.IsRunning) Active.ReportMistake(category, description, correction);

            // COC4's assessment spans six scenes and has no recorder of its own, so
            // COC4AssessmentRun.ReportMistake() was never called and in-site mistakes (wrong
            // diagnosis, repairing before diagnosing, a failed verification) were not counted.
            // They are now - which is what the run's Mistake Penalty was always meant to apply to.
            if (CircuitCrafter.Repair.COC4AssessmentRun.IsActive) CircuitCrafter.Repair.COC4AssessmentRun.ReportMistake();
        }

        public static void Attempt(string stageId, bool correct)
        {
            if (Active != null) Active.ReportAttempt(stageId, correct);
        }

        public static void Stage(string stageId)
        {
            if (Active != null) Active.CompleteStage(stageId);
        }

        // ==================================================================
        // Finish
        // ==================================================================

        /// <summary>Ends the run, writes the result and updates the ledger.
        /// Wire a "Finish" button here to let a learner end early - that counts as
        /// completed: false, which is what stops it awarding Competent.</summary>
        public AssessmentResult Finish(bool completed = true)
        {
            if (!IsRunning) return null;

            IsRunning = false;
            IsFinished = true;
            Unsubscribe();

            bool everyStageDone = AllStagesComplete();
            bool reallyCompleted = completed && everyStageDone;

            if (reallyCompleted && MistakeCount == 0 && perfectRunBonus > 0)
                Score += perfectRunBonus;

            AssessmentResult result = BuildResult(reallyCompleted);

            // Ledger first: RecordResult fills in result.criteria, and the stored
            // JSON should carry that judgement rather than an empty list.
            if (CompetencyLedger.Instance != null) CompetencyLedger.Instance.RecordResult(result);

            ResultsRepository.Save(result);

            if (contributeToGlobalScore && ScoreManager.Instance != null)
                ScoreManager.Instance.AddScore(Score, $"module:{moduleId}");

            Debug.Log($"[AssessmentRecorder] {moduleId} finished. Score {Score}/{result.maxScore}, " +
                      $"mistakes {MistakeCount}, first-attempt {result.FirstAttemptPercent}%, " +
                      $"time {AssessmentResult.FormatTime(ElapsedSeconds)}.");

            OnRunFinished?.Invoke(result);
            return result;
        }

        private AssessmentResult BuildResult(bool completed)
        {
            var profile = ProfileManager.Instance != null ? ProfileManager.Instance.ActiveProfile : null;

            int maxScore = 0;
            foreach (var stage in stages)
            {
                if (stage != null && !string.IsNullOrEmpty(stage.Key)) maxScore += stage.points;
            }
            if (perfectRunBonus > 0) maxScore += perfectRunBonus;

            int gradedAttempts = 0;
            int firstAttemptCorrect = 0;
            foreach (var outcome in outcomes)
            {
                if (outcome.attempts <= 0) continue;
                gradedAttempts++;
                if (outcome.firstAttemptCorrect) firstAttemptCorrect++;
            }

            var result = new AssessmentResult
            {
                resultId = Guid.NewGuid().ToString("N").Substring(0, 12),
                profileId = profile != null ? profile.profileId : "default",
                learnerName = profile != null ? profile.displayName : "",
                learnerSection = profile != null ? profile.section : "",
                moduleId = moduleId,
                mode = (int)TrainingMode.Current,
                startedUtc = startedUtc.ToString("o"),
                finishedUtc = DateTime.UtcNow.ToString("o"),
                durationSeconds = ElapsedSeconds,
                completed = completed,
                score = Score,
                maxScore = maxScore,
                mistakeCount = MistakeCount,
                gradedAttempts = gradedAttempts,
                firstAttemptCorrect = firstAttemptCorrect
            };

            result.stages.AddRange(outcomes);
            result.mistakes.AddRange(mistakes);
            return result;
        }

        // ==================================================================

        private bool AllStagesComplete()
        {
            if (outcomes.Count == 0) return false;
            foreach (var outcome in outcomes)
            {
                if (!outcome.completed) return false;
            }
            return true;
        }

        private StageSpec FindSpec(string stageId)
        {
            foreach (var stage in stages)
            {
                if (stage != null && stage.Key == stageId) return stage;
            }
            return null;
        }

        private StageOutcome FindOutcome(string stageId)
        {
            foreach (var outcome in outcomes)
            {
                if (outcome.stageId == stageId) return outcome;
            }
            return null;
        }
    }
}