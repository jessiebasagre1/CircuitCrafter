using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// The assessment overlay inside a repair site: which ticket this is, how long the run has
    /// taken, and the way out of a ticket the learner cannot solve.
    ///
    /// ATTACH THIS TO: a small canvas in EVERY repair-site scene. It switches itself off entirely
    /// when the run is not active, so the same object can sit in a scene that story mode also uses.
    ///
    /// IT SHOWS NO SCORE, and that is deliberate. A running score during an assessment invites the
    /// learner to work the number rather than the machine, and COC4's score is not knowable
    /// mid-run anyway - a ticket is graded on diagnosis AND repair, and neither is settled until
    /// they leave the site. Ticket count and elapsed time are the two things they genuinely need
    /// to pace themselves.
    ///
    /// MISTAKES (optional, like the COC1-3 HUDs): assign Mistakes Text for "Mistakes: 2  (-20)",
    /// and Mistake Notice Text for a brief "Mistake recorded  -10" when one happens. Neither says
    /// WHAT the mistake was - that waits for the report.
    /// </summary>
    public class COC4AssessmentHud : MonoBehaviour
    {
        [Header("Panel")]
        [Tooltip("The overlay root. Usually this object. Switched off when no assessment is running.")]
        [SerializeField] private GameObject panelRoot;

        [Header("Labels")]
        [Tooltip("\"Ticket 2 of 5\".")]
        [SerializeField] private TMP_Text progressText;

        [Tooltip("Time LEFT for the whole run when the launcher set a limit, otherwise time " +
                 "elapsed. Counting down rather than up is the point of having a limit - a learner " +
                 "watching 34:12 climb has to do arithmetic to know whether to hurry.")]
        [SerializeField] private TMP_Text timeText;

        [Tooltip("The colour the clock turns inside the last few minutes.")]
        [SerializeField] private Color timeLowColor = new Color32(0xFF, 0xB3, 0x3C, 0xFF);

        [Tooltip("Seconds left at which the clock changes colour. It never flashes or beeps: an " +
                 "alarm makes a learner rush the ticket they are on, which is how a run that was " +
                 "going to finish four tickets finishes three badly.")]
        [Min(0f)]
        [SerializeField] private float lowTimeSeconds = 300f;

        [Tooltip("OPTIONAL, and think before you assign it.\n\n" +
                 "It shows points BANKED from tickets already finished - never a prediction about " +
                 "the ticket in hand, because that outcome is not settled until the learner leaves " +
                 "the site.\n\n" +
                 "A running score invites a learner to work the number rather than the machine, and " +
                 "someone who sees 0 after a ticket that felt fine can lose the next two to it. " +
                 "Leave it empty for a cleaner assessment; assign it if your learners expect the " +
                 "same HUD the other COCs give them.")]
        [SerializeField] private TMP_Text scoreText;

        [Tooltip("Optional. \"Mistakes: 2  (-20)\" - the same counter the COC1-3 assessment HUDs " +
                 "show. Every mistake costs Mistake Penalty points (10) off the final score.")]
        [SerializeField] private TMP_Text mistakesText;

        [Tooltip("{0} = mistakes, {1} = points taken off so far.")]
        [SerializeField] private string mistakesFormat = "Mistakes: {0}  (-{1})";

        [Tooltip("Optional. A short line that appears for a moment when a mistake is recorded, e.g. " +
                 "\"Mistake recorded  -10\". It never says what the mistake was: the assessment " +
                 "does not explain itself until the report. Start it empty or switched off.")]
        [SerializeField] private TMP_Text mistakeNoticeText;

        [Tooltip("{0} = the penalty for one mistake.")]
        [SerializeField] private string mistakeNoticeFormat = "Mistake recorded  -{0}";

        [Min(0f)]
        [SerializeField] private float mistakeNoticeSeconds = 2.5f;

        [Tooltip("The Mistakes counter shows this colour for a moment after a new mistake.")]
        [SerializeField] private Color mistakeFlashColor = new Color32(0xFF, 0x8A, 0x6B, 0xFF);

        [Tooltip("Optional. The customer's complaint, so the learner can re-read what they were " +
                 "called out for without a hub to go back to.")]
        [SerializeField] private TMP_Text ticketText;

        [Header("Give up")]
        [Tooltip("Moves to the next ticket and records this one as not resolved.")]
        [SerializeField] private Button giveUpButton;

        [Tooltip("Shown when Give up is pressed. Wire its Yes to ConfirmGiveUp() and its No to " +
                 "CancelGiveUp(). Leave empty and the button acts immediately - which you do not " +
                 "want, because it is unrecoverable.")]
        [SerializeField] private GameObject confirmPanel;

        [SerializeField] private TMP_Text confirmText;

        // ==================================================================

        private Color normalColour = Color.white;
        private bool normalColourCaptured;

        private Color mistakesColour = Color.white;
        private int lastMistakeCount = -1;
        private float flashUntil;
        private float noticeUntil;

        private void Awake()
        {
            if (giveUpButton != null) giveUpButton.onClick.AddListener(AskToGiveUp);
            if (confirmPanel != null) confirmPanel.SetActive(false);
            if (mistakesText != null) mistakesColour = mistakesText.color;
            if (mistakeNoticeText != null) mistakeNoticeText.gameObject.SetActive(false);
        }

        private void Start()
        {
            bool running = COC4AssessmentRun.IsActive;

            if (panelRoot != null) panelRoot.SetActive(running);
            if (!running) return;

            var attempt = COC4AssessmentRun.Current;

            if (ticketText != null)
            {
                ticketText.text = attempt != null && attempt.ticket != null
                    ? attempt.ticket.title + "\n" + attempt.ticket.description
                    : "";
            }

            Refresh();
        }

        private void Update()
        {
            if (!COC4AssessmentRun.IsActive) return;
            Refresh();
        }

        private void Refresh()
        {
            if (progressText != null) progressText.text = COC4AssessmentRun.Progress;

            if (scoreText != null)
                scoreText.text = $"Score: {COC4AssessmentRun.Score}";

            RefreshMistakes();

            if (timeText == null) return;

            bool limited = COC4AssessmentRun.TimeLimitSeconds > 0f;
            float t = limited ? COC4AssessmentRun.TimeRemaining : COC4AssessmentRun.ElapsedSeconds;

            timeText.text = $"{Mathf.FloorToInt(t / 60f):00}:{Mathf.FloorToInt(t % 60f):00}";

            if (limited && t <= lowTimeSeconds)
            {
                timeText.color = timeLowColor;
            }
            else if (!normalColourCaptured)
            {
                // Captured on the first frame it is NOT low, so the original colour survives - read
                // in Awake it would be whatever the prefab had, and read after a recolour it would
                // be the warning colour, which is how a clock gets permanently stuck amber.
                normalColour = timeText.color;
                normalColourCaptured = true;
            }
            else
            {
                timeText.color = normalColour;
            }
        }

        /// <summary>The Mistakes counter, and the brief notice when it goes up. Read from
        /// COC4AssessmentRun every frame rather than told by an event, so a mistake made on the
        /// frame a site loads - before this HUD existed - still shows.</summary>
        private void RefreshMistakes()
        {
            int count = COC4AssessmentRun.TotalMistakes;

            // The first frame only learns the count - arriving at ticket 3 with 2 mistakes
            // already made is not a new mistake to flash about.
            bool isNew = lastMistakeCount >= 0 && count > lastMistakeCount;
            lastMistakeCount = count;

            float now = Time.unscaledTime;

            if (isNew)
            {
                flashUntil = now + 0.8f;

                if (mistakeNoticeText != null && mistakeNoticeSeconds > 0f)
                {
                    mistakeNoticeText.text = SafeFormat(mistakeNoticeFormat, "Mistake recorded  -{0}",
                                                        COC4AssessmentRun.MistakePenalty);
                    mistakeNoticeText.gameObject.SetActive(true);
                    noticeUntil = now + mistakeNoticeSeconds;
                }
            }

            if (mistakeNoticeText != null && mistakeNoticeText.gameObject.activeSelf && now >= noticeUntil)
                mistakeNoticeText.gameObject.SetActive(false);

            if (mistakesText == null) return;

            mistakesText.text = SafeFormat(mistakesFormat, "Mistakes: {0}  (-{1})",
                                           count, COC4AssessmentRun.PenaltyPoints);
            mistakesText.color = now < flashUntil ? mistakeFlashColor : mistakesColour;
        }

        /// <summary>A typo in a format string in the Inspector should not throw every frame.</summary>
        private static string SafeFormat(string format, string fallback, params object[] args)
        {
            try { return string.Format(string.IsNullOrEmpty(format) ? fallback : format, args); }
            catch (System.FormatException) { return string.Format(fallback, args); }
        }

        // ==================================================================

        /// <summary>Wired to the Give up button.</summary>
        public void AskToGiveUp()
        {
            if (!COC4AssessmentRun.IsActive) return;

            if (confirmPanel == null)
            {
                // No confirmation built. Warned rather than silently made safe, because the safe
                // behaviour here - refusing - would look like a dead button on a phone.
                Debug.LogWarning("[COC4AssessmentHud] No Confirm Panel assigned, so Give up acted " +
                                 "immediately. Build a confirmation: this cannot be undone.", this);
                ConfirmGiveUp();
                return;
            }

            if (confirmText != null)
            {
                confirmText.text =
                    "Move on without resolving this ticket?\n\n" +
                    "It will be recorded as not resolved, and you cannot come back to it.";
            }

            confirmPanel.SetActive(true);
        }

        /// <summary>Wire the confirmation's Yes here.</summary>
        public void ConfirmGiveUp()
        {
            if (!COC4AssessmentRun.IsActive) return;

            if (confirmPanel != null) confirmPanel.SetActive(false);

            Debug.Log($"[COC4AssessmentHud] Learner gave up on {COC4AssessmentRun.Progress}.", this);

            COC4AssessmentRun.CompleteCurrentTicket(gaveUp: true);
        }

        /// <summary>Wire the confirmation's No here.</summary>
        public void CancelGiveUp()
        {
            if (confirmPanel != null) confirmPanel.SetActive(false);
        }
    }
}