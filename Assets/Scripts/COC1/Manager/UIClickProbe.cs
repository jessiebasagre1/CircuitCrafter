using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Temporary diagnostic. Reports why a UI control is not receiving clicks.
///
/// ATTACH THIS TO: any always-active object. It announces itself in Start(), so if you do not
/// see that line the component is not running and nothing else it says matters.
///
/// EVERYTHING IS LOGGED AS A WARNING, ON PURPOSE. The Console's info filter is off often
/// enough that a silent probe usually means a hidden log rather than a dead script.
///
/// WHAT IT ANSWERS
/// A Graphic is skipped by GraphicRaycaster for exactly five reasons:
///     if (graphic.depth == -1 || !graphic.raycastTarget || graphic.canvasRenderer.cull) continue;
///     if (!RectangleContainsScreenPoint(...)) continue;
///     if (!graphic.Raycast(...)) continue;
/// plus not being active at all. This dumps all of them for the control you point it at, so the
/// cause is read off a list rather than guessed at.
/// </summary>
public class UIClickProbe : MonoBehaviour
{
    [Tooltip("The control that is not responding - e.g. BrowserPanel > Content > Hotspot.")]
    [SerializeField] private RectTransform expectedTarget;

    [Tooltip("Press this any time to dump the same report without needing a click to register.")]
    [SerializeField] private KeyCode manualKey = KeyCode.P;

    private readonly List<RaycastResult> results = new List<RaycastResult>();

    private void Start()
    {
        Debug.LogWarning($"[UIClickProbe] ACTIVE on '{Path(transform)}'. " +
                         $"Click in the Game view, or press {manualKey}, to dump a report. " +
                         $"Target: {(expectedTarget != null ? expectedTarget.name : "NOT ASSIGNED")}");
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(0) || Input.GetKeyDown(manualKey))
            Dump();
    }

    private void Dump()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== UIClickProbe ===");
        sb.AppendLine($"Cursor: lockState={Cursor.lockState}, visible={Cursor.visible}");
        sb.AppendLine($"Click/probe at: {Input.mousePosition}  (screen {Screen.width}x{Screen.height})");

        DescribeTarget(sb);
        DescribeRaycast(sb);

        Debug.LogWarning(sb.ToString());
    }

    /// <summary>
    /// The five conditions that make a Graphic invisible to the raycaster, plus the canvas it
    /// belongs to. One of these lines is the answer.
    /// </summary>
    private void DescribeTarget(StringBuilder sb)
    {
        if (expectedTarget == null)
        {
            sb.AppendLine("No Expected Target assigned - assign the dead control for the useful half.");
            return;
        }

        sb.AppendLine($"--- {Path(expectedTarget)} ---");
        sb.AppendLine($"  activeInHierarchy : {expectedTarget.gameObject.activeInHierarchy}");
        sb.AppendLine($"  screen rect       : {ScreenRectOf(expectedTarget)}");
        sb.AppendLine($"  localPosition.z   : {expectedTarget.localPosition.z}");
        sb.AppendLine($"  lossyScale        : {expectedTarget.lossyScale}");

        var graphic = expectedTarget.GetComponent<Graphic>();
        if (graphic == null)
        {
            sb.AppendLine("  NO GRAPHIC on this object - there is nothing for a raycast to hit. " +
                          "A Button needs an Image (or other Graphic) to be clickable.");
        }
        else
        {
            sb.AppendLine($"  graphic           : {graphic.GetType().Name}");
            sb.AppendLine($"  enabled           : {graphic.enabled}");
            sb.AppendLine($"  raycastTarget     : {graphic.raycastTarget}");
            sb.AppendLine($"  color.a           : {graphic.color.a}");
            sb.AppendLine($"  depth             : {graphic.depth}   (-1 means not batched, never hit)");

            var cr = expectedTarget.GetComponent<CanvasRenderer>();
            if (cr != null)
            {
                sb.AppendLine($"  canvasRenderer.cull            : {cr.cull}   (true = skipped)");
                sb.AppendLine($"  canvasRenderer.cullTransparentMesh : {cr.cullTransparentMesh}");
            }
        }

        var sel = expectedTarget.GetComponent<Selectable>();
        if (sel != null)
            sb.AppendLine($"  interactable      : {sel.interactable}");

        // A CanvasGroup anywhere up the chain can switch off raycasts for everything beneath it,
        // and it is easy to miss because it sits on a parent rather than on the dead control.
        var groups = expectedTarget.GetComponentsInParent<CanvasGroup>(true);
        foreach (var g in groups)
        {
            if (g == null) continue;
            sb.AppendLine($"  CanvasGroup on '{g.name}': blocksRaycasts={g.blocksRaycasts}, " +
                          $"interactable={g.interactable}, alpha={g.alpha}");
        }

        var canvas = expectedTarget.GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            var root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
            sb.AppendLine($"  canvas            : {root.name} [{root.renderMode}], " +
                          $"eventCamera={(root.worldCamera != null ? root.worldCamera.name : "NULL -> Camera.main")}");
            sb.AppendLine($"  Camera.main       : {(Camera.main != null ? Camera.main.name : "NULL")}");

            var caster = root.GetComponent<GraphicRaycaster>();
            sb.AppendLine(caster == null
                ? "  NO GraphicRaycaster on the root canvas."
                : $"  raycaster         : blocking={caster.blockingObjects}, " +
                  $"ignoreReversed={caster.ignoreReversedGraphics}");
        }
    }

    private void DescribeRaycast(StringBuilder sb)
    {
        if (EventSystem.current == null)
        {
            sb.AppendLine("NO EventSystem - nothing in any Canvas can be clicked.");
            return;
        }

        var data = new PointerEventData(EventSystem.current)
        {
            position = new Vector2(Input.mousePosition.x, Input.mousePosition.y)
        };

        results.Clear();
        EventSystem.current.RaycastAll(data, results);

        if (results.Count == 0)
        {
            sb.AppendLine("RAYCAST HIT NOTHING at this position.");
            return;
        }

        sb.AppendLine($"Raycast hit {results.Count}, front first - the FIRST one gets the click:");
        for (int i = 0; i < results.Count; i++)
            sb.AppendLine($"  {i + 1}. {Path(results[i].gameObject.transform)}");
    }

    private static string Path(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    private static string ScreenRectOf(RectTransform rt)
    {
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);

        var canvas = rt.GetComponentInParent<Canvas>();
        Camera cam = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = canvas.worldCamera != null ? canvas.worldCamera : Camera.main;

        Vector2 bl = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 tr = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);

        return $"x {bl.x:0}..{tr.x:0}, y {bl.y:0}..{tr.y:0}  (w {tr.x - bl.x:0}, h {tr.y - bl.y:0})";
    }
}