using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using CircuitCrafter.Repair;

namespace CircuitCrafter.Repair.EditorTools
{
    /// <summary>
    /// Builds the three repair-site panels and wires every field, from one menu
    /// click:
    ///
    ///     CircuitCrafter &gt; Build COC4 Repair Panels
    ///
    /// PUT THIS FILE IN a folder named Editor.
    ///
    /// WHY THIS EXISTS
    /// The three panels need roughly sixty Inspector assignments between them -
    /// five buttons and five labels that must be in matching order, three panel
    /// roots, six text fields, four close buttons. Every one of those is a chance
    /// to cross two lists over or leave a slot empty, and both failures are
    /// silent: the panel opens looking fine and behaves wrongly.
    ///
    /// Doing it in code makes the ordering guaranteed rather than careful.
    ///
    /// IT ALSO FILLS IN OBSERVATION SETS. Every TicketObservationSet asset in the
    /// project is found and assigned to the Examine panel - which is the exact
    /// step that is easiest to forget, and the one whose symptom is "no buttons
    /// appear and nothing says why".
    ///
    /// WHAT YOU GET is functional, not pretty: plain rectangles, legible text,
    /// sensible sizes. Restyle it afterwards - moving and recolouring things does
    /// not break any of the wiring.
    ///
    /// SAFE TO RE-RUN: a panel that already exists is left alone. Delete one and
    /// re-run to rebuild just that one. Ctrl+Z undoes the whole thing.
    /// </summary>
    public static class COC4PanelBuilder
    {
        // Colours chosen to read as a technician's tool, not a game menu.
        private static readonly Color PanelBg = new Color(0.11f, 0.13f, 0.12f, 0.97f);
        private static readonly Color ButtonBg = new Color(0.18f, 0.22f, 0.20f, 1f);
        private static readonly Color ToolbarBg = new Color(0.08f, 0.10f, 0.09f, 0.92f);
        private static readonly Color TextMain = new Color(0.91f, 0.93f, 0.91f, 1f);
        private static readonly Color TextDim = new Color(0.66f, 0.71f, 0.68f, 1f);
        private static readonly Color Accent = new Color(0.31f, 0.76f, 0.60f, 1f);

        private const string UndoName = "Build COC4 Repair Panels";

        [MenuItem("CircuitCrafter/Build COC4 Repair Panels")]
        public static void Build()
        {
            Canvas canvas = FindOrCreateCanvas();
            EnsureEventSystem();

            var built = new List<string>();

            GameObject examine = BuildExamine(canvas.transform, built);
            GameObject diagnose = BuildDiagnose(canvas.transform, built);
            GameObject verify = BuildVerify(canvas.transform, built);

            BuildToolbar(canvas.transform, examine, diagnose, verify, built);

            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);

            if (built.Count == 0)
            {
                Debug.Log("[COC4PanelBuilder] Everything already exists in this scene - nothing was created. Delete a panel and re-run to rebuild just that one.");
                return;
            }

            Debug.Log($"[COC4PanelBuilder] Built: {string.Join(", ", built)}.\n\n" +
                      "STILL TO DO BY HAND:\n" +
                      "  1. Set each TicketFault's Verification Label (e.g. \"Display output\").\n" +
                      "  2. Set your Return To Hub button's Interactable to OFF, then add it to the\n" +
                      "     verify panel's On Verify Passed event with the tick ON.\n" +
                      "  3. Untick the old TicketDiagnosticPanelView so it stops auto-opening.\n" +
                      "  4. Restyle freely - moving and recolouring breaks none of the wiring.");
        }

        // ==================================================================
        // Panels
        // ==================================================================

        private static GameObject BuildExamine(Transform canvas, List<string> built)
        {
            GameObject existing = Find(canvas, "ExaminePanel");
            if (existing != null) return existing;

            RectTransform panel = Panel(canvas, "ExaminePanel", new Vector2(880, 560));
            Label(panel, "Title", "EXAMINE THE MACHINE", new Vector2(24, 18), new Vector2(500, 30), 20, TextMain);
            Label(panel, "Hint", "Each check costs time. Choose what is worth looking at.",
                  new Vector2(24, 48), new Vector2(500, 24), 14, TextDim);

            var buttons = new List<Button>();
            var labels = new List<TMP_Text>();
            for (int i = 0; i < 5; i++)
            {
                Button b = Btn(panel, $"Action{i + 1}", "Action " + (i + 1),
                               new Vector2(24, 86 + i * 56), new Vector2(400, 48), out TMP_Text t);
                buttons.Add(b);
                labels.Add(t);
            }

            TMP_Text findings = Label(panel, "FindingsText", "Nothing checked yet.",
                                      new Vector2(452, 86), new Vector2(404, 380), 15, TextMain);
            findings.alignment = TextAlignmentOptions.TopLeft;

            TMP_Text status = Label(panel, "StatusText", "0 check(s), 00:00 spent",
                                    new Vector2(24, 500), new Vector2(400, 26), 14, Accent);

            Button close = Btn(panel, "CloseButton", "Close", new Vector2(752, 16), new Vector2(104, 34), out _);

            var component = panel.gameObject.AddComponent<SiteObservationPanel>();
            var so = new SerializedObject(component);
            so.FindProperty("panelRoot").objectReferenceValue = panel.gameObject;
            AssignList(so.FindProperty("actionButtons"), buttons);
            AssignList(so.FindProperty("actionLabels"), labels);
            so.FindProperty("findingsText").objectReferenceValue = findings;
            so.FindProperty("statusText").objectReferenceValue = status;
            so.FindProperty("closeButton").objectReferenceValue = close;
            AssignAllObservationSets(so.FindProperty("observationSets"));
            so.ApplyModifiedPropertiesWithoutUndo();

            built.Add("ExaminePanel");
            return panel.gameObject;
        }

        private static GameObject BuildDiagnose(Transform canvas, List<string> built)
        {
            GameObject existing = Find(canvas, "DiagnosePanel");
            if (existing != null) return existing;

            RectTransform panel = Panel(canvas, "DiagnosePanel", new Vector2(780, 540));
            Label(panel, "Title", "PROBABLE CAUSE", new Vector2(24, 18), new Vector2(500, 30), 20, TextMain);
            Label(panel, "Hint", "Name the cause you can point to evidence for.",
                  new Vector2(24, 48), new Vector2(600, 24), 14, TextDim);

            var buttons = new List<Button>();
            var labels = new List<TMP_Text>();
            for (int i = 0; i < 4; i++)
            {
                Button b = Btn(panel, $"Option{i + 1}", "Option " + (i + 1),
                               new Vector2(24, 86 + i * 60), new Vector2(732, 52), out TMP_Text t);
                buttons.Add(b);
                labels.Add(t);
            }

            TMP_Text feedback = Label(panel, "FeedbackText", "",
                                      new Vector2(24, 340), new Vector2(732, 176), 15, TextMain);
            feedback.alignment = TextAlignmentOptions.TopLeft;

            Button close = Btn(panel, "CloseButton", "Close", new Vector2(652, 16), new Vector2(104, 34), out _);

            var component = panel.gameObject.AddComponent<TicketDiagnosisPanel>();
            var so = new SerializedObject(component);
            so.FindProperty("panelRoot").objectReferenceValue = panel.gameObject;
            AssignList(so.FindProperty("optionButtons"), buttons);
            AssignList(so.FindProperty("optionLabels"), labels);
            so.FindProperty("feedbackText").objectReferenceValue = feedback;
            so.FindProperty("closeButton").objectReferenceValue = close;
            so.ApplyModifiedPropertiesWithoutUndo();

            built.Add("DiagnosePanel");
            return panel.gameObject;
        }

        private static GameObject BuildVerify(Transform canvas, List<string> built)
        {
            GameObject existing = Find(canvas, "VerifyPanel");
            if (existing != null) return existing;

            RectTransform panel = Panel(canvas, "VerifyPanel", new Vector2(640, 500));
            Label(panel, "Title", "POWER ON AND TEST", new Vector2(24, 18), new Vector2(500, 30), 20, TextMain);

            TMP_Text output = Label(panel, "OutputText", "Ready to test. Press the power button.",
                                    new Vector2(24, 66), new Vector2(592, 330), 15, TextMain);
            output.alignment = TextAlignmentOptions.TopLeft;
            output.font = null;   // leave as project default; a mono face suits this panel if you have one

            Button run = Btn(panel, "RunButton", "Power on and test",
                             new Vector2(24, 412), new Vector2(260, 46), out _);
            Button close = Btn(panel, "CloseButton", "Close", new Vector2(512, 16), new Vector2(104, 34), out _);

            var component = panel.gameObject.AddComponent<RepairVerificationPanel>();
            var so = new SerializedObject(component);
            so.FindProperty("panelRoot").objectReferenceValue = panel.gameObject;
            so.FindProperty("outputText").objectReferenceValue = output;
            so.FindProperty("runButton").objectReferenceValue = run;
            so.FindProperty("closeButton").objectReferenceValue = close;
            so.ApplyModifiedPropertiesWithoutUndo();

            built.Add("VerifyPanel");
            return panel.gameObject;
        }

        /// <summary>Three always-visible openers, bottom-left. Replace them with
        /// interactions on the machine itself once the loop works - walking over
        /// to it is a better first decision than a button on the HUD.</summary>
        private static void BuildToolbar(Transform canvas, GameObject examine, GameObject diagnose,
                                         GameObject verify, List<string> built)
        {
            if (Find(canvas, "RepairToolbar") != null) return;

            var go = new GameObject("RepairToolbar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Undo.RegisterCreatedObjectUndo(go, UndoName);
            go.transform.SetParent(canvas, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = new Vector2(20f, 20f);
            rect.sizeDelta = new Vector2(560f, 60f);
            go.GetComponent<Image>().color = ToolbarBg;

            Wire(Btn(rect, "ExamineButton", "Examine machine", new Vector2(10, 8), new Vector2(170, 44), out _),
                 examine, "Open");
            Wire(Btn(rect, "DiagnoseButton", "Diagnose", new Vector2(190, 8), new Vector2(170, 44), out _),
                 diagnose, "Open");
            Wire(Btn(rect, "VerifyButton", "Test the machine", new Vector2(370, 8), new Vector2(180, 44), out _),
                 verify, "Open");

            built.Add("RepairToolbar");
        }

        /// <summary>Adds a persistent OnClick call, the same thing dragging an
        /// object into the Inspector's event list does.</summary>
        private static void Wire(Button button, GameObject target, string methodName)
        {
            if (button == null || target == null) return;

            Component component = null;
            foreach (var c in target.GetComponents<MonoBehaviour>())
            {
                if (c != null && c.GetType().GetMethod(methodName) != null) { component = c; break; }
            }

            if (component == null)
            {
                Debug.LogWarning($"[COC4PanelBuilder] Couldn't find {methodName}() on '{target.name}' - wire that button by hand.");
                return;
            }

            UnityEditor.Events.UnityEventTools.AddVoidPersistentListener(
                button.onClick,
                (UnityEngine.Events.UnityAction)System.Delegate.CreateDelegate(
                    typeof(UnityEngine.Events.UnityAction), component, methodName));
        }

        // ==================================================================
        // Building blocks
        // ==================================================================

        private static RectTransform Panel(Transform parent, string name, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Undo.RegisterCreatedObjectUndo(go, UndoName);
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;

            go.GetComponent<Image>().color = PanelBg;
            return rect;
        }

        /// <summary>Places a child by its TOP-LEFT corner, in pixels from the
        /// parent's top-left. Predictable, and it means no layout groups to fight
        /// with when you rearrange things later.</summary>
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

        private static Button Btn(Transform parent, string name, string text, Vector2 topLeft,
                                  Vector2 size, out TMP_Text label)
        {
            RectTransform rect = Place(parent, name, topLeft, size,
                                       typeof(CanvasRenderer), typeof(Image), typeof(Button));

            rect.GetComponent<Image>().color = ButtonBg;

            var button = rect.GetComponent<Button>();
            button.targetGraphic = rect.GetComponent<Image>();

            label = Label(rect, "Label", text, new Vector2(12, 0), new Vector2(size.x - 24, size.y), 15, TextMain);
            label.alignment = TextAlignmentOptions.MidlineLeft;

            return button;
        }

        // ==================================================================
        // Wiring helpers
        // ==================================================================

        private static void AssignList<T>(SerializedProperty property, List<T> items) where T : Object
        {
            if (property == null) return;

            property.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
        }

        /// <summary>Every observation set in the project. This is the assignment
        /// people forget, and its failure mode is a panel with no buttons and no
        /// explanation - so it is done automatically.</summary>
        private static void AssignAllObservationSets(SerializedProperty property)
        {
            if (property == null) return;

            var sets = new List<Object>();
            foreach (string guid in AssetDatabase.FindAssets("t:TicketObservationSet"))
            {
                var set = AssetDatabase.LoadAssetAtPath<TicketObservationSet>(AssetDatabase.GUIDToAssetPath(guid));
                if (set != null) sets.Add(set);
            }

            property.arraySize = sets.Count;
            for (int i = 0; i < sets.Count; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = sets[i];

            Debug.Log($"[COC4PanelBuilder] Assigned {sets.Count} observation set(s) to the Examine panel.");
        }

        private static GameObject Find(Transform parent, string name)
        {
            Transform found = parent.Find(name);
            return found != null ? found.gameObject : null;
        }

        private static Canvas FindOrCreateCanvas()
        {
            var canvas = Object.FindAnyObjectByType<Canvas>();
            if (canvas != null) return canvas;

            var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(go, UndoName);

            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            Debug.Log("[COC4PanelBuilder] No Canvas in this scene - created one.");
            return canvas;
        }

        /// <summary>Without an EventSystem no UI button in the scene responds to a
        /// click, and nothing says why. Worth checking every time.</summary>
        private static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(go, UndoName);
            Debug.Log("[COC4PanelBuilder] No EventSystem in this scene - created one. Without it no button works.");
        }
    }
}
