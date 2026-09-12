using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CircuitCrafter.Settings.EditorTools
{
    /// <summary>
    /// Makes the Settings screen's text bigger, in the scene, without touching a single colour.
    ///
    /// OPEN IT WITH: CircuitCrafter > Settings Panel > Resize Fonts
    ///
    /// WHY THIS IS A TOOL AND NOT A NEW BUILDER
    /// Rebuilding the panel would throw away every tweak already made to it - the tab names, the
    /// row order, the sprites - to fix one number per label. This walks the panel that is already
    /// in the scene and changes only fontSize, so everything else survives untouched. It is also
    /// fully undoable: one Ctrl+Z puts every label back.
    ///
    /// A MINIMUM, NOT A MULTIPLIER - that is the important design choice here.
    /// Scaling everything by 1.4 makes the small text readable and the heading enormous, because
    /// the heading was never the problem. Raising everything BELOW a floor up TO that floor fixes
    /// exactly the labels that were too small and leaves the rest alone. Scale is still there as a
    /// second knob for when the whole screen wants to come up a notch, but it defaults to 1.0
    /// because most of the time it should not be used at all.
    ///
    /// THE ROWS GROW WITH THE TEXT. A 16pt label in a row 24 pixels tall becomes a 22pt label
    /// clipped by a row 24 pixels tall, which reads as "the tool broke my layout". So every
    /// LayoutElement above a label that grew is grown by the same ratio. Rows that size themselves
    /// - a ContentSizeFitter, or a layout group with no fixed height - need nothing and get nothing.
    /// </summary>
    public class SettingsFontResizer : EditorWindow
    {
        // How far up from a label to look for the row that holds it. Four covers
        // Label > Row > Page in every panel shape I have seen, without escaping into the
        // panel itself and resizing the whole screen.
        private const int RowSearchDepth = 4;

        private SettingsPanel panel;

        [Tooltip("Any label smaller than this is raised to it. Labels already this big are left alone.")]
        private float minimumSize = 22f;

        private float scale = 1f;
        private bool growRows = true;
        private bool widenBubble = true;
        private float bubbleWidth = 420f;

        private Vector2 scroll;
        private readonly List<string> preview = new List<string>();
        private bool previewStale = true;

        // ==================================================================

        [MenuItem("CircuitCrafter/Settings Panel/Resize Fonts", false, 42)]
        public static void Open()
        {
            var window = GetWindow<SettingsFontResizer>(true, "Settings Font Size", true);
            window.minSize = new Vector2(430f, 460f);
            window.FindPanel();
            window.Show();
        }

        private void OnEnable()
        {
            if (panel == null) FindPanel();
        }

        private void FindPanel()
        {
            var found = Object.FindObjectsByType<SettingsPanel>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            panel = found.Length > 0 ? found[0] : null;
            previewStale = true;

            if (found.Length > 1)
                Debug.LogWarning("[SettingsFontResizer] " + found.Length + " SettingsPanels are in " +
                                 "the open scenes. The first was picked - drag the right one into " +
                                 "the window if that is not the one you meant.", panel);
        }

        // ==================================================================

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Raises every label on the Settings screen that is smaller than the minimum. " +
                "Colours, fonts, sprites and layout are not touched. Undo puts it all back.",
                MessageType.None);

            EditorGUI.BeginChangeCheck();

            panel = (SettingsPanel)EditorGUILayout.ObjectField(
                new GUIContent("Settings Panel", "The panel in the open scene. Found for you."),
                panel, typeof(SettingsPanel), true);

            EditorGUILayout.Space(6f);

            minimumSize = EditorGUILayout.Slider(
                new GUIContent("Minimum size", "Anything below this is raised to it. 22 is a " +
                               "comfortable floor for body text on a projector at the back of a lab."),
                minimumSize, 10f, 48f);

            scale = EditorGUILayout.Slider(
                new GUIContent("Scale everything by", "Leave at 1.00 unless the WHOLE screen needs " +
                               "to come up. Applied first, then the minimum."),
                scale, 0.5f, 2f);

            EditorGUILayout.Space(6f);

            growRows = EditorGUILayout.Toggle(
                new GUIContent("Grow rows to match", "Grows any fixed LayoutElement height above a " +
                               "label that got bigger, so the new text is not clipped."),
                growRows);

            widenBubble = EditorGUILayout.Toggle(
                new GUIContent("Widen the help bubble", "Bigger text in a 340-wide bubble wraps every " +
                               "few words. The bubble's HEIGHT already measures itself."),
                widenBubble);

            using (new EditorGUI.DisabledScope(!widenBubble))
                bubbleWidth = EditorGUILayout.Slider("    Bubble width", bubbleWidth, 240f, 720f);

            if (EditorGUI.EndChangeCheck()) previewStale = true;

            EditorGUILayout.Space(10f);

            if (panel == null)
            {
                EditorGUILayout.HelpBox(
                    "No SettingsPanel found in the open scenes.\n\nOpen the scene with your settings " +
                    "screen in it - the Main Menu, most likely - then press Find again.",
                    MessageType.Warning);

                if (GUILayout.Button("Find it")) FindPanel();
                return;
            }

            if (previewStale) BuildPreview();

            EditorGUILayout.LabelField("What will change", EditorStyles.boldLabel);

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(140f));
            if (preview.Count == 0)
                EditorGUILayout.LabelField("Nothing - every label is already at least " +
                                           minimumSize.ToString("0") + ".");
            else
                foreach (string line in preview) EditorGUILayout.LabelField(line);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(8f);

            using (new EditorGUI.DisabledScope(preview.Count == 0))
            {
                if (GUILayout.Button("Apply", GUILayout.Height(30f))) Apply();
            }

            EditorGUILayout.HelpBox(
                "Not what you wanted? Ctrl+Z. Then change the minimum and apply again.",
                MessageType.None);
        }

        // ==================================================================

        /// <summary>The new size for an old one, or the old one when nothing changes.</summary>
        private float Resize(float old)
        {
            if (old <= 0f) return old;                 // auto-sized with no base - leave it
            return Mathf.Round(Mathf.Max(minimumSize, old * scale));
        }

        private void BuildPreview()
        {
            previewStale = false;
            preview.Clear();
            if (panel == null) return;

            // Grouped by size rather than listed per label. A settings screen has thirty labels at
            // four sizes; thirty lines is a wall, four lines is an answer.
            var counts = new SortedDictionary<float, KeyValuePair<float, int>>();

            foreach (TMP_Text text in panel.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text == null) continue;

                float old = text.fontSize;
                float now = Resize(old);
                if (Mathf.Approximately(now, old)) continue;

                int count = counts.TryGetValue(old, out var entry) ? entry.Value : 0;
                counts[old] = new KeyValuePair<float, int>(now, count + 1);
            }

            foreach (var pair in counts)
            {
                preview.Add("    " + pair.Key.ToString("0") + "  ->  " + pair.Value.Key.ToString("0") +
                            "      (" + pair.Value.Value + " label" + (pair.Value.Value == 1 ? "" : "s") + ")");
            }
        }

        // ==================================================================

        private void Apply()
        {
            if (panel == null) return;

            Undo.SetCurrentGroupName("Resize settings fonts");
            int group = Undo.GetCurrentGroup();

            // Row -> the largest ratio any label inside it grew by. Collected first and applied
            // after, because two labels in one row would otherwise grow it twice.
            var rowGrowth = new Dictionary<LayoutElement, float>();

            int changed = 0;

            foreach (TMP_Text text in panel.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text == null) continue;

                float old = text.fontSize;
                float now = Resize(old);
                if (Mathf.Approximately(now, old)) continue;

                Undo.RecordObject(text, "Resize settings fonts");

                text.fontSize = now;

                // An auto-sizing label ignores fontSize and obeys this pair instead, so raising one
                // without the other changes nothing on screen and looks like the tool did nothing.
                if (text.enableAutoSizing)
                {
                    text.fontSizeMin = Resize(text.fontSizeMin);
                    text.fontSizeMax = Mathf.Max(Resize(text.fontSizeMax), text.fontSizeMin);
                }

                EditorUtility.SetDirty(text);
                changed++;

                if (!growRows || old <= 0f) continue;

                LayoutElement row = FindRow(text.transform);
                if (row == null) continue;

                float ratio = now / old;
                rowGrowth[row] = rowGrowth.TryGetValue(row, out float existing)
                    ? Mathf.Max(existing, ratio)
                    : ratio;
            }

            int rows = 0;
            foreach (var pair in rowGrowth)
            {
                LayoutElement row = pair.Key;
                float ratio = pair.Value;

                // Only fixed heights are grown. A negative value means "I have no opinion", and
                // writing a number into it would take the row's height away from the layout group
                // that was correctly deciding it.
                bool hasMin = row.minHeight > 0f;
                bool hasPreferred = row.preferredHeight > 0f;
                if (!hasMin && !hasPreferred) continue;

                Undo.RecordObject(row, "Resize settings fonts");

                if (hasMin) row.minHeight = Mathf.Round(row.minHeight * ratio);
                if (hasPreferred) row.preferredHeight = Mathf.Round(row.preferredHeight * ratio);

                EditorUtility.SetDirty(row);
                rows++;
            }

            if (widenBubble) ApplyBubbleWidth();

            EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
            Undo.CollapseUndoOperations(group);

            Debug.Log("[SettingsFontResizer] " + changed + " label(s) resized" +
                      (rows > 0 ? ", " + rows + " row height(s) grown to match" : "") +
                      (widenBubble ? ", help bubble widened to " + bubbleWidth.ToString("0") : "") +
                      ". Ctrl+Z undoes all of it.", panel);

            previewStale = true;
            Repaint();
        }

        /// <summary>
        /// bubbleWidth is a private [SerializeField], so it is reached the only way the editor can
        /// reach one: through SerializedObject. Setting it by reflection would skip Unity's undo and
        /// dirty bookkeeping and the value would not survive a scene save.
        /// </summary>
        private void ApplyBubbleWidth()
        {
            var so = new SerializedObject(panel);
            SerializedProperty width = so.FindProperty("bubbleWidth");
            if (width == null) return;

            width.floatValue = bubbleWidth;
            so.ApplyModifiedProperties();
        }

        /// <summary>Nearest LayoutElement at or above this label, within RowSearchDepth.</summary>
        private static LayoutElement FindRow(Transform from)
        {
            Transform t = from;
            for (int i = 0; i <= RowSearchDepth && t != null; i++)
            {
                var element = t.GetComponent<LayoutElement>();
                if (element != null) return element;
                t = t.parent;
            }
            return null;
        }
    }
}
