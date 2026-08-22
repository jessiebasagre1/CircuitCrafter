#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the "download and install Rufus" sequence: the seven step assets, their dialogs,
/// and the browser panel that plays them.
///
/// Tools > COC 1 > Rufus Setup Builder
///
/// WHAT YOU SUPPLY: seven background sprites (browser, the Rufus site, the download bar, the
/// downloaded file, the UAC prompt, the update prompt, the Rufus splash). Drag each one onto
/// its step asset after building.
///
/// WHAT THIS SUPPLIES: the interaction on top of them - the address bar that marks the domain,
/// the click hotspots, the UAC decoy, the progress bars, and the completion that unlocks the
/// Rufus desktop icon.
///
/// THE RECTS ARE GUESSES. They were written without seeing your screenshots and cannot be
/// right. Nudging them in the Inspector while watching the Scene view is the intended
/// workflow, not a defect.
/// </summary>
public class COC1RufusSetupBuilder : EditorWindow
{
    // ==================================================================
    // Content - edit this table, press Build, done.
    // ==================================================================

    /// <summary>The OFFICIAL Rufus site. Not rufus.com, which is somebody else's domain - and
    /// that distinction is the single most useful thing this whole sequence teaches.</summary>
    public const string OfficialUrl = "rufus.ie";

    public const string InstallerName = "rufus-4.4.exe";
    public const string RootFolder = "Assets/COC1/Content/RufusSetup";

    private class StepDef
    {
        public string assetName;
        public string stepId;
        public string instruction;
        public RufusSetupStepData.StepKind kind;
        public Rect hotspot;
        public string hotspotLabel = "";
        public Rect decoy = new Rect(0f, 0f, 0f, 0f);
        public string decoyLabel = "";
        public float duration = 3f;
        public string progressLabel = "";
        public string hint;
        public string teach;
        public string wrong;

        /// <summary>Which drawn screen this step shows. The rects come from the same layout,
        /// so nothing has to be lined up by hand.</summary>
        public string screen;
    }

    private static readonly List<StepDef> Steps = new List<StepDef>
    {
        new StepDef
        {
            assetName = "RS_01_OpenSite",
            screen = COC1BrowserScreens.NewTab,
            stepId = "open_site",
            kind = RufusSetupStepData.StepKind.TypeUrl,
            instruction = "Open the official Rufus website. Type the address in the address bar and press Enter.",
            hint = "Rufus is an Irish project. Its official site ends in .ie, not .com.",
            teach = "Always download a tool from the vendor's own site. Download portals and " +
                    "look-alike domains wrap the same installer in adware, and a bench PC that " +
                    "picks that up has been made worse by the person servicing it.",
            wrong = "That is not the official Rufus site. Downloading a system tool from the wrong " +
                    "domain is how a workshop PC picks up bundled adware. The official address is " +
                    OfficialUrl + "."
        },

        new StepDef
        {
            assetName = "RS_02_DownloadLink",
            screen = COC1BrowserScreens.Site,
            stepId = "download_link",
            kind = RufusSetupStepData.StepKind.ClickHotspot,
            instruction = "Scroll to the Download section and click the latest release, " + InstallerName + ".",
            hotspot = new Rect(0.33f, 0.52f, 0.24f, 0.05f),
            decoyLabel = "advert download button",
            hint = "The real download is the file name in the version table. The big green button " +
                   "is inside a block labelled Advertisement.",
            wrong = "That button is an advertisement, not the download. On a real download page it " +
                    "is usually the biggest, greenest thing on the screen, and it installs " +
                    "something nobody asked for. The real download is the file name in the " +
                    "version table.",
            teach = "Note the version number before you click. Recording which build you installed " +
                    "is part of documenting the job - \"I installed Rufus\" is not a service record, " +
                    "\"Rufus " + InstallerName + "\" is."
        },

        new StepDef
        {
            assetName = "RS_03_Downloading",
            screen = COC1BrowserScreens.Downloading,
            stepId = "downloading",
            kind = RufusSetupStepData.StepKind.Timed,
            instruction = "Downloading " + InstallerName + "...",
            progressLabel = "Downloading " + InstallerName + "...",
            duration = 3f
        },

        new StepDef
        {
            assetName = "RS_04_RunInstaller",
            screen = COC1BrowserScreens.DownloadsBar,
            stepId = "run_installer",
            kind = RufusSetupStepData.StepKind.ClickHotspot,
            instruction = "The download is finished. Click the downloaded file to run it.",
            hotspot = new Rect(0.02f, 0.88f, 0.18f, 0.06f),
            hint = "It appears in the downloads bar along the bottom of the browser.",
            teach = "Rufus is a portable executable - there is no install wizard, no Next-Next-" +
                    "Finish, and nothing written to Program Files. Running the file IS the install."
        },

        new StepDef
        {
            assetName = "RS_05_UAC",
            screen = COC1BrowserScreens.Uac,
            stepId = "uac_prompt",
            kind = RufusSetupStepData.StepKind.ClickHotspot,
            instruction = "Windows is asking whether to let this app make changes to the device. " +
                          "Read who the publisher is, then allow it.",
            hotspot = new Rect(0.42f, 0.62f, 0.13f, 0.06f),
            hotspotLabel = "",
            decoy = new Rect(0.57f, 0.62f, 0.13f, 0.06f),
            decoyLabel = "",
            hint = "Yes is the left button. Check the publisher line says Akeo Consulting before " +
                   "you click it.",
            teach = "User Account Control is Windows asking permission before a program is allowed " +
                    "to change the system. Rufus needs it because writing to a raw disk is exactly " +
                    "the kind of thing UAC exists to gate. The line that matters is the publisher - " +
                    "an unsigned or unexpected publisher on a UAC prompt is a reason to stop.",
            wrong = "Choosing No cancels Rufus - it cannot write to a USB device without permission " +
                    "to change the system. This is the prompt you should read carefully and then " +
                    "accept, having checked the publisher."
        },

        new StepDef
        {
            assetName = "RS_06_UpdatePolicy",
            screen = COC1BrowserScreens.UpdatePolicy,
            stepId = "update_policy",
            kind = RufusSetupStepData.StepKind.ClickHotspot,
            instruction = "Rufus asks whether to check for application updates. Allow it.",
            hotspot = new Rect(0.42f, 0.60f, 0.13f, 0.06f),
            hint = "Yes keeps the tool patched.",
            teach = "A tool that writes bootable media is worth keeping current: newer builds fix " +
                    "compatibility with newer Windows images. Letting it check for updates is the " +
                    "right default on a workshop machine that has internet."
        },

        new StepDef
        {
            assetName = "RS_07_Launching",
            screen = COC1BrowserScreens.Splash,
            stepId = "launching",
            kind = RufusSetupStepData.StepKind.Timed,
            instruction = "Starting Rufus...",
            progressLabel = "Starting Rufus...",
            duration = 1.5f
        }
    };

    // ==================================================================
    // Window
    // ==================================================================

    private TMP_FontAsset uiFont;
    private GameObject browserPanel;
    private Transform newPanelParent;
    private GameObject desktopPanel;
    private RufusSetupManager manager;
    private DesktopIconButton rufusIcon;
    private bool useDrawnScreens = true;

    private Vector2 scroll;
    private string status = "";

    [MenuItem("Tools/COC 1/Rufus Setup Builder")]
    public static void Open()
    {
        var w = GetWindow<COC1RufusSetupBuilder>("Rufus Setup");
        if (w != null) w.minSize = new Vector2(430, 600);
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("Rufus - download and install", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Seven screens before the Rufus window: find the official site, download the " +
            "installer, run it, get past UAC, allow update checks, launch.\n\n" +
            "Two of them are graded - typing the wrong domain, and refusing the UAC prompt.\n\n" +
            "You supply seven background sprites and nudge the hotspot rects to match them.",
            MessageType.Info);

        EditorGUILayout.Space();
        uiFont = (TMP_FontAsset)EditorGUILayout.ObjectField(
            new GUIContent("UI Font (optional)"), uiFont, typeof(TMP_FontAsset), false);

        EditorGUILayout.Space();
        useDrawnScreens = EditorGUILayout.Toggle(
            new GUIContent("Draw the 7 screens",
                           "On: the browser, the download page, the UAC prompt and the rest are " +
                           "drawn in Unity UI and every hotspot rect is set exactly. Off: each " +
                           "step falls back to a Background sprite you supply and position."),
            useDrawnScreens);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Step 1 - content", EditorStyles.boldLabel);
        if (GUILayout.Button("Build / Rebuild Step Assets", GUILayout.Height(28)))
            EditorApplication.delayCall += BuildSteps;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Step 2 - the browser panel", EditorStyles.boldLabel);

        browserPanel = (GameObject)EditorGUILayout.ObjectField(
            new GUIContent("Browser Panel"), browserPanel, typeof(GameObject), true);

        if (browserPanel == null)
        {
            EditorGUILayout.HelpBox(
                "No browser panel yet? Drop the Canvas (or the object the other station panels " +
                "live under) below and press Create, and one will be made for you.",
                MessageType.None);

            newPanelParent = (Transform)EditorGUILayout.ObjectField(
                new GUIContent("Create under"), newPanelParent, typeof(Transform), true);

            using (new EditorGUI.DisabledScope(newPanelParent == null))
            {
                if (GUILayout.Button("Create Browser Panel"))
                    EditorApplication.delayCall += CreatePanel;
            }
        }

        desktopPanel = (GameObject)EditorGUILayout.ObjectField(
            new GUIContent("Desktop Panel"), desktopPanel, typeof(GameObject), true);
        manager = (RufusSetupManager)EditorGUILayout.ObjectField(
            new GUIContent("Rufus Setup Manager"), manager, typeof(RufusSetupManager), true);

        if (GUILayout.Button("Auto-Detect"))
            EditorApplication.delayCall += AutoDetect;

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(browserPanel == null))
        {
            if (GUILayout.Button("Rebuild Browser Panel", GUILayout.Height(30)))
                EditorApplication.delayCall += RebuildPanel;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Step 3 - hide the Rufus icon until it is installed",
                                   EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Gates the Rufus desktop shortcut behind \"" + RufusSetupManager.TaskId + "\" and hides " +
            "it completely until then - an uninstalled program has no desktop icon at all.\n\n" +
            "Also adds DesktopIconVisibility to the desktop panel, which is what brings the icon " +
            "back. The icon cannot do that itself: an object that switches itself off never runs " +
            "again, so it could hide once and never reappear.",
            MessageType.None);

        rufusIcon = (DesktopIconButton)EditorGUILayout.ObjectField(
            new GUIContent("Rufus Desktop Icon"), rufusIcon, typeof(DesktopIconButton), true);

        using (new EditorGUI.DisabledScope(rufusIcon == null || desktopPanel == null))
        {
            if (GUILayout.Button("Gate + Hide Rufus Icon", GUILayout.Height(28)))
                EditorApplication.delayCall += GateRufusIcon;
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
    // Step 1 - content assets
    // ==================================================================

    private int created, updated;

    private void BuildSteps()
    {
        created = 0;
        updated = 0;

        EnsureFolder(RootFolder);
        EnsureFolder(RootFolder + "/Dialogs");

        var built = new List<RufusSetupStepData>();

        foreach (var def in Steps)
        {
            var asset = GetOrCreate<RufusSetupStepData>(RootFolder, def.assetName);
            if (asset == null) continue;

            asset.stepId = def.stepId;
            asset.drawnScreen = useDrawnScreens ? (def.screen ?? "") : "";
            asset.instruction = def.instruction;
            asset.kind = def.kind;
            asset.hint = def.hint ?? "";

            if (def.kind == RufusSetupStepData.StepKind.ClickHotspot)
            {
                asset.hotspot = def.hotspot;
                asset.hotspotLabel = def.hotspotLabel ?? "";
                asset.decoyHotspot = def.decoy;
                asset.decoyLabel = def.decoyLabel ?? "";
            }

            if (def.kind == RufusSetupStepData.StepKind.TypeUrl)
            {
                asset.correctUrl = OfficialUrl;
                asset.alsoAccept = new[] { OfficialUrl + "/downloads" };
            }

            if (def.kind == RufusSetupStepData.StepKind.Timed)
            {
                asset.duration = def.duration;
                asset.progressLabel = def.progressLabel ?? "";
            }

            // Only fill a dialog slot that is still empty. Rebuilding must not throw away a
            // dialog you wrote yourself and dragged in.
            if (asset.educationalDialog == null)
                asset.educationalDialog = MakeDialog("Dlg_Teach_" + def.assetName, "Guide", def.teach);

            if (asset.wrongDialog == null)
                asset.wrongDialog = MakeDialog("Dlg_Wrong_" + def.assetName, "Guide", def.wrong);

            EditorUtility.SetDirty(asset);
            built.Add(asset);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Push them straight onto the manager if we can find one - saves a drag per step.
        var m = manager != null ? manager : FindInScene<RufusSetupManager>();
        if (m != null)
        {
            var list = new List<Object>();
            foreach (var a in built) list.Add(a);
            WriteList(m, "steps", list);
            MarkSceneDirty();
        }

        status = "Built " + Steps.Count + " step assets (" + created + " new, " + updated + " updated).\n" +
                 "Folder: " + RootFolder + "\n\n" +
                 (m != null ? "Assigned them to '" + m.name + "'.\n\n" : "No RufusSetupManager in the scene yet.\n\n") +
                 "NOW: click each RS_* asset and drag your screenshot into its Background field.";

        Debug.Log("[COC1RufusSetupBuilder] " + status);
        Repaint();
    }

    // ==================================================================
    // Step 2 - the panel
    // ==================================================================

    private void CreatePanel()
    {
        var go = new GameObject("BrowserPanel", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create Browser Panel");

        var rt = (RectTransform)go.transform;
        rt.SetParent(newPanelParent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;

        // Switched OFF. A station panel is opened by its desktop icon; one left active at
        // scene load runs RufusSetupView.OnEnable immediately, which starts the sequence and
        // fires step one's teaching popup over the top of the intro dialog.
        go.SetActive(false);

        browserPanel = go;
        Selection.activeGameObject = go;

        status = "Created 'BrowserPanel' under '" + newPanelParent.name + "', stretched to fill, " +
                 "and switched off - the desktop icon opens it.\n\nNow press Rebuild Browser Panel.";
        MarkSceneDirty();
        Repaint();
    }

    private void AutoDetect()
    {
        if (manager == null) manager = FindInScene<RufusSetupManager>();

        if (browserPanel == null)
        {
            var v = FindInScene<RufusSetupView>();
            if (v != null) browserPanel = v.gameObject;
        }

        if (desktopPanel == null)
        {
            var creatorView = FindInScene<BootableUSBScreenshotView>();
            if (creatorView != null)
            {
                var close = creatorView.GetComponentInChildren<ClosePanelButton>(true);
                if (close != null)
                    desktopPanel = GetObject(new SerializedObject(close), "desktopPanel") as GameObject;
            }
        }

        if (rufusIcon == null)
        {
            // The Rufus icon is the one whose Target Panel is the panel holding the Rufus window.
            // Matching on the panel rather than on the object's name means a renamed icon is
            // still found, and a differently-named panel does not fool it.
            var creatorView = FindInScene<BootableUSBScreenshotView>();
            var usbPanel = creatorView != null ? creatorView.gameObject : null;

            if (usbPanel != null)
            {
                foreach (var icon in FindAllInScene<DesktopIconButton>())
                {
                    var target = GetObject(new SerializedObject(icon), "targetPanel") as GameObject;
                    if (target == usbPanel) { rufusIcon = icon; break; }
                }
            }
        }

        status = "Auto-detect:\n" +
                 "  Browser Panel: " + Name(browserPanel) + "\n" +
                 "  Desktop Panel: " + Name(desktopPanel) + "\n" +
                 "  Rufus Setup Manager: " + Name(manager) + "\n" +
                 "  Rufus Desktop Icon: " + Name(rufusIcon);
        Repaint();
    }

    private void RebuildPanel()
    {
        var panelRt = browserPanel.GetComponent<RectTransform>();
        if (panelRt == null)
        {
            status = "'" + browserPanel.name + "' has no RectTransform - that is not a UI panel.";
            Repaint();
            return;
        }

        int childCount = panelRt.childCount;
        if (childCount > 0)
        {
            bool ok = EditorUtility.DisplayDialog(
                "Rebuild " + browserPanel.name + "?",
                "This deletes " + childCount + " child object(s) under '" + browserPanel.name +
                "' and rebuilds them.\n\nThe panel object itself, your step assets and your " +
                "sprites are not touched. One undo step.",
                "Rebuild", "Cancel");

            if (!ok)
            {
                status = "Cancelled. Nothing was changed.";
                Repaint();
                return;
            }
        }

        int undoGroup = Undo.GetCurrentGroup();

        try
        {
            Build(panelRt);
            status = "Rebuilt '" + browserPanel.name + "'.\n\n" +
                     (useDrawnScreens
                         ? "  7 screens drawn in Unity UI, every hotspot rect written exactly\n"
                         : "  Using Background sprites - position the rects yourself\n") +
                     "  Background, instruction strip, hint line\n" +
                     "  Address bar + Go, click hotspot, UAC decoy, progress bar\n" +
                     "  CLOSE returns to: " + Name(desktopPanel) + "\n" +
                     "  Panel switched OFF - the desktop icon opens it\n\n" +
                     (useDrawnScreens
                         ? "Nothing left to position. Press Play and walk through it."
                         : "The controls sit at default positions until Play - the view moves each " +
                           "one to the current step's rect. Select an RS_* asset to drag the rects " +
                           "onto your screenshot.");
            Debug.Log("[COC1RufusSetupBuilder] " + status, browserPanel);
        }
        catch (System.Exception e)
        {
            status = "Rebuild FAILED: " + e.Message + "\n\nPress Ctrl+Z once, then send me this error.";
            Debug.LogError("[COC1RufusSetupBuilder] " + e);
        }
        finally
        {
            Undo.CollapseUndoOperations(undoGroup);
            MarkSceneDirty();
            Repaint();
        }
    }

    private void GateRufusIcon()
    {
        int undoGroup = Undo.GetCurrentGroup();

        var iso = new SerializedObject(rufusIcon);

        // TaskGate is a plain [System.Serializable] class embedded in the icon, so its fields
        // are reached by dotted path rather than by looking for a separate component.
        var ids = iso.FindProperty("gate.requiredTaskIds");
        if (ids == null)
        {
            status = "'" + rufusIcon.name + "' has no gate.requiredTaskIds - is that really a " +
                     "DesktopIconButton with a TaskGate on it?";
            Repaint();
            return;
        }

        ids.arraySize = 1;
        ids.GetArrayElementAtIndex(0).stringValue = RufusSetupManager.TaskId;

        var hide = iso.FindProperty("hideWhenLocked");
        if (hide == null)
        {
            status = "'" + rufusIcon.name + "' has no Hide When Locked field - replace " +
                     "DesktopIconButton.cs with the updated version first.";
            Repaint();
            return;
        }
        hide.boolValue = true;
        iso.ApplyModifiedProperties();

        var vis = desktopPanel.GetComponent<DesktopIconVisibility>();
        bool addedVis = false;
        if (vis == null)
        {
            vis = Undo.AddComponent<DesktopIconVisibility>(desktopPanel);
            addedVis = true;
        }

        // Match the Editor to what the learner will see on a fresh save, so the Scene view is
        // not quietly lying about the starting state.
        bool unlocked = rufusIcon.IsUnlocked;
        if (rufusIcon.gameObject.activeSelf != unlocked)
        {
            Undo.RecordObject(rufusIcon.gameObject, "Hide Rufus Icon");
            rufusIcon.gameObject.SetActive(unlocked);
        }

        Undo.CollapseUndoOperations(undoGroup);
        MarkSceneDirty();

        status = "Gated '" + rufusIcon.name + "'.\n\n" +
                 "  Required Task Ids: " + RufusSetupManager.TaskId + "\n" +
                 "  Hide When Locked: on\n" +
                 "  DesktopIconVisibility on '" + desktopPanel.name + "': " +
                 (addedVis ? "added" : "already there") + "\n" +
                 "  Icon set " + (unlocked ? "ACTIVE (task already complete in this scene)" : "INACTIVE") +
                 " to match its starting state\n\n" +
                 "Optional: give the gate a Locked Message dialog, so pressing the spot where the " +
                 "icon will be says something useful rather than nothing.";

        Debug.Log("[COC1RufusSetupBuilder] " + status, rufusIcon);
        Repaint();
    }

    private void Build(RectTransform panelRt)
    {
        var doomed = new List<GameObject>();
        for (int i = 0; i < panelRt.childCount; i++)
            doomed.Add(panelRt.GetChild(i).gameObject);
        foreach (var child in doomed)
            Undo.DestroyObjectImmediate(child);

        COC1WinWidgets.Font = uiFont;

        // ---- background, filling the panel ----
        var bg = COC1WinWidgets.Stretch(panelRt, "Background");
        var bgImage = bg.gameObject.AddComponent<Image>();
        bgImage.color = Color.white;
        bgImage.preserveAspect = false;      // normalised hotspots assume it stretches edge to edge
        bgImage.raycastTarget = false;       // never eat a click meant for a hotspot

        // ---- drawn screens, between the sprite fallback and the hotspots ----
        // Order matters: Screens must be created BEFORE Content, because uGUI draws in
        // Hierarchy order and a screen built afterwards would paint over every hotspot.
        Transform screensRoot = null;
        if (useDrawnScreens)
        {
            screensRoot = COC1WinWidgets.Stretch(panelRt, "Screens");
            COC1BrowserScreens.Font = uiFont;
            var rects = COC1BrowserScreens.BuildAll(screensRoot);
            ApplyDrawnScreenRects(rects);
        }

        // ---- content root: hotspots are placed inside this, over the background ----
        var content = COC1WinWidgets.Stretch(panelRt, "Content");

        // ---- controls ----
        // ORDER MATTERS. uGUI draws and raycasts in hierarchy order, so the LAST child wins a
        // click. The visible controls are built first and the two invisible hotspots last, so
        // a hotspot is never sitting behind a sibling.
        //
        // A transparent Image still takes raycasts - that is the whole point of a hotspot - so
        // when one loses to a sibling there is nothing on screen to show you why. The view also
        // re-asserts this with SetAsLastSibling every time it shows a hotspot, so a hand
        // reorder cannot reintroduce it either.
        TextMeshProUGUI hotLabel, decoyText, goLabel;
        Image hotOutline;

        var url = COC1WinWidgets.InputField(content, "AddressBar", 0f, 0f, 400f, 30f,
                                            "Search or type a web address");
        var goBtn = COC1WinWidgets.FlatButton(content, "GoButton", 0f, 0f, 60f, 30f, "Go", out goLabel,
                                              COC1WinWidgets.Accent, COC1WinWidgets.Accent);
        goLabel.color = Color.white;

        var progress = COC1WinWidgets.FlatProgressBar(content, "ProgressBar", 0f, 0f, 300f, 22f);
        var progressLabel = COC1WinWidgets.Label(content, "ProgressLabel", 0f, 0f, 300f, 22f, "",
                                                 TextAlignmentOptions.Center);

        var decoy = COC1WinWidgets.HotspotButton(content, "DecoyHotspot", 0f, 0f, 200f, 40f,
                                                 out decoyText, out Image decoyOutline);
        var hotspot = COC1WinWidgets.HotspotButton(content, "Hotspot", 0f, 0f, 200f, 40f,
                                                   out hotLabel, out hotOutline);

        // ---- instruction strip along the bottom ----
        var strip = new GameObject("InstructionStrip", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(strip, "COC1 Build UI");
        var srt = (RectTransform)strip.transform;
        srt.SetParent(panelRt, false);
        srt.anchorMin = new Vector2(0f, 0f);
        srt.anchorMax = new Vector2(1f, 0f);
        srt.pivot = new Vector2(0.5f, 0f);
        srt.offsetMin = new Vector2(0f, 0f);
        srt.offsetMax = new Vector2(0f, 96f);
        srt.localScale = Vector3.one;

        var stripImg = strip.AddComponent<Image>();
        stripImg.color = new Color(0.05f, 0.06f, 0.08f, 0.88f);
        stripImg.raycastTarget = false;

        var instruction = COC1WinWidgets.StretchLabel(srt, "InstructionText", "", 24f, 14f, 130f, 48f,
                                                      TextAlignmentOptions.TopLeft, Color.white);
        instruction.fontSize = 17f;

        var hint = COC1WinWidgets.StretchLabel(srt, "HintText", "", 24f, 52f, 130f, 12f,
                                               TextAlignmentOptions.TopLeft,
                                               new Color(1f, 0.82f, 0.35f, 1f));
        hint.fontSize = 14f;
        hint.gameObject.SetActive(false);

        var closeBtn = COC1WinWidgets.FlatButton(srt, "CloseButton", 0f, 0f, 96f, 30f, "CLOSE",
                                                 out TextMeshProUGUI closeLabel);
        var crt = (RectTransform)closeBtn.transform;
        crt.anchorMin = new Vector2(1f, 0.5f);
        crt.anchorMax = new Vector2(1f, 0.5f);
        crt.pivot = new Vector2(1f, 0.5f);
        crt.anchoredPosition = new Vector2(-20f, 0f);
        crt.sizeDelta = new Vector2(96f, 30f);

        var closeComp = Undo.AddComponent<ClosePanelButton>(closeBtn.gameObject);
        if (closeComp != null)
        {
            var cso = new SerializedObject(closeComp);
            SetObject(cso, "panelToClose", browserPanel);
            SetObject(cso, "desktopPanel", desktopPanel);
            cso.ApplyModifiedProperties();
        }

        var banner = COC1WinWidgets.Label(panelRt, "AlreadyInstalledBanner", 0f, 0f, 10f, 10f, "");
        var brt = (RectTransform)banner.transform;
        brt.anchorMin = new Vector2(0.5f, 0.5f);
        brt.anchorMax = new Vector2(0.5f, 0.5f);
        brt.pivot = new Vector2(0.5f, 0.5f);
        brt.anchoredPosition = Vector2.zero;
        brt.sizeDelta = new Vector2(600f, 120f);
        banner.text = "Rufus is already installed on this PC.";
        banner.alignment = TextAlignmentOptions.Center;
        banner.fontSize = 22f;
        banner.gameObject.SetActive(false);

        // ---- the view ----
        var view = browserPanel.GetComponent<RufusSetupView>();
        if (view == null) view = Undo.AddComponent<RufusSetupView>(browserPanel);

        var vso = new SerializedObject(view);
        SetObject(vso, "manager", manager != null ? manager : FindInScene<RufusSetupManager>());
        SetObject(vso, "background", bgImage);
        SetObject(vso, "contentRoot", content);
        SetObject(vso, "screensRoot", screensRoot);
        SetObject(vso, "instructionText", instruction);
        SetObject(vso, "hintText", hint);
        SetObject(vso, "hotspotButton", hotspot);
        SetObject(vso, "hotspotLabel", hotLabel);
        SetObject(vso, "hotspotOutline", hotOutline);
        SetObject(vso, "decoyButton", decoy);
        SetObject(vso, "decoyLabel", decoyText);
        SetObject(vso, "urlInput", url);
        SetObject(vso, "goButton", goBtn);
        SetObject(vso, "progressBar", progress);
        SetObject(vso, "progressLabel", progressLabel);
        SetObject(vso, "alreadyInstalledBanner", banner.gameObject);
        vso.ApplyModifiedProperties();

        // ---- the manager's own panel references ----
        var m = manager != null ? manager : FindInScene<RufusSetupManager>();
        if (m != null)
        {
            var mso = new SerializedObject(m);
            SetObject(mso, "browserPanel", browserPanel);
            SetObject(mso, "desktopPanel", desktopPanel);
            mso.ApplyModifiedProperties();
        }

        // Same reason as above: this panel belongs to a desktop icon, not to scene load. You
        // will have had it ticked on to look at the layout - that is what leaves it running at
        // startup, so put it back.
        if (browserPanel.activeSelf)
        {
            Undo.RecordObject(browserPanel, "Hide Browser Panel");
            browserPanel.SetActive(false);
        }
    }

    /// <summary>
    /// Copies the rects straight out of the layout that was just drawn into the step assets.
    ///
    /// This is the point of drawing the screens: the builder knows exactly where it put the
    /// download link and the Yes button, so nobody has to drag a box onto a screenshot and
    /// guess. Change the layout, press rebuild, and the rects follow it.
    /// </summary>
    private void ApplyDrawnScreenRects(COC1BrowserScreens.Rects r)
    {
        SetRects("RS_01_OpenSite", input: r.addressBar, go: r.go);
        SetRects("RS_02_DownloadLink", hotspot: r.siteDownload, decoy: r.siteAd);
        SetRects("RS_03_Downloading", progress: r.downloadingBar);
        SetRects("RS_04_RunInstaller", hotspot: r.downloadsFile);
        SetRects("RS_05_UAC", hotspot: r.uacYes, decoy: r.uacNo);
        SetRects("RS_06_UpdatePolicy", hotspot: r.updateYes);
        SetRects("RS_07_Launching", progress: r.splashBar);

        AssetDatabase.SaveAssets();
    }

    private static void SetRects(string assetName, Rect? hotspot = null, Rect? decoy = null,
                                 Rect? input = null, Rect? go = null, Rect? progress = null)
    {
        var asset = AssetDatabase.LoadAssetAtPath<RufusSetupStepData>(
            RootFolder + "/" + assetName + ".asset");

        if (asset == null)
        {
            Debug.LogWarning("[COC1RufusSetupBuilder] " + assetName + " not found - press " +
                             "Build / Rebuild Step Assets first.");
            return;
        }

        if (hotspot.HasValue) asset.hotspot = hotspot.Value;
        if (decoy.HasValue) asset.decoyHotspot = decoy.Value;
        if (input.HasValue) asset.inputRect = input.Value;
        if (go.HasValue) asset.goRect = go.Value;
        if (progress.HasValue) asset.progressRect = progress.Value;

        EditorUtility.SetDirty(asset);
    }

    // ==================================================================
    // Helpers
    // ==================================================================

    private T GetOrCreate<T>(string folder, string assetName) where T : ScriptableObject
    {
        string path = folder + "/" + assetName + ".asset";

        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null) { updated++; return existing; }

        var asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        created++;
        return asset;
    }

    private DialogSequence MakeDialog(string assetName, string speaker, string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var seq = GetOrCreate<DialogSequence>(RootFolder + "/Dialogs", assetName);
        if (seq == null) return null;

        seq.lines = new List<DialogLine> { new DialogLine { speakerName = speaker, text = text } };
        EditorUtility.SetDirty(seq);
        return seq;
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

    private static bool WriteList(Object target, string fieldName, List<Object> values)
    {
        var so = new SerializedObject(target);
        var prop = so.FindProperty(fieldName);
        if (prop == null) return false;

        prop.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];

        so.ApplyModifiedProperties();
        return true;
    }

    private static Object GetObject(SerializedObject so, string field)
    {
        var p = so.FindProperty(field);
        return p != null ? p.objectReferenceValue : null;
    }

    private static void SetObject(SerializedObject so, string field, Object value)
    {
        var p = so.FindProperty(field);
        if (p == null)
        {
            Debug.LogWarning("[COC1RufusSetupBuilder] No serialized field '" + field + "'.");
            return;
        }
        p.objectReferenceValue = value;
    }

    private static string Name(Object o) { return o != null ? o.name : "NOT FOUND"; }

    private static T FindInScene<T>() where T : Component
    {
        foreach (var c in FindAllInScene<T>()) return c;
        return null;
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

    private static void MarkSceneDirty()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.IsValid())
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    }
}
#endif