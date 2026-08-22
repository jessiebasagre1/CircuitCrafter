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
    public const float DW = 1920f;
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
        SetupWindow(s, "Windows Setup");

        // The partition UI lives inside this skin, so it is only ever reachable on the stage
        // that uses this skin. OSInstallView switches the panel itself on and off.
        var panel = Stretch(s.root, "PartitionSelectPanel");
        var p = new Frame { root = panel, ox = 0f, oy = 0f };

        Box(p, "HeaderRow", 500, 362, 920, 30, TableHead);
        Text(p, "ColName", 512, 362, 420, 30, "Name", 15, MutedText);
        Text(p, "ColTotal", 950, 362, 150, 30, "Total Size", 15, MutedText);
        Text(p, "ColFree", 1110, 362, 150, 30, "Free Space", 15, MutedText);
        Text(p, "ColType", 1270, 362, 140, 30, "Type", 15, MutedText);

        var listBorder = Box(p, "ListBorder", 500, 394, 920, 250, Rule).rectTransform;
        var list = Box(p, "RowsContent", 501, 395, 918, 248, WindowFill).rectTransform;

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

        b.newBtn = FlatButton(p, "NewButton", 500, 664, 140, 38, "New");
        b.formatBtn = FlatButton(p, "FormatButton", 652, 664, 140, 38, "Format");
        b.deleteBtn = FlatButton(p, "DeleteButton", 804, 664, 140, 38, "Delete");

        b.errorText = Text(p, "ErrorText", 500, 712, 920, 30, "", 17, ErrorRed);
        b.errorText.gameObject.SetActive(false);

        b.nextBtn = FlatButton(p, "NextButton", 1230, 820, 190, 48, "Next");

        b.partitionPanel = panel.gameObject;
        b.partitionView = Undo.AddComponent<PartitionSelectView>(panel.gameObject);
        panel.gameObject.SetActive(false);

        b.skins.Add(new SkinSpec
        {
            name = "Partition",
            root = s.root.gameObject,
            stages = new[] { OSInstallManager.InstallStage.PartitionSelect },
            label = N(500, 236, 920, 110),
            dropdown = N(500, 900, 660, 46),      // parked - the view hides it on this stage
            cont = N(1230, 900, 190, 48),
            progress = N(500, 900, 920, 22),
            labelColor = DarkText,
            labelSize = 19f,
            labelAlign = TextAlignmentOptions.TopLeft
        });
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
    private static void SetupWindow(Frame s, string title)
    {
        Box(s, "Backdrop", 0, 0, DW, DH, SetupBackdrop);

        Box(s, "WindowBorder", 460, 150, 1000, 760, WindowBorder);
        Box(s, "WindowFill", 461, 151, 998, 758, WindowFill);

        Box(s, "TitleStrip", 461, 151, 998, 44, TitleStrip);
        Text(s, "WindowTitle", 480, 151, 600, 44, title, 18, DarkText);
        Box(s, "TitleRule", 461, 195, 998, 1, Rule);
        Box(s, "FooterRule", 461, 800, 998, 1, Rule);
    }

    // ==================================================================
    // Shared controls
    // ==================================================================

    private static void BuildControls(RectTransform panel, Built b)
    {
        var controls = Stretch(panel, "Controls");

        b.stageLabel = COC1WinWidgets.Label(controls, "StageLabel", 0f, 0f, 800f, 300f, "",
                                            TextAlignmentOptions.TopLeft);
        b.dropdown = COC1WinWidgets.FlatDropdown(controls, "ChoiceDropdown", 0f, 0f, 600f, 46f, "");
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

        var name = ChildLabel(rt, "NameText", 12f, 0.00f, 0.46f, DarkText, 16f);
        var total = ChildLabel(rt, "TotalSizeText", 8f, 0.46f, 0.62f, DarkText, 16f);
        var free = ChildLabel(rt, "FreeSpaceText", 8f, 0.62f, 0.78f, DarkText, 16f);
        var type = ChildLabel(rt, "TypeText", 8f, 0.78f, 1.00f, MutedText, 16f);

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

        label.fontSize = size;
        label.color = color;
        label.alignment = TextAlignmentOptions.Left;
        label.text = "";
        label.enableAutoSizing = false;
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
        img.color = Hex(0xE1E1E1);

        var outline = rt.gameObject.AddComponent<Outline>();
        outline.effectColor = Hex(0xADADAD);
        outline.effectDistance = new Vector2(1f, -1f);

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;

        var label = ChildStretch(rt, "Label", 2f, 2f, 2f, 2f).gameObject.AddComponent<TextMeshProUGUI>();
        if (Font != null) label.font = Font;
        else if (TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;
        label.fontSize = 17f;
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
