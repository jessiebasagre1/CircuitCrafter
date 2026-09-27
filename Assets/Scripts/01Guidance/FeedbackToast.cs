using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CircuitCrafter.Guidance
{
    /// <summary>
    /// The small non-blocking card that CoachFeedback shows. It never stops the player, never needs
    /// the cursor, and goes away by itself.
    ///
    /// ATTACH THIS TO: the FeedbackToast object on the Guidance canvas. The builder
    /// (CircuitCrafter > Guidance > Build Guidance UI) makes it and fills every field.
    ///
    /// Up to three wait in line; a fourth replaces the oldest waiting one, so a burst of mistakes
    /// cannot bury the learner in a minute of cards.
    /// </summary>
    public class FeedbackToast : MonoBehaviour
    {
        public enum Tone { Neutral, Mistake, Success }

        public static FeedbackToast Instance { get; private set; }

        [Header("UI")]
        [Tooltip("The card itself. Starts hidden.")]
        public GameObject panel;
        public TMP_Text titleText;
        public TMP_Text bodyText;

        [Tooltip("The 'How to fix' line. Hidden when there is no fix.")]
        public TMP_Text fixText;

        [Tooltip("The coloured strip down the card's left edge.")]
        public Image accent;

        [Tooltip("Optional. Fades the card in and out.")]
        public CanvasGroup canvasGroup;

        [Header("Colours")]
        public Color neutralColor = new Color32(0x5F, 0xD4, 0xFF, 0xFF);
        public Color mistakeColor = new Color32(0xFF, 0x8A, 0x6B, 0xFF);
        public Color successColor = new Color32(0x3A, 0xDB, 0x76, 0xFF);

        [Header("Behaviour")]
        [Min(0f)] public float fadeSeconds = 0.2f;

        [Tooltip("Prefix for the fix line.")]
        public string fixPrefix = "How to fix: ";

        private struct Item
        {
            public Tone tone;
            public string title, body, fix;
            public float seconds;
            public int requestedFrame;
        }

        private readonly Queue<Item> waiting = new Queue<Item>();
        private float hideAt;
        private float shownAt;
        private bool showing;

        private void Awake()
        {
            Instance = this;
            if (panel != null) panel.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Queues a card. It appears on the NEXT frame, not this one, on purpose: gameplay code often
        /// reports a mistake and then opens its own explanation dialog on the following line. Waiting
        /// a frame lets the toast see that dialog and stand aside instead of repeating it underneath.
        /// </summary>
        public void Show(Tone tone, string title, string body, string fix, float seconds)
        {
            var item = new Item
            {
                tone = tone, title = title, body = body, fix = fix,
                seconds = Mathf.Max(1f, seconds), requestedFrame = Time.frameCount
            };

            if (waiting.Count >= 3) waiting.Dequeue();
            waiting.Enqueue(item);
        }

        public void HideNow()
        {
            waiting.Clear();
            showing = false;
            if (panel != null) panel.SetActive(false);
        }

        private void Present(Item item)
        {
            showing = true;
            shownAt = Time.unscaledTime;
            hideAt = shownAt + item.seconds;

            if (titleText != null)
            {
                titleText.text = item.title ?? "";
                titleText.gameObject.SetActive(!string.IsNullOrEmpty(item.title));
            }

            if (bodyText != null) bodyText.text = item.body ?? "";

            if (fixText != null)
            {
                bool hasFix = !string.IsNullOrEmpty(item.fix);
                fixText.gameObject.SetActive(hasFix);
                if (hasFix) fixText.text = fixPrefix + item.fix;
            }

            Color c = item.tone == Tone.Mistake ? mistakeColor : item.tone == Tone.Success ? successColor : neutralColor;
            if (accent != null) accent.color = c;
            if (titleText != null) titleText.color = c;

            if (panel != null) panel.SetActive(true);
            // No sound of its own: the gameplay code that refused the action has already played
            // SfxPlayer.Blocked(), and a second blip on top reads as two mistakes.
            if (canvasGroup != null) canvasGroup.alpha = fadeSeconds > 0f ? 0f : 1f;
        }

        private void Update()
        {
            if (!showing)
            {
                if (waiting.Count > 0) PresentNext();
                return;
            }

            float now = Time.unscaledTime;

            if (canvasGroup != null && fadeSeconds > 0f)
            {
                float fadeIn = Mathf.Clamp01((now - shownAt) / fadeSeconds);
                float fadeOut = Mathf.Clamp01((hideAt - now) / fadeSeconds);
                canvasGroup.alpha = Mathf.Min(fadeIn, fadeOut);
            }

            if (now < hideAt) return;

            showing = false;
            if (panel != null) panel.SetActive(false);
            if (waiting.Count > 0) PresentNext();
        }

        private void PresentNext()
        {
            while (waiting.Count > 0)
            {
                Item item = waiting.Dequeue();

                // A mistake whose own dialog opened alongside it has already been explained.
                bool dialogOpen = DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen;
                if (item.tone == Tone.Mistake && dialogOpen && Time.frameCount - item.requestedFrame <= 2) continue;

                Present(item);
                return;
            }
        }
    }
}
