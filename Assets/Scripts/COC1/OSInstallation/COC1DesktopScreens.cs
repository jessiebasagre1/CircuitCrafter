#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws the post-install desktop: wallpaper, taskbar, five icons, five app windows, and the
/// three row prefabs those windows spawn.
///
/// THE APPS
///   Device Manager   - drivers          (ChecklistToolManager, install_drivers)
///   Software Center  - applications     (ChecklistToolManager, install_app_software)
///   Windows Update   - patches          (ChecklistToolManager, install_updates)
///   Diagnostics      - test the machine (DiagnosticsTestManager)
///   Service Report   - sign the job off (ServiceReportPanel)
///
/// -------------------------------------------------------------------------------------
/// MANAGERS DO NOT LIVE ON THESE PANELS. THE VIEWS DO.
/// ChecklistToolManager, DiagnosticsTestManager and ServiceReportPanel all register their
/// TaskManager tasks in Start(), and Start() never runs on an object that begins life
/// inactive. An app window starts switched off, so a manager parented to one would never
/// register its task - the checklist would silently never complete, and the service report
/// would never unlock. The managers belong on an always-active object; only the views, which
/// wire themselves up in Start/OnEnable when their panel is first opened, go on the panels.
///
/// Same rule that stranded the partition panel, and the same one behind OSInstallView needing
/// an active host. It is the single most common way this project breaks.
/// -------------------------------------------------------------------------------------
///
/// NOTHING IS NESTED. Containers are authored in local coordinates through the Frame struct,
/// but every element is parented to the screen root in absolute design space - anchors are
/// fractions of the parent, so a child placed at "x / 1920" inside a 1200px window would land
/// at a fraction of 1200. That bug cost an afternoon; the Frame struct is how it stays fixed.
/// </summary>
public static class COC1DesktopScreens
{
    public const float DW = 1920f;
    public const float DH = 1080f;

    private const string UndoLabel = "COC1 Build Desktop";
    private const string PrefabFolder = "Assets/COC1/Content/Prefabs";

    public static TMP_FontAsset Font;

    // ==================================================================
    // Palette
    // ==================================================================

    private static readonly Color Wallpaper = Hex(0x1E3A5F);
    private static readonly Color TaskbarBg = Hex(0x1F1F1F);
    private static readonly Color TaskbarText = Hex(0xE8E8E8);
    private static readonly Color IconTile = Hex(0x2E4C73);
    private static readonly Color IconLabel = Hex(0xFFFFFF);
    private static readonly Color WindowFill = Hex(0xFFFFFF);
    private static readonly Color WindowBorder = Hex(0x7A7A7A);
    private static readonly Color TitleStrip = Hex(0xF0F0F0);
    private static readonly Color Rule = Hex(0xDADCE0);
    private static readonly Color DarkText = Hex(0x202124);
    private static readonly Color MutedText = Hex(0x5F6368);
    private static readonly Color ButtonFace = Hex(0xE1E1E1);
    private static readonly Color ButtonBorder = Hex(0xADADAD);
    private static readonly Color Accent = Hex(0x0078D4);
    private static readonly Color RowAlt = Hex(0xF8F9FA);
    private static readonly Color PassGreen = Hex(0x2E9E4F);
    private static readonly Color FailRed = Hex(0xC42B1C);
    private static readonly Color BannerGreen = Hex(0xE6F4EA);

    // ==================================================================
    // Results
    // ==================================================================

    public class AppWindow
    {
        public string key;
        public GameObject panel;          // the window, starts off
        public Button closeButton;
        public Button icon;               // its desktop icon
        public GameObject iconObject;

        // ChecklistToolView / DiagnosticsView / ServiceReportView, whichever applies
        public Transform rowsContent;
        public GameObject banner;
        public Button actionButton;       // Run Diagnostics / Submit Report
        public TMP_Text statusText;
    }

    public class Built
    {
        public GameObject iconsRoot;
        public readonly List<AppWindow> apps = new List<AppWindow>();

        public ChecklistItemRowView checklistRowPrefab;
        public DiagnosticTestRowView testRowPrefab;
        public ChecklistStatusRowView statusRowPrefab;

        public AppWindow Find(string key)
        {
            foreach (var a in apps) if (a.key == key) return a;
            return null;
        }
    }

    public const string Drivers = "DeviceManager";
    public const string Apps = "SoftwareCenter";
    public const string Updates = "WindowsUpdate";
    public const string Diagnostics = "Diagnostics";
    public const string Report = "ServiceReport";

    // ==================================================================
    // Entry point
    // ==================================================================

    public static Built BuildAll(RectTransform desktop)
    {
        var b = new Built();
        var s = new Frame { root = desktop, ox = 0f, oy = 0f };

        Box(s, "Wallpaper", 0, 0, DW, DH, Wallpaper);

        // Icons before the windows, so a window opened on top covers them.
        var icons = Stretch(desktop, "Icons");
        b.iconsRoot = icons.gameObject;

        var windows = Stretch(desktop, "Apps");

        BuildTaskbar(s);

        b.apps.Add(BuildChecklistApp(icons, windows, Drivers, "Device Manager",
                                     "Drivers reported by this PC", 0));
        b.apps.Add(BuildChecklistApp(icons, windows, Apps, "Software Center",
                                     "Application software", 1));
        b.apps.Add(BuildChecklistApp(icons, windows, Updates, "Windows Update",
                                     "Updates and patches", 2));
        b.apps.Add(BuildDiagnosticsApp(icons, windows, 3));
        b.apps.Add(BuildReportApp(icons, windows, 4));

        b.checklistRowPrefab = BuildChecklistRowPrefab();
        b.testRowPrefab = BuildTestRowPrefab();
        b.statusRowPrefab = BuildStatusRowPrefab();

        return b;
    }

    // ==================================================================
    // Desktop furniture
    // ==================================================================

    private static void BuildTaskbar(Frame s)
    {
        Box(s, "Taskbar", 0, DH - 48f, DW, 48f, TaskbarBg);
        Box(s, "StartTile", 8, DH - 44f, 40f, 40f, Hex(0x3A3A3A));

        // Four small squares rather than a glyph. LiberationSans has no U+25A6, and TMP
        // substitutes a hollow box and logs a warning for every missing character - the kind of
        // detail that makes a build look broken for no functional reason.
        Box(s, "StartPane1", 18, DH - 34f, 8f, 8f, TaskbarText);
        Box(s, "StartPane2", 28, DH - 34f, 8f, 8f, TaskbarText);
        Box(s, "StartPane3", 18, DH - 24f, 8f, 8f, TaskbarText);
        Box(s, "StartPane4", 28, DH - 24f, 8f, 8f, TaskbarText);
        Text(s, "TaskbarSearch", 60, DH - 44f, 300f, 40f, "Type here to search", 15, Hex(0x9A9A9A));
        Text(s, "Clock", DW - 180f, DH - 44f, 160f, 40f, "10:42\n21/04/2026", 13, TaskbarText,
             TextAlignmentOptions.Right);
    }

    /// <summary>One desktop icon. Returned as a Button so the builder can add a
    /// DesktopIconButton and point it at the matching window.</summary>
    private static Button Icon(Transform iconsRoot, string key, string label, int slot)
    {
        var f = new Frame { root = iconsRoot, ox = 0f, oy = 0f };

        float x = 40f;
        float y = 40f + slot * 132f;

        var rt = Place(f, "Icon_" + key, x, y, 150f, 118f);

        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 1f / 255f);   // invisible but never culled
        img.raycastTarget = true;

        var cr = rt.GetComponent<CanvasRenderer>();
        if (cr != null) cr.cullTransparentMesh = false;

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;

        Box(f, "IconTile_" + key, x + 43f, y + 4f, 64f, 64f, IconTile);
        Text(f, "IconLabel_" + key, x, y + 74f, 150f, 40f, label, 15, IconLabel,
             TextAlignmentOptions.Top);

        return btn;
    }

    // ==================================================================
    // App windows
    // ==================================================================

    private const float WinX = 360f;
    private const float WinY = 120f;
    private const float WinW = 1200f;
    private const float WinH = 800f;

    /// <summary>Window chrome shared by all five apps. Returns a Frame whose origin is the
    /// window, so contents are written in local coordinates.</summary>
    private static Frame Window(Transform parent, string name, string title, out GameObject panel,
                                out Button close)
    {
        var root = Stretch(parent, "App_" + name);
        panel = root.gameObject;

        var f = new Frame { root = root, ox = 0f, oy = 0f };

        // A dim behind the window, so the desktop reads as "behind a dialog" rather than
        // simply having something drawn on it.
        Box(f, "Dim", 0, 0, DW, DH, new Color(0f, 0f, 0f, 0.35f));

        Box(f, "Border", WinX, WinY, WinW, WinH, WindowBorder);
        Box(f, "Fill", WinX + 1f, WinY + 1f, WinW - 2f, WinH - 2f, WindowFill);

        Box(f, "TitleStrip", WinX + 1f, WinY + 1f, WinW - 2f, 44f, TitleStrip);
        Text(f, "Title", WinX + 20f, WinY + 1f, 800f, 44f, title, 19, DarkText,
             TextAlignmentOptions.Left, true);
        Box(f, "TitleRule", WinX + 1f, WinY + 45f, WinW - 2f, 1f, Rule);

        close = FlatButton(f, "CloseButton", WinX + WinW - 140f, WinY + WinH - 62f, 120f, 42f, "CLOSE");

        root.gameObject.SetActive(false);
        return f;
    }

    private static AppWindow BuildChecklistApp(Transform icons, Transform windows, string key,
                                               string title, string subtitle, int slot)
    {
        GameObject panel; Button close;
        var f = Window(windows, key, title, out panel, out close);

        Text(f, "Subtitle", WinX + 20f, WinY + 58f, WinW - 40f, 30f, subtitle, 16, MutedText);

        Box(f, "ListBorder", WinX + 20f, WinY + 96f, WinW - 40f, 560f, Rule);
        var list = Box(f, "RowsContent", WinX + 21f, WinY + 97f, WinW - 42f, 558f, WindowFill)
                   .rectTransform;

        // ChecklistToolView only instantiates rows into this transform - it does no layout of
        // its own, so the stacking has to come from a layout group here.
        var vlg = list.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 2f;
        vlg.padding = new RectOffset(6, 6, 6, 6);
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;

        var bannerRt = Place(f, "AllCompleteBanner", WinX + 20f, WinY + 668f, WinW - 180f, 44f);
        var bannerImg = bannerRt.gameObject.AddComponent<Image>();
        bannerImg.color = BannerGreen;
        bannerImg.raycastTarget = false;
        Text(f, "AllCompleteLabel", WinX + 36f, WinY + 668f, WinW - 200f, 44f,
             "Everything in this list is installed.", 16, PassGreen);
        bannerRt.gameObject.SetActive(false);

        return new AppWindow
        {
            key = key,
            panel = panel,
            closeButton = close,
            icon = Icon(icons, key, title, slot),
            rowsContent = list,
            banner = bannerRt.gameObject
        };
    }

    private static AppWindow BuildDiagnosticsApp(Transform icons, Transform windows, int slot)
    {
        GameObject panel; Button close;
        var f = Window(windows, Diagnostics, "Diagnostics", out panel, out close);

        Text(f, "Subtitle", WinX + 20f, WinY + 58f, WinW - 40f, 30f,
             "Run every check, then fix whatever fails and run it again.", 16, MutedText);

        Box(f, "ListBorder", WinX + 20f, WinY + 96f, WinW - 40f, 540f, Rule);
        var list = Box(f, "RowsContent", WinX + 21f, WinY + 97f, WinW - 42f, 538f, WindowFill)
                   .rectTransform;

        var vlg = list.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 2f;
        vlg.padding = new RectOffset(6, 6, 6, 6);
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;

        var run = FlatButton(f, "RunButton", WinX + 20f, WinY + 650f, 240f, 44f, "Run Diagnostics");
        var result = Text(f, "OverallResultText", WinX + 280f, WinY + 650f, WinW - 320f, 44f,
                          "Not tested yet.", 17, DarkText);

        return new AppWindow
        {
            key = Diagnostics,
            panel = panel,
            closeButton = close,
            icon = Icon(icons, Diagnostics, "Diagnostics", slot),
            rowsContent = list,
            actionButton = run,
            statusText = result
        };
    }

    private static AppWindow BuildReportApp(Transform icons, Transform windows, int slot)
    {
        GameObject panel; Button close;
        var f = Window(windows, Report, "Service Report", out panel, out close);

        Text(f, "Subtitle", WinX + 20f, WinY + 58f, WinW - 40f, 30f,
             "The sign-off sheet. Everything below has to be done before it can be submitted.",
             16, MutedText);

        Box(f, "ListBorder", WinX + 20f, WinY + 96f, WinW - 40f, 430f, Rule);
        var list = Box(f, "RowsContent", WinX + 21f, WinY + 97f, WinW - 42f, 428f, WindowFill)
                   .rectTransform;

        var vlg = list.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 2f;
        vlg.padding = new RectOffset(6, 6, 6, 6);
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;

        var status = Text(f, "StatusText", WinX + 20f, WinY + 542f, WinW - 40f, 36f,
                          "Finish the items above before submitting.", 17, MutedText);

        var submit = FlatButton(f, "SubmitButton", WinX + 20f, WinY + 590f, 260f, 48f,
                                "Submit Report");

        var submittedRt = Place(f, "SubmittedBanner", WinX + 300f, WinY + 590f, 560f, 48f);
        var submittedImg = submittedRt.gameObject.AddComponent<Image>();
        submittedImg.color = BannerGreen;
        submittedImg.raycastTarget = false;
        Text(f, "SubmittedLabel", WinX + 316f, WinY + 590f, 540f, 48f,
             "Report submitted. Job documented.", 17, PassGreen);
        submittedRt.gameObject.SetActive(false);

        return new AppWindow
        {
            key = Report,
            panel = panel,
            closeButton = close,
            icon = Icon(icons, Report, "Service Report", slot),
            rowsContent = list,
            actionButton = submit,
            banner = submittedRt.gameObject,
            statusText = status
        };
    }

    // ==================================================================
    // Row prefabs
    // ==================================================================

    private static ChecklistItemRowView BuildChecklistRowPrefab()
    {
        EnsureFolder(PrefabFolder);

        var go = NewRow("ChecklistItemRow", 72f, out RectTransform rt);

        var name = Col(rt, "NameText", 0.00f, 0.52f, 14f, 8f, 34f, 18f, DarkText, true);
        var desc = Col(rt, "DescriptionText", 0.00f, 0.52f, 14f, 38f, 8f, 14f, MutedText, false);
        var status = Col(rt, "StatusText", 0.74f, 0.86f, 6f, 8f, 8f, 15f, MutedText, false);

        var bar = FlatBar(rt, "ProgressBar", 0.53f, 0.73f);
        var install = RowButton(rt, "InstallButton", 0.87f, 0.99f, "Install");
        var tick = Marker(rt, "CompleteCheckmark", 0.87f, 0.99f, PassGreen);
        tick.SetActive(false);

        var view = go.AddComponent<ChecklistItemRowView>();
        var so = new SerializedObject(view);
        SetRef(so, "nameText", name);
        SetRef(so, "descriptionText", desc);
        SetRef(so, "progressBar", bar);
        SetRef(so, "statusText", status);
        SetRef(so, "installButton", install);
        SetRef(so, "completeCheckmark", tick);
        so.ApplyModifiedProperties();

        return SavePrefab<ChecklistItemRowView>(go, "ChecklistItemRow");
    }

    private static DiagnosticTestRowView BuildTestRowPrefab()
    {
        var go = NewRow("DiagnosticTestRow", 76f, out RectTransform rt);

        var name = Col(rt, "NameText", 0.00f, 0.55f, 14f, 6f, 38f, 18f, DarkText, true);
        var desc = Col(rt, "DescriptionText", 0.00f, 0.55f, 14f, 32f, 26f, 14f, MutedText, false);
        var hint = Col(rt, "FailureHintText", 0.00f, 0.99f, 14f, 54f, 4f, 13f, FailRed, false);
        hint.gameObject.SetActive(false);

        var bar = FlatBar(rt, "ProgressBar", 0.56f, 0.80f);
        var result = Col(rt, "ResultText", 0.82f, 0.99f, 6f, 6f, 32f, 16f, DarkText, true);

        var view = go.AddComponent<DiagnosticTestRowView>();
        var so = new SerializedObject(view);
        SetRef(so, "nameText", name);
        SetRef(so, "descriptionText", desc);
        SetRef(so, "progressBar", bar);
        SetRef(so, "resultText", result);
        SetRef(so, "failureHintText", hint);
        so.ApplyModifiedProperties();

        return SavePrefab<DiagnosticTestRowView>(go, "DiagnosticTestRow");
    }

    private static ChecklistStatusRowView BuildStatusRowPrefab()
    {
        var go = NewRow("ChecklistStatusRow", 46f, out RectTransform rt);

        var label = Col(rt, "LabelText", 0.06f, 0.90f, 8f, 0f, 0f, 17f, DarkText, false);

        var tick = Marker(rt, "CheckIcon", 0.01f, 0.05f, PassGreen);
        var cross = Marker(rt, "CrossIcon", 0.01f, 0.05f, FailRed);

        var view = go.AddComponent<ChecklistStatusRowView>();
        var so = new SerializedObject(view);
        SetRef(so, "labelText", label);
        SetRef(so, "checkIcon", tick);
        SetRef(so, "crossIcon", cross);
        so.ApplyModifiedProperties();

        return SavePrefab<ChecklistStatusRowView>(go, "ChecklistStatusRow");
    }

    // ------------------------------------------------------------------

    private static GameObject NewRow(string name, float height, out RectTransform rt)
    {
        var go = new GameObject(name, typeof(RectTransform));
        rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(1100f, height);

        var bg = go.AddComponent<Image>();
        bg.color = RowAlt;

        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        le.minHeight = height;

        return go;
    }

    /// <summary>
    /// A prefab, not a scene object. Every one of these views calls Instantiate on its
    /// rowPrefab each time it repopulates - a scene reference would clone whatever state that
    /// object happened to be in, including being switched off.
    /// </summary>
    private static T SavePrefab<T>(GameObject go, string fileName) where T : Component
    {
        EnsureFolder(PrefabFolder);

        string path = PrefabFolder + "/" + fileName + ".prefab";
        var saved = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);

        return saved != null ? saved.GetComponent<T>() : null;
    }

    /// <summary>A column inside a row, placed by horizontal fraction so rows scale with the
    /// window instead of assuming a pixel width.</summary>
    private static TextMeshProUGUI Col(Transform parent, string name, float x0, float x1,
                                       float left, float top, float bottom, float size,
                                       Color color, bool bold)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(x0, 0f);
        rt.anchorMax = new Vector2(x1, 1f);
        rt.offsetMin = new Vector2(left, bottom);
        rt.offsetMax = new Vector2(-6f, -top);
        rt.localScale = Vector3.one;

        var label = go.AddComponent<TextMeshProUGUI>();
        ApplyFont(label);
        label.fontSize = size;
        label.color = color;
        label.alignment = TextAlignmentOptions.Left;
        label.text = "";
        label.enableAutoSizing = false;
        label.raycastTarget = false;
        if (bold) label.fontStyle = FontStyles.Bold;

        return label;
    }

    private static Slider FlatBar(Transform parent, string name, float x0, float x1)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(x0, 0.5f);
        rt.anchorMax = new Vector2(x1, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(0f, -8f);
        rt.offsetMax = new Vector2(0f, 8f);
        rt.localScale = Vector3.one;

        var track = go.AddComponent<Image>();
        track.color = Hex(0xE6E6E6);
        track.raycastTarget = false;

        var slider = go.AddComponent<Slider>();
        slider.transition = Selectable.Transition.None;
        slider.interactable = false;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.wholeNumbers = false;
        slider.direction = Slider.Direction.LeftToRight;

        var area = ChildStretch(rt, "Fill Area", 1f, 1f, 1f, 1f);
        var fill = ChildStretch(area, "Fill", 0f, 0f, 0f, 0f);
        var fillImg = fill.gameObject.AddComponent<Image>();
        fillImg.color = Accent;
        fillImg.raycastTarget = false;

        slider.fillRect = fill;
        slider.value = 0f;
        return slider;
    }

    private static Button RowButton(Transform parent, string name, float x0, float x1, string text)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(x0, 0.5f);
        rt.anchorMax = new Vector2(x1, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(0f, -17f);
        rt.offsetMax = new Vector2(0f, 17f);
        rt.localScale = Vector3.one;

        var img = go.AddComponent<Image>();
        img.color = ButtonFace;

        var outline = go.AddComponent<Outline>();
        outline.effectColor = ButtonBorder;
        outline.effectDistance = new Vector2(1f, -1f);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;

        var label = ChildStretch(rt, "Label", 2f, 2f, 2f, 2f).gameObject.AddComponent<TextMeshProUGUI>();
        ApplyFont(label);
        label.fontSize = 15f;
        label.color = DarkText;
        label.alignment = TextAlignmentOptions.Center;
        label.text = text;
        label.raycastTarget = false;

        return btn;
    }

    /// <summary>
    /// A small coloured square standing in for a tick or a cross.
    ///
    /// Drawn rather than typed: whether a font has U+2713 is not something to gamble a status
    /// indicator on, and TMP's fallback is a hollow box plus a console warning per character.
    /// A shape reads the same in any font.
    /// </summary>
    private static GameObject Marker(Transform parent, string name, float x0, float x1, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(x0, 0.5f);
        rt.anchorMax = new Vector2(x1, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(0f, -9f);
        rt.offsetMax = new Vector2(0f, 9f);
        rt.localScale = Vector3.one;

        var img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;

        return go;
    }

    private static RectTransform ChildStretch(Transform parent, string name,
                                              float l, float t, float r, float b)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(l, b);
        rt.offsetMax = new Vector2(-r, -t);
        rt.localScale = Vector3.one;
        return rt;
    }

    private static void SetRef(SerializedObject so, string field, Object value)
    {
        var p = so.FindProperty(field);
        if (p == null)
        {
            Debug.LogWarning("[COC1DesktopScreens] No field '" + field + "' on " + so.targetObject);
            return;
        }
        p.objectReferenceValue = value;
    }

    // ==================================================================
    // Primitives
    // ==================================================================

    private struct Frame
    {
        public Transform root;
        public float ox, oy;
    }

    private static RectTransform Stretch(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);

        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        return rt;
    }

    private static RectTransform Place(Frame f, string name, float x, float y, float w, float h)
    {
        float ax = f.ox + x;
        float ay = f.oy + y;

        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);

        var rt = (RectTransform)go.transform;
        rt.SetParent(f.root, false);          // always the screen root, never a nested box

        rt.anchorMin = new Vector2(ax / DW, 1f - (ay + h) / DH);
        rt.anchorMax = new Vector2((ax + w) / DW, 1f - ay / DH);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        return rt;
    }

    private static Image Box(Frame f, string name, float x, float y, float w, float h, Color c)
    {
        var rt = Place(f, name, x, y, w, h);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = c;
        img.raycastTarget = false;      // drawn scenery must never intercept a click
        return img;
    }

    private static TextMeshProUGUI Text(Frame f, string name, float x, float y, float w, float h,
                                        string text, float size, Color color,
                                        TextAlignmentOptions align = TextAlignmentOptions.Left,
                                        bool bold = false)
    {
        var rt = Place(f, name, x, y, w, h);
        var label = rt.gameObject.AddComponent<TextMeshProUGUI>();

        ApplyFont(label);
        label.fontSize = size;
        label.color = color;
        label.alignment = align;
        label.text = text;
        label.enableAutoSizing = false;
        label.raycastTarget = false;
        if (bold) label.fontStyle = FontStyles.Bold;

        return label;
    }

    private static Button FlatButton(Frame f, string name, float x, float y, float w, float h,
                                     string text)
    {
        var rt = Place(f, name, x, y, w, h);

        var img = rt.gameObject.AddComponent<Image>();
        img.color = ButtonFace;

        var outline = rt.gameObject.AddComponent<Outline>();
        outline.effectColor = ButtonBorder;
        outline.effectDistance = new Vector2(1f, -1f);

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;

        var label = ChildStretch(rt, "Label", 2f, 2f, 2f, 2f).gameObject.AddComponent<TextMeshProUGUI>();
        ApplyFont(label);
        label.fontSize = 16f;
        label.color = DarkText;
        label.alignment = TextAlignmentOptions.Center;
        label.text = text;
        label.raycastTarget = false;

        return btn;
    }

    /// <summary>A TMP text added from an Editor script has not run its own Awake, so anything
    /// that parses it before a font is resolved throws. Always set one explicitly.</summary>
    private static void ApplyFont(TMP_Text label)
    {
        if (Font != null) label.font = Font;
        else if (TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string[] parts = path.Split('/');
        string running = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = running + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(running, parts[i]);
            running = next;
        }
    }

    private static Color Hex(int rgb)
    {
        return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
#endif