#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Strips the old screenshot-and-input furniture off every step panel that has been converted to
/// the simulated desktop, leaving the instruction as a strip across the top.
///
/// Tools ▸ COC 3 ▸ Step Panel Converter
///
/// WHY A TOOL
/// Doing it by hand is four or five unticks per panel, and getting one wrong is invisible: an
/// Image left with Raycast Target on is a transparent sheet over the desktop that blocks every
/// click and looks like nothing at all. That is the bug you already hit once.
///
/// IT ONLY DISABLES, NEVER DELETES. Every object it touches is switched off, not destroyed, and
/// the whole pass is one undo step - Ctrl+Z puts the panels back exactly as they were. Nothing is
/// lost if you decide a step should go back to the old style.
///
/// WHICH PANELS
/// Every step whose StepData has Step Type = ButtonInteraction. That is precisely the set you
/// converted: ScreenshotStep.Validate() returns true for that type without comparing anything,
/// because the marking moved into WinTaskGate. Steps of any other type are left alone.
/// </summary>
public class COC3StepPanelConverter : EditorWindow
{
    private const string UndoLabel = "Convert COC3 Step Panels";

    private COC3Manager manager;
    private float stripHeight = 150f;
    private bool addBacking = true;
    private bool moveInstruction = true;
    private string report = "";
    private Vector2 scroll;

    [MenuItem("Tools/COC 3/Step Panel Converter")]
    private static void Open()
    {
        GetWindow<COC3StepPanelConverter>(false, "Step Panels", true).minSize = new Vector2(420f, 320f);
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("COC 3 - convert step panels", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "For every step with Step Type = ButtonInteraction:\n\n" +
            "  • switches off the screenshot\n" +
            "  • switches off the input rows\n" +
            "  • switches off any button wired to COC3Manager\n" +
            "  • unticks Raycast Target on full-screen Images\n" +
            "  • moves the instruction into a strip across the top\n\n" +
            "Nothing is deleted. Ctrl+Z undoes the whole pass.",
            MessageType.None);

        EditorGUILayout.Space();

        manager = (COC3Manager)EditorGUILayout.ObjectField("COC3 Manager", manager,
                                                           typeof(COC3Manager), true);

        moveInstruction = EditorGUILayout.Toggle(
            new GUIContent("Move instruction", "Re-anchor the instruction label to a strip across " +
                                               "the top. Untick to leave your layout alone."),
            moveInstruction);

        using (new EditorGUI.DisabledScope(!moveInstruction))
        {
            stripHeight = EditorGUILayout.FloatField("Strip height", stripHeight);
            addBacking = EditorGUILayout.Toggle(
                new GUIContent("Add backing", "A dark panel behind the strip so the text stays " +
                                              "readable over the desktop."),
                addBacking);
        }

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(manager == null))
        {
            if (GUILayout.Button("Convert ButtonInteraction panels", GUILayout.Height(30f)))
                EditorApplication.delayCall += Convert;
        }

        if (!string.IsNullOrEmpty(report))
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(report, MessageType.Info);
        }

        EditorGUILayout.EndScrollView();
    }

    // ==================================================================

    private void Convert()
    {
        if (manager == null || manager.steps == null) return;

        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(UndoLabel);

        var log = new StringBuilder();
        int converted = 0;

        try
        {
            foreach (var step in manager.steps)
            {
                if (step == null || step.stepData == null) continue;
                if (step.stepData.stepType != StepType.ButtonInteraction) continue;

                log.AppendLine(ConvertOne(step));
                converted++;
            }

            report = converted == 0
                ? "No ButtonInteraction steps found. Set the Step Type on the StepData assets first."
                : "Converted " + converted + " panel(s).\n\n" + log;
        }
        catch (System.Exception e)
        {
            report = "Failed: " + e.Message;
            Debug.LogException(e);
        }
        finally
        {
            Undo.CollapseUndoOperations(group);
            Repaint();
        }
    }

    private string ConvertOne(ScreenshotStep step)
    {
        var parts = new List<string>();

        // --- the screenshot ---
        if (step.screenshotDisplay != null && step.screenshotDisplay.gameObject.activeSelf)
        {
            Undo.RecordObject(step.screenshotDisplay.gameObject, UndoLabel);
            step.screenshotDisplay.gameObject.SetActive(false);
            parts.Add("screenshot");
        }

        // --- the input rows ---
        // The label sits alongside each field rather than inside it, so switching off the field's
        // PARENT takes the row with it. Falling back to the field itself when it has no parent row
        // would leave an orphan label, which is why the parent is preferred.
        int fields = 0;
        fields += HideRow(step.ipInputField);
        fields += HideRow(step.subnetInputField);
        fields += HideRow(step.gatewayInputField);
        fields += HideRow(step.textInputField);
        if (fields > 0) parts.Add(fields + " input row(s)");

        // --- anything that submits ---
        int buttons = HideManagerButtons(step);
        if (buttons > 0) parts.Add(buttons + " submit button(s)");

        // --- invisible click blockers ---
        int blockers = ClearRaycastBlockers(step);
        if (blockers > 0) parts.Add(blockers + " raycast blocker(s)");

        // --- the instruction ---
        if (moveInstruction && step.instructionLabel != null)
        {
            MoveToStrip(step);
            parts.Add("instruction moved to a strip");
        }

        return "  " + step.name + ": " +
               (parts.Count == 0 ? "nothing to change" : string.Join(", ", parts.ToArray()));
    }

    private static int HideRow(TMP_InputField field)
    {
        if (field == null) return 0;

        var target = field.transform.parent != null
            ? field.transform.parent.gameObject
            : field.gameObject;

        if (!target.activeSelf) return 0;

        Undo.RecordObject(target, UndoLabel);
        target.SetActive(false);
        return 1;
    }

    /// <summary>
    /// Switches off any Button under this panel whose OnClick points at the COC3Manager.
    ///
    /// This is the one that matters most. A ButtonInteraction step passes Validate() without
    /// checking anything, so a submit button left on screen lets the learner complete the step
    /// without touching the desktop at all - the whole exercise, bypassed by one click.
    ///
    /// Found by reading the persistent listeners rather than by name, because the button is called
    /// something different on nearly every panel.
    /// </summary>
    private int HideManagerButtons(ScreenshotStep step)
    {
        int hidden = 0;

        foreach (var button in step.GetComponentsInChildren<Button>(true))
        {
            if (button == null || !button.gameObject.activeSelf) continue;

            bool targetsManager = false;

            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                var target = button.onClick.GetPersistentTarget(i);
                if (target == manager || target == manager.gameObject)
                {
                    targetsManager = true;
                    break;
                }
            }

            if (!targetsManager) continue;

            Undo.RecordObject(button.gameObject, UndoLabel);
            button.gameObject.SetActive(false);
            hidden++;
        }

        return hidden;
    }

    /// <summary>
    /// Unticks Raycast Target on Images that cover most of the panel.
    ///
    /// A panel background left raycastable is a transparent sheet over the desktop: the icons are
    /// visible, they highlight nothing, and no click reaches them. It is invisible in the Scene
    /// view and in the Inspector, and it is the single most likely thing to go wrong here.
    ///
    /// Only large ones, judged against the panel's own rect. A small Image is a divider or an icon
    /// and switching off its raycast could break something that is meant to be clicked.
    /// </summary>
    private static int ClearRaycastBlockers(ScreenshotStep step)
    {
        var panel = step.GetComponent<RectTransform>();
        if (panel == null) return 0;

        float panelArea = Mathf.Max(1f, panel.rect.width * panel.rect.height);
        int cleared = 0;

        foreach (var image in step.GetComponentsInChildren<Image>(true))
        {
            if (image == null || !image.raycastTarget) continue;

            var rt = image.rectTransform;
            float area = rt.rect.width * rt.rect.height;

            if (area < panelArea * 0.5f) continue;

            Undo.RecordObject(image, UndoLabel);
            image.raycastTarget = false;
            cleared++;
        }

        return cleared;
    }

    /// <summary>
    /// Re-anchors the instruction to a strip across the top of the panel, with an optional dark
    /// backing so it stays readable over whatever the desktop is showing behind it.
    /// </summary>
    private void MoveToStrip(ScreenshotStep step)
    {
        var label = step.instructionLabel.rectTransform;

        Undo.RecordObject(label, UndoLabel);

        label.anchorMin = new Vector2(0f, 1f);
        label.anchorMax = new Vector2(1f, 1f);
        label.pivot = new Vector2(0.5f, 1f);
        label.offsetMin = new Vector2(40f, -stripHeight + 16f);
        label.offsetMax = new Vector2(-40f, -16f);

        var text = step.instructionLabel;
        Undo.RecordObject(text, UndoLabel);
        text.alignment = TextAlignmentOptions.TopLeft;
        text.raycastTarget = false;

        if (!addBacking) return;

        // Behind the label, so it must be an EARLIER sibling - later siblings draw in front.
        var existing = step.transform.Find("InstructionBacking");
        if (existing != null) return;

        var go = new GameObject("InstructionBacking", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);

        var rt = (RectTransform)go.transform;
        rt.SetParent(step.transform, false);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(0f, -stripHeight);
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;

        var img = go.AddComponent<Image>();
        img.color = new Color(0.05f, 0.07f, 0.09f, 0.88f);
        img.raycastTarget = false;      // never block the desktop underneath

        rt.SetSiblingIndex(label.GetSiblingIndex());
    }
}
#endif
