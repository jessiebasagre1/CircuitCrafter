using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using CircuitCrafter.Assessment;
using CircuitCrafter.Repair;

namespace CircuitCrafter.COC4.EditorTools
{
    /// <summary>
    /// Builds the COC4 assessment report screen and wires every field on COC4AssessmentReportView.
    ///
    /// RUN IT WITH: CircuitCrafter > Build COC4 Report Screen, with COC4-Results open.
    ///
    /// THE BODY IS A SCROLL VIEW, and that is the reason this is worth a builder rather than a
    /// paragraph of instructions. The report prints a line per ticket plus a verdict line under
    /// each, so five tickets is eleven lines before the header - and a label hand-sized against a
    /// three-ticket test clips the fifth, which is the one a learner most wants to read. A label
    /// that cannot outgrow its container cannot have that bug.
    ///
    /// It also checks the scene for a CompetencyLedger, because a results screen with no ledger
    /// builds the result, shows it, saves it - and never judges it. Everything looks right and the
    /// competency screen stays empty, which is the hardest kind of wrong to notice.
    ///
    /// Palette and helpers are a deliberate copy of CompetencyScreenBuilder's, for the same reason
    /// the other builders' are: a shared kit means restyling one screen quietly restyles the rest,
    /// and builders are throwaway tools meant to be deleted once the screen exists.
    /// </summary>
    public static class COC4ReportScreenBuilder
    {
        private static readonly Color PageTint = new Color32(0x16, 0x25, 0x6E, 0xCC);
        private static readonly Color HeaderBg = new Color32(0x2A, 0x3A, 0xB5, 0xFF);
        private static readonly Color CardBg = new Color32(0x35, 0x47, 0xC4, 0xFF);
        private static readonly Color RailBg = new Color32(0x1B, 0x2A, 0x7A, 0xE6);
        private static readonly Color TextPrimary = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        private static readonly Color TextSecondary = new Color32(0xA9, 0xC6, 0xF0, 0xFF);
        private static readonly Color EdgeCyan = new Color32(0x5F, 0xD4, 0xFF, 0xFF);
        private static readonly Color Gold = new Color32(0xFF, 0xC9, 0x3C, 0xFF);
        private static readonly Color AccentBar = new Color32(0x46, 0xC8, 0xF5, 0xFF);
        private static readonly Color BarTrack = new Color32(0x12, 0x1D, 0x5E, 0xFF);
        private static readonly Color ButtonBg = new Color32(0x3B, 0x4C, 0xCB, 0xFF);

        private const float SizeTitle = 36f;
        private const float SizeHeadline = 30f;
        private const float SizeBody = 24f;
        private const float SizeCaption = 21f;

        // ==================================================================

        [MenuItem("CircuitCrafter/Build COC4 Report Screen", false, 45)]
        public static void Build()
        {
            Canvas canvas = FindOrCreateCanvas();
            if (canvas == null) return;

            GameObject screen = BuildScreen(canvas);

            Undo.RegisterCreatedObjectUndo(screen, "Build COC4 Report Screen");
            Selection.activeGameObject = screen;
            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);

            var ledgers = Object.FindObjectsByType<CompetencyLedger>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            string ledgerLine = ledgers.Length > 0
                ? "A CompetencyLedger is in this scene, so results will be judged."
                : "WARNING: there is NO CompetencyLedger in this scene. The report will build, show " +
                  "and save a result, and nothing will ever judge it - the competency screen stays " +
                  "empty and nothing says why. Drag in your AssessmentManagers prefab.";

            Debug.Log(
                "[COC4ReportScreenBuilder] Built '" + screen.name + "' under '" + canvas.name + "'.\n\n" +
                ledgerLine + "\n\n" +
                "NEXT:\n" +
                "  1. Check Main Menu Scene Name on COC4AssessmentReportView matches your menu " +
                "scene exactly.\n" +
                "  2. Drop sprites on COC4ReportScreen (page background), Header and Headline if " +
                "you want them.\n" +
                "  3. Make sure this scene, every ticket scene and the main menu are all in Build " +
                "Settings.\n\n" +
                "The body is a scroll view, so a five-ticket report cannot clip. Every field on " +
                "COC4AssessmentReportView is already wired, and the Done button's OnClick is " +
                "deliberately left empty - the script adds that listener itself.",
                screen);
        }

        // ==================================================================

        private static Canvas FindOrCreateCanvas()
        {
            if (Selection.activeGameObject != null)
            {
                Canvas selected = Selection.activeGameObject.GetComponentInParent<Canvas>();
                if (selected != null) return selected.rootCanvas != null ? selected.rootCanvas : selected;
            }

            Canvas[] all = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include,
                                                            FindObjectsSortMode.None);
            foreach (Canvas c in all)
            {
                if (c != null && c.rootCanvas == c && c.renderMode != RenderMode.WorldSpace) return c;
            }

            var go = new GameObject("ReportCanvas",
                                    typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            Canvas made = go.GetComponent<Canvas>();
            made.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            Undo.RegisterCreatedObjectUndo(go, "Create Canvas");
            Debug.Log("[COC4ReportScreenBuilder] No canvas in this scene, so one was created.", go);
            return made;
        }

        // ==================================================================

        private static GameObject BuildScreen(Canvas canvas)
        {
            RectTransform screen = NewRect(UniqueName(canvas, "COC4ReportScreen"), canvas.transform);
            Stretch(screen);
            AddImage(screen, PageTint);                 // <- page background sprite goes here

            RectTransform column = NewRect("Column", screen);
            Stretch(column);
            column.offsetMin = new Vector2(0f, 0f);
            column.offsetMax = new Vector2(0f, 0f);

            VerticalLayoutGroup col = column.gameObject.AddComponent<VerticalLayoutGroup>();
            col.padding = new RectOffset(120, 120, 40, 40);
            col.spacing = 16f;
            col.childControlWidth = true;
            col.childControlHeight = true;
            col.childForceExpandWidth = true;
            col.childForceExpandHeight = false;

            BuildHeader(column);

            TMP_Text headline = BuildHeadline(column);
            TMP_Text body = BuildBody(column);
            Button done = BuildFooter(column);

            // ---- the view -------------------------------------------------
            var view = screen.gameObject.AddComponent<COC4AssessmentReportView>();

            var so = new SerializedObject(view);
            SetRef(so, "panelRoot", screen.gameObject);
            SetRef(so, "headlineText", headline);
            SetRef(so, "bodyText", body);
            SetRef(so, "doneButton", done);
            SetString(so, "mainMenuSceneName", "MainMenu");
            so.ApplyModifiedPropertiesWithoutUndo();

            return screen.gameObject;
        }

        // ==================================================================

        private static void BuildHeader(RectTransform parent)
        {
            RectTransform header = NewRect("Header", parent);
            Fixed(header, 104f);
            AddImage(header, HeaderBg);                 // <- header sprite goes here
            AddTopEdge(header);

            RectTransform inner = NewRect("Inner", header);
            Stretch(inner);
            inner.offsetMin = new Vector2(28f, 12f);
            inner.offsetMax = new Vector2(-28f, -12f);

            VerticalLayoutGroup v = inner.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 2f;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childAlignment = TextAnchor.MiddleLeft;

            AddText(inner, "Title", "Assessment Report", SizeTitle, TextPrimary, FontStyles.Bold);
            AddText(inner, "Caption", "Maintain and repair computer systems and networks",
                    SizeCaption, TextSecondary);
        }

        /// <summary>
        /// The one line that answers "how did I do", on its own plate.
        ///
        /// Separated from the body because it is the only thing most learners read, and because a
        /// score buried as the first line of eleven is a score nobody sees. Gold, to match the
        /// achievements card and the competency badges - earned things in this game are gold.
        /// </summary>
        private static TMP_Text BuildHeadline(RectTransform parent)
        {
            RectTransform plate = NewRect("Headline", parent);
            Fixed(plate, 96f);
            AddImage(plate, CardBg);
            AddTopEdge(plate);

            TMP_Text text = AddText(plate, "HeadlineText", "3 of 5 tickets resolved   -   340 / 500 (68%)",
                                    SizeHeadline, Gold, FontStyles.Bold);
            Stretch(text.rectTransform, 24f);
            text.alignment = TextAlignmentOptions.Center;
            Object.DestroyImmediate(text.GetComponent<LayoutElement>());

            return text;
        }

        /// <summary>
        /// The per-ticket breakdown, in a scroll view.
        ///
        /// A ContentSizeFitter on the content lets the label grow to whatever the report needs, and
        /// the ScrollRect lets the learner reach it. Together they are the difference between a
        /// report that is always complete and one that is complete until the day somebody runs a
        /// six-ticket assessment.
        /// </summary>
        private static TMP_Text BuildBody(RectTransform parent)
        {
            RectTransform area = NewRect("BodyArea", parent);
            AddImage(area, RailBg);

            LayoutElement le = area.gameObject.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.minHeight = 420f;

            RectTransform viewport = NewRect("Viewport", area);
            Stretch(viewport, 10f);

            // raycastTarget TRUE, and that is the whole reason this scrolls: a ScrollRect only
            // receives drag and wheel events while the pointer is over a graphic that answers
            // raycasts, and alpha plays no part in whether one does.
            AddImage(viewport, new Color(1f, 1f, 1f, 0.004f), raycastTarget: true);
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = NewRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            VerticalLayoutGroup v = content.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(26, 34, 22, 22);
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            TMP_Text body = AddText(content, "BodyText", SampleBody(), SizeBody, TextPrimary);
            body.alignment = TextAlignmentOptions.TopLeft;
            body.enableWordWrapping = true;
            body.overflowMode = TextOverflowModes.Overflow;

            // The fixed height AddText applies is removed on purpose. This is the one label on the
            // screen that must be free to grow - its height comes from the text, which is what the
            // ContentSizeFitter above measures.
            Object.DestroyImmediate(body.GetComponent<LayoutElement>());

            ScrollRect scroll = area.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36f;
            scroll.verticalScrollbar = BuildScrollbar(area);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            return body;
        }

        /// <summary>
        /// Placeholder text in the real shape of a report, so the screen can be judged in the Scene
        /// view before anyone plays an assessment. Overwritten the moment a run finishes.
        /// </summary>
        private static string SampleBody()
        {
            return
                "Time: 19:40\n" +
                "Mistakes: 1\n\n" +
                "TICKETS\n" +
                "  1. Ticket #003 - Computer will not start\n" +
                "       resolved   -   03:34\n" +
                "  2. Ticket #007 - No network access\n" +
                "       cause identified, but not repaired   -   05:12\n" +
                "  3. Ticket #001 - Random shutdowns\n" +
                "       resolved   -   04:08\n" +
                "  4. Ticket #005 - No display output\n" +
                "       gave up   -   02:46, 1 mistake(s)\n" +
                "  5. Ticket #009 - Will not boot from the disk\n" +
                "       resolved   -   04:00";
        }

        private static Scrollbar BuildScrollbar(RectTransform area)
        {
            RectTransform rect = NewRect("Scrollbar Vertical", area);
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.offsetMin = new Vector2(-22f, 10f);
            rect.offsetMax = new Vector2(-6f, -10f);

            AddImage(rect, BarTrack, raycastTarget: true);

            RectTransform slide = NewRect("Sliding Area", rect);
            Stretch(slide);

            RectTransform handle = NewRect("Handle", slide);
            Stretch(handle);
            Image handleImage = AddImage(handle, AccentBar, raycastTarget: true);

            Scrollbar bar = rect.gameObject.AddComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;
            bar.handleRect = handle;
            bar.targetGraphic = handleImage;
            return bar;
        }

        private static Button BuildFooter(RectTransform parent)
        {
            RectTransform footer = NewRect("Footer", parent);
            Fixed(footer, 60f);

            HorizontalLayoutGroup h = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childAlignment = TextAnchor.MiddleRight;

            return MakeButton(footer, "DoneButton", "Done", 260f);
        }

        // ==================================================================
        // Small builders - a deliberate copy of CompetencyScreenBuilder's
        // ==================================================================

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            if (parent != null) rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        private static void AddTopEdge(RectTransform plate, float height = 4f)
        {
            RectTransform edge = NewRect("EdgeLight", plate);
            edge.anchorMin = new Vector2(0f, 1f);
            edge.anchorMax = new Vector2(1f, 1f);
            edge.pivot = new Vector2(0.5f, 1f);
            edge.offsetMin = new Vector2(0f, -height);
            edge.offsetMax = Vector2.zero;
            AddImage(edge, EdgeCyan);
        }

        private static Image AddImage(RectTransform rect, Color color, bool raycastTarget = false)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycastTarget;
            return image;
        }

        private static TMP_Text AddText(RectTransform parent, string name, string value, float size,
                                        Color color, FontStyles style = FontStyles.Normal)
        {
            RectTransform rect = NewRect(name, parent);

            TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;

            if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;

            Fixed(rect, size * 1.45f);
            return text;
        }

        private static Button MakeButton(RectTransform parent, string name, string label, float width)
        {
            RectTransform rect = NewRect(name, parent);
            Width(rect, width);

            Image bg = rect.gameObject.AddComponent<Image>();
            bg.color = ButtonBg;                        // buttons must be hit, so raycastTarget stays on

            TMP_Text text = AddText(rect, "Label", label, SizeBody, TextPrimary);
            Stretch(text.rectTransform);
            text.alignment = TextAlignmentOptions.Center;
            Object.DestroyImmediate(text.GetComponent<LayoutElement>());

            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
            colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            button.colors = colors;

            return button;
        }

        // ==================================================================
        // Rect helpers
        // ==================================================================

        private static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
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

        // ==================================================================

        private static void SetRef(SerializedObject so, string field, Object value)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null) return;
            property.objectReferenceValue = value;
        }

        private static void SetString(SerializedObject so, string field, string value)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null) return;
            property.stringValue = value;
        }

        private static string UniqueName(Canvas canvas, string wanted)
        {
            if (canvas.transform.Find(wanted) == null) return wanted;

            for (int i = 2; i < 50; i++)
            {
                string candidate = wanted + " " + i;
                if (canvas.transform.Find(candidate) == null) return candidate;
            }
            return wanted;
        }
    }
}
