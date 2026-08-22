#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Rebuilds COC 1's station panels as drawn Windows UI.
///
/// Tools > COC 1 > Panel Builder
///
/// PHASE 1 (this file, so far): the bootable-USB station - the Rufus window, drawn in Unity
/// UI rather than sat behind a screenshot. Phases 2 (Windows Setup) and 3 (the post-install
/// desktop apps) land in this same window.
///
/// CLEAR AND REBUILD IN PLACE
/// The panel GameObject you point this at is KEPT - only its children are replaced. Anything
/// already referencing the panel itself (DesktopIconButton.targetPanel, a station camera, an
/// animation) stays connected. What lives on the children does not survive, so this reads the
/// settings worth keeping first (which BootableUSBCreator, which desktop to return to, the
/// highlight ring prefab) and puts them back afterwards.
///
/// It asks before deleting anything, and the whole rebuild is one undo step.
/// </summary>
public class COC1PanelBuilder : EditorWindow
{
    [Tooltip("Inter or Open Sans as a TMP Font Asset. Leave empty for the TMP default.")]
    private TMP_FontAsset uiFont;

    private GameObject usbPanel;
    private GameObject desktopPanel;
    private BootableUSBCreator creator;

    // Phase 2
    private GameObject installPanel;
    private OSInstallView osInstallView;

    // Phase 3
    private GameObject desktopAppsPanel;
    private GameObject managersObject;

    /// <summary>Managers found on objects that start switched off. Populated by the guard in
    /// RebuildDesktop so the window can offer to move them rather than just refusing.</summary>
    private readonly List<Component> sleepingManagers = new List<Component>();

    private Vector2 scroll;
    private string status = "";

    // Rufus window, in its own pixels.
    private const float WinW = 500f;
    private const float WinH = 660f;
    private const float Pad = 14f;
    private const float BodyW = WinW - 2f - Pad * 2f;      // 470

    [MenuItem("Tools/COC 1/Panel Builder")]
    public static void Open()
    {
        var w = GetWindow<COC1PanelBuilder>("COC 1 Panels");
        if (w != null) w.minSize = new Vector2(420, 520);
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("COC 1 - station panels", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Phase 1: the bootable-USB station. Draws the Rufus window in Unity UI - title bar, " +
            "Device and Boot selection dropdowns, partition scheme, file system, volume label, " +
            "status bar, START and CLOSE.\n\n" +
            "The two dropdowns, START, the progress bar and the status text are wired to your " +
            "BootableUSBScreenshotView. Everything else is drawn decoration, deliberately not " +
            "clickable - your BootableUSBCreator does not model those settings, so a learner " +
            "changing them would be changing nothing.",
            MessageType.Info);

        EditorGUILayout.Space();

        uiFont = (TMP_FontAsset)EditorGUILayout.ObjectField(
            new GUIContent("UI Font (optional)"), uiFont, typeof(TMP_FontAsset), false);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Scene objects", EditorStyles.boldLabel);

        usbPanel = (GameObject)EditorGUILayout.ObjectField(
            new GUIContent("USB Creator Panel"), usbPanel, typeof(GameObject), true);
        desktopPanel = (GameObject)EditorGUILayout.ObjectField(
            new GUIContent("Desktop Panel"), desktopPanel, typeof(GameObject), true);
        creator = (BootableUSBCreator)EditorGUILayout.ObjectField(
            new GUIContent("Bootable USB Creator"), creator, typeof(BootableUSBCreator), true);

        // Deferred, not called straight from OnGUI. Anything that touches the scene - or opens
        // a modal dialog - from inside a layout pass will corrupt GUILayout's Begin/End state
        // the moment it throws, burying the real error under "Invalid GUILayout state".
        if (GUILayout.Button("Auto-Detect"))
            EditorApplication.delayCall += AutoDetect;

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(usbPanel == null))
        {
            if (GUILayout.Button("Rebuild Bootable USB Panel (Rufus)", GUILayout.Height(30)))
                EditorApplication.delayCall += RebuildUsbPanel;
        }

        if (usbPanel == null)
            EditorGUILayout.HelpBox("Assign the USB Creator Panel to enable the rebuild.", MessageType.Warning);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Phase 2 - OS installation", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Six looks for the 17 stages: the black POST console, the blue firmware setup, the " +
            "grey Windows Setup window, the same window laid out for the disk table, the " +
            "full-screen install progress, and the blue out-of-box screens.\n\n" +
            "Also builds the interactive partition table and saves its row prefab, then wires " +
            "OSInstallView, PartitionSelectView and OSInstallSkinner.",
            MessageType.Info);

        installPanel = (GameObject)EditorGUILayout.ObjectField(
            new GUIContent("Install Panel"), installPanel, typeof(GameObject), true);
        osInstallView = (OSInstallView)EditorGUILayout.ObjectField(
            new GUIContent("OS Install View"), osInstallView, typeof(OSInstallView), true);

        using (new EditorGUI.DisabledScope(installPanel == null || osInstallView == null))
        {
            if (GUILayout.Button("Rebuild OS Install Panel", GUILayout.Height(30)))
                EditorApplication.delayCall += RebuildInstallPanel;
        }

        if (installPanel == null || osInstallView == null)
            EditorGUILayout.HelpBox("Press Auto-Detect, or assign both fields above.", MessageType.None);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Phase 3 - the post-install desktop", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Wallpaper, taskbar, five icons and five app windows: Device Manager, Software " +
            "Center, Windows Update, Diagnostics and the Service Report. Plus the three row " +
            "prefabs they spawn.\n\n" +
            "The MANAGERS are not created here - it finds the ones the Content Builder already " +
            "wired and points the views at them. They must live on an always-active object, " +
            "because they register their tasks in Start().",
            MessageType.Info);

        desktopAppsPanel = (GameObject)EditorGUILayout.ObjectField(
            new GUIContent("Desktop Panel"), desktopAppsPanel, typeof(GameObject), true);
        managersObject = (GameObject)EditorGUILayout.ObjectField(
            new GUIContent("Managers Object"), managersObject, typeof(GameObject), true);

        using (new EditorGUI.DisabledScope(desktopAppsPanel == null))
        {
            if (GUILayout.Button("Rebuild Post-Install Desktop", GUILayout.Height(30)))
                EditorApplication.delayCall += RebuildDesktop;
        }

        if (sleepingManagers.Count > 0)
        {
            EditorGUILayout.HelpBox(
                sleepingManagers.Count + " manager(s) are on panels that start switched off. " +
                "This moves them to the Managers Object with every field intact - the item " +
                "lists, group task ids and test lists all come across.",
                MessageType.Warning);

            using (new EditorGUI.DisabledScope(managersObject == null))
            {
                if (GUILayout.Button($"Move {sleepingManagers.Count} manager(s) to " +
                                     $"'{Name(managersObject)}'", GUILayout.Height(28)))
                    EditorApplication.delayCall += MoveSleepingManagers;
            }
        }

        if (!string.IsNullOrEmpty(status))
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(status, MessageType.None);
        }

        EditorGUILayout.EndScrollView();
    }

    // ==================================================================

    private void AutoDetect()
    {
        if (creator == null) creator = FindInScene<BootableUSBCreator>();

        if (usbPanel == null)
        {
            var view = FindInScene<BootableUSBScreenshotView>();
            if (view != null)
            {
                // The view usually sits ON the panel; if it is on a child, walk up to the
                // object whose name reads like a panel rather than guessing at the root.
                var t = view.transform;
                usbPanel = t.gameObject;
                if (t.parent != null && !t.name.ToLower().Contains("panel") &&
                    t.parent.name.ToLower().Contains("panel"))
                    usbPanel = t.parent.gameObject;
            }
        }

        if (desktopPanel == null)
        {
            var close = usbPanel != null ? usbPanel.GetComponentInChildren<ClosePanelButton>(true) : null;
            if (close != null)
                desktopPanel = GetObject(new SerializedObject(close), "desktopPanel") as GameObject;
        }

        if (osInstallView == null) osInstallView = FindInScene<OSInstallView>();

        if (installPanel == null && osInstallView != null)
            installPanel = GetObject(new SerializedObject(osInstallView), "installPanel") as GameObject;

        if (desktopAppsPanel == null && osInstallView != null)
            desktopAppsPanel = GetObject(new SerializedObject(osInstallView), "desktopPanel") as GameObject;

        if (managersObject == null)
        {
            var tm = FindInScene<TaskManager>();
            if (tm != null) managersObject = tm.gameObject;
        }

        status = "Auto-detect:\n" +
                 "  USB Creator Panel: " + Name(usbPanel) + "\n" +
                 "  Desktop Panel (USB station): " + Name(desktopPanel) + "\n" +
                 "  Bootable USB Creator: " + Name(creator) + "\n" +
                 "  Install Panel: " + Name(installPanel) + "\n" +
                 "  OS Install View: " + Name(osInstallView) + "\n" +
                 "  Post-Install Desktop: " + Name(desktopAppsPanel) + "\n" +
                 "  Managers Object: " + Name(managersObject);

        Repaint();
    }

    private static string Name(Object o) { return o != null ? o.name : "NOT FOUND"; }

    // ==================================================================
    // The rebuild
    // ==================================================================

    private void RebuildUsbPanel()
    {
        var panelRt = usbPanel.GetComponent<RectTransform>();
        if (panelRt == null)
        {
            status = "'" + usbPanel.name + "' has no RectTransform - that is not a UI panel.";
            return;
        }

        // ---- remember what is worth keeping off the old children ----
        var oldView = usbPanel.GetComponentInChildren<BootableUSBScreenshotView>(true);
        BootableUSBCreator keptCreator = creator;
        GameObject keptRing = null;
        string keptIsoPlaceholder = null;
        string keptDrivePlaceholder = null;
        bool viewWasOnChild = false;

        if (oldView != null)
        {
            var so = new SerializedObject(oldView);
            if (keptCreator == null) keptCreator = GetObject(so, "creator") as BootableUSBCreator;
            keptRing = GetObject(so, "highlightRingPrefab") as GameObject;
            keptIsoPlaceholder = GetString(so, "isoPlaceholderLabel");
            keptDrivePlaceholder = GetString(so, "drivePlaceholderLabel");
            viewWasOnChild = oldView.gameObject != usbPanel;
        }

        var oldClose = usbPanel.GetComponentInChildren<ClosePanelButton>(true);
        GameObject keptDesktop = desktopPanel;
        if (keptDesktop == null && oldClose != null)
            keptDesktop = GetObject(new SerializedObject(oldClose), "desktopPanel") as GameObject;

        if (keptCreator == null)
        {
            status = "No BootableUSBCreator found. Assign it above (or press Auto-Detect) and try again - " +
                     "the panel is useless without it and I would rather stop than build something dead.";
            return;
        }

        // ---- ask before destroying ----
        int childCount = panelRt.childCount;
        bool ok = EditorUtility.DisplayDialog(
            "Rebuild " + usbPanel.name + "?",
            "This deletes " + childCount + " child object(s) under '" + usbPanel.name +
            "' and rebuilds them as a drawn Rufus window.\n\n" +
            "KEPT: the panel object itself, your BootableUSBCreator, its ISO and drive lists, " +
            "and everything outside this panel.\n" +
            "LOST: the current children - any screenshot Image, hand-placed controls, and " +
            "anything you wired to them from elsewhere.\n\n" +
            "It is one undo step, so Ctrl+Z puts it all back.",
            "Rebuild", "Cancel");

        if (!ok)
        {
            status = "Cancelled. Nothing was changed.";
            return;
        }

        int undoGroup = Undo.GetCurrentGroup();

        try
        {
            Rebuild(panelRt, childCount, keptCreator, keptRing, keptDesktop,
                    keptIsoPlaceholder, keptDrivePlaceholder, viewWasOnChild);
        }
        catch (System.Exception e)
        {
            // The children are already gone by the time most failures happen, so the important
            // thing is that ONE Ctrl+Z puts the panel back rather than twenty.
            status = "Rebuild FAILED: " + e.Message +
                     "\n\nPress Ctrl+Z once to restore the panel, then send me this error.";
            Debug.LogError("[COC1PanelBuilder] " + e);
        }
        finally
        {
            Undo.CollapseUndoOperations(undoGroup);
            MarkSceneDirty();
            Repaint();
        }
    }

    private void Rebuild(RectTransform panelRt, int childCount,
                         BootableUSBCreator keptCreator, GameObject keptRing, GameObject keptDesktop,
                         string keptIsoPlaceholder, string keptDrivePlaceholder, bool viewWasOnChild)
    {
        // ---- clear ----
        var doomed = new List<GameObject>();
        for (int i = 0; i < panelRt.childCount; i++)
            doomed.Add(panelRt.GetChild(i).gameObject);
        foreach (var go in doomed)
            Undo.DestroyObjectImmediate(go);

        // ---- build ----
        COC1WinWidgets.Font = uiFont;
        string layoutOff = DisableAutoLayout(usbPanel);
        string layoutUp = AncestorLayoutNote(usbPanel);

        RectTransform windowRoot;
        var body = COC1WinWidgets.Window(panelRt, "RufusWindow", "Rufus 4.4.2103",
                                         WinW, WinH, out windowRoot, Pad);

        var built = BuildRufusBody(body);

        // ---- CLOSE button, rewired ----
        var closeBtn = COC1WinWidgets.Button(body, "CloseButton", BodyW - 92f, 530f, 92f, 28f, "CLOSE");
        var closeComp = Undo.AddComponent<ClosePanelButton>(closeBtn.gameObject);
        if (closeComp != null)
        {
            var cso = new SerializedObject(closeComp);
            SetObject(cso, "panelToClose", usbPanel);
            SetObject(cso, "desktopPanel", keptDesktop);
            cso.ApplyModifiedProperties();
        }

        COC1WinWidgets.FitInside(windowRoot, panelRt);

        // ---- the view ----
        var view = usbPanel.GetComponent<BootableUSBScreenshotView>();
        if (view == null) view = Undo.AddComponent<BootableUSBScreenshotView>(usbPanel);

        var vso = new SerializedObject(view);
        SetObject(vso, "creator", keptCreator);
        SetObject(vso, "isoDropdown", built.isoDropdown);
        SetObject(vso, "driveDropdown", built.driveDropdown);
        SetObject(vso, "createButton", built.startButton);
        SetObject(vso, "progressBar", built.progressBar);
        SetObject(vso, "statusText", built.statusText);
        SetObject(vso, "highlightRingPrefab", keptRing);

        // The screenshot fields are dead now. They are never read by any method on that class,
        // but leaving a stale reference in the Inspector invites someone to "fix" it later.
        SetObject(vso, "rufusScreenshot", null);
        SetObject(vso, "screenshotRoot", null);

        // MUST be off. With a real drawn window there is nothing underneath to cover, and
        // StyleControlsToCoverScreenshot would repaint the START button flat white.
        SetBool(vso, "coverScreenshotText", false);

        if (!string.IsNullOrEmpty(keptIsoPlaceholder)) SetString(vso, "isoPlaceholderLabel", keptIsoPlaceholder);
        if (!string.IsNullOrEmpty(keptDrivePlaceholder)) SetString(vso, "drivePlaceholderLabel", keptDrivePlaceholder);
        vso.ApplyModifiedProperties();

        status = "Rebuilt '" + usbPanel.name + "'.\n\n" +
                 (layoutOff != null
                     ? "  DISABLED auto-layout on the panel: " + layoutOff +
                       " - it was overriding every anchor\n"
                     : "") +
                 (layoutUp != null
                     ? "  NOTE: a LayoutGroup/ContentSizeFitter also sits on: " + layoutUp +
                       " - left alone, but it can still move this panel\n"
                     : "") +
                 "  Deleted " + childCount + " old child object(s)\n" +
                 "  Rufus window: " + WinW + " x " + WinH + ", scaled to fit\n" +
                 "  Wired: Device dropdown, Boot selection dropdown, START, progress bar, status text\n" +
                 "  CLOSE returns to: " + Name(keptDesktop) + "\n" +
                 (viewWasOnChild
                     ? "  NOTE: BootableUSBScreenshotView was on a child and has been moved to the panel root.\n"
                     : "") +
                 (keptRing == null
                     ? "  NOTE: no highlight ring prefab was set, so no ring will appear. That is fine.\n"
                     : "") +
                 "\nPress Play and open the station. If the arrow glyphs show as boxes, your font " +
                 "has no U+25BC - pick a font with fuller coverage, or clear the UI Font field.";

        Debug.Log("[COC1PanelBuilder] " + status, usbPanel);
    }

    // ==================================================================
    // The Rufus layout
    // ==================================================================

    private struct RufusParts
    {
        public TMP_Dropdown driveDropdown;
        public TMP_Dropdown isoDropdown;
        public Button startButton;
        public Slider progressBar;
        public TMP_Text statusText;
    }

    private RufusParts BuildRufusBody(RectTransform body)
    {
        var p = new RufusParts();

        // ---- Drive Properties ----
        COC1WinWidgets.SectionHeader(body, "Hdr_Drive", 0f, 0f, BodyW, "Drive Properties");

        COC1WinWidgets.Label(body, "Lbl_Device", 0f, 24f, 200f, 18f, "Device");
        p.driveDropdown = COC1WinWidgets.Dropdown(body, "DeviceDropdown", 0f, 42f, BodyW,
                                                  "SELECT A FLASHDRIVE");

        COC1WinWidgets.Label(body, "Lbl_Boot", 0f, 76f, 200f, 18f, "Boot selection");
        p.isoDropdown = COC1WinWidgets.Dropdown(body, "BootSelectionDropdown", 0f, 94f, 360f,
                                                "SELECT AN ISO IMAGE");
        COC1WinWidgets.Button(body, "SelectButton", 368f, 94f, 102f, COC1WinWidgets.RowH,
                              "SELECT  ▾", false, false);

        COC1WinWidgets.Label(body, "Lbl_ImageOption", 0f, 128f, 200f, 18f, "Image option");
        COC1WinWidgets.FakeCombo(body, "ImageOption", 0f, 146f, BodyW,
                                 "Standard Windows installation", true);

        COC1WinWidgets.Label(body, "Lbl_Scheme", 0f, 180f, 225f, 18f, "Partition scheme");
        COC1WinWidgets.Label(body, "Lbl_Target", 245f, 180f, 225f, 18f, "Target system");
        COC1WinWidgets.FakeCombo(body, "PartitionScheme", 0f, 198f, 225f, "GPT");
        COC1WinWidgets.FakeCombo(body, "TargetSystem", 245f, 198f, 225f, "UEFI (non CSM)");

        COC1WinWidgets.Label(body, "Link_AdvancedDrive", 0f, 232f, BodyW, 18f,
                             "▸ Show advanced drive properties", TextAlignmentOptions.Left,
                             false, COC1WinWidgets.LinkText, COC1WinWidgets.SmallFontSize);

        // ---- Format Options ----
        COC1WinWidgets.SectionHeader(body, "Hdr_Format", 0f, 262f, BodyW, "Format Options");

        COC1WinWidgets.Label(body, "Lbl_Volume", 0f, 286f, 200f, 18f, "Volume label");
        COC1WinWidgets.FakeField(body, "VolumeLabel", 0f, 304f, BodyW, "CCCOMA_X64FRE_EN-US_DV9");

        COC1WinWidgets.Label(body, "Lbl_Fs", 0f, 338f, 225f, 18f, "File system");
        COC1WinWidgets.Label(body, "Lbl_Cluster", 245f, 338f, 225f, 18f, "Cluster size");
        COC1WinWidgets.FakeCombo(body, "FileSystem", 0f, 356f, 225f, "NTFS");
        COC1WinWidgets.FakeCombo(body, "ClusterSize", 245f, 356f, 225f, "4096 bytes (default)");

        COC1WinWidgets.Label(body, "Link_AdvancedFormat", 0f, 390f, BodyW, 18f,
                             "▸ Show advanced format options", TextAlignmentOptions.Left,
                             false, COC1WinWidgets.LinkText, COC1WinWidgets.SmallFontSize);

        // ---- Status ----
        COC1WinWidgets.SectionHeader(body, "Hdr_Status", 0f, 420f, BodyW, "Status");

        p.progressBar = COC1WinWidgets.ProgressBar(body, "ProgressBar", 0f, 444f, BodyW, 28f);

        // Status text is a SIBLING of the bar, not a child. BootableUSBScreenshotView switches
        // the progress bar's GameObject off while idle, and a child caption would vanish with
        // it - taking "Select a device and boot image." off the screen exactly when the learner
        // needs to read it.
        p.statusText = COC1WinWidgets.Label(body, "StatusText", 0f, 444f, BodyW, 28f,
                                            "READY", TextAlignmentOptions.Center);

        // ---- bottom row ----
        for (int i = 0; i < 4; i++)
            COC1WinWidgets.BorderedBox(body, "ToolIcon_" + i, i * 26f, 530f, 20f, 20f,
                                       COC1WinWidgets.WindowBg, COC1WinWidgets.Separator);

        p.startButton = COC1WinWidgets.Button(body, "StartButton", BodyW - 92f - 100f, 530f,
                                              92f, 28f, "START", true);

        return p;
    }

    // ==================================================================
    // Phase 2 - the OS install panel
    // ==================================================================

    private void RebuildInstallPanel()
    {
        var panelRt = installPanel.GetComponent<RectTransform>();
        if (panelRt == null)
        {
            status = "'" + installPanel.name + "' has no RectTransform - that is not a UI panel.";
            Repaint();
            return;
        }

        // OSInstallView must survive the clear, and it must live somewhere that is ACTIVE:
        // its Start() is what switches the install panel off at scene load, and Start never
        // runs on an object that begins life inactive.
        if (osInstallView.transform.IsChildOf(panelRt) && osInstallView.gameObject != installPanel)
        {
            status = "OS Install View is on a child of the install panel, which this rebuild " +
                     "deletes. Move that component onto the panel's parent (or your Managers " +
                     "object) and press rebuild again.";
            Repaint();
            return;
        }

        var vso = new SerializedObject(osInstallView);
        var keptDesktop = GetObject(vso, "desktopPanel") as GameObject;

        int childCount = panelRt.childCount;
        bool ok = EditorUtility.DisplayDialog(
            "Rebuild " + installPanel.name + "?",
            "This deletes " + childCount + " child object(s) under '" + installPanel.name +
            "' and rebuilds them as six drawn Windows Setup looks.\n\n" +
            "KEPT: the panel itself, your 17 stage assets, OSInstallManager, and the desktop " +
            "panel this view switches on when the install finishes.\n\n" +
            "One undo step.",
            "Rebuild", "Cancel");

        if (!ok)
        {
            status = "Cancelled. Nothing was changed.";
            Repaint();
            return;
        }

        int undoGroup = Undo.GetCurrentGroup();

        try
        {
            var doomed = new List<GameObject>();
            for (int i = 0; i < panelRt.childCount; i++)
                doomed.Add(panelRt.GetChild(i).gameObject);
            foreach (var child in doomed)
                Undo.DestroyObjectImmediate(child);

            COC1WinWidgets.Font = uiFont;
            COC1SetupScreens.Font = uiFont;

            string layoutOff = DisableAutoLayout(installPanel);
            string layoutUp = AncestorLayoutNote(installPanel);

            var built = COC1SetupScreens.BuildAll(panelRt);

            WirePartitionView(built);
            WireInstallView(built, keptDesktop);
            var skinner = WireSkinner(built, panelRt);

            status = "Rebuilt '" + installPanel.name + "'.\n\n" +
                     (layoutOff != null
                         ? "  DISABLED auto-layout on the panel: " + layoutOff +
                           " - it was overriding every anchor\n"
                         : "") +
                     (layoutUp != null
                         ? "  NOTE: a LayoutGroup/ContentSizeFitter also sits on: " + layoutUp +
                           " - left alone, but it can still move this panel\n"
                         : "") +
                     "  Deleted " + childCount + " old child object(s)\n" +
                     "  6 looks covering all 17 stages\n" +
                     "  Partition table + row prefab saved to Assets/COC1/Content/Prefabs\n" +
                     "  OSInstallSkinner on '" + skinner.gameObject.name + "'\n" +
                     "  Desktop panel kept: " + Name(keptDesktop) + "\n\n" +
                     (keptDesktop == null
                         ? "NOTE: OSInstallView had no Desktop Panel set. The install will finish " +
                           "with nothing to show. Assign it before testing.\n"
                         : "") +
                     "Press Play, create the bootable USB, then press T at the target PC.";

            Debug.Log("[COC1PanelBuilder] " + status, installPanel);
        }
        catch (System.Exception e)
        {
            status = "Rebuild FAILED: " + e.Message +
                     "\n\nPress Ctrl+Z once to restore the panel, then send me this error.";
            Debug.LogError("[COC1PanelBuilder] " + e);
        }
        finally
        {
            Undo.CollapseUndoOperations(undoGroup);
            AssetDatabase.SaveAssets();
            MarkSceneDirty();
            Repaint();
        }
    }

    private static void WirePartitionView(COC1SetupScreens.Built b)
    {
        if (b.partitionView == null) return;

        var so = new SerializedObject(b.partitionView);
        SetObject(so, "rowsContent", b.rowsContent);
        SetObject(so, "rowPrefab", b.rowPrefab);
        SetObject(so, "toggleGroup", b.toggleGroup);
        SetObject(so, "newButton", b.newBtn);
        SetObject(so, "formatButton", b.formatBtn);
        SetObject(so, "deleteButton", b.deleteBtn);
        SetObject(so, "nextButton", b.nextBtn);
        SetObject(so, "errorText", b.errorText);
        so.ApplyModifiedProperties();
    }

    private void WireInstallView(COC1SetupScreens.Built b, GameObject keptDesktop)
    {
        var so = new SerializedObject(osInstallView);
        SetObject(so, "installPanel", installPanel);
        SetObject(so, "stageLabel", b.stageLabel);
        SetObject(so, "progressBar", b.progressBar);
        SetObject(so, "continueButton", b.continueButton);
        SetObject(so, "choiceDropdown", b.dropdown);
        SetObject(so, "rebootOverlay", b.rebootOverlay);
        SetObject(so, "partitionSelectPanel", b.partitionPanel);
        SetObject(so, "partitionSelectView", b.partitionView);
        SetObject(so, "desktopPanel", keptDesktop);
        so.ApplyModifiedProperties();
    }

    /// <summary>
    /// The skinner goes on the SAME object as OSInstallView, for the same reason that object
    /// has to be active: both subscribe in Start(), and Start does not run on an object that
    /// starts inactive - which the install panel does.
    /// </summary>
    private OSInstallSkinner WireSkinner(COC1SetupScreens.Built b, RectTransform panelRt)
    {
        var host = osInstallView.gameObject;

        var skinner = host.GetComponent<OSInstallSkinner>();
        if (skinner == null) skinner = Undo.AddComponent<OSInstallSkinner>(host);

        var so = new SerializedObject(skinner);

        SetObject(so, "stageLabel", b.stageLabel);
        SetObject(so, "choiceDropdown", b.dropdown);
        SetObject(so, "continueButton", b.continueButton);
        SetObject(so, "progressBar", b.progressBar);

        var skinsRoot = panelRt.Find("Skins");
        SetObject(so, "skinsRoot", skinsRoot);

        var list = so.FindProperty("skins");
        if (list != null)
        {
            list.arraySize = b.skins.Count;

            for (int i = 0; i < b.skins.Count; i++)
            {
                var spec = b.skins[i];
                var e = list.GetArrayElementAtIndex(i);

                e.FindPropertyRelative("name").stringValue = spec.name;
                e.FindPropertyRelative("root").objectReferenceValue = spec.root;
                e.FindPropertyRelative("labelRect").rectValue = spec.label;
                e.FindPropertyRelative("dropdownRect").rectValue = spec.dropdown;
                e.FindPropertyRelative("continueRect").rectValue = spec.cont;
                e.FindPropertyRelative("progressRect").rectValue = spec.progress;
                e.FindPropertyRelative("labelColor").colorValue = spec.labelColor;
                e.FindPropertyRelative("labelFontSize").floatValue = spec.labelSize;

                // intValue, NOT enumValueIndex.
                // enumValueIndex is the position in the enum's declaration list, so it only
                // matches the cast value when the enum runs 0, 1, 2... TMP's
                // TextAlignmentOptions does not: it is bit-packed (TopLeft is 257, Left is 513),
                // so casting it into enumValueIndex asks for list entry 257 of about thirty and
                // Unity rightly refuses. intValue writes the underlying number, which is what is
                // actually serialised, and works for any enum whatever its values.
                e.FindPropertyRelative("labelAlign").intValue = (int)spec.labelAlign;

                var stages = e.FindPropertyRelative("stages");
                stages.arraySize = spec.stages.Length;
                for (int j = 0; j < spec.stages.Length; j++)
                {
                    // intValue here too. InstallStage happens to be contiguous, so
                    // enumValueIndex would also work today - but relying on that is exactly the
                    // assumption that broke labelAlign above, and it would break again the day
                    // someone gave a stage an explicit value.
                    stages.GetArrayElementAtIndex(j).intValue = (int)spec.stages[j];
                }
            }
        }

        so.ApplyModifiedProperties();
        return skinner;
    }

    // ==================================================================
    // Phase 3 - the post-install desktop
    // ==================================================================

    private void RebuildDesktop()
    {
        var panelRt = desktopAppsPanel.GetComponent<RectTransform>();
        if (panelRt == null)
        {
            status = "'" + desktopAppsPanel.name + "' has no RectTransform - that is not a UI panel.";
            Repaint();
            return;
        }

        // Find the managers the Content Builder already wired. Creating new ones here would
        // leave the old ones stranded and still holding the item lists - the exact failure that
        // cost us an afternoon with the partition panel.
        var drivers = FindTool(SystemConfigTaskIds.InstallDrivers);
        var apps = FindTool(SystemConfigTaskIds.InstallAppSoftware);
        var updates = FindTool(SystemConfigTaskIds.InstallUpdates);
        var diagnostics = FindInScene<DiagnosticsTestManager>();
        var reportPanel = FindInScene<ServiceReportPanel>();

        var missing = new List<string>();
        if (drivers == null) missing.Add("ChecklistToolManager with groupTaskId 'install_drivers'");
        if (apps == null) missing.Add("ChecklistToolManager with groupTaskId 'install_app_software'");
        if (updates == null) missing.Add("ChecklistToolManager with groupTaskId 'install_updates'");
        if (diagnostics == null) missing.Add("DiagnosticsTestManager");

        if (missing.Count > 0)
        {
            status = "Missing manager(s):\n  " + string.Join("\n  ", missing.ToArray()) +
                     "\n\nRun Tools > COC 1 > Content Builder first - it creates the item and " +
                     "test assets and wires them onto these managers. This builder only draws " +
                     "the UI for them.";
            Repaint();
            return;
        }

        // A manager on an object that starts switched off never runs Start(), so its
        // TaskManager task is never registered and the checklist can never complete. Worth
        // refusing over, because the symptom appears much later and looks like content trouble.
        var asleep = new List<string>();
        sleepingManagers.Clear();
        CheckAwake(drivers, asleep);
        CheckAwake(apps, asleep);
        CheckAwake(updates, asleep);
        CheckAwake(diagnostics, asleep);
        if (reportPanel != null) CheckAwake(reportPanel, asleep);

        if (asleep.Count > 0)
        {
            status = "These managers start INACTIVE:\n  " + string.Join("\n  ", asleep.ToArray()) +
                     "\n\nThey register their TaskManager tasks in Start(), which never runs on " +
                     "an object that begins life switched off.\n\n" +
                     "This works TODAY only because each manager shares a panel with its view, so " +
                     "they wake up together when the learner opens that window. The moment the " +
                     "views move onto the new panels, these would never start at all.\n\n" +
                     "Use the Move button above - it copies each component with its lists intact.";
            Repaint();
            return;
        }

        int childCount = panelRt.childCount;
        bool ok = EditorUtility.DisplayDialog(
            "Rebuild " + desktopAppsPanel.name + "?",
            "This deletes " + childCount + " child object(s) under '" + desktopAppsPanel.name +
            "' and rebuilds the desktop, its five icons and five app windows.\n\n" +
            "KEPT: the panel itself, all five managers, and every item/test asset.\n\n" +
            "One undo step.",
            "Rebuild", "Cancel");

        if (!ok) { status = "Cancelled. Nothing was changed."; Repaint(); return; }

        int undoGroup = Undo.GetCurrentGroup();

        try
        {
            var doomed = new List<GameObject>();
            for (int i = 0; i < panelRt.childCount; i++)
                doomed.Add(panelRt.GetChild(i).gameObject);
            foreach (var child in doomed)
                Undo.DestroyObjectImmediate(child);

            COC1DesktopScreens.Font = uiFont;

            // A LayoutGroup here would stack Wallpaper, Icons, Apps and the taskbar pieces into
            // a column and ignore every anchor. See DisableAutoLayout.
            string layoutOff = DisableAutoLayout(desktopAppsPanel);
            string layoutUp = AncestorLayoutNote(desktopAppsPanel);

            var built = COC1DesktopScreens.BuildAll(panelRt);

            WireChecklistApp(built, COC1DesktopScreens.Drivers, drivers);
            WireChecklistApp(built, COC1DesktopScreens.Apps, apps);
            WireChecklistApp(built, COC1DesktopScreens.Updates, updates);
            WireDiagnosticsApp(built, diagnostics);
            reportPanel = WireReportApp(built, reportPanel, diagnostics);

            // Icons and CLOSE buttons. Note what is NOT set: Desktop Panel is left EMPTY on
            // both. These windows are CHILDREN of the desktop, and DesktopIconButton hides the
            // desktop when it opens its target - which would take the window down with it.
            // Each window draws its own dim instead.
            foreach (var app in built.apps)
                WireIconAndClose(app);

            var visibility = desktopAppsPanel.GetComponent<DesktopIconVisibility>();
            if (visibility == null) Undo.AddComponent<DesktopIconVisibility>(desktopAppsPanel);

            status = "Rebuilt '" + desktopAppsPanel.name + "'.\n\n" +
                     (layoutOff != null
                         ? "  DISABLED auto-layout on the panel: " + layoutOff +
                           " - it was overriding every anchor, which is what piled the icons " +
                           "and taskbar into the corner\n"
                         : "") +
                     (layoutUp != null
                         ? "  NOTE: a LayoutGroup/ContentSizeFitter also sits on: " + layoutUp +
                           " - left alone, but it can still move this panel\n"
                         : "") +
                     "  Deleted " + childCount + " old child object(s)\n" +
                     "  5 icons, 5 app windows, taskbar\n" +
                     "  3 row prefabs in Assets/COC1/Content/Prefabs\n" +
                     "  Views pointed at your existing managers\n" +
                     "  Service Report panel: " + Name(reportPanel) + "\n\n" +
                     "Icons do NOT hide the desktop - the windows are children of it, so hiding " +
                     "the desktop would hide them too. Each window dims the desktop instead.";

            Debug.Log("[COC1PanelBuilder] " + status, desktopAppsPanel);
        }
        catch (System.Exception e)
        {
            status = "Rebuild FAILED: " + e.Message + "\n\nPress Ctrl+Z once, then send me this error.";
            Debug.LogError("[COC1PanelBuilder] " + e);
        }
        finally
        {
            Undo.CollapseUndoOperations(undoGroup);
            AssetDatabase.SaveAssets();
            MarkSceneDirty();
            Repaint();
        }
    }

    private void CheckAwake(Component c, List<string> asleep)
    {
        if (c == null) return;
        if (c.gameObject.activeInHierarchy) return;

        asleep.Add(c.GetType().Name + " on '" + c.gameObject.name + "'");
        sleepingManagers.Add(c);
    }

    /// <summary>
    /// Moves each sleeping manager onto the Managers object, fields and all.
    ///
    /// ComponentUtility.CopyComponent / PasteComponentAsNew is the only way to relocate a
    /// component in Unity that carries its serialized values across - the item lists, the group
    /// task ids, the test lists. Re-adding by hand and retyping them is where the real risk is.
    ///
    /// Object references POINTING AT these managers break, because the instance is destroyed
    /// and a new one takes its place. That is fine here: the only such references are the ones
    /// the desktop rebuild re-establishes a moment later. Move first, then rebuild.
    /// </summary>
    private void MoveSleepingManagers()
    {
        if (managersObject == null) return;

        int group = Undo.GetCurrentGroup();
        var moved = new List<string>();
        var failed = new List<string>();

        foreach (var src in sleepingManagers)
        {
            if (src == null) continue;

            string label = src.GetType().Name + " from '" + src.gameObject.name + "'";

            var before = new HashSet<Component>(managersObject.GetComponents<Component>());

            if (!ComponentUtility.CopyComponent(src) ||
                !ComponentUtility.PasteComponentAsNew(managersObject))
            {
                failed.Add(label);
                continue;
            }

            // PasteComponentAsNew does not hand back what it made, so find it by difference and
            // register it, otherwise the paste would sit outside the undo step.
            foreach (var c in managersObject.GetComponents<Component>())
            {
                if (c == null || before.Contains(c)) continue;
                Undo.RegisterCreatedObjectUndo(c, "Move Manager");
                break;
            }

            Undo.DestroyObjectImmediate(src);
            moved.Add(label);
        }

        Undo.CollapseUndoOperations(group);
        MarkSceneDirty();

        sleepingManagers.Clear();

        status = "Moved " + moved.Count + " manager(s) to '" + managersObject.name + "':\n  " +
                 string.Join("\n  ", moved.ToArray()) +
                 (failed.Count > 0
                     ? "\n\nFAILED (move these by hand):\n  " + string.Join("\n  ", failed.ToArray())
                     : "") +
                 "\n\nCheck the lists came across - Device Manager should still show 4 items - " +
                 "then press Rebuild Post-Install Desktop.";

        Debug.Log("[COC1PanelBuilder] " + status, managersObject);
        Repaint();
    }

    /// <summary>The three checklist tools are told apart by their groupTaskId, the same way the
    /// Content Builder found them.</summary>
    private static ChecklistToolManager FindTool(string groupTaskId)
    {
        foreach (var tool in FindAllInScene<ChecklistToolManager>())
        {
            if (tool == null) continue;
            if (GetString(new SerializedObject(tool), "groupTaskId") == groupTaskId) return tool;
        }
        return null;
    }

    private static void WireChecklistApp(COC1DesktopScreens.Built built, string key,
                                         ChecklistToolManager manager)
    {
        var app = built.Find(key);
        if (app == null) return;

        var view = Undo.AddComponent<ChecklistToolView>(app.panel);
        var so = new SerializedObject(view);
        SetObject(so, "manager", manager);
        SetObject(so, "rowsContent", app.rowsContent);
        SetObject(so, "rowPrefab", built.checklistRowPrefab);
        SetObject(so, "allCompleteBanner", app.banner);
        so.ApplyModifiedProperties();
    }

    private static void WireDiagnosticsApp(COC1DesktopScreens.Built built, DiagnosticsTestManager mgr)
    {
        var app = built.Find(COC1DesktopScreens.Diagnostics);
        if (app == null) return;

        var view = Undo.AddComponent<DiagnosticsView>(app.panel);
        var so = new SerializedObject(view);
        SetObject(so, "rowsContent", app.rowsContent);
        SetObject(so, "rowPrefab", built.testRowPrefab);
        SetObject(so, "runButton", app.actionButton);
        SetObject(so, "overallResultText", app.statusText);
        so.ApplyModifiedProperties();
    }

    private ServiceReportPanel WireReportApp(COC1DesktopScreens.Built built,
                                             ServiceReportPanel reportPanel,
                                             DiagnosticsTestManager diagnostics)
    {
        var app = built.Find(COC1DesktopScreens.Report);
        if (app == null) return reportPanel;

        // ServiceReportPanel is a manager, not a view: it registers document_work in Start().
        // If it does not exist yet it goes on the Managers object, never on the window.
        if (reportPanel == null)
        {
            var host = managersObject != null ? managersObject : desktopAppsPanel;
            reportPanel = Undo.AddComponent<ServiceReportPanel>(host);
        }

        var pso = new SerializedObject(reportPanel);
        SetObject(pso, "diagnostics", diagnostics);
        pso.ApplyModifiedProperties();

        var view = Undo.AddComponent<ServiceReportView>(app.panel);
        var vso = new SerializedObject(view);
        SetObject(vso, "report", reportPanel);
        SetObject(vso, "rowsContent", app.rowsContent);
        SetObject(vso, "rowPrefab", built.statusRowPrefab);
        SetObject(vso, "submitButton", app.actionButton);
        SetObject(vso, "statusText", app.statusText);
        SetObject(vso, "submittedBanner", app.banner);
        vso.ApplyModifiedProperties();

        return reportPanel;
    }

    private static void WireIconAndClose(COC1DesktopScreens.AppWindow app)
    {
        if (app.icon != null)
        {
            var iconComp = Undo.AddComponent<DesktopIconButton>(app.icon.gameObject);
            var so = new SerializedObject(iconComp);
            SetObject(so, "targetPanel", app.panel);
            SetObject(so, "desktopPanel", null);   // deliberately empty - see RebuildDesktop
            so.ApplyModifiedProperties();
        }

        if (app.closeButton != null)
        {
            var closeComp = Undo.AddComponent<ClosePanelButton>(app.closeButton.gameObject);
            var so = new SerializedObject(closeComp);
            SetObject(so, "panelToClose", app.panel);
            SetObject(so, "desktopPanel", null);
            so.ApplyModifiedProperties();
        }
    }

    private static List<T> FindAllInScene<T>() where T : Component
    {
        var result = new List<T>();
        foreach (var c in Resources.FindObjectsOfTypeAll<T>())
        {
            if (c == null) continue;
            if (EditorUtility.IsPersistent(c)) continue;
            if (c.hideFlags != HideFlags.None) continue;
            result.Add(c);
        }
        return result;
    }

    /// <summary>
    /// Switches off any auto-layout component on a panel this builder is about to draw into,
    /// and reports what it found.
    ///
    /// WHY THIS IS NOT OPTIONAL
    /// Every widget here is placed by explicit anchors. A LayoutGroup on the panel overrides
    /// that completely: it repositions and resizes its direct children itself, in creation
    /// order, ignoring anchors entirely. The result is a column of controls piled into a
    /// corner - the taskbar clock halfway up the left edge, five icon labels stacked on top of
    /// each other - which reads as "the builder produced garbage" rather than "something else
    /// is moving these".
    ///
    /// A ContentSizeFitter is the same problem one level up: it resizes the panel itself, so
    /// the fractions every anchor is computed against stop meaning what they meant.
    ///
    /// Disabled, not deleted - reversible, and visible in the Inspector so you can see what
    /// changed.
    /// </summary>
    private static string DisableAutoLayout(GameObject panel)
    {
        var off = new List<string>();

        foreach (var g in panel.GetComponents<LayoutGroup>())
        {
            if (g == null || !g.enabled) continue;
            Undo.RecordObject(g, "Disable Auto Layout");
            g.enabled = false;
            off.Add(g.GetType().Name);
        }

        foreach (var f in panel.GetComponents<ContentSizeFitter>())
        {
            if (f == null || !f.enabled) continue;
            Undo.RecordObject(f, "Disable Auto Layout");
            f.enabled = false;
            off.Add("ContentSizeFitter");
        }

        foreach (var a in panel.GetComponents<AspectRatioFitter>())
        {
            if (a == null || !a.enabled) continue;
            Undo.RecordObject(a, "Disable Auto Layout");
            a.enabled = false;
            off.Add("AspectRatioFitter");
        }

        return off.Count == 0 ? null : string.Join(", ", off.ToArray());
    }

    /// <summary>
    /// A LayoutGroup on an ANCESTOR of the panel is a milder version of the same problem: it
    /// moves and resizes the panel itself rather than scrambling its contents. Reported, never
    /// switched off - that group is almost certainly doing a job somewhere else in your UI, and
    /// a builder that quietly disables things it does not own is worse than one that tells you.
    /// </summary>
    private static string AncestorLayoutNote(GameObject panel)
    {
        var names = new List<string>();
        var t = panel.transform.parent;

        while (t != null)
        {
            if (t.GetComponent<LayoutGroup>() != null || t.GetComponent<ContentSizeFitter>() != null)
                names.Add(t.name);
            t = t.parent;
        }

        return names.Count == 0 ? null : string.Join(", ", names.ToArray());
    }

    // ==================================================================
    // Serialized-field helpers
    // ==================================================================

    private static Object GetObject(SerializedObject so, string field)
    {
        var p = so.FindProperty(field);
        return p != null ? p.objectReferenceValue : null;
    }

    private static string GetString(SerializedObject so, string field)
    {
        var p = so.FindProperty(field);
        return p != null ? p.stringValue : null;
    }

    private static void SetObject(SerializedObject so, string field, Object value)
    {
        var p = so.FindProperty(field);
        if (p == null)
        {
            Debug.LogWarning("[COC1PanelBuilder] No serialized field '" + field + "' - did the script change?");
            return;
        }
        p.objectReferenceValue = value;
    }

    private static void SetBool(SerializedObject so, string field, bool value)
    {
        var p = so.FindProperty(field);
        if (p != null) p.boolValue = value;
    }

    private static void SetString(SerializedObject so, string field, string value)
    {
        var p = so.FindProperty(field);
        if (p != null) p.stringValue = value;
    }

    // ==================================================================

    private static T FindInScene<T>() where T : Component
    {
        foreach (var c in Resources.FindObjectsOfTypeAll<T>())
        {
            if (c == null) continue;
            if (EditorUtility.IsPersistent(c)) continue;
            if (c.hideFlags != HideFlags.None) continue;
            return c;
        }
        return null;
    }

    private static void MarkSceneDirty()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.IsValid())
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    }
}
#endif