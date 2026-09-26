using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using CircuitCrafter.Assessment.UI;

namespace CircuitCrafter.Assessment.EditorTools
{
    /// <summary>
    /// Adds the two evidence lines to the competency module header prefab and wires them up.
    ///
    /// RUN IT WITH: CircuitCrafter > Build Evidence Lines, with the competency screen's scene open.
    ///
    /// It finds the prefab by asking CompetencyProfileView in the open scene which one it uses, so
    /// there is nothing to select and nothing to type. Failing that, select the header prefab in the
    /// Project window and run it again.
    ///
    /// WHAT IT ADDS
    ///   EvidenceLines            (vertical layout, under everything already there)
    ///     PerformanceLine        -> Performance Evidence Text
    ///     KnowledgeRow           -> Knowledge Row
    ///       KnowledgeLine        -> Knowledge Evidence Text
    ///
    /// KnowledgeRow exists so the whole knowledge requirement can be hidden on a build with no
    /// quizzes. An evidence screen showing a requirement that can never be met reads as a failure,
    /// which is why it is a wrapper object rather than just hiding the text.
    ///
    /// WHY A PREFAB NEEDS SPECIAL HANDLING
    /// A prefab asset is not a scene object and cannot be edited by finding it and calling
    /// AddComponent - changes made that way either throw or are silently discarded on the next
    /// reimport. PrefabUtility.LoadPrefabContents opens the asset into a hidden scene of its own,
    /// which is a real GameObject hierarchy that can be edited normally; SaveAsPrefabAsset writes it
    /// back, and UnloadPrefabContents closes it. The try/finally below is what stops a failure
    /// halfway through leaving that hidden scene open for the rest of the Editor session.
    ///
    /// SAFE TO RUN TWICE. Anything already present is reused and re-wired rather than duplicated,
    /// and the report says which of the two it did.
    ///
    /// IT TAKES ITS LOOK FROM THE PREFAB. Font, size and colour are copied from the header's own
    /// title text, so the new lines match whatever you have already styled rather than arriving in
    /// the builder's house style.
    /// </summary>
    public static class EvidenceLinesBuilder
    {
        private const string EvidenceRoot = "EvidenceLines";
        private const string PerformanceName = "PerformanceLine";
        private const string KnowledgeRowName = "KnowledgeRow";
        private const string KnowledgeName = "KnowledgeLine";

        [MenuItem("CircuitCrafter/Build Evidence Lines", false, 41)]
        public static void Build()
        {
            GameObject prefabAsset = FindHeaderPrefab(out string how);

            if (prefabAsset == null)
            {
                EditorUtility.DisplayDialog("Build Evidence Lines",
                    "Could not find the module header prefab.\n\n" +
                    "Either open the scene with your CompetencyProfileView on it (this reads its " +
                    "Module Header Prefab field), or select the header prefab in the Project window, " +
                    "then run this again.", "OK");
                return;
            }

            string path = AssetDatabase.GetAssetPath(prefabAsset);
            if (string.IsNullOrEmpty(path))
            {
                EditorUtility.DisplayDialog("Build Evidence Lines",
                    $"'{prefabAsset.name}' is a scene object, not a prefab asset.\n\n" +
                    "CompetencyProfileView instantiates its Module Header Prefab, so that field has " +
                    "to point at a prefab in the Project window. Drag the object into your Project " +
                    "folder to make it one, assign that, then run this again.", "OK");
                return;
            }

            // Open the asset into its own hidden scene. Everything between here and
            // UnloadPrefabContents operates on 'root', never on the asset directly.
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            var report = new StringBuilder();
            report.AppendLine($"[EvidenceLinesBuilder] {how}");
            report.AppendLine($"  Prefab: {path}");

            try
            {
                var view = root.GetComponent<CompetencyModuleHeaderView>();
                if (view == null)
                {
                    EditorUtility.DisplayDialog("Build Evidence Lines",
                        $"'{root.name}' has no CompetencyModuleHeaderView on its root object.\n\n" +
                        "That component is what these lines belong to. Add it to the prefab root " +
                        "first, then run this again.", "OK");
                    return;
                }

                TMP_Text style = FindStyleSource(view);

                RectTransform holder = EnsureHolder(root.transform, report);
                EnsureVerticalLayout(holder);

                TMP_Text performance = EnsureText(holder, PerformanceName, style,
                                                  "Performance (demonstration): not yet met", report);

                RectTransform knowledgeRow = EnsureRect(holder, KnowledgeRowName, report);
                EnsureVerticalLayout(knowledgeRow);

                TMP_Text knowledge = EnsureText(knowledgeRow, KnowledgeName, style,
                                                "Quiz (written test): not taken yet", report);

                WireUp(view, performance, knowledge, knowledgeRow.gameObject, report);

                PrefabUtility.SaveAsPrefabAsset(root, path);
                report.AppendLine("  Saved.");
            }
            finally
            {
                // finally, not just at the end: a thrown exception above would otherwise leave the
                // hidden prefab scene open for the rest of the session, and every later
                // LoadPrefabContents on the same asset would then behave strangely with no clue why.
                PrefabUtility.UnloadPrefabContents(root);
            }

            report.AppendLine();
            report.AppendLine("  Both lines fill themselves at runtime - leave the placeholder text.");
            report.AppendLine("  Nothing else to do: step 5 is finished.");

            AssetDatabase.SaveAssets();
            Debug.Log(report.ToString());
        }

        // ==================================================================

        /// <summary>
        /// The prefab to edit: whatever CompetencyProfileView in the open scene is using, or the
        /// Project-window selection as a fallback.
        ///
        /// Asking the scene first is deliberate. Selecting the prefab by hand is the step most
        /// likely to go wrong - there are three prefabs in that folder with similar names, and
        /// editing the one the screen does not use produces a build where nothing appears and
        /// nothing is wrong.
        /// </summary>
        private static GameObject FindHeaderPrefab(out string how)
        {
            var screen = Object.FindAnyObjectByType<CompetencyProfileView>(FindObjectsInactive.Include);
            if (screen != null)
            {
                var so = new SerializedObject(screen);
                SerializedProperty prop = so.FindProperty("moduleHeaderPrefab");
                var assigned = prop != null ? prop.objectReferenceValue as CompetencyModuleHeaderView : null;

                if (assigned != null)
                {
                    how = $"Found via CompetencyProfileView on '{screen.name}' in the open scene.";
                    return assigned.gameObject;
                }

                how = $"CompetencyProfileView on '{screen.name}' has no Module Header Prefab assigned.";
            }
            else
            {
                how = "No CompetencyProfileView in the open scene.";
            }

            // Fallback: whatever is selected in the Project window.
            var selected = Selection.activeGameObject;
            if (selected != null && selected.GetComponent<CompetencyModuleHeaderView>() != null)
            {
                how += " Used the prefab selected in the Project window instead.";
                return selected;
            }

            return null;
        }

        /// <summary>
        /// A text on the prefab to copy font, size and colour from, so the new lines look native.
        ///
        /// Prefers the title - it is the one field the header is documented as always having - and
        /// falls back to any text at all. Returns null for a prefab with no text yet, and the
        /// defaults below then apply.
        /// </summary>
        private static TMP_Text FindStyleSource(CompetencyModuleHeaderView view)
        {
            var so = new SerializedObject(view);

            foreach (string field in new[] { "summaryText", "moduleTitleText", "moduleIdText" })
            {
                SerializedProperty prop = so.FindProperty(field);
                var text = prop != null ? prop.objectReferenceValue as TMP_Text : null;
                if (text != null) return text;
            }

            return view.GetComponentInChildren<TMP_Text>(true);
        }

        /// <summary>
        /// Finds the EvidenceLines holder wherever it already is, and only creates one when the
        /// prefab genuinely has none.
        ///
        /// WHY IT SEARCHES THE WHOLE PREFAB RATHER THAN JUST THE ROOT'S CHILDREN
        /// CompetencyScreenBuilder now builds these lines itself, nested under the header's Pad
        /// object. A root-children-only search would not see them, would create a SECOND holder
        /// beside Pad - where the root has no layout group to position it - and would then wire the
        /// view to that invisible copy. The prefab would look untouched and the lines would never
        /// appear, with nothing in the Console to say why.
        ///
        /// WHERE A NEW ONE GOES, when there is nothing to find: under the first child that has a
        /// VerticalLayoutGroup (Pad, on a header built by CompetencyScreenBuilder), because that is
        /// the object that stacks the row's bands. Falling back to the root matches what this
        /// builder did before, which is right for an older single-band header laid out by hand.
        /// </summary>
        private static RectTransform EnsureHolder(Transform root, StringBuilder report)
        {
            foreach (var candidate in root.GetComponentsInChildren<RectTransform>(true))
            {
                if (candidate != null && candidate.name == EvidenceRoot)
                {
                    report.AppendLine($"  {EvidenceRoot}: already there (under '{PathOf(candidate, root)}'), reused.");
                    return candidate;
                }
            }

            Transform parent = root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.GetComponent<VerticalLayoutGroup>() != null) { parent = child; break; }
            }

            report.AppendLine($"  {EvidenceRoot}: created under '{PathOf(parent, root)}'.");

            var go = new GameObject(EvidenceRoot, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        private static string PathOf(Transform target, Transform root)
        {
            string path = target.name;
            Transform walk = target.parent;

            while (walk != null && walk != root.parent)
            {
                path = walk.name + "/" + path;
                walk = walk.parent;
            }
            return path;
        }

        private static RectTransform EnsureRect(Transform parent, string name, StringBuilder report)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                report.AppendLine($"  {name}: already there, reused.");
                return existing as RectTransform ?? existing.gameObject.AddComponent<RectTransform>();
            }

            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;

            report.AppendLine($"  {name}: created.");
            return rect;
        }

        private static void EnsureVerticalLayout(RectTransform rect)
        {
            var layout = rect.GetComponent<VerticalLayoutGroup>();
            if (layout == null) layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();

            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            layout.spacing = 2f;

            // Both, and this pairing is the one people miss. A layout group arranges its children
            // but does nothing about its OWN height, so without a fitter the holder stays whatever
            // size it happened to be and the lines are laid out inside a box of the wrong height -
            // usually zero, which looks exactly like the lines never being created.
            var fitter = rect.GetComponent<ContentSizeFitter>();
            if (fitter == null) fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        }

        private static TMP_Text EnsureText(RectTransform parent, string name, TMP_Text style,
                                           string placeholder, StringBuilder report)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                var found = existing.GetComponent<TMP_Text>();
                if (found != null)
                {
                    report.AppendLine($"  {name}: already there, reused.");
                    return found;
                }
            }

            RectTransform rect = EnsureRect(parent, name, report);

            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = placeholder;
            text.raycastTarget = false;
            text.alignment = TextAlignmentOptions.MidlineLeft;

            // Word wrap ON here, unlike the rest of the builder's labels. These lines carry a real
            // sentence - "82% in practice - needs a graded run" - and a narrow column should wrap it
            // rather than cut it off at the point where the number the learner needs would be.
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;

            if (style != null)
            {
                text.font = style.font;
                text.fontSize = Mathf.Max(10f, style.fontSize * 0.85f);
                text.fontSharedMaterial = style.fontSharedMaterial;
            }
            else
            {
                if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
                text.fontSize = 16f;
            }

            // Colour is left to the component, which recolours both lines every time it draws from
            // its Met / Not Met Colour fields. Setting one here would only ever be seen for the one
            // frame before Setup runs.

            return text;
        }

        /// <summary>
        /// Assigns the three fields. They are [SerializeField] private, so SerializedObject is the
        /// only way in - and it is also the way that marks the prefab dirty properly.
        /// </summary>
        private static void WireUp(CompetencyModuleHeaderView view, TMP_Text performance,
                                   TMP_Text knowledge, GameObject knowledgeRow, StringBuilder report)
        {
            var so = new SerializedObject(view);

            Assign(so, "performanceEvidenceText", performance, report);
            Assign(so, "knowledgeEvidenceText", knowledge, report);
            Assign(so, "knowledgeRow", knowledgeRow, report);

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Assign(SerializedObject so, string field, Object value, StringBuilder report)
        {
            SerializedProperty prop = so.FindProperty(field);

            if (prop == null)
            {
                // Names a real mismatch rather than failing silently: this happens when the older
                // CompetencyModuleHeaderView is still in the project, and the fix is to replace that
                // file first.
                report.AppendLine($"  {field}: NOT FOUND on CompetencyModuleHeaderView. You are still " +
                                  "on the old version of that script - replace it, then run this again.");
                return;
            }

            prop.objectReferenceValue = value;
            report.AppendLine($"  {field}: wired to '{value.name}'.");
        }
    }
}