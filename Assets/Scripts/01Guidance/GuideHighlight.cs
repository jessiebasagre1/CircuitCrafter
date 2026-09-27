using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CircuitCrafter.Guidance
{
    /// <summary>
    /// A soft pulsing glow on the object the learner needs next, with an optional tooltip label -
    /// "Side panel screws", "DIMM slot 1", "Network adapter settings". Works on 3D objects (pulses the
    /// renderers' colour and emission) and on UI (a pulsing frame over the button).
    ///
    /// USUALLY YOU DO NOT ADD THIS BY HAND. StoryGuide puts it on each step's Highlight objects while
    /// the step is active and takes it off after. Add it yourself only for a highlight you want to
    /// drive from a UnityEvent: tick Start On, or call SetOn(true) / Pulse().
    ///
    /// Story only by default: it checks GuidancePolicy (ObjectHighlights) every frame, so an
    /// assessment switches every glow off even if something forgot to.
    ///
    /// For the glow (not just the tint) on URP Lit materials, run CircuitCrafter > Prepare Materials
    /// For Glow once - the same step DisassemblyHintGlow needs. Without it the object still tints.
    /// </summary>
    [DisallowMultipleComponent]
    public class GuideHighlight : MonoBehaviour
    {
        [Tooltip("What kind of help this is. ObjectHighlights is Story-only in the default settings.")]
        public GuidanceKind kind = GuidanceKind.ObjectHighlights;

        public Color color = new Color32(0xFF, 0xC9, 0x3C, 0xFF);

        [Tooltip("Pulses per second.")]
        [Min(0.1f)] public float pulseSpeed = 1.4f;

        [Tooltip("How far the object's colour is pushed toward the highlight colour at the peak.")]
        [Range(0f, 1f)] public float tintStrength = 0.35f;

        [Tooltip("Emission brightness at the peak (needs the material's emission switched on).")]
        [Range(0f, 4f)] public float emissionStrength = 1.2f;

        [Tooltip("Tooltip shown beside the object while highlighted. Leave blank for none.")]
        public string label = "";

        [Tooltip("Switch on as soon as the scene starts.")]
        public bool startOn = false;

        public bool IsOn { get; private set; }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private Renderer[] renderers;
        private MaterialPropertyBlock block;
        private Image uiFrame;
        private bool isUi;
        private bool visualOn;
        private float pulseUntil = -1f;

        // ==================================================================

        /// <summary>Highlights an object, adding the component if it has none. Returns it.</summary>
        public static GuideHighlight Show(GameObject target, string tooltip = null)
        {
            if (target == null) return null;
            GuideHighlight h = target.GetComponent<GuideHighlight>();
            if (h == null) h = target.AddComponent<GuideHighlight>();
            if (tooltip != null) h.label = tooltip;
            h.SetOn(true);
            return h;
        }

        public static void Hide(GameObject target)
        {
            if (target == null) return;
            GuideHighlight h = target.GetComponent<GuideHighlight>();
            if (h != null) h.SetOn(false);
        }

        private void Awake()
        {
            isUi = GetComponent<RectTransform>() != null && GetComponentInParent<Canvas>() != null;
            if (!isUi)
            {
                var found = new List<Renderer>();
                foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer) continue;
                    found.Add(r);
                }
                renderers = found.ToArray();
                block = new MaterialPropertyBlock();
            }
        }

        private void Start()
        {
            if (startOn) SetOn(true);
        }

        private void OnDisable()
        {
            ApplyVisual(false, 0f);
            if (GuideMarkerOverlay.Instance != null) GuideMarkerOverlay.Instance.Untrack(this);
        }

        public void SetOn(bool on)
        {
            IsOn = on;
            if (!on) pulseUntil = -1f;
            if (!on) ApplyVisual(false, 0f);

            if (GuideMarkerOverlay.Instance != null)
            {
                if (on && !string.IsNullOrEmpty(label)) GuideMarkerOverlay.Instance.Track(this);
                else GuideMarkerOverlay.Instance.Untrack(this);
            }
        }

        /// <summary>Glow for a few seconds, then stop - for a demonstration or a Show Me button.</summary>
        public void Pulse(float seconds)
        {
            pulseUntil = Time.unscaledTime + Mathf.Max(0.5f, seconds);
        }

        /// <summary>UnityEvent-friendly: three seconds.</summary>
        public void Pulse() => Pulse(3f);

        private void Update()
        {
            bool pulsing = pulseUntil > 0f && Time.unscaledTime < pulseUntil;
            bool wanted = (IsOn || pulsing) && GuidancePolicy.Allows(kind);

            if (!wanted)
            {
                if (visualOn) ApplyVisual(false, 0f);
                return;
            }

            float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * pulseSpeed * Mathf.PI * 2f);
            ApplyVisual(true, t);
        }

        private void ApplyVisual(bool on, float t)
        {
            visualOn = on;

            if (isUi)
            {
                if (on && uiFrame == null) BuildUiFrame();
                if (uiFrame != null)
                {
                    uiFrame.enabled = on;
                    Color c = color;
                    c.a = Mathf.Lerp(0.12f, 0.45f, t);
                    uiFrame.color = c;
                }
                return;
            }

            if (renderers == null) return;

            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                Material m = r.sharedMaterial;
                if (m == null) continue;

                r.GetPropertyBlock(block);

                if (m.HasProperty(BaseColorId))
                {
                    Color baseC = m.GetColor(BaseColorId);
                    block.SetColor(BaseColorId, on ? Color.Lerp(baseC, color, tintStrength * t) : baseC);
                }
                else if (m.HasProperty(ColorId))
                {
                    Color baseC = m.GetColor(ColorId);
                    block.SetColor(ColorId, on ? Color.Lerp(baseC, color, tintStrength * t) : baseC);
                }

                if (m.HasProperty(EmissionId))
                {
                    Color baseE = m.GetColor(EmissionId);
                    block.SetColor(EmissionId, on ? baseE + color * (emissionStrength * t) : baseE);
                }

                r.SetPropertyBlock(block);
            }
        }

        private void BuildUiFrame()
        {
            var go = new GameObject("GuideHighlightFrame", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(-6f, -6f);
            rect.offsetMax = new Vector2(6f, 6f);
            rect.SetAsLastSibling();

            go.GetComponent<LayoutElement>().ignoreLayout = true;

            uiFrame = go.GetComponent<Image>();
            uiFrame.raycastTarget = false;   // the button underneath must still take the click
        }
    }
}
