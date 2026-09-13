// CrimpingStageBuilder.cs   -   Assets/Scripts/COC2/Editor/
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Builds the wiring stage's UI in one press: a canvas holding two guide panels and two wire
    /// trays. Layout is yours to move afterwards - this only makes the pieces and wires the
    /// references between them.
    ///
    /// WHY TWO TRAYS
    ///
    /// A slot refuses a wire whose endNumber is not its own. That rule already works, but with all
    /// sixteen wires in one place the player meets it as a refusal: they reach for the white/orange
    /// they can see, it will not go in, and nothing distinguishes it from the white/orange that
    /// would have. Two trays turn the rule into something visible - End A's wires here, End B's
    /// there - which is also how it works on a bench, where the two ends are simply at opposite
    /// ends of the cable.
    ///
    /// The trays are built empty. What goes in them is yours: drag your wires under the tray you
    /// want them in, or use the tray as a backdrop and place 3D wires in front of it.
    ///
    /// SETUP: Tools > CircuitCrafter > Crimping Stage Builder.
    /// </summary>
    public class CrimpingStageBuilder : EditorWindow
    {
        CrimpingStationController station;
        Canvas targetCanvas;
        bool replaceExisting = true;

        [MenuItem("Tools/CircuitCrafter/Crimping Stage Builder")]
        static void ShowWindow()
        {
            CrimpingStageBuilder w = GetWindow<CrimpingStageBuilder>(true, "Crimping Stage Builder");
            w.minSize = new Vector2(420, 300);
            w.station = FindFirstObjectByType<CrimpingStationController>();
            w.targetCanvas = FindFirstObjectByType<Canvas>();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Build the wiring stage UI", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Makes four panels on one canvas: a guide for each end, and a tray for each end's " +
                "wires.\n\n" +
                "The guides are fully wired and will fill themselves in from the cable type the " +
                "player picks. The trays are built empty - move them and fill them yourself.\n\n" +
                "Ctrl+Z undoes the whole build.",
                MessageType.Info);
            EditorGUILayout.Space(8);

            station = (CrimpingStationController)EditorGUILayout.ObjectField(
                new GUIContent("Station", "The guides read the chosen cable type from this. Leave " +
                                          "empty and they will show dashes until you assign it."),
                station, typeof(CrimpingStationController), true);

            targetCanvas = (Canvas)EditorGUILayout.ObjectField(
                new GUIContent("Canvas", "Where the four panels go. Empty creates one."),
                targetCanvas, typeof(Canvas), true);

            replaceExisting = EditorGUILayout.Toggle(
                new GUIContent("Replace existing", "Delete panels of the same name before building, " +
                                                   "so pressing this twice does not leave two of each."),
                replaceExisting);

            EditorGUILayout.Space(12);

            if (GUILayout.Button("Build wiring stage UI", GUILayout.Height(32)))
                Build();
        }

        // ------------------------------------------------------------------ build

        void Build()
        {
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Build Wiring Stage UI");

            Canvas canvas = targetCanvas != null ? targetCanvas : CreateCanvas();

            BuildGuide(canvas, 1, "CrimpingGuidePanel_EndA", new Vector2(-560f, 60f));
            BuildGuide(canvas, 2, "CrimpingGuidePanel_EndB", new Vector2(560f, 60f));

            BuildTray(canvas, "WireTray_EndA", "END A  -  wires for this end only",
                      new Vector2(-560f, -330f), new Color(0.16f, 0.22f, 0.30f, 0.92f));
            BuildTray(canvas, "WireTray_EndB", "END B  -  wires for this end only",
                      new Vector2(560f, -330f), new Color(0.26f, 0.20f, 0.14f, 0.92f));

            Undo.CollapseUndoOperations(undo);
            Selection.activeObject = canvas.gameObject;

            Debug.Log("[CrimpingStageBuilder] Built two guide panels and two wire trays on '" +
                      canvas.name + "'. Move them where you want them; the guides are already wired.");
        }

        Canvas CreateCanvas()
        {
            GameObject go = new GameObject("CrimpingStageCanvas", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build Wiring Stage UI");

            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Overlay and a matched scaler, because this stage switches cameras - a Screen Space
            // Camera canvas pointed at one that gets disabled simply vanishes, which reads exactly
            // like a sorting problem and is not one.
            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        // ------------------------------------------------------------------ guide panels

        void BuildGuide(Canvas canvas, int endNumber, string panelName, Vector2 position)
        {
            if (replaceExisting)
            {
                Transform existing = canvas.transform.Find(panelName);
                if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
            }

            GameObject panel = Panel(canvas.transform, panelName,
                                     new Color(0.09f, 0.10f, 0.13f, 0.92f), position,
                                     new Vector2(300f, 430f));

            VerticalLayoutGroup outer = panel.AddComponent<VerticalLayoutGroup>();
            outer.padding = new RectOffset(14, 14, 14, 14);
            outer.spacing = 6f;
            outer.childControlWidth = true;
            outer.childForceExpandWidth = true;
            outer.childControlHeight = false;
            outer.childForceExpandHeight = false;

            TMP_Text title = Label(panel.transform, "Title", endNumber == 1 ? "END A" : "END B",
                                   20f, FontStyles.Bold);
            title.alignment = TextAlignmentOptions.Center;
            Height(title.rectTransform, 26f);

            TMP_Text cableType = Label(panel.transform, "CableTypeLabel", "-", 13f, FontStyles.Normal);
            cableType.alignment = TextAlignmentOptions.Center;
            cableType.color = new Color(0.72f, 0.76f, 0.84f);
            Height(cableType.rectTransform, 18f);

            TMP_Text standard = Label(panel.transform, "StandardLabel", "-", 15f, FontStyles.Bold);
            standard.alignment = TextAlignmentOptions.Center;
            standard.color = new Color(0.45f, 0.72f, 1f);
            Height(standard.rectTransform, 20f);

            GameObject list = Empty(panel.transform, "Rows");
            VerticalLayoutGroup rows = list.AddComponent<VerticalLayoutGroup>();
            rows.spacing = 3f;
            rows.childControlWidth = true;
            rows.childForceExpandWidth = true;
            rows.childControlHeight = false;
            rows.childForceExpandHeight = false;

            // A disabled child rather than a project prefab. CrimpingGuideUI instantiates whatever
            // it is handed, so a template living inside the panel it belongs to does the same job
            // with nothing to save to disk and nothing to keep in step.
            CrimpingGuidePinRow template = BuildRow(list.transform);
            template.gameObject.SetActive(false);

            CrimpingGuideUI guide = panel.AddComponent<CrimpingGuideUI>();
            guide.station = station;
            guide.endNumber = endNumber;
            guide.panel = panel;
            guide.rowPrefab = template;
            guide.listParent = list.transform;
            guide.cableTypeLabel = cableType;
            guide.standardLabel = standard;
            EditorUtility.SetDirty(guide);
        }

        CrimpingGuidePinRow BuildRow(Transform parent)
        {
            GameObject row = Empty(parent, "RowTemplate");
            Height((RectTransform)row.transform, 30f);

            HorizontalLayoutGroup h = row.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 10f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = false;
            h.childForceExpandWidth = false;
            h.childControlHeight = true;
            h.childForceExpandHeight = true;

            TMP_Text pin = Label(row.transform, "PinNumber", "0", 16f, FontStyles.Bold);
            pin.alignment = TextAlignmentOptions.Center;
            Width(pin.rectTransform, 22f);

            GameObject swatchGo = Empty(row.transform, "Swatch");
            Image swatch = swatchGo.AddComponent<Image>();
            swatch.color = Color.gray;
            Width((RectTransform)swatchGo.transform, 34f);

            TMP_Text colourName = Label(row.transform, "ColorName", "-", 14f, FontStyles.Normal);
            colourName.alignment = TextAlignmentOptions.Left;
            Width(colourName.rectTransform, 180f);

            CrimpingGuidePinRow comp = row.AddComponent<CrimpingGuidePinRow>();

            // The row's three references are private [SerializeField]s, so they go in through the
            // serialised object - the same route the Inspector uses when you drag into one.
            SerializedObject so = new SerializedObject(comp);
            so.FindProperty("pinNumberText").objectReferenceValue = pin;
            so.FindProperty("swatch").objectReferenceValue = swatch;
            so.FindProperty("colorNameText").objectReferenceValue = colourName;
            so.ApplyModifiedPropertiesWithoutUndo();

            return comp;
        }

        // ------------------------------------------------------------------ trays

        void BuildTray(Canvas canvas, string trayName, string heading, Vector2 position, Color tint)
        {
            if (replaceExisting)
            {
                Transform existing = canvas.transform.Find(trayName);
                if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
            }

            GameObject tray = Panel(canvas.transform, trayName, tint, position,
                                    new Vector2(620f, 150f));

            VerticalLayoutGroup outer = tray.AddComponent<VerticalLayoutGroup>();
            outer.padding = new RectOffset(12, 12, 10, 12);
            outer.spacing = 8f;
            outer.childControlWidth = true;
            outer.childForceExpandWidth = true;
            outer.childControlHeight = false;
            outer.childForceExpandHeight = false;

            // The two trays are tinted differently on purpose. A player glancing down mid-drag
            // should be able to tell which pile they are reaching into without reading anything.
            TMP_Text label = Label(tray.transform, "Heading", heading, 14f, FontStyles.Bold);
            label.alignment = TextAlignmentOptions.Left;
            label.color = new Color(0.88f, 0.91f, 0.96f);
            Height(label.rectTransform, 20f);

            GameObject slots = Empty(tray.transform, "Wires");
            Height((RectTransform)slots.transform, 90f);

            HorizontalLayoutGroup row = slots.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 10f;
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childControlWidth = false;
            row.childForceExpandWidth = false;
            row.childControlHeight = false;
            row.childForceExpandHeight = false;
        }

        // ------------------------------------------------------------------ helpers

        GameObject Panel(Transform parent, string name, Color colour, Vector2 position, Vector2 size)
        {
            GameObject go = Empty(parent, name);
            Image img = go.AddComponent<Image>();
            img.color = colour;

            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = position;
            return go;
        }

        GameObject Empty(Transform parent, string name)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build Wiring Stage UI");
            go.transform.SetParent(parent, false);
            return go;
        }

        TMP_Text Label(Transform parent, string name, string text, float size, FontStyles style)
        {
            GameObject go = Empty(parent, name);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = Color.white;
            t.raycastTarget = false;
            return t;
        }

        static void Height(RectTransform rt, float height)
        {
            LayoutElement le = rt.gameObject.GetComponent<LayoutElement>();
            if (le == null) le = rt.gameObject.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = height;
        }

        static void Width(RectTransform rt, float width)
        {
            LayoutElement le = rt.gameObject.GetComponent<LayoutElement>();
            if (le == null) le = rt.gameObject.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = width;
        }
    }
}
