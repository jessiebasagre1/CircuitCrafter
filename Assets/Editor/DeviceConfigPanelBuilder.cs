// DeviceConfigPanelBuilder.cs
//
// EDITOR SCRIPT. This file MUST live in a folder called "Editor" - Unity compiles anything under
// an Editor folder into a separate assembly that is stripped from the build. Put it anywhere else
// and the `using UnityEditor;` line below will break your player build with a compile error.
//
//     Assets/Scripts/COC2/Editor/DeviceConfigPanelBuilder.cs
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
    /// Builds the configuration panel for a DeviceConfigStation: a Windows-style desktop with the
    /// TCP/IPv4 Properties dialog on it, the job order open in Notepad beside it, and a taskbar.
    ///
    /// WHAT THIS IS AND IS NOT
    ///
    /// It is a one-shot generator, not a system. It creates ordinary GameObjects with ordinary
    /// components and then gets out of the way - nothing it makes depends on this file still
    /// existing, nothing is regenerated at runtime, and every object it produces can be moved,
    /// restyled or deleted by hand afterwards. That is the only kind of builder worth having: it
    /// saves the forty minutes of dragging RectTransforms around and leaves you owning the result.
    ///
    /// It also wires the new objects into a DeviceConfigStation for you if you point it at one,
    /// which removes the other half of the tedium - and the class of bug where the panel exists,
    /// looks right, and does nothing because Apply Button was never assigned.
    ///
    /// WHY IT LOOKS LIKE THE REAL DIALOG
    ///
    /// A trainee who has set a static IP on a real machine has seen exactly one screen: Internet
    /// Protocol Version 4 (TCP/IPv4) Properties, with the two radio buttons and the three boxes.
    /// Reproducing it - the explanatory paragraph, the greyed automatic option, the OK/Cancel pair
    /// bottom right - means what they practise here is what they will meet in the shop. A generic
    /// form with three boxes would teach the values but not the screen.
    ///
    /// HOW TO USE IT
    ///   1. Tools > CircuitCrafter > Config Panel Builder
    ///   2. Pick the variant, type the machine name, drag in the DeviceConfigStation (optional).
    ///   3. Build. Ctrl+Z undoes the whole thing in one step if you don't like it.
    /// </summary>
    public class DeviceConfigPanelBuilder : EditorWindow
    {
        enum Variant { WorkstationIPv4, WirelessRouter }

        Variant variant = Variant.WorkstationIPv4;
        string machineName = "Server";
        Canvas targetCanvas;
        DeviceConfigStation station;
        bool includeDesktop = true;
        bool includeJobOrderNote = true;

        // ------------------------------------------------------------------ palette
        //
        // Windows 11 light, sampled rather than invented. The greys are the ones the OS actually
        // uses, which is most of why a mock reads as real - close-but-wrong greys are the tell.

        static readonly Color Wallpaper = new Color32(0x10, 0x3B, 0x66, 255);
        static readonly Color Taskbar = new Color32(0xF3, 0xF3, 0xF3, 250);
        static readonly Color WindowBody = new Color32(0xF0, 0xF0, 0xF0, 255);
        static readonly Color TitleBar = new Color32(0xFF, 0xFF, 0xFF, 255);
        static readonly Color WindowEdge = new Color32(0xB8, 0xB8, 0xB8, 255);
        static readonly Color InkStrong = new Color32(0x1A, 0x1A, 0x1A, 255);
        static readonly Color InkBody = new Color32(0x20, 0x20, 0x20, 255);
        static readonly Color InkMuted = new Color32(0x6B, 0x6B, 0x6B, 255);
        static readonly Color AccentBlue = new Color32(0x00, 0x67, 0xC0, 255);
        static readonly Color ButtonFace = new Color32(0xFD, 0xFD, 0xFD, 255);
        static readonly Color FieldFace = new Color32(0xFF, 0xFF, 0xFF, 255);
        static readonly Color CloseHot = new Color32(0xC4, 0x2B, 0x1C, 255);
        static readonly Color ErrorInk = new Color32(0xA8, 0x1D, 0x18, 255);
        static readonly Color NotepadFace = new Color32(0xFF, 0xFF, 0xFF, 255);

        const float ContentPad = 24f;
        const float RowHeight = 30f;
        const float RowGap = 12f;
        const float LabelWidth = 170f;

        [MenuItem("Tools/CircuitCrafter/Config Panel Builder")]
        static void ShowWindow()
        {
            DeviceConfigPanelBuilder w = GetWindow<DeviceConfigPanelBuilder>(true, "Config Panel Builder");
            w.minSize = new Vector2(420, 360);
            w.targetCanvas = FindFirstObjectByType<Canvas>();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Build a Windows-style settings panel", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Creates the panel under the canvas, starts it inactive, and - if you drag a " +
                "DeviceConfigStation in below - fills in Panel, Apply Button, Close Button, " +
                "Result Text, Job Order Text and every Field row for you.\n\n" +
                "Ctrl+Z undoes the whole build in one step.",
                MessageType.Info);
            EditorGUILayout.Space(8);

            variant = (Variant)EditorGUILayout.EnumPopup(
                new GUIContent("Variant", "Workstation builds the TCP/IPv4 Properties dialog. " +
                                          "Router builds a wireless settings dialog."), variant);

            machineName = EditorGUILayout.TextField(
                new GUIContent("Machine name", "Used to name the objects: 'Server' gives " +
                                               "ServerConfigPanel, Field_Server_IP and so on."),
                machineName);

            targetCanvas = (Canvas)EditorGUILayout.ObjectField(
                new GUIContent("Canvas", "Where the panel goes. Leave empty and one is created."),
                targetCanvas, typeof(Canvas), true);

            station = (DeviceConfigStation)EditorGUILayout.ObjectField(
                new GUIContent("Wire into station", "Optional but strongly recommended."),
                station, typeof(DeviceConfigStation), true);

            EditorGUILayout.Space(6);
            includeDesktop = EditorGUILayout.Toggle(
                new GUIContent("Desktop and taskbar", "The wallpaper and taskbar behind the dialog. " +
                                                      "Untick for just the dialog on a dim backdrop."),
                includeDesktop);
            includeJobOrderNote = EditorGUILayout.Toggle(
                new GUIContent("Job order in Notepad", "Opens the job order in a Notepad window " +
                                                       "beside the dialog, which is where a real " +
                                                       "technician would have it."),
                includeJobOrderNote);

            EditorGUILayout.Space(12);

            if (string.IsNullOrWhiteSpace(machineName))
                EditorGUILayout.HelpBox("Machine name is empty.", MessageType.Warning);

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(machineName)))
            {
                if (GUILayout.Button("Build panel", GUILayout.Height(32)))
                    Build();
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(
                station != null
                    ? "Will wire into: " + station.name
                    : "No station assigned - you will have to fill the Inspector by hand.",
                EditorStyles.miniLabel);
        }

        // ------------------------------------------------------------------ field definitions

        struct FieldDef
        {
            public string key;       // used in object names
            public string label;     // shown to the player
            public string expected;  // pre-filled into the station's Expected Value
            public string placeholder;
            public string hint;
            public string[] alsoAccept;

            public FieldDef(string k, string l, string e, string p, string h, params string[] alt)
            { key = k; label = l; expected = e; placeholder = p; hint = h; alsoAccept = alt; }
        }

        FieldDef[] Fields()
        {
            if (variant == Variant.WirelessRouter)
                return new[]
                {
                    new FieldDef("SSID", "Network name (SSID)", "CSS-LAB", "Network name",
                                 "The network name from the job order, spelled exactly."),
                    new FieldDef("Security", "Security", "WPA2", "WPA2",
                                 "WEP is broken and open is no security at all.", "WPA2-PSK"),
                    new FieldDef("Passphrase", "Passphrase", "TesdaLab2026", "At least 8 characters",
                                 "Must match the job order so the client can join."),
                    new FieldDef("Channel", "Channel", "6", "1-11",
                                 "1, 6 and 11 are the non-overlapping 2.4 GHz channels.")
                };

            return new[]
            {
                new FieldDef("IP", "IP address:", DefaultIp(), "___ . ___ . ___ . ___",
                             "Each host on a subnet needs its own address."),
                new FieldDef("Mask", "Subnet mask:", "255.255.255.0", "___ . ___ . ___ . ___",
                             "A /24 mask puts all 254 hosts on one network."),
                new FieldDef("Gateway", "Default gateway:", "192.168.1.1", "___ . ___ . ___ . ___",
                             "The gateway is the router, and it is the same for every machine here.")
            };
        }

        string DefaultIp()
        {
            // A guess the user will almost always keep, and a wrong guess costs one keystroke.
            // Two machines must not share an address, so the client gets a different last octet.
            return machineName.ToLower().Contains("client") ? "192.168.1.20" : "192.168.1.10";
        }

        string WindowTitle()
        {
            return variant == Variant.WirelessRouter
                ? "Wireless Settings - 192.168.1.1"
                : "Internet Protocol Version 4 (TCP/IPv4) Properties";
        }

        string IntroText()
        {
            return variant == Variant.WirelessRouter
                ? "Set the wireless network for this router. Client devices will use these settings to join it."
                : "You can get IP settings assigned automatically if your network supports this " +
                  "capability. Otherwise, you need to ask your network administrator for the " +
                  "appropriate IP settings.";
        }

        string RadioOffLabel()
        {
            return variant == Variant.WirelessRouter
                ? "Open - no security"
                : "Obtain an IP address automatically";
        }

        string RadioOnLabel()
        {
            return variant == Variant.WirelessRouter
                ? "WPA2 Personal (recommended)"
                : "Use the following IP address:";
        }

        string JobOrderBody()
        {
            if (variant == Variant.WirelessRouter)
                return "JOB ORDER JO-2601\r\nWireless router\r\n\r\n" +
                       "SSID          CSS-LAB\r\n" +
                       "Security      WPA2\r\n" +
                       "Passphrase    TesdaLab2026\r\n" +
                       "Channel       6\r\n";

            bool client = machineName.ToLower().Contains("client");
            return "JOB ORDER JO-2601\r\n" + (client ? "Client" : "Server") + " workstation\r\n\r\n" +
                   "IP address       " + DefaultIp() + "\r\n" +
                   "Subnet mask      255.255.255.0\r\n" +
                   "Default gateway  192.168.1.1\r\n\r\n" +
                   "Note: each workstation must have its\r\nown address on this subnet.\r\n";
        }

        // ------------------------------------------------------------------ the build

        void Build()
        {
            Canvas canvas = targetCanvas != null ? targetCanvas : CreateCanvas();
            FieldDef[] defs = Fields();
            string safeName = machineName.Trim().Replace(" ", "");

            // ---- root, full screen, inactive.
            GameObject root = new GameObject(safeName + "ConfigPanel", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(root, "Build Config Panel");
            root.transform.SetParent(canvas.transform, false);
            Stretch(Rect(root));

            // A backdrop always exists even without the desktop, because a dialog floating over the
            // live 3D scene reads as a bug rather than as a screen.
            GameObject backdrop = Panel(root.transform, "Desktop",
                                        includeDesktop ? Wallpaper : new Color(0f, 0f, 0f, 0.65f));
            Stretch(Rect(backdrop));

            if (includeDesktop) BuildTaskbar(backdrop.transform);

            TextMeshProUGUI jobOrderText = null;
            if (includeJobOrderNote)
                jobOrderText = BuildNotepad(backdrop.transform);

            // ---- the dialog
            float bodyTop = 60f;
            float rowsTop = bodyTop + (variant == Variant.WirelessRouter ? 96f : 126f);
            float rowsHeight = defs.Length * RowHeight + (defs.Length - 1) * RowGap;
            float resultTop = rowsTop + rowsHeight + 14f;
            float resultHeight = 72f;
            float winHeight = resultTop + resultHeight + 12f + 32f + ContentPad;
            const float winWidth = 620f;

            GameObject window = Panel(backdrop.transform, "Window", WindowBody);
            RectTransform winRt = Rect(window);
            winRt.anchorMin = winRt.anchorMax = winRt.pivot = new Vector2(0.5f, 0.5f);
            winRt.sizeDelta = new Vector2(winWidth, winHeight);
            winRt.anchoredPosition = new Vector2(includeJobOrderNote ? 180f : 0f, 0f);
            AddOutline(window, WindowEdge);

            Button titleClose = BuildTitleBar(window.transform, WindowTitle());

            // Intro paragraph - the real dialog has one, and it is the line that tells a trainee
            // this screen is the one they have seen before.
            TextMeshProUGUI intro = Label(window.transform, "IntroText", IntroText(), 13f, InkBody);
            TopLeft(Rect(intro), ContentPad, bodyTop, winWidth - ContentPad * 2f, 52f);
            intro.alignment = TextAlignmentOptions.TopLeft;

            BuildRadio(window.transform, "Radio_Auto", RadioOffLabel(), false,
                       ContentPad + 4f, bodyTop + 58f, winWidth);
            BuildRadio(window.transform, "Radio_Manual", RadioOnLabel(), true,
                       ContentPad + 4f, bodyTop + 84f, winWidth);

            // ---- the fields
            List<TMP_InputField> inputs = new List<TMP_InputField>();
            for (int i = 0; i < defs.Length; i++)
            {
                float y = rowsTop + i * (RowHeight + RowGap);
                inputs.Add(BuildFieldRow(window.transform, safeName, defs[i], y, winWidth));
            }

            // ---- result readout
            TextMeshProUGUI result = Label(window.transform, "ResultText", "", 12.5f, ErrorInk);
            TopLeft(Rect(result), ContentPad, resultTop, winWidth - ContentPad * 2f, resultHeight);
            result.alignment = TextAlignmentOptions.TopLeft;

            // ---- OK / Cancel, bottom right, in that order, as every Windows dialog has them
            Button apply = BuildButton(window.transform, "ApplyButton",
                                       variant == Variant.WirelessRouter ? "Apply" : "OK",
                                       AccentBlue, Color.white);
            BottomRight(Rect(apply), ContentPad + 100f, ContentPad, 92f, 32f);

            Button close = BuildButton(window.transform, "CloseButton", "Cancel", ButtonFace, InkBody);
            BottomRight(Rect(close), ContentPad, ContentPad, 92f, 32f);

            root.SetActive(false);

            if (station != null) WireStation(root, apply, close, titleClose, result, jobOrderText, inputs, defs);

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);

            Debug.Log("[DeviceConfigPanelBuilder] Built '" + root.name + "' with " + defs.Length +
                      " field(s)" + (station != null ? " and wired it into '" + station.name + "'." :
                      ". No station was assigned, so fill the Inspector by hand."), root);
        }

        void WireStation(GameObject root, Button apply, Button close, Button titleClose,
                         TextMeshProUGUI result, TextMeshProUGUI jobOrder,
                         List<TMP_InputField> inputs, FieldDef[] defs)
        {
            Undo.RecordObject(station, "Wire Config Panel");

            station.panel = root;
            station.applyButton = apply;
            station.closeButton = close;
            station.resultText = result;
            station.jobOrderText = jobOrder;
            station.jobOrder = jobOrder != null ? jobOrder.text : station.jobOrder;
            if (string.IsNullOrEmpty(station.stationName) || station.stationName == "Server")
                station.stationName = machineName.Trim();

            station.fields = new List<DeviceConfigStation.ConfigField>();
            for (int i = 0; i < defs.Length; i++)
            {
                DeviceConfigStation.ConfigField f = new DeviceConfigStation.ConfigField();
                f.label = defs[i].label.TrimEnd(':');
                f.input = inputs[i];
                f.expectedValue = defs[i].expected;
                f.alsoAccept = defs[i].alsoAccept ?? new string[0];
                f.wrongHint = defs[i].hint;
                station.fields.Add(f);
            }

            // The title-bar X is the one control the station cannot wire itself, because it has no
            // field for it. A persistent listener is the same thing you would add by hand.
            if (titleClose != null)
                UnityEventTools.AddVoidPersistentListener(titleClose.onClick, station.Close);

            EditorUtility.SetDirty(station);
        }

        // ------------------------------------------------------------------ window furniture

        Button BuildTitleBar(Transform parent, string title)
        {
            GameObject bar = Panel(parent, "TitleBar", TitleBar);
            RectTransform rt = Rect(bar);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -36f);
            rt.offsetMax = Vector2.zero;

            TextMeshProUGUI t = Label(bar.transform, "TitleText", title, 12.5f, InkStrong);
            RectTransform trt = Rect(t);
            trt.anchorMin = new Vector2(0f, 0f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.offsetMin = new Vector2(12f, 0f);
            trt.offsetMax = new Vector2(-96f, 0f);
            t.alignment = TextAlignmentOptions.Left;

            Button x = BuildButton(bar.transform, "TitleCloseButton", "×", TitleBar, InkStrong);
            RectTransform xrt = Rect(x);
            xrt.anchorMin = new Vector2(1f, 0f);
            xrt.anchorMax = new Vector2(1f, 1f);
            xrt.pivot = new Vector2(1f, 0.5f);
            xrt.sizeDelta = new Vector2(46f, 0f);
            xrt.anchoredPosition = Vector2.zero;

            // Windows turns the close button red on hover and leaves the others alone. One
            // three-line colour block is the whole reason the chrome reads as real rather than as
            // three grey squares.
            ColorBlock cb = x.colors;
            cb.highlightedColor = CloseHot;
            cb.pressedColor = new Color32(0x9C, 0x22, 0x16, 255);
            x.colors = cb;

            return x;
        }

        void BuildTaskbar(Transform parent)
        {
            GameObject bar = Panel(parent, "Taskbar", Taskbar);
            RectTransform rt = Rect(bar);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, 48f);
            rt.anchoredPosition = Vector2.zero;

            // Centred icons, Windows 11 style. Two flat squares stand in for Start and the pinned
            // app - a placeholder that reads correctly at a glance beats a wrong-looking real icon.
            for (int i = 0; i < 2; i++)
            {
                GameObject icon = Panel(bar.transform, i == 0 ? "StartIcon" : "AppIcon",
                                        i == 0 ? new Color32(0x00, 0x78, 0xD4, 255) : InkMuted);
                RectTransform irt = Rect(icon);
                irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 0.5f);
                irt.sizeDelta = new Vector2(22f, 22f);
                irt.anchoredPosition = new Vector2(i == 0 ? -20f : 20f, 0f);
            }

            TextMeshProUGUI clock = Label(bar.transform, "Clock", "14:32\n06/09/2026", 11f, InkBody);
            RectTransform crt = Rect(clock);
            crt.anchorMin = new Vector2(1f, 0f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 0.5f);
            crt.sizeDelta = new Vector2(110f, 0f);
            crt.anchoredPosition = new Vector2(-16f, 0f);
            clock.alignment = TextAlignmentOptions.Right;
            clock.lineSpacing = -18f;
        }

        TextMeshProUGUI BuildNotepad(Transform parent)
        {
            GameObject win = Panel(parent, "JobOrderWindow", NotepadFace);
            RectTransform rt = Rect(win);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(420f, 460f);
            rt.anchoredPosition = new Vector2(-440f, 20f);
            AddOutline(win, WindowEdge);

            GameObject bar = Panel(win.transform, "TitleBar", TitleBar);
            RectTransform brt = Rect(bar);
            brt.anchorMin = new Vector2(0f, 1f);
            brt.anchorMax = new Vector2(1f, 1f);
            brt.pivot = new Vector2(0.5f, 1f);
            brt.offsetMin = new Vector2(0f, -32f);
            brt.offsetMax = Vector2.zero;

            TextMeshProUGUI title = Label(bar.transform, "TitleText", "job-order.txt - Notepad", 12f, InkStrong);
            RectTransform trt = Rect(title);
            Stretch(trt);
            trt.offsetMin = new Vector2(12f, 0f);
            trt.offsetMax = new Vector2(-12f, 0f);
            title.alignment = TextAlignmentOptions.Left;

            TextMeshProUGUI body = Label(win.transform, "JobOrderText", JobOrderBody(), 13f, InkBody);
            RectTransform yrt = Rect(body);
            Stretch(yrt);
            yrt.offsetMin = new Vector2(16f, 16f);
            yrt.offsetMax = new Vector2(-16f, -44f);
            body.alignment = TextAlignmentOptions.TopLeft;

            return body;
        }

        void BuildRadio(Transform parent, string name, string label, bool selected,
                        float x, float y, float winWidth)
        {
            GameObject row = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(row, "Build Config Panel");
            row.transform.SetParent(parent, false);
            TopLeft(Rect(row), x, y, winWidth - x - ContentPad, 22f);

            GameObject ring = Panel(row.transform, "Ring", selected ? AccentBlue : InkMuted,
                                    Builtin("UI/Skin/Knob.psd"), Image.Type.Simple);
            RectTransform rrt = Rect(ring);
            rrt.anchorMin = new Vector2(0f, 0.5f);
            rrt.anchorMax = new Vector2(0f, 0.5f);
            rrt.pivot = new Vector2(0f, 0.5f);
            rrt.sizeDelta = new Vector2(15f, 15f);
            rrt.anchoredPosition = Vector2.zero;

            TextMeshProUGUI t = Label(row.transform, "Label", label, 13f,
                                      selected ? InkBody : InkMuted);
            RectTransform trt = Rect(t);
            Stretch(trt);
            trt.offsetMin = new Vector2(24f, 0f);
            trt.offsetMax = Vector2.zero;
            t.alignment = TextAlignmentOptions.Left;
        }

        TMP_InputField BuildFieldRow(Transform parent, string prefix, FieldDef def,
                                     float y, float winWidth)
        {
            GameObject row = new GameObject("Row_" + def.key, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(row, "Build Config Panel");
            row.transform.SetParent(parent, false);
            TopLeft(Rect(row), ContentPad + 4f, y, winWidth - (ContentPad + 4f) - ContentPad, RowHeight);

            TextMeshProUGUI label = Label(row.transform, "Label", def.label, 13f, InkBody);
            RectTransform lrt = Rect(label);
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(0f, 1f);
            lrt.pivot = new Vector2(0f, 0.5f);
            lrt.sizeDelta = new Vector2(LabelWidth, 0f);
            lrt.anchoredPosition = Vector2.zero;
            label.alignment = TextAlignmentOptions.Left;

            return BuildInputField(row.transform, "Field_" + prefix + "_" + def.key,
                                   def.placeholder, LabelWidth + 12f);
        }

        /// <summary>
        /// A TMP_InputField built the way TMP itself builds one: background, a Text Area with a
        /// RectMask2D, and Placeholder plus Text inside it. Skip the mask and long values scroll
        /// straight out of the box and over the rest of the dialog.
        /// </summary>
        TMP_InputField BuildInputField(Transform parent, string name, string placeholder, float leftInset)
        {
            GameObject go = Panel(parent, name, FieldFace,
                                  Builtin("UI/Skin/InputFieldBackground.psd"), Image.Type.Sliced);
            RectTransform rt = Rect(go);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.offsetMin = new Vector2(leftInset, 0f);
            rt.offsetMax = Vector2.zero;

            GameObject area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            Undo.RegisterCreatedObjectUndo(area, "Build Config Panel");
            area.transform.SetParent(go.transform, false);
            RectTransform art = Rect(area);
            Stretch(art);
            art.offsetMin = new Vector2(10f, 5f);
            art.offsetMax = new Vector2(-10f, -5f);

            TextMeshProUGUI ph = Label(area.transform, "Placeholder", placeholder, 13f,
                                       new Color32(0x9A, 0x9A, 0x9A, 255));
            Stretch(Rect(ph));
            ph.alignment = TextAlignmentOptions.Left;
            ph.fontStyle = FontStyles.Italic;

            TextMeshProUGUI txt = Label(area.transform, "Text", "", 13f, InkStrong);
            Stretch(Rect(txt));
            txt.alignment = TextAlignmentOptions.Left;
            txt.richText = false;

            TMP_InputField field = go.AddComponent<TMP_InputField>();
            field.textViewport = art;
            field.textComponent = txt;
            field.placeholder = ph;
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.characterLimit = 40;
            field.caretWidth = 2;
            field.customCaretColor = true;
            field.caretColor = InkStrong;
            field.selectionColor = new Color32(0x00, 0x67, 0xC0, 90);

            return field;
        }

        Button BuildButton(Transform parent, string name, string text, Color face, Color ink)
        {
            GameObject go = Panel(parent, name, face, Builtin("UI/Skin/UISprite.psd"), Image.Type.Sliced);
            Button b = go.AddComponent<Button>();
            b.targetGraphic = go.GetComponent<Image>();

            TextMeshProUGUI t = Label(go.transform, "Text", text, 13f, ink);
            Stretch(Rect(t));
            t.alignment = TextAlignmentOptions.Center;

            return b;
        }

        // ------------------------------------------------------------------ small helpers

        Canvas CreateCanvas()
        {
            GameObject go = new GameObject("UI Canvas", typeof(Canvas), typeof(CanvasScaler),
                                           typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(go, "Build Config Panel");
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
                Undo.RegisterCreatedObjectUndo(es, "Build Config Panel");
            }
            return c;
        }

        GameObject Panel(Transform parent, string name, Color color,
                         Sprite sprite = null, Image.Type type = Image.Type.Simple)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build Config Panel");
            go.transform.SetParent(parent, false);
            Image img = go.AddComponent<Image>();
            img.color = color;
            if (sprite != null) { img.sprite = sprite; img.type = type; }
            return go;
        }

        TextMeshProUGUI Label(Transform parent, string name, string text, float size, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build Config Panel");
            go.transform.SetParent(parent, false);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.raycastTarget = false;
            t.enableWordWrapping = true;
            return t;
        }

        /// <summary>
        /// A one-pixel border, drawn as a slightly larger Image behind the panel.
        ///
        /// Unity's Image has no border property, and the usual answers are a 9-sliced sprite you
        /// have to author or an Outline component that blurs. A sibling rectangle one pixel bigger
        /// on each side is exact, costs one draw call, and is what makes a flat grey rectangle read
        /// as a window rather than as a flat grey rectangle.
        /// </summary>
        void AddOutline(GameObject panel, Color edge)
        {
            GameObject line = new GameObject("Edge", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(line, "Build Config Panel");
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

        static Sprite Builtin(string path)
        {
            return AssetDatabase.GetBuiltinExtraResource<Sprite>(path);
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

        /// <summary>Position from the top-left of the parent, which is how a dialog is laid out on
        /// paper and therefore the least surprising way to nudge one afterwards.</summary>
        static void TopLeft(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, -y);
        }

        static void BottomRight(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(-x, y);
        }
    }
}