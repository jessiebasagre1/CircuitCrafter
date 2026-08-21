using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// TEMPORARY TROUBLESHOOTING SCRIPT - delete it once COC3 runs.
///
/// Drop this on the same GameObject as your COC3Manager and press Play.
/// It waits one frame (so COC3Manager.Start() has already done its work), then
/// dumps everything that decides whether step 1 is actually visible on screen.
///
/// A blank screen has only a handful of possible causes, and this separates them:
///
///   A. COC3Manager never ran        -> its GameObject is inactive, or the
///                                      component is unticked
///   B. steps list is empty          -> the builder's result was not saved, or
///                                      the wrong parent was assigned
///   C. step 1 is inactive           -> a disabled PARENT is blocking it
///   D. step 1 is active but unseen  -> CanvasGroup alpha, zero size, off-screen,
///                                      or the Canvas itself cannot render
///
/// Read the CONCLUSION line at the bottom of the log first.
/// </summary>
public class COC3Diagnostics : MonoBehaviour
{
    [Tooltip("Leave empty to use the COC3Manager on this same GameObject.")]
    public COC3Manager manager;

    private IEnumerator Start()
    {
        // Wait a frame so COC3Manager.Start() has finished showing step 1.
        yield return null;
        Debug.Log(BuildReport());
    }

    private string BuildReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine("========== COC3 DIAGNOSTICS ==========");

        if (manager == null) manager = GetComponent<COC3Manager>();

        // ---- A. did the manager run at all? --------------------------------
        if (manager == null)
        {
            sb.AppendLine("CONCLUSION: No COC3Manager found. Put this script on the same");
            sb.AppendLine("            GameObject as COC3Manager, or drag it into the field.");
            return sb.ToString();
        }

        sb.AppendLine($"COC3Manager           : '{manager.name}'");
        sb.AppendLine($"  GameObject active   : {manager.gameObject.activeInHierarchy}");
        sb.AppendLine($"  Component enabled   : {manager.enabled}");
        sb.AppendLine($"  steps.Count         : {(manager.steps == null ? "NULL" : manager.steps.Count.ToString())}");
        sb.AppendLine($"  completionScreen    : {Describe(manager.completionScreen)}");

        if (!manager.gameObject.activeInHierarchy || !manager.enabled)
        {
            sb.AppendLine();
            sb.AppendLine("CONCLUSION (A): COC3Manager never ran, so nothing was ever shown.");
            sb.AppendLine("            Tick its GameObject active in the Hierarchy and make sure");
            sb.AppendLine("            the COC3Manager component's own checkbox is ticked.");
            return sb.ToString();
        }

        // ---- B. is the steps list populated? -------------------------------
        if (manager.steps == null || manager.steps.Count == 0)
        {
            sb.AppendLine();
            sb.AppendLine("CONCLUSION (B): The steps list is EMPTY, so COC3Manager jumped straight");
            sb.AppendLine("            to the completion screen (which is blank if unassigned).");
            sb.AppendLine("            Re-open Tools > COC3 > Scene Builder, check that 'Step Panels");
            sb.AppendLine("            Parent' points at the object whose DIRECT CHILDREN are the 30");
            sb.AppendLine("            panels, press BUILD EVERYTHING, then SAVE THE SCENE (Ctrl+S).");
            return sb.ToString();
        }

        var step = manager.steps[0];
        if (step == null)
        {
            sb.AppendLine();
            sb.AppendLine("CONCLUSION (B): steps[0] is empty (a missing reference).");
            sb.AppendLine("            Re-run the Scene Builder.");
            return sb.ToString();
        }

        // ---- C. is step 1 actually active? ---------------------------------
        var go = step.gameObject;
        var rt = go.transform as RectTransform;
        var cg = go.GetComponent<CanvasGroup>();

        sb.AppendLine();
        sb.AppendLine($"Step 1                : '{go.name}'");
        sb.AppendLine($"  activeSelf          : {go.activeSelf}");
        sb.AppendLine($"  activeInHierarchy   : {go.activeInHierarchy}");
        sb.AppendLine($"  CanvasGroup alpha   : {(cg == null ? "NO CANVASGROUP" : cg.alpha.ToString("0.00"))}");
        sb.AppendLine($"  stepData            : {(step.stepData == null ? "MISSING" : step.stepData.stepId)}");
        sb.AppendLine($"  instructionLabel    : {Describe(step.instructionLabel)}");
        sb.AppendLine($"  screenshotDisplay   : {Describe(step.screenshotDisplay)}");
        if (rt != null)
        {
            sb.AppendLine($"  rect size           : {rt.rect.width:0} x {rt.rect.height:0}");
            sb.AppendLine($"  world position      : {rt.position}");
            sb.AppendLine($"  local scale         : {rt.localScale}");
        }

        // Parent chain - names any ancestor that is switched off.
        sb.AppendLine();
        sb.AppendLine("Parent chain (step 1 upwards):");
        for (Transform t = go.transform; t != null; t = t.parent)
            sb.AppendLine($"  {(t.gameObject.activeSelf ? "[on ]" : "[OFF]")} {t.name}");

        // ---- D. can the Canvas render at all? ------------------------------
        var canvas = go.GetComponentInParent<Canvas>();
        sb.AppendLine();
        if (canvas == null)
        {
            sb.AppendLine("Canvas                : NONE FOUND ABOVE THIS PANEL");
        }
        else
        {
            var root = canvas.rootCanvas;
            sb.AppendLine($"Canvas                : '{root.name}'");
            sb.AppendLine($"  enabled             : {root.enabled}");
            sb.AppendLine($"  renderMode          : {root.renderMode}");
            sb.AppendLine($"  worldCamera         : {Describe(root.worldCamera)}");
            sb.AppendLine($"  sortingOrder        : {root.sortingOrder}");
            sb.AppendLine($"  GraphicRaycaster    : {Describe(root.GetComponent<GraphicRaycaster>())}");
        }

        bool hasEventSystem = EventSystem.current != null;
        sb.AppendLine($"EventSystem in scene  : {(hasEventSystem ? "yes" : "NO - buttons will never respond")}");

        // ---- conclusion ----------------------------------------------------
        sb.AppendLine();
        var blocker = DisabledAncestor(go);

        if (blocker != null)
        {
            sb.AppendLine($"CONCLUSION (C): step 1 cannot appear because its parent '{blocker}' is");
            sb.AppendLine("            switched OFF. Show() only activates the panel itself, never its");
            sb.AppendLine($"            ancestors. Tick '{blocker}' active in the Hierarchy.");
        }
        else if (!go.activeSelf)
        {
            sb.AppendLine("CONCLUSION: step 1 is still inactive after COC3Manager ran, which should");
            sb.AppendLine("            be impossible. Check that nothing else in the scene switches");
            sb.AppendLine("            these panels off (an old menu/tutorial script, for example).");
        }
        else if (cg != null && cg.alpha < 0.99f)
        {
            sb.AppendLine($"CONCLUSION (D): step 1 is active but its CanvasGroup alpha is {cg.alpha:0.00}.");
            sb.AppendLine("            Something is fading it out - look for another script or an");
            sb.AppendLine("            Animator driving this CanvasGroup.");
        }
        else if (rt != null && (rt.rect.width < 1f || rt.rect.height < 1f))
        {
            sb.AppendLine("CONCLUSION (D): step 1 is active and visible, but its RectTransform is");
            sb.AppendLine("            effectively zero-sized, so there is nothing to see. Set its");
            sb.AppendLine("            anchors to stretch (Alt+click the bottom-right stretch preset).");
        }
        else if (canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                                && canvas.rootCanvas.worldCamera == null)
        {
            sb.AppendLine("CONCLUSION (D): the Canvas is set to Screen Space - Camera (or World Space)");
            sb.AppendLine("            but has NO camera assigned, so it renders nothing at all.");
            sb.AppendLine("            Assign your camera, or switch it to Screen Space - Overlay.");
        }
        else if (canvas != null && !canvas.rootCanvas.enabled)
        {
            sb.AppendLine("CONCLUSION (D): the root Canvas component is unticked. Enable it.");
        }
        else
        {
            sb.AppendLine("CONCLUSION: step 1 looks correct - active, opaque, sized, on a live Canvas.");
            sb.AppendLine("            If the screen is still blank, something is drawing OVER it.");
            sb.AppendLine("            Check for another full-screen panel with a higher sortingOrder,");
            sb.AppendLine("            or a panel later in the Hierarchy under the same Canvas.");
        }

        sb.AppendLine("======================================");
        return sb.ToString();
    }

    private static string DisabledAncestor(GameObject go)
    {
        for (Transform t = go.transform.parent; t != null; t = t.parent)
            if (!t.gameObject.activeSelf) return t.name;
        return null;
    }

    private static string Describe(Object o) => o == null ? "NOT ASSIGNED" : $"'{o.name}'";
}
