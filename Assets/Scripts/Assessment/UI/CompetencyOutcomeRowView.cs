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

        [Tooltip("Shows e.g. \"Score 85%\" once a COMPLETED ASSESSMENT run has scored this outcome, " +
                 "and stays blank until then. Optional.\n\n" +
                 "THIS ROW SHOWS THE CURRENT SCORE, NOT THE BEST. The high score belongs to the " +
                 "module header, where there is one of it - repeating a \"Best\" on every outcome " +
                 "row invites the reader to compare eight bests that all came from different runs.\n\n" +
                 "Story runs are not marked - StoryRunRecorder writes 0/0 on purpose - so a row that " +
                 "printed a score for them read \"0%\" after a clean playthrough. Hence the blank.")]
        [FormerlySerializedAs("bestScoreText")]
        [SerializeField] private TMP_Text scoreText;

        [Header("Status pill")]
        [Tooltip("An Image tinted by status. Optional.")]
        [SerializeField] private Image statusPill;

        [SerializeField] private Color notAttemptedColor = new Color(0.62f, 0.66f, 0.64f);
        [SerializeField] private Color practisedColor = new Color(0.85f, 0.58f, 0.24f);
        [SerializeField] private Color competentColor = new Color(0.13f, 0.55f, 0.40f);

        public enum BarShows
        {
            /// <summary>Where the learner stands after their most recent graded run.</summary>
            LatestScore = 0,

            /// <summary>Their all-time high for this outcome.</summary>
            BestScore = 1,
        }

        [Header("Score bar")]
        [Tooltip("The bar at the end of the row: a SCORE, out of 100. \"80%\" fills it four fifths. " +
                 "Optional.\n\n" +
                 "It used to show evidence collected over evidence required, which is why it sat " +
                 "FULL beside an 80% - both figures were true, they were just measuring different " +
                 "things, and a full bar next to a number that is not full reads as a bug.\n\n" +
                 "Empty until a completed Assessment run has scored this outcome, matching the label " +
                 "beside it: a story run is not marked, so there is no score to draw.")]
        [FormerlySerializedAs("evidenceBar")]
        [SerializeField] private Slider scoreBar;

        [Tooltip("WHICH score the bar draws.\n\n" +
                 "LATEST (default): every graded run moves it. A perfect first run fills it; an 85% " +
                 "retry drops it to 85. This is the only setting that shows a retry happened at all, " +
                 "and the best score is still kept and still printed beside it.\n\n" +
                 "BEST: the bar only ever rises. Choose this if you would rather the row read as a " +
                 "trophy than as a current standing.")]
        [SerializeField] private BarShows barShows = BarShows.LatestScore;

        [Tooltip("The word in front of the figure, e.g. \"Score\" for \"Score 85%\". Leave it empty " +
                 "for the bare percentage.")]
        [SerializeField] private string scoreLabel = "Score";

        [Tooltip("Fill colour for a score BELOW the pass mark. The threshold is read from " +
                 "CompetencyLedger.PassThresholdPercent, so this row and the judgement the ledger " +
                 "actually made can never disagree about what counts as a pass.")]
        [SerializeField] private Color failFillColour = new Color32(0xE5, 0x54, 0x4B, 0xFF);

        [Tooltip("Fill colour for a score at or above the pass mark. Leave the alpha at 0 (the " +
                 "default) to keep whatever colour the prefab's Fill image already has - which is " +
                 "usually what you want, so the passing rows look exactly as they do now.")]
        [SerializeField] private Color passFillColour = new Color(0f, 0f, 0f, 0f);

        [Tooltip("Pass mark to use when there is no CompetencyLedger to ask - on a prefab preview, " +
                 "or a scene opened on its own. Keep it the same as the ledger's.")]
        [Range(0, 100)]
        [SerializeField] private int fallbackPassThreshold = 80;

        [Tooltip("Also tint the score text on a fail. ON by default: the bar and the number then " +
                 "agree, and a learner who cannot tell the red from the green still has the figure " +
                 "and the status word - colour is never the only thing saying it.")]
        [SerializeField] private bool tintScoreTextToo = true;

        public string OutcomeCode { get; private set; }

        // The Fill image of the score bar, and the colour the prefab gave it. Captured once, before
        // anything has been recoloured, so a pass can be put back exactly as it was authored rather
        // than to a colour guessed here.
        private Image fillImage;
        private Color authoredFillColour = Color.white;
        private Color authoredScoreColour = Color.white;
        private bool captured;

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
            // LATEST BY DEFAULT, AND THE DEFAULT IS THE POINT. A bar pinned to the best can only
            // ever rise, so a learner who scored 100 and then 85 sees a full bar and no sign that
            // the second run counted for anything. Drawing the latest is the only version where the
            // screen answers "how am I doing now" - and because the best is kept on the record and
            // printed in the label, nothing they earned is thrown away by showing it.
            int shown = ScoreToDraw(record);

            if (scoreBar != null)
            {
                scoreBar.minValue = 0f;
                scoreBar.maxValue = 100f;
                scoreBar.value = shown < 0 ? 0f : Mathf.Clamp(shown, 0, 100);
            }

            Recolour(shown);

            // BLANK UNTIL A GRADED RUN HAS ACTUALLY SCORED THIS OUTCOME.
            //
            // The test used to be `attempts > 0`, and a story run counts as an attempt - so a
            // finished COC1 story playthrough put "Best 0%" on every row in the module. The 0 was
            // real: StoryRunRecorder writes score 0 / maxScore 0 deliberately, because a story run
            // is not marked. `assessmentScored` on the record is what tells a genuine zero apart
            // from never having been graded at all; see CompetencyLedger.Apply.
            if (scoreText != null)
            {
                // The SAME figure the bar is drawing, so the two can never disagree. That was the
                // whole trouble with the old row: a bar showing one number beside a label showing
                // another, with nothing to tell the reader which was which.
                scoreText.text = shown < 0
                    ? ""
                    : string.IsNullOrEmpty(scoreLabel)
                        ? $"{shown}%"
                        : $"{scoreLabel} {shown}%";
            }
        }

        /// <summary>
        /// The number the bar draws, or -1 for "nothing to draw".
        ///
        /// latestScored is checked separately from assessmentScored because a ledger written before
        /// the latest figure existed has assessmentScored = true and a latest of 0. The migration in
        /// CompetencyLedger backfills those, but a file that has not been through it yet must fall
        /// back to the best rather than draw an empty bar for a learner who has scored.
        /// </summary>
        private int ScoreToDraw(OutcomeRecord record)
        {
            if (record == null || !record.assessmentScored) return -1;

            if (barShows == BarShows.BestScore) return record.bestScorePercent;

            return record.latestScored ? record.latestScorePercent : record.bestScorePercent;
        }

        /// <summary>
        /// Red fill for a score below the pass mark, the authored colour at or above it.
        ///
        /// THE THRESHOLD IS THE LEDGER'S, NOT A NUMBER TYPED IN HERE. CompetencyLedger decides what
        /// passes - it is the same figure that decides whether an outcome may be marked Competent at
        /// all - and a row with its own copy would eventually show a green bar on a run the ledger
        /// had already judged as not competent. There is a fallback for a scene with no ledger in
        /// it, and that is the only time this row guesses.
        ///
        /// A score that has not been graded is left ALONE rather than painted red. The bar is empty
        /// there because nothing has been measured, and an empty red bar says "failed" about a
        /// learner who has simply not sat the assessment yet.
        /// </summary>
        private void Recolour(int shown)
        {
            Capture();

            if (fillImage == null && scoreText == null) return;

            if (shown < 0)
            {
                // Never graded: back to as-authored, in case this row object was recycled from a
                // failing outcome by a Refresh.
                if (fillImage != null) fillImage.color = authoredFillColour;
                if (scoreText != null && tintScoreTextToo) scoreText.color = authoredScoreColour;
                return;
            }

            CompetencyLedger ledger = CompetencyLedger.Instance;
            int threshold = ledger != null ? ledger.PassThresholdPercent : fallbackPassThreshold;

            bool failed = shown < threshold;

            if (fillImage != null)
            {
                fillImage.color = failed
                    ? failFillColour
                    : passFillColour.a > 0f ? passFillColour : authoredFillColour;
            }

            if (scoreText != null && tintScoreTextToo)
                scoreText.color = failed ? failFillColour : authoredScoreColour;
        }

        /// <summary>
        /// Finds the bar's Fill image and remembers the colours the prefab was built with.
        ///
        /// Done on first use rather than in Awake because a row is spawned and Set up in the same
        /// frame, and Awake on a freshly instantiated object runs before the prefab's own values
        /// could have been changed by anything - but a row that has already been recoloured once
        /// must not have the red read back as its authored colour. Hence the flag.
        /// </summary>
        private void Capture()
        {
            if (captured) return;
            captured = true;

            if (scoreBar != null && scoreBar.fillRect != null)
            {
                fillImage = scoreBar.fillRect.GetComponent<Image>();
                if (fillImage != null) authoredFillColour = fillImage.color;
            }

            if (scoreText != null) authoredScoreColour = scoreText.color;
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