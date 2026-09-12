using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace CircuitCrafter.COC4.EditorTools
{
    /// <summary>
    /// Builds the COC4 Ticket Hub screen - the service desk the learner works from - and wires every
    /// reference on TicketHubView, TicketDetailPanelView and the ticket row prefab.
    ///
    /// RUN IT WITH: CircuitCrafter > Build COC4 Ticket Hub
    ///
    /// WHY LIST ON THE LEFT AND DETAIL ON THE RIGHT
    /// The alternative is a list that opens a modal over itself, and the code rules that out: the
    /// hub's whole job is comparing one work order against the others - which ticket is still Open,
    /// which was diagnosed and is waiting on a return visit - and a modal hides exactly that while
    /// the learner is deciding. TicketDetailPanelView already exists as a SEPARATE component with
    /// its own Show/Hide, which is the shape of a side panel, not of a dialog.
    ///
    /// THE DETAIL CARD IS NOT WHAT GETS HIDDEN. Panel Root is a child INSIDE the card, so
    /// TicketHubView's opening Hide() empties the card rather than deleting it from the layout. If
    /// the card itself were hidden, the list would jump sideways to fill the space the moment the
    /// screen opened and jump back the first time a ticket was picked. A permanent "Select a ticket"
    /// line sits underneath, so the empty half of the screen explains itself.
    ///
    /// PALETTE AND HELPERS ARE A DELIBERATE COPY of CompetencyScreenBuilder's, for the same reason
    /// ProfileScreenBuilder's are: three builders sharing one kit means editing the kit to suit one
    /// screen quietly restyles the other two, and builders are throwaway tools - they are meant to
    /// be deleted once the screen exists. If you ever do factor them together, do it after all three
    /// screens are final.
    /// </summary>
    public static class TicketHubBuilder
    {
        private const string PrefabFolder = "Assets/COC4/UI/Generated";

        // ==================================================================
        // Palette - identical to the competency and profile screens
        // ==================================================================

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
        private static readonly Color Resolved = new Color32(0x5A, 0xE0, 0xA8, 0xFF);

        private const float SizeTitle = 36f;
        private const float SizeSection = 28f;
        private const float SizeBody = 24f;
        private const float SizeCaption = 21f;

        private const float DetailWidth = 620f;
        private const float RowHeight = 92f;

        // ==================================================================

        [MenuItem("CircuitCrafter/Build COC4 Ticket Hub", false, 43)]
        public static void Build()
        {
            Canvas canvas = FindOrCreateCanvas();
            if (canvas == null) return;

            GameObject screen = BuildScreen(canvas);

            Undo.RegisterCreatedObjectUndo(screen, "Build COC4 Ticket Hub");
            Selection.activeGameObject = screen;
            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);

            Debug.Log(
                "[TicketHubBuilder] Built '" + screen.name + "' under '" + canvas.name + "'.\n\n" +
                "Unlike the competency screen this one is left SWITCHED ON - the hub is what the " +
                "COC4 scene opens on, not a panel behind a menu button.\n\n" +
                "NEXT:\n" +
                "  1. Put your TicketData assets in the TicketManager's All Tickets list. The hub " +
                "shows what is in that list and nothing else, so an empty list is an empty screen.\n" +
                "  2. Drop sprites on: TicketHubScreen (page background), Header, DetailCard and " +
                "ListArea. Each ticket's row icon comes from its own TicketData asset's Icon field.\n" +
                "  3. Wire the footer's Back button to whatever leaves COC4 - it is built but " +
                "deliberately unwired, because only you know whether that is a scene load or a " +
                "MenuNavigator screen.\n\n" +
                "Every reference on TicketHubView and TicketDetailPanelView is already wired. The " +
                "row prefab was written to " + PrefabFolder + ".",
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

            var go = new GameObject("TicketHubCanvas",
                                    typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            Canvas made = go.GetComponent<Canvas>();
            made.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            Undo.RegisterCreatedObjectUndo(go, "Create Canvas");
            Debug.Log("[TicketHubBuilder] No canvas in this scene, so one was created.", go);
            return made;
        }

        // ==================================================================

        private static GameObject BuildScreen(Canvas canvas)
        {
            RectTransform screen = NewRect(UniqueName(canvas, "TicketHubScreen"), canvas.transform);
            Stretch(screen);
            AddImage(screen, PageTint);                 // <- page background sprite goes here

            RectTransform column = NewRect("Column", screen);
            Stretch(column);
            VerticalLayoutGroup col = column.gameObject.AddComponent<VerticalLayoutGroup>();
            col.padding = new RectOffset(44, 44, 24, 24);
            col.spacing = 14f;
            col.childControlWidth = true;
            col.childControlHeight = true;
            col.childForceExpandWidth = true;
            col.childForceExpandHeight = false;

            BuildHeader(column);

            // ---- body: list | detail ------------------------------------
            RectTransform body = NewRect("Body", column);
            LayoutElement bodyLayout = body.gameObject.AddComponent<LayoutElement>();
            bodyLayout.flexibleHeight = 1f;
            bodyLayout.minHeight = 460f;

            HorizontalLayoutGroup row = body.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 16f;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;

            RectTransform rowsContent = BuildList(body);

            GameObject detailRoot;
            TMP_Text detailTitle, detailDescription, detailStatus;
            Button startButton, goToSiteButton;
            RectTransform detailCard = BuildDetail(body, out detailRoot, out detailTitle,
                                                   out detailDescription, out detailStatus,
                                                   out startButton, out goToSiteButton);

            Button backButton = BuildFooter(column);

            // ---- the components -----------------------------------------
            TicketManager manager = FindOrCreateManager();

            TicketRowView rowPrefab = BuildRowPrefab();

            var detailView = detailCard.gameObject.AddComponent<TicketDetailPanelView>();
            var detailSo = new SerializedObject(detailView);
            SetRef(detailSo, "panelRoot", detailRoot);
            SetRef(detailSo, "titleText", detailTitle);
            SetRef(detailSo, "descriptionText", detailDescription);
            SetRef(detailSo, "statusText", detailStatus);
            SetRef(detailSo, "startButton", startButton);
            SetRef(detailSo, "goToSiteButton", goToSiteButton);
            SetRef(detailSo, "manager", manager);
            detailSo.ApplyModifiedPropertiesWithoutUndo();

            var hubView = screen.gameObject.AddComponent<TicketHubView>();
            var hubSo = new SerializedObject(hubView);
            SetRef(hubSo, "manager", manager);
            SetRef(hubSo, "rowsContent", rowsContent);
            SetRef(hubSo, "rowPrefab", rowPrefab);
            SetRef(hubSo, "detailPanel", detailView);
            hubSo.ApplyModifiedPropertiesWithoutUndo();

            if (backButton != null) { /* left unwired on purpose - see the Build log */ }

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
            Stretch(inner, 0f);
            inner.offsetMin = new Vector2(28f, 12f);
            inner.offsetMax = new Vector2(-28f, -12f);

            VerticalLayoutGroup v = inner.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 2f;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childAlignment = TextAnchor.MiddleLeft;

            AddText(inner, "Title", "Service Tickets", SizeTitle, TextPrimary, FontStyles.Bold);
            AddText(inner, "Caption",
                    "Pick a work order, diagnose the fault on site, then apply the repair.",
                    SizeCaption, TextSecondary);
        }

        // ==================================================================

        private static RectTransform BuildList(RectTransform parent)
        {
            RectTransform area = NewRect("ListArea", parent);
            AddImage(area, RailBg);                     // <- list background sprite goes here

            LayoutElement le = area.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.minWidth = 520f;
            le.flexibleHeight = 1f;

            RectTransform viewport = NewRect("Viewport", area);
            Stretch(viewport, 10f);

            // raycastTarget TRUE, and that is the whole reason the list scrolls. Nearly invisible
            // but present: a ScrollRect only receives drag and wheel events while the pointer is
            // over a graphic that answers raycasts, and alpha plays no part in whether one does.
            AddImage(viewport, new Color(1f, 1f, 1f, 0.004f), raycastTarget: true);
            viewport.gameObject.AddComponent<RectMask2D>();

            RectTransform content = NewRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            VerticalLayoutGroup v = content.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(14, 30, 14, 14);
            v.spacing = 10f;
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

        // ==================================================================

        private static RectTransform BuildDetail(RectTransform parent, out GameObject panelRoot,
                                                 out TMP_Text title, out TMP_Text description,
                                                 out TMP_Text status, out Button start, out Button goToSite)
        {
            RectTransform card = NewRect("DetailCard", parent);
            Width(card, DetailWidth);
            AddImage(card, CardBg);                     // <- detail card sprite goes here
            AddTopEdge(card);

            // Sits BEHIND Panel Root and is never hidden, so the half of the screen with nothing
            // selected says what to do instead of being a blank blue rectangle.
            TMP_Text hint = AddText(card, "SelectHint",
                                    "Select a ticket to read the work order.", SizeBody, TextSecondary);
            Stretch(hint.rectTransform, 40f);
            hint.alignment = TextAlignmentOptions.Center;
            hint.enableWordWrapping = true;
            Object.DestroyImmediate(hint.GetComponent<LayoutElement>());

            // Panel Root is this child, NOT the card. Hiding the card would pull it out of the
            // horizontal layout and the list would jump sideways every time a ticket was picked.
            RectTransform root = NewRect("PanelRoot", card);
            Stretch(root);
            AddImage(root, CardBg);

            RectTransform inner = NewRect("Inner", root);
            Stretch(inner);
            inner.offsetMin = new Vector2(28f, 24f);
            inner.offsetMax = new Vector2(-28f, -28f);

            VerticalLayoutGroup v = inner.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = 14f;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            title = AddText(inner, "TicketTitle", "Ticket title", SizeSection, TextPrimary, FontStyles.Bold);
            title.enableWordWrapping = true;
            Fixed(title.rectTransform, SizeSection * 2.9f);

            status = AddText(inner, "Status", "Open", SizeCaption, Gold, FontStyles.Bold);

            RectTransform rule = NewRect("Rule", inner);
            Fixed(rule, 2f);
            AddImage(rule, new Color(1f, 1f, 1f, 0.16f));

            AddText(inner, "DescriptionLabel", "WORK ORDER", SizeCaption, TextSecondary, FontStyles.Bold);

            description = AddText(inner, "Description", "", SizeBody, TextPrimary);
            description.enableWordWrapping = true;
            description.alignment = TextAlignmentOptions.TopLeft;
            description.overflowMode = TextOverflowModes.Overflow;

            // The one label on this screen allowed to grow: a work order is however long the
            // customer's complaint is, and a fixed height would clip the sentence that names the
            // symptom. Flexible height plus a min, so it takes the slack the card has left.
            LayoutElement descriptionLayout = description.GetComponent<LayoutElement>();
            descriptionLayout.minHeight = 120f;
            descriptionLayout.preferredHeight = -1f;
            descriptionLayout.flexibleHeight = 1f;

            RectTransform buttons = NewRect("Buttons", inner);
            Fixed(buttons, 56f);
            HorizontalLayoutGroup h = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 14f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;

            start = MakeButton(buttons, "StartButton", "Start Ticket", 240f);
            goToSite = MakeButton(buttons, "GoToSiteButton", "Go to Site", 240f);

            panelRoot = root.gameObject;
            return card;
        }

        // ==================================================================

        private static Button BuildFooter(RectTransform parent)
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

            return MakeButton(footer, "BackButton", "Back", 200f);
        }

        // ==================================================================
        // Row prefab
        // ==================================================================

        private static TicketRowView BuildRowPrefab()
        {
            var temp = new GameObject("TicketRow", typeof(RectTransform));
            var rect = temp.GetComponent<RectTransform>();
            rect.localScale = Vector3.one;

            AddImage(rect, CardBg);                     // <- row background sprite goes here
            Fixed(rect, RowHeight);

            HorizontalLayoutGroup h = temp.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(16, 16, 12, 12);
            h.spacing = 16f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;

            RectTransform iconRect = NewRect("Icon", rect);
            Width(iconRect, 64f);
            Image icon = AddImage(iconRect, new Color(1f, 1f, 1f, 0.12f));
            icon.preserveAspect = true;

            TMP_Text titleText = AddText(rect, "Title", "Ticket title", SizeBody, TextPrimary, FontStyles.Bold);
            LayoutElement titleLayout = titleText.GetComponent<LayoutElement>();
            titleLayout.flexibleWidth = 1f;
            titleLayout.minWidth = 200f;

            // 220, not the 80 a word like "Open" needs. The longest string this label is ever given
            // is "In Progress", and a column sized for the shortest one turns the longest into an
            // ellipsis - which is how the competency screen's evidence column went wrong.
            TMP_Text statusText = AddText(rect, "Status", "Open", SizeCaption, TextSecondary, FontStyles.Bold);
            Width(statusText.rectTransform, 220f);
            statusText.alignment = TextAlignmentOptions.MidlineRight;

            Button viewButton = MakeButton(rect, "ViewButton", "View", 140f);

            var view = temp.AddComponent<TicketRowView>();
            var so = new SerializedObject(view);
            SetRef(so, "iconImage", icon);
            SetRef(so, "titleText", titleText);
            SetRef(so, "statusText", statusText);
            SetRef(so, "viewButton", viewButton);

            // These four are the additive fields on TicketRowView. Guarded, so this builder still
            // works against the older version of that script rather than throwing.
            SetColor(so, "openColor", TextSecondary);
            SetColor(so, "inProgressColor", AccentBar);
            SetColor(so, "diagnosedColor", Gold);
            SetColor(so, "resolvedColor", Resolved);

            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject asset = SaveAsPrefab(temp);
            return asset != null ? asset.GetComponent<TicketRowView>() : null;
        }

        // ==================================================================

        /// <summary>
        /// The TicketManager already in the scene, or a new one.
        ///
        /// Made rather than warned about, because a hub wired to nothing logs "[TicketHubView] No
        /// TicketManager assigned" at Play and shows an empty list - which reads as the builder
        /// having failed. An empty manager shows the same empty list, but with an object in the
        /// Hierarchy that has a list waiting for TicketData assets.
        /// </summary>
        private static TicketManager FindOrCreateManager()
        {
            TicketManager[] found = Object.FindObjectsByType<TicketManager>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (found.Length > 0) return found[0];

            var go = new GameObject("TicketManager", typeof(TicketManager));
            Undo.RegisterCreatedObjectUndo(go, "Create TicketManager");

            Debug.LogWarning("[TicketHubBuilder] No TicketManager was in this scene, so one was " +
                             "made. It has no tickets yet - put your TicketData assets in its All " +
                             "Tickets list or the hub will open empty.", go);

            return go.GetComponent<TicketManager>();
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
        // Asset and serialisation helpers
        // ==================================================================

        private static GameObject SaveAsPrefab(GameObject temp)
        {
            Directory.CreateDirectory(PrefabFolder);
            AssetDatabase.Refresh();

            string path = AssetDatabase.GenerateUniqueAssetPath(PrefabFolder + "/" + temp.name + ".prefab");
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(temp, path);

            Object.DestroyImmediate(temp);
            return asset;
        }

        /// <summary>Sets a private [SerializeField] reference. Silent when the field is absent, so
        /// this builder does not break the day one of those scripts is refactored.</summary>
        private static void SetRef(SerializedObject so, string field, Object value)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null) return;
            property.objectReferenceValue = value;
        }

        private static void SetColor(SerializedObject so, string field, Color value)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null) return;
            property.colorValue = value;
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
