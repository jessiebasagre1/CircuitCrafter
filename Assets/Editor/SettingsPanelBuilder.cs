using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using CircuitCrafter.Settings;

namespace CircuitCrafter.Settings.EditorTools
{
    /// <summary>
    /// Builds the whole Settings screen - a full-screen panel with four category
    /// tabs down the left, a page of controls on the right, and a question mark
    /// beside every setting - and wires every field on SettingsPanel.
    ///
    /// MENU: CircuitCrafter > Build Settings Panel
    ///
    /// NOTHING IN IT HAS A FIXED SIZE ON THE AXIS THAT MATTERS.
    /// The panel is pinned to all four corners of the canvas, the content pane is
    /// pinned to the panel's edges, the rows stretch across the pane, and the
    /// buttons hang off the bottom corners. That is what makes it survive a phone
    /// in portrait, a projector, and Interface Size at 1.6x - a screen laid out
    /// from one authored resolution is correct exactly once.
    ///
    /// The controls INSIDE each row deliberately stay at fixed positions on the
    /// left. A slider that grows to a thousand pixels is harder to set precisely,
    /// not easier.
    ///
    /// WHY A BUILDER RATHER THAN A PREFAB
    /// A Unity Slider is five nested objects with four cross-references (Fill Rect,
    /// Handle Rect, Target Graphic, Direction) and a Toggle is three with two.
    /// Eleven of those, plus eleven help buttons, plus four tabs and four pages, is
    /// most of an afternoon of dragging and roughly four chances per control to
    /// wire something to the wrong object - which produces a slider that moves
    /// smoothly and changes nothing, with no error anywhere. Building them in code
    /// removes that whole category of mistake, and the layout numbers at the top
    /// mean the proportions can be retuned in one place.
    ///
    /// SAFE TO RE-RUN. It skips a panel that already exists, so running it again
    /// after restyling changes nothing. Delete SettingsPanel and re-run to rebuild.
    /// </summary>
    public static class SettingsPanelBuilder
    {
        private const string UndoName = "Build Settings Panel";

        // ---- layout ------------------------------------------------------
        // The panel fills the screen, so there is no panel width or height any
        // more - everything is measured from an edge instead.
        private const float Margin = 40f;
        private const float FooterHeight = 106f;

        private const float SidebarWidth = 220f;
        private const float TabHeight = 52f;
        private const float TabGap = 8f;
        private const float ContentTop = 76f;

        private const float PaneX = 292f;

        private const float RowHeight = 46f;
        private const float RowGap = 4f;
        private const float RowLabelWidth = 230f;
        private const float HelpX = 252f;
        private const float HelpSize = 28f;
        private const float ControlX = 292f;
        private const float ControlWidth = 268f;
        private const float ValueX = 572f;
        private const float ValueWidth = 104f;

        private const float BubbleWidth = 340f;

        // ---- colour ------------------------------------------------------
        private static readonly Color PanelBg = new Color(0.11f, 0.13f, 0.12f, 0.98f);
        private static readonly Color PaneBg = new Color(1f, 1f, 1f, 0.02f);
        private static readonly Color RowBg = new Color(1f, 1f, 1f, 0.03f);
        private static readonly Color HelpBoxBg = new Color(0.08f, 0.16f, 0.13f, 1f);
        private static readonly Color TrackBg = new Color(0.06f, 0.08f, 0.07f, 1f);
        private static readonly Color Accent = new Color(0.31f, 0.76f, 0.60f, 1f);
        private static readonly Color TabIdle = new Color(0.16f, 0.19f, 0.18f, 1f);
        private static readonly Color ButtonBg = new Color(0.18f, 0.22f, 0.20f, 1f);
        private static readonly Color TextMain = new Color(0.91f, 0.93f, 0.91f, 1f);
        private static readonly Color TextDim = new Color(0.66f, 0.71f, 0.68f, 1f);

        private static float rowCursor;

        // ==================================================================

        [MenuItem("CircuitCrafter/Build Settings Panel")]
        public static void Build()
        {
            Canvas canvas = FindOrCreateCanvas();
            EnsureEventSystem();

            if (Find(canvas.transform, "SettingsPanel") != null)
            {
                Debug.Log("[SettingsPanelBuilder] A SettingsPanel already exists in this scene - nothing was created. " +
                          "Delete it and re-run to rebuild.");
                return;
            }

            RectTransform panel = Panel(canvas.transform, "SettingsPanel");
            Label(panel, "Title", "SETTINGS", new Vector2(Margin, 26), new Vector2(400, 30), 22, TextMain);

            // ---- the four tabs ----
            Button tabControls = Tab(panel, "Tab_Controls", "Controls", 0);
            Button tabStation = Tab(panel, "Tab_Station", "Station camera", 1);
            Button tabDisplay = Tab(panel, "Tab_Display", "Display", 2);
            Button tabAudio = Tab(panel, "Tab_Audio", "Audio", 3);

            // ---- the pane the pages live in ----
            // Stretched to all four edges rather than given a size: on a full-screen
            // panel the pane has to grow with the window, and pinning it to the
            // edges is the only way that survives a resolution it was not authored
            // at. The sidebar is on the left and the buttons are along the bottom,
            // so those two offsets are what keeps it clear of them.
            RectTransform pane = Stretch(panel, "ContentPane", typeof(CanvasRenderer), typeof(Image));
            pane.offsetMin = new Vector2(PaneX, FooterHeight);
            pane.offsetMax = new Vector2(-Margin, -ContentTop);
            pane.GetComponent<Image>().color = PaneBg;

            // ---- page 1: controls ----
            RectTransform pageControls = Page(pane, "Page_Controls");
            GameObject mouseRow = SliderRow(pageControls, "Row_MouseLook", "Mouse look speed", "mouseLook",
                                            out Slider mouseSlider, out TMP_Text mouseValue);
            GameObject touchRow = SliderRow(pageControls, "Row_TouchLook", "Touch look speed", "touchLook",
                                            out Slider touchSlider, out TMP_Text touchValue);
            ToggleRow(pageControls, "Row_InvertLookY", "Invert vertical look", "invertLook", out Toggle invertLook);

            // ---- page 2: station camera ----
            RectTransform pageStation = Page(pane, "Page_StationCamera");
            SliderRow(pageStation, "Row_StationPan", "Drag speed", "stationPan", out Slider panSlider, out TMP_Text panValue);
            SliderRow(pageStation, "Row_StationZoom", "Zoom speed", "stationZoom", out Slider zoomSlider, out TMP_Text zoomValue);
            ToggleRow(pageStation, "Row_InvertDrag", "Invert drag direction", "invertDrag", out Toggle invertDrag);

            // ---- page 3: display ----
            RectTransform pageDisplay = Page(pane, "Page_Display");
            SliderRow(pageDisplay, "Row_UiScale", "Interface size", "uiScale", out Slider uiSlider, out TMP_Text uiValue);
            ToggleRow(pageDisplay, "Row_ReduceMotion", "Reduce motion", "reduceMotion", out Toggle reduceMotion);
            ToggleRow(pageDisplay, "Row_Performance", "Performance mode", "performance", out Toggle performance);
            GameObject fullscreenRow = ToggleRow(pageDisplay, "Row_Fullscreen", "Fullscreen", "fullscreen", out Toggle fullscreen);

            // ---- page 4: audio ----
            RectTransform pageAudio = Page(pane, "Page_Audio");
            SliderRow(pageAudio, "Row_Volume", "Master volume", "volume", out Slider volumeSlider, out TMP_Text volumeValue);

            // ---- buttons, pinned to the bottom corners ----
            Button reset = Btn(panel, "ResetButton", "Reset to defaults", Vector2.zero, new Vector2(SidebarWidth, 48));
            PinToCorner(reset.GetComponent<RectTransform>(), 0f, new Vector2(Margin, Margin));

            Button close = Btn(panel, "CloseButton", "Close", Vector2.zero, new Vector2(180, 48));
            PinToCorner(close.GetComponent<RectTransform>(), 1f, new Vector2(-Margin, Margin));

            // ---- the floating help bubble ----
            // Built LAST so it is the final child and draws over everything else.
            // Its position and height are worked out at runtime; the size set here
            // only matters for looking at it in the editor.
            RectTransform bubble = BuildBubble(panel, out TMP_Text helpTitle, out TMP_Text helpBody,
                                               out Button helpClose);

            // ---- component ----
            var component = panel.gameObject.AddComponent<SettingsPanel>();
            var so = new SerializedObject(component);

            Assign(so, "panelRoot", panel.gameObject);
            Assign(so, "closeButton", close);
            Assign(so, "resetButton", reset);
            Assign(so, "helpBubble", bubble);
            Assign(so, "helpTitle", helpTitle);
            Assign(so, "helpBody", helpBody);
            Assign(so, "helpCloseButton", helpClose);

            AssignCategories(so,
                new[] { "Controls", "Station camera", "Display", "Audio" },
                new[] { tabControls, tabStation, tabDisplay, tabAudio },
                new[] { pageControls.gameObject, pageStation.gameObject, pageDisplay.gameObject, pageAudio.gameObject });

            Assign(so, "mouseLookRow", mouseRow);
            Assign(so, "mouseLookSlider", mouseSlider);
            Assign(so, "mouseLookValue", mouseValue);
            Assign(so, "touchLookRow", touchRow);
            Assign(so, "touchLookSlider", touchSlider);
            Assign(so, "touchLookValue", touchValue);
            Assign(so, "invertLookYToggle", invertLook);

            Assign(so, "stationPanSlider", panSlider);
            Assign(so, "stationPanValue", panValue);
            Assign(so, "stationZoomSlider", zoomSlider);
            Assign(so, "stationZoomValue", zoomValue);
            Assign(so, "invertStationDragToggle", invertDrag);

            Assign(so, "uiScaleSlider", uiSlider);
            Assign(so, "uiScaleValue", uiValue);
            Assign(so, "reduceMotionToggle", reduceMotion);
            Assign(so, "performanceModeToggle", performance);
            Assign(so, "fullscreenRow", fullscreenRow);
            Assign(so, "fullscreenToggle", fullscreen);

            Assign(so, "masterVolumeSlider", volumeSlider);
            Assign(so, "masterVolumeValue", volumeValue);

            so.ApplyModifiedPropertiesWithoutUndo();

            bool madeApplier = EnsureApplier();

            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);

            Debug.Log("[SettingsPanelBuilder] Built SettingsPanel: 4 categories, 6 slider(s), 5 toggle(s), " +
                      "11 help button(s)" + (madeApplier ? ", plus a SettingsApplier." : ".") + "\n\n" +
                      "STILL TO DO BY HAND:\n" +
                      "  1. Wire whatever opens this - your pause menu's Settings button, or a\n" +
                      "     main-menu button - to SettingsPanel.Open().\n" +
                      "  2. The SettingsApplier belongs in your FIRST scene (the main menu). It\n" +
                      "     survives every scene load, so it only needs to exist once.\n" +
                      "  3. Untick the old CameraSettingsUI. Pan and zoom are driven from here\n" +
                      "     now, and two things writing the same fields will fight.\n" +
                      "  4. Help text lives in SettingsHelp.cs, not on the buttons. Edit it there.\n" +
                      "     The bubble sizes and positions itself - do not anchor it by hand.\n" +
                      "  5. Restyle freely - moving and recolouring breaks none of the wiring.");
        }

        // ==================================================================
        // The help bubble
        // ==================================================================

        /// <summary>
        /// The floating tip. Built with STRETCHING children rather than fixed ones,
        /// which is the whole trick: SettingsPanel changes the bubble's height at
        /// runtime to fit the text, and a body label placed at a fixed size would
        /// keep its old height and spill out of the bottom.
        /// </summary>
        private static RectTransform BuildBubble(RectTransform panel, out TMP_Text title,
                                                 out TMP_Text body, out Button close)
        {
            RectTransform bubble = Place(panel, "HelpBubble", Vector2.zero,
                                         new Vector2(BubbleWidth, 120f),
                                         typeof(CanvasRenderer), typeof(Image));
            bubble.GetComponent<Image>().color = HelpBoxBg;

            // Pinned across the top: stays put whatever the bubble's height becomes.
            RectTransform titleRect = Place(bubble, "HelpTitle", new Vector2(18, 14),
                                            new Vector2(BubbleWidth - 60, 22));
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.offsetMin = new Vector2(18, -36);
            titleRect.offsetMax = new Vector2(-42, -14);

            title = titleRect.gameObject.AddComponent<TextMeshProUGUI>();
            title.text = "";
            title.fontSize = 16;
            title.color = Accent;
            title.alignment = TextAlignmentOptions.TopLeft;
            title.raycastTarget = false;

            // Stretched to every edge: grows with the bubble, which is what lets the
            // height be decided from the text.
            RectTransform bodyRect = Stretch(bubble, "HelpBody");
            bodyRect.offsetMin = new Vector2(18, 16);
            bodyRect.offsetMax = new Vector2(-18, -40);

            body = bodyRect.gameObject.AddComponent<TextMeshProUGUI>();
            body.text = "";
            body.fontSize = 14;
            body.color = TextMain;
            body.alignment = TextAlignmentOptions.TopLeft;
            body.textWrappingMode = TextWrappingModes.Normal;
            body.raycastTarget = false;

            // Top-right corner, whatever the size.
            RectTransform closeRect = Place(bubble, "HelpClose", Vector2.zero, new Vector2(24, 24),
                                            typeof(CanvasRenderer), typeof(Image), typeof(Button));
            closeRect.anchorMin = new Vector2(1f, 1f);
            closeRect.anchorMax = new Vector2(1f, 1f);
            closeRect.pivot = new Vector2(1f, 1f);
            closeRect.anchoredPosition = new Vector2(-10, -10);

            var closeImage = closeRect.GetComponent<Image>();
            closeImage.color = new Color(1f, 1f, 1f, 0.06f);

            close = closeRect.GetComponent<Button>();
            close.targetGraphic = closeImage;

            TMP_Text closeLabel = Label(closeRect, "Label", "\u00d7", Vector2.zero, new Vector2(24, 24), 16, TextDim);
            closeLabel.alignment = TextAlignmentOptions.Center;

            bubble.gameObject.SetActive(false);
            return bubble;
        }

        // ==================================================================
        // Tabs and pages
        // ==================================================================

        private static Button Tab(RectTransform panel, string name, string text, int index)
        {
            float y = ContentTop + index * (TabHeight + TabGap);

            RectTransform rect = Place(panel, name, new Vector2(Margin, y), new Vector2(SidebarWidth, TabHeight),
                                       typeof(CanvasRenderer), typeof(Image), typeof(Button));

            var image = rect.GetComponent<Image>();
            image.color = TabIdle;

            var button = rect.GetComponent<Button>();
            button.targetGraphic = image;

            TMP_Text label = Label(rect, "Label", text, new Vector2(18, 0),
                                   new Vector2(SidebarWidth - 36, TabHeight), 16, TextDim);
            label.alignment = TextAlignmentOptions.MidlineLeft;

            return button;
        }

        /// <summary>A page fills the content pane. SettingsPanel switches which one
        /// is active; all four sit in exactly the same place.</summary>
        private static RectTransform Page(RectTransform pane, string name)
        {
            RectTransform page = Stretch(pane, name);
            rowCursor = 0f;
            return page;
        }

        // ==================================================================
        // Rows
        // ==================================================================

        /// <summary>A label, a question mark, a slider and a live value readout.
        /// Returns the ROW so SettingsPanel can hide the whole thing on a platform
        /// where the control would do nothing.</summary>
        private static GameObject SliderRow(RectTransform page, string name, string labelText, string settingId,
                                            out Slider slider, out TMP_Text valueText)
        {
            RectTransform row = Row(page, name, labelText, settingId);

            slider = MakeSlider(row, new Vector2(ControlX, (RowHeight - 20f) / 2f), new Vector2(ControlWidth, 20f));

            valueText = Label(row, "Value", "50%", new Vector2(ValueX, 0), new Vector2(ValueWidth, RowHeight), 15, TextDim);
            valueText.alignment = TextAlignmentOptions.MidlineRight;

            return row.gameObject;
        }

        private static GameObject ToggleRow(RectTransform page, string name, string labelText, string settingId,
                                            out Toggle toggle)
        {
            RectTransform row = Row(page, name, labelText, settingId);
            toggle = MakeToggle(row, new Vector2(ControlX, (RowHeight - 26f) / 2f));
            return row.gameObject;
        }

        /// <summary>The bit both row kinds share: background, label, question mark.</summary>
        private static RectTransform Row(RectTransform page, string name, string labelText, string settingId)
        {
            RectTransform row = Place(page, name, new Vector2(0, rowCursor), new Vector2(0, RowHeight),
                                      typeof(CanvasRenderer), typeof(Image));

            // Stretched across the page rather than given a width. The controls
            // inside stay at fixed positions on the left - a slider a thousand
            // pixels wide is worse to use, not better - but the row's background
            // reaches both edges, which is what makes the list read as a list.
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0f, 1f);
            row.sizeDelta = new Vector2(0f, RowHeight);
            row.anchoredPosition = new Vector2(0f, -rowCursor);

            row.GetComponent<Image>().color = RowBg;

            TMP_Text label = Label(row, "Label", labelText, new Vector2(16, 0),
                                   new Vector2(RowLabelWidth, RowHeight), 16, TextMain);
            label.alignment = TextAlignmentOptions.MidlineLeft;

            HelpButton(row, settingId);

            rowCursor += RowHeight + RowGap;
            return row;
        }

        /// <summary>The question mark. Round, quiet, and it carries the id of the
        /// entry in SettingsHelp rather than the text itself - so the wording lives
        /// in one file that can be read and revised in a single pass.</summary>
        private static void HelpButton(RectTransform row, string settingId)
        {
            RectTransform rect = Place(row, "Help", new Vector2(HelpX, (RowHeight - HelpSize) / 2f),
                                       new Vector2(HelpSize, HelpSize),
                                       typeof(CanvasRenderer), typeof(Image), typeof(Button),
                                       typeof(SettingsHelpButton));

            var image = rect.GetComponent<Image>();
            image.color = TrackBg;
            image.sprite = Builtin("UI/Skin/Knob.psd");

            var button = rect.GetComponent<Button>();
            button.targetGraphic = image;

            TMP_Text label = Label(rect, "Label", "?", Vector2.zero, new Vector2(HelpSize, HelpSize), 15, Accent);
            label.alignment = TextAlignmentOptions.Center;

            var helpButton = rect.GetComponent<SettingsHelpButton>();
            var so = new SerializedObject(helpButton);
            var property = so.FindProperty("settingId");
            if (property != null) property.stringValue = settingId;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ==================================================================
        // The fiddly bits: Unity's composite controls
        // ==================================================================

        /// <summary>A Slider is five objects and four references. Getting fillRect
        /// or handleRect wrong produces a control that moves and does nothing, with
        /// no error - which is exactly why this is code and not a setup
        /// instruction.</summary>
        private static Slider MakeSlider(RectTransform parent, Vector2 topLeft, Vector2 size)
        {
            RectTransform root = Place(parent, "Slider", topLeft, size, typeof(Slider));

            RectTransform background = Stretch(root, "Background", typeof(CanvasRenderer), typeof(Image));
            var backgroundImage = background.GetComponent<Image>();
            backgroundImage.color = TrackBg;
            backgroundImage.sprite = Builtin("UI/Skin/Background.psd");
            backgroundImage.type = Image.Type.Sliced;

            RectTransform fillArea = Stretch(root, "Fill Area");
            fillArea.offsetMin = new Vector2(6, 0);
            fillArea.offsetMax = new Vector2(-14, 0);

            RectTransform fill = Stretch(fillArea, "Fill", typeof(CanvasRenderer), typeof(Image));
            var fillImage = fill.GetComponent<Image>();
            fillImage.color = Accent;
            fillImage.sprite = Builtin("UI/Skin/UISprite.psd");
            fillImage.type = Image.Type.Sliced;
            fill.sizeDelta = new Vector2(10, 0);

            RectTransform handleArea = Stretch(root, "Handle Slide Area");
            handleArea.offsetMin = new Vector2(8, 0);
            handleArea.offsetMax = new Vector2(-8, 0);

            RectTransform handle = Stretch(handleArea, "Handle", typeof(CanvasRenderer), typeof(Image));
            var handleImage = handle.GetComponent<Image>();
            handleImage.color = Color.white;
            handleImage.sprite = Builtin("UI/Skin/Knob.psd");
            handle.sizeDelta = new Vector2(20, 20);

            var slider = root.GetComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 0.5f;

            return slider;
        }

        private static Toggle MakeToggle(RectTransform parent, Vector2 topLeft)
        {
            RectTransform root = Place(parent, "Toggle", topLeft, new Vector2(26, 26), typeof(Toggle));

            RectTransform background = Stretch(root, "Background", typeof(CanvasRenderer), typeof(Image));
            var backgroundImage = background.GetComponent<Image>();
            backgroundImage.color = TrackBg;
            backgroundImage.sprite = Builtin("UI/Skin/UISprite.psd");
            backgroundImage.type = Image.Type.Sliced;

            RectTransform checkmark = Stretch(background, "Checkmark", typeof(CanvasRenderer), typeof(Image));
            var checkmarkImage = checkmark.GetComponent<Image>();
            checkmarkImage.color = Accent;
            checkmarkImage.sprite = Builtin("UI/Skin/Checkmark.psd");
            checkmark.offsetMin = new Vector2(3, 3);
            checkmark.offsetMax = new Vector2(-3, -3);

            var toggle = root.GetComponent<Toggle>();
            toggle.targetGraphic = backgroundImage;
            toggle.graphic = checkmarkImage;
            toggle.isOn = false;

            return toggle;
        }

        private static Sprite Builtin(string path)
        {
            // Null is survivable - the control still works, it just draws as a flat
            // rectangle. Worth not throwing over.
            return AssetDatabase.GetBuiltinExtraResource<Sprite>(path);
        }

        // ==================================================================
        // Wiring
        // ==================================================================

        /// <summary>Three parallel arrays rather than one array of tuples, because
        /// a SerializedProperty list of custom classes has to be filled element by
        /// element anyway and this keeps the call site readable in the Inspector's
        /// own terms: names, tabs, pages.</summary>
        private static void AssignCategories(SerializedObject so, string[] names, Button[] tabs, GameObject[] pages)
        {
            var list = so.FindProperty("categories");
            if (list == null)
            {
                Debug.LogWarning("[SettingsPanelBuilder] No 'categories' field - assign the tabs by hand.");
                return;
            }

            list.arraySize = names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                var element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("name").stringValue = names[i];
                element.FindPropertyRelative("tabButton").objectReferenceValue = tabs[i];
                element.FindPropertyRelative("page").objectReferenceValue = pages[i];
            }
        }

        private static void Assign(SerializedObject so, string propertyName, Object value)
        {
            var property = so.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogWarning($"[SettingsPanelBuilder] No field called '{propertyName}' - assign it by hand.");
                return;
            }
            property.objectReferenceValue = value;
        }

        private static bool EnsureApplier()
        {
            if (Object.FindAnyObjectByType<SettingsApplier>() != null) return false;

            var go = new GameObject("SettingsApplier", typeof(SettingsApplier));
            Undo.RegisterCreatedObjectUndo(go, UndoName);
            return true;
        }

        // ==================================================================
        // Building blocks
        // ==================================================================

        /// <summary>
        /// The panel itself, parented to the Canvas and deliberately opted OUT of
        /// any layout the Canvas might be running.
        ///
        /// WHY THE LayoutElement IS NOT OPTIONAL
        /// If the Canvas has a Vertical/Horizontal/Grid Layout Group on it - which
        /// is a perfectly normal way to build a main menu - then adding ANY child
        /// re-flows every existing child to make room. The symptom is bewildering:
        /// you build a settings panel and all your other menu panels change size,
        /// with nothing having touched them. ignoreLayout tells the group this
        /// object is not its business, and the siblings are left alone.
        /// </summary>
        private static RectTransform Panel(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Undo.RegisterCreatedObjectUndo(go, UndoName);
            go.transform.SetParent(parent, false);

            go.AddComponent<LayoutElement>().ignoreLayout = true;

            if (parent.GetComponent<LayoutGroup>() != null)
            {
                Debug.LogWarning($"[SettingsPanelBuilder] '{parent.name}' has a Layout Group on it. The settings panel " +
                                 "has been marked Ignore Layout so it does not disturb your other panels - but if they " +
                                 "changed size just now, that group re-flowed them when the panel was added. Undo, " +
                                 "check the group is what you want, and re-run.", parent);
            }

            // Pinned to all four corners of the canvas, so it fills the screen at
            // any resolution and on any aspect ratio - no size is stored anywhere,
            // which is what stops it being right on one device and wrong on the next.
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);

            go.GetComponent<Image>().color = PanelBg;
            return rect;
        }

        /// <summary>Pins a control to a bottom corner: 0 for left, 1 for right.
        /// A full-screen panel has no fixed height, so anything along the bottom has
        /// to be anchored there rather than positioned from the top.</summary>
        private static void PinToCorner(RectTransform rect, float xAnchor, Vector2 offset)
        {
            if (rect == null) return;

            rect.anchorMin = new Vector2(xAnchor, 0f);
            rect.anchorMax = new Vector2(xAnchor, 0f);
            rect.pivot = new Vector2(xAnchor, 0f);
            rect.anchoredPosition = offset;
        }

        /// <summary>Places a child by its TOP-LEFT corner, in pixels from the
        /// parent's top-left. Predictable, and no layout groups to fight with when
        /// you rearrange things later.</summary>
        private static RectTransform Place(Transform parent, string name, Vector2 topLeft, Vector2 size,
                                           params System.Type[] extra)
        {
            var types = new List<System.Type> { typeof(RectTransform) };
            types.AddRange(extra);

            var go = new GameObject(name, types.ToArray());
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rect.sizeDelta = size;
            return rect;
        }

        /// <summary>A child filling its parent. What a page, and every part of a
        /// Slider or Toggle, wants to be.</summary>
        private static RectTransform Stretch(Transform parent, string name, params System.Type[] extra)
        {
            var types = new List<System.Type> { typeof(RectTransform) };
            types.AddRange(extra);

            var go = new GameObject(name, types.ToArray());
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static TMP_Text Label(Transform parent, string name, string text, Vector2 topLeft,
                                      Vector2 size, float fontSize, Color color)
        {
            RectTransform rect = Place(parent, name, topLeft, size);
            var tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static Button Btn(Transform parent, string name, string text, Vector2 topLeft, Vector2 size)
        {
            RectTransform rect = Place(parent, name, topLeft, size,
                                       typeof(CanvasRenderer), typeof(Image), typeof(Button));

            rect.GetComponent<Image>().color = ButtonBg;

            var button = rect.GetComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();

            TMP_Text label = Label(rect, "Label", text, new Vector2(12, 0), new Vector2(size.x - 24, size.y), 16, TextMain);
            label.alignment = TextAlignmentOptions.Center;

            return button;
        }

        private static GameObject Find(Transform parent, string name)
        {
            Transform found = parent.Find(name);
            return found != null ? found.gameObject : null;
        }

        /// <summary>Returns the ROOT canvas, not whichever Canvas component Unity
        /// happened to find first. A scene with nested canvases - a common way to
        /// keep a busy menu from re-batching everything on every change - would
        /// otherwise get the settings panel parented inside some sub-panel, where it
        /// inherits that panel's anchors, scale and layout.</summary>
        private static Canvas FindOrCreateCanvas()
        {
            var canvas = Object.FindAnyObjectByType<Canvas>();
            if (canvas != null) return canvas.rootCanvas != null ? canvas.rootCanvas : canvas;

            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(go, UndoName);

            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            Debug.Log("[SettingsPanelBuilder] No Canvas in this scene - created one.");
            return canvas;
        }

        /// <summary>Without an EventSystem no UI control in the scene responds to a
        /// click, and nothing says why. Worth checking every time.</summary>
        private static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(go, UndoName);
            Debug.Log("[SettingsPanelBuilder] No EventSystem in this scene - created one. Without it no control works.");
        }
    }
}