using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CircuitCrafter.Guidance
{
    /// <summary>
    /// Shows TutorialCardData cards - one at a time, only the first time, and never on top of a
    /// dialog. This is the "Explain -> Demonstrate" half of the learning loop; the learner then
    /// performs the action, and StoryGuide / ControlsTutorial check it and give feedback.
    ///
    /// ATTACH THIS TO: the TutorialCard object on the Guidance canvas. The builder makes it and fills
    /// every field.
    ///
    /// ANYTHING CAN ASK FOR A CARD:
    ///     TutorialCardPanel.TryShow(card);                 // from code
    ///     TutorialTrigger component                        // from the Inspector, on a condition
    ///     StoryGuide step > Tutorial                       // when a step begins
    ///
    /// WHAT STOPS A CARD (so pop-ups never pile up)
    ///   - The card's Kind is not allowed in this mode (GuidancePolicy) - so no card ever shows in
    ///     an assessment, whoever asks.
    ///   - Show Once is ticked and this learner has already dismissed it (TutorialProgress).
    ///   - It is already showing or already waiting.
    /// And cards WAIT while a DialogManager dialog or the job order is open, and leave a gap of
    /// GuidanceSettings > Min Seconds Between Cards after the previous one.
    ///
    /// Non-pausing cards (controls and feature tips) never take the cursor or stop the player - they
    /// sit in the corner and go away when the learner does the thing, presses Enter, or taps Got It.
    /// </summary>
    public class TutorialCardPanel : MonoBehaviour
    {
        public static TutorialCardPanel Instance { get; private set; }

        [Header("UI")]
        public GameObject panel;
        public TMP_Text titleText;
        public TMP_Text bodyText;
        public TMP_Text tryItText;

        [Tooltip("Shows the card's picture or flipbook. Hidden when the card has neither.")]
        public Image image;

        [Tooltip("'1 of 5' during the controls walkthrough. Optional.")]
        public TMP_Text counterText;

        public Button gotItButton;
        public Button showMeButton;

        [Tooltip("Only shown during the controls walkthrough - skips the rest of it.")]
        public Button skipButton;

        [Header("Keys")]
        [Tooltip("Dismisses the card on a keyboard. Not Space - Space is jump and dialog-advance.")]
        public KeyCode dismissKey = KeyCode.Return;

        [Header("Diagnostics")]
        public bool log;

        /// <summary>Raised when the learner presses Skip during a coach sequence.</summary>
        public event Action OnSkipRequested;

        /// <summary>Raised when a card closes, with the card and whether it was the learner (true)
        /// or the game (false - the action was done, or it timed out) that closed it.</summary>
        public event Action<TutorialCardData, bool> OnCardClosed;

        public TutorialCardData Current { get; private set; }
        public bool IsShowing => Current != null;

        private class Request
        {
            public TutorialCardData card;
            public Action onShowMe;
            public bool coach;
            public string counter;
        }

        private readonly List<Request> queue = new List<Request>();
        private Request active;
        private float nextAllowedAt;
        private float hideAt;
        private bool holdingPlayer;
        private bool recaptureOnRelease;
        private Coroutine flipbook;
        private int shownFrame = -1;
        private TMP_Text gotItLabel;
        private string gotItBaseText = "Got it";

        // ==================================================================

        private void Awake()
        {
            Instance = this;
            if (panel != null) panel.SetActive(false);

            if (gotItButton != null)
            {
                gotItButton.onClick.AddListener(Dismiss);
                gotItLabel = gotItButton.GetComponentInChildren<TMP_Text>(true);
                if (gotItLabel != null) gotItBaseText = gotItLabel.text;
            }
            if (showMeButton != null) showMeButton.onClick.AddListener(ShowMe);
            if (skipButton != null) skipButton.onClick.AddListener(Skip);
        }

        private void OnDestroy()
        {
            if (holdingPlayer) GuidanceUtil.ReleasePlayer(false);
            if (Instance == this) Instance = null;
        }

        /// <summary>Static convenience - safe in a scene with no panel.</summary>
        public static bool TryShow(TutorialCardData card, Action onShowMe = null)
        {
            return Instance != null && Instance.RequestCard(card, onShowMe);
        }

        /// <summary>
        /// Queues a card if the mode allows it and the learner has not seen it. Returns true when it
        /// was queued (or is already showing/queued).
        /// </summary>
        public bool RequestCard(TutorialCardData card, Action onShowMe = null)
        {
            if (card == null) return false;

            if (!GuidancePolicy.Allows(card.kind))
            {
                if (log) Debug.Log($"[TutorialCardPanel] '{card.Id}' not shown - {card.kind} is withheld in {GuidancePolicy.Mode}.", this);
                return false;
            }

            if (card.showOnce && TutorialProgress.HasSeen(card.Id))
            {
                if (log) Debug.Log($"[TutorialCardPanel] '{card.Id}' already seen by this learner.", this);
                return false;
            }

            if (active != null && active.card == card) return true;
            foreach (Request r in queue) if (r.card == card) return true;

            queue.Add(new Request { card = card, onShowMe = onShowMe });
            return true;
        }

        /// <summary>
        /// Shows a card as part of a coached sequence (the controls walkthrough), jumping the queue
        /// and ignoring Show Once - the sequence keeps its own record. Closes whatever was showing.
        /// </summary>
        public void PresentCoachCard(TutorialCardData card, string counter, Action onShowMe = null)
        {
            if (card == null) return;
            if (!GuidancePolicy.Allows(card.kind)) return;

            if (active != null)
            {
                // A feature tip that was up gets put back in line rather than lost - it has not been
                // read, so it has not been seen.
                Request bumped = active;
                Close(byLearner: false, markSeen: false);
                if (!bumped.coach) queue.Insert(0, bumped);
            }

            queue.Insert(0, new Request { card = card, onShowMe = onShowMe, coach = true, counter = counter });
            nextAllowedAt = 0f;
        }

        /// <summary>Closes the card if it is the one showing - "the learner did it". Also drops it
        /// from the queue if it had not come up yet.</summary>
        public void Complete(TutorialCardData card)
        {
            if (card == null) return;
            queue.RemoveAll(r => r.card == card);
            if (active != null && active.card == card) Close(byLearner: false, markSeen: true);
        }

        /// <summary>Got It.</summary>
        public void Dismiss()
        {
            if (active == null) return;
            if (Time.frameCount == shownFrame) return;   // the press that opened it is not the press that closes it
            Close(byLearner: true, markSeen: true);
        }

        /// <summary>Drops every waiting card and hides the one showing. Used when an assessment starts
        /// in a scene that already queued story cards.</summary>
        public void ClearAll()
        {
            queue.Clear();
            if (active != null) Close(byLearner: false, markSeen: false);
        }

        // ==================================================================

        private void Update()
        {
            if (active != null)
            {
                // A mode change under an open card (an assessment starting in this scene) takes it away.
                if (!GuidancePolicy.Allows(active.card.kind)) { ClearAll(); return; }

                if (GuidanceUtil.KeyPressed(dismissKey)) Dismiss();
                else if (hideAt > 0f && Time.unscaledTime >= hideAt) Close(byLearner: false, markSeen: true);
                return;
            }

            if (queue.Count == 0) return;
            if (Time.unscaledTime < nextAllowedAt) return;
            if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return;
            if (JobOrderPanel.Instance != null && JobOrderPanel.Instance.IsOpen) return;
            if (AssessmentReviewPanel.Instance != null && AssessmentReviewPanel.Instance.IsOpen) return;

            Request next = queue[0];

            // The controls walkthrough goes first; other cards wait for it.
            if (!next.coach && ControlsTutorial.Instance != null && ControlsTutorial.Instance.HoldsCardQueue) return;

            queue.RemoveAt(0);

            if (next.card == null || !GuidancePolicy.Allows(next.card.kind)) return;
            Open(next);
        }

        private void Open(Request request)
        {
            active = request;
            Current = request.card;
            shownFrame = Time.frameCount;

            TutorialCardData card = request.card;
            bool touch = GuidanceUtil.TouchWanted();

            if (titleText != null) titleText.text = card.title;
            if (bodyText != null) bodyText.text = GuidanceUtil.Bulletize(card.BodyFor(touch));

            if (tryItText != null)
            {
                tryItText.gameObject.SetActive(!string.IsNullOrEmpty(card.tryIt));
                tryItText.text = card.tryIt;
            }

            if (counterText != null)
            {
                counterText.gameObject.SetActive(!string.IsNullOrEmpty(request.counter));
                counterText.text = request.counter ?? "";
            }

            if (image != null)
            {
                Sprite still = card.image != null ? card.image : (card.HasDemo ? card.demoFrames[0] : null);
                image.gameObject.SetActive(still != null);
                image.sprite = still;
                image.preserveAspect = true;
            }

            if (showMeButton != null)
                showMeButton.gameObject.SetActive(card.HasDemo || request.onShowMe != null);

            if (skipButton != null) skipButton.gameObject.SetActive(request.coach);

            // On a keyboard the cursor is usually captured for mouse-look, so the button cannot be
            // clicked - name the key that works instead.
            if (gotItLabel != null)
                gotItLabel.text = touch || dismissKey == KeyCode.None ? gotItBaseText
                                : gotItBaseText + "  [" + (dismissKey == KeyCode.Return ? "Enter" : dismissKey.ToString()) + "]";

            hideAt = card.autoHideSeconds > 0f ? Time.unscaledTime + card.autoHideSeconds : 0f;

            if (card.pauseGameplay && !holdingPlayer)
            {
                holdingPlayer = true;
                recaptureOnRelease = GuidanceUtil.HoldPlayer();
            }

            if (panel != null) panel.SetActive(true);

            if (log) Debug.Log($"[TutorialCardPanel] Showing '{card.Id}' ({card.kind}).", this);
        }

        private void Close(bool byLearner, bool markSeen)
        {
            if (active == null) return;

            TutorialCardData card = active.card;
            if (markSeen && card.showOnce && !active.coach) TutorialProgress.MarkSeen(card.Id);

            StopFlipbook();
            active = null;
            Current = null;
            hideAt = 0f;

            if (panel != null) panel.SetActive(false);

            if (holdingPlayer)
            {
                holdingPlayer = false;
                GuidanceUtil.ReleasePlayer(recaptureOnRelease);
            }

            nextAllowedAt = Time.unscaledTime + GuidancePolicy.Settings.minSecondsBetweenCards;
            OnCardClosed?.Invoke(card, byLearner);
        }

        private void ShowMe()
        {
            if (active == null) return;

            if (active.card.HasDemo && image != null)
            {
                StopFlipbook();
                flipbook = StartCoroutine(PlayFlipbook(active.card));
            }

            active.onShowMe?.Invoke();
        }

        private void Skip()
        {
            if (active == null) return;
            Close(byLearner: true, markSeen: false);
            OnSkipRequested?.Invoke();
        }

        private IEnumerator PlayFlipbook(TutorialCardData card)
        {
            image.gameObject.SetActive(true);
            float wait = 1f / Mathf.Max(1f, card.demoFramesPerSecond);

            // Twice through, then back to the still - a loop that never ends pulls the eye away from
            // the Try It line, which is the point of the card.
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (Sprite frame in card.demoFrames)
                {
                    image.sprite = frame;
                    yield return new WaitForSecondsRealtime(wait);
                }
            }

            image.sprite = card.image != null ? card.image : card.demoFrames[0];
            flipbook = null;
        }

        private void StopFlipbook()
        {
            if (flipbook != null) StopCoroutine(flipbook);
            flipbook = null;
        }
    }
}
