// AiTutorBuilder.cs
//
// EDITOR ONLY. Put this file in a folder called Editor (Assets/Scripts/Editor is fine).
// Unity refuses to build a player if an editor script sits anywhere else.
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CSSSimulation.Tutor
{
    /// <summary>
    /// Builds the whole AI tutor - canvas, icon, window, input box - and wires every field.
    ///
    /// WHY A BUILDER FOR THIS ONE
    ///
    /// This has to go into four scenes. Assembling a scroll view and a TMP input field by hand is
    /// about twenty steps, and a TMP input field in particular has three child objects that must
    /// be assigned to three specific fields on it - miss one and the box takes typing but shows
    /// nothing, with no error anywhere. Doing that four times by hand is four chances to get it
    /// subtly different, and then the bug only shows up in one module.
    ///
    /// The layout it produces is plain on purpose. Restyle it freely: colours, sizes, corner
    /// radius, where the icon sits. Nothing here reads the layout back, so moving things cannot
    /// break the chat.
    ///
    /// USE: Tools > CircuitCrafter > AI Tutor Builder, once per scene. Then set Coc Number on the
    /// AiTutorChat component and paste your key into GeminiClient.
    /// </summary>
    public static class AiTutorBuilder
    {
        const string CanvasName = "AiTutorCanvas";

        [MenuItem("Tools/CircuitCrafter/AI Tutor Builder")]
        public static void Build()
        {
            GameObject existing = GameObject.Find(CanvasName);
            if (existing != null)
            {
                Selection.activeGameObject = existing;
                EditorGUIUtility.PingObject(existing);
                Debug.LogWarning("[AiTutorBuilder] This scene already has an '" + CanvasName +
                                 "'. It has been selected in the Hierarchy rather than a second " +
                                 "one being made - two would fight over the same screen corner. " +
                                 "Delete it first if you want a clean rebuild.", existing);
                return;
            }

            // ---------------------------------------------------------------- canvas

            GameObject canvasGO = new GameObject(CanvasName,
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(canvasGO, "Build AI Tutor");

            Canvas canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Above the world HUD so the icon is never buried, below the dialog canvas so a
            // tutorial popup still comes out on top. Move it if your dialogs sit lower.
            canvas.sortingOrder = 250;

            CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // ---------------------------------------------------------------- the icon

            GameObject iconGO = Panel("ChatButton", canvasGO.transform, new Color(0.16f, 0.45f, 0.85f));
            RectTransform icon = iconGO.GetComponent<RectTransform>();

            // Wide rather than round, because the label carries the key on a keyboard - "AI  Tab"
            // rather than "AI". A round badge would have to drop the key, and the key is the half
            // that teaches the player something.
            Anchor(icon, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -40),
                   new Vector2(210, 96));
            Button iconButton = iconGO.AddComponent<Button>();

            TextMeshProUGUI iconLabel = Label("Label", iconGO.transform, "AI  Tab", 34,
                                              TextAlignmentOptions.Center);
            Stretch(iconLabel.rectTransform);

            // ---------------------------------------------------------------- the window

            GameObject panelGO = Panel("ChatPanel", canvasGO.transform, new Color(0.09f, 0.10f, 0.13f, 0.97f));
            RectTransform panel = panelGO.GetComponent<RectTransform>();
            Anchor(panel, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-40, 0),
                   new Vector2(760, 900));

            TextMeshProUGUI title = Label("Title", panelGO.transform, "AI Tutor", 34,
                                          TextAlignmentOptions.Left);
            Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -26),
                   new Vector2(400, 48));

            GameObject closeGO = Panel("CloseButton", panelGO.transform, new Color(0.35f, 0.14f, 0.14f));
            Anchor(closeGO.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(1, 1),
                   new Vector2(-20, -20), new Vector2(56, 56));
            Button closeButton = closeGO.AddComponent<Button>();
            Stretch(Label("Label", closeGO.transform, "X", 28, TextAlignmentOptions.Center).rectTransform);

            // ---------------------------------------------------------------- the message list

            GameObject scrollGO = Panel("Messages", panelGO.transform, new Color(0, 0, 0, 0.25f));
            RectTransform scrollRT = scrollGO.GetComponent<RectTransform>();
            Stretch(scrollRT);
            scrollRT.offsetMin = new Vector2(20, 150);     // leaves room for the input row
            scrollRT.offsetMax = new Vector2(-20, -86);    // leaves room for the title

            ScrollRect scroll = scrollGO.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            // The viewport is what clips the list. RectMask2D rather than Mask: no extra material,
            // no stencil buffer, and it costs nothing on a phone.
            GameObject viewportGO = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportGO.transform.SetParent(scrollGO.transform, false);
            RectTransform viewport = viewportGO.GetComponent<RectTransform>();
            Stretch(viewport);

            GameObject contentGO = new GameObject("Content", typeof(RectTransform));
            contentGO.transform.SetParent(viewportGO.transform, false);
            RectTransform content = contentGO.GetComponent<RectTransform>();

            // Anchored to the TOP and growing downward. Anchored anywhere else and the list creeps
            // off the top of the viewport as it fills.
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = new Vector2(0, 0);
            content.offsetMax = new Vector2(0, 0);
            content.sizeDelta = new Vector2(0, 0);

            VerticalLayoutGroup rows = contentGO.AddComponent<VerticalLayoutGroup>();
            rows.padding = new RectOffset(16, 16, 16, 16);
            rows.spacing = 14;
            rows.childControlWidth = true;
            rows.childControlHeight = true;
            rows.childForceExpandWidth = true;
            rows.childForceExpandHeight = false;

            ContentSizeFitter fitter = contentGO.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewport;
            scroll.content = content;

            // The row every message is cloned from. Left switched off - it is a stencil, not a
            // message, and AiTutorChat says so out loud if it is ever found active.
            TextMeshProUGUI template = Label("MessageTemplate", contentGO.transform,
                                             "Message goes here", 28, TextAlignmentOptions.TopLeft);
            template.rectTransform.anchorMin = new Vector2(0, 1);
            template.rectTransform.anchorMax = new Vector2(1, 1);
            template.gameObject.SetActive(false);

            // ---------------------------------------------------------------- status + input row

            TextMeshProUGUI status = Label("StatusLabel", panelGO.transform, "", 22,
                                           TextAlignmentOptions.Left);
            status.color = new Color(0.75f, 0.78f, 0.85f);
            Anchor(status.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(24, 124),
                   new Vector2(600, 28));

            TMP_InputField input = InputField("InputField", panelGO.transform,
                                              "Ask about this task...");
            Anchor(input.GetComponent<RectTransform>(), new Vector2(0, 0), new Vector2(0, 0),
                   new Vector2(20, 24), new Vector2(560, 88));

            GameObject sendGO = Panel("SendButton", panelGO.transform, new Color(0.16f, 0.45f, 0.85f));
            Anchor(sendGO.GetComponent<RectTransform>(), new Vector2(1, 0), new Vector2(1, 0),
                   new Vector2(-20, 24), new Vector2(150, 88));
            Button sendButton = sendGO.AddComponent<Button>();
            Stretch(Label("Label", sendGO.transform, "Send", 28, TextAlignmentOptions.Center).rectTransform);

            panelGO.SetActive(false);

            // ---------------------------------------------------------------- components

            canvasGO.AddComponent<InternetWatcher>();
            canvasGO.AddComponent<GeminiClient>();
            AiTutorChat chat = canvasGO.AddComponent<AiTutorChat>();

            chat.openButton = iconButton;
            chat.panel = panelGO;
            chat.closeButton = closeButton;
            chat.sendButton = sendButton;
            chat.inputField = input;
            chat.messageRoot = content;
            chat.messageTemplate = template.gameObject;
            chat.scrollRect = scroll;
            chat.statusLabel = status;

            EditorUtility.SetDirty(chat);
            Selection.activeGameObject = canvasGO;
            EditorGUIUtility.PingObject(canvasGO);

            Debug.Log("[AiTutorBuilder] Built '" + CanvasName + "' at sort order " +
                      canvas.sortingOrder + ".\n\n" +
                      "Three things left, all on this object:\n" +
                      "  1. AiTutorChat > Coc Number - set it to this scene's module (1-4).\n" +
                      "  2. GeminiClient > Api Key - paste a key from aistudio.google.com/apikey.\n" +
                      "  3. AiTutorChat > Freeze While Open - drag in your movement and " +
                      "mouse-look scripts. The interactor and the station Back button are found " +
                      "automatically at Start; movement scripts cannot be.\n\n" +
                      "Then press Play. The icon appears once the internet has actually been " +
                      "reached, which takes a second or two - that delay is deliberate.",
                      canvasGO);
        }

        // ---------------------------------------------------------------- little makers

        static GameObject Panel(string name, Transform parent, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return go;
        }

        static TextMeshProUGUI Label(string name, Transform parent, string text, float size,
                                     TextAlignmentOptions align)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.alignment = align;
            t.color = Color.white;
            t.raycastTarget = false;      // a label over a button must not swallow the tap
            return t;
        }

        /// <summary>
        /// Builds a TMP input field with its three children wired.
        ///
        /// A TMP_InputField does not draw its own text. It needs a viewport with a mask, a text
        /// component inside that viewport, and a placeholder - and all three have to be assigned
        /// to fields ON the input field. Adding the component alone gives you a box that accepts
        /// typing and displays absolutely nothing, with no warning of any kind. That is the single
        /// most common reason a hand-built chat box appears broken.
        /// </summary>
        static TMP_InputField InputField(string name, Transform parent, string placeholderText)
        {
            GameObject go = Panel(name, parent, new Color(1f, 1f, 1f, 0.10f));
            TMP_InputField field = go.AddComponent<TMP_InputField>();

            GameObject areaGO = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            areaGO.transform.SetParent(go.transform, false);
            RectTransform area = areaGO.GetComponent<RectTransform>();
            Stretch(area);
            area.offsetMin = new Vector2(18, 10);
            area.offsetMax = new Vector2(-18, -10);

            TextMeshProUGUI placeholder = Label("Placeholder", areaGO.transform, placeholderText, 26,
                                                TextAlignmentOptions.Left);
            placeholder.color = new Color(1f, 1f, 1f, 0.4f);
            placeholder.fontStyle = FontStyles.Italic;
            Stretch(placeholder.rectTransform);

            TextMeshProUGUI text = Label("Text", areaGO.transform, "", 26, TextAlignmentOptions.Left);
            Stretch(text.rectTransform);

            field.textViewport = area;
            field.textComponent = text;
            field.placeholder = placeholder;

            // Single line, so Enter submits instead of adding a newline. That is what makes the
            // keyboard usable without reaching for the Send button.
            field.lineType = TMP_InputField.LineType.SingleLine;

            return field;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static void Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.pivot = min;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }
    }
}
#endif