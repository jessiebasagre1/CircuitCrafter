#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws the OS install screen: six looks that the 17 stages cycle through, the shared controls
/// they all reuse, the interactive partition table, and its row prefab.
///
/// THE SIX LOOKS
///   TextMode   - black console. Power on, POST, booting the installer.
///   Bios       - the blue firmware setup utility. The boot-order stage.
///   Wizard     - the grey Windows Setup window. Language through installation type.
///   Partition  - the same window, laid out for a disk table instead of a paragraph.
///   Progress   - full-screen blue. Copying, installing, restarting, first boot.
///   Oobe       - full-screen accent blue. Region, keyboard, account, done.
///
/// That is genuinely what a Windows 10 install looks like; seventeen separate panels would be
/// eleven more sets of references for no extra fidelity.
///
/// ----------------------------------------------------------------------------------
/// NOTHING IS NESTED - same rule as COC1BrowserScreens, and for the same hard-won reason.
/// Anchors are FRACTIONS OF THE PARENT, so a child placed at "x / 1920" inside a 1000px window
/// lands at a fraction of 1000, not of the screen. Containers are authored in LOCAL
/// coordinates through the Frame struct for readability, but every element is parented to the
/// screen root and positioned in ABSOLUTE design space.
/// ----------------------------------------------------------------------------------
///
/// The helpers below are a deliberate copy of the ones in COC1BrowserScreens rather than a
/// shared dependency: these are generate-once Editor tools, and a shared base class between
/// two of them is exactly the thing that breaks one while you are editing the other.
/// </summary>
public static class COC1SetupScreens
{

    // ------------------------------------------------------------------
    // TEXT SIZE
    // ------------------------------------------------------------------

    /// <summary>
    /// Multiplies every font size on the OS install stage screens - the POST console, the firmware
    /// setup, the Windows Setup window, the partition table, the progress screen and the
    /// out-of-box screens. 1 is the original look; 1.35 is a comfortable start for a phone.
    ///
    /// ONE NUMBER, NOT EVERY CALL SITE EDITED. Every label here is created through the two helpers
    /// below, so scaling at that point reaches all of them and cannot miss one - and the sizes
    /// passed in stay as written, so the relative hierarchy of heading, body and caption is
    /// preserved rather than drifting as numbers get rounded by hand.
    ///
    /// static readonly, not const: a const makes the FontScale &lt;= 1 branch provably dead and the
    /// compiler warns about unreachable code on every recompile.
    /// </summary>
    // BACK TO 1 ON PURPOSE. Scaling the numbers here was the wrong lever: the boxes on these
    // screens are positioned by normalised anchors and resize with the panel, while a font size is
    // an absolute number, so raising it pushed text through boxes that had not grown with it.
    // OSInstallTextFit on the install panel does the job properly - it lets each label grow only
    // as far as its own box allows. Leave this at 1 unless that component has been removed.
    public static readonly float FontScale = 1f;

    /// <summary>
    /// Applies the scaled size, with auto-sizing as the safety net.
    ///
    /// A bigger number alone is what breaks a layout: a label that exactly filled its box at 14
    /// does not fit at 19, and it then overlaps its neighbour or is clipped. Giving TMP a RANGE -
    /// the original size as the floor, the scaled size as the ceiling - means each label grows as
    /// far as its own box allows and falls back toward the original where there is no room.
    /// </summary>
    private static void SetScaledFontSize(TextMeshProUGUI label, float size)
    {
        float scaled = size * FontScale;

        if (FontScale <= 1f)
        {
            label.fontSize = size;
            label.enableAutoSizing = false;
            return;
        }

        label.fontSize = scaled;
        label.fontSizeMin = size;
        label.fontSizeMax = scaled;
        label.enableAutoSizing = true;
    }
    public const float DW = 1920f;

    // ------------------------------------------------------------------
    // PARTITION TABLE GEOMETRY
    //
    // THE COLUMN FRACTIONS LIVE HERE, ONCE, because the header row and the row prefab are built
    // by two different methods and were drifting apart. The headers were placed at hand-typed x
    // positions (512, 950, 1110, 1270) while the rows place their columns by fraction - so
    // "Total Size" sat about 26px left of the numbers under it, and "Type" about 55px. Deriving
    // both from the same four numbers is what makes them line up, at any width.
    // ------------------------------------------------------------------

    /// <summary>Column edges as a fraction of the row's width: Name, Total, Free, Type.</summary>
    private static readonly float[] ColumnEdges = { 0.00f, 0.46f, 0.62f, 0.78f, 1.00f };

    /// <summary>Left padding inside each column, matching the row prefab's own labels.</summary>
    private const float NameColumnPad = 12f;
    private const float ColumnPad = 8f;

    /// <summary>The partition window is wider than the wizard's. A disk table has four columns and
    /// real Windows Setup gives it the room; at the wizard's 1000 the sizes crowd the names.</summary>
    private const float PartitionWindowW = 1400f;

    private const float WindowPad = 40f;
    public const float DH = 1080f;

    private const string UndoLabel = "COC1 Build Install Screens";
    private const string PrefabFolder = "Assets/COC1/Content/Prefabs";

    public static TMP_FontAsset Font;

    // ==================================================================
    // Palette
    // ==================================================================

    private static readonly Color Black = Hex(0x000000);
    private static readonly Color ConsoleText = Hex(0xCCCCCC);
    private static readonly Color BiosBlue = Hex(0x0000AA);
    private static readonly Color BiosBar = Hex(0xC0C0C0);
    private static readonly Color BiosText = Hex(0xE8E8E8);
    private static readonly Color SetupBackdrop = Hex(0x1B4C7A);
    private static readonly Color WindowFill = Hex(0xFFFFFF);
    private static readonly Color WindowBorder = Hex(0x7A7A7A);
    private static readonly Color TitleStrip = Hex(0xF0F0F0);
    private static readonly Color Rule = Hex(0xDADCE0);
    private static readonly Color DarkText = Hex(0x202124);
    private static readonly Color MutedText = Hex(0x5F6368);
    private static readonly Color ProgressBlue = Hex(0x0067B8);
    private static readonly Color OobeBlue = Hex(0x0078D4);
    private static readonly Color ErrorRed = Hex(0xC42B1C);
    private static readonly Color TableHead = Hex(0xF1F3F4);

    // ==================================================================
    // Results
    // ==================================================================

    public class SkinSpec
    {
        public string name;
        public GameObject root;
        public OSInstallManager.InstallStage[] stages;
        public Rect label, dropdown, cont, progress;
        public Color labelColor = Color.white;
        public float labelSize = 22f;
        public TextAlignmentOptions labelAlign = TextAlignmentOptions.TopLeft;
    }

    public class Built
    {
        public List<SkinSpec> skins = new List<SkinSpec>();

        public TMP_Text stageLabel;
        public TMP_Dropdown dropdown;
        public Button continueButton;
        public Slider progressBar;
        public GameObject rebootOverlay;

        public GameObject partitionPanel;
        public PartitionSelectView partitionView;
        public Transform rowsContent;
        public ToggleGroup toggleGroup;
        public Button newBtn, formatBtn, deleteBtn, nextBtn;
        public TMP_Text errorText;
        public PartitionRowView rowPrefab;
    }

    // ==================================================================
    // Entry point
    // ==================================================================

    public static Built BuildAll(RectTransform panel)
    {
        var b = new Built();

        var skinsRoot = Stretch(panel, "Skins");

        BuildTextMode(skinsRoot, b);
        BuildBios(skinsRoot, b);
        BuildWizard(skinsRoot, b);
        BuildPartition(skinsRoot, b);
        BuildProgress(skinsRoot, b);
        BuildOobe(skinsRoot, b);

        // Controls come AFTER the skins so they paint on top of every backdrop, and before the
        // reboot overlay so the overlay can black the whole thing out.
        BuildControls(panel, b);

        b.rebootOverlay = FullScreen(panel, "RebootOverlay", Black).gameObject;
        b.rebootOverlay.SetActive(false);

        b.rowPrefab = BuildRowPrefab();

        return b;
    }

    // ==================================================================
    // Skins
    // ==================================================================

    private static void BuildTextMode(Transform root, Built b)
    {
        var s = Screen(root, "Skin_TextMode");
        Box(s, "Backdrop", 0, 0, DW, DH, Black);

        b.skins.Add(new SkinSpec
        {
            name = "TextMode",
            root = s.root.gameObject,
            stages = new[]
            {
                OSInstallManager.InstallStage.PowerOn,
                OSInstallManager.InstallStage.POST,
                OSInstallManager.InstallStage.BootMediaCheck
            },
            label = N(120, 100, 1680, 700),
            dropdown = N(120, 840, 700, 44),
            cont = N(900, 840, 220, 44),
            progress = N(120, 900, 700, 18),
            labelColor = ConsoleText,
            labelSize = 22f,
            labelAlign = TextAlignmentOptions.TopLeft
        });
    }

    private static void BuildBios(Transform root, Built b)
    {
        var s = Screen(root, "Skin_Bios");
        Box(s, "Backdrop", 0, 0, DW, DH, BiosBlue);

        Box(s, "TopBar", 0, 0, DW, 56, BiosBar);
        Text(s, "TopBarTitle", 0, 0, DW, 56, "BIOS SETUP UTILITY", 24, Black,
             TextAlignmentOptions.Center, true);

        Text(s, "MenuStrip", 120, 70, 1680, 34,
             "Main     Advanced     [ Boot ]     Security     Exit", 20, BiosText);

        // Bordered content well, the way a text-mode firmware UI frames its list.
        Box(s, "WellBorder", 120, 120, 1680, 720, BiosBar);
        Box(s, "WellFill", 122, 122, 1676, 716, BiosBlue);

        Box(s, "HelpBar", 0, 1010, DW, 50, BiosBar);
        Text(s, "HelpText", 40, 1010, 1840, 50,
             "↑↓ Select Item     Enter: Change     F10: Save & Exit     ESC: Exit",
             18, Black);

        b.skins.Add(new SkinSpec
        {
            name = "Bios",
            root = s.root.gameObject,
            stages = new[] { OSInstallManager.InstallStage.BiosSetup },
            label = N(180, 180, 1560, 520),
            dropdown = N(180, 730, 760, 46),
            cont = N(980, 730, 220, 46),
            progress = N(180, 800, 700, 18),
            labelColor = BiosText,
            labelSize = 22f,
            labelAlign = TextAlignmentOptions.TopLeft
        });
    }

    private static void BuildWizard(Transform root, Built b)
    {
        var s = Screen(root, "Skin_Wizard");
        SetupWindow(s, "Windows Setup");

        b.skins.Add(new SkinSpec
        {
            name = "Wizard",
            root = s.root.gameObject,
            stages = new[]
            {
                OSInstallManager.InstallStage.LanguageSelect,
                OSInstallManager.InstallStage.ProductKey,
                OSInstallManager.InstallStage.EditionSelect,
                OSInstallManager.InstallStage.LicenseTerms,
                OSInstallManager.InstallStage.InstallType
            },
            label = N(500, 236, 920, 380),
            dropdown = N(500, 650, 660, 46),
            cont = N(1230, 820, 190, 48),
            progress = N(500, 650, 920, 22),
            labelColor = DarkText,
            labelSize = 20f,
            labelAlign = TextAlignmentOptions.TopLeft
        });
    }

    private static void BuildPartition(Transform root, Built b)
    {
        var s = Screen(root, "Skin_Partition");
        SetupWindow(s, "Windows Setup", PartitionWindowW);

        // The partition UI lives inside this skin, so it is only ever reachable on the stage
        // that uses this skin. OSInstallView switches the panel itself on and off.
        var panel = Stretch(s.root, "PartitionSelectPanel");
        var p = new Frame { root = panel, ox = 0f, oy = 0f };

        // EVERY NUMBER BELOW IS DERIVED, NOT TYPED. Change PartitionWindowW and the table, its
        // headers, the buttons and Next all follow - which is the only way four columns and four
        // headers stay lined up after a width change.
        float winX = Mathf.Round((DW - PartitionWindowW) * 0.5f);
        float cx = winX + WindowPad;                 // content left
        float cw = PartitionWindowW - WindowPad * 2; // content width

        float listX = cx + 1f;                       // inside the 1px border
        float listW = cw - 2f;

        // The rows sit inside the list's VerticalLayoutGroup padding, set to 4 below. The headers
        // have to measure from the same place the rows start, or they line up with the list and
        // not with the text in it.
        float rowX = listX + 4f;
        float rowW = listW - 8f;

        Box(p, "HeaderRow", cx, 362, cw, 30, TableHead);

        Column(p, "ColName", "Name", rowX, rowW, 0, NameColumnPad);
        Column(p, "ColTotal", "Total Size", rowX, rowW, 1, ColumnPad);
        Column(p, "ColFree", "Free Space", rowX, rowW, 2, ColumnPad);
        Column(p, "ColType", "Type", rowX, rowW, 3, ColumnPad);

        var listBorder = Box(p, "ListBorder", cx, 394, cw, 250, Rule).rectTransform;
        var list = Box(p, "RowsContent", listX, 395, listW, 248, WindowFill).rectTransform;

        // PartitionSelectView only instantiates rows into this transform - it does no layout of
        // its own, so the stacking has to come from a layout group here.
        var vlg = list.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 2f;
        vlg.padding = new RectOffset(4, 4, 4, 4);
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;

        b.rowsContent = list;
        b.toggleGroup = list.gameObject.AddComponent<ToggleGroup>();
        b.toggleGroup.allowSwitchOff = false;

        b.newBtn = FlatButton(p, "NewButton", cx, 664, 140, 38, "New");
        b.formatBtn = FlatButton(p, "FormatButton", cx + 152f, 664, 140, 38, "Format");
        b.deleteBtn = FlatButton(p, "DeleteButton", cx + 304f, 664, 140, 38, "Delete");

        b.errorText = Text(p, "ErrorText", cx, 712, cw, 30, "", 17, ErrorRed);
        b.errorText.gameObject.SetActive(false);

        // Right-aligned against the window, where Setup puts it.
        float nextX = winX + PartitionWindowW - WindowPad - 190f;
        b.nextBtn = FlatButton(p, "NextButton", nextX, 820, 190, 48, "Next");

        b.partitionPanel = panel.gameObject;
        b.partitionView = Undo.AddComponent<PartitionSelectView>(panel.gameObject);
        panel.gameObject.SetActive(false);

        b.skins.Add(new SkinSpec
        {
            name = "Partition",
            root = s.root.gameObject,
            stages = new[] { OSInstallManager.InstallStage.PartitionSelect },
            label = N(cx, 236, cw, 110),
            dropdown = N(cx, 900, 660, 46),       // parked - the view hides it on this stage
            cont = N(nextX, 900, 190, 48),
            progress = N(cx, 900, cw, 22),
            labelColor = DarkText,
            labelSize = 19f,
            labelAlign = TextAlignmentOptions.TopLeft
        });
    }

    /// <summary>
    /// One header cell, placed from the same fractions the row prefab uses for its columns.
    ///
    /// The pad matches the row's own label inset, so the header text sits directly above the text
    /// beneath it rather than above the edge of its cell.
    /// </summary>
    private static void Column(Frame f, string name, string text, float rowX, float rowW,
                               int index, float pad)
    {
        float x0 = rowX + ColumnEdges[index] * rowW;
        float x1 = rowX + ColumnEdges[index + 1] * rowW;

        Text(f, name, x0 + pad, 362, x1 - x0 - pad, 30, text, 15, MutedText);
    }

    private static void BuildProgress(Transform root, Built b)
    {
        var s = Screen(root, "Skin_Progress");
        Box(s, "Backdrop", 0, 0, DW, DH, ProgressBlue);

        b.skins.Add(new SkinSpec
        {
            name = "Progress",
            root = s.root.gameObject,
            stages = new[]
            {
                OSInstallManager.InstallStage.Copying,
                OSInstallManager.InstallStage.Installing,
                OSInstallManager.InstallStage.Reboot,
                OSInstallManager.InstallStage.FirstBoot
            },
            label = N(360, 360, 1200, 240),
            dropdown = N(660, 760, 600, 46),
            cont = N(860, 830, 200, 48),
            progress = N(660, 650, 600, 16),
            labelColor = Color.white,
            labelSize = 30f,
            labelAlign = TextAlignmentOptions.Center
        });
    }

    private static void BuildOobe(Transform root, Built b)
    {
        var s = Screen(root, "Skin_Oobe");
        Box(s, "Backdrop", 0, 0, DW, DH, OobeBlue);

        b.skins.Add(new SkinSpec
        {
            name = "Oobe",
            root = s.root.gameObject,
            stages = new[]
            {
                OSInstallManager.InstallStage.RegionKeyboard,
                OSInstallManager.InstallStage.AccountSetup,
                OSInstallManager.InstallStage.Complete
            },
            label = N(460, 260, 1000, 280),
            dropdown = N(560, 590, 800, 48),
            cont = N(860, 690, 200, 48),
            progress = N(660, 700, 600, 16),
            labelColor = Color.white,
            labelSize = 26f,
            labelAlign = TextAlignmentOptions.Center
        });
    }

    /// <summary>The grey Setup window, shared by the Wizard and Partition looks.</summary>
    private static void SetupWindow(Frame s, string title, float winW = 1000f)
    {
        // Centred rather than pinned at 460, so a wider window stays in the middle of the screen
        // instead of growing off the right edge.
        float x = Mathf.Round((DW - winW) * 0.5f);

        Box(s, "Backdrop", 0, 0, DW, DH, SetupBackdrop);

        Box(s, "WindowBorder", x, 150, winW, 760, WindowBorder);
        Box(s, "WindowFill", x + 1, 151, winW - 2, 758, WindowFill);

        Box(s, "TitleStrip", x + 1, 151, winW - 2, 44, TitleStrip);

        // Bold and two points up on the body text. A window title that is the same weight and size
        // as the paragraph under it is not a title - and real Windows Setup does draw it heavier.
        Text(s, "WindowTitle", x + 20, 151, 600, 44, title, 20, DarkText,
             TextAlignmentOptions.Left, true);

        Box(s, "TitleRule", x + 1, 195, winW - 2, 1, Rule);
        Box(s, "FooterRule", x + 1, 800, winW - 2, 1, Rule);
    }

    // ==================================================================
    // Shared controls
    // ==================================================================

    private static void BuildControls(RectTransform panel, Built b)
    {
        var controls = Stretch(panel, "Controls");

        b.stageLabel = COC1WinWidgets.Label(controls, "StageLabel", 0f, 0f, 800f, 300f, "",
                                            TextAlignmentOptions.TopLeft);
        // 20 to match the wizard skin's labelSize - the paragraph above the dropdown. The list
        // rows grow with it; a 20pt option in a 22px row is clipped, not small.
        b.dropdown = COC1WinWidgets.FlatDropdown(controls, "ChoiceDropdown", 0f, 0f, 600f, 46f, "",
                                                 200f, 20f, 36f);
        b.continueButton = COC1WinWidgets.FlatButton(controls, "ContinueButton", 0f, 0f, 190f, 48f,
                                                     "Next", out TextMeshProUGUI _);
        b.progressBar = COC1WinWidgets.FlatProgressBar(controls, "ProgressBar", 0f, 0f, 600f, 20f,
                                                       COC1WinWidgets.Accent);

        // Positions here are placeholders. OSInstallSkinner moves all four to the current
        // skin's rects the moment the first stage fires.
    }

    // ==================================================================
    // The partition row prefab
    // ==================================================================

    /// <summary>
    /// Builds one row, saves it as a prefab asset, and removes the scene copy.
    ///
    /// PartitionSelectView.rowPrefab has to be a PREFAB, not a scene object: it calls
    /// Instantiate on it every time the table rebuilds, and a scene reference would clone
    /// whatever state that object happened to be in - including being switched off.
    /// </summary>
    private static PartitionRowView BuildRowPrefab()
    {
        EnsureFolder(PrefabFolder);

        var go = new GameObject("PartitionRow", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(900f, 34f);

        var bg = go.AddComponent<Image>();
        bg.color = WindowFill;

        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 34f;
        le.minHeight = 34f;

        var toggle = go.AddComponent<Toggle>();
        toggle.targetGraphic = bg;
        toggle.isOn = false;

        // A tinted strip that is only visible while this row is the selected one. Using the
        // Toggle's graphic slot rather than a colour tint keeps the row readable either way.
        var check = ChildStretch(rt, "Selected", 0f, 0f, 0f, 0f);
        var checkImg = check.gameObject.AddComponent<Image>();
        checkImg.color = new Color(0.80f, 0.91f, 1f, 1f);
        checkImg.raycastTarget = false;
        toggle.graphic = checkImg;

        // The same ColumnEdges the headers use. Typing 0.46 again here is how they drifted apart
        // the first time.
        var name = ChildLabel(rt, "NameText", NameColumnPad,
                              ColumnEdges[0], ColumnEdges[1], DarkText, 16f);
        var total = ChildLabel(rt, "TotalSizeText", ColumnPad,
                               ColumnEdges[1], ColumnEdges[2], DarkText, 16f);
        var free = ChildLabel(rt, "FreeSpaceText", ColumnPad,
                              ColumnEdges[2], ColumnEdges[3], DarkText, 16f);
        var type = ChildLabel(rt, "TypeText", ColumnPad,
                              ColumnEdges[3], ColumnEdges[4], MutedText, 16f);

        var view = go.AddComponent<PartitionRowView>();
        var so = new SerializedObject(view);
        SetRef(so, "toggle", toggle);
        SetRef(so, "nameText", name);
        SetRef(so, "totalSizeText", total);
        SetRef(so, "freeSpaceText", free);
        SetRef(so, "typeText", type);
        so.ApplyModifiedProperties();

        string path = PrefabFolder + "/PartitionRow.prefab";
        var saved = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);

        return saved != null ? saved.GetComponent<PartitionRowView>() : null;
    }

    private static RectTransform ChildStretch(Transform parent, string name,
                                              float l, float t, float r, float btm)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(l, btm);
        rt.offsetMax = new Vector2(-r, -t);
        rt.localScale = Vector3.one;
        return rt;
    }

    /// <summary>A column inside the row, placed by horizontal fraction so the row scales.</summary>
    private static TextMeshProUGUI ChildLabel(Transform parent, string name, float pad,
                                              float x0, float x1, Color color, float size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(x0, 0f);
        rt.anchorMax = new Vector2(x1, 1f);
        rt.offsetMin = new Vector2(pad, 0f);
        rt.offsetMax = new Vector2(-4f, 0f);
        rt.localScale = Vector3.one;

        var label = go.AddComponent<TextMeshProUGUI>();
        if (Font != null) label.font = Font;
        else if (TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;

        SetScaledFontSize(label, size);
        label.color = color;
        label.alignment = TextAlignmentOptions.Left;
        label.text = "";
        // enableAutoSizing is owned by SetScaledFontSize above: it switches auto-sizing ON
        // to hold the scaled size inside this label's own box. Setting it false here would
        // undo that a few lines after it was applied.
        label.raycastTarget = false;
        return label;
    }

    private static void SetRef(SerializedObject so, string field, Object value)
    {
        var p = so.FindProperty(field);
        if (p == null)
        {
            Debug.LogWarning("[COC1SetupScreens] PartitionRowView has no field '" + field + "'.");
            return;
        }
        p.objectReferenceValue = value;
    }

    // ==================================================================
    // Primitives - design pixels in, normalised anchors out
    // ==================================================================

    private struct Frame
    {
        public Transform root;
        public float ox, oy;
    }

    private static Frame Screen(Transform root, string name)
    {
        var rt = Stretch(root, name);
        rt.gameObject.SetActive(false);
        return new Frame { root = rt, ox = 0f, oy = 0f };
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

    private static Image FullScreen(Transform parent, string name, Color c)
    {
        var rt = Stretch(parent, name);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = c;
        return img;
    }

    public static Rect N(float x, float y, float w, float h)
    {
        return new Rect(x / DW, y / DH, w / DW, h / DH);
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
        img.raycastTarget = false;
        return img;
    }

    private static TextMeshProUGUI Text(Frame f, string name, float x, float y, float w, float h,
                                        string text, float size, Color color,
                                        TextAlignmentOptions align = TextAlignmentOptions.Left,
                                        bool bold = false)
    {
        var rt = Place(f, name, x, y, w, h);
        var label = rt.gameObject.AddComponent<TextMeshProUGUI>();

        if (Font != null) label.font = Font;
        else if (TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;

        SetScaledFontSize(label, size);
        label.color = color;
        label.alignment = align;
        label.text = text;
        // enableAutoSizing is owned by SetScaledFontSize above: it switches auto-sizing ON
        // to hold the scaled size inside this label's own box. Setting it false here would
        // undo that a few lines after it was applied.
        label.raycastTarget = false;
        if (bold) label.fontStyle = FontStyles.Bold;

        return label;
    }

    private static Button FlatButton(Frame f, string name, float x, float y, float w, float h,
                                     string text)
    {
        var rt = Place(f, name, x, y, w, h);

        var img = rt.gameObject.AddComponent<Image>();
        img.color = Hex(0xE1E1E1);

        var outline = rt.gameObject.AddComponent<Outline>();
        outline.effectColor = Hex(0xADADAD);
        outline.effectDistance = new Vector2(1f, -1f);

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;

        var label = ChildStretch(rt, "Label", 2f, 2f, 2f, 2f).gameObject.AddComponent<TextMeshProUGUI>();
        if (Font != null) label.font = Font;
        else if (TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;
        SetScaledFontSize(label, 17f);
        label.color = DarkText;
        label.alignment = TextAlignmentOptions.Center;
        label.text = text;
        label.raycastTarget = false;

        return btn;
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