using UnityEngine;

namespace CSSSimulation.WindowsSim
{
    /// <summary>
    /// Scales a fixed-size design area to fit its parent, keeping the aspect ratio.
    ///
    /// ATTACH THIS TO: the DesignRoot the desktop builder creates. You should not need to add it
    /// by hand.
    ///
    /// WHY IT IS NEEDED
    /// Every window, icon and taskbar in the simulated desktop is placed in absolute pixels
    /// against a 1600 x 900 design space, because that is the only way dialog layouts stay
    /// readable - a Windows property sheet that reflows is not a Windows property sheet.
    ///
    /// A plain stretch to the parent does NOT scale those contents. It resizes the container and
    /// leaves everything inside it at its authored pixel size, anchored top-left. On a canvas that
    /// is not exactly 1600 x 900 the result is a desktop that fills part of the screen and spills
    /// off the rest - which is what "the panel is not stretched" looks like.
    ///
    /// This scales instead of stretching, so the whole desktop grows and shrinks as one piece and
    /// the proportions the dialogs were drawn at survive.
    ///
    /// Uniform, not per-axis: scaling x and y differently would make circles into ovals and text
    /// into something subtly wrong that you feel before you can name.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    public class UniformScaleToFit : MonoBehaviour
    {
        [Tooltip("The size everything inside was authored at. Must match the builder's DW x DH.")]
        public Vector2 designSize = new Vector2(1600f, 900f);

        [Tooltip("Never scale above this. 1 keeps the desktop pixel-exact on a large canvas " +
                 "rather than letting it blow up and go soft; raise it to fill a big screen.")]
        public float maxScale = 4f;

        private RectTransform self;

        private void OnEnable()
        {
            self = GetComponent<RectTransform>();
            Apply();
        }

        private void Update()
        {
            // Cheap, and the alternative is missing a resolution change, a Canvas Scaler
            // recalculation, or the Game view being resized while you work.
            Apply();
        }

        private void Apply()
        {
            if (self == null) self = GetComponent<RectTransform>();

            var parent = self.parent as RectTransform;
            if (parent == null) return;

            if (designSize.x <= 0f || designSize.y <= 0f) return;

            // Keep the authored size; only the scale changes.
            if (self.sizeDelta != designSize)
            {
                self.anchorMin = self.anchorMax = self.pivot = new Vector2(0.5f, 0.5f);
                self.anchoredPosition = Vector2.zero;
                self.sizeDelta = designSize;
            }

            Rect area = parent.rect;
            if (area.width <= 0f || area.height <= 0f) return;

            float scale = Mathf.Min(area.width / designSize.x, area.height / designSize.y);
            scale = Mathf.Clamp(scale, 0.01f, Mathf.Max(0.01f, maxScale));

            if (!Mathf.Approximately(self.localScale.x, scale))
                self.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
