using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace CircuitCrafter.Guidance
{
    /// <summary>
    /// The contextual tooltip that floats beside a highlighted object - "RAM retention clips",
    /// "Rufus: Device". Follows the object on screen and hides when it is behind the camera.
    ///
    /// ATTACH THIS TO: the GuideMarker object on the Guidance canvas (the builder makes it). There is
    /// nothing to wire by hand: GuideHighlight registers itself here whenever it has a label.
    ///
    /// One tooltip at a time - the most recently highlighted object with a label. Two floating
    /// labels competing for attention teach less than one.
    /// </summary>
    public class GuideMarkerOverlay : MonoBehaviour
    {
        public static GuideMarkerOverlay Instance { get; private set; }

        [Tooltip("The tooltip root (icon + label). Moved every frame; hidden when nothing is tracked.")]
        public RectTransform marker;

        public TMP_Text labelText;

        [Tooltip("Screen pixels above the object's top edge.")]
        public float verticalOffset = 24f;

        [Tooltip("The camera looking at 3D targets. Empty = Camera.main.")]
        public Camera worldCamera;

        private readonly List<GuideHighlight> tracked = new List<GuideHighlight>();
        private Canvas canvas;
        private RectTransform canvasRect;

        private void Awake()
        {
            Instance = this;
            canvas = GetComponentInParent<Canvas>();
            if (canvas != null) canvasRect = canvas.transform as RectTransform;
            if (marker != null) marker.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Track(GuideHighlight h)
        {
            if (h == null) return;
            tracked.Remove(h);
            tracked.Add(h);
        }

        public void Untrack(GuideHighlight h)
        {
            tracked.Remove(h);
        }

        private void LateUpdate()
        {
            if (marker == null || canvasRect == null) return;

            GuideHighlight target = null;
            for (int i = tracked.Count - 1; i >= 0; i--)
            {
                GuideHighlight h = tracked[i];
                if (h == null || !h.isActiveAndEnabled || !h.IsOn) { tracked.RemoveAt(i); continue; }
                if (target == null && !string.IsNullOrEmpty(h.label) && GuidancePolicy.Allows(h.kind)) target = h;
            }

            Vector2 screen;
            if (target == null || !TryScreenPoint(target, out screen))
            {
                if (marker.gameObject.activeSelf) marker.gameObject.SetActive(false);
                return;
            }

            Camera uiCam = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen + Vector2.up * verticalOffset, uiCam, out local))
                return;

            if (!marker.gameObject.activeSelf) marker.gameObject.SetActive(true);
            if (labelText != null && labelText.text != target.label) labelText.text = target.label;
            marker.anchoredPosition = ClampInside(local);
        }

        private bool TryScreenPoint(GuideHighlight h, out Vector2 screen)
        {
            screen = Vector2.zero;
            RectTransform rt = h.transform as RectTransform;

            if (rt != null)
            {
                Canvas targetCanvas = h.GetComponentInParent<Canvas>();
                if (targetCanvas != null)
                {
                    Vector3[] corners = new Vector3[4];
                    rt.GetWorldCorners(corners);
                    Vector3 topCentre = (corners[1] + corners[2]) * 0.5f;
                    Camera cam = targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : targetCanvas.worldCamera;
                    screen = RectTransformUtility.WorldToScreenPoint(cam, topCentre);
                    return true;
                }
            }

            Camera world = worldCamera != null ? worldCamera : Camera.main;
            if (world == null) return false;

            Vector3 top = h.transform.position;
            Renderer r = h.GetComponentInChildren<Renderer>();
            if (r != null) top = r.bounds.center + Vector3.up * r.bounds.extents.y;

            Vector3 sp = world.WorldToScreenPoint(top);
            if (sp.z <= 0f) return false;   // behind the camera
            screen = sp;
            return true;
        }

        private Vector2 ClampInside(Vector2 local)
        {
            Rect r = canvasRect.rect;
            Vector2 half = marker.rect.size * 0.5f;
            local.x = Mathf.Clamp(local.x, r.xMin + half.x, r.xMax - half.x);
            local.y = Mathf.Clamp(local.y, r.yMin + half.y, r.yMax - half.y);
            return local;
        }
    }
}
