// AssessmentReportBuilder.cs
//
// EDITOR ONLY. Put this file in a folder called Editor (Assets/Scripts/Editor is fine).
// Unity refuses to build a player if an editor script sits anywhere else.
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CircuitCrafter.Assessment
{
    /// <summary>
    /// Builds the end-of-run assessment report panel, with a scroll view, and wires it to whichever
    /// assessment manager is in the scene.
    ///
    /// WHY THE SCROLL VIEW IS THE WHOLE POINT
    ///
    /// AssessmentManagerBase.BuildReport writes a report that has no fixed length. A clean run is
    /// about twelve lines; a run with eight mistakes and five competency criteria is sixty, and
    /// every one of those lines is something the trainee is meant to read and learn from. Put that
    /// into a plain TMP_Text on a panel and the overflow is simply not drawn - no scrollbar, no
    /// clipping indicator, nothing to suggest anything is missing. The learner sees a report that
    /// ends mid-sentence and has no way to know there was more, and the assessor reviewing it never
    /// sees the mistakes list at all.
    ///
    /// That failure is invisible in testing, because a test run is almost always a short one.
    ///
    /// WHAT IT WIRES
    ///
    /// AssessmentManagerBase keeps Report Panel, Report Text and Return To Menu Button as protected
    /// [SerializeField]s, so they cannot be assigned from ordinary code - only the Inspector or a
    /// SerializedObject can reach them. This uses a SerializedObject, which is the same thing the
    /// Inspector does, so the result is identical to having dragged them in by hand.
    ///
    /// COC4 keeps its report on COC4AssessmentReportView instead, with differently named fields.
    /// Both are handled; whichever is in the scene gets filled in.
    ///
    /// USE: Tools > CircuitCrafter > Assessment Report Builder, once per scene.
    /// </summary>
    public static class AssessmentReportBuilder
    {
        const string CanvasName = "AssessmentReportCanvas";

        [MenuItem("Tools/CircuitCrafter/Assessment Report Builder")]
        public static void Build()
        {
            GameObject existing = GameObject.Find(CanvasName);
            if (existing != null)
            {
                Selection.activeGameObject = existing;
                EditorGUIUtility.PingObject(existing);
                Debug.LogWarning("[AssessmentReportBuilder] This scene already has an '" + CanvasName +
                                 "'. It has been selected rather than a second one being made. " +
                                 "Delete it first if you want a clean rebuild.", existing);
                return;
            }

            // ---------------------------------------------------------------- canvas

            GameObject canvasGO = new GameObject(CanvasName,
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(canvasGO, "Build Assessment Report");

            Canvas canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // High, because this is terminal. The run is over, and nothing else on screen - a HUD,
            // a waypoint arrow, the AI tutor button - should be able to draw over the result.
            canvas.sortingOrder = 400;

            CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            // ---------------------------------------------------------------- dimmer + panel

            // Covers the scene behind, and eats clicks aimed at whatever is under it. Without it a
            // learner can keep operating the station through the gaps around the report.
            GameObject dim = Panel("Dimmer", canvasGO.transform, new Color(0f, 0f, 0f, 0.72f));
            Stretch(dim.GetComponent<RectTransform>());

            GameObject panelGO = Panel("ReportPanel", dim.transform, new Color(0.10f, 0.11f, 0.14f, 1f));
            RectTransform panel = panelGO.GetComponent<RectTransform>();
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(1280, 860);

            TextMeshProUGUI title = Label("Title", panelGO.transform, "Assessment Report", 44,
                                          TextAlignmentOptions.Left);
            Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -32),
                   new Vector2(700, 56));

            // ---------------------------------------------------------------- the scroll view

            GameObject scrollGO = Panel("ReportScroll", panelGO.transform, new Color(0, 0, 0, 0.30f));
            RectTransform scrollRT = scrollGO.GetComponent<RectTransform>();
            Stretch(scrollRT);
            scrollRT.offsetMin = new Vector2(32, 132);    // room for the button row
            scrollRT.offsetMax = new Vector2(-32, -104);  // room for the title

            ScrollRect scroll = scrollGO.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.scrollSensitivity = 40f;

            GameObject viewportGO = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportGO.transform.SetParent(scrollGO.transform, false);
            RectTransform viewport = viewportGO.GetComponent<RectTransform>();
            Stretch(viewport);
            viewport.offsetMax = new Vector2(-28, 0);     // leaves the strip the scrollbar sits in

            GameObject contentGO = new GameObject("Content", typeof(RectTransform));
            contentGO.transform.SetParent(viewportGO.transform, false);
            RectTransform content = contentGO.GetComponent<RectTransform>();

            // Anchored to the top and growing downward. Anchored anywhere else, a report that grows
            // past one screen creeps up out of the viewport instead of extending below it.
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            VerticalLayoutGroup rows = contentGO.AddComponent<VerticalLayoutGroup>();
            rows.padding = new RectOffset(28, 28, 24, 24);
            rows.childControlWidth = true;
            rows.childControlHeight = true;
            rows.childForceExpandWidth = true;
            rows.childForceExpandHeight = false;

            ContentSizeFitter fitter = contentGO.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            TextMeshProUGUI reportText = Label("ReportText", contentGO.transform,
                "The report appears here when the run finishes.", 26, TextAlignmentOptions.TopLeft);
            reportText.rectTransform.anchorMin = new Vector2(0, 1);
            reportText.rectTransform.anchorMax = new Vector2(1, 1);

            scroll.viewport = viewport;
            scroll.content = content;

            // ---------------------------------------------------------------- scrollbar

            // Not decoration. A scroll view with no visible bar looks exactly like a short report
            // that happens to end where it ends - the bar is the only thing telling the reader
            // there is more below, and this report is the one place that matters.
            Scrollbar bar = Scrollbar("Scrollbar", scrollGO.transform);
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

            // ---------------------------------------------------------------- buttons

            GameObject backGO = Panel("BackToMenuButton", panelGO.transform,
                                      new Color(0.16f, 0.45f, 0.85f));
            Anchor(backGO.GetComponent<RectTransform>(), new Vector2(1, 0), new Vector2(1, 0),
                   new Vector2(-32, 32), new Vector2(300, 80));
            Button backButton = backGO.AddComponent<Button>();
            Stretch(Label("Label", backGO.transform, "Back to Menu", 30,
                          TextAlignmentOptions.Center).rectTransform);

            // Left inactive: the manager switches it on when the run ends, and a report panel that
            // is on screen from the first frame of the scene is the giveaway that it never was.
            dim.SetActive(false);

            // ---------------------------------------------------------------- wire it up

            List<string> notes = new List<string>();
            int wired = WireManagers(dim, reportText, backButton, notes);

            EditorUtility.SetDirty(canvasGO);
            Selection.activeGameObject = panelGO;
            EditorGUIUtility.PingObject(panelGO);

            string msg = "[AssessmentReportBuilder] Built '" + CanvasName + "' at sort order " +
                         canvas.sortingOrder + ", with a scroll view sized for a long report.";

            if (wired == 0)
                msg += "\n\nNo assessment manager was found in this scene, so nothing was wired. " +
                       "Drag 'Dimmer' into Report Panel, 'ReportText' into Report Text and " +
                       "'BackToMenuButton' into Return To Menu Button on your manager once it is " +
                       "in the scene.";
            else
                foreach (string n in notes) msg += "\n  " + n;

            msg += "\n\nTwo things worth knowing:" +
                   "\n  - The report is laid out in aligned columns ('Final score:     7 of 10'), " +
                   "and those only line up in a MONOSPACE font. Assign a monospace TMP font asset " +
                   "to ReportText, or the columns will wander." +
                   "\n  - 'Dimmer' is what gets switched on and off, not the panel, so the report " +
                   "always arrives with the scene greyed out behind it and clicks cannot reach " +
                   "the station underneath.";

            Debug.Log(msg, panelGO);
        }

        /// <summary>
        /// Fills in the manager's fields the way the Inspector would.
        ///
        /// Those fields are protected [SerializeField], which means no amount of ordinary C# can
        /// touch them from outside the class - assigning them needs a SerializedObject, which is
        /// the same mechanism the Inspector itself uses. That is why this is an editor script and
        /// not something the panel could do for itself at runtime.
        /// </summary>
        static int WireManagers(GameObject panelRoot, TMP_Text reportText, Button backButton,
                                List<string> notes)
        {
            int wired = 0;

            AssessmentManagerBase[] managers = Object.FindObjectsByType<AssessmentManagerBase>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (AssessmentManagerBase m in managers)
            {
                if (m == null) continue;

                SerializedObject so = new SerializedObject(m);
                bool any = Set(so, "reportPanel", panelRoot) |
                           Set(so, "reportText", reportText) |
                           Set(so, "returnToMenuButton", backButton);
                so.ApplyModifiedProperties();

                if (!any) continue;
                wired++;
                notes.Add("Wired " + m.GetType().Name + " on '" + m.name + "'.");
            }

            // COC4 keeps its report on a separate view with its own field names, so it needs its
            // own pass rather than being made to look like the others.
            MonoBehaviour[] all = Object.FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (MonoBehaviour mb in all)
            {
                if (mb == null || mb.GetType().Name != "COC4AssessmentReportView") continue;

                SerializedObject so = new SerializedObject(mb);
                bool any = Set(so, "panelRoot", panelRoot) |
                           Set(so, "bodyText", reportText) |
                           Set(so, "doneButton", backButton);
                so.ApplyModifiedProperties();

                if (!any) continue;
                wired++;
                notes.Add("Wired COC4AssessmentReportView on '" + mb.name +
                          "'. Its Headline Text and Score Text are separate labels - add them to " +
                          "the panel and drag them in if you want them.");
            }

            return wired;
        }

        static bool Set(SerializedObject so, string field, Object value)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null) return false;
            p.objectReferenceValue = value;
            return true;
        }

        // ---------------------------------------------------------------- little makers

        /// <summary>
        /// A vertical scrollbar, with the three nested pieces uGUI requires.
        ///
        /// A Scrollbar is not one object: it needs a Sliding Area to move within and a Handle to
        /// move, and the Handle has to be assigned to the Scrollbar's own Handle Rect field.
        /// Skipping that last step leaves a bar that is drawn, responds to nothing, and never
        /// moves - which reads as the scroll view being broken rather than the bar being unwired.
        /// </summary>
        static Scrollbar Scrollbar(string name, Transform parent)
        {
            GameObject go = Panel(name, parent, new Color(1f, 1f, 1f, 0.08f));
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 0);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 1);
            rt.offsetMin = new Vector2(-24, 0);
            rt.offsetMax = new Vector2(0, 0);

            GameObject areaGO = new GameObject("Sliding Area", typeof(RectTransform));
            areaGO.transform.SetParent(go.transform, false);
            RectTransform area = areaGO.GetComponent<RectTransform>();
            Stretch(area);
            area.offsetMin = new Vector2(4, 4);
            area.offsetMax = new Vector2(-4, -4);

            GameObject handleGO = Panel("Handle", areaGO.transform, new Color(0.55f, 0.60f, 0.70f, 0.9f));
            RectTransform handle = handleGO.GetComponent<RectTransform>();
            Stretch(handle);

            Scrollbar bar = go.AddComponent<Scrollbar>();
            bar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
            bar.handleRect = handle;
            bar.targetGraphic = handleGO.GetComponent<Image>();
            return bar;
        }

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
            t.raycastTarget = false;
            return t;
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
