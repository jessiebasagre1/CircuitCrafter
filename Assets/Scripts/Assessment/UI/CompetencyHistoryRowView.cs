using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CircuitCrafter.Assessment.UI
{
    /// <summary>
    /// One row of CompetencyHistoryView: one past run of one module.
    ///
    /// PUT THIS ON: a prefab, and assign it to CompetencyHistoryView.rowPrefab.
    ///
    /// Suggested layout:
    ///   [Sep 3, 2026] [ASSESSMENT] [94%  (47/50)] [Completed] [1 mistake - 90% first-attempt - 06:12]
    ///
    /// All fields optional except one of them actually being assigned - a row with everything
    /// left empty is a blank bar and tells the learner nothing happened here.
    ///
    /// MODE IS SHOWN, NEVER HIDDEN. Guided, Practice and Assessment runs all appear in the same
    /// list rather than three separate tabs, because a learner's whole history is one story - but
    /// they mean very different things (only Assessment counts toward Competent, or toward the
    /// high score on the popup this row sits in), so the mode is coloured and never left implicit.
    /// </summary>
    public class CompetencyHistoryRowView : MonoBehaviour
    {
        [Header("Fields")]
        [SerializeField] private TMP_Text dateText;
        [SerializeField] private TMP_Text modeText;
        [SerializeField] private TMP_Text scoreText;
        [SerializeField] private TMP_Text statusText;

        [Tooltip("The detail line - mistakes, first-attempt accuracy, duration. Optional.")]
        [SerializeField] private TMP_Text detailText;

        [Tooltip("Optional small pill/strip coloured to match the mode.")]
        [SerializeField] private Image modePill;

        [Header("Colours")]
        [Tooltip("Gold, matching the palette this project already reserves for anything earned - Assessment is the mode that can actually be earned.")]
        [SerializeField] private Color assessmentColor = new Color32(0xFF, 0xC9, 0x3C, 0xFF);

        [SerializeField] private Color practiceColor = new Color32(0x5F, 0xD4, 0xFF, 0xFF);
        [SerializeField] private Color guidedColor = new Color32(0xA9, 0xC6, 0xF0, 0xFF);

        // Light, for the same reason as the header's met/not-met pair: these rows sit on #2B3AA8,
        // and the dark green and grey they used to be measured 1.84:1 and 1.91:1 against it.
        [Tooltip("Light mint - readable on the row's blue, where a dark green is not.")]
        [SerializeField] private Color completedColor = new Color32(0x6E, 0xE7, 0xB7, 0xFF);

        [Tooltip("Deliberately neutral, not red - an incomplete run is not a failure, just unfinished.")]
        [SerializeField] private Color incompleteColor = new Color32(0xC3, 0xD7, 0xF7, 0xFF);

        [Header("Detail (optional)")]
        [Tooltip("Opens CompetencyRunDetailView for this run. Leave empty on a build that doesn't want a detail popup - the row still shows its summary either way.")]
        [SerializeField] private Button viewButton;

        /// <summary>Fired with this row's full result when View is clicked. CompetencyHistoryView
        /// subscribes to every row it spawns and forwards this to its own detail popup.</summary>
        public event Action<AssessmentResult> ViewRequested;

        private AssessmentResult currentResult;

        private void Awake()
        {
            // Reads the FIELD, not a captured parameter - Setup() can be called again on the same
            // instance in principle, and this way the listener always fires for whatever result
            // this row was most recently set up to show, never a stale one from the first Setup.
            if (viewButton != null)
            {
                viewButton.onClick.AddListener(() => ViewRequested?.Invoke(currentResult));
            }
            else
            {
                // Otherwise "the button does nothing when clicked" and "there is no button" look
                // identical from the outside - this is what tells them apart.
                Debug.LogWarning("[CompetencyHistoryRowView] No View Button assigned on '" + name +
                                 "' - clicking it (if one exists on this row) does nothing. Assign " +
                                 "this row's Button to the View Button field, or rebuild the " +
                                 "screen so it's wired automatically.", this);
            }
        }

        public void Setup(AssessmentResult result)
        {
            if (result == null) return;

            currentResult = result;

            if (dateText != null) dateText.text = AssessmentResult.FormatWhen(result.finishedUtc, "MMM d, yyyy  HH:mm");

            Color modeColor = ModeColor(result.Mode);

            if (modeText != null)
            {
                modeText.text = result.Mode.ToString();
                modeText.color = modeColor;
            }

            if (modePill != null) modePill.color = modeColor;

            if (scoreText != null)
            {
                scoreText.text = result.maxScore > 0
                    ? $"{result.ScorePercent}%  ({result.score}/{result.maxScore})"
                    : "-";
            }

            if (statusText != null)
            {
                statusText.text = result.completed ? "Completed" : "Incomplete";
                statusText.color = result.completed ? completedColor : incompleteColor;
            }

            if (detailText != null)
            {
                string firstAttempt = result.FirstAttemptPercent >= 0
                    ? $"{result.FirstAttemptPercent}% first-attempt"
                    : "no graded attempts";

                // The quiz mark rides on the same line rather than getting a field of its own, so
                // the row prefab does not have to be rebuilt to show it. It is omitted entirely
                // on a run with no quiz - printing "quiz 0/0" would read as a failed quiz rather
                // than as one that was never offered.
                string quiz = result.KnowledgeAttempted
                    ? $"  \u00b7  quiz {result.knowledgeCorrect}/{result.knowledgeQuestions} ({result.KnowledgePercent}%)"
                    : "";

                detailText.text = $"{result.mistakeCount} mistake{(result.mistakeCount == 1 ? "" : "s")}" +
                                  $"  \u00b7  {firstAttempt}  \u00b7  {AssessmentResult.FormatTime(result.durationSeconds)}" +
                                  quiz;
            }
        }

        private Color ModeColor(SessionMode mode)
        {
            switch (mode)
            {
                case SessionMode.Assessment: return assessmentColor;
                case SessionMode.Practice: return practiceColor;
                default: return guidedColor;
            }
        }
    }
}