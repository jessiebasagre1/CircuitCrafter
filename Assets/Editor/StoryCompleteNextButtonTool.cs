using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;

namespace CircuitCrafter.EditorTools
{
    /// <summary>
    /// Adds a Next button to the open scene's Story Complete panel:
    /// CircuitCrafter → Add Next Button To Story Complete Panel.
    ///
    /// THIS FILE MUST LIVE IN AN "Editor" FOLDER - next to StoryCompleteBuilder.cs is right.
    ///
    /// Run it once in each COC scene that ends with the Story Complete panel (COC1, COC2, COC3).
    /// COC4 is the last COC, so it needs none - and if it gets one anyway, the button hides itself.
    ///
    /// The button is a copy of the panel's Take Assessment button (or Main Menu, or Retry), so it
    /// matches the panel's look without any colours to pick. It goes at the right-hand end of the
    /// button row, and the card is widened if the row would no longer fit.
    ///
    /// Nothing else to set: StoryCompletePanel wires the button in code and works out the next
    /// COC from the Main Menu's own COC list at run time.
    /// </summary>
    public static class StoryCompleteNextButtonTool
    {
        private const string UndoName = "Add Next Button";
        private const string Title = "Story Complete - Next Button";

        [MenuItem("CircuitCrafter/Add Next Button To Story Complete Panel")]
        public static void AddNextButton()
        {
            StoryCompletePanel panel = Object.FindAnyObjectByType<StoryCompletePanel>(FindObjectsInactive.Include);
            if (panel == null)
            {
                EditorUtility.DisplayDialog(Title,
                    "This scene has no Story Complete panel.\n\nRun CircuitCrafter → Build Story Complete Panel first.", "OK");
                return;
            }

            var so = new SerializedObject(panel);
            SerializedProperty nextProperty = so.FindProperty("nextButton");
            if (nextProperty == null)
            {
                // Name the exact file the panel is using - replacing a copy somewhere else (or in the
                // other project folder) is the usual reason this happens.
                MonoScript script = MonoScript.FromMonoBehaviour(panel);
                string path = script != null ? AssetDatabase.GetAssetPath(script) : "(unknown)";

                EditorUtility.DisplayDialog(Title,
                    "Unity is still running the OLD StoryCompletePanel (no Next Button field).\n\n" +
                    "The panel in this scene uses:\n" + path + "\n\n" +
                    "1. Replace THAT file with the new StoryCompletePanel.cs.\n" +
                    "2. Check the Console for red errors. Any compile error - for example a second copy of " +
                    "StoryCompletePanel.cs somewhere else - makes Unity keep running the old code.\n" +
                    "3. Wait for the compile to finish, then run this again.", "OK");

                Debug.Log("[StoryCompleteNextButtonTool] The Story Complete panel's script is " + path +
                          " - that is the file to replace. Click this message to highlight it.", script);
                if (script != null) EditorGUIUtility.PingObject(script);
                return;
            }

            if (nextProperty.objectReferenceValue != null)
            {
                EditorUtility.DisplayDialog(Title,
                    $"This panel already has a Next button: '{nextProperty.objectReferenceValue.name}'.", "OK");
                Selection.activeObject = nextProperty.objectReferenceValue;
                return;
            }

            // The style to copy: the panel's primary button first.
            Button source = ButtonIn(so, "assessmentButton");
            if (source == null) source = ButtonIn(so, "mainMenuButton");
            if (source == null) source = ButtonIn(so, "retryButton");
            if (source == null)
            {
                EditorUtility.DisplayDialog(Title,
                    "The panel has no Retry, Main Menu or Take Assessment button to copy.\n\n" +
                    "Make a button by hand and drag it into the panel's Next Button field.", "OK");
                return;
            }

            Transform row = source.transform.parent;
            GameObject clone = Object.Instantiate(source.gameObject, row);
            Undo.RegisterCreatedObjectUndo(clone, UndoName);
            clone.name = "NextButton";
            clone.SetActive(true);
            clone.transform.SetAsLastSibling();

            Button next = clone.GetComponent<Button>();

            // StoryCompletePanel wires its buttons in code and asks for EMPTY OnClick lists. A copied
            // OnClick entry would make Next also do whatever the copied button does.
            for (int i = next.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                UnityEditor.Events.UnityEventTools.RemovePersistentListener(next.onClick, i);

            // Shown in the Editor only - at run time the panel writes "Next: COC2" etc.
            TMP_Text label = clone.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = "Next COC";

            nextProperty.objectReferenceValue = next;
            so.ApplyModifiedProperties();

            bool fitted = WidenCardToFit(row);

            EditorSceneManager.MarkSceneDirty(panel.gameObject.scene);
            Selection.activeGameObject = clone;

            Debug.Log("[StoryCompleteNextButtonTool] Added 'NextButton' to the Story Complete panel and assigned it. " +
                      "It shows as \"Next: COC2\" (etc.) once this COC is complete, and hides itself after COC4. " +
                      "Test it by pressing Play from the MainMenu scene." +
                      (fitted ? "" : "\n  The buttons are not in a Horizontal Layout Group - move NextButton into place by hand."),
                      clone);
        }

        // ==================================================================

        private static Button ButtonIn(SerializedObject so, string field)
        {
            SerializedProperty p = so.FindProperty(field);
            return p != null ? p.objectReferenceValue as Button : null;
        }

        /// <summary>
        /// Widens the card holding the button row so four buttons fit where three did. Only when the
        /// row is a Horizontal Layout Group inside a centred card - the Build Story Complete Panel
        /// layout. Returns false for a hand-made layout, which is left exactly as it is.
        /// </summary>
        private static bool WidenCardToFit(Transform row)
        {
            var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            if (rowLayout == null) return false;

            float needed = rowLayout.padding.left + rowLayout.padding.right;
            int count = 0;
            foreach (Transform child in row)
            {
                var rect = child as RectTransform;
                if (rect == null) continue;
                needed += rect.rect.width;
                count++;
            }
            if (count > 1) needed += rowLayout.spacing * (count - 1);

            var card = row.parent as RectTransform;
            if (card == null) return true;

            var cardLayout = card.GetComponent<LayoutGroup>();
            if (cardLayout != null) needed += cardLayout.padding.left + cardLayout.padding.right;

            bool centred = Mathf.Approximately(card.anchorMin.x, card.anchorMax.x);
            if (centred && card.sizeDelta.x < needed)
            {
                Undo.RecordObject(card, UndoName);
                card.sizeDelta = new Vector2(Mathf.Ceil(needed), card.sizeDelta.y);
            }
            return true;
        }
    }
}