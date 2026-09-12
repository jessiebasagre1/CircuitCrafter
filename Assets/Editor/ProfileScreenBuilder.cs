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
    /// Builds the learner profile screen - header, the create-a-learner bar, the scrolling list of
    /// profiles, the delete confirmation, and the footer - and wires every reference on it.
    ///
    /// RUN IT FROM: CircuitCrafter > Build Profile Screen.
    ///
    /// WHY THIS SCREEN MATTERS MORE THAN IT LOOKS
    /// Every competency record, every saved result and every ledger file is keyed by profile id.
    /// A class sharing one profile does not produce twenty records, it produces one record that is
    /// a class average - which is not assessment evidence for anybody. This screen is how a learner
    /// says which of those records is theirs, so it is worth having it work properly rather than
    /// being the one screen assembled in a hurry.
    ///
    /// DELETION IS HIDDEN UNLESS YOU ASK FOR IT
    /// ProfilePickerView hides every Delete button when Confirm Delete Panel is unassigned. The
    /// builder DOES build that panel and wire it, because a confirm step is exactly what makes
    /// deletion safe - but if you would rather no one could delete a profile at all, clear the
    /// Confirm Delete Panel field afterwards and the buttons disappear with it.
    ///
    /// PALETTE NOTE
    /// The colours and type scale below are a copy of the ones in CompetencyScreenBuilder rather
    /// than a shared file. Two screens, two copies - so retuning the look means editing both. Say
    /// the word and I will factor them into one kit; it was left this way to avoid touching a
    /// builder you are actively using.
    /// </summary>
    public static class ProfileScreenBuilder
    {
        private const string PrefabFolder = "Assets/Assessment/UI/Generated";

        // ---- Palette (kept in step with CompetencyScreenBuilder) -----------

        private static readonly Color PageTint = new Color32(0x16, 0x25, 0x6E, 0xCC);
        private static readonly Color HeaderBg = new Color32(0x2A, 0x3A, 0xB5, 0xFF);
        private static readonly Color CardBg = new Color32(0x35, 0x47, 0xC4, 0xFF);
        private static readonly Color RailBg = new Color32(0x1B, 0x2A, 0x7A, 0xE6);
        private static readonly Color FieldBg = new Color32(0x12, 0x1D, 0x5E, 0xFF);
        private static readonly Color TextPrimary = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        private static readonly Color TextSecondary = new Color32(0xA9, 0xC6, 0xF0, 0xFF);
        private static readonly Color TextMuted = new Color32(0x7E, 0x93, 0xC4, 0xFF);
        private static readonly Color EdgeCyan = new Color32(0x5F, 0xD4, 0xFF, 0xFF);
        private static readonly Color Gold = new Color32(0xFF, 0xC9, 0x3C, 0xFF);
        private static readonly Color ButtonBg = new Color32(0x3B, 0x4C, 0xCB, 0xFF);
        private static readonly Color DangerBg = new Color32(0xD9, 0x36, 0x4D, 0xFF);
        private static readonly Color ScrimBg = new Color32(0x06, 0x0C, 0x28, 0xD9);

        private const float SizeTitle = 36f;
        private const float SizeSection = 28f;
        private const float SizeBody = 24f;
        private const float SizeCaption = 21f;

        // ==================================================================

        [MenuItem("CircuitCrafter/Build Profile Screen", false, 41)]
        public static void Build()
        {
            Canvas canvas = FindOrCreateCanvas();
            if (canvas == null) return;

            GameObject screen = BuildScreen(canvas);

            Undo.RegisterCreatedObjectUndo(screen, "Build Profile Screen");
            Selection.activeGameObject = screen;
            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);

            Debug.Log(
                "[ProfileScreenBuilder] Built '" + screen.name + "' under '" + canvas.name + "'.\n\n" +
                "IT IS SWITCHED OFF so it cannot cover your existing menu. Tick it to look at it.\n\n" +
                "NEXT:\n" +
                "  1. Wire your PROFILE button (top-right of the main menu) to MenuNavigator > Show " +
                "with this panel in the slot. The screen is already in MenuNavigator's Screens list " +
                "and Back already works.\n" +
                "  2. Drop your sprites on: ProfileScreen (page background), Header (header plate), " +
                "and CreateBar (the plate behind the name and section fields).\n\n" +
                "Every reference on ProfilePickerView is wired, including the delete confirmation. " +
                "The row prefab was written to " + PrefabFolder + ".",
                screen);
        }

        // ==================================================================

        private static GameObject BuildScreen(Canvas canvas)
        {
            RectTransform screen = NewRect(UniqueName(canvas, "ProfileScreen"), canvas.transform);
            Stretch(screen);
            AddImage(screen, PageTint);         // <- page background sprite goes here

            RectTransform column = NewRect("Column", screen);
            Stretch(column);
            VerticalLayoutGroup col = column.gameObject.AddComponent<VerticalLayoutGroup>();
            col.padding = new RectOffset(44, 44, 24, 24);
            col.spacing = 14f;
            col.childControlWidth = true;
            col.childControlHeight = true;
            col.childForceExpandWidth = true;
            col.childForceExpandHeight = false;

            TMP_Text activeText = BuildHeader(column);

            TMP_InputField nameInput, sectionInput;
            Button createButton;
            TMP_Text createError;
            BuildCreateBar(column, out nameInput, out sectionInput, out createButton, out createError);

            RectTransform content = BuildList(column);

            Button back = BuildFooter(column);

            GameObject confirmPanel;
            TMP_Text confirmText;
            Button confirmYes, confirmNo;
            BuildConfirmDelete(screen, out confirmPanel, out confirmText, out confirmYes, out confirmNo);

            WireNavigation(screen.gameObject, back);

            ProfileRowView rowPrefab = BuildRowPrefab();

            ProfilePickerView view = screen.gameObject.AddComponent<ProfilePickerView>();
            var so = new SerializedObject(view);
            SetRef(so, "rowContainer", content);
            SetRef(so, "rowPrefab", rowPrefab);
            SetRef(so, "activeProfileText", activeText);
            SetRef(so, "nameInput", nameInput);
            SetRef(so, "sectionInput", sectionInput);
            SetRef(so, "createButton", createButton);
            SetRef(so, "createErrorText", createError);
            SetRef(so, "confirmDeletePanel", confirmPanel);
            SetRef(so, "confirmDeleteText", confirmText);
            SetRef(so, "confirmDeleteYes", confirmYes);
            SetRef(so, "confirmDeleteNo", confirmNo);
            so.ApplyModifiedPropertiesWithoutUndo();

            screen.gameObject.SetActive(false);
            return screen.gameObject;
        }

        // ==================================================================

        private static TMP_Text BuildHeader(RectTransform parent)
        {
            RectTransform header = NewRect("Header", parent);
            AddImage(header, HeaderBg);         // <- header plate sprite goes here
            AddTopEdge(header, 5f);
            Fixed(header, 92f);

            RectTransform row = NewRect("Pad", header);
            Stretch(row);
            HorizontalLayoutGroup h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(28, 28, 14, 14);
            h.spacing = 18f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;

            TMP_Text title = AddText(row, "Title", "LEARNER PROFILES", SizeTitle,
                                     TextPrimary, FontStyles.Bold);
            title.characterSpacing = 5f;
            Width(title.rectTransform, 520f);

            // The active learner belongs in the header, not buried in the list. Everything this
            // screen exists to control hangs off which name is showing here, and a learner who
            // cannot see it at a glance is one who records a run against somebody else's profile.
            TMP_Text active = AddText(row, "ActiveProfileText", "No learner selected", SizeSection,
                                      Gold, FontStyles.Bold);
            active.alignment = TextAlignmentOptions.MidlineRight;
            LayoutElement le = active.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;

            return active;
        }

        private static void BuildCreateBar(RectTransform parent, out TMP_InputField nameInput,
                                           out TMP_InputField sectionInput, out Button createButton,
                                           out TMP_Text errorText)
        {
            RectTransform bar = NewRect("CreateBar", parent);
            AddImage(bar, CardBg);              // <- create-bar plate sprite goes here
            AddTopEdge(bar);
            Fixed(bar, 84f);

            RectTransform row = NewRect("Pad", bar);
            Stretch(row);
            HorizontalLayoutGroup h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(20, 20, 16, 16);
            h.spacing = 14f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;

            nameInput = MakeInputField(row, "NameInput", "Learner name", 0f, flexible: true);
            sectionInput = MakeInputField(row, "SectionInput", "Section", 320f);

            createButton = MakeButton(row, "CreateButton", "Create", 200f, ButtonBg);

            errorText = AddText(row, "CreateErrorText", "", SizeCaption, Gold);
            LayoutElement le = errorText.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.minWidth = 260f;
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

            // raycastTarget TRUE - a ScrollRect only receives drag and wheel events while the
            // pointer is over a graphic that answers raycasts. Nearly invisible, but present.
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
            AddImage(rect, FieldBg, raycastTarget: true);

            RectTransform slide = NewRect("Sliding Area", rect);
            Stretch(slide);

            RectTransform handle = NewRect("Handle", slide);
            Stretch(handle);
            Image handleImage = AddImage(handle, EdgeCyan, raycastTarget: true);

            Scrollbar bar = rect.gameObject.AddComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;
            bar.handleRect = handle;
            bar.targetGraphic = handleImage;
            return bar;
        }

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

            Button back = MakeButton(footer, "BackButton", "Back", 200f, ButtonBg);

            TMP_Text hint = AddText(footer, "Hint",
                                    "Every result and competency record is stored against the " +
                                    "selected learner.", SizeCaption, TextMuted);
            LayoutElement le = hint.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;

            return back;
        }

        /// <summary>
        /// The delete confirmation, as a full-screen scrim with a dialog on it.
        ///
        /// Full-screen on purpose: the scrim swallows every click that is not on the dialog, so a
        /// stray press behind it cannot select a different learner or delete a second one while the
        /// question is still on screen. Deleting a profile destroys its whole competency history.
        /// </summary>
        private static void BuildConfirmDelete(RectTransform parent, out GameObject panel,
                                               out TMP_Text text, out Button yes, out Button no)
        {
            RectTransform scrim = NewRect("ConfirmDeletePanel", parent);
            Stretch(scrim);
            AddImage(scrim, ScrimBg, raycastTarget: true);

            RectTransform dialog = NewRect("Dialog", scrim);
            Centre(dialog, 760f, 280f);
            AddImage(dialog, CardBg);
            AddTopEdge(dialog, 5f);

            RectTransform pad = NewRect("Pad", dialog);
            Stretch(pad);
            VerticalLayoutGroup v = pad.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(32, 32, 28, 28);
            v.spacing = 20f;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            v.childAlignment = TextAnchor.UpperCenter;

            TMP_Text heading = AddText(pad, "Heading", "DELETE THIS LEARNER?", SizeSection,
                                       TextPrimary, FontStyles.Bold);
            heading.alignment = TextAlignmentOptions.Center;

            text = AddText(pad, "ConfirmDeleteText",
                           "This removes the learner and every competency record stored " +
                           "against them. It cannot be undone.", SizeBody, TextSecondary);
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = true;
            Fixed(text.rectTransform, 84f);

            RectTransform buttons = NewRect("Buttons", pad);
            Fixed(buttons, 56f);
            HorizontalLayoutGroup h = buttons.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 18f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childAlignment = TextAnchor.MiddleCenter;

            // Cancel first and Delete second, so the safe answer is the one under the thumb on a
            // touch build and the destructive one is never where a double-tap lands.
            no = MakeButton(buttons, "ConfirmDeleteNo", "Cancel", 220f, ButtonBg);
            yes = MakeButton(buttons, "ConfirmDeleteYes", "Delete", 220f, DangerBg);

            panel = scrim.gameObject;
            panel.SetActive(false);
        }

        // ==================================================================

        private static ProfileRowView BuildRowPrefab()
        {
            RectTransform root = NewRect("ProfileRow", null);
            AddImage(root, new Color32(0x2B, 0x3A, 0xA8, 0xFF));
            Fixed(root, 68f);

            RectTransform pad = NewRect("Pad", root);
            Stretch(pad);
            HorizontalLayoutGroup h = pad.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(16, 16, 10, 10);
            h.spacing = 14f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;

            RectTransform marker = NewRect("ActiveMarker", pad);
            Width(marker, 14f);
            AddImage(marker, Gold);
            marker.gameObject.SetActive(false);

            TMP_Text name = AddText(pad, "Name", "Learner name", SizeBody, TextPrimary, FontStyles.Bold);
            LayoutElement nle = name.gameObject.AddComponent<LayoutElement>();
            nle.flexibleWidth = 1f;
            nle.minWidth = 260f;

            TMP_Text section = AddText(pad, "Section", "Section", SizeCaption, TextSecondary);
            Width(section.rectTransform, 240f);

            TMP_Text lastPlayed = AddText(pad, "LastPlayed", "", SizeCaption, TextMuted);
            Width(lastPlayed.rectTransform, 300f);
            lastPlayed.alignment = TextAlignmentOptions.MidlineRight;

            Button select = MakeButton(pad, "SelectButton", "Select", 180f, ButtonBg);
            Button delete = MakeButton(pad, "DeleteButton", "Delete", 150f, DangerBg);

            ProfileRowView view = root.gameObject.AddComponent<ProfileRowView>();
            var so = new SerializedObject(view);
            SetRef(so, "nameText", name);
            SetRef(so, "sectionText", section);
            SetRef(so, "lastPlayedText", lastPlayed);
            SetRef(so, "selectButton", select);
            SetRef(so, "deleteButton", delete);
            SetRef(so, "activeMarker", marker.gameObject);
            so.ApplyModifiedPropertiesWithoutUndo();

            return SaveAsPrefab(root.gameObject).GetComponent<ProfileRowView>();
        }

        // ==================================================================

        /// <summary>
        /// A TMP input field, built by hand.
        ///
        /// TMP_InputField needs three things wired or it silently does nothing when typed into: a
        /// text viewport, the text component itself, and a placeholder. All three are children it
        /// cannot create for itself, which is why adding the component to a bare Image and pressing
        /// Play gives you a field that takes focus and shows no characters.
        /// </summary>
        private static TMP_InputField MakeInputField(RectTransform parent, string name,
                                                     string placeholderText, float width,
                                                     bool flexible = false)
        {
            RectTransform rect = NewRect(name, parent);
            Image bg = rect.gameObject.AddComponent<Image>();
            bg.color = FieldBg;

            LayoutElement le = rect.gameObject.AddComponent<LayoutElement>();
            if (flexible) { le.flexibleWidth = 1f; le.minWidth = 320f; }
            else { le.minWidth = width; le.preferredWidth = width; }

            RectTransform area = NewRect("Text Area", rect);
            Stretch(area, 12f);
            area.gameObject.AddComponent<RectMask2D>();

            TMP_Text placeholder = AddText(area, "Placeholder", placeholderText, SizeBody, TextMuted,
                                           FontStyles.Italic);
            Stretch(placeholder.rectTransform);

            TMP_Text text = AddText(area, "Text", "", SizeBody, TextPrimary);
            Stretch(text.rectTransform);

            TMP_InputField field = rect.gameObject.AddComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = (TextMeshProUGUI)text;
            field.placeholder = placeholder;
            field.targetGraphic = bg;
            field.pointSize = SizeBody;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = 40;
            field.text = "";

            return field;
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

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
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

            // Ellipsis, never Overflow - a label wider than its slot must clip to its own column
            // rather than draw straight over whatever is beside it.
            text.overflowMode = TextOverflowModes.Ellipsis;

            if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;

            Fixed(rect, size * 1.45f);
            return text;
        }

        private static Button MakeButton(RectTransform parent, string name, string label,
                                         float width, Color colour)
        {
            RectTransform rect = NewRect(name, parent);
            Width(rect, width);

            Image bg = rect.gameObject.AddComponent<Image>();
            bg.color = colour;      // buttons DO need to be hit, so raycastTarget stays on

            TMP_Text text = AddText(rect, "Label", label, SizeBody, TextPrimary, FontStyles.Bold);
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

        private static void WireNavigation(GameObject screen, Button back)
        {
            MenuNavigator nav = Object.FindFirstObjectByType<MenuNavigator>(FindObjectsInactive.Include);

            if (nav == null)
            {
                Debug.LogWarning("[ProfileScreenBuilder] No MenuNavigator in this scene, so the " +
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

            Debug.Log("[ProfileScreenBuilder] Registered with MenuNavigator on '" + nav.name +
                      "' and wired Back to it.", nav);
        }

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

            var go = new GameObject("ProfileCanvas",
                                    typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            Canvas made = go.GetComponent<Canvas>();
            made.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            Undo.RegisterCreatedObjectUndo(go, "Create Canvas");
            Debug.Log("[ProfileScreenBuilder] No canvas in this scene, so one was created.", go);
            return made;
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

        private static void SetRef(SerializedObject so, string field, Object value)
        {
            SerializedProperty property = so.FindProperty(field);

            if (property == null)
            {
                Debug.LogWarning("[ProfileScreenBuilder] '" + so.targetObject.GetType().Name +
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
    }
}
#endif
