using UnityEngine;
using TMPro;

namespace CSSSimulation.WindowsSim
{
    /// <summary>
    /// Grows the instruction banner to fit whatever text is in it.
    ///
    /// ATTACH THIS TO: COC3Desktop ▸ DesignRoot ▸ InstructionBanner. The desktop builder adds it.
    ///
    /// WHY NOT A ContentSizeFitter
    /// Because a LayoutGroup or a ContentSizeFitter takes over the RectTransform of everything it
    /// touches, and this project has already lost a day to exactly that - a fitter on the COC 1
    /// desktop panel silently overrode every anchor that had been set by hand. The banner sits
    /// inside a fixed 1600x900 design space with absolute placement everywhere else; introducing
    /// auto-layout into that is inviting the same problem back.
    ///
    /// This asks TextMeshPro one question - how tall would this text be at this width - and sets
    /// one number. Nothing else on the object is touched.
    ///
    /// LateUpdate, because COC3StepGate writes the text during Update. Measuring first would size
    /// the banner to the PREVIOUS step's instruction, which is the kind of one-frame error that
    /// only shows up as an occasional flicker and takes an hour to find.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class WinBannerFit : MonoBehaviour
    {
        [TooltipAttribute("The label inside the banner. Left empty, the first one found is used.")]
        public TMP_Text text;

        [HeaderAttribute("Padding")]
        public float paddingTop = 10f;
        public float paddingBottom = 12f;

        [TooltipAttribute("Left and right inset of the text, used when measuring. Must match what " +
                 "the label is actually inset by, or the measurement is of the wrong width.")]
        public float paddingSides = 28f;

        [HeaderAttribute("Limits")]
        [TooltipAttribute("Never shorter than this, so a one-line instruction still reads as a banner " +
                 "rather than a stripe.")]
        public float minHeight = 96f;

        [TooltipAttribute("Never taller than this. A banner that ate half the desktop would hide the " +
                 "windows the instruction is telling you to use.")]
        public float maxHeight = 260f;

        [TooltipAttribute("If the text is still too tall at Max Height, shrink the font to fit rather " +
                 "than letting it spill out of the bottom.")]
        public bool shrinkTextToFit = true;

        public float minFontSize = 12f;

        private RectTransform self;
        private float baseFontSize;

        // ------------------------------------------------------------------

        private void Awake()
        {
            self = GetComponent<RectTransform>();
            if (text == null) text = GetComponentInChildren<TMP_Text>(true);
            if (text != null) baseFontSize = text.fontSize;
        }

        private void LateUpdate()
        {
            if (self == null || text == null) return;

            float available = self.rect.width - paddingSides * 2f;
            if (available <= 1f) return;

            // Measured explicitly at the width the label actually has, rather than read from
            // preferredHeight - that reports against the label's CURRENT rect, which is the one
            // about to be changed.
            float needed = text.GetPreferredValues(text.text, available, 0f).y;
            float target = Mathf.Clamp(needed + paddingTop + paddingBottom, minHeight, maxHeight);

            if (!Mathf.Approximately(self.sizeDelta.y, target))
                self.sizeDelta = new Vector2(self.sizeDelta.x, target);

            if (!shrinkTextToFit) return;

            // Only the longest instructions ever reach this. Auto-sizing is left OFF the rest of
            // the time so every other step's banner is the same size as its neighbours.
            bool overflowing = needed + paddingTop + paddingBottom > maxHeight;

            if (overflowing != text.enableAutoSizing)
            {
                text.enableAutoSizing = overflowing;

                if (overflowing)
                {
                    text.fontSizeMin = minFontSize;
                    text.fontSizeMax = baseFontSize;
                }
                else
                {
                    text.fontSize = baseFontSize;
                }
            }
        }
    }
}
