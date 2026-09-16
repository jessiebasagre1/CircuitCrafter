using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using CircuitCrafter.Assessment;

namespace CircuitCrafter.Assessment.EditorTools
{
    /// <summary>
    /// Builds the assessment task-list panel and fills in its rows from whatever assessment
    /// manager is already in the scene.
    ///
    /// RUN IT WITH: CircuitCrafter > Build Assessment Task List, with the assessment scene open.
    ///
    /// THE ROWS COME FROM THE SCENE, WHICH IS THE WHOLE POINT OF THIS BEING A BUILDER. Every
    /// module already has its tasks written down - COC 1 and COC 2 in their manager's Stages
    /// list, COC 3 in its Steps. Typing those out again into a second list is how the checklist
    /// ends up saying "Crimp both cables" for a module that renamed that stage six weeks ago,
    /// and how a task id gets a trailing space. So the tool reads them, and a rebuild is how you
    /// resync after changing the manager.
    ///
    /// AssessmentTaskList itself knows nothing about any of those managers - it only watches
    /// TaskManager, so it works in any module. All the module-specific knowledge is in here, in
    /// a throwaway editor tool, where it is read once and never shipped.
    ///
    /// Palette and helpers are a deliberate copy of COC4ReportScreenBuilder's, for the reason
    /// its own header gives: a shared kit means restyling one screen quietly restyles the rest.
    /// </summary>
    public static class AssessmentTaskListBuilder
    {
        private static readonly Color PanelBg = new Color32(0x16, 0x25, 0x6E, 0xE6);
        private static readonly Color HeaderBg = new Color32(0x2A, 0x3A, 0xB5, 0xFF);
        private static readonly Color RowBg = new Color32(0x1B, 0x2A, 0x7A, 0x00);
        private static readonly Color TextPrimary = new Color32(0xFF, 0xFF, 0xFF, 0xFF);
        private static readonly Color TextSecondary = new Color32(0xA9, 0xC6, 0xF0, 0xFF);
        private static readonly Color TextDone = new Color32(0x6E, 0x85, 0xA8, 0xFF);
        private static readonly Color EdgeCyan = new Color32(0x5F, 0xD4, 0xFF, 0xFF);
        private static readonly Color BoxPending = new Color32(0x6B, 0x7A, 0xA8, 0xFF);
        private static readonly Color BoxCurrent = new Color32(0x5F, 0xD4, 0xFF, 0xFF);
        private static readonly Color BoxDone = new Color32(0x3D, 0xD5, 0x83, 0xFF);
        private static readonly Color RowHighlightOn = new Color32(0x2A, 0x3A, 0xB5, 0x80);

        private const float SizeTitle = 26f;
        private const float SizeBody = 22f;
        private const float SizeCaption = 20f;

        private const float PanelWidth = 420f;
        private const float RowHeight = 44f;
        private const float BoxSize = 26f;

        // ==================================================================

        [MenuItem("CircuitCrafter/Build Assessment Task List", false, 46)]
        public static void Build()
        {
            Canvas canvas = FindOrCreateCanvas();
            if (canvas == null) return;

            string moduleId;
            List<(string taskId, string label)> tasks = ReadTasksFromScene(out moduleId);

            if (tasks.Count == 0)
            {
                // Built anyway, with placeholders. Refusing would leave you with nothing to look
                // at and no idea what shape it was going to be - and the panel is still correct,
                // it just needs its rows typed in.
                Debug.LogWarning(
                    "[AssessmentTaskListBuilder] No assessment manager with a task list was found " +
                    "in this scene, so the panel has been built with three placeholder rows.\n\n" +
                    "Fill in Rows on the AssessmentTaskList component by hand, or open the scene " +
                    "that has the manager and run this again. Every row needs a Task Id that " +
                    "matches one registered with TaskManager.");

                tasks.Add(("", "First task"));
                tasks.Add(("", "Second task"));
                tasks.Add(("", "Third task"));
            }

            GameObject panel = BuildPanel(canvas, moduleId, tasks);

            Undo.RegisterCreatedObjectUndo(panel, "Build Assessment Task List");
            Selection.activeGameObject = panel;
            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);

            Debug.Log(
                $"[AssessmentTaskListBuilder] Built '{panel.name}' under '{canvas.name}' with " +
                $"{tasks.Count} row(s), for module '{moduleId}'.\n\n" +
                "NEXT:\n" +
                "  1. Drag it where you want it. It is anchored TOP-LEFT and inset 24px - move it " +
                "to the other side if it fights your HUD.\n" +
                "  2. Check the rows read the way you want. The labels come from the manager's " +
                "stage names, which were written for the report, not for a checklist - shorten " +
                "any that are long-winded. Changing a LABEL is free; changing a TASK ID breaks " +
                "the row.\n" +
                "  3. Press Play in assessment mode and right-click the component > Report task " +
                "list. It names any row whose task id TaskManager has never heard of - the one " +
                "failure that otherwise looks completely normal and simply never ticks.\n\n" +
                "The panel hides itself outside assessment mode, so it is safe to leave in a " +
                "scene that is also played as story mode.",
                panel);
        }

        // ==================================================================
        // Reading the module's own task list
        // ==================================================================

        /// <summary>
        /// Pulls the ordered tasks out of whichever assessment manager is in this scene.
        ///
        /// Each module is handled explicitly rather than through reflection or a shared
        /// interface. Reflection would turn a renamed field into a checklist that silently
        /// comes back empty; a shared interface would mean editing four working managers to
        /// add a panel. An if-chain in a build tool is honest about what it knows, and fails
        /// by telling you which module it did not recognise.
        /// </summary>
        private static List<(string taskId, string label)> ReadTasksFromScene(out string moduleId)
        {
            var tasks = new List<(string, string)>();
            moduleId = "COC1";

            // ---- COC 1 ----
            var coc1 = Object.FindAnyObjectByType<COC1AssessmentManager>(FindObjectsInactive.Include);
            if (coc1 != null && coc1.stages != null && coc1.stages.Count > 0)
            {
                moduleId = "COC1";
                foreach (var stage in coc1.stages)
                {
                    if (stage == null) continue;
                    tasks.Add((stage.taskId, Readable(stage.displayName, stage.taskId)));
                }
                return tasks;
            }

            // ---- COC 2 ----
            var coc2 = Object.FindAnyObjectByType<CSSSimulation.Networking.COC2AssessmentManager>(
                FindObjectsInactive.Include);
            if (coc2 != null && coc2.stages != null && coc2.stages.Count > 0)
            {
                moduleId = "COC2";
                foreach (var stage in coc2.stages)
                {
                    if (stage == null) continue;
                    tasks.Add((stage.taskId, Readable(stage.displayName, stage.taskId)));
                }
                return tasks;
            }

            // ---- COC 3 and COC 4, and anything else driven by a recorder ----
            //
            // These do not use TaskManager the way COC 1 and COC 2 do. COC 3 grades nine steps
            // that close through AssessmentRecorder.CompleteStage with manual ids like
            // "coc3_static_ip"; COC 4 names a stage per ticket. Either way the recorder's own
            // Stages list is the definition of what is being marked, so it is the right thing
            // to read - and StageSpec.Key already resolves to the task id where there is one and
            // the manual id otherwise, which is exactly the string AssessmentTaskList watches.
            var recorder = Object.FindAnyObjectByType<AssessmentRecorder>(FindObjectsInactive.Include);
            if (recorder != null && recorder.StageSpecs != null && recorder.StageSpecs.Count > 0)
            {
                moduleId = string.IsNullOrWhiteSpace(recorder.ModuleId) ? "COC3" : recorder.ModuleId;

                foreach (var spec in recorder.StageSpecs)
                {
                    if (spec == null) continue;
                    tasks.Add((spec.Key, Readable(spec.displayName, spec.Key)));
                }
                return tasks;
            }

            // A recorder with an EMPTY stage list is worth saying out loud. It is not the same
            // as no recorder: the scene is set up for one and nobody filled it in, so the
            // assessment grades nothing and this panel would have nothing to show either.
            if (recorder != null)
            {
                moduleId = string.IsNullOrWhiteSpace(recorder.ModuleId) ? "COC3" : recorder.ModuleId;
                Debug.LogWarning($"[AssessmentTaskListBuilder] The AssessmentRecorder on " +
                                 $"'{recorder.name}' has an EMPTY Stages list, so there is nothing " +
                                 "to build rows from - and nothing for the assessment to grade " +
                                 "either. Fill in its Stages first, then run this again.", recorder);
            }

            return tasks;
        }

        /// <summary>A stage name written for the report, or the task id if nobody wrote one.</summary>
        private static string Readable(string displayName, string taskId)
        {
            if (!string.IsNullOrWhiteSpace(displayName) && displayName != "Stage")
                return displayName.Trim();

            return string.IsNullOrWhiteSpace(taskId) ? "Task" : taskId.Trim();
        }

        // ==================================================================
        // The panel
        // ==================================================================

        private static GameObject BuildPanel(Canvas canvas, string moduleId,
                                             List<(string taskId, string label)> tasks)
        {
            RectTransform panel = NewRect(UniqueName(canvas, "AssessmentTaskList"), canvas.transform);

            // Top-left, and a fixed width. Not stretched: this is a rail beside the play area,
            // not a screen, and it has to leave the machine visible.
            panel.anchorMin = new Vector2(0f, 1f);
            panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 1f);
            panel.anchoredPosition = new Vector2(24f, -24f);
            panel.sizeDelta = new Vector2(PanelWidth, 0f);

            AddImage(panel, PanelBg);

            VerticalLayoutGroup outer = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            outer.spacing = 0f;
            outer.childControlWidth = true;
            outer.childControlHeight = true;
            outer.childForceExpandWidth = true;
            outer.childForceExpandHeight = false;

            // The panel grows and shrinks with its rows, so collapsing actually gives the space
            // back instead of leaving an empty box.
            ContentSizeFitter fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            Button toggle;
            RectTransform arrow;
            TMP_Text title, summary;
            BuildHeader(panel, out toggle, out arrow, out title, out summary);

            RectTransform body = BuildBody(panel);

            var list = panel.gameObject.AddComponent<AssessmentTaskList>();
            var rows = new List<AssessmentTaskList.Row>();

            for (int i = 0; i < tasks.Count; i++)
            {
                AssessmentTaskRow view = BuildRow(body, i + 1, tasks[i].label);
                rows.Add(new AssessmentTaskList.Row
                {
                    taskId = (tasks[i].taskId ?? "").Trim(),
                    label = tasks[i].label,
                    view = view,
                });
            }

            list.moduleId = moduleId;
            list.rows = rows;
            list.panelRoot = panel.gameObject;
            list.bodyRoot = body.gameObject;
            list.toggleButton = toggle;
            list.toggleArrow = arrow;
            list.summaryText = summary;
            list.titleText = title;

            title.text = "Assessment tasks";
            summary.text = $"0 / {tasks.Count}";

            EditorUtility.SetDirty(list);
            return panel.gameObject;
        }

        private static void BuildHeader(RectTransform parent, out Button toggle, out RectTransform arrow,
                                        out TMP_Text title, out TMP_Text summary)
        {
            RectTransform header = NewRect("Header", parent);
            Fixed(header, 62f);

            Image bg = AddImage(header, HeaderBg, raycastTarget: true);
            AddTopEdge(header);

            RectTransform inner = NewRect("Inner", header);
            Stretch(inner);
            inner.offsetMin = new Vector2(18f, 0f);
            inner.offsetMax = new Vector2(-14f, 0f);

            HorizontalLayoutGroup h = inner.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            h.childAlignment = TextAnchor.MiddleLeft;

            title = AddText(inner, "Title", "Assessment tasks", SizeTitle, TextPrimary, FontStyles.Bold);
            Flexible(title.rectTransform);

            summary = AddText(inner, "Summary", "0 / 0", SizeBody, EdgeCyan, FontStyles.Bold);
            summary.alignment = TextAlignmentOptions.MidlineRight;
            Width(summary.rectTransform, 74f);

            arrow = AddText(inner, "Arrow", "▼", SizeCaption, TextSecondary).rectTransform;
            Width(arrow, 26f);

            // The WHOLE header is the button, not a small chevron. A 26px glyph is an unfair
            // target with a thumb, and there is nothing else on this bar to hit by mistake.
            toggle = header.gameObject.AddComponent<Button>();
            toggle.targetGraphic = bg;

            ColorBlock colors = toggle.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            colors.selectedColor = Color.white;
            toggle.colors = colors;
        }

        private static RectTransform BuildBody(RectTransform parent)
        {
            RectTransform body = NewRect("Body", parent);

            VerticalLayoutGroup v = body.gameObject.AddComponent<VerticalLayoutGroup>();
            v.padding = new RectOffset(14, 14, 12, 14);
            v.spacing = 4f;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;

            ContentSizeFitter fitter = body.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            return body;
        }

        /// <summary>One row: highlight plate, checkbox, tick, label.</summary>
        private static AssessmentTaskRow BuildRow(RectTransform parent, int number, string label)
        {
            RectTransform row = NewRect($"Row{number:00}", parent);
            Fixed(row, RowHeight);

            Image highlight = AddImage(row, RowHighlightOn);
            highlight.color = RowBg;   // starts off; the component drives it

            RectTransform inner = NewRect("Inner", row);
            Stretch(inner);
            inner.offsetMin = new Vector2(10f, 0f);
            inner.offsetMax = new Vector2(-10f, 0f);

            HorizontalLayoutGroup h = inner.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 12f;
            h.childControlWidth = true;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = false;
            h.childAlignment = TextAnchor.MiddleLeft;

            // ---- the box ----
            RectTransform box = NewRect("CheckBox", inner);
            Width(box, BoxSize);
            Fixed(box, BoxSize);
            Image boxImage = AddImage(box, BoxPending);

            // ---- the tick, drawn as a glyph rather than a sprite ----
            // A sprite would be one more asset to import, name, set to Sprite (2D and UI), and
            // remember to ship. A character needs none of that and restyles with the font.
            RectTransform markRect = NewRect("CheckMark", box);
            Stretch(markRect);
            TextMeshProUGUI mark = markRect.gameObject.AddComponent<TextMeshProUGUI>();
            mark.text = "✓";
            mark.fontSize = BoxSize * 0.9f;
            mark.color = new Color32(0x0B, 0x14, 0x3A, 0xFF);
            mark.alignment = TextAlignmentOptions.Center;
            mark.raycastTarget = false;
            if (TMP_Settings.defaultFontAsset != null) mark.font = TMP_Settings.defaultFontAsset;

            // The row component switches an IMAGE on and off, so the tick needs to be one.
            // An Image with no sprite draws a solid block, so it is left transparent and the
            // glyph above rides on top of it - the Image is the switch, the text is the tick.
            Image markSwitch = markRect.gameObject.AddComponent<Image>();
            markSwitch.color = new Color(1f, 1f, 1f, 0f);
            markSwitch.raycastTarget = false;

            // ---- the label ----
            TMP_Text text = AddText(inner, "Label", label, SizeBody, TextSecondary);
            text.richText = true;                 // the strikethrough is a <s> tag
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            Flexible(text.rectTransform);

            var view = row.gameObject.AddComponent<AssessmentTaskRow>();
            view.checkBox = boxImage;
            view.checkMark = markSwitch;
            view.label = text;
            view.rowHighlight = highlight;
            view.pendingBox = BoxPending;
            view.currentBox = BoxCurrent;
            view.doneBox = BoxDone;
            view.pendingText = TextSecondary;
            view.currentText = TextPrimary;
            view.doneText = TextDone;
            view.highlightOn = RowHighlightOn;
            view.highlightOff = RowBg;

            // Hidden until the list says otherwise, so a freshly built panel reads as "nothing
            // done yet" rather than "everything done".
            markSwitch.enabled = false;

            EditorUtility.SetDirty(view);
            return view;
        }

        // ==================================================================
        // Small builders - a deliberate copy of COC4ReportScreenBuilder's
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

            var go = new GameObject("HUDCanvas",
                                    typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));

            Canvas made = go.GetComponent<Canvas>();
            made.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            Undo.RegisterCreatedObjectUndo(go, "Create Canvas");
            Debug.Log("[AssessmentTaskListBuilder] No canvas in this scene, so one was created.", go);
            return made;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            if (parent != null) rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        private static void AddTopEdge(RectTransform plate, float height = 3f)
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

        private static void Flexible(RectTransform rect)
        {
            LayoutElement le = rect.GetComponent<LayoutElement>();
            if (le == null) le = rect.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
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