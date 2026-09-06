#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using CSSSimulation.WindowsSim;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// Rebuilds every COC 3 step panel from scratch, in one consistent style.
///
/// Tools ▸ COC 3 ▸ Steps Container Builder
///
/// WHY THIS RATHER THAN THE STEP PANEL CONVERTER
/// The converter switched things off. That was the right tool while the desktop covered a few
/// steps and the rest had to keep working, but it leaves every panel carrying the furniture of the
/// old design with most of it hidden - and a panel that LOOKS empty and still has a raycasting
/// Image on it is the single most expensive bug in this project so far. This builds each panel
/// with only what its step actually needs, so there is nothing switched off to go wrong.
///
/// WHAT IT READS
/// The order and the StepData assets come from COC3Manager.steps - your existing list, in your
/// existing order. Nothing is read from disk and no step is invented, so a panel cannot end up
/// pointing at the wrong asset.
///
/// WHAT IT DOES NOT REBUILD
/// A PingTest step, and by default a ButtonInteraction step with no desktop binding (your hotspot
/// steps), are DUPLICATED from the old panel instead. Those carry wiring this builder cannot see -
/// PingSimulation's console, a hotspot placed over a particular part of a screenshot - and
/// rebuilding them from a StepData asset would quietly throw that away. They come across
/// unchanged, into the new container.
///
/// NOTHING IS DELETED. The old container is renamed and switched off. Ctrl+Z undoes the lot.
/// </summary>
public class COC3StepsContainerBuilder : EditorWindow
{
    private const string UndoLabel = "Rebuild COC3 Steps";

    private COC3Manager manager;
    private COC3StepGate stepGate;
    private RectTransform parent;
    private TMP_FontAsset font;

    private string containerName = "StepsContainer";
    private float stripHeight = 150f;
    private float cardWidth = 1100f;
    private float cardHeight = 620f;

    private bool rebuildHotspotSteps;
    private bool dimBackground = true;

    private string report = "";
    private Vector2 scroll;

    // Colours. Deliberately not a theme asset - these panels sit over the 3D station, not inside
    // the simulated Windows desktop, and borrowing COC3WinTheme would make them look like dialogs.
    private static readonly Color CardColor = new Color(0.07f, 0.09f, 0.12f, 0.97f);
    private static readonly Color StripColor = new Color(0.05f, 0.07f, 0.09f, 0.88f);
    private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.55f);
    private static readonly Color FieldColor = new Color(0.14f, 0.17f, 0.21f, 1f);
    private static readonly Color ButtonColor = new Color(0.16f, 0.42f, 0.78f, 1f);
    private static readonly Color TextColor = new Color(0.93f, 0.95f, 0.97f, 1f);
    private static readonly Color MutedColor = new Color(0.62f, 0.68f, 0.74f, 1f);

    [MenuItem("Tools/COC 3/Steps Container Builder")]
    private static void Open()
    {
        GetWindow<COC3StepsContainerBuilder>(false, "COC3 Steps", true).minSize =
            new Vector2(440f, 460f);
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("COC 3 - rebuild the step panels", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Builds one fresh panel per step, in COC3Manager's own order, and repoints the list " +
            "at them.\n\n" +
            "  Informational      instruction card + Next\n" +
            "  TextInput          instruction card + one box + Submit\n" +
            "  NetworkConfigInput instruction card + three boxes + Submit\n" +
            "  Desktop steps      instruction strip only, nothing clickable\n" +
            "  PingTest, hotspot  duplicated from the old panel, untouched\n\n" +
            "Your old container is renamed and switched off, not deleted.",
            MessageType.None);

        EditorGUILayout.Space();

        manager = (COC3Manager)EditorGUILayout.ObjectField(
            new GUIContent("COC3 Manager", "The step list and its order are read from here, and " +
                                           "repointed at the new panels when it finishes."),
            manager, typeof(COC3Manager), true);

        stepGate = (COC3StepGate)EditorGUILayout.ObjectField(
            new GUIContent("Step Gate", "Optional, but you want it. Its bindings are how a step is " +
                                        "known to be desktop-driven - and those panels get an " +
                                        "instruction strip and nothing else, because the desktop " +
                                        "in front of them is what the learner is using."),
            stepGate, typeof(COC3StepGate), true);

        parent = (RectTransform)EditorGUILayout.ObjectField(
            new GUIContent("Parent", "Left empty, the new container is created beside the old one."),
            parent, typeof(RectTransform), true);

        font = (TMP_FontAsset)EditorGUILayout.ObjectField(
            new GUIContent("Font", "Optional. Left empty, TextMeshPro's default is used."),
            font, typeof(TMP_FontAsset), false);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Layout", EditorStyles.boldLabel);

        containerName = EditorGUILayout.TextField("Container name", containerName);
        stripHeight = EditorGUILayout.FloatField("Strip height", stripHeight);
        cardWidth = EditorGUILayout.FloatField("Card width", cardWidth);
        cardHeight = EditorGUILayout.FloatField("Card height", cardHeight);

        dimBackground = EditorGUILayout.Toggle(
            new GUIContent("Dim behind cards", "A dark sheet behind the card on non-desktop steps, " +
                                               "so the station is not competing with the text. " +
                                               "Never added to a desktop step - that is what used " +
                                               "to swallow every click on the desktop."),
            dimBackground);

        rebuildHotspotSteps = EditorGUILayout.Toggle(
            new GUIContent("Rebuild hotspot steps",
                           "OFF: ButtonInteraction steps with no desktop binding are duplicated " +
                           "from your old panels, keeping their screenshot and their hotspot.\n\n" +
                           "ON: they are rebuilt as a screenshot with a Continue button. Tidier, " +
                           "but the hotspot's position over the screenshot is lost - and since " +
                           "Validate() passes any ButtonInteraction step, that position is the " +
                           "only thing making them an exercise."),
            rebuildHotspotSteps);

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(manager == null))
        {
            if (GUILayout.Button("Rebuild step panels", GUILayout.Height(32f)))
                EditorApplication.delayCall += Build;
        }

        if (!string.IsNullOrEmpty(report))
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(report, MessageType.Info);
        }

        EditorGUILayout.EndScrollView();
    }

    // ==================================================================

    private void Build()
    {
        if (manager == null || manager.steps == null || manager.steps.Count == 0)
        {
            report = "COC3Manager has no steps, so there is nothing to rebuild from.";
            Repaint();
            return;
        }

        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(UndoLabel);

        var log = new StringBuilder();

        try
        {
            // The old panels ARE the source of truth: their order, their StepData, and for the two
            // kinds we do not rebuild, the panels themselves.
            var old = new List<ScreenshotStep>(manager.steps);

            Transform oldContainer = FirstParent(old);

            if (oldContainer == null)
            {
                report = "Could not find the container - none of the steps in the list have a parent.";
                return;
            }

            var into = parent != null ? parent : oldContainer.parent as RectTransform;

            if (into == null)
            {
                report = "The old container has no RectTransform parent. Set Parent by hand.";
                return;
            }

            // Renamed FIRST, so the new container can take the name it had.
            Undo.RecordObject(oldContainer.gameObject, UndoLabel);
            string oldName = oldContainer.name;
            oldContainer.name = oldName + "_Old";

            var container = Full(into, containerName);

            // TAKES THE OLD CONTAINER'S PLACE IN THE SIBLING ORDER, not the end of the list.
            //
            // Sibling order is what decides which full-screen thing draws over which, and
            // COC3Desktop has to stay in front of the step panels or its icons are unreachable.
            // A new container appended as the last child would sit over the desktop and swallow
            // every click on it - the exact deadlock this project spent an evening on.
            if (into == oldContainer.parent)
                container.SetSiblingIndex(oldContainer.GetSiblingIndex());

            var built = new List<ScreenshotStep>();
            int rebuilt = 0, copied = 0, skipped = 0;

            for (int i = 0; i < old.Count; i++)
            {
                var source = old[i];

                if (source == null || source.stepData == null)
                {
                    log.AppendLine("  [" + i + "] empty slot - left out of the new list.");
                    skipped++;
                    continue;
                }

                bool desktop = IsDesktopDriven(source.stepData.stepId);
                bool duplicate = ShouldDuplicate(source.stepData.stepType, desktop);

                ScreenshotStep made = duplicate
                    ? Duplicate(container, source)
                    : Create(container, source, desktop, i);

                built.Add(made);

                if (duplicate) copied++; else rebuilt++;

                log.AppendLine("  " + Describe(i, source, desktop, duplicate));
            }

            // Only now, so a failure above leaves the manager pointing at panels that still exist.
            Undo.RecordObject(manager, UndoLabel);
            manager.steps = built;
            EditorUtility.SetDirty(manager);

            oldContainer.gameObject.SetActive(false);

            EditorUtility.SetDirty(container.gameObject);

            report = "Built '" + containerName + "'.\n\n" +
                     "  " + rebuilt + " rebuilt\n" +
                     "  " + copied + " duplicated from the old panels\n" +
                     (skipped > 0 ? "  " + skipped + " empty slots dropped\n" : "") +
                     "\n'" + oldName + "_Old' is switched off, not deleted.\n\n" + log;
        }
        catch (System.Exception e)
        {
            report = "Failed: " + e.Message + "\n\nNothing was saved - press Ctrl+Z.";
            Debug.LogException(e);
        }
        finally
        {
            Undo.CollapseUndoOperations(group);
            Repaint();
        }
    }

    private static Transform FirstParent(List<ScreenshotStep> steps)
    {
        foreach (var s in steps)
            if (s != null && s.transform.parent != null) return s.transform.parent;

        return null;
    }

    private bool IsDesktopDriven(string stepId)
    {
        if (stepGate == null || stepGate.bindings == null) return false;

        foreach (var b in stepGate.bindings)
            if (b != null && b.stepId == stepId && !string.IsNullOrEmpty(b.actionId)) return true;

        return false;
    }

    /// <summary>
    /// The two kinds this builder refuses to guess at.
    ///
    /// A PingTest panel is wired to PingSimulation's console, and an unbound ButtonInteraction step
    /// is a screenshot with a hotspot placed somewhere specific on it. Neither of those facts is
    /// recorded in the StepData asset, so rebuilding from the asset would silently lose them.
    /// </summary>
    private bool ShouldDuplicate(StepType type, bool desktopDriven)
    {
        if (type == StepType.PingTest) return true;
        if (type == StepType.ButtonInteraction && !desktopDriven && !rebuildHotspotSteps) return true;

        return false;
    }

    private static string Describe(int index, ScreenshotStep source, bool desktop, bool duplicate)
    {
        string kind = duplicate
            ? "duplicated"
            : desktop ? "strip only" : source.stepData.stepType.ToString();

        return (index + 1) + ". " + source.stepData.stepId + "  -  " + kind;
    }

    // ==================================================================
    // Panels
    // ==================================================================

    private ScreenshotStep Duplicate(RectTransform container, ScreenshotStep source)
    {
        var copy = (GameObject)Object.Instantiate(source.gameObject, container, false);
        Undo.RegisterCreatedObjectUndo(copy, UndoLabel);

        // Instantiate appends "(Clone)", and the panel names are how you find a step in a
        // hierarchy of thirty-five of them.
        copy.name = source.gameObject.name;
        copy.SetActive(false);

        return copy.GetComponent<ScreenshotStep>();
    }

    private ScreenshotStep Create(RectTransform container, ScreenshotStep source,
                                  bool desktopDriven, int index)
    {
        var data = source.stepData;

        var root = Full(container, source.gameObject.name);
        root.gameObject.AddComponent<CanvasGroup>();

        var step = root.gameObject.AddComponent<ScreenshotStep>();
        step.stepData = data;

        if (desktopDriven)
        {
            // NOTHING but the strip, and nothing on it that raycasts. This panel sits behind the
            // simulated desktop, and anything here that takes a click takes it away from a window.
            step.instructionLabel = Strip(root, data.instructionText);
            root.gameObject.SetActive(false);
            return step;
        }

        if (dimBackground)
        {
            var dim = Full(root, "Dim");
            var image = dim.gameObject.AddComponent<Image>();
            image.color = DimColor;
            image.raycastTarget = true;      // deliberate: the station behind is not clickable here
        }

        var card = Centred(root, "Card", cardWidth, cardHeight);
        card.gameObject.AddComponent<Image>().color = CardColor;

        float y = 36f;

        // Hotspot steps that were asked to be rebuilt keep their screenshot, copied off the old
        // panel - the sprite is usually on the panel rather than on the StepData asset.
        if (data.stepType == StepType.ButtonInteraction)
        {
            var shot = Node(card, "Screenshot", 36f, y, cardWidth - 72f, 280f);
            var image = shot.gameObject.AddComponent<Image>();
            image.color = Color.white;
            image.preserveAspect = true;
            image.raycastTarget = false;

            if (source.screenshotDisplay != null) image.sprite = source.screenshotDisplay.sprite;
            else if (data.screenshotImage != null) image.sprite = data.screenshotImage;

            step.screenshotDisplay = image;
            y += 300f;
        }

        step.instructionLabel = Body(card, "Instruction", 36f, y, cardWidth - 72f,
                                     data.stepType == StepType.Informational ? 400f : 190f,
                                     data.instructionText);

        y += data.stepType == StepType.Informational ? 420f : 210f;

        switch (data.stepType)
        {
            case StepType.NetworkConfigInput:
                step.ipInputField = Field(card, "IPAddress", "IP address", y);
                step.subnetInputField = Field(card, "SubnetMask", "Subnet mask", y + 66f);
                step.gatewayInputField = Field(card, "Gateway", "Default gateway", y + 132f);
                break;

            case StepType.TextInput:
                step.textInputField = Field(card, "TextInput",
                                            string.IsNullOrEmpty(data.textFieldLabel)
                                                ? "Value" : data.textFieldLabel,
                                            y);
                break;
        }

        string label = data.stepType == StepType.Informational ? "Next"
                     : data.stepType == StepType.ButtonInteraction ? "Continue"
                     : "Submit";

        Submit(card, label);

        root.gameObject.SetActive(false);
        return step;
    }

    // ==================================================================
    // Pieces
    // ==================================================================

    private TMP_Text Strip(RectTransform root, string content)
    {
        var strip = Node(root, "InstructionStrip", 0f, 0f, 0f, stripHeight);
        strip.anchorMin = new Vector2(0f, 1f);
        strip.anchorMax = new Vector2(1f, 1f);
        strip.pivot = new Vector2(0.5f, 1f);
        strip.offsetMin = new Vector2(0f, -stripHeight);
        strip.offsetMax = Vector2.zero;

        var backing = strip.gameObject.AddComponent<Image>();
        backing.color = StripColor;
        backing.raycastTarget = false;

        var text = Body(strip, "Text", 40f, 16f, 0f, stripHeight - 32f, content);

        // Stretched horizontally rather than given a fixed width, because the strip's own width
        // depends on the canvas and is not known here.
        var rt = text.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.offsetMin = new Vector2(40f, -stripHeight + 16f);
        rt.offsetMax = new Vector2(-40f, -16f);

        return text;
    }

    private TMP_Text Body(Transform parent, string name, float x, float y, float w, float h,
                          string content)
    {
        var rt = Node(parent, name, x, y, w, h);

        var text = rt.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = 22f;
        text.color = TextColor;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.raycastTarget = false;
        text.parseCtrlCharacters = false;    // a path in an instruction keeps both backslashes

        if (font != null) text.font = font;

        return text;
    }

    /// <summary>
    /// A labelled input box.
    ///
    /// Built by hand rather than with DefaultControls, because a TMP_InputField needs its viewport,
    /// text and placeholder wired to each other and DefaultControls builds the uGUI one. The
    /// placeholder is left EMPTY on purpose: ScreenshotStep.Setup() fills it from the StepData's
    /// textFieldLabel every time the step is shown, and a value written here would be overwritten
    /// on the first frame anyway.
    /// </summary>
    private TMP_InputField Field(RectTransform card, string name, string label, float y)
    {
        Body(card, name + "Label", 36f, y, 260f, 44f, label).color = MutedColor;

        var box = Node(card, name, 300f, y - 4f, cardWidth - 340f, 52f);
        var background = box.gameObject.AddComponent<Image>();
        background.color = FieldColor;

        // RectMask2D on the viewport is what stops long text spilling out of the box as it is
        // typed. Without it a UNC path runs off the side of the card.
        var viewport = Node(box, "Text Area", 12f, 6f, cardWidth - 364f, 40f);
        viewport.gameObject.AddComponent<RectMask2D>();

        var placeholder = Body(viewport, "Placeholder", 0f, 0f, cardWidth - 364f, 40f, "");
        placeholder.color = MutedColor;
        placeholder.alignment = TextAlignmentOptions.Left;

        var text = Body(viewport, "Text", 0f, 0f, cardWidth - 364f, 40f, "");
        text.alignment = TextAlignmentOptions.Left;

        var field = box.gameObject.AddComponent<TMP_InputField>();
        field.targetGraphic = background;
        field.textViewport = viewport;
        field.textComponent = text;
        field.placeholder = placeholder;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.characterLimit = 0;
        field.text = string.Empty;

        return field;
    }

    private void Submit(RectTransform card, string label)
    {
        var rt = Node(card, label + "Button", cardWidth - 236f, cardHeight - 84f, 200f, 52f);

        var background = rt.gameObject.AddComponent<Image>();
        background.color = ButtonColor;

        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = background;

        var text = Body(rt, "Label", 0f, 0f, 200f, 52f, label);
        text.alignment = TextAlignmentOptions.Center;

        // A PERSISTENT listener, so it shows in the Inspector's OnClick list exactly as if you had
        // dragged it there. A runtime AddListener would work and be invisible, which is worse.
        UnityEventTools.AddPersistentListener(button.onClick,
                                              new UnityAction(manager.SubmitCurrentStep));
    }

    // ==================================================================
    // Rect helpers
    // ==================================================================

    private static RectTransform Full(Transform parent, string name)
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

    private static RectTransform Centred(Transform parent, string name, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);

        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(w, h);
        rt.localScale = Vector3.one;
        return rt;
    }

    /// <summary>Top-left placement inside the parent, the model the desktop builder uses too.</summary>
    private static RectTransform Node(Transform parent, string name,
                                      float x, float y, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);

        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
        rt.localScale = Vector3.one;
        return rt;
    }
}
#endif
