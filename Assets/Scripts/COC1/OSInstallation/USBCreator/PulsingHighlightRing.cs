using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple pulsing ring/glow used to draw the learner's eye to the currently active
/// ImageHotspotButton. Attach this to a prefab consisting of a circular/rounded-rect
/// Image (a ring sprite with a transparent center works best) and assign that prefab
/// to RufusStyleUSBCreatorView.highlightRingPrefab.
///
/// The ring is instantiated as a child of the active hotspot and stretches to fill it
/// (see RufusStyleUSBCreatorView.HighlightCurrentStep), so size it generously — a ring
/// a bit larger than the button itself reads better than one that hugs it tightly.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class PulsingHighlightRing : MonoBehaviour
{
    [Header("Pulse animation")]
    [Tooltip("How much the ring scales up and down, e.g. 1.0 to 1.15.")]
    [SerializeField] private float minScale = 1.0f;
    [SerializeField] private float maxScale = 1.18f;
    [SerializeField] private float pulseSpeed = 1.6f;

    [Header("Fade animation")]
    [SerializeField] private float minAlpha = 0.35f;
    [SerializeField] private float maxAlpha = 0.9f;

    [Header("Color")]
    [SerializeField] private Color ringColor = new Color(1f, 0.85f, 0.1f, 1f); // amber, reads well on screenshots

    private RectTransform _rect;
    private Graphic _graphic; // works with Image or other UI Graphic

    private void Awake()
    {
        _rect = GetComponent<RectTransform>();
        _graphic = GetComponent<Graphic>();
        if (_graphic != null)
        {
            var c = ringColor;
            _graphic.color = c;
        }

        // Rings shouldn't block clicks on the real hotspot underneath.
        if (_graphic != null) _graphic.raycastTarget = false;
    }

    private void Update()
    {
        float t = (Mathf.Sin(Time.unscaledTime * pulseSpeed * Mathf.PI * 2f) + 1f) * 0.5f;

        float scale = Mathf.Lerp(minScale, maxScale, t);
        _rect.localScale = new Vector3(scale, scale, 1f);

        if (_graphic != null)
        {
            float alpha = Mathf.Lerp(minAlpha, maxAlpha, t);
            var c = _graphic.color;
            c.a = alpha;
            _graphic.color = c;
        }
    }
}