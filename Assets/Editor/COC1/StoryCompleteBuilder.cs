using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEditor;
using TMPro;

namespace CircuitCrafter.EditorTools
{
    /// <summary>
    /// Builds the completion panel: CircuitCrafter → Build Story Complete Panel.
    ///
    /// THIS FILE MUST LIVE IN AN "Editor" FOLDER - Assets/Scripts/COC1/Editor/.
    /// Anything referencing UnityEditor is stripped from a player build, and the
    /// build fails on the MenuItem attribute itself if the file is anywhere else.
    ///
    /// It works out its own sorting order by looking at every Canvas already in the
    /// scene and going ten above the highest. A hard-coded number is how a panel
    /// ends up opening correctly and rendering behind something else, with nothing
    /// to say why.
    /// </summary>
    public static class StoryCompleteBuilder
    {
        private const string UndoName = "Build Story Complete Panel";

        private static readonly Color Dim      = new Color(0f, 0f, 0f, 0.78f);
        private static readonly Color CardBg   = new Color(0.106f, 0.125f, 0.137f, 1f);
        private static readonly Color Ink      = new Color(0.949f, 0.965f, 0.961f, 1f);
        private static readonly Color InkSoft  = new Color(0.686f, 0.729f, 0.722f, 1f);
        private static readonly Color Primary  = new Color(0.106f, 0.494f, 0.365f, 1f);
        private static readonly Color Quiet    = new Color(0.204f, 0.235f, 0.251f, 1f);

        [MenuItem("CircuitCrafter/Build Story Complete Panel")]
        public static void Build()
        {
            if (Object.FindAnyObjectByType<StoryCompletePanel>(FindObjectsInactive.Include) != null)
            {
                Debug.LogWarning("[StoryCompleteBuilder] This scene already has a StoryCompletePanel. Delete its canvas first if you want a fresh one.");
                return;
            }

            EnsureEventSystem();

            int order = HighestSortingOrder() + 10;

            var canvasGo = new GameObject("StoryCompleteCanvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(canvasGo, UndoName);

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

            // Match = 1 (height). At 2.23:1 on a phone, Match = 0 throws away 219
            // units of vertical space and the buttons walk off the bottom.
            scaler.matchWidthOrHeight = 1f;

            // ---- panel ----------------------------------------------------
            RectTransform panel = Node(canvasGo.transform, "StoryCompletePanel");
            Stretch(panel);

            RectTransform dim = Node(panel, "Dim", typeof(CanvasRenderer), typeof(Image));
            Stretch(dim);
            dim.GetComponent<Image>().color = Dim;

            RectTransform card = Node(panel, "Card", typeof(CanvasRenderer), typeof(Image));
            Centre(card, new Vector2(760f, 420f));
            card.GetComponent<Image>().color = CardBg;

            var cardLayout = card.gameObject.AddComponent<VerticalLayoutGroup>();
            cardLayout.padding = new RectOffset(44, 44, 40, 36);
            cardLayout.spacing = 16f;
            cardLayout.childControlWidth = true;
            cardLayout.childControlHeight = true;
            cardLayout.childForceExpandWidth = true;
            cardLayout.childForceExpandHeight = false;
            cardLayout.childAlignment = TextAnchor.MiddleCenter;

            TMP_Text title = Label(card, "Title", "Story mode complete", 40f, Ink);
            title.alignment = TextAlignmentOptions.Center;

            TMP_Text message = Label(card, "Message",
                "You have finished every task in this module.", 18f, InkSoft);
            message.alignment = TextAlignmentOptions.Center;

            RectTransform spacer = Node(card, "Spacer");
            spacer.gameObject.AddComponent<LayoutElement>().minHeight = 12f;

            // ---- buttons ---------------------------------------------------
            RectTransform row = Node(card, "Buttons");
            var rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 14f;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childControlWidth = false;
            rowLayout.childControlHeight = false;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            row.gameObject.AddComponent<LayoutElement>().minHeight = 60f;

            Button retry      = Btn(row, "RetryButton", "Retry", Quiet, Ink, 200f);
            Button mainMenu   = Btn(row, "MainMenuButton", "Main Menu", Quiet, Ink, 200f);
            Button assessment = Btn(row, "AssessmentButton", "Take Assessment", Primary, Color.white, 250f);

            // ---- the component ---------------------------------------------
            var panelComponent = Undo.AddComponent<StoryCompletePanel>(panel.gameObject);

            var so = new SerializedObject(panelComponent);
            Assign(so, "panelRoot", panel.gameObject);
            Assign(so, "titleText", title);
            Assign(so, "messageText", message);
            Assign(so, "retryButton", retry);
            Assign(so, "mainMenuButton", mainMenu);
            Assign(so, "assessmentButton", assessment);
            so.ApplyModifiedPropertiesWithoutUndo();

            Selection.activeGameObject = panel.gameObject;

            Debug.Log($"[StoryCompleteBuilder] Built StoryCompleteCanvas at sorting order {order} " +
                      $"(above every canvas already in this scene).\n" +
                      "  Still to do:\n" +
                      "   • Set Main Menu Scene Name on StoryCompletePanel.\n" +
                      "   • Set Assessment Scene Name, or wire On Take Assessment. Leave BOTH empty and that button hides itself.\n" +
                      "   • Call StoryCompletePanel.ShowNow() when the module ends - or wire your last button's OnClick to StoryCompletePanel.Show().");
        }

        // ==================================================================

        /// <summary>The highest sorting order of any Canvas in the scene, including
        /// inactive ones - a canvas that is switched off now can be switched on
        /// while this panel is up.</summary>
        private static int HighestSortingOrder()
        {
            int highest = 0;
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var canvas in canvases)
            {
                if (canvas != null && canvas.sortingOrder > highest) highest = canvas.sortingOrder;
            }
            return highest;
        }

        private static RectTransform Node(Transform parent, string name, params System.Type[] extra)
        {
            var types = new List<System.Type> { typeof(RectTransform) };
            types.AddRange(extra);

            var go = new GameObject(name, types.ToArray());
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Centre(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
        }

        private static TMP_Text Label(Transform parent, string name, string text, float size, Color color)
        {
            RectTransform rect = Node(parent, name);

            var tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.raycastTarget = false;
            return tmp;
        }

        private static Button Btn(Transform parent, string name, string text,
                                  Color background, Color ink, float width)
        {
            RectTransform rect = Node(parent, name, typeof(CanvasRenderer), typeof(Image), typeof(Button));
            rect.sizeDelta = new Vector2(width, 60f);

            var image = rect.GetComponent<Image>();
            image.color = background;

            var button = rect.GetComponent<Button>();
            button.targetGraphic = image;

            TMP_Text label = Label(rect, "Label", text, 19f, ink);
            Stretch((RectTransform)label.transform);
            label.alignment = TextAlignmentOptions.Center;

            return button;
        }

        private static void Assign(SerializedObject so, string propertyName, Object value)
        {
            var property = so.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogWarning($"[StoryCompleteBuilder] No field called '{propertyName}' - assign it by hand.");
                return;
            }
            property.objectReferenceValue = value;
        }

        /// <summary>Without an EventSystem no button responds to a click, and
        /// nothing in the Console says why.</summary>
        private static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include) != null) return;

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(go, UndoName);
            Debug.Log("[StoryCompleteBuilder] No EventSystem in this scene - created one.");
        }
    }
}
