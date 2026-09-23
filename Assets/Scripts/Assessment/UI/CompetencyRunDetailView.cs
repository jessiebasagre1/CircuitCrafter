using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CircuitCrafter.Assessment.UI
{
    /// <summary>
    /// The full detail behind one history row: every stage, every mistake, every learning
    /// outcome this one run touched - everything AssessmentResult actually recorded, not just
    /// the summary line CompetencyHistoryRowView shows.
    ///
    /// ATTACH THIS TO: the popup panel built by CompetencyScreenBuilder (or your own - see
    /// SETUP). Assign it to CompetencyHistoryView.detailView.
    ///
    /// WHY ONE RICH-TEXT BLOCK, NOT A ROW PREFAB PER STAGE/MISTAKE
    /// This is a read-only report, and ModuleDebriefPanel already established the pattern of
    /// writing `<b>`/`<color>` tags straight into one TMP_Text for exactly this kind of screen.
    /// A stage row prefab AND a mistake row prefab for one popup that exists to be glanced at
    /// once, then closed, is a lot of machinery for very little benefit over one formatted block
    /// in a scroll view.
    ///
    /// SETUP
    ///   Body Text needs Rich Text ON and Word Wrap ON, sitting in a ScrollRect content object
    ///   with a Vertical Layout Group and a Content Size Fitter (Preferred Size) so the block
    ///   grows to fit whatever this particular run recorded - a COC4 ticket with eight mistakes
    ///   takes far more room than a clean COC1 run, and this should never clip either one.
    /// </summary>
    public class CompetencyRunDetailView : MonoBehaviour
    {
        [Header("Header")]
        [Tooltip("\"COC3 - Sep 3, 2026  14:12\", filled in by Open(). Optional.")]
        [SerializeField] private TMP_Text titleText;

        [Header("Body")]
        [Tooltip("The whole report, as one rich-text block. Needs Rich Text and Word Wrap both ON.")]
        [SerializeField] private TMP_Text bodyText;

        [Header("Buttons")]
        [SerializeField] private Button closeButton;

        // ---- palette -------------------------------------------------
        // Matched to CompetencyScreenBuilder's palette so this reads as part of the same screen
        // rather than a different tool bolted on.
        private const string ColorCyan = "#5FD4FF";
        private const string ColorGold = "#FFC93C";
        private const string ColorGreen = "#3ADB76";
        private const string ColorMuted = "#A9C6F0";
        private const string ColorMistake = "#FF8A6B";

        // Set at the very start of Open(), before SetActive(true) - see Awake() for why.
        private bool openRequested;

        private void Awake()
        {
            if (closeButton != null) closeButton.onClick.AddListener(Close);

            // THE SAME Start/UnityEvent TRAP THIS PROJECT HAS ALREADY HIT AND FIXED FOUR TIMES
            // (PCPowerStation, SiteObservationPanel, TicketDiagnosisPanel, RepairVerificationPanel,
            // SettingsPanel). This popup starts inactive in the built scene, so Awake does not run
            // until the FIRST time something activates it - and Open() calling SetActive(true) for
            // the first time IS that something. Blindly hiding here would undo the very activation
            // that just woke this component up, which is exactly what forces a second click before
            // anything visibly opens. openRequested is set at the very start of Open(), before
            // SetActive(true), so by the time this line runs it can tell "just built, sitting
            // inactive in the scene" apart from "just asked to open".
            if (gameObject.activeSelf && !openRequested) gameObject.SetActive(false);
        }

        /// <summary>Opens the popup showing everything one run recorded. Safe to call with a
        /// result straight off disk - nothing here depends on live gameplay state.</summary>
        public void Open(AssessmentResult result)
        {
            openRequested = true;

            if (result == null) return;

            if (titleText != null)
            {
                titleText.text = $"{result.moduleId} - " +
                                  AssessmentResult.FormatWhen(result.finishedUtc, "MMM d, yyyy  HH:mm");
            }

            if (bodyText != null) bodyText.text = BuildBody(result);

            gameObject.SetActive(true);
        }

        public void Close() => gameObject.SetActive(false);

        // ==================================================================

        private static string BuildBody(AssessmentResult result)
        {
            var sb = new StringBuilder();

            // ---- summary ---------------------------------------------
            string learner = string.IsNullOrEmpty(result.learnerName) ? "n/a" : result.learnerName;
            if (!string.IsNullOrEmpty(result.learnerSection)) learner += $"  ({result.learnerSection})";

            sb.AppendLine($"<b>Learner:</b> {learner}");
            sb.AppendLine($"<b>Mode:</b> {result.Mode}     " +
                          $"<b>Status:</b> {(result.completed ? "Completed" : "Incomplete")}");
            sb.AppendLine($"<b>Score:</b> {result.ScorePercent}%  ({result.score}/{result.maxScore})     " +
                          $"<b>First-attempt:</b> " +
                          $"{(result.FirstAttemptPercent >= 0 ? result.FirstAttemptPercent + "%" : "n/a")}");
            sb.AppendLine($"<b>Duration:</b> {AssessmentResult.FormatTime(result.durationSeconds)}     " +
                          $"<b>Mistakes:</b> {result.mistakeCount}");

            if (result.KnowledgeAttempted)
            {
                sb.AppendLine($"<b>Knowledge (written test):</b> {result.KnowledgePercent}%  " +
                              $"({result.knowledgeCorrect}/{result.knowledgeQuestions} across " +
                              $"{result.knowledgeQuizzes} quiz(zes))");
            }

            // ---- stages ------------------------------------------------
            sb.AppendLine();
            sb.AppendLine($"<b><color={ColorCyan}>STAGES</color></b>");

            if (result.stages == null || result.stages.Count == 0)
            {
                sb.AppendLine($"  <color={ColorMuted}>(none recorded)</color>");
            }
            else
            {
                foreach (var stage in result.stages)
                {
                    if (stage == null) continue;

                    string name = string.IsNullOrEmpty(stage.displayName) ? stage.stageId : stage.displayName;
                    string mark = stage.completed
                        ? $"<color={ColorGreen}>done</color>"
                        : $"<color={ColorMuted}>not done</color>";
                    string first = stage.firstAttemptCorrect ? ", first-attempt" : "";
                    string when = stage.completedAtSeconds >= 0f
                        ? $" at {AssessmentResult.FormatTime(stage.completedAtSeconds)}"
                        : "";

                    sb.AppendLine($"  - {name} - {mark} - {stage.points} pt(s) - " +
                                  $"{stage.attempts} attempt(s){first}{when}");
                }
            }

            // ---- mistakes ------------------------------------------------
            sb.AppendLine();
            sb.AppendLine($"<b><color={ColorMistake}>MISTAKES</color></b>");

            if (result.mistakes == null || result.mistakes.Count == 0)
            {
                sb.AppendLine($"  <color={ColorMuted}>(none recorded)</color>");
            }
            else
            {
                foreach (var mistake in result.mistakes)
                {
                    if (mistake == null) continue;

                    sb.AppendLine($"  - [{mistake.category}] {mistake.description}  " +
                                  $"({AssessmentResult.FormatTime(mistake.atSeconds)})");
                }
            }

            // ---- learning outcomes ----------------------------------------
            if (result.criteria != null && result.criteria.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"<b><color={ColorGold}>LEARNING OUTCOMES</color></b>");

                foreach (var criterion in result.criteria)
                {
                    if (criterion == null) continue;

                    sb.AppendLine($"  - {criterion.outcomeCode} {criterion.outcomeTitle}: " +
                                  $"{criterion.Status} " +
                                  $"({criterion.evidenceSatisfied} of {criterion.evidenceRequired} evidenced)");
                }
            }

            return sb.ToString();
        }
    }
}