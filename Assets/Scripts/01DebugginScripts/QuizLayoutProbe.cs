using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Diagnostic for "the quiz layout is right when I play COC2 directly, and wrong when I reach COC2
/// through the Main Menu".
///
/// ATTACH THIS TO: the 'Managers' object in the COC2 scene, then drag the QuizCanvas object into
/// its Quiz Canvas field.
///
/// WHY THIS EXISTS
/// That difference is the useful fact in this whole bug. The COC2 scene is identical either way -
/// same prefab, same saved RectTransforms - so whatever moves the layout is something that is
/// DIFFERENT when the menu scene ran first. There is a short list of things that can do that, and
/// every one of them is a number this prints:
///
///   - the screen size the canvas is scaling against (a menu that calls Screen.SetResolution, or a
///     saved display setting applied on startup, changes CanvasScaler's answer for the rest of the
///     session - and CanvasScaler's scale factor is an input to every layout group under it)
///   - a second QuizCanvas or QuizView surviving from the menu scene on DontDestroyOnLoad, so the
///     one on screen is not the one you arranged
///   - the canvas's own scale factor and render mode
///   - a ContentSizeFitter on a choice label, which makes that label's height follow its text
///   - the layout group settings on the row container
///   - each label's rect size against the row it is supposed to sit in, which is the actual
///     symptom: the labels drift off their bars
///
/// HOW TO USE IT
///   1. Press Play ON THE COC2 SCENE. When the first quiz appears, press L. Copy the report.
///   2. Stop. Press Play on the MAIN MENU, go into COC2, reach the first quiz, press L again.
///   3. Send me both. The line that differs is the cause.
///
/// This writes nothing and changes nothing. Delete the component once the layout is right.
/// </summary>
public class QuizLayoutProbe : MonoBehaviour
{
    [Header("What to inspect")]
    [Tooltip("The QuizCanvas object.")]
    [SerializeField] private GameObject quizCanvasObject;

    [Header("Controls")]
    [SerializeField] private KeyCode reportKey = KeyCode.L;

    [Tooltip("Leave on. Also prints automatically the moment the quiz appears, so you get the " +
             "report even if you forget the key.")]
    [SerializeField] private bool reportWhenQuizOpens = true;

    private bool wasOpen;

    private void Update()
    {
        if (Input.GetKeyDown(reportKey)) Report("you pressed " + reportKey);

        if (reportWhenQuizOpens)
        {
            bool open = quizCanvasObject != null && quizCanvasObject.activeInHierarchy;
            if (open && !wasOpen) Report("the quiz just appeared");
            wasOpen = open;
        }
    }

    [ContextMenu("Report now")]
    private void ReportFromMenu() { Report("right-click menu"); }

    private void Report(string why)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"=== QuizLayoutProbe ({why}) ===");
        sb.AppendLine($"  active scene    : {SceneManager.GetActiveScene().name}");
        sb.AppendLine($"  scenes loaded   : {SceneManager.sceneCount}");

        ReportScreen(sb);
        ReportDuplicates(sb);

        if (quizCanvasObject == null)
        {
            sb.AppendLine("  NO QUIZ CANVAS ASSIGNED - drag QuizCanvas into this component's field.");
            Debug.LogWarning(sb.ToString(), this);
            return;
        }

        ReportCanvas(sb);
        ReportChoices(sb);

        Debug.LogWarning(sb.ToString(), this);
    }

    // ======================================================================

    /// <summary>
    /// The screen the canvas is scaling against.
    ///
    /// This is the first thing to compare between the two runs. CanvasScaler in Scale With Screen
    /// Size mode turns the real screen size into the canvas scale factor, and every layout group
    /// and every TMP preferred-size answer is measured in that scaled space. A menu scene that sets
    /// a resolution - from a saved settings file, say - changes this for the rest of the session,
    /// and nothing about it shows up in the Game view's own resolution dropdown.
    /// </summary>
    private void ReportScreen(StringBuilder sb)
    {
        sb.AppendLine("--- Screen ---");
        sb.AppendLine($"  Screen.width x height : {Screen.width} x {Screen.height}");
        sb.AppendLine($"  currentResolution     : {Screen.currentResolution.width} x {Screen.currentResolution.height}");
        sb.AppendLine($"  fullScreen            : {Screen.fullScreen}   dpi: {Screen.dpi}");
    }

    /// <summary>
    /// Is the canvas on screen the one you arranged?
    ///
    /// An object carried over from the menu scene on DontDestroyOnLoad shows its scene as
    /// 'DontDestroyOnLoad' rather than 'COC2-StoryMode'. If a second QuizView or QuizCanvas arrives
    /// that way, the quiz you see was never laid out in the COC2 scene at all - and that would
    /// explain the difference exactly, because playing COC2 directly means no menu scene ran and no
    /// second copy exists.
    /// </summary>
    private void ReportDuplicates(StringBuilder sb)
    {
        QuizView[] views = Object.FindObjectsByType<QuizView>(FindObjectsInactive.Include,
                                                             FindObjectsSortMode.None);
        sb.AppendLine($"--- QuizView components in memory: {views.Length} ---");
        foreach (QuizView v in views)
        {
            sb.AppendLine($"  '{PathOf(v.transform)}'   scene: {v.gameObject.scene.name}   " +
                          $"activeInHierarchy: {v.gameObject.activeInHierarchy}");
        }
        if (views.Length > 1)
            sb.AppendLine("  MORE THAN ONE. Whichever one QuizManager's Quiz View field points at is " +
                          "the one that runs - and it may not be the one you arranged.");

        QuizManager[] managers = Object.FindObjectsByType<QuizManager>(FindObjectsInactive.Include,
                                                                      FindObjectsSortMode.None);
        sb.AppendLine($"--- QuizManager components in memory: {managers.Length} ---");
        foreach (QuizManager m in managers)
        {
            sb.AppendLine($"  '{PathOf(m.transform)}'   scene: {m.gameObject.scene.name}" +
                          (m == QuizManager.Instance ? "   <-- Instance" : ""));
        }
    }

    private void ReportCanvas(StringBuilder sb)
    {
        Canvas canvas = quizCanvasObject.GetComponent<Canvas>();
        if (canvas == null) canvas = quizCanvasObject.GetComponentInParent<Canvas>();
        if (canvas == null) canvas = quizCanvasObject.GetComponentInChildren<Canvas>(true);

        if (canvas == null) { sb.AppendLine("--- No Canvas found on the assigned object ---"); return; }

        Canvas root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;

        sb.AppendLine($"--- Canvas '{PathOf(root.transform)}' ---");
        sb.AppendLine($"  scene           : {root.gameObject.scene.name}");
        sb.AppendLine($"  renderMode      : {root.renderMode}");
        sb.AppendLine($"  scaleFactor     : {root.scaleFactor}          <-- compare this between runs");
        sb.AppendLine($"  referencePixelsPerUnit : {root.referencePixelsPerUnit}");

        RectTransform crt = root.transform as RectTransform;
        if (crt != null)
            sb.AppendLine($"  canvas rect     : {crt.rect.width:0.#} x {crt.rect.height:0.#}   " +
                          $"lossyScale: {crt.lossyScale.x:0.####}");

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        if (scaler == null)
        {
            sb.AppendLine("  CanvasScaler    : NONE");
        }
        else
        {
            sb.AppendLine($"  CanvasScaler    : mode={scaler.uiScaleMode}");
            sb.AppendLine($"                    referenceResolution={scaler.referenceResolution.x} x {scaler.referenceResolution.y}");
            sb.AppendLine($"                    screenMatchMode={scaler.screenMatchMode}  match={scaler.matchWidthOrHeight}");
            sb.AppendLine($"                    scaleFactor field={scaler.scaleFactor}");
        }
    }

    /// <summary>
    /// The choice rows themselves - the part that actually looks wrong.
    ///
    /// For each label this prints its rect against the row that contains it. A label whose height
    /// does not match its row is the drift you can see on screen, and a ContentSizeFitter on the
    /// label is the usual reason for it.
    /// </summary>
    private void ReportChoices(StringBuilder sb)
    {
        Toggle[] toggles = quizCanvasObject.GetComponentsInChildren<Toggle>(true);
        sb.AppendLine($"--- Choice rows: {toggles.Length} ---");

        if (toggles.Length == 0) return;

        Transform container = toggles[0].transform.parent;
        if (container != null)
        {
            sb.AppendLine($"  container '{container.name}'");

            RectTransform crt = container as RectTransform;
            if (crt != null) sb.AppendLine($"     rect: {crt.rect.width:0.#} x {crt.rect.height:0.#}");

            HorizontalOrVerticalLayoutGroup lg = container.GetComponent<HorizontalOrVerticalLayoutGroup>();
            if (lg == null)
            {
                sb.AppendLine("     no layout group - the rows are positioned by hand");
            }
            else
            {
                sb.AppendLine($"     {lg.GetType().Name}: spacing={lg.spacing}  " +
                              $"childControlHeight={lg.childControlHeight}  " +
                              $"childForceExpandHeight={lg.childForceExpandHeight}  " +
                              $"childAlignment={lg.childAlignment}");
            }

            ContentSizeFitter cf = container.GetComponent<ContentSizeFitter>();
            if (cf != null)
                sb.AppendLine($"     ContentSizeFitter: horizontal={cf.horizontalFit}  vertical={cf.verticalFit}");
        }

        foreach (Toggle t in toggles)
        {
            RectTransform row = t.transform as RectTransform;
            sb.AppendLine($"  '{t.name}'  active={t.gameObject.activeInHierarchy}");
            if (row != null)
                sb.AppendLine($"     row rect   : {row.rect.width:0.#} x {row.rect.height:0.#}   " +
                              $"anchoredPos: {row.anchoredPosition.x:0.#}, {row.anchoredPosition.y:0.#}");

            ContentSizeFitter rowFit = t.GetComponent<ContentSizeFitter>();
            if (rowFit != null)
                sb.AppendLine($"     row ContentSizeFitter: vertical={rowFit.verticalFit}   <-- makes the row follow its text");

            TMP_Text label = t.GetComponentInChildren<TMP_Text>(true);
            if (label == null) { sb.AppendLine("     no TMP label found"); continue; }

            RectTransform lrt = label.rectTransform;
            sb.AppendLine($"     label '{label.name}': rect {lrt.rect.width:0.#} x {lrt.rect.height:0.#}   " +
                          $"anchoredPos: {lrt.anchoredPosition.x:0.#}, {lrt.anchoredPosition.y:0.#}");
            sb.AppendLine($"        anchors min({lrt.anchorMin.x:0.##},{lrt.anchorMin.y:0.##}) " +
                          $"max({lrt.anchorMax.x:0.##},{lrt.anchorMax.y:0.##})   " +
                          $"pivot({lrt.pivot.x:0.##},{lrt.pivot.y:0.##})");
            sb.AppendLine($"        alignment={label.alignment}  fontSize={label.fontSize:0.#}  " +
                          $"autoSize={label.enableAutoSizing} ({label.fontSizeMin:0.#}-{label.fontSizeMax:0.#})");
            sb.AppendLine($"        preferredHeight={label.preferredHeight:0.#}  " +
                          $"preferredWidth={label.preferredWidth:0.#}  lines={label.textInfo.lineCount}");
            sb.AppendLine($"        text: \"{Short(label.text)}\"");

            ContentSizeFitter lf = label.GetComponent<ContentSizeFitter>();
            if (lf != null)
                sb.AppendLine($"        ContentSizeFitter: horizontal={lf.horizontalFit}  vertical={lf.verticalFit}" +
                              "   <-- this makes the label's size follow its text, which is what " +
                              "makes a long choice sit off its bar");

            LayoutElement le = label.GetComponent<LayoutElement>();
            if (le != null)
                sb.AppendLine($"        LayoutElement: minH={le.minHeight} prefH={le.preferredHeight} " +
                              $"flexH={le.flexibleHeight} ignore={le.ignoreLayout}");
        }
    }

    // ======================================================================

    private static string Short(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Replace("\n", " ");
        return s.Length <= 60 ? s : s.Substring(0, 57) + "...";
    }

    private static string PathOf(Transform t)
    {
        string path = t.name;
        Transform cur = t.parent;
        int guard = 0;
        while (cur != null && guard++ < 20)
        {
            path = cur.name + "/" + path;
            cur = cur.parent;
        }
        return path;
    }
}
