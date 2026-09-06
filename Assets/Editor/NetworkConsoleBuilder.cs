// NetworkConsoleBuilder.cs
//
// EDITOR SCRIPT. Must live in a folder called "Editor", beside DeviceConfigPanelBuilder.cs:
//
//     Assets/Scripts/COC2/Editor/NetworkConsoleBuilder.cs
//
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using CSSSimulation.Networking;

namespace CSSSimulation.NetworkingEditor
{
    /// <summary>
    /// Builds the Command Prompt window that a ConnectivityCheckStation prints its ping into, and
    /// wires the station to it.
    ///
    /// A SEPARATE BUILDER, ON PURPOSE
    ///
    /// This could have been another variant inside DeviceConfigPanelBuilder, and it deliberately is
    /// not. That window builds a settings dialog for one component; this builds a console for a
    /// different component, with different inputs and different wiring. Folding them together would
    /// mean one Inspector where half the fields are greyed out depending on a mode dropdown, and a
    /// file where every method has to ask which thing it is building. Two small windows that each do
    /// one thing are easier to use and much easier to change later.
    ///
    /// HOW TO USE IT
    ///   1. Tools > CircuitCrafter > Command Prompt Builder
    ///   2. Type the machine name, drag in that machine's ConnectivityCheckStation.
    ///   3. Build. Ctrl+Z undoes it in one step.
    ///
    /// A NOTE ON THE FONT
    ///
    /// A real command prompt is monospaced, and column alignment is most of why it reads as one.
    /// TMP's default font is not monospaced, so this leaves the font asset alone rather than
    /// pretending: assign a mono TMP font asset to the Console Text afterwards if you have one.
    /// Everything else about the window is right, and the text is legible either way.
    /// </summary>
    public class NetworkConsoleBuilder : EditorWindow
    {
        string machineName = "Server";
        Canvas targetCanvas;
        ConnectivityCheckStation station;
        bool dimBackdrop = true;

        static readonly Color Backdrop = new Color32(0x00, 0x00, 0x00, 0xA6);
        static readonly Color ConsoleBg = new Color32(0x0C, 0x0C, 0x0C, 255);
        static readonly Color ConsoleInk = new Color32(0xCC, 0xCC, 0xCC, 255);
        static readonly Color TitleBarBg = new Color32(0x1F, 0x1F, 0x1F, 255);
        static readonly Color TitleInk = new Color32(0xE6, 0xE6, 0xE6, 255);
        static readonly Color WindowEdge = new Color32(0x3C, 0x3C, 0x3C, 255);
        static readonly Color CloseHot = new Color32(0xC4, 0x2B, 0x1C, 255);

        static readonly Color FieldFace = new Color32(0x16, 0x16, 0x16, 255);
        static readonly Color PingFace = new Color32(0x2B, 0x2B, 0x2B, 255);
        static readonly Color PingInk = new Color32(0xE6, 0xE6, 0xE6, 255);

        const float WinWidth = 720f;
        const float WinHeight = 420f;
        const float TitleHeight = 32f;
        const float InputRowHeight = 34f;

        [MenuItem("Tools/CircuitCrafter/Command Prompt Builder")]
        static void ShowWindow()
        {
            NetworkConsoleBuilder w = GetWindow<NetworkConsoleBuilder>(true, "Command Prompt Builder");
            w.minSize = new Vector2(420, 300);
            w.targetCanvas = FindFirstObjectByType<Canvas>();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Build a Command Prompt window", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Creates the window under the canvas, starts it inactive, and - if you drag a " +
                "ConnectivityCheckStation in below - fills in Console Panel and Console Text and " +
                "wires the close button to CloseConsole().\n\n" +
                "Assign a monospaced TMP font asset to the Console Text afterwards if you have one.",
                MessageType.Info);
            EditorGUILayout.Space(8);

            machineName = EditorGUILayout.TextField(
                new GUIContent("Machine name", "Names the objects: 'Server' gives ServerConsolePanel."),
                machineName);

            targetCanvas = (Canvas)EditorGUILayout.ObjectField(
                new GUIContent("Canvas", "Where the window goes. Leave empty and one is created."),
                targetCanvas, typeof(Canvas), true);

            station = (ConnectivityCheckStation)EditorGUILayout.ObjectField(
                new GUIContent("Wire into station", "That machine's ConnectivityCheckStation."),
                station, typeof(ConnectivityCheckStation), true);

            EditorGUILayout.Space(6);
            dimBackdrop = EditorGUILayout.Toggle(
                new GUIContent("Dim the scene behind", "A dark sheet behind the window, so it does " +
                                                       "not float over the live 3D view."),
                dimBackdrop);

            EditorGUILayout.Space(12);

            if (station != null && station.consolePanel != null)
                EditorGUILayout.HelpBox(
                    "'" + station.stationName + "' already has a Console Panel assigned ('" +
                    station.consolePanel.name + "'). Building again will point it at the new " +
                    "window and leave the old one orphaned in the scene.", MessageType.Warning);

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(machineName)))
            {
                if (GUILayout.Button("Build window", GUILayout.Height(32)))
                    Build();
            }
        }

        void Build()
        {
            Canvas canvas = targetCanvas != null ? targetCanvas : CreateCanvas();
            string safeName = machineName.Trim().Replace(" ", "");

            GameObject root = new GameObject(safeName + "ConsolePanel", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(root, "Build Command Prompt");
            root.transform.SetParent(canvas.transform, false);
            Stretch(Rect(root));

            Transform host = root.transform;
            if (dimBackdrop)
            {
                GameObject dim = Panel(root.transform, "Backdrop", Backdrop);
                Stretch(Rect(dim));
                host = dim.transform;
            }

            // ---- the window
            GameObject window = Panel(host, "Window", ConsoleBg);
            RectTransform wrt = Rect(window);
            wrt.anchorMin = wrt.anchorMax = wrt.pivot = new Vector2(0.5f, 0.5f);
            wrt.sizeDelta = new Vector2(WinWidth, WinHeight);
            wrt.anchoredPosition = Vector2.zero;
            AddOutline(window, WindowEdge);

            // ---- title bar
            GameObject bar = Panel(window.transform, "TitleBar", TitleBarBg);
            RectTransform brt = Rect(bar);
            brt.anchorMin = new Vector2(0f, 1f);
            brt.anchorMax = new Vector2(1f, 1f);
            brt.pivot = new Vector2(0.5f, 1f);
            brt.offsetMin = new Vector2(0f, -TitleHeight);
            brt.offsetMax = Vector2.zero;

            TextMeshProUGUI title = Label(bar.transform, "TitleText",
                                          "Command Prompt - " + machineName.Trim(), 12.5f, TitleInk);
            RectTransform trt = Rect(title);
            Stretch(trt);
            trt.offsetMin = new Vector2(12f, 0f);
            trt.offsetMax = new Vector2(-96f, 0f);
            title.alignment = TextAlignmentOptions.Left;

            Button close = BuildButton(bar.transform, "CloseButton", "×", TitleBarBg, TitleInk);
            RectTransform crt = Rect(close);
            crt.anchorMin = new Vector2(1f, 0f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 0.5f);
            crt.sizeDelta = new Vector2(46f, 0f);
            crt.anchoredPosition = Vector2.zero;

            ColorBlock cb = close.colors;
            cb.highlightedColor = CloseHot;
            cb.pressedColor = new Color32(0x9C, 0x22, 0x16, 255);
            close.colors = cb;

            // ---- the readout
            //
            // Anchored to the TOP of the window and left to grow downward, because a console reads
            // downward. Anchoring it to the middle would make short output float in dead space and
            // long output creep off both edges at once.
            TextMeshProUGUI console = Label(window.transform, "ConsoleText",
                                            SamplePreview(), 15f, ConsoleInk);
            RectTransform rrt = Rect(console);
            rrt.anchorMin = new Vector2(0f, 0f);
            rrt.anchorMax = new Vector2(1f, 1f);
            rrt.offsetMin = new Vector2(16f, InputRowHeight + 20f);
            rrt.offsetMax = new Vector2(-16f, -(TitleHeight + 14f));
            console.alignment = TextAlignmentOptions.TopLeft;
            console.enableWordWrapping = true;
            console.richText = false;

            // ---- the command line
            //
            // Laid out as one row pinned to the bottom: the C:\> prompt, the box, the button. The
            // prompt is a separate label rather than placeholder text inside the field, because a
            // placeholder disappears the moment you type and the prompt should not.
            GameObject row = new GameObject("CommandRow", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(row, "Build Command Prompt");
            row.transform.SetParent(window.transform, false);
            RectTransform rowRt = Rect(row);
            rowRt.anchorMin = new Vector2(0f, 0f);
            rowRt.anchorMax = new Vector2(1f, 0f);
            rowRt.pivot = new Vector2(0.5f, 0f);
            rowRt.sizeDelta = new Vector2(-32f, InputRowHeight);
            rowRt.anchoredPosition = new Vector2(0f, 12f);

            TextMeshProUGUI prompt = Label(row.transform, "Prompt", "C:\\>", 15f, ConsoleInk);
            RectTransform prt = Rect(prompt);
            prt.anchorMin = new Vector2(0f, 0f);
            prt.anchorMax = new Vector2(0f, 1f);
            prt.pivot = new Vector2(0f, 0.5f);
            prt.sizeDelta = new Vector2(44f, 0f);
            prt.anchoredPosition = Vector2.zero;
            prompt.alignment = TextAlignmentOptions.Left;

            TMP_InputField input = BuildInputField(row.transform, "CommandInput", 48f, 104f);

            Button ping = BuildButton(row.transform, "PingButton", "Ping", PingFace, PingInk);
            RectTransform pgt = Rect(ping);
            pgt.anchorMin = new Vector2(1f, 0f);
            pgt.anchorMax = new Vector2(1f, 1f);
            pgt.pivot = new Vector2(1f, 0.5f);
            pgt.sizeDelta = new Vector2(92f, 0f);
            pgt.anchoredPosition = Vector2.zero;

            root.SetActive(false);

            if (station != null)
            {
                Undo.RecordObject(station, "Wire Command Prompt");
                station.consolePanel = root;
                station.consoleText = console;
                station.commandInput = input;
                station.pingButton = ping;
                if (station.consoleLines < 10) station.consoleLines = 10;
                UnityEventTools.AddVoidPersistentListener(close.onClick, station.CloseConsole);
                EditorUtility.SetDirty(station);
            }

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);

            Debug.Log("[NetworkConsoleBuilder] Built '" + root.name + "'" +
                      (station != null
                           ? " and wired it into '" + station.stationName + "'."
                           : ". No station assigned, so set Console Panel and Console Text by hand."),
                      root);
        }

        /// <summary>
        /// Placeholder text so the window is not an empty black rectangle in the editor.
        ///
        /// It is overwritten the first frame a check runs, but a builder that leaves you looking at
        /// nothing gives you no way to judge the size, the padding or the font without entering
        /// Play mode.
        /// </summary>
        string SamplePreview()
        {
            return "Microsoft Windows [Version 10.0.19045.4046]\n" +
                   "(c) Microsoft Corporation. All rights reserved.\n\n" +
                   "C:\\> _";
        }

        // ------------------------------------------------------------------ helpers

        Canvas CreateCanvas()
        {
            GameObject go = new GameObject("UI Canvas", typeof(Canvas), typeof(CanvasScaler),
                                           typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(go, "Build Command Prompt");
            Canvas c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler s = go.GetComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1920f, 1080f);
            s.matchWidthOrHeight = 0.5f;

            if (FindFirstObjectByType<EventSystem>() == null)
            {
                GameObject es = new GameObject("EventSystem", typeof(EventSystem),
                                               typeof(StandaloneInputModule));
                Undo.RegisterCreatedObjectUndo(es, "Build Command Prompt");
            }
            return c;
        }

        GameObject Panel(Transform parent, string name, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build Command Prompt");
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = color;
            return go;
        }

        TextMeshProUGUI Label(Transform parent, string name, string text, float size, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build Command Prompt");
            go.transform.SetParent(parent, false);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.raycastTarget = false;
            return t;
        }

        /// <summary>
        /// A TMP_InputField built the way TMP builds one - background, a Text Area carrying a
        /// RectMask2D, Placeholder and Text inside it. Without the mask a long command scrolls
        /// straight out of the box and across the rest of the window.
        /// </summary>
        TMP_InputField BuildInputField(Transform parent, string name, float leftInset, float rightInset)
        {
            GameObject go = Panel(parent, name, FieldFace);
            RectTransform rt = Rect(go);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = new Vector2(leftInset, 0f);
            rt.offsetMax = new Vector2(-rightInset, 0f);

            GameObject area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            Undo.RegisterCreatedObjectUndo(area, "Build Command Prompt");
            area.transform.SetParent(go.transform, false);
            RectTransform art = Rect(area);
            Stretch(art);
            art.offsetMin = new Vector2(8f, 4f);
            art.offsetMax = new Vector2(-8f, -4f);

            TextMeshProUGUI ph = Label(area.transform, "Placeholder", "ping 192.168.1.20", 14f,
                                       new Color32(0x6A, 0x6A, 0x6A, 255));
            Stretch(Rect(ph));
            ph.alignment = TextAlignmentOptions.Left;

            TextMeshProUGUI txt = Label(area.transform, "Text", "", 14f, ConsoleInk);
            Stretch(Rect(txt));
            txt.alignment = TextAlignmentOptions.Left;
            txt.richText = false;

            TMP_InputField field = go.AddComponent<TMP_InputField>();
            field.textViewport = art;
            field.textComponent = txt;
            field.placeholder = ph;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = 60;
            field.caretWidth = 2;
            field.customCaretColor = true;
            field.caretColor = ConsoleInk;
            field.selectionColor = new Color32(0x33, 0x66, 0x99, 120);
            return field;
        }

        Button BuildButton(Transform parent, string name, string text, Color face, Color ink)
        {
            GameObject go = Panel(parent, name, face);
            Button b = go.AddComponent<Button>();
            b.targetGraphic = go.GetComponent<Image>();

            TextMeshProUGUI t = Label(go.transform, "Text", text, 13f, ink);
            Stretch(Rect(t));
            t.alignment = TextAlignmentOptions.Center;
            return b;
        }

        void AddOutline(GameObject panel, Color edge)
        {
            GameObject line = new GameObject("Edge", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(line, "Build Command Prompt");
            line.transform.SetParent(panel.transform, false);
            line.transform.SetAsFirstSibling();
            Image img = line.AddComponent<Image>();
            img.color = edge;
            img.raycastTarget = false;
            RectTransform rt = Rect(line);
            Stretch(rt);
            rt.offsetMin = new Vector2(-1f, -1f);
            rt.offsetMax = new Vector2(1f, 1f);
        }

        static RectTransform Rect(GameObject go) { return (RectTransform)go.transform; }
        static RectTransform Rect(Component c) { return (RectTransform)c.transform; }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}