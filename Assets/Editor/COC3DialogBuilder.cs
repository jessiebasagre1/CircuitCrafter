#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// COC 3 DIALOG BUILDER  -  Tools > COC3 > Dialog Builder
///
/// Replaces a step panel's contents with a recreated Windows dialog, and wires that
/// dialog's OWN controls into ScreenshotStep - so the box the learner types into is part
/// of the window, and the checkbox they click is the real checkbox.
///
/// PHASED BY DESIGN
/// It only touches steps that have an entry in COC3DialogBlueprint. Everything else is
/// left exactly as it is, so the activity stays fully playable while the remaining phases
/// are built. Run it again after each phase.
///
/// DESTRUCTIVE, ON PURPOSE
/// "Clear and rebuild" was the chosen behaviour: each panel it handles has its children
/// deleted before the window is constructed. That is what stops half-migrated panels -
/// leftover input boxes and hotspots from the screenshot era sitting behind the new
/// window. It asks first, and the whole run is a single Undo.
/// </summary>
public class COC3DialogBuilder : EditorWindow
{
    private COC3Manager manager;
    private COC3WinTheme theme;

    private bool autoFitWindow = true;
    private float windowHeightFraction = 0.72f;
    private float manualScale = 1.6f;
    private bool showInstructionBar = true;

    private Vector2 scroll;
    private readonly List<string> report = new List<string>();

    [MenuItem("Tools/COC3/Dialog Builder")]
    public static void Open()
    {
        var win = GetWindow<COC3DialogBuilder>("COC3 Dialogs");
        win.minSize = new Vector2(430f, 470f);
        win.TryAutoFind();
    }

    private void TryAutoFind()
    {
        if (manager == null)
        {
            foreach (var m in Resources.FindObjectsOfTypeAll<COC3Manager>())
            {
                if (m == null || EditorUtility.IsPersistent(m)) continue;
                if (m.hideFlags != HideFlags.None) continue;
                if (!m.gameObject.scene.IsValid()) continue;
                manager = m;
                break;
            }
        }

        if (theme == null)
        {
            var guids = AssetDatabase.FindAssets("t:COC3WinTheme", new[] { "Assets" });
            if (guids.Length > 0)
                theme = AssetDatabase.LoadAssetAtPath<COC3WinTheme>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }

    // ------------------------------------------------------------------

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("Recreated Windows dialogs", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            $"{COC3DialogBlueprint.DefinedCount} step(s) currently have a window defined. " +
            "Steps without one are left untouched, so the activity stays playable between phases.",
            MessageType.Info);

        EditorGUILayout.Space();
        manager = (COC3Manager)EditorGUILayout.ObjectField(
            new GUIContent("COC3 Manager", "Its Steps list is the source of which panels exist."),
            manager, typeof(COC3Manager), true);

        theme = (COC3WinTheme)EditorGUILayout.ObjectField(
            new GUIContent("Theme", "Create > COC3 > Windows UI Theme. Leave empty to use built-in defaults."),
            theme, typeof(COC3WinTheme), false);

        if (theme == null)
            EditorGUILayout.HelpBox("No theme asset assigned - default Windows 10 colours will be used, " +
                                    "with the TMP default font.", MessageType.None);
        else if (theme.uiFont == null)
            EditorGUILayout.HelpBox("The theme has no font assigned. Import Inter or Open Sans, make a TMP " +
                                    "Font Asset from it, and drop it into the theme's UI Font slot.",
                                    MessageType.Warning);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Sizing", EditorStyles.boldLabel);

        autoFitWindow = EditorGUILayout.Toggle(
            new GUIContent("Auto-Fit Window", "Scale each window so it fills this fraction of the panel height. Recommended - dialog sizes are authentic pixel sizes and would look tiny on a 1080p canvas."),
            autoFitWindow);

        if (autoFitWindow)
            windowHeightFraction = EditorGUILayout.Slider(
                new GUIContent("Height Fraction", "How much of the panel height the window should occupy."),
                windowHeightFraction, 0.4f, 0.95f);
        else
            manualScale = EditorGUILayout.Slider(new GUIContent("Manual Scale"), manualScale, 0.5f, 4f);

        showInstructionBar = EditorGUILayout.Toggle(
            new GUIContent("Instruction Bar", "Create the step's instruction text across the top of the panel, above the window."),
            showInstructionBar);

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(manager == null))
        {
            if (GUILayout.Button("BUILD DIALOGS", GUILayout.Height(36f)))
                BuildAll();
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Each panel that has a window defined has its EXISTING CHILDREN DELETED before " +
            "the window is built. Ctrl+Z undoes the whole run.", MessageType.Warning);

        if (report.Count > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Report", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(string.Join("\n", report), GUILayout.MinHeight(200f));
        }

        EditorGUILayout.EndScrollView();
    }

    // ------------------------------------------------------------------

    private void BuildAll()
    {
        if (manager.steps == null || manager.steps.Count == 0)
        {
            report.Clear();
            Log("ERROR: COC3Manager.steps is empty. Run the Scene Builder first.");
            Repaint();
            return;
        }

        int defined = 0;
        foreach (var s in manager.steps)
            if (s != null && COC3DialogBlueprint.For(StepKey(s)) != null) defined++;

        if (defined == 0)
        {
            report.Clear();
            Log("Nothing to do - none of the steps in this scene have a window defined yet.");
            Log("Check that the panel names match the blueprint (Scene Builder > Rename Panels To Match Blueprint).");
            Repaint();
            return;
        }

        if (!EditorUtility.DisplayDialog("Rebuild step panels?",
                $"{defined} panel(s) will have their existing children DELETED and replaced with a " +
                $"recreated Windows dialog.\n\nEverything else is left alone. Ctrl+Z undoes the whole run.",
                "Build", "Cancel"))
            return;

        report.Clear();
        var t = theme != null ? theme : COC3WinTheme.CreateDefault();

        int undoGroup = Undo.GetCurrentGroup();
        int built = 0, skipped = 0;

        for (int i = 0; i < manager.steps.Count; i++)
        {
            var step = manager.steps[i];
            if (step == null) continue;

            var def = COC3DialogBlueprint.For(StepKey(step));
            if (def == null) { skipped++; continue; }

            BuildOne(step, def, t, i + 1);
            built++;
        }

        Undo.CollapseUndoOperations(undoGroup);

        Log("");
        Log($"Built {built} dialog(s). Left {skipped} step(s) untouched.");

        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        Debug.Log("[COC3DialogBuilder]\n" + string.Join("\n", report));
        Repaint();
    }

    /// <summary>Panels are matched by GameObject name first, falling back to the step id on
    /// the data asset - so a panel that has not been renamed yet still resolves.</summary>
    private static string StepKey(ScreenshotStep step)
    {
        if (step.stepData != null && !string.IsNullOrEmpty(step.stepData.stepId))
        {
            if (COC3DialogBlueprint.For(step.stepData.stepId) != null) return step.stepData.stepId;
        }
        return step.name;
    }

    private void BuildOne(ScreenshotStep step, COC3DialogBlueprint.DialogDef def, COC3WinTheme t, int number)
    {
        var panel = (RectTransform)step.transform;

        // ---- clear ----
        for (int i = panel.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(panel.GetChild(i).gameObject);

        Undo.RecordObject(step, "COC3 Build Dialog");
        step.screenshotDisplay = null;
        step.instructionLabel = null;
        step.ipInputField = null;
        step.subnetInputField = null;
        step.gatewayInputField = null;
        step.textInputField = null;
        step.interactionButtons = new Button[0];

        // ---- instruction bar ----
        if (showInstructionBar)
        {
            var barRt = COC3WinWidgets.Rect(panel, "InstructionLabel", 0f, 0f, 0f, 0f);
            barRt.anchorMin = new Vector2(0.05f, 0.84f);
            barRt.anchorMax = new Vector2(0.95f, 0.98f);
            barRt.offsetMin = Vector2.zero;
            barRt.offsetMax = Vector2.zero;

            var label = barRt.gameObject.AddComponent<TextMeshProUGUI>();
            if (t.uiFont != null) label.font = t.uiFont;
            label.fontSize = 26f;
            label.enableAutoSizing = true;
            label.fontSizeMin = 12f;
            label.fontSizeMax = 30f;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.text = step.stepData != null ? step.stepData.instructionText : string.Empty;

            step.instructionLabel = label;
        }

        // ---- window ----
        RectTransform windowRoot, body;
        float bodyW, bodyH;

        if (def.frame == COC3DialogBlueprint.Frame.Wizard)
        {
            body = COC3WinWidgets.Wizard(panel, t, "Window", def.title, def.heading,
                                         def.stages ?? new string[0], def.activeStage,
                                         def.size.x, def.size.y, out windowRoot, out bodyW, out bodyH);
        }
        else if (def.frame == COC3DialogBlueprint.Frame.Bare)
        {
            // Consoles draw their own panes flush to the frame, so no body padding.
            body = COC3WinWidgets.WindowBare(panel, t, "Window", def.title, def.size.x, def.size.y,
                                             out windowRoot);
            bodyW = def.size.x - 2f;
            bodyH = def.size.y - 2f - t.titleBarHeight - 1f;
        }
        else
        {
            body = COC3WinWidgets.Window(panel, t, "Window", def.title, def.size.x, def.size.y,
                                         out windowRoot);
            bodyW = def.size.x - 2f - t.padding * 2f;
            bodyH = def.size.y - 2f - t.titleBarHeight - t.padding * 2f;
        }

        var bind = new COC3DialogBlueprint.DialogBindings();
        def.build(body, t, bind, bodyW, bodyH);

        // ---- button bar, pinned to the bottom of the body ----
        Button[] buttons = null;
        if (def.buttons != null && def.buttons.Length > 0)
        {
            buttons = COC3WinWidgets.ButtonBar(body, t, "Buttons", bodyW,
                                               bodyH - t.buttonSize.y, def.buttons, def.submitButtonIndex);
        }

        // ---- scale the window to the panel ----
        float scale = manualScale;
        if (autoFitWindow)
        {
            float panelHeight = panel.rect.height;
            if (panelHeight > 1f)
                scale = Mathf.Clamp((panelHeight * windowHeightFraction) / def.size.y, 0.5f, 4f);
        }
        windowRoot.localScale = new Vector3(scale, scale, 1f);

        // ---- bind to the step ----
        var notes = new System.Text.StringBuilder();
        StepType type = step.stepData != null ? step.stepData.stepType : StepType.Informational;

        switch (type)
        {
            case StepType.NetworkConfigInput:
                step.ipInputField = bind.ip;
                step.subnetInputField = bind.mask;
                step.gatewayInputField = bind.gateway;
                if (bind.ip == null || bind.mask == null || bind.gateway == null)
                    notes.Append("  ERROR: dialog did not bind all three network fields - " +
                                 "Validate() will throw on submit.");
                WireSubmit(buttons, def.submitButtonIndex, notes);
                break;

            case StepType.TextInput:
                step.textInputField = bind.answer;
                if (bind.answer == null)
                    notes.Append("  ERROR: dialog did not set bind.answer - nothing to type into.");
                WireSubmit(buttons, def.submitButtonIndex, notes);
                break;

            case StepType.ButtonInteraction:
                if (bind.target != null)
                {
                    step.interactionButtons = new[] { bind.target };
                    WireToSubmit(bind.target);
                }
                else
                {
                    notes.Append("  ERROR: dialog did not set bind.target - the learner cannot advance.");
                }
                break;

            case StepType.PingTest:
                // The console the learner watches IS PingSimulation's output target, and the
                // RUN PING button is the one it hides while the replies scroll in.
                if (manager.pingSimulation == null)
                {
                    notes.Append("  WARNING: COC3Manager.pingSimulation is not assigned - " +
                                 "the ping step will do nothing.");
                }
                else if (bind.console == null)
                {
                    notes.Append("  ERROR: dialog did not set bind.console - no output to write to.");
                }
                else
                {
                    Undo.RecordObject(manager.pingSimulation, "COC3 Wire Ping");
                    manager.pingSimulation.pingOutputText = bind.console;

                    if (buttons != null && def.submitButtonIndex >= 0 &&
                        def.submitButtonIndex < buttons.Length)
                        manager.pingSimulation.pingButton = buttons[def.submitButtonIndex].gameObject;

                    EditorUtility.SetDirty(manager.pingSimulation);
                }
                WireSubmit(buttons, def.submitButtonIndex, notes);
                break;

            default:   // Informational
                WireSubmit(buttons, def.submitButtonIndex, notes);
                break;
        }

        EditorUtility.SetDirty(step);
        Log($"[{number:00}] {step.name}  ->  \"{def.title}\"  ({type}, scale {scale:0.00}){notes}");
    }

    private void WireSubmit(Button[] buttons, int index, System.Text.StringBuilder notes)
    {
        if (buttons == null || index < 0 || index >= buttons.Length)
        {
            notes.Append("  WARNING: no submit button on this dialog.");
            return;
        }
        WireToSubmit(buttons[index]);
    }

    private void WireToSubmit(Button btn)
    {
        Undo.RecordObject(btn, "COC3 Wire Dialog Button");

        for (int i = btn.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            if (btn.onClick.GetPersistentTarget(i) is COC3Manager)
                UnityEventTools.RemovePersistentListener(btn.onClick, i);

        UnityEventTools.AddPersistentListener(btn.onClick, manager.SubmitCurrentStep);
        EditorUtility.SetDirty(btn);
    }

    private void Log(string line) => report.Add(line);
}
#endif