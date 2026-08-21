#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The widget factory: every piece of recreated Windows chrome is built here, from code.
///
/// WHY CODE AND NOT PREFABS
/// Same reason COC3SceneBuilder generates its UI. A prefab kit would mean 14 binary assets
/// that cannot be diffed, cannot be regenerated, and drift the moment someone nudges one in
/// the Scene view. Generated widgets are reproducible: delete everything, run the builder,
/// get an identical result.
///
/// LAYOUT MODEL
/// Everything anchors TOP-LEFT with a top-left pivot, and is placed by explicit (x, y) in
/// window-space pixels measured downward from the top-left corner. No layout groups. Windows
/// dialogs are pixel-positioned by nature, and auto-layout fights that at every turn.
/// The window itself is then scaled once to fit the step panel.
/// </summary>
public static class COC3WinWidgets
{
    // ==================================================================
    // Core primitives
    // ==================================================================

    /// <summary>A rect anchored to its parent's top-left, positioned in downward pixels.</summary>
    public static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "COC3 Create Widget");

        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
        rt.localScale = Vector3.one;
        return rt;
    }

    public static Image Box(Transform parent, string name, float x, float y, float w, float h, Color color)
    {
        var rt = Rect(parent, name, x, y, w, h);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        return img;
    }

    /// <summary>A filled rect with a 1px border, drawn as a border box with an inset fill.
    /// Cheaper and sharper than a 9-sliced sprite, and needs no art.</summary>
    public static RectTransform BorderedBox(Transform parent, string name, float x, float y, float w, float h,
                                            Color fill, Color border, float thickness = 1f)
    {
        var outer = Box(parent, name, x, y, w, h, border).rectTransform;

        var inner = Rect(outer, "Fill", thickness, thickness, w - thickness * 2f, h - thickness * 2f);
        var img = inner.gameObject.AddComponent<Image>();
        img.color = fill;

        return inner;   // callers put content inside the fill, not the border
    }

    public static TextMeshProUGUI Label(Transform parent, COC3WinTheme t, string name,
                                        float x, float y, float w, float h, string text,
                                        TextAlignmentOptions align = TextAlignmentOptions.Left,
                                        bool bold = false, Color? colorOverride = null)
    {
        var rt = Rect(parent, name, x, y, w, h);
        var label = rt.gameObject.AddComponent<TextMeshProUGUI>();

        if (t.uiFont != null) label.font = t.uiFont;
        label.fontSize = t.fontSize;
        label.color = colorOverride ?? t.text;
        label.alignment = align;
        label.text = text;
        label.enableAutoSizing = false;
        if (bold) label.fontStyle = FontStyles.Bold;

        return label;
    }

    // ==================================================================
    // Window frame
    // ==================================================================

    /// <summary>
    /// Builds a window with a title bar and returns its BODY rect - the client area that
    /// content is placed into. Body coordinates start at (0,0) top-left of the client area,
    /// so a dialog definition never has to know the title bar height.
    /// </summary>
    public static RectTransform Window(Transform parent, COC3WinTheme t, string name,
                                       string title, float w, float h, out RectTransform windowRoot)
    {
        // Centre the window in the panel: anchored centre, then convert to our top-left model
        // by parenting a top-left rect inside it.
        var holder = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(holder, "COC3 Create Window");

        var hrt = (RectTransform)holder.transform;
        hrt.SetParent(parent, false);
        hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0.5f, 0.5f);
        hrt.anchoredPosition = Vector2.zero;
        hrt.sizeDelta = new Vector2(w, h);
        hrt.localScale = Vector3.one;

        var frameImg = holder.AddComponent<Image>();
        frameImg.color = t.windowBorder;

        // Everything below is placed top-left relative to the frame.
        var inner = Rect(hrt, "Client", 1f, 1f, w - 2f, h - 2f);
        var innerImg = inner.gameObject.AddComponent<Image>();
        innerImg.color = t.windowBackground;

        // Title bar
        var bar = Box(inner, "TitleBar", 0f, 0f, w - 2f, t.titleBarHeight, t.titleBar);
        Label(bar.transform, t, "Title", 10f, 0f, w - 120f, t.titleBarHeight, title,
              TextAlignmentOptions.Left).fontSize = t.titleFontSize;

        // Close glyph. Not a Button - it is decoration, and a live close button on a step
        // panel would only ever confuse the learner.
        Label(bar.transform, t, "Close", w - 40f, 0f, 30f, t.titleBarHeight, "✕",
              TextAlignmentOptions.Center);

        Box(inner, "TitleSeparator", 0f, t.titleBarHeight, w - 2f, 1f, t.separator);

        var body = Rect(inner, "Body", t.padding, t.titleBarHeight + t.padding,
                        w - 2f - t.padding * 2f, h - 2f - t.titleBarHeight - t.padding * 2f);

        windowRoot = hrt;
        return body;
    }

    // ==================================================================
    // Controls
    // ==================================================================

    /// <summary>Label on the left, input box on the right. Returns the input field so the
    /// builder can bind it to ScreenshotStep - this IS the graded control, not a separate
    /// box floating under a screenshot.</summary>
    public static TMP_InputField Field(Transform parent, COC3WinTheme t, string name,
                                       float x, float y, float labelWidth, float fieldWidth,
                                       string label, string value, string placeholderText,
                                       bool readOnly)
    {
        float h = t.rowHeight;

        if (!string.IsNullOrEmpty(label))
            Label(parent, t, name + "_Label", x, y + 4f, labelWidth - 8f, h, label);

        float fx = x + labelWidth;
        var fill = BorderedBox(parent, name, fx, y, fieldWidth, h,
                               readOnly ? t.fieldDisabled : t.fieldBackground, t.fieldBorder);

        var field = fill.gameObject.AddComponent<TMP_InputField>();
        field.targetGraphic = fill.GetComponent<Image>();
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.readOnly = readOnly;
        field.characterLimit = 0;                     // 0 = unlimited; a limit silently truncates long paths
        field.contentType = TMP_InputField.ContentType.Standard;   // never Alphanumeric - it eats backslashes

        var area = Rect(fill, "Text Area", 6f, 3f, fieldWidth - 14f, h - 8f);
        area.gameObject.AddComponent<RectMask2D>();

        var ph = Label(area, t, "Placeholder", 0f, 0f, fieldWidth - 14f, h - 8f,
                       placeholderText ?? string.Empty, TextAlignmentOptions.Left, false, t.placeholder);
        ph.fontStyle = FontStyles.Italic;

        var txt = Label(area, t, "Text", 0f, 0f, fieldWidth - 14f, h - 8f,
                        string.Empty, TextAlignmentOptions.Left, false,
                        readOnly ? t.disabledText : t.text);

        field.textViewport = area;
        field.textComponent = txt;
        field.placeholder = ph;
        if (t.uiFont != null) field.fontAsset = t.uiFont;
        field.pointSize = t.fontSize;
        field.text = value ?? string.Empty;

        return field;
    }

    public static Button Button(Transform parent, COC3WinTheme t, string name,
                                float x, float y, float w, float h, string text, bool isDefault = false)
    {
        var fill = BorderedBox(parent, name, x, y, w, h,
                               isDefault ? t.buttonFace : t.buttonFace,
                               isDefault ? t.accent : t.buttonBorder);

        var btn = fill.gameObject.AddComponent<Button>();
        btn.targetGraphic = fill.GetComponent<Image>();

        Label(fill, t, "Label", 0f, 0f, w - 2f, h - 2f, text, TextAlignmentOptions.Center);
        return btn;
    }

    /// <summary>
    /// A checkbox row. Returned as a Button so it can be bound directly as the step's
    /// interaction target - the learner clicks the real checkbox, not an invisible
    /// rectangle floating over a screenshot.
    /// </summary>
    public static Button Checkbox(Transform parent, COC3WinTheme t, string name,
                                  float x, float y, float w, string label, bool ticked)
    {
        float h = t.rowHeight;

        var row = Rect(parent, name, x, y, w, h);
        var hit = row.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);          // invisible but raycastable
        var btn = row.gameObject.AddComponent<Button>();
        btn.targetGraphic = hit;

        float boxSize = 14f;
        var box = BorderedBox(row, "Box", 0f, (h - boxSize) * 0.5f, boxSize, boxSize,
                              t.fieldBackground, t.fieldBorder);

        if (ticked)
            Label(box, t, "Check", 0f, -3f, boxSize - 2f, boxSize, "✓",
                  TextAlignmentOptions.Center, false, t.text);

        Label(row, t, "Label", boxSize + 8f, 0f, w - boxSize - 8f, h, label);
        return btn;
    }

    /// <summary>A radio row. Also a Button, for the same reason as Checkbox.</summary>
    public static Button Radio(Transform parent, COC3WinTheme t, string name,
                               float x, float y, float w, string label, bool selected)
    {
        float h = t.rowHeight;

        var row = Rect(parent, name, x, y, w, h);
        var hit = row.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        var btn = row.gameObject.AddComponent<Button>();
        btn.targetGraphic = hit;

        Label(row, t, "Dot", 0f, 0f, 16f, h, selected ? "◉" : "○",
              TextAlignmentOptions.Left, false, selected ? t.accent : t.fieldBorder);

        Label(row, t, "Label", 22f, 0f, w - 22f, h, label);
        return btn;
    }

    /// <summary>Group box: a titled border. Returns the inner content rect.</summary>
    public static RectTransform GroupBox(Transform parent, COC3WinTheme t, string name,
                                         float x, float y, float w, float h, string title)
    {
        var group = Rect(parent, name, x, y, w, h);

        // Four 1px edges rather than a bordered box, so the interior stays transparent and
        // shows the window background through it, exactly like a real group box.
        Box(group, "Top", 0f, 0f, w, 1f, t.groupBorder);
        Box(group, "Bottom", 0f, h - 1f, w, 1f, t.groupBorder);
        Box(group, "Left", 0f, 0f, 1f, h, t.groupBorder);
        Box(group, "Right", w - 1f, 0f, 1f, h, t.groupBorder);

        if (!string.IsNullOrEmpty(title))
        {
            // The title sits ON the top edge, so mask that stretch of border behind it.
            float titleWidth = title.Length * (t.fontSize * 0.58f) + 10f;
            Box(group, "TitleMask", 8f, -1f, titleWidth, 3f, t.windowBackground);
            Label(group, t, "GroupTitle", 12f, -t.fontSize * 0.72f, titleWidth, t.fontSize * 1.4f, title);
        }

        return Rect(group, "Content", t.padding, t.padding + 4f, w - t.padding * 2f, h - t.padding * 2f - 4f);
    }

    /// <summary>Tab strip across the top of a properties dialog. Decorative - the active tab
    /// is the one the step is about, and the others are not clickable on purpose.</summary>
    public static void TabStrip(Transform parent, COC3WinTheme t, string name,
                                float x, float y, float w, string[] tabs, int activeIndex)
    {
        float h = 26f;
        var strip = Rect(parent, name, x, y, w, h);

        float tx = 0f;
        for (int i = 0; i < tabs.Length; i++)
        {
            float tw = tabs[i].Length * (t.fontSize * 0.58f) + 22f;
            bool active = i == activeIndex;

            var tab = BorderedBox(strip, "Tab_" + tabs[i], tx, 0f, tw, h,
                                  active ? t.windowBackground : t.buttonFace, t.windowBorder);

            Label(tab, t, "Label", 0f, 0f, tw - 2f, h - 2f, tabs[i], TextAlignmentOptions.Center,
                  false, active ? t.text : t.disabledText);

            tx += tw - 1f;
        }

        // The line under the strip, broken by the active tab, is what sells a Win32 tab
        // control. Drawn as a full line here; the active tab's own fill covers its section.
        Box(parent, name + "_Underline", x, y + h - 1f, w, 1f, t.windowBorder);
    }

    /// <summary>A right-aligned row of buttons at the bottom of a dialog. Returns them in the
    /// order given, so the builder can wire whichever one submits the step.</summary>
    public static Button[] ButtonBar(Transform parent, COC3WinTheme t, string name,
                                     float rightEdge, float y, string[] labels, int defaultIndex)
    {
        var result = new Button[labels.Length];

        float x = rightEdge;
        for (int i = labels.Length - 1; i >= 0; i--)
        {
            x -= t.buttonSize.x;
            result[i] = Button(parent, t, name + "_" + labels[i], x, y,
                               t.buttonSize.x, t.buttonSize.y, labels[i], i == defaultIndex);
            x -= 8f;
        }

        return result;
    }

    // ==================================================================
    // Phase 2 widgets
    // ==================================================================

    /// <summary>
    /// Draws the window frame and title bar shared by every archetype, and returns the
    /// client area below the title bar.
    /// </summary>
    private static RectTransform Frame(Transform parent, COC3WinTheme t, string name,
                                       string title, float w, float h, out RectTransform windowRoot)
    {
        var holder = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(holder, "COC3 Create Window");

        var hrt = (RectTransform)holder.transform;
        hrt.SetParent(parent, false);
        hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0.5f, 0.5f);
        hrt.anchoredPosition = Vector2.zero;
        hrt.sizeDelta = new Vector2(w, h);
        hrt.localScale = Vector3.one;
        holder.AddComponent<Image>().color = t.windowBorder;

        var inner = Rect(hrt, "Client", 1f, 1f, w - 2f, h - 2f);
        inner.gameObject.AddComponent<Image>().color = t.windowBackground;

        var bar = Box(inner, "TitleBar", 0f, 0f, w - 2f, t.titleBarHeight, t.titleBar);
        Label(bar.transform, t, "Title", 10f, 0f, w - 120f, t.titleBarHeight, title,
              TextAlignmentOptions.Left).fontSize = t.titleFontSize;
        Label(bar.transform, t, "Close", w - 40f, 0f, 30f, t.titleBarHeight, "\u2715",
              TextAlignmentOptions.Center);

        Box(inner, "TitleSeparator", 0f, t.titleBarHeight, w - 2f, 1f, t.separator);

        windowRoot = hrt;
        return inner;
    }

    /// <summary>
    /// A Server-style wizard: white heading strip, a stage list down the left, and the page
    /// content on the right. Returns the CONTENT rect, so a dialog definition places controls
    /// as if the left column and heading did not exist.
    ///
    /// The builder puts the button bar at the bottom of that content rect, which lands
    /// Back / Next / Cancel bottom-right - exactly where a real wizard has them.
    /// </summary>
    public static RectTransform Wizard(Transform parent, COC3WinTheme t, string name,
                                       string title, string heading, string[] stages, int activeStage,
                                       float w, float h, out RectTransform windowRoot,
                                       out float bodyW, out float bodyH)
    {
        var inner = Frame(parent, t, name, title, w, h, out windowRoot);

        float top = t.titleBarHeight + 1f;
        float headingH = 44f;
        float navW = 190f;

        var headStrip = Box(inner, "Heading", 0f, top, w - 2f, headingH, t.titleBar);
        Label(headStrip.transform, t, "HeadingText", 16f, 0f, w - 40f, headingH, heading,
              TextAlignmentOptions.Left, true);
        Box(inner, "HeadingSeparator", 0f, top + headingH, w - 2f, 1f, t.separator);

        float listTop = top + headingH + 1f;
        float listH = h - 2f - listTop;

        Box(inner, "NavPane", 0f, listTop, navW, listH, t.windowBackground);
        Box(inner, "NavSeparator", navW, listTop, 1f, listH, t.separator);

        for (int i = 0; i < stages.Length; i++)
        {
            bool active = i == activeStage;
            Label(inner, t, "Stage_" + i, 16f, listTop + 12f + i * 24f, navW - 24f, 22f,
                  stages[i], TextAlignmentOptions.Left, active,
                  active ? t.text : t.disabledText);
        }

        bodyW = w - 2f - navW - t.padding * 2f;
        bodyH = listH - t.padding * 2f;

        return Rect(inner, "Body", navW + t.padding, listTop + t.padding, bodyW, bodyH);
    }

    /// <summary>Command Prompt. Returns the output text so the builder can hand it to
    /// PingSimulation - the console the learner watches IS the simulation's output target.</summary>
    public static TextMeshProUGUI Console(Transform parent, COC3WinTheme t, string name,
                                          float x, float y, float w, float h, string initialText)
    {
        var bg = Box(parent, name, x, y, w, h, t.consoleBackground);

        var txt = Label(bg.transform, t, "Output", 8f, 6f, w - 16f, h - 12f, initialText,
                        TextAlignmentOptions.TopLeft, false, t.consoleText);
        txt.fontSize = t.fontSize;
        return txt;
    }

    /// <summary>Explorer-style address bar. Returns the field so it can be the graded input -
    /// the learner types the path where a real address bar is.</summary>
    public static TMP_InputField AddressBar(Transform parent, COC3WinTheme t, string name,
                                            float x, float y, float w, string placeholderText)
    {
        float h = t.rowHeight + 2f;
        var fill = BorderedBox(parent, name, x, y, w, h, t.fieldBackground, t.fieldBorder);

        var field = fill.gameObject.AddComponent<TMP_InputField>();
        field.targetGraphic = fill.GetComponent<Image>();
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.characterLimit = 0;
        field.contentType = TMP_InputField.ContentType.Standard;

        var area = Rect(fill, "Text Area", 8f, 4f, w - 18f, h - 10f);
        area.gameObject.AddComponent<RectMask2D>();

        var ph = Label(area, t, "Placeholder", 0f, 0f, w - 18f, h - 10f, placeholderText,
                       TextAlignmentOptions.Left, false, t.placeholder);
        var tx = Label(area, t, "Text", 0f, 0f, w - 18f, h - 10f, string.Empty,
                       TextAlignmentOptions.Left);

        field.textViewport = area;
        field.textComponent = tx;
        field.placeholder = ph;
        if (t.uiFont != null) field.fontAsset = t.uiFont;
        field.pointSize = t.fontSize;

        return field;
    }

    /// <summary>A simple column-less list, as used by Explorer panes and role lists.</summary>
    public static void ListView(Transform parent, COC3WinTheme t, string name,
                                float x, float y, float w, float h, string[] rows, int selectedRow)
    {
        var fill = BorderedBox(parent, name, x, y, w, h, t.fieldBackground, t.fieldBorder);

        float rowH = 22f;
        for (int i = 0; i < rows.Length && (i + 1) * rowH < h; i++)
        {
            if (i == selectedRow)
                Box(fill, "Sel_" + i, 1f, 4f + i * rowH, w - 4f, rowH, t.selection);

            Label(fill, t, "Row_" + i, 8f, 4f + i * rowH, w - 16f, rowH, rows[i]);
        }
    }

    // ==================================================================
    // Phase 3 widgets
    // ==================================================================

    /// <summary>Panes of an MMC console window, handed back so a dialog definition can fill
    /// each one without re-deriving the geometry.</summary>
    public class MmcLayout
    {
        public RectTransform tree, list, actions;
        public float treeW, treeH, listW, listH, actionsW;
    }

    /// <summary>A window with NO body padding - for consoles and anything that draws its own
    /// panes flush to the frame.</summary>
    public static RectTransform WindowBare(Transform parent, COC3WinTheme t, string name,
                                           string title, float w, float h, out RectTransform windowRoot)
    {
        var inner = Frame(parent, t, name, title, w, h, out windowRoot);
        return Rect(inner, "Body", 0f, t.titleBarHeight + 1f, w - 2f, h - 2f - t.titleBarHeight - 1f);
    }

    /// <summary>
    /// Microsoft Management Console: menu bar, toolbar strip, tree pane on the left, list pane
    /// on the right. Group Policy Editor, ADUC, DHCP, Event Viewer and the Firewall console
    /// are all this same window with different contents - which is what makes the dense half
    /// of COC 3 tractable.
    /// </summary>
    public static MmcLayout Mmc(Transform parent, COC3WinTheme t, RectTransform body,
                                float w, float h, string[] menu, float treeWidth = 240f,
                                float actionsWidth = 0f)
    {
        float menuH = 22f;
        float toolH = 26f;

        Box(body, "MenuBar", 0f, 0f, w, menuH, t.windowBackground);
        float mx = 8f;
        foreach (var m in menu)
        {
            float mw = m.Length * (t.fontSize * 0.58f) + 14f;
            Label(body, t, "Menu_" + m, mx, 0f, mw, menuH, m);
            mx += mw;
        }

        Box(body, "Toolbar", 0f, menuH, w, toolH, t.windowBackground);
        Box(body, "ToolbarSep", 0f, menuH + toolH, w, 1f, t.separator);

        float top = menuH + toolH + 1f;
        float paneH = h - top;

        var tree = Rect(body, "TreePane", 0f, top, treeWidth, paneH);
        tree.gameObject.AddComponent<Image>().color = t.fieldBackground;

        Box(body, "PaneSplitter", treeWidth, top, 2f, paneH, t.separator);

        float listW = w - treeWidth - 2f - actionsWidth;

        var list = Rect(body, "ListPane", treeWidth + 2f, top, listW, paneH);
        list.gameObject.AddComponent<Image>().color = t.fieldBackground;

        RectTransform actions = null;
        if (actionsWidth > 0f)
        {
            Box(body, "ActionsSplitter", treeWidth + 2f + listW, top, 1f, paneH, t.separator);

            actions = Rect(body, "ActionsPane", treeWidth + 3f + listW, top, actionsWidth - 1f, paneH);
            actions.gameObject.AddComponent<Image>().color = t.windowBackground;

            Label(actions, t, "ActionsTitle", 8f, 6f, actionsWidth - 16f, 22f, "Actions",
                  TextAlignmentOptions.Left, true);
        }

        return new MmcLayout
        {
            tree = tree,
            list = list,
            actions = actions,
            treeW = treeWidth,
            treeH = paneH,
            listW = listW,
            listH = paneH,
            actionsW = actionsWidth,
        };
    }

    /// <summary>
    /// A tree row with a checkbox - the Add Roles and Features wizard's Server Roles page.
    /// Returned as a Button so the specific role the step is about becomes the target: the
    /// learner ticks "Active Directory Domain Services" itself.
    /// </summary>
    public static Button CheckTreeNode(Transform parent, COC3WinTheme t, string name,
                                       float y, float w, int depth, string label,
                                       bool ticked, bool dimmed = false)
    {
        float h = 22f;
        float indent = 8f + depth * 18f;

        var row = Rect(parent, name, 0f, y, w, h);
        var hit = row.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        var btn = row.gameObject.AddComponent<Button>();
        btn.targetGraphic = hit;

        float box = 13f;
        var b = BorderedBox(row, "Box", indent, (h - box) * 0.5f, box, box,
                            dimmed ? t.fieldDisabled : t.fieldBackground, t.fieldBorder);

        if (ticked)
            Label(b, t, "Check", 0f, -3f, box - 2f, box, "\u2713", TextAlignmentOptions.Center);

        Label(row, t, "Label", indent + box + 8f, 0f, w - indent - box - 12f, h, label,
              TextAlignmentOptions.Left, false, dimmed ? t.disabledText : t.text);

        return btn;
    }

    /// <summary>One row of a tree, indented by depth. Returned as a Button so any node can be
    /// the step's interaction target.</summary>
    public static Button TreeNode(Transform parent, COC3WinTheme t, string name,
                                  float y, float w, int depth, string label,
                                  bool hasChildren, bool expanded, bool selected)
    {
        float h = 22f;
        float indent = 12f + depth * 16f;

        var row = Rect(parent, name, 0f, y, w, h);
        var hit = row.gameObject.AddComponent<Image>();
        hit.color = selected ? t.selection : new Color(0f, 0f, 0f, 0f);
        var btn = row.gameObject.AddComponent<Button>();
        btn.targetGraphic = hit;

        if (hasChildren)
            Label(row, t, "Expander", indent - 12f, 0f, 12f, h, expanded ? "▾" : "▸",
                  TextAlignmentOptions.Center, false, t.disabledText);

        Label(row, t, "Label", indent, 0f, w - indent - 4f, h, label);
        return btn;
    }

    /// <summary>
    /// A list where every row is clickable. Returns one Button per row so a dialog can hand a
    /// specific row back as the interaction target - the learner clicks the actual policy or
    /// rule, not an invisible rectangle over a picture of one.
    /// </summary>
    public static Button[] ListRows(Transform parent, COC3WinTheme t, string name,
                                    float x, float y, float w, float h,
                                    string[] header, string[][] rows, int selectedRow)
    {
        float rowH = 22f;
        float cursor = 0f;

        if (header != null && header.Length > 0)
        {
            Box(parent, name + "_Header", x, y, w, rowH, t.windowBackground);
            float hx = x + 8f;
            float colW = (w - 16f) / header.Length;
            for (int c = 0; c < header.Length; c++)
            {
                Label(parent, t, name + "_H" + c, hx, y, colW - 8f, rowH, header[c],
                      TextAlignmentOptions.Left, true);
                hx += colW;
            }
            Box(parent, name + "_HeaderSep", x, y + rowH, w, 1f, t.separator);
            cursor = rowH + 1f;
        }

        var result = new Button[rows.Length];
        int columns = header != null && header.Length > 0 ? header.Length : 1;
        float cw = (w - 16f) / columns;

        for (int i = 0; i < rows.Length; i++)
        {
            float ry = y + cursor + i * rowH;
            if (ry + rowH > y + h) break;

            var row = Rect(parent, name + "_R" + i, x, ry, w, rowH);
            var hit = row.gameObject.AddComponent<Image>();
            hit.color = i == selectedRow ? t.selection : new Color(0f, 0f, 0f, 0f);
            var btn = row.gameObject.AddComponent<Button>();
            btn.targetGraphic = hit;
            result[i] = btn;

            for (int c = 0; c < rows[i].Length && c < columns; c++)
                Label(row, t, "C" + c, 8f + c * cw, 0f, cw - 8f, rowH, rows[i][c]);
        }

        return result;
    }

    /// <summary>
    /// The Security tab's permission table: a row per permission, with Allow and Deny
    /// checkboxes. Returns the ALLOW column so a dialog can bind one specific cell - "Modify
    /// for STUDENTS" becomes a real checkbox the learner ticks.
    /// </summary>
    public static Button[] PermissionGrid(Transform parent, COC3WinTheme t, string name,
                                          float x, float y, float w, float h,
                                          string headerLabel, string[] permissions,
                                          int tickedRow)
    {
        float rowH = 24f;
        float colW = 60f;
        float labelW = w - colW * 2f - 8f;

        var fill = BorderedBox(parent, name, x, y, w, h, t.fieldBackground, t.fieldBorder);

        Label(fill, t, "HeaderLabel", 8f, 2f, labelW - 8f, rowH, headerLabel,
              TextAlignmentOptions.Left, true);
        Label(fill, t, "HeaderAllow", labelW, 2f, colW, rowH, "Allow",
              TextAlignmentOptions.Center, true);
        Label(fill, t, "HeaderDeny", labelW + colW, 2f, colW, rowH, "Deny",
              TextAlignmentOptions.Center, true);
        Box(fill, "HeaderSep", 0f, rowH + 2f, w - 2f, 1f, t.separator);

        var allow = new Button[permissions.Length];

        for (int i = 0; i < permissions.Length; i++)
        {
            float ry = rowH + 6f + i * rowH;
            if (ry + rowH > h) break;

            Label(fill, t, "P" + i, 8f, ry, labelW - 8f, rowH, permissions[i]);

            allow[i] = CheckCell(fill, t, name + "_Allow" + i, labelW, ry, colW, rowH, i == tickedRow);
            CheckCell(fill, t, name + "_Deny" + i, labelW + colW, ry, colW, rowH, false);
        }

        return allow;
    }

    /// <summary>A centred checkbox inside a grid cell, clickable as a whole cell.</summary>
    private static Button CheckCell(Transform parent, COC3WinTheme t, string name,
                                    float x, float y, float w, float h, bool ticked)
    {
        var cell = Rect(parent, name, x, y, w, h);
        var hit = cell.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        var btn = cell.gameObject.AddComponent<Button>();
        btn.targetGraphic = hit;

        float box = 14f;
        var b = BorderedBox(cell, "Box", (w - box) * 0.5f, (h - box) * 0.5f, box, box,
                            t.fieldBackground, t.fieldBorder);

        if (ticked)
            Label(b, t, "Check", 0f, -3f, box - 2f, box, "✓", TextAlignmentOptions.Center);

        return btn;
    }
}
#endif