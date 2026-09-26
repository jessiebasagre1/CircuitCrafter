using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A TEMPORARY, READ-ONLY PROBE. It finds the input bar that will not go away and prints exactly
/// what it is and where it lives. It changes nothing, moves nothing and destroys nothing - every
/// line below either reads a value or writes to the Console.
///
/// THERE IS NOTHING TO ATTACH. Drop the file in, press Play, and read the Console.
///
/// WHY THIS EXISTS RATHER THAN ANOTHER GUESS
/// The bar has now survived deleting two scripts and fixing a third, and it is not in the
/// Hierarchy under the open scene. Every remaining explanation is a guess, and guesses have cost
/// enough already. This asks Unity directly: every input field and every Done/Send button that is
/// actually on screen, with its full path, the scene it belongs to, the canvas that draws it and
/// that canvas's sorting order.
///
/// Objects created with DontDestroyOnLoad live in a scene of their own called "DontDestroyOnLoad",
/// which is why something can be on screen in every scene and impossible to find in the Hierarchy
/// while looking at the open one. The scene name in the report below is the line that settles it.
///
/// DELETE THIS FILE once the bar is gone. It is a diagnostic, not a feature.
/// </summary>
public class FindThatBar : MonoBehaviour
{
    private float nextReportTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        var go = new GameObject("~FindThatBar");
        go.hideFlags = HideFlags.DontSave;
        go.AddComponent<FindThatBar>();
        DontDestroyOnLoad(go);

        Debug.Log("[FindThatBar] Installed. A report follows in one second, and again every five " +
                  "seconds after that. Copy the whole thing.");
    }

    private void Update()
    {
        if (Time.unscaledTime < nextReportTime) return;

        nextReportTime = Time.unscaledTime < 1f
            ? 1f                       // first report a second in, once the UI has settled
            : Time.unscaledTime + 5f;

        if (Time.unscaledTime < 1f) return;

        Report();
    }

    [ContextMenu("Report now")]
    public void Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine("[FindThatBar] REPORT");
        sb.AppendLine($"  Screen {Screen.width} x {Screen.height}, " +
                      $"keyboard visible: {TouchScreenKeyboard.visible}, " +
                      $"reported area: {TouchScreenKeyboard.area}, " +
                      $"mobile platform: {Application.isMobilePlatform}");

        sb.AppendLine();
        sb.AppendLine("  INPUT FIELDS ON SCREEN");

        int found = 0;

        var tmpFields = Object.FindObjectsByType<TMP_InputField>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (var field in tmpFields)
        {
            found++;
            Describe(sb, field.gameObject, "TMP_InputField");
        }

        var legacyFields = Object.FindObjectsByType<InputField>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (var field in legacyFields)
        {
            found++;
            Describe(sb, field.gameObject, "InputField");
        }

        if (found == 0)
        {
            // The most useful possible outcome, and the reason this line is here rather than an
            // empty section: no input field on screen while a bar with a text box IS on screen
            // means the bar is not drawn by this game at all. It would then be the Editor's own
            // overlay, and switching the build target off Android makes it vanish.
            sb.AppendLine("  (none - no input field of either kind is active anywhere)");
        }

        sb.AppendLine();
        sb.AppendLine("  BUTTONS LABELLED Done / Send / OK");

        int buttons = 0;
        var allButtons = Object.FindObjectsByType<Button>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (var button in allButtons)
        {
            string label = LabelOf(button);
            if (string.IsNullOrEmpty(label)) continue;

            string trimmed = label.Trim();
            if (!trimmed.Equals("Done", System.StringComparison.OrdinalIgnoreCase) &&
                !trimmed.Equals("Send", System.StringComparison.OrdinalIgnoreCase) &&
                !trimmed.Equals("OK", System.StringComparison.OrdinalIgnoreCase)) continue;

            buttons++;
            Describe(sb, button.gameObject, $"Button \"{trimmed}\"");
        }

        if (buttons == 0) sb.AppendLine("  (none)");

        Debug.Log(sb.ToString());
    }

    // ==================================================================

    private static void Describe(StringBuilder sb, GameObject go, string what)
    {
        Canvas canvas = go.GetComponentInParent<Canvas>();
        Canvas root = canvas != null && canvas.rootCanvas != null ? canvas.rootCanvas : canvas;

        var rect = go.transform as RectTransform;
        string where = "no RectTransform";

        if (rect != null && root != null)
        {
            Camera cam = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;

            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);

            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            Vector2 topRight = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);

            where = $"x {bottomLeft.x:0}..{topRight.x:0}, y {bottomLeft.y:0}..{topRight.y:0} on screen";
        }

        sb.AppendLine($"  - {what}");
        sb.AppendLine($"      path   : {PathOf(go.transform)}");

        // THE LINE THAT MATTERS. "DontDestroyOnLoad" here means the object is in the Hierarchy's
        // own section at the very bottom, below every open scene - which is why it appears in every
        // scene and cannot be found by looking at the one that is open.
        sb.AppendLine($"      scene  : {go.scene.name}");

        sb.AppendLine($"      canvas : {(root != null ? root.name : "none")}" +
                      (root != null ? $"  (sorting order {root.sortingOrder}, {root.renderMode})" : ""));
        sb.AppendLine($"      rect   : {where}");
    }

    private static string LabelOf(Button button)
    {
        var tmp = button.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null && !string.IsNullOrEmpty(tmp.text)) return tmp.text;

        var legacy = button.GetComponentInChildren<Text>(true);
        return legacy != null ? legacy.text : null;
    }

    private static string PathOf(Transform target)
    {
        var sb = new StringBuilder(target.name);

        Transform walk = target.parent;
        while (walk != null)
        {
            sb.Insert(0, walk.name + " / ");
            walk = walk.parent;
        }

        return sb.ToString();
    }
}
