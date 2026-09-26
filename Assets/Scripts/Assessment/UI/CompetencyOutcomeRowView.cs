using UnityEngine;
using UnityEngine.Serialization;
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

        [Tooltip("Shows e.g. \"Best 92%\" once a COMPLETED ASSESSMENT run has scored this outcome, " +
                 "and stays blank until then. Optional.\n\n" +
                 "Story runs are not marked - StoryRunRecorder writes 0/0 on purpose - so a row that " +
                 "printed a score for them read \"Best 0%\" after a clean playthrough.")]
        [SerializeField] private TMP_Text bestScoreText;

        [Header("Status pill")]
        [Tooltip("An Image tinted by status. Optional.")]
        [SerializeField] private Image statusPill;

        [SerializeField] private Color notAttemptedColor = new Color(0.62f, 0.66f, 0.64f);
        [SerializeField] private Color practisedColor = new Color(0.85f, 0.58f, 0.24f);
        [SerializeField] private Color competentColor = new Color(0.13f, 0.55f, 0.40f);

        [Header("Score bar")]
        [Tooltip("The bar at the end of the row: THE BEST SCORE, out of 100. \"Best 80%\" fills it " +
                 "four fifths. Optional.\n\n" +
                 "It used to show evidence collected over evidence required, which is why it sat " +
                 "FULL beside an 80% - both figures were true, they were just measuring different " +
                 "things, and a full bar next to a number that is not full reads as a bug.\n\n" +
                 "Empty until a completed Assessment run has scored this outcome, matching the label " +
                 "beside it: a story run is not marked, so there is no score to draw.")]
        [FormerlySerializedAs("evidenceBar")]
        [SerializeField] private Slider scoreBar;

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

            // THE BAR IS THE SCORE, OUT OF 100 - so it agrees with the number printed beside it.
            //
            // maxValue is fixed at 100 rather than at the evidence count, which is the whole change:
            // 2 of 2 evidenced genuinely IS full, and drawing that next to "Best 80%" put a full bar
            // beside a four-fifths number. Two true measurements, one bar, and the reader has no way
            // to tell which one they are looking at.
            //
            // Ungraded outcomes draw nothing, matching the blank label: a story run is not marked,
            // so there is no score to show. See CompetencyLedger.Apply for why 0 and "never graded"
            // have to be told apart.
            if (scoreBar != null)
            {
                scoreBar.minValue = 0f;
                scoreBar.maxValue = 100f;
                scoreBar.value = record != null && record.assessmentScored
                    ? Mathf.Clamp(record.bestScorePercent, 0, 100)
                    : 0f;
            }

            // BLANK UNTIL A GRADED RUN HAS ACTUALLY SCORED THIS OUTCOME.
            //
            // The test used to be `attempts > 0`, and a story run counts as an attempt - so a
            // finished COC1 story playthrough put "Best 0%" on every row in the module. The 0 was
            // real: StoryRunRecorder writes score 0 / maxScore 0 deliberately, because a story run
            // is not marked. `assessmentScored` on the record is what tells a genuine zero apart
            // from never having been graded at all; see CompetencyLedger.Apply.
            if (bestScoreText != null)
            {
                bestScoreText.text = record != null && record.assessmentScored
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