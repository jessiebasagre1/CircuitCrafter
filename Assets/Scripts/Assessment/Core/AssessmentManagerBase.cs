using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CircuitCrafter.Assessment
{
    /// <summary>
    /// The shared shape of a module's assessment run: HUD, mode switching,
    /// tutorial suppression, report panel and return-to-menu. Everything that is
    /// currently copied line-for-line between COC1AssessmentManager and
    /// COC2AssessmentManager, with the recording delegated to AssessmentRecorder.
    ///
    /// USE THIS FOR NEW MODULES - COC3 and COC4. Derive, and you get the whole
    /// run flow for the price of a class declaration:
    ///
    ///     public class COC3AssessmentManager : AssessmentManagerBase
    ///     {
    ///         protected override string ModuleId =&gt; "COC3";
    ///     }
    ///
    /// DO NOT retrofit COC1 and COC2 onto this yet. Both are finished and wired,
    /// and changing their base class means re-checking every Inspector reference
    /// in two scenes for no functional gain - LegacyAssessmentBridge already gets
    /// records out of them. Migrate them when you next have reason to open those
    /// scenes anyway.
    ///
    /// WHAT THIS CLASS DELIBERATELY DOES NOT DO
    /// It holds no stage list, no score and no timer of its own. All of that
    /// lives on the AssessmentRecorder it drives, so there is exactly one
    /// definition of how a run is scored and recorded. This class is presentation
    /// and flow; the recorder is the model.
    /// </summary>
    public abstract class AssessmentManagerBase : MonoBehaviour
    {
        [Header("Recorder")]
        [Tooltip("The AssessmentRecorder for this scene. Found on this GameObject if left empty.")]
        [SerializeField] protected AssessmentRecorder recorder;

        [Header("HUD")]
        [SerializeField] protected TMP_Text timerText;
        [SerializeField] protected TMP_Text scoreText;
        [SerializeField] protected TMP_Text mistakesText;

        [Tooltip("The timer/score bar. Shown only in Practice and Assessment.")]
        [SerializeField] protected GameObject hudRoot;

        [Header("Mode switching")]
        [Tooltip("Switched OFF for a graded run - QuizManager, TaskCheckpointAutosaver, intro dialog triggers.")]
        [SerializeField] protected GameObject[] disableWhenGraded;

        [Tooltip("Switched ON for a graded run - the HUD, a Finish button, anything assessment-only.")]
        [SerializeField] protected GameObject[] enableWhenGraded;

        [Tooltip("Silence every tutorial dialog while graded. This manager's own messages still play, because they pass ignoreSuppression.")]
        [SerializeField] protected bool suppressTutorialDialogs = true;

        [Header("Report")]
        [SerializeField] protected GameObject reportPanel;
        [SerializeField] protected TMP_Text reportText;

        [Tooltip("Optional. Its OnClick is wired to ReturnToMenu() automatically.")]
        [SerializeField] protected Button returnToMenuButton;

        [SerializeField] protected string mainMenuSceneName = "MainMenu";

        /// <summary>e.g. "COC3". Must match the recorder's module id and the
        /// moduleId on the matching CompetencyDefinition asset.</summary>
        protected abstract string ModuleId { get; }

        protected AssessmentResult LastResult { get; private set; }

        private bool reportPending;

        // ==================================================================

        protected virtual void Awake()
        {
            if (recorder == null) recorder = GetComponent<AssessmentRecorder>();
        }

        protected virtual void Start()
        {
            if (recorder == null)
            {
                Debug.LogError($"[{GetType().Name}] No AssessmentRecorder assigned or found - the run can't be recorded.", this);
                enabled = false;
                return;
            }

            recorder.OnRunFinished += HandleRunFinished;

            bool graded = TrainingMode.IsGraded;

            foreach (var go in disableWhenGraded) if (go != null) go.SetActive(!graded);
            foreach (var go in enableWhenGraded) if (go != null) go.SetActive(graded);

            // The HUD earns its place in Practice too - a learner working
            // unassisted wants to see the same numbers they'll be judged on, even
            // when this particular run isn't the judgement.
            if (hudRoot != null) hudRoot.SetActive(TrainingMode.IsUnassisted);
            if (reportPanel != null) reportPanel.SetActive(false);

            DialogManager.SuppressTutorials = graded && suppressTutorialDialogs;

            if (returnToMenuButton != null)
            {
                returnToMenuButton.onClick.RemoveListener(ReturnToMenu);
                returnToMenuButton.onClick.AddListener(ReturnToMenu);
            }

            RefreshHud();
        }

        protected virtual void OnDestroy()
        {
            if (recorder != null) recorder.OnRunFinished -= HandleRunFinished;

            // Never leave the flag set for whatever scene loads next - the exact
            // bug both existing assessment managers guard against in their own
            // OnDestroy.
            DialogManager.SuppressTutorials = false;
        }

        protected virtual void Update()
        {
            // Before the running check, deliberately: by the time a report can be
            // pending the run is already over, so a check after the guard would
            // never fire and the report would never appear. Same ordering fix
            // COC2AssessmentManager needed.
            if (reportPending && !DialogIsOpen()) ShowReport();

            if (recorder != null && recorder.IsRunning) RefreshHud();
        }

        // ==================================================================

        /// <summary>Wire a "Finish" button here to let a learner end early.</summary>
        public void Finish() => recorder?.Finish(completed: false);

        private void HandleRunFinished(AssessmentResult result)
        {
            LastResult = result;

            DialogManager.SuppressTutorials = false;
            RefreshHud();

            // Never drop the report on top of a dialog the learner is still
            // reading - wait for it to close.
            if (DialogIsOpen()) reportPending = true;
            else ShowReport();
        }

        private void ShowReport()
        {
            reportPending = false;

            if (reportPanel != null) reportPanel.SetActive(true);
            if (reportText != null) reportText.text = BuildReport(LastResult);

            if (reportPanel == null && reportText == null)
            {
                Debug.LogWarning($"[{GetType().Name}] Run finished, but neither Report Panel nor Report Text is assigned - the learner sees nothing.\n\n" +
                                 BuildReport(LastResult), this);
            }
        }

        /// <summary>
        /// The end-of-run report. Override to add module-specific lines - call
        /// base first and append, so every module's report keeps the same
        /// recognisable header.
        /// </summary>
        protected virtual string BuildReport(AssessmentResult result)
        {
            if (result == null) return "No result was recorded for this run.";

            var sb = new StringBuilder();
            sb.AppendLine($"=== {ModuleId} {result.Mode} Report ===");
            sb.AppendLine();
            sb.AppendLine($"Learner:         {(string.IsNullOrEmpty(result.learnerName) ? "-" : result.learnerName)}");
            sb.AppendLine($"Time taken:      {AssessmentResult.FormatTime(result.durationSeconds)}");
            sb.AppendLine($"Final score:     {result.score} of {result.maxScore}  ({result.ScorePercent}%)");
            sb.AppendLine($"Mistakes:        {result.mistakeCount}");

            if (result.FirstAttemptPercent >= 0)
                sb.AppendLine($"Right first time: {result.firstAttemptCorrect} of {result.gradedAttempts}  ({result.FirstAttemptPercent}%)");

            sb.AppendLine($"Stages complete: {result.StagesCompleted} of {result.stages.Count}");
            sb.AppendLine();

            if (result.criteria.Count > 0)
            {
                sb.AppendLine("Competency:");
                foreach (var criterion in result.criteria)
                {
                    if (criterion == null) continue;
                    sb.AppendLine($"  {criterion.outcomeCode}  {criterion.outcomeTitle}");
                    sb.AppendLine($"      {criterion.Status}   (evidence {criterion.evidenceSatisfied}/{criterion.evidenceRequired})");
                }
                sb.AppendLine();
            }

            if (result.mistakes.Count > 0)
            {
                sb.AppendLine("Mistakes:");
                foreach (var mistake in result.mistakes)
                {
                    if (mistake == null) continue;
                    sb.AppendLine($"  {AssessmentResult.FormatTime(mistake.atSeconds)}  [{mistake.category}]  {mistake.description}");
                }
                sb.AppendLine();
            }

            if (!result.completed)
                sb.AppendLine("This run was not completed, so it cannot count toward a competency judgement.");

            return sb.ToString();
        }

        /// <summary>Wire the report panel's "Back to Menu" button here.</summary>
        public virtual void ReturnToMenu()
        {
            TrainingMode.End();
            DialogManager.SuppressTutorials = false;

            if (!string.IsNullOrEmpty(mainMenuSceneName))
                SceneManager.LoadScene(mainMenuSceneName);
        }

        // ==================================================================

        protected virtual void RefreshHud()
        {
            if (recorder == null) return;

            if (timerText != null)
                timerText.text = AssessmentResult.FormatTime(recorder.ElapsedSeconds);

            if (scoreText != null) scoreText.text = $"Score: {recorder.Score}";
            if (mistakesText != null) mistakesText.text = $"Mistakes: {recorder.MistakeCount}";
        }

        protected static bool DialogIsOpen()
        {
            return DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen;
        }
    }
}
