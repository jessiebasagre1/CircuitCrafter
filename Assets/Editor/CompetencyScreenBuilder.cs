#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using CircuitCrafter.Assessment.UI;

namespace CircuitCrafter.Assessment.EditorTools
{
    /// <summary>
    /// Builds the whole competency screen - header, four COC badges, the scrolling outcome list,
    /// the empty state and the footer - and wires every reference on it.
    ///
    /// RUN IT FROM: CircuitCrafter > Build Competency Screen.
    ///
    /// WHY A BUILDER AND NOT A PREFAB
    /// This screen has roughly sixty objects and thirty Inspector references across four
    /// components, and every one of those references is a field that fails silently when it is
    /// left empty - a badge with no Status Text simply never shows a number. Assembling it by hand
    /// is an hour of work and a long tail of "why is that blank". Assembling it in code takes a
    /// second and cannot forget a field.
    ///
    /// WHAT IT DOES NOT DO
    /// It brings no art. Every Image it creates is a flat colour with its Sprite left empty, sized
    /// and positioned and ready for you to drop your own sprite in - see the Console summary after
    /// a build for the exact list. A screen built from this is complete and readable on the day it
    /// is built; it just looks like a wireframe until you dress it.
    ///
    /// IT NEVER TOUCHES WHAT IS ALREADY THERE. A new panel is created alongside any existing one,
    /// switched off, so you can compare the two and delete the old one in your own time.
    /// </summary>
    public static class CompetencyScreenBuilder
    {
        private const string PrefabFolder = "Assets/Assessment/UI/Generated";

        // ---- Palette ------------------------------------------------------
        //
        // Matched to the CircuitCrafter main menu: deep royal-blue plates, a cyan edge light along
        // the top of each one, white text, and GOLD for anything earned - the same gold as the
        // Achievements card and its trophy. A competent badge reading gold rather than green is
        // what makes this screen look like it belongs in the game rather than bolted onto it.
        //
        // The page tint is deliberately translucent so the classroom photograph carries through
        // exactly as it does behind the main menu. Every plate ON TOP of it is opaque, though, so
        // no text is ever asked to compete with a photo - white on the panel blue is about 11:1
        // and the secondary periwinkle about 6:1, both past WCAG AA. This gets read off a
        // projector, and anything less is unreadable from the back row.

        private static readonly Color PageTint = new Color32(0x16, 0x25, 0x6E, 0xCC);
        private static readonly Color HeaderBg = new Color32(0x2A, 0x3A, 0xB5, 0xFF);
        private static readonly Color CardBg = new Color32(0x35, 0x47, 0xC4, 0xFF);
        private static readonly Color RailBg = new Color32(0x1B, 0x2A, 0x7A, 0xE6);
        private static readonly Color TextPrimary = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        private static readonly Color TextSecondary = new Color32(0xA9, 0xC6, 0xF0, 0xFF);
        private static readonly Color EdgeCyan = new Color32(0x5F, 0xD4, 0xFF, 0xFF);
        private static readonly Color Gold = new Color32(0xFF, 0xC9, 0x3C, 0xFF);
        private static readonly Color GoldInk = new Color32(0x3A, 0x26, 0x00, 0xFF);
        private static readonly Color AccentBar = new Color32(0x46, 0xC8, 0xF5, 0xFF);

        /// <summary>
        /// The YELLOW story fill, drawn behind the blue assessment fill.
        ///
        /// Told apart from the blue by LIGHTNESS as much as by hue - a warm bright yellow against a
        /// mid cyan-blue - because roughly one male student in twelve has some colour vision
        /// deficiency, and two fills of similar lightness are one indistinct bar to them. The
        /// caption printing both numbers is the other half of that: colour is the quick read, not
        /// the only one.
        /// </summary>
        private static readonly Color StoryFill = new Color32(0xFF, 0xC9, 0x3C, 0xFF);

        private static readonly Color BarTrack = new Color32(0x12, 0x1D, 0x5E, 0xFF);
        private static readonly Color ButtonBg = new Color32(0x3B, 0x4C, 0xCB, 0xFF);

        // ---- Type scale ---------------------------------------------------
        //
        // Authored against a 1920x1080 reference. Nothing below 20 - a competency screen is read,
        // not glanced at, and 18px TMP at a 0.85 interface scale is 15px on screen.

        private const float SizeTitle = 36f;
        private const float SizeSection = 28f;
        private const float SizeBody = 24f;
        private const float SizeCaption = 21f;
        private const float SizeBadgeTitle = 26f;

        // ==================================================================

        [MenuItem("CircuitCrafter/Build Competency Screen", false, 40)]
        public static void Build()
        {
            Canvas canvas = FindOrCreateCanvas();
            if (canvas == null) return;

            GameObject screen = BuildScreen(canvas);

            Undo.RegisterCreatedObjectUndo(screen, "Build Competency Screen");
            Selection.activeGameObject = screen;
            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);

            Debug.Log(
                "[CompetencyScreenBuilder] Built '" + screen.name + "' under '" + canvas.name + "'.\n\n" +
                "IT IS SWITCHED OFF so it cannot cover your existing menu. Tick it to look at it.\n\n" +
                "NEXT:\n" +
                "  1. Wire YOUR menu button to MenuNavigator > Show with this panel in the slot. " +
                "The screen is already in MenuNavigator's Screens list and Back already works.\n" +
                "  2. Drop your sprites on: CompetencyScreen (page background), Header (header " +
                "plate), each Badge_COCn/Art/Frame (badge plate) and Badge_COCn/Art/Frame/Badge " +
                "(the badge artwork itself).\n" +
                "  3. Set each badge's Module Id if yours are not COC1-COC4.\n\n" +
                "Every reference on CompetencyProfileView and on the four CompetencyBadgeViews is " +
                "already wired. The two row prefabs were written to " + PrefabFolder + ".\n\n" +
                "TWO FILLS ON THE MODULE ROWS AND THE BADGES: BarStack/StoryBar is the YELLOW one " +
                "(outcomes practised or better) drawn behind the BLUE one (outcomes competent). The " +
                "front bar deliberately has no Background of its own; an opaque track would hide " +
                "the yellow completely.\n\n" +
                "THE OUTCOME ROWS HAVE ONE BAR, AND IT IS THE SCORE - bestScorePercent out of 100, " +
                "so it agrees with the 'Best 80%' beside it rather than measuring evidence.\n\n" +
                "THE OVERALL BAR IN THE HEADER IS BLUE ONLY, on purpose: the headline number is what " +
                "has been PROVEN, and a nearly-full bar at the top of the screen for a learner who " +
                "has proven nothing is the one misreading worth designing out. Header/Pad/BarLegend " +
                "is the key.",
                screen);
        }

        // ==================================================================

        private static Canvas FindOrCreateCanvas()
        {
            // Prefer whatever the user has selected, so building twice in a scene with several
            // canvases puts the second one where they were looking.
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

            var go = new GameObject("CompetencyCanvas",
                                    typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            Canvas made = go.GetComponent<Canvas>();
            made.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            Undo.RegisterCreatedObjectUndo(go, "Create Canvas");
            Debug.Log("[CompetencyScreenBuilder] No canvas in this scene, so one was created.", go);
            return made;
        }

        private static GameObject BuildScreen(Canvas canvas)
        {
            RectTransform screen = NewRect(UniqueName(canvas, "CompetencyScreen"), canvas.transform);
            Stretch(screen);
            AddImage(screen, PageTint);         // <- page background sprite goes here

            // One column: header, badges, list, footer. A layout group rather than hand-placed
            // rects so the list can take whatever height is left on any aspect ratio.
            RectTransform column = NewRect("Column", screen);
            Stretch(column);
            VerticalLayoutGroup col = column.gameObject.AddComponent<VerticalLayoutGroup>();
            col.padding = new RectOffset(44, 44, 24, 24);
            col.spacing = 14f;
            col.childControlWidth = true;
            col.childControlHeight = true;
            col.childForceExpandWidth = true;
            col.childForceExpandHeight = false;

            TMP_Text learnerText, overallText;
            Slider overallBar;
            BuildHeader(column, out learnerText, out overallText, out overallBar);

            BuildBadgeStrip(column);

            RectTransform content = BuildList(column);

            GameObject emptyPanel;
            TMP_Text emptyText;
            BuildEmptyState(column, out emptyPanel, out emptyText);

            Button backButton, refreshButton, exportButton;
            TMP_Text exportStatus;
            BuildFooter(column, out backButton, out refreshButton, out exportButton, out exportStatus);

            WireNavigation(screen.gameObject, backButton);

            // ---- the view itself -----------------------------------------
            CompetencyProfileView view = screen.gameObject.AddComponent<CompetencyProfileView>();

            CompetencyModuleHeaderView headerPrefab = BuildModuleHeaderPrefab();
            CompetencyOutcomeRowView rowPrefab = BuildOutcomeRowPrefab();

            // Built last and parented alongside Column, not inside it - Column's own
            // VerticalLayoutGroup only governs its direct children, so a sibling overlay sits on
            // top of the whole screen (later siblings draw in front) without being squeezed into
            // the header/badges/list/footer stack itself.
            CompetencyHistoryView historyView = BuildHistoryPopup(screen);

            // Built after the history popup, so it stacks ON TOP of it (later siblings draw in
            // front) - a row's View button opens this while the history popup is still open
            // behind it.
            CompetencyRunDetailView detailView = BuildRunDetailPopup(screen);
            var historySo = new SerializedObject(historyView);
            SetRef(historySo, "detailView", detailView);
            historySo.ApplyModifiedPropertiesWithoutUndo();

            var so = new SerializedObject(view);
            SetRef(so, "learnerText", learnerText);
            SetRef(so, "overallText", overallText);
            SetRef(so, "overallBar", overallBar);
            SetRef(so, "rowContainer", content);
            SetRef(so, "moduleHeaderPrefab", headerPrefab);
            SetRef(so, "outcomeRowPrefab", rowPrefab);
            SetRef(so, "emptyStatePanel", emptyPanel);
            SetRef(so, "emptyStateText", emptyText);
            SetRef(so, "exportButton", exportButton);
            SetRef(so, "exportStatusText", exportStatus);
            SetRef(so, "refreshButton", refreshButton);
            SetRef(so, "historyView", historyView);
            so.ApplyModifiedPropertiesWithoutUndo();

            // Off, so it cannot land on top of the menu the user is already looking at.
            screen.gameObject.SetActive(false);
            return screen.gameObject;
        }

        /// <summary>
        /// Registers the screen with MenuNavigator and makes Back work.
        ///
        /// Left to the reader this is two steps that are easy to half-do: a screen missing from the
        /// Screens list is one MenuNavigator will not hide when another opens, so it stays on top of
        /// whatever comes next, and a Back button with no listener is a button that looks finished
        /// and does nothing. Both are quiet failures, so the builder does them.
        /// </summary>
        private static void WireNavigation(GameObject screen, Button back)
        {
            MenuNavigator nav = Object.FindFirstObjectByType<MenuNavigator>(FindObjectsInactive.Include);

            if (nav == null)
            {
                Debug.LogWarning("[CompetencyScreenBuilder] No MenuNavigator in this scene, so the " +
                                 "screen could not be registered and Back is not wired. Do both by " +
                                 "hand, or build this in the Main Menu scene.", screen);
                return;
            }

            var so = new SerializedObject(nav);
            SerializedProperty screens = so.FindProperty("screens");

            if (screens != null && screens.isArray)
            {
                bool already = false;
                for (int i = 0; i < screens.arraySize; i++)
                {
                    if (screens.GetArrayElementAtIndex(i).objectReferenceValue == screen) already = true;
                }

                if (!already)
                {
                    screens.arraySize++;
                    screens.GetArrayElementAtIndex(screens.arraySize - 1).objectReferenceValue = screen;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(nav);
                }
            }

            if (back != null)
            {
                UnityEventTools.AddVoidPersistentListener(
                    back.onClick, new UnityEngine.Events.UnityAction(nav.Back));
                EditorUtility.SetDirty(back);
            }

            Debug.Log("[CompetencyScreenBuilder] Registered with MenuNavigator on '" + nav.name +
                      "' and wired Back to it.", nav);
        }

        // ==================================================================

        private static void BuildHeader(RectTransform parent, out TMP_Text learnerText,
                                        out TMP_Text overallText, out Slider overallBar)
        {
            RectTransform header = NewRect("Header", parent);
            AddImage(header, HeaderBg);         // <- header plate sprite goes here
            AddTopEdge(header, 5f);

            // 130, not 104: the legend row under the overall bar needs 22px plus its 6px gap.
            // Counted out, not eyeballed - 12 + 42 + 6 + 28 + 6 + 22 + 12.
            Fixed(header, 130f);

            RectTransform pad = NewRect("Pad", header);
            Stretch(pad);
            VerticalLayoutGroup v = pad.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(28, 28, 12, 12);
            v.spacing = 6f;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            // Title and learner share one line. The title is the least useful thing on this screen -
            // you already know which screen you opened - so it gives up its own row to the list.
            RectTransform top = NewRect("TitleRow", pad);
            Fixed(top, 42f);
            HorizontalLayoutGroup th = top.gameObject.AddComponent<HorizontalLayoutGroup>();
            th.spacing = 18f;
            th.childControlWidth = true;
            th.childControlHeight = true;
            th.childForceExpandWidth = false;
            th.childForceExpandHeight = true;
            th.childAlignment = TextAnchor.MiddleLeft;

            TMP_Text title = AddText(top, "Title", "COMPETENCY PROFILE", SizeTitle,
                                     TextPrimary, FontStyles.Bold);
            title.characterSpacing = 5f;
            Width(title.rectTransform, 520f);

            learnerText = AddText(top, "LearnerText", "No learner selected", SizeSection, TextSecondary);
            learnerText.alignment = TextAlignmentOptions.MidlineRight;
            LayoutElement lle = learnerText.gameObject.AddComponent<LayoutElement>();
            lle.flexibleWidth = 1f;

            RectTransform row = NewRect("OverallRow", pad);
            Fixed(row, 28f);
            HorizontalLayoutGroup h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 18f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;

            overallText = AddText(row, "OverallText", "0 of 0 outcomes competent", SizeBody, TextPrimary);
            Width(overallText.rectTransform, 400f);

            // ONE FILL ONLY UP HERE, and that is a deliberate difference from the bars below.
            //
            // This is the screen's headline number. A learner glancing at the top of the page should
            // read one thing - how much of the qualification they have PROVEN - and the yellow story
            // fill belongs where it can be compared against something: beside a module's own
            // outcomes, and on that module's badge. Put it here as well and the top of the screen
            // shows a bar that is nearly full for a learner who has proven nothing, which is the
            // single most flattering misreading this screen could offer.
            overallBar = MakeBar(row, "OverallBar", AccentBar);
            LayoutElement be = overallBar.GetComponent<LayoutElement>();
            be.flexibleWidth = 1f;

            // THE KEY, and it is not optional decoration. Two coloured fills with no legend is a
            // guessing game, and the wrong guess here - reading yellow as "done" - is the one
            // misreading this whole screen exists to prevent. Static text, so there is nothing to
            // wire and nothing that can come unwired.
            TMP_Text legend = AddText(pad, "BarLegend",
                                      "<color=#46C8F5>Blue</color>: proven in assessment    " +
                                      "<color=#FFC93C>Yellow</color>: worked through in story mode " +
                                      "(shown on each module and badge)",
                                      SizeCaption, TextSecondary);
            legend.richText = true;
            Fixed(legend.rectTransform, 22f);
        }

        private static void BuildBadgeStrip(RectTransform parent)
        {
            RectTransform strip = NewRect("BadgeStrip", parent);
            Fixed(strip, 116f);

            HorizontalLayoutGroup h = strip.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 14f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = true;
            h.childForceExpandHeight = true;

            for (int i = 1; i <= 4; i++) BuildBadge(strip, "COC" + i);
        }

        private static void BuildBadge(RectTransform parent, string moduleId)
        {
            RectTransform badge = NewRect("Badge_" + moduleId, parent);
            AddImage(badge, CardBg);
            AddTopEdge(badge);

            // Laid out SIDEWAYS - art on the left, text on the right - rather than stacked. Stacked
            // badges were 300px tall and pushed the outcome list down to a third of the screen,
            // which had the emphasis backwards: the badge is the reward, the list is the work.
            HorizontalLayoutGroup v = badge.gameObject.AddComponent<HorizontalLayoutGroup>();
            v.padding = new RectOffset(14, 14, 12, 12);
            v.spacing = 12f;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = false;
            v.childForceExpandHeight = true;
            v.childAlignment = TextAnchor.MiddleLeft;

            // ---- artwork --------------------------------------------------
            RectTransform art = NewRect("Art", badge);
            Width(art, 84f);

            RectTransform frameRect = NewRect("Frame", art);
            Centre(frameRect, 84f, 84f);
            Image frame = AddImage(frameRect, new Color32(0x23, 0x33, 0x96, 0xFF));  // <- plate sprite

            RectTransform badgeRect = NewRect("Badge", frameRect);
            Centre(badgeRect, 66f, 66f);
            Image badgeImage = AddImage(badgeRect, new Color32(0x55, 0x68, 0xE2, 0xFF)); // <- YOUR ART

            // The padlock is text rather than a sprite on purpose: it has to work on the day the
            // screen is built, before any art exists, and a missing sprite would leave the locked
            // state indistinguishable from the unlocked one.
            RectTransform lockRect = NewRect("LockOverlay", art);
            Centre(lockRect, 84f, 26f);
            lockRect.anchoredPosition = new Vector2(0f, -29f);
            AddImage(lockRect, new Color32(0x12, 0x1C, 0x55, 0xE6));

            TMP_Text lockLabel = AddText(lockRect, "LockLabel", "LOCKED", 15f,
                                         TextPrimary, FontStyles.Bold);
            Stretch(lockLabel.rectTransform);
            lockLabel.alignment = TextAlignmentOptions.Center;
            lockLabel.characterSpacing = 3f;

            // ---- text -----------------------------------------------------
            RectTransform textCol = NewRect("Text", badge);
            LayoutElement tcl = textCol.gameObject.AddComponent<LayoutElement>();
            tcl.flexibleWidth = 1f;

            VerticalLayoutGroup tv = textCol.gameObject.AddComponent<VerticalLayoutGroup>();
            tv.spacing = 2f;
            tv.childControlWidth = true;
            tv.childControlHeight = true;
            tv.childForceExpandWidth = true;
            tv.childForceExpandHeight = false;
            tv.childAlignment = TextAnchor.MiddleLeft;

            TMP_Text title = AddText(textCol, "Title", ReadableName(moduleId), SizeBadgeTitle,
                                     TextPrimary, FontStyles.Bold);

            TMP_Text status = AddText(textCol, "Status", "0 of 0 outcomes", SizeCaption, TextSecondary);

            Slider bar, storyBar;
            MakeBarPair(textCol, out storyBar, out bar);

            // The unit title lives on the module header row in the list below, where there is room
            // for it. Kept as an empty object so CompetencyBadgeView's Subtitle reference is still
            // satisfied and you can switch it back on if you want it here instead.
            TMP_Text subtitle = AddText(textCol, "Subtitle", "", 1f, TextSecondary);
            subtitle.gameObject.SetActive(false);

            // ---- wiring ---------------------------------------------------
            CompetencyBadgeView view = badge.gameObject.AddComponent<CompetencyBadgeView>();
            view.moduleId = moduleId;

            var so = new SerializedObject(view);
            SetRef(so, "badgeImage", badgeImage);
            SetRef(so, "frameImage", frame);
            SetRef(so, "lockOverlay", lockRect.gameObject);
            SetRef(so, "titleText", title);
            SetRef(so, "subtitleText", subtitle);
            SetRef(so, "statusText", status);
            SetRef(so, "progressBar", bar);
            SetRef(so, "storyBar", storyBar);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static RectTransform BuildList(RectTransform parent)
        {
            RectTransform area = NewRect("ListArea", parent);
            AddImage(area, RailBg);

            LayoutElement le = area.gameObject.AddComponent<LayoutElement>();
            le.flexibleHeight = 1f;
            le.minHeight = 420f;

            RectTransform viewport = NewRect("Viewport", area);
            Stretch(viewport, 10f);

            // raycastTarget TRUE, and that is the whole reason the list scrolls. Nearly invisible
            // (alpha 0.004) but present: a ScrollRect only receives drag and wheel events while the
            // pointer is over a graphic that answers raycasts, and alpha plays no part in whether a
            // graphic answers one.
            AddImage(viewport, new Color(1f, 1f, 1f, 0.004f), raycastTarget: true);
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = NewRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = new Vector2(0f, 0f);
            content.offsetMax = new Vector2(0f, 0f);

            VerticalLayoutGroup v = content.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(14, 30, 14, 14);
            v.spacing = 8f;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = area.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36f;

            // A visible bar as well as the drag. Dragging alone is invisible affordance - a learner
            // looking at a list that runs off the bottom has no way to know it moves, and on a
            // classroom PC with a mouse there is no inertia flick to discover it by.
            scroll.verticalScrollbar = BuildScrollbar(area);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            return content;
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

        private static void BuildEmptyState(RectTransform parent, out GameObject panel, out TMP_Text text)
        {
            RectTransform rect = NewRect("EmptyState", parent);
            AddImage(rect, CardBg);
            Fixed(rect, 150f);

            text = AddText(rect, "EmptyStateText", "", SizeBody, TextSecondary);
            Stretch(text.rectTransform, 28f);
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = true;

            panel = rect.gameObject;
            panel.SetActive(false);
        }

        private static void BuildFooter(RectTransform parent, out Button back, out Button refresh,
                                        out Button export, out TMP_Text exportStatus)
        {
            RectTransform footer = NewRect("Footer", parent);
            Fixed(footer, 56f);

            HorizontalLayoutGroup h = footer.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 14f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;

            back = MakeButton(footer, "BackButton", "Back", 200f);
            refresh = MakeButton(footer, "RefreshButton", "Refresh", 200f);
            export = MakeButton(footer, "ExportButton", "Export CSV", 240f);

            exportStatus = AddText(footer, "ExportStatusText", "", SizeCaption, TextSecondary);
            exportStatus.enableWordWrapping = false;
            exportStatus.overflowMode = TextOverflowModes.Ellipsis;
            LayoutElement le = exportStatus.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
        }

        // ==================================================================
        // Row prefabs
        // ==================================================================

        private static CompetencyModuleHeaderView BuildModuleHeaderPrefab()
        {
            RectTransform root = NewRect("CompetencyModuleHeaderRow", null);
            AddImage(root, new Color32(0x3B, 0x4C, 0xC6, 0xFF));
            AddTopEdge(root, 3f);
            Fixed(root, 72f);

            RectTransform pad = NewRect("Pad", root);
            Stretch(pad);
            HorizontalLayoutGroup h = pad.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(18, 18, 10, 10);
            h.spacing = 16f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;

            TMP_Text id = AddText(pad, "ModuleId", "COC1", SizeSection, TextPrimary, FontStyles.Bold);
            Width(id.rectTransform, 110f);

            TMP_Text unitTitle = AddText(pad, "ModuleTitle", "Unit title", SizeBody, TextPrimary);
            LayoutElement tle = unitTitle.gameObject.AddComponent<LayoutElement>();
            tle.flexibleWidth = 1f;

            // 168, not 130: "Best: 100%" in bold was clipping to "Best: 10..." - the one string
            // this label has to fit is also its longest, and an ellipsis on a score reads as a
            // different number rather than as truncation.
            TMP_Text best = AddText(pad, "BestScore", "Best: -", SizeBody, Gold, FontStyles.Bold);
            Width(best.rectTransform, 168f);
            best.alignment = TextAlignmentOptions.MidlineRight;

            TMP_Text summary = AddText(pad, "Summary", "0 of 0 competent", SizeBody, TextSecondary);
            Width(summary.rectTransform, 230f);
            summary.alignment = TextAlignmentOptions.MidlineRight;

            Slider bar, storyBar;
            RectTransform barStack = MakeBarPair(pad, out storyBar, out bar);
            Width(barStack, 220f);

            Button history = MakeButton(pad, "HistoryButton", "History", 130f);

            RectTransform done = NewRect("CompleteBadge", pad);
            Width(done, 130f);
            AddImage(done, Gold);
            TMP_Text doneText = AddText(done, "Label", "COMPETENT", 18f, GoldInk, FontStyles.Bold);
            Stretch(doneText.rectTransform);
            doneText.alignment = TextAlignmentOptions.Center;
            done.gameObject.SetActive(false);

            CompetencyModuleHeaderView view = root.gameObject.AddComponent<CompetencyModuleHeaderView>();
            var so = new SerializedObject(view);
            SetRef(so, "moduleIdText", id);
            SetRef(so, "moduleTitleText", unitTitle);
            SetRef(so, "summaryText", summary);
            SetRef(so, "progressBar", bar);
            SetRef(so, "storyBar", storyBar);
            SetRef(so, "completeBadge", done.gameObject);
            SetRef(so, "bestScoreText", best);
            SetRef(so, "historyButton", history);
            so.ApplyModifiedPropertiesWithoutUndo();

            return SaveAsPrefab(root.gameObject).GetComponent<CompetencyModuleHeaderView>();
        }

        private static CompetencyOutcomeRowView BuildOutcomeRowPrefab()
        {
            RectTransform root = NewRect("CompetencyOutcomeRow", null);
            AddImage(root, new Color32(0x2B, 0x3A, 0xA8, 0xFF));
            Fixed(root, 58f);

            RectTransform pad = NewRect("Pad", root);
            Stretch(pad);
            HorizontalLayoutGroup h = pad.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(18, 18, 8, 8);
            h.spacing = 14f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;

            RectTransform pill = NewRect("StatusPill", pad);
            Width(pill, 14f);
            Image pillImage = AddImage(pill, TextSecondary);

            TMP_Text code = AddText(pad, "Code", "LO1", SizeBody, TextSecondary, FontStyles.Bold);
            Width(code.rectTransform, 80f);

            TMP_Text title = AddText(pad, "Title", "Outcome title", SizeBody, TextPrimary);
            LayoutElement tle = title.gameObject.AddComponent<LayoutElement>();
            tle.flexibleWidth = 1f;

            TMP_Text status = AddText(pad, "Status", "Not attempted", SizeCaption, TextSecondary);
            Width(status.rectTransform, 170f);

            // BEFORE the bar, not after it. CompetencyOutcomeRowView writes a whole phrase here -
            // "2 of 2 evidenced" - not the bare "2/2" this column was sized for, and a right-aligned
            // label overflows LEFTWARD, which put it on top of the bar. Ordering it ahead of the bar
            // means even a longer phrase has nothing but empty padding to grow into.
            TMP_Text evidence = AddText(pad, "Evidence", "0 of 0 evidenced", SizeCaption, TextSecondary);
            Width(evidence.rectTransform, 210f);
            evidence.alignment = TextAlignmentOptions.MidlineRight;

            // ONE FILL, and it is the SCORE - CompetencyOutcomeRowView draws bestScorePercent out
            // of 100 here so the bar agrees with the "Best 80%" printed beside it. No yellow
            // counterpart exists because a story run is never marked: there is no story score to
            // draw behind an assessment one. The story/assessment pair lives on the module row and
            // the badge, where it compares two counts of the same thing.
            Slider bar = MakeBar(pad, "ScoreBar", AccentBar);
            Width(bar.GetComponent<RectTransform>(), 150f);

            TMP_Text best = AddText(pad, "BestScore", "", SizeCaption, TextSecondary);
            Width(best.rectTransform, 130f);
            best.alignment = TextAlignmentOptions.MidlineRight;

            CompetencyOutcomeRowView view = root.gameObject.AddComponent<CompetencyOutcomeRowView>();
            var so = new SerializedObject(view);
            SetRef(so, "codeText", code);
            SetRef(so, "titleText", title);
            SetRef(so, "evidenceText", evidence);
            SetRef(so, "statusText", status);
            SetRef(so, "bestScoreText", best);
            SetRef(so, "statusPill", pillImage);
            SetRef(so, "scoreBar", bar);
            so.ApplyModifiedPropertiesWithoutUndo();

            return SaveAsPrefab(root.gameObject).GetComponent<CompetencyOutcomeRowView>();
        }

        // ==================================================================
        // History popup
        // ==================================================================

        /// <summary>
        /// The "History" popup: a dim full-screen scrim behind a centred card, with a title,
        /// the module's Assessment high score, a count line and a scrolling list of past runs.
        /// Starts switched off; CompetencyHistoryView.Open() turns it on.
        /// </summary>
        private static CompetencyHistoryView BuildHistoryPopup(RectTransform screen)
        {
            RectTransform popup = NewRect("HistoryPopup", screen);
            Stretch(popup);

            // raycastTarget TRUE and no Button on it - this is what stops a click reaching the
            // competency list behind the popup while it's open, without needing a "click outside
            // to close" handler that could just as easily fire from a click on a label INSIDE the
            // card (most of this card's own text has raycastTarget off, so a click on it would
            // otherwise fall through to whatever sits behind - the Close button is the one and
            // only way out on purpose).
            AddImage(popup, new Color(0f, 0f, 0f, 0.55f), raycastTarget: true);

            RectTransform card = NewRect("Card", popup);
            Centre(card, 1100f, 760f);
            AddImage(card, CardBg);
            AddTopEdge(card, 5f);

            RectTransform pad = NewRect("Pad", card);
            Stretch(pad, 24f);
            VerticalLayoutGroup v = pad.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 12f;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            // ---- title row --------------------------------------------------
            RectTransform titleRow = NewRect("TitleRow", pad);
            Fixed(titleRow, 46f);
            HorizontalLayoutGroup th = titleRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            th.spacing = 14f;
            th.childControlWidth = true;
            th.childControlHeight = true;
            th.childForceExpandWidth = false;
            th.childForceExpandHeight = true;
            th.childAlignment = TextAnchor.MiddleLeft;

            TMP_Text title = AddText(titleRow, "Title", "Module - History", SizeSection,
                                     TextPrimary, FontStyles.Bold);
            LayoutElement titleLe = title.gameObject.AddComponent<LayoutElement>();
            titleLe.flexibleWidth = 1f;

            Button clearHistory = MakeButton(titleRow, "ClearHistoryButton", "Clear History", 210f);
            Button close = MakeButton(titleRow, "CloseButton", "Close", 160f);

            // ---- clear-history confirm strip -------------------------------
            // Hidden until Clear History is pressed. A dark red-tinted plate rather than the
            // card's usual blue is the only cue this needs - it should read as "careful" at a
            // glance, before anyone reads the words on it.
            RectTransform confirmRow = NewRect("ClearConfirm", pad);
            Fixed(confirmRow, 58f);
            AddImage(confirmRow, new Color32(0x47, 0x1E, 0x1E, 0xFF));

            HorizontalLayoutGroup ch = confirmRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            ch.padding = new RectOffset(16, 16, 8, 8);
            ch.spacing = 14f;
            ch.childControlWidth = true;
            ch.childControlHeight = true;
            ch.childForceExpandWidth = false;
            ch.childForceExpandHeight = true;
            ch.childAlignment = TextAnchor.MiddleLeft;

            TMP_Text confirmLabel = AddText(confirmRow, "Label",
                "Clear this module's history? Competency status and high score are not affected. This cannot be undone.",
                SizeCaption, TextPrimary);
            LayoutElement confirmLabelLe = confirmLabel.gameObject.AddComponent<LayoutElement>();
            confirmLabelLe.flexibleWidth = 1f;
            confirmLabel.enableWordWrapping = true;

            Button confirmYes = MakeButton(confirmRow, "YesButton", "Yes, clear", 160f);
            Button confirmNo = MakeButton(confirmRow, "NoButton", "Cancel", 130f);

            confirmRow.gameObject.SetActive(false);

            // ---- best score + count -----------------------------------------
            TMP_Text best = AddText(pad, "BestScoreText", "Personal best: -", SizeBody, Gold,
                                    FontStyles.Bold);
            Fixed(best.rectTransform, 32f);

            TMP_Text count = AddText(pad, "CountText", "", SizeCaption, TextSecondary);
            Fixed(count.rectTransform, 26f);

            // ---- list ---------------------------------------------------------
            RectTransform listArea = NewRect("ListArea", pad);
            AddImage(listArea, RailBg);
            LayoutElement listLe = listArea.gameObject.AddComponent<LayoutElement>();
            listLe.flexibleHeight = 1f;
            listLe.minHeight = 380f;

            RectTransform viewport = NewRect("Viewport", listArea);
            Stretch(viewport, 10f);
            AddImage(viewport, new Color(1f, 1f, 1f, 0.004f), raycastTarget: true);
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = NewRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            VerticalLayoutGroup lv = content.gameObject.AddComponent<VerticalLayoutGroup>();
            lv.padding = new RectOffset(10, 24, 10, 10);
            lv.spacing = 6f;
            lv.childControlWidth = true;
            lv.childControlHeight = true;
            lv.childForceExpandWidth = true;
            lv.childForceExpandHeight = false;

            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = listArea.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36f;
            scroll.verticalScrollbar = BuildScrollbar(listArea);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            // ---- empty state --------------------------------------------------
            GameObject emptyPanel;
            TMP_Text emptyText;
            BuildEmptyState(pad, out emptyPanel, out emptyText);

            // ---- wiring ---------------------------------------------------------
            CompetencyHistoryRowView rowPrefab = BuildHistoryRowPrefab();

            CompetencyHistoryView view = popup.gameObject.AddComponent<CompetencyHistoryView>();
            var so = new SerializedObject(view);
            SetRef(so, "titleText", title);
            SetRef(so, "bestScoreText", best);
            SetRef(so, "countText", count);
            SetRef(so, "rowContainer", content);
            SetRef(so, "rowPrefab", rowPrefab);
            SetRef(so, "emptyStatePanel", emptyPanel);
            SetRef(so, "emptyStateText", emptyText);
            SetRef(so, "closeButton", close);
            SetRef(so, "clearHistoryButton", clearHistory);
            SetRef(so, "clearConfirmPanel", confirmRow.gameObject);
            SetRef(so, "clearConfirmYesButton", confirmYes);
            SetRef(so, "clearConfirmNoButton", confirmNo);
            so.ApplyModifiedPropertiesWithoutUndo();

            popup.gameObject.SetActive(false);
            return view;
        }

        private static CompetencyHistoryRowView BuildHistoryRowPrefab()
        {
            RectTransform root = NewRect("CompetencyHistoryRow", null);
            AddImage(root, new Color32(0x2B, 0x3A, 0xA8, 0xFF));
            Fixed(root, 64f);

            RectTransform pad = NewRect("Pad", root);
            Stretch(pad);
            HorizontalLayoutGroup h = pad.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(16, 16, 8, 8);
            h.spacing = 14f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;

            RectTransform pill = NewRect("ModePill", pad);
            Width(pill, 10f);
            Image pillImage = AddImage(pill, TextSecondary);

            TMP_Text date = AddText(pad, "Date", "Jan 1, 2026  00:00", SizeCaption, TextSecondary);
            Width(date.rectTransform, 220f);

            TMP_Text mode = AddText(pad, "Mode", "ASSESSMENT", SizeCaption, TextSecondary,
                                    FontStyles.Bold);
            Width(mode.rectTransform, 130f);

            TMP_Text score = AddText(pad, "Score", "0%  (0/0)", SizeBody, TextPrimary,
                                     FontStyles.Bold);
            Width(score.rectTransform, 170f);

            TMP_Text status = AddText(pad, "Status", "Completed", SizeCaption, TextSecondary);
            Width(status.rectTransform, 150f);

            TMP_Text detail = AddText(pad, "Detail", "0 mistakes", SizeCaption, TextSecondary);
            LayoutElement dle = detail.gameObject.AddComponent<LayoutElement>();
            dle.flexibleWidth = 1f;
            detail.alignment = TextAlignmentOptions.MidlineRight;

            Button viewButton = MakeButton(pad, "ViewButton", "View", 90f);

            CompetencyHistoryRowView view = root.gameObject.AddComponent<CompetencyHistoryRowView>();
            var so = new SerializedObject(view);
            SetRef(so, "dateText", date);
            SetRef(so, "modeText", mode);
            SetRef(so, "scoreText", score);
            SetRef(so, "statusText", status);
            SetRef(so, "detailText", detail);
            SetRef(so, "modePill", pillImage);
            SetRef(so, "viewButton", viewButton);
            so.ApplyModifiedPropertiesWithoutUndo();

            return SaveAsPrefab(root.gameObject).GetComponent<CompetencyHistoryRowView>();
        }

        /// <summary>
        /// The full-detail popup opened from a history row's View button: one scrollable
        /// rich-text report (stages, mistakes, learning outcomes) for a single run. Starts
        /// switched off; CompetencyRunDetailView.Open() turns it on.
        /// </summary>
        private static CompetencyRunDetailView BuildRunDetailPopup(RectTransform screen)
        {
            RectTransform popup = NewRect("RunDetailPopup", screen);
            Stretch(popup);
            AddImage(popup, new Color(0f, 0f, 0f, 0.65f), raycastTarget: true);  // see BuildHistoryPopup for why this has no Button on it

            RectTransform card = NewRect("Card", popup);
            Centre(card, 1100f, 780f);
            AddImage(card, CardBg);
            AddTopEdge(card, 5f);

            RectTransform pad = NewRect("Pad", card);
            Stretch(pad, 24f);
            VerticalLayoutGroup v = pad.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 12f;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            // ---- title row --------------------------------------------------
            RectTransform titleRow = NewRect("TitleRow", pad);
            Fixed(titleRow, 46f);
            HorizontalLayoutGroup th = titleRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            th.spacing = 14f;
            th.childControlWidth = true;
            th.childControlHeight = true;
            th.childForceExpandWidth = false;
            th.childForceExpandHeight = true;
            th.childAlignment = TextAnchor.MiddleLeft;

            TMP_Text title = AddText(titleRow, "Title", "Run detail", SizeSection,
                                     TextPrimary, FontStyles.Bold);
            LayoutElement titleLe = title.gameObject.AddComponent<LayoutElement>();
            titleLe.flexibleWidth = 1f;

            Button close = MakeButton(titleRow, "CloseButton", "Close", 160f);

            // ---- body -----------------------------------------------------
            RectTransform bodyArea = NewRect("BodyArea", pad);
            AddImage(bodyArea, RailBg);
            LayoutElement bodyAreaLe = bodyArea.gameObject.AddComponent<LayoutElement>();
            bodyAreaLe.flexibleHeight = 1f;
            bodyAreaLe.minHeight = 500f;

            RectTransform viewport = NewRect("Viewport", bodyArea);
            Stretch(viewport, 10f);
            AddImage(viewport, new Color(1f, 1f, 1f, 0.004f), raycastTarget: true);
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = NewRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            VerticalLayoutGroup lv = content.gameObject.AddComponent<VerticalLayoutGroup>();
            lv.padding = new RectOffset(16, 24, 12, 12);
            lv.childControlWidth = true;
            lv.childControlHeight = true;
            lv.childForceExpandWidth = true;
            lv.childForceExpandHeight = false;

            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            ScrollRect scroll = bodyArea.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 36f;
            scroll.verticalScrollbar = BuildScrollbar(bodyArea);
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            // One rich-text block, not a row prefab per stage/mistake - see the class comment on
            // CompetencyRunDetailView for why.
            TMP_Text body = AddText(content, "BodyText", "", SizeBody, TextPrimary);
            body.alignment = TextAlignmentOptions.TopLeft;
            body.enableWordWrapping = true;
            body.richText = true;
            body.overflowMode = TextOverflowModes.Overflow;  // the ScrollRect handles overflow, not mid-line clipping

            // AddText fixes every label's height to one line by default, which is right for
            // everything else it builds but wrong here - this block grows with however much a
            // given run recorded, so its height has to come from its own wrapped content rather
            // than a size fixed for one line.
            LayoutElement oneLineHeight = body.gameObject.GetComponent<LayoutElement>();
            if (oneLineHeight != null) Object.DestroyImmediate(oneLineHeight);

            // ---- wiring ---------------------------------------------------------
            CompetencyRunDetailView view = popup.gameObject.AddComponent<CompetencyRunDetailView>();
            var so = new SerializedObject(view);
            SetRef(so, "titleText", title);
            SetRef(so, "bodyText", body);
            SetRef(so, "closeButton", close);
            so.ApplyModifiedPropertiesWithoutUndo();

            popup.gameObject.SetActive(false);
            return view;
        }

        private static GameObject SaveAsPrefab(GameObject temp)
        {
            Directory.CreateDirectory(PrefabFolder);
            AssetDatabase.Refresh();

            string path = AssetDatabase.GenerateUniqueAssetPath(PrefabFolder + "/" + temp.name + ".prefab");
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(temp, path);

            Object.DestroyImmediate(temp);
            return asset;
        }

        // ==================================================================
        // Small builders
        // ==================================================================

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            if (parent != null) rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }


        /// <summary>
        /// The cyan light along the top edge of a plate.
        ///
        /// Every panel in CircuitCrafter's menu is lit along one edge, and it is most of what makes
        /// the style read as "tech" rather than "flat blue rectangle". Built from a plain Image so
        /// it is there before any art is, and so a sprite with its own edge light can simply switch
        /// it off rather than fight it.
        /// </summary>
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

        /// <summary>
        /// A flat plate, with its Sprite left empty for you to fill in.
        ///
        /// raycastTarget defaults OFF because almost everything here is furniture, and every extra
        /// raycast target is one more thing the EventSystem has to test on every pointer move. It
        /// must be turned ON for anything that has to RECEIVE input - buttons, and the scroll
        /// viewport. A ScrollRect drags only while the pointer is over a raycastable graphic, so a
        /// viewport image with this left off produces a list that cannot be scrolled at all, with
        /// nothing on screen or in the Console to say why.
        /// </summary>
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

            // Ellipsis, never Overflow. With Overflow a label whose text is wider than its layout
            // slot simply keeps drawing past both edges, straight over whatever is beside it -
            // which is how "2 of 2 evidenced" ended up printed across the middle of the progress
            // bar next to it. Ellipsis clips it to its own slot instead, so a too-narrow column
            // looks like a too-narrow column rather than like a broken one.
            text.overflowMode = TextOverflowModes.Ellipsis;

            if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;

            Fixed(rect, size * 1.45f);
            return text;
        }

        /// <summary>
        /// A non-interactive progress bar. Built as a Slider rather than two Images because
        /// CompetencyProfileView, CompetencyModuleHeaderView and CompetencyOutcomeRowView all expect
        /// a Slider - matching their fields matters more here than the handful of components saved.
        /// </summary>
        /// <summary>
        /// The two-fill bar: yellow story progress behind, blue proven progress in front, both in
        /// the same rect.
        ///
        /// THE FRONT BAR HAS NO TRACK, and that is the part that is easy to get wrong. A Slider's
        /// Background is an opaque image stretched across the whole width - put one on the front bar
        /// and it covers the yellow fill completely, so the overlay looks like it simply is not
        /// working. Only the back bar draws the track.
        ///
        /// THE CONTAINER EXISTS BECAUSE OF THE LAYOUT GROUPS. Both bars cannot be siblings in a
        /// column with a VerticalLayoutGroup - it would stack them one above the other. A container
        /// the layout group can see, holding two Stretch()ed bars it cannot, is what puts them on
        /// top of each other. Returned so the caller can size it the way that site needs: flexible
        /// width in the header row, a fixed 220 in the module header, full width in a badge.
        ///
        /// StoryBar is created FIRST so it draws BEHIND - in uGUI the later sibling draws in front.
        /// Practised always counts Competent too (see CompetencyLedger.GetModuleProgress), so the
        /// back fill can never be overtaken by the front one and the overlay cannot render nonsense.
        /// </summary>
        private static RectTransform MakeBarPair(RectTransform parent, out Slider story, out Slider proven,
                                                 string frontName = "ProgressBar")
        {
            RectTransform stack = NewRect("BarStack", parent);
            Fixed(stack, 14f);

            story = MakeBar(stack, "StoryBar", StoryFill);
            Stretch(story.GetComponent<RectTransform>());

            proven = MakeBar(stack, frontName, AccentBar, withTrack: false);
            Stretch(proven.GetComponent<RectTransform>());

            return stack;
        }

        private static Slider MakeBar(RectTransform parent, string name, Color fillColor,
                                      bool withTrack = true)
        {
            RectTransform rect = NewRect(name, parent);
            Fixed(rect, 14f);

            if (withTrack)
            {
                RectTransform bg = NewRect("Background", rect);
                Stretch(bg);
                AddImage(bg, BarTrack);
            }

            RectTransform fillArea = NewRect("Fill Area", rect);
            Stretch(fillArea);

            RectTransform fill = NewRect("Fill", fillArea);
            Stretch(fill);
            AddImage(fill, fillColor);

            Slider slider = rect.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.targetGraphic = null;
            slider.handleRect = null;
            slider.transition = Selectable.Transition.None;
            slider.interactable = false;
            slider.wholeNumbers = false;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 0f;

            rect.gameObject.AddComponent<LayoutElement>();
            return slider;
        }

        private static Button MakeButton(RectTransform parent, string name, string label, float width)
        {
            RectTransform rect = NewRect(name, parent);
            Width(rect, width);

            Image bg = rect.gameObject.AddComponent<Image>();
            bg.color = ButtonBg;                // buttons DO need to be hit, so raycastTarget stays on

            TMP_Text text = AddText(rect, "Label", label, SizeBody, TextPrimary);
            Stretch(text.rectTransform);
            text.alignment = TextAlignmentOptions.Center;

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

        private static void Centre(RectTransform rect, float width, float height)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(width, height);
        }

        /// <summary>Fixes a layout child's height. minHeight AND preferredHeight, because a layout
        /// group with Child Force Expand off reads preferred and one with it on reads min.</summary>
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

            if (property == null)
            {
                // A renamed field in one of the view scripts would otherwise leave a reference
                // quietly unset and the screen half-blank with nothing to explain it.
                Debug.LogWarning("[CompetencyScreenBuilder] '" + so.targetObject.GetType().Name +
                                 "' has no serialized field called '" + field + "' - that reference " +
                                 "could not be wired. Assign it by hand.");
                return;
            }

            property.objectReferenceValue = value;
        }

        private static string UniqueName(Canvas canvas, string wanted)
        {
            if (canvas.transform.Find(wanted) == null) return wanted;

            for (int i = 2; i < 100; i++)
            {
                string candidate = wanted + " " + i;
                if (canvas.transform.Find(candidate) == null) return candidate;
            }
            return wanted + " " + System.Guid.NewGuid().ToString("N").Substring(0, 4);
        }

        private static string ReadableName(string id)
        {
            return id.Length == 4 && id.StartsWith("COC") && char.IsDigit(id[3])
                ? "COC " + id[3]
                : id;
        }
    }
}
#endif