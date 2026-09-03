using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CircuitCrafter.Assessment.UI
{
    /// <summary>
    /// One learning outcome on the competency profile screen.
    ///
    /// PUT THIS ON: a prefab, and assign that prefab to
    /// CompetencyProfileView.outcomeRowPrefab - same pattern as TicketRowView
    /// and ChecklistItemRowView.
    ///
    /// Suggested row layout:
    ///   [LO1] [outcome title] [evidence 3/4] [status pill]
    ///
    /// Only titleText is required. Every other field is optional - leave one
    /// unassigned to simplify the row and it is skipped, exactly like the
    /// existing row views in the project.
    ///
    /// The status pill can be an Image (tinted), a TMP_Text (worded), or both.
    /// Colour alone is never enough on its own - roughly one male student in
    /// twelve has some colour vision deficiency, and a red/green pill is the
    /// classic thing they cannot read. Assign statusText as well as statusPill
    /// unless the row already says the status some other way.
    /// </summary>
    public class CompetencyOutcomeRowView : MonoBehaviour
    {
        [Header("Text")]
        [Tooltip("The outcome code, e.g. \"LO1\". Optional.")]
        [SerializeField] private TMP_Text codeText;

        [Tooltip("The outcome title. The one field that really should be assigned.")]
        [SerializeField] private TMP_Text titleText;

        [Tooltip("Shows e.g. \"3 of 4 evidenced\". Optional.")]
        [SerializeField] private TMP_Text evidenceText;

        [Tooltip("Shows \"Not attempted\" / \"Practised\" / \"Competent\". Optional but strongly recommended - see the class comment.")]
        [SerializeField] private TMP_Text statusText;

        [Tooltip("Shows e.g. \"Best 92%\" once there is a score. Optional.")]
        [SerializeField] private TMP_Text bestScoreText;

        [Header("Status pill")]
        [Tooltip("An Image tinted by status. Optional.")]
        [SerializeField] private Image statusPill;

        [SerializeField] private Color notAttemptedColor = new Color(0.62f, 0.66f, 0.64f);
        [SerializeField] private Color practisedColor = new Color(0.85f, 0.58f, 0.24f);
        [SerializeField] private Color competentColor = new Color(0.13f, 0.55f, 0.40f);

        [Header("Evidence bar")]
        [Tooltip("Optional 0-1 bar showing how much of the outcome's evidence is in place.")]
        [SerializeField] private Slider evidenceBar;

        public string OutcomeCode { get; private set; }

        /// <summary>
        /// Fills the row in. Pass record = null for an outcome the learner has
        /// never attempted - the row still appears, showing what is available to
        /// work toward rather than hiding it, which is the whole point of the
        /// screen.
        /// </summary>
        public void Setup(CompetencyDefinition.LearningOutcome outcome, OutcomeRecord record)
        {
            if (outcome == null) return;

            OutcomeCode = outcome.code;

            if (codeText != null) codeText.text = outcome.code;
            if (titleText != null) titleText.text = outcome.title;

            CompetencyStatus status = record != null ? record.Status : CompetencyStatus.NotAttempted;

            if (statusText != null) statusText.text = Describe(status);
            if (statusPill != null) statusPill.color = ColorFor(status);

            // An outcome with no record has no evidence figures to show. Falling
            // back to the definition's own count means the row still reads
            // "0 of 4 evidenced" rather than "0 of 0", which would look broken.
            int satisfied = record != null ? record.evidenceSatisfied : 0;
            int required = record != null && record.evidenceRequired > 0
                ? record.evidenceRequired
                : CountEvidence(outcome);

            if (evidenceText != null)
                evidenceText.text = required > 0 ? $"{satisfied} of {required} evidenced" : "";

            if (evidenceBar != null)
            {
                evidenceBar.maxValue = Mathf.Max(1, required);
                evidenceBar.value = satisfied;
            }

            if (bestScoreText != null)
            {
                bestScoreText.text = record != null && record.attempts > 0
                    ? $"Best {record.bestScorePercent}%"
                    : "";
            }
        }

        private static int CountEvidence(CompetencyDefinition.LearningOutcome outcome)
        {
            if (outcome.evidenceTaskIds == null) return 0;

            int n = 0;
            foreach (string id in outcome.evidenceTaskIds)
            {
                if (!string.IsNullOrWhiteSpace(id)) n++;
            }

            return outcome.requiredEvidenceCount > 0
                ? Mathf.Min(outcome.requiredEvidenceCount, n)
                : n;
        }

        private static string Describe(CompetencyStatus status)
        {
            switch (status)
            {
                case CompetencyStatus.Competent: return "Competent";
                case CompetencyStatus.Practised: return "Practised";
                default: return "Not attempted";
            }
        }

        private Color ColorFor(CompetencyStatus status)
        {
            switch (status)
            {
                case CompetencyStatus.Competent: return competentColor;
                case CompetencyStatus.Practised: return practisedColor;
                default: return notAttemptedColor;
            }
        }
    }
}
