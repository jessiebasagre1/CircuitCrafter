using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using CircuitCrafter.Repair;

namespace CircuitCrafter.Repair.EditorTools
{
    /// <summary>
    /// Builds the two stations at a repair site and the monitor screen that sits
    /// on one of them, and wires everything that can be wired without knowing the
    /// scene.
    ///
    /// MENU: CircuitCrafter > Build Repair Stations
    ///
    /// WHAT IT MAKES
    ///   BootScreenPanel     the monitor: POST output, POWER button, Step back
    ///   MonitorStation      walk up, press E, the screen comes on
    ///   SystemUnitStation   walk up, press E, the tower lies down
    ///
    /// WHY A SECOND BUILDER AND NOT AN EXTRA STEP IN THE FIRST ONE
    /// COC4PanelBuilder builds the three investigation panels, and they are a set:
    /// examine, diagnose, verify. These are the machine itself, they live in the
    /// world rather than on the HUD, and most sites will want to move and restyle
    /// them to fit the model they sit on. Keeping them separate means re-running
    /// either builder never disturbs the other, which matters a lot while you are
    /// still moving things around.
    ///
    /// SAFE TO RE-RUN. It skips anything already in the scene, so running it after
    /// hand-editing changes nothing. Delete an object and run it again to rebuild
    /// just that one.
    /// </summary>
    public static class COC4BootScreenBuilder
    {
        private const string UndoName = "Build Repair Stations";

        // A POST screen, so: near-black with the phosphor green everyone recognises.
        private static readonly Color CaseBg = new Color(0.09f, 0.10f, 0.11f, 0.98f);
        private static readonly Color ScreenBg = new Color(0.02f, 0.03f, 0.02f, 1f);
        private static readonly Color ScreenInk = new Color(0.62f, 0.93f, 0.66f, 1f);
        private static readonly Color ButtonBg = new Color(0.18f, 0.22f, 0.20f, 1f);
        private static readonly Color TextMain = new Color(0.91f, 0.93f, 0.91f, 1f);

        // ==================================================================

        [MenuItem("CircuitCrafter/Build Repair Stations")]
        public static void Build()
        {
            Canvas canvas = FindOrCreateCanvas();
            EnsureEventSystem();

            var built = new List<string>();

            GameObject bootScreen = BuildBootScreen(canvas.transform, built);
            RepairStation monitor = EnsureStation("MonitorStation", "the monitor", built);
            RepairStation systemUnit = EnsureStation("SystemUnitStation", "the system unit", built);

            WireStations(bootScreen, monitor, systemUnit);

            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);

            if (built.Count == 0)
            {
                Debug.Log("[COC4BootScreenBuilder] Everything already exists in this scene - nothing was created. " +
                          "Delete an object and re-run to rebuild just that one.");
                return;
            }

            Debug.Log($"[COC4BootScreenBuilder] Built: {string.Join(", ", built)}.\n\n" +
                      "STILL TO DO BY HAND - none of this can be guessed from the scene:\n" +
                      "  1. Move each station object to where the learner should stand, or assign\n" +
                      "     Station Point. Nothing happens until the learner can get near it.\n" +
                      "  2. Both stations: assign Player Camera (the walking camera) and\n" +
                      "     Station Camera (a new camera framed on the monitor / on where the\n" +
                      "     tower ends up lying down).\n" +
                      "  3. SystemUnitStation: assign Moved Object (the ROOT of the machine -\n" +
                      "     case AND internals) and Entered Pose (a marker where it should lie).\n" +
                      "  4. SystemUnitStation: Enable On Enter -> hotbar, part description panel.\n" +
                      "     Both stations: Disable On Enter -> mobile controls, waypoint arrow.\n" +
                      "  5. Check the player object is tagged \"Player\".\n" +
                      "  6. Fill in each TicketFault's Boot Symptom - what the machine DOES,\n" +
                      "     never what is wrong with it.\n" +
                      "  7. Set your Return To Hub button's Interactable to OFF, then add it to\n" +
                      "     On Boot Succeeded with the tick ON, so a ticket cannot be closed\n" +
                      "     until the machine has actually been started and passed.\n" +
                      "  8. Give Screen Text a monospace font if you have one - the output is\n" +
                      "     laid out in columns and a proportional face makes it ragged.");
        }

        // ==================================================================
        // The monitor
        // ==================================================================

        private static GameObject BuildBootScreen(Transform canvas, List<string> built)
        {
            GameObject existing = Find(canvas, "BootScreenPanel");
            if (existing != null) return existing;

            RectTransform panel = Panel(canvas, "BootScreenPanel", new Vector2(760, 600));

            Label(panel, "Title", "MONITOR", new Vector2(24, 16), new Vector2(400, 26), 16, TextMain);

            // The screen is its own child rather than the panel's background, so the
            // black overlay below can cover exactly the screen and nothing else.
            RectTransform screen = Place(panel, "Screen", new Vector2(24, 54), new Vector2(712, 430),
                                         typeof(CanvasRenderer), typeof(Image));
            screen.GetComponent<Image>().color = ScreenBg;

            TMP_Text screenText = Label(screen, "ScreenText",
                                        "The computer is switched off.\n\nPress POWER to start it.",
                                        new Vector2(22, 20), new Vector2(668, 390), 16, ScreenInk);
            screenText.alignment = TextAlignmentOptions.TopLeft;

            // Sits over the screen, not over the buttons - the monitor reads as dead
            // rather than as a blank panel, the same trick OSInstallView plays with
            // its reboot overlay.
            RectTransform off = Place(screen, "ScreenOffOverlay", Vector2.zero, new Vector2(712, 430),
                                      typeof(CanvasRenderer), typeof(Image));
            off.GetComponent<Image>().color = ScreenBg;

            Button power = Btn(panel, "PowerButton", "POWER", new Vector2(24, 508), new Vector2(200, 54));
            Button close = Btn(panel, "CloseButton", "Step back", new Vector2(600, 516), new Vector2(136, 40));

            var station = panel.gameObject.AddComponent<PCPowerStation>();
            var so = new SerializedObject(station);
            Assign(so, "panelRoot", panel.gameObject);
            Assign(so, "screenText", screenText);
            Assign(so, "powerButton", power);
            Assign(so, "closeButton", close);
            Assign(so, "screenOffOverlay", off.gameObject);
            Assign(so, "coordinator", Object.FindAnyObjectByType<TicketFaultCoordinator>());
            so.ApplyModifiedPropertiesWithoutUndo();

            built.Add("BootScreenPanel");
            return panel.gameObject;
        }

        // ==================================================================
        // The stations
        // ==================================================================

        /// <summary>Creates the station but assigns no scene references, deliberately.
        /// Where the learner stands, which object is the system unit, and where it
        /// should end up are scene decisions no script can read; guessing them and
        /// getting it wrong is worse than an empty field, because an empty field is
        /// obvious the moment you look at the Inspector.</summary>
        private static RepairStation EnsureStation(string objectName, string stationName, List<string> built)
        {
            foreach (var candidate in Object.FindObjectsByType<RepairStation>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (candidate != null && candidate.name == objectName) return candidate;
            }

            var go = new GameObject(objectName, typeof(RepairStation));
            Undo.RegisterCreatedObjectUndo(go, UndoName);

            var station = go.GetComponent<RepairStation>();

            var so = new SerializedObject(station);
            var nameProperty = so.FindProperty("stationName");
            if (nameProperty != null) nameProperty.stringValue = stationName;

            // The main camera is very likely the walking camera. Being wrong here
            // costs one drag to fix; being empty costs a confused half hour.
            if (Camera.main != null) Assign(so, "playerCamera", Camera.main);
            so.ApplyModifiedPropertiesWithoutUndo();

            built.Add(objectName);
            return station;
        }

        /// <summary>Arriving at the monitor is what brings the screen up, and leaving
        /// is what puts it away. Wired here rather than left as a to-do because it is
        /// the one connection that is the same in every scene.</summary>
        private static void WireStations(GameObject bootScreen, RepairStation monitor, RepairStation systemUnit)
        {
            if (bootScreen == null || monitor == null) return;

            var power = bootScreen.GetComponent<PCPowerStation>();
            if (power == null) return;

            var so = new SerializedObject(power);
            Assign(so, "monitorStation", monitor);
            Assign(so, "systemUnitStation", systemUnit);
            so.ApplyModifiedPropertiesWithoutUndo();

            if (monitor.onEnter.GetPersistentEventCount() == 0)
                AddCall(monitor.onEnter, power, "Open");

            if (monitor.onExit.GetPersistentEventCount() == 0)
                AddCall(monitor.onExit, power, "Close");

            EditorUtility.SetDirty(monitor);
        }

        private static void AddCall(UnityEvent unityEvent, Object target, string methodName)
        {
            if (unityEvent == null || target == null) return;

            if (target.GetType().GetMethod(methodName) == null)
            {
                Debug.LogWarning($"[COC4BootScreenBuilder] No {methodName}() on {target.GetType().Name} - wire that event by hand.");
                return;
            }

            UnityEventTools.AddVoidPersistentListener(
                unityEvent,
                (UnityAction)System.Delegate.CreateDelegate(typeof(UnityAction), target, methodName));
        }

        // ==================================================================
        // Building blocks
        // ==================================================================

        private static void Assign(SerializedObject so, string propertyName, Object value)
        {
            var property = so.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogWarning($"[COC4BootScreenBuilder] No field called '{propertyName}' - assign it by hand.");
                return;
            }
            property.objectReferenceValue = value;
        }

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

            go.GetComponent<Image>().color = CaseBg;
            return rect;
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

            Debug.Log("[COC4BootScreenBuilder] No Canvas in this scene - created one.");
            return canvas;
        }

        /// <summary>Without an EventSystem no UI button in the scene responds to a
        /// click, and nothing says why. Worth checking every time.</summary>
        private static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(go, UndoName);
            Debug.Log("[COC4BootScreenBuilder] No EventSystem in this scene - created one. Without it no button works.");
        }
    }
}