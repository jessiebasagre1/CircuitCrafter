using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CircuitCrafter.Guidance.EditorTools
{
    /// <summary>
    /// Builds the whole guidance UI in the open scene, with every reference already wired:
    ///
    ///   GuidanceCanvas (Screen Space Overlay, sort order 60)
    ///     ObjectiveHud      - Current Task panel, top left
    ///     FeedbackToast     - "what went wrong / how to fix it", top centre
    ///     TutorialCard      - explain + demonstrate card, right side
    ///     GuideMarker       - the tooltip beside a highlighted object
    ///     JobOrder          - the job order window (J)
    ///     AssessmentReview  - the results and review screen
    ///
    /// RUN IT WITH: CircuitCrafter > Guidance > Build Guidance UI In Open Scene - once per gameplay
    /// scene (COC1, COC2, COC3, every COC4 repair site) and in COC4-Results (where only the review
    /// is needed - delete the rest there if you like).
    ///
    /// Safe to run twice: a second run adds nothing if a GuidanceCanvas already exists.
    ///
    /// Every panel's ROOT stays active and holds the component; the visible part is a child
    /// "Panel" that the component switches. That is deliberate - a component on the object it hides
    /// stops running the moment it hides itself, and could never come back.
    /// </summary>
    public static class GuidanceUIBuilder
    {
        private const string UndoName = "Build Guidance UI";

        // Palette - the same as CompetencyScreenBuilder, so this reads as one game.
        private static readonly Color PageDim = new Color(0f, 0f, 0f, 0.6f);
        private static readonly Color PanelBg = new Color32(0x16, 0x25, 0x6E, 0xE6);
        private static readonly Color HeaderBg = new Color32(0x2A, 0x3A, 0xB5, 0xFF);
        private static readonly Color CardBg = new Color32(0x1B, 0x2A, 0x7A, 0xF2);
        private static readonly Color TextPrimary = Color.white;
        private static readonly Color TextSecondary = new Color32(0xA9, 0xC6, 0xF0, 0xFF);
        private static readonly Color Cyan = new Color32(0x5F, 0xD4, 0xFF, 0xFF);
        private static readonly Color Gold = new Color32(0xFF, 0xC9, 0x3C, 0xFF);
        private static readonly Color ButtonBg = new Color32(0x3B, 0x4C, 0xCB, 0xFF);
        private static readonly Color ButtonPrimary = new Color32(0x2F, 0xA8, 0x5A, 0xFF);

        [MenuItem("CircuitCrafter/Guidance/Build Guidance UI In Open Scene", false, 20)]
        public static void Build()
        {
            if (Object.FindAnyObjectByType<ObjectiveHud>(FindObjectsInactive.Include) != null ||
                GameObject.Find("GuidanceCanvas") != null)
            {
                EditorUtility.DisplayDialog("Guidance UI",
                    "This scene already has a GuidanceCanvas. Delete it first if you want to rebuild it.", "OK");
                return;
            }

            EnsureEventSystem();

            var canvasGo = new GameObject("GuidanceCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(canvasGo, UndoName);

            Canvas canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;

            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform root = canvasGo.GetComponent<RectTransform>();

            BuildObjectiveHud(root);
            BuildFeedbackToast(root);
            BuildTutorialCard(root);
            BuildMarker(root);
            BuildJobOrder(root);
            BuildReview(root);

            EditorSceneManager.MarkSceneDirty(canvasGo.scene);
            Selection.activeGameObject = canvasGo;

            Debug.Log("[GuidanceUIBuilder] Built the guidance UI. Next:\n" +
                      "  1. CircuitCrafter > Guidance > Create Guidance Settings (once per project)\n" +
                      "  2. CircuitCrafter > Guidance > Create Starter Content (once per project)\n" +
                      "  3. In a Story scene: CircuitCrafter > Guidance > Add Story Guide To Scene, then assign its Job Order\n" +
                      "  4. Drag the module's own assessment report panel into AssessmentReview > Hide While Open", canvasGo);
        }

        // ==================================================================
        // Current Task
        // ==================================================================

        private static void BuildObjectiveHud(RectTransform canvas)
        {
            RectTransform host = Host(canvas, "ObjectiveHud");
            ObjectiveHud hud = host.gameObject.AddComponent<ObjectiveHud>();

            RectTransform panel = NewRect("Panel", host);
            panel.anchorMin = panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 1f);
            panel.anchoredPosition = new Vector2(24f, -24f);
            panel.sizeDelta = new Vector2(540f, 150f);
            AddImage(panel, PanelBg);
            Column(panel, 18, 14, 6);
            Fit(panel);
            AddEdge(panel, Cyan);

            RectTransform header = NewRect("HeaderRow", panel);
            Row(header, 8);
            Fixed(header, 36f);
            TMP_Text headerText = AddText(header, "Header", "CURRENT TASK", 20f, Cyan, FontStyles.Bold);
            Flexible(headerText.rectTransform);
            Button jobButton = MakeButton(header, "JobOrderButton", "Job order", 150f, 32f, 18f, ButtonBg);
            Button collapse = MakeButton(header, "CollapseButton", "-", 40f, 32f, 22f, ButtonBg);

            TMP_Text title = AddText(panel, "Title", "Next task", 28f, TextPrimary, FontStyles.Bold, wrap: true);

            RectTransform detail = NewRect("Detail", panel);
            Column(detail, 0, 0, 4);
            TMP_Text instruction = AddText(detail, "Instruction", "How to do it.", 22f, TextSecondary, FontStyles.Normal, wrap: true);
            TMP_Text progress = AddText(detail, "Progress", "1 of 6", 20f, Gold, FontStyles.Bold);

            TMP_Text keyHint = AddText(panel, "KeyHint", "J  Job order", 17f, TextSecondary, FontStyles.Italic);

            hud.panel = panel.gameObject;
            hud.headerText = headerText;
            hud.titleText = title;
            hud.instructionText = instruction;
            hud.progressText = progress;
            hud.keyHintText = keyHint;
            hud.jobOrderButton = jobButton;
            hud.collapseButton = collapse;
            hud.detailRoot = detail.gameObject;
            EditorUtility.SetDirty(hud);
        }

        // ==================================================================
        // Feedback toast
        // ==================================================================

        private static void BuildFeedbackToast(RectTransform canvas)
        {
            RectTransform host = Host(canvas, "FeedbackToast");
            FeedbackToast toast = host.gameObject.AddComponent<FeedbackToast>();

            RectTransform panel = NewRect("Panel", host);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 1f);
            panel.pivot = new Vector2(0.5f, 1f);
            panel.anchoredPosition = new Vector2(0f, -110f);
            panel.sizeDelta = new Vector2(680f, 110f);
            AddImage(panel, CardBg);
            CanvasGroup group = panel.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            var row = panel.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(0, 18, 0, 0);
            row.spacing = 16f;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;
            Fit(panel);

            RectTransform accent = NewRect("Accent", panel);
            Image accentImage = AddImage(accent, new Color32(0xFF, 0x8A, 0x6B, 0xFF));
            Width(accent, 8f);

            RectTransform content = NewRect("Content", panel);
            Column(content, 0, 12, 4);
            Flexible(content);

            TMP_Text title = AddText(content, "Title", "Not quite", 22f, Gold, FontStyles.Bold);
            TMP_Text body = AddText(content, "Body", "What happened.", 22f, TextPrimary, FontStyles.Normal, wrap: true);
            TMP_Text fix = AddText(content, "Fix", "How to fix: ...", 20f, Cyan, FontStyles.Normal, wrap: true);

            toast.panel = panel.gameObject;
            toast.titleText = title;
            toast.bodyText = body;
            toast.fixText = fix;
            toast.accent = accentImage;
            toast.canvasGroup = group;
            EditorUtility.SetDirty(toast);
        }

        // ==================================================================
        // Tutorial card
        // ==================================================================

        private static void BuildTutorialCard(RectTransform canvas)
        {
            RectTransform host = Host(canvas, "TutorialCard");
            TutorialCardPanel card = host.gameObject.AddComponent<TutorialCardPanel>();

            RectTransform panel = NewRect("Panel", host);
            panel.anchorMin = panel.anchorMax = new Vector2(1f, 0.5f);
            panel.pivot = new Vector2(1f, 0.5f);
            panel.anchoredPosition = new Vector2(-24f, 60f);
            panel.sizeDelta = new Vector2(540f, 400f);
            AddImage(panel, CardBg);   // not a raycast target: it must never eat a touch meant for the look area
            Column(panel, 22, 20, 10);
            Fit(panel);
            AddEdge(panel, Gold);

            TMP_Text counter = AddText(panel, "Counter", "1 of 3", 18f, TextSecondary, FontStyles.Normal);
            TMP_Text title = AddText(panel, "Title", "Tutorial", 30f, Gold, FontStyles.Bold, wrap: true);

            RectTransform imageRect = NewRect("Image", panel);
            Image image = imageRect.gameObject.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            Fixed(imageRect, 200f);

            TMP_Text body = AddText(panel, "Body", "- Explain the idea in two to four lines.", 23f, TextPrimary, FontStyles.Normal, wrap: true);
            TMP_Text tryIt = AddText(panel, "TryIt", "Now you try: ...", 22f, Cyan, FontStyles.Italic, wrap: true);

            RectTransform buttons = NewRect("Buttons", panel);
            Row(buttons, 10);
            Fixed(buttons, 48f);
            Button showMe = MakeButton(buttons, "ShowMe", "Show me", 130f, 46f, 20f, ButtonBg);
            Button skip = MakeButton(buttons, "Skip", "Skip tutorial", 150f, 46f, 20f, ButtonBg);
            RectTransform spacer = NewRect("Spacer", buttons);
            Flexible(spacer);
            Button gotIt = MakeButton(buttons, "GotIt", "Got it", 180f, 46f, 21f, ButtonPrimary);

            card.panel = panel.gameObject;
            card.titleText = title;
            card.bodyText = body;
            card.tryItText = tryIt;
            card.image = image;
            card.counterText = counter;
            card.gotItButton = gotIt;
            card.showMeButton = showMe;
            card.skipButton = skip;
            EditorUtility.SetDirty(card);
        }

        // ==================================================================
        // Highlight tooltip
        // ==================================================================

        private static void BuildMarker(RectTransform canvas)
        {
            RectTransform host = Host(canvas, "GuideMarker");
            GuideMarkerOverlay overlay = host.gameObject.AddComponent<GuideMarkerOverlay>();

            RectTransform marker = NewRect("Marker", host);
            marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
            marker.pivot = new Vector2(0.5f, 0f);
            marker.sizeDelta = new Vector2(380f, 52f);
            AddImage(marker, new Color32(0x12, 0x1D, 0x5E, 0xE6));
            AddEdge(marker, Gold, 3f);

            TMP_Text label = AddText(marker, "Label", "Highlighted part", 22f, Gold, FontStyles.Bold);
            Stretch(label.rectTransform, 8f);
            label.alignment = TextAlignmentOptions.Center;

            overlay.marker = marker;
            overlay.labelText = label;
            EditorUtility.SetDirty(overlay);
        }

        // ==================================================================
        // Job order
        // ==================================================================

        private static void BuildJobOrder(RectTransform canvas)
        {
            RectTransform host = Host(canvas, "JobOrder");
            JobOrderPanel job = host.gameObject.AddComponent<JobOrderPanel>();

            RectTransform panel = NewRect("Panel", host);
            Stretch(panel);
            AddImage(panel, PageDim, raycastTarget: true);

            RectTransform window = NewRect("Window", panel);
            Centre(window, 1040f, 780f);
            AddImage(window, PanelBg, raycastTarget: true);
            AddEdge(window, Cyan);

            RectTransform header = NewRect("Header", window);
            TopStrip(header, 76f);
            AddImage(header, HeaderBg);
            TMP_Text headerText = AddText(header, "Title", "Job Order", 30f, TextPrimary, FontStyles.Bold);
            Stretch(headerText.rectTransform, 24f);

            ScrollRect scroll;
            TMP_Text body = BuildScrollText(window, 92f, 96f, 24f, out scroll);

            RectTransform footer = NewRect("Footer", window);
            BottomStrip(footer, 80f);
            Row(footer, 12, TextAnchor.MiddleRight, 24);
            Button close = MakeButton(footer, "CloseButton", "Close", 220f, 54f, 24f, ButtonPrimary);

            job.panel = panel.gameObject;
            job.headerText = headerText;
            job.bodyText = body;
            job.closeButton = close;
            job.scroll = scroll;
            EditorUtility.SetDirty(job);
        }

        // ==================================================================
        // Assessment review
        // ==================================================================

        private static void BuildReview(RectTransform canvas)
        {
            RectTransform host = Host(canvas, "AssessmentReview");
            host.SetAsLastSibling();
            AssessmentReviewPanel review = host.gameObject.AddComponent<AssessmentReviewPanel>();

            RectTransform panel = NewRect("Panel", host);
            Stretch(panel);
            AddImage(panel, new Color(0f, 0f, 0f, 0.75f), raycastTarget: true);

            RectTransform window = NewRect("Window", panel);
            Centre(window, 1400f, 960f);
            AddImage(window, PanelBg, raycastTarget: true);
            AddEdge(window, Gold);

            RectTransform header = NewRect("Header", window);
            TopStrip(header, 72f);
            AddImage(header, HeaderBg);
            TMP_Text headerText = AddText(header, "Title", "Assessment - Results and Review", 30f, TextPrimary, FontStyles.Bold);
            Stretch(headerText.rectTransform, 24f);

            RectTransform summaryRect = NewRect("Summary", window);
            summaryRect.anchorMin = new Vector2(0f, 1f);
            summaryRect.anchorMax = new Vector2(1f, 1f);
            summaryRect.pivot = new Vector2(0.5f, 1f);
            summaryRect.offsetMin = new Vector2(28f, -172f);
            summaryRect.offsetMax = new Vector2(-28f, -80f);
            TextMeshProUGUI summary = summaryRect.gameObject.AddComponent<TextMeshProUGUI>();
            summary.text = "0 / 0 (0%)   Not Yet Competent";
            summary.fontSize = 34f;
            summary.color = TextPrimary;
            summary.richText = true;
            summary.enableWordWrapping = true;
            summary.alignment = TextAlignmentOptions.MidlineLeft;
            summary.raycastTarget = false;
            if (TMP_Settings.defaultFontAsset != null) summary.font = TMP_Settings.defaultFontAsset;

            ScrollRect scroll;
            TMP_Text body = BuildScrollText(window, 180f, 184f, 22f, out scroll);

            // Activity buttons above the footer.
            RectTransform activities = NewRect("Activities", window);
            activities.anchorMin = new Vector2(0f, 0f);
            activities.anchorMax = new Vector2(1f, 0f);
            activities.pivot = new Vector2(0.5f, 0f);
            activities.offsetMin = new Vector2(24f, 92f);
            activities.offsetMax = new Vector2(-24f, 170f);
            Row(activities, 12, TextAnchor.MiddleLeft);
            Button template = MakeButton(activities, "ActivityButtonTemplate", "Practise: ...", 420f, 64f, 20f, ButtonBg);
            TMP_Text templateLabel = template.GetComponentInChildren<TMP_Text>();
            if (templateLabel != null) { templateLabel.enableWordWrapping = true; templateLabel.overflowMode = TextOverflowModes.Ellipsis; }
            template.gameObject.SetActive(false);

            RectTransform footer = NewRect("Footer", window);
            BottomStrip(footer, 84f);
            Row(footer, 14, TextAnchor.MiddleRight, 24);
            Button close = MakeButton(footer, "CloseButton", "Close", 200f, 56f, 24f, ButtonBg);
            Button menu = MakeButton(footer, "MenuButton", "Back to Main Menu", 300f, 56f, 24f, ButtonPrimary);

            review.panel = panel.gameObject;
            review.headerText = headerText;
            review.summaryText = summary;
            review.bodyText = body;
            review.scroll = scroll;
            review.activityButtonTemplate = template;
            review.activityContainer = activities;
            review.closeButton = close;
            review.menuButton = menu;
            EditorUtility.SetDirty(review);
        }

        // ==================================================================
        // Pieces
        // ==================================================================

        /// <summary>A scroll view filling the window between a top and a bottom inset, holding one
        /// rich-text block that grows to fit.</summary>
        private static TMP_Text BuildScrollText(RectTransform window, float top, float bottom, float fontSize, out ScrollRect scroll)
        {
            RectTransform view = NewRect("Scroll", window);
            view.anchorMin = Vector2.zero;
            view.anchorMax = Vector2.one;
            view.offsetMin = new Vector2(24f, bottom);
            view.offsetMax = new Vector2(-24f, -top);

            scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;

            RectTransform viewport = NewRect("Viewport", view);
            Stretch(viewport);
            AddImage(viewport, new Color(1f, 1f, 1f, 0.004f), raycastTarget: true);   // a ScrollRect only drags over a raycast target
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = NewRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, 400f);
            Column(content, 4, 8, 0);
            Fit(content);

            TMP_Text body = AddText(content, "Body", "", fontSize, TextPrimary, FontStyles.Normal, wrap: true);
            body.richText = true;

            scroll.viewport = viewport;
            scroll.content = content;
            return body;
        }

        private static RectTransform Host(RectTransform canvas, string name)
        {
            RectTransform host = NewRect(name, canvas);
            Stretch(host);
            return host;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, UndoName);
            var rect = go.GetComponent<RectTransform>();
            if (parent != null) rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        private static Image AddImage(RectTransform rect, Color color, bool raycastTarget = false)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycastTarget;
            return image;
        }

        private static TMP_Text AddText(RectTransform parent, string name, string value, float size, Color color,
                                        FontStyles style, bool wrap = false)
        {
            RectTransform rect = NewRect(name, parent);
            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.richText = true;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.raycastTarget = false;
            text.enableWordWrapping = wrap;
            text.overflowMode = wrap ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
            if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;

            // Wrapped text sizes itself through the parent's layout group; single lines get a fixed height.
            if (!wrap) Fixed(rect, size * 1.35f);
            return text;
        }

        private static Button MakeButton(RectTransform parent, string name, string label, float width, float height,
                                         float fontSize, Color color)
        {
            RectTransform rect = NewRect(name, parent);
            rect.sizeDelta = new Vector2(width, height);
            Width(rect, width);
            Fixed(rect, height);

            Image bg = rect.gameObject.AddComponent<Image>();
            bg.color = color;   // buttons must be hit, so raycastTarget stays on

            TMP_Text text = AddText(rect, "Label", label, fontSize, TextPrimary, FontStyles.Bold);
            Stretch(text.rectTransform, 6f);
            text.alignment = TextAlignmentOptions.Center;
            LayoutElement le = text.GetComponent<LayoutElement>();
            if (le != null) le.ignoreLayout = true;

            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            button.colors = colors;
            return button;
        }

        private static void AddEdge(RectTransform plate, Color color, float height = 4f)
        {
            RectTransform edge = NewRect("EdgeLight", plate);
            edge.anchorMin = new Vector2(0f, 1f);
            edge.anchorMax = new Vector2(1f, 1f);
            edge.pivot = new Vector2(0.5f, 1f);
            edge.offsetMin = new Vector2(0f, -height);
            edge.offsetMax = Vector2.zero;
            AddImage(edge, color);
            edge.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        }

        private static void Column(RectTransform rect, int padH, int padV, float spacing)
        {
            var col = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            col.padding = new RectOffset(padH, padH, padV, padV);
            col.spacing = spacing;
            col.childControlWidth = true;
            col.childControlHeight = true;
            col.childForceExpandWidth = true;
            col.childForceExpandHeight = false;
        }

        private static void Row(RectTransform rect, float spacing, TextAnchor align = TextAnchor.MiddleLeft, int padH = 0)
        {
            var row = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(padH, padH, 0, 0);
            row.spacing = spacing;
            row.childAlignment = align;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = false;
        }

        private static void Fit(RectTransform rect)
        {
            var fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        }

        private static void Fixed(RectTransform rect, float height)
        {
            LayoutElement le = rect.GetComponent<LayoutElement>();
            if (le == null) le = rect.gameObject.AddComponent<LayoutElement>();
            le.minHeight = height;
            le.preferredHeight = height;
        }

        private static void Width(RectTransform rect, float width)
        {
            LayoutElement le = rect.GetComponent<LayoutElement>();
            if (le == null) le = rect.gameObject.AddComponent<LayoutElement>();
            le.minWidth = width;
            le.preferredWidth = width;
        }

        private static void Flexible(RectTransform rect)
        {
            LayoutElement le = rect.GetComponent<LayoutElement>();
            if (le == null) le = rect.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
        }

        private static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        private static void Centre(RectTransform rect, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void TopStrip(RectTransform rect, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(0f, -height);
            rect.offsetMax = Vector2.zero;
        }

        private static void BottomStrip(RectTransform rect, float height)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = new Vector2(0f, height);
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(go, UndoName);
            Debug.Log("[GuidanceUIBuilder] No EventSystem in this scene - created one. Without it no button works.");
        }
    }
}
