// WindowsDesktopBuilder.cs
//
// EDITOR SCRIPT. Must live in a folder called "Editor":
//     Assets/Scripts/COC2/Editor/WindowsDesktopBuilder.cs
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
    /// Builds a whole Windows desktop for one machine - Start menu, Control Panel, Network and
    /// Sharing Center, adapter list, Ethernet Properties, TCP/IPv4 Properties and a Command Prompt -
    /// and wires the navigation, the config station and the ping station into it.
    ///
    /// WHY SIX WINDOWS INSTEAD OF ONE
    ///
    /// Setting a static address is four numbers; FINDING the screen that takes them is six clicks,
    /// and that is the half a trainee gets stuck on in the shop. An assessor watching someone
    /// configure a workstation is watching the route as much as the values. A single dialog that
    /// appears on E teaches the numbers and nothing else.
    ///
    /// So the route is the real one, and WindowsShell counts the wrong turns along the way.
    ///
    /// WHY IT IS A BUILDER
    ///
    /// Six windows, about forty objects, and roughly thirty button hookups per machine - times two
    /// machines. Done by hand that is an evening, and a single unassigned On Click leaves a dead
    /// button that looks identical to a live one. Generated, it is two clicks and the wiring cannot
    /// be forgotten. What comes out is ordinary GameObjects you own and can restyle freely.
    ///
    /// HOW TO USE IT
    ///   1. Put a DeviceConfigStation and a ConnectivityCheckStation on the machine (same object is
    ///      fine now - the check station stops competing for the E press).
    ///   2. Tools > CircuitCrafter > Windows Desktop Builder
    ///   3. Machine name, drag both stations in, Build. Ctrl+Z undoes the lot.
    /// </summary>
    public class WindowsDesktopBuilder : EditorWindow
    {
        string machineName = "Server";
        Canvas targetCanvas;
        DeviceConfigStation configStation;
        ConnectivityCheckStation checkStation;
        bool markTheRoute = true;
        bool showBreadcrumb = true;

        // Windows 11 light, sampled rather than invented - close-but-wrong greys are the tell.
        static readonly Color Wallpaper = new Color32(0x10, 0x3B, 0x66, 255);
        static readonly Color Taskbar = new Color32(0xF3, 0xF3, 0xF3, 250);
        static readonly Color WindowBody = new Color32(0xF0, 0xF0, 0xF0, 255);
        static readonly Color TitleBar = new Color32(0xFF, 0xFF, 0xFF, 255);
        static readonly Color WindowEdge = new Color32(0xB8, 0xB8, 0xB8, 255);
        static readonly Color ListFace = new Color32(0xFF, 0xFF, 0xFF, 255);
        static readonly Color RowHover = new Color32(0xE5, 0xF1, 0xFB, 255);
        static readonly Color InkStrong = new Color32(0x1A, 0x1A, 0x1A, 255);
        static readonly Color InkBody = new Color32(0x20, 0x20, 0x20, 255);
        static readonly Color InkMuted = new Color32(0x6B, 0x6B, 0x6B, 255);
        static readonly Color LinkInk = new Color32(0x00, 0x66, 0xCC, 255);
        static readonly Color AccentBlue = new Color32(0x00, 0x67, 0xC0, 255);
        static readonly Color ButtonFace = new Color32(0xFD, 0xFD, 0xFD, 255);
        static readonly Color FieldFace = new Color32(0xFF, 0xFF, 0xFF, 255);
        static readonly Color CloseHot = new Color32(0xC4, 0x2B, 0x1C, 255);
        static readonly Color ErrorInk = new Color32(0xA8, 0x1D, 0x18, 255);
        static readonly Color ConsoleBg = new Color32(0x0C, 0x0C, 0x0C, 255);
        static readonly Color ConsoleInk = new Color32(0xCC, 0xCC, 0xCC, 255);
        static readonly Color CmdBar = new Color32(0x1F, 0x1F, 0x1F, 255);
        static readonly Color CmdInk = new Color32(0xE6, 0xE6, 0xE6, 255);
        static readonly Color CmdField = new Color32(0x16, 0x16, 0x16, 255);

        // One type scale, named, so "make it bigger" is six numbers rather than forty literals
        // scattered through the build. Sized for a 1920x1080 reference canvas read at a distance -
        // a trainee is looking at a projector or a laptop across a bench, not a phone in their hand.
        const float FTitle = 16f;    // window title bars
        const float FRow = 18f;    // list rows - the things you click
        const float FBody = 16f;    // labels, buttons, field text
        const float FIntro = 15.5f;  // explanatory paragraphs
        const float FSmall = 14f;    // notes and asides
        const float FConsole = 19f;    // the command prompt

        const float Pad = 24f;
        const float TitleH = 40f;
        const float RowH = 44f;

        WindowsShell shell;
        List<WindowsShell.Screen> built;

        [MenuItem("Tools/CircuitCrafter/Windows Desktop Builder")]
        static void ShowWindow()
        {
            WindowsDesktopBuilder w = GetWindow<WindowsDesktopBuilder>(true, "Windows Desktop Builder");
            w.minSize = new Vector2(440, 400);
            w.targetCanvas = FindFirstObjectByType<Canvas>();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Build a navigable Windows desktop", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox(
                "Generates Start menu, Control Panel, Network and Sharing Center, adapter list, " +
                "Ethernet Properties, TCP/IPv4 Properties and Command Prompt - all wired.\n\n" +
                "The config station's Panel becomes the desktop root; the player navigates to the " +
                "settings themselves. Ctrl+Z undoes the whole build.",
                MessageType.Info);
            EditorGUILayout.Space(8);

            machineName = EditorGUILayout.TextField(
                new GUIContent("Machine name", "'Server' gives ServerDesktop, Field_Server_IP, ..."),
                machineName);

            targetCanvas = (Canvas)EditorGUILayout.ObjectField(
                new GUIContent("Canvas", "Where the desktop goes. Empty creates one."),
                targetCanvas, typeof(Canvas), true);

            configStation = (DeviceConfigStation)EditorGUILayout.ObjectField(
                new GUIContent("Config station", "Gets Panel, the three fields, OK and Cancel."),
                configStation, typeof(DeviceConfigStation), true);

            checkStation = (ConnectivityCheckStation)EditorGUILayout.ObjectField(
                new GUIContent("Check station", "Gets the Command Prompt window, its input and Ping."),
                checkStation, typeof(ConnectivityCheckStation), true);

            EditorGUILayout.Space(6);
            markTheRoute = EditorGUILayout.Toggle(
                new GUIContent("Mark the route", "Records wrong turns on WindowsShell, so a trainee " +
                                                 "who walked straight there and one who opened four " +
                                                 "wrong panels are distinguishable."),
                markTheRoute);
            showBreadcrumb = EditorGUILayout.Toggle(
                new GUIContent("Show breadcrumb", "'Control Panel > Network and Sharing Center' " +
                                                  "across the top. Turn it off for a graded build " +
                                                  "if navigation should be unaided."),
                showBreadcrumb);

            EditorGUILayout.Space(12);

            if (configStation == null)
                EditorGUILayout.HelpBox("Without a config station the windows are built but nothing " +
                                        "opens them and nothing marks the fields.", MessageType.Warning);

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(machineName)))
                if (GUILayout.Button("Build desktop", GUILayout.Height(32)))
                    Build();
        }

        // ------------------------------------------------------------------ the build

        void Build()
        {
            Canvas canvas = targetCanvas != null ? targetCanvas : CreateCanvas();
            string safe = machineName.Trim().Replace(" ", "");
            bool isClient = safe.ToLower().Contains("client");
            string ip = isClient ? "192.168.1.20" : "192.168.1.10";

            built = new List<WindowsShell.Screen>();

            GameObject root = new GameObject(safe + "Desktop", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(root, "Build Desktop");
            root.transform.SetParent(canvas.transform, false);
            Stretch(Rect(root));

            shell = root.AddComponent<WindowsShell>();

            // ---- the desktop itself: wallpaper, taskbar, Start button. Never hidden.
            GameObject desk = Panel(root.transform, "Wallpaper", Wallpaper);
            Stretch(Rect(desk));

            TextMeshProUGUI hint = Label(desk.transform, "DesktopHint",
                "Open Start to reach Control Panel, or to open the Command Prompt.\n" +
                "Press E to step away from this machine.", 19f, new Color32(0xC8, 0xD8, 0xE8, 255));
            RectTransform hrt = Rect(hint);
            hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0.5f, 0.5f);
            hrt.sizeDelta = new Vector2(900f, 100f);
            hrt.anchoredPosition = new Vector2(0f, -60f);
            hint.alignment = TextAlignmentOptions.Center;

            Button start = BuildTaskbar(desk.transform);

            TextMeshProUGUI crumb = null;
            if (showBreadcrumb)
            {
                crumb = Label(desk.transform, "Breadcrumb", "", FBody, new Color32(0xD0, 0xDD, 0xEA, 255));
                RectTransform crt = Rect(crumb);
                crt.anchorMin = new Vector2(0f, 1f);
                crt.anchorMax = new Vector2(1f, 1f);
                crt.pivot = new Vector2(0.5f, 1f);
                crt.sizeDelta = new Vector2(-48f, 34f);
                crt.anchoredPosition = new Vector2(0f, -12f);
                crumb.alignment = TextAlignmentOptions.Left;
            }

            // ---- windows, in route order
            GameObject windows = new GameObject("Windows", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(windows, "Build Desktop");
            windows.transform.SetParent(root.transform, false);
            Stretch(Rect(windows));

            BuildStartMenu(windows.transform);
            BuildControlPanel(windows.transform);
            BuildNetworkCenter(windows.transform);
            BuildAdapterList(windows.transform);
            BuildEthernetProps(windows.transform);
            TMP_InputField[] inputs = BuildTcpIpProps(windows.transform, safe, ip, isClient);
            BuildCommandPrompt(windows.transform);

            Wire(start, "start");

            shell.screens = built;
            shell.breadcrumbText = crumb;
            shell.expectedPath = markTheRoute
                ? new[] { "start", "controlpanel", "networkcenter", "adapters", "ethernetprops", "tcpip" }
                : new string[0];

            root.SetActive(false);

            if (configStation != null) WireConfig(root, inputs, ip, isClient);
            if (checkStation != null) WireCheck();

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);

            Debug.Log("[WindowsDesktopBuilder] Built '" + root.name + "' with " + built.Count +
                      " window(s)." + (configStation == null ? " No config station was assigned." : ""), root);
        }

        // ------------------------------------------------------------------ wiring

        void WireConfig(GameObject root, TMP_InputField[] inputs, string ip, bool isClient)
        {
            Undo.RecordObject(configStation, "Wire Desktop");

            configStation.panel = root;
            configStation.stationName = machineName.Trim();
            configStation.jobOrder =
                "JOB ORDER JO-2601\r\n" + (isClient ? "Client" : "Server") + " workstation\r\n\r\n" +
                "IP address       " + ip + "\r\n" +
                "Subnet mask      255.255.255.0\r\n" +
                "Default gateway  192.168.1.1\r\n";

            // The dialog dismisses, the desktop stays. Clicking OK in TCP/IPv4 Properties should not
            // eject the player from the machine any more than it does in Windows.
            configStation.closePanelOnPass = false;

            configStation.fields = new List<DeviceConfigStation.ConfigField>
            {
                Field("IP address", inputs[0], ip,
                      "Each host on a subnet needs its own address."),
                Field("Subnet mask", inputs[1], "255.255.255.0",
                      "A /24 mask puts all 254 hosts on one network."),
                Field("Default gateway", inputs[2], "192.168.1.1",
                      "The gateway is the router, and it is the same for every machine here.")
            };

            EditorUtility.SetDirty(configStation);

            // Escape on the bare desktop, and the taskbar's power button, leave the machine.
            UnityEventTools.AddVoidPersistentListener(shell.OnCloseRequested, configStation.Close);
            EditorUtility.SetDirty(shell);
        }

        void WireCheck()
        {
            Undo.RecordObject(checkStation, "Wire Desktop");
            checkStation.stationName = machineName.Trim();
            checkStation.openOnInteract = false;   // the desktop owns the E press
            checkStation.managedByShell = true;    // the desktop owns the player lock
            checkStation.consolePanel = FindPanel("cmd");
            checkStation.consoleText = cmdText;
            checkStation.commandInput = cmdInput;
            checkStation.pingButton = cmdPing;
            if (checkStation.consoleLines < 12) checkStation.consoleLines = 12;

            // The relay is what keeps the station's IsOpen honest when the shell shows or hides the
            // window. Without it the console appears and the Ping button does nothing.
            GameObject cmdWindow = FindPanel("cmd");
            if (cmdWindow != null)
            {
                ConsoleWindowRelay relay = cmdWindow.GetComponent<ConsoleWindowRelay>();
                if (relay == null) relay = cmdWindow.AddComponent<ConsoleWindowRelay>();
                relay.station = checkStation;
            }
            EditorUtility.SetDirty(checkStation);
        }

        static DeviceConfigStation.ConfigField Field(string label, TMP_InputField input,
                                                    string expected, string hint)
        {
            DeviceConfigStation.ConfigField f = new DeviceConfigStation.ConfigField();
            f.label = label;
            f.input = input;
            f.expectedValue = expected;
            f.wrongHint = hint;
            f.alsoAccept = new string[0];
            return f;
        }

        // ------------------------------------------------------------------ the windows

        Button BuildTaskbar(Transform parent)
        {
            GameObject bar = Panel(parent, "Taskbar", Taskbar);
            RectTransform rt = Rect(bar);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, 54f);
            rt.anchoredPosition = Vector2.zero;

            Button start = SmallButton(bar.transform, "StartButton", "Start",
                                       new Color32(0x00, 0x78, 0xD4, 255), Color.white);
            RectTransform srt = Rect(start);
            srt.anchorMin = new Vector2(0f, 0.5f);
            srt.anchorMax = new Vector2(0f, 0.5f);
            srt.pivot = new Vector2(0f, 0.5f);
            srt.sizeDelta = new Vector2(110f, 40f);
            srt.anchoredPosition = new Vector2(12f, 0f);

            Button leave = SmallButton(bar.transform, "LeavePcButton", "Leave PC", ButtonFace, InkBody);
            RectTransform lrt = Rect(leave);
            lrt.anchorMin = new Vector2(1f, 0.5f);
            lrt.anchorMax = new Vector2(1f, 0.5f);
            lrt.pivot = new Vector2(1f, 0.5f);
            lrt.sizeDelta = new Vector2(140f, 40f);
            lrt.anchoredPosition = new Vector2(-16f, 0f);
            UnityEventTools.AddVoidPersistentListener(leave.onClick, shell.RequestClose);

            return start;
        }

        /// <summary>
        /// The Start menu, built the way Windows 10 builds one: a narrow icon rail down the left
        /// with the account and the power button, and the app list beside it.
        ///
        /// It gets no title bar, because Start does not have one - and that is the whole reason the
        /// window helper takes a flag instead of always drawing one. A Start menu with a title bar
        /// reading "Start" is the single clearest tell that a mock was assembled from generic panels.
        ///
        /// The power button on the rail is live and leaves the machine, which is both correct and
        /// the affordance a player reaches for first.
        /// </summary>
        void BuildStartMenu(Transform parent)
        {
            const float W = 400f, H = 470f, Rail = 60f;

            GameObject win = Panel(parent, "StartMenu", new Color32(0x3C, 0x3C, 0x3C, 250));
            RectTransform wrt = Rect(win);
            wrt.anchorMin = wrt.anchorMax = wrt.pivot = new Vector2(0f, 0f);
            wrt.sizeDelta = new Vector2(W, H);
            wrt.anchoredPosition = new Vector2(12f, 62f);
            AddFace(win, new Color32(0x24, 0x24, 0x24, 250));

            // ---- the icon rail
            GameObject rail = Panel(win.transform, "Rail", new Color32(0x1C, 0x1C, 0x1C, 255));
            RectTransform rrt = Rect(rail);
            rrt.anchorMin = new Vector2(0f, 0f);
            rrt.anchorMax = new Vector2(0f, 1f);
            rrt.pivot = new Vector2(0f, 0.5f);
            rrt.sizeDelta = new Vector2(Rail, 0f);
            rrt.anchoredPosition = Vector2.zero;

            RailIcon(rail.transform, "Rail_User", new Color32(0x4C, 0x9A, 0xD8, 255), 1, true);
            RailIcon(rail.transform, "Rail_Documents", new Color32(0x8A, 0x8A, 0x8A, 255), 2, true);
            RailIcon(rail.transform, "Rail_Pictures", new Color32(0x8A, 0x8A, 0x8A, 255), 3, true);
            RailIcon(rail.transform, "Rail_Settings", new Color32(0x8A, 0x8A, 0x8A, 255), 4, true);

            Button power = SmallButton(rail.transform, "Rail_Power", "",
                                       new Color32(0x1C, 0x1C, 0x1C, 255), Color.white);
            RectTransform prt = Rect(power);
            prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0.5f, 0f);
            prt.sizeDelta = new Vector2(Rail, 52f);
            prt.anchoredPosition = new Vector2(0f, 0f);
            ColorBlock pcb = power.colors;
            pcb.highlightedColor = new Color32(0x3A, 0x3A, 0x3A, 255);
            power.colors = pcb;
            UnityEventTools.AddVoidPersistentListener(power.onClick, shell.RequestClose);

            GameObject powerDot = Panel(power.transform, "Icon", new Color32(0xD0, 0x50, 0x50, 255));
            RectTransform pdrt = Rect(powerDot);
            pdrt.anchorMin = pdrt.anchorMax = pdrt.pivot = new Vector2(0.5f, 0.5f);
            pdrt.sizeDelta = new Vector2(20f, 20f);
            pdrt.anchoredPosition = Vector2.zero;

            TextMeshProUGUI powerLabel = Label(win.transform, "PowerLabel", "Power", 11f,
                                               new Color32(0xA8, 0xA8, 0xA8, 255));
            RectTransform plrt = Rect(powerLabel);
            plrt.anchorMin = plrt.anchorMax = plrt.pivot = new Vector2(0f, 0f);
            plrt.sizeDelta = new Vector2(Rail, 16f);
            plrt.anchoredPosition = new Vector2(0f, 2f);
            powerLabel.alignment = TextAlignmentOptions.Center;

            // ---- account line and app list
            TextMeshProUGUI who = Label(win.transform, "AccountName",
                                        machineName.Trim().ToUpper() + "-PC", FBody,
                                        new Color32(0xE8, 0xE8, 0xE8, 255));
            TopLeft(Rect(who), Rail + 18f, 16f, W - Rail - 30f, 26f);
            who.alignment = TextAlignmentOptions.Left;

            TextMeshProUGUI header = Label(win.transform, "MostUsed", "Most used", FSmall,
                                           new Color32(0x9A, 0x9A, 0x9A, 255));
            TopLeft(Rect(header), Rail + 18f, 50f, W - Rail - 30f, 22f);

            StartRow(win.transform, "Item_ControlPanel", "Control Panel",
                     new Color32(0x4C, 0x9A, 0xD8, 255), 0, Rail, W, "controlpanel");
            StartRow(win.transform, "Item_Cmd", "Command Prompt",
                     new Color32(0x2E, 0x2E, 0x2E, 255), 1, Rail, W, "cmd");
            DeadStartRow(win.transform, "Item_Explorer", "File Explorer",
                         new Color32(0xE0, 0xB1, 0x5A, 255), 2, Rail, W);
            DeadStartRow(win.transform, "Item_Notepad", "Notepad",
                         new Color32(0xC8, 0xC8, 0xC8, 255), 3, Rail, W);
            DeadStartRow(win.transform, "Item_Settings", "Settings",
                         new Color32(0x8A, 0x8A, 0x8A, 255), 4, Rail, W);

            win.SetActive(false);

            WindowsShell.Screen screen = new WindowsShell.Screen();
            screen.id = "start";
            screen.panel = win;
            screen.title = "Start";
            built.Add(screen);
        }

        void RailIcon(Transform parent, string name, Color color, int fromTop, bool square)
        {
            GameObject go = Panel(parent, name, color);
            RectTransform rt = Rect(go);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(square ? 22f : 26f, 22f);
            rt.anchoredPosition = new Vector2(0f, -(14f + (fromTop - 1) * 46f));
        }

        /// <summary>A Start menu app row: colour chip, then the name.</summary>
        void StartRow(Transform parent, string name, string text, Color chip,
                      int index, float rail, float width, string opensId)
        {
            Button b = StartRowBase(parent, name, text, chip, index, rail, width,
                                    new Color32(0xE8, 0xE8, 0xE8, 255));
            Wire(b, opensId);
        }

        void DeadStartRow(Transform parent, string name, string text, Color chip,
                          int index, float rail, float width)
        {
            StartRowBase(parent, name, text, chip, index, rail, width,
                         new Color32(0x8A, 0x8A, 0x8A, 255));
        }

        Button StartRowBase(Transform parent, string name, string text, Color chip,
                            int index, float rail, float width, Color ink)
        {
            Button b = SmallButton(parent, name, "", new Color32(0x24, 0x24, 0x24, 0), ink);
            RectTransform rt = Rect(b);
            TopLeft(rt, rail + 8f, 80f + index * 46f, width - rail - 16f, 44f);

            ColorBlock cb = b.colors;
            cb.normalColor = new Color(1f, 1f, 1f, 0f);
            cb.highlightedColor = new Color(1f, 1f, 1f, 0.12f);
            cb.pressedColor = new Color(1f, 1f, 1f, 0.2f);
            b.colors = cb;

            GameObject icon = Panel(b.transform, "Chip", chip);
            RectTransform irt = Rect(icon);
            irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0f, 0.5f);
            irt.sizeDelta = new Vector2(26f, 26f);
            irt.anchoredPosition = new Vector2(10f, 0f);

            TextMeshProUGUI t = Label(b.transform, "Text", text, FRow, ink);
            RectTransform trt = Rect(t);
            Stretch(trt);
            trt.offsetMin = new Vector2(48f, 0f);
            t.alignment = TextAlignmentOptions.Left;

            return b;
        }

        void BuildControlPanel(Transform parent)
        {
            GameObject win = Window(parent, "ControlPanel", "controlpanel", "Control Panel", 840f, 320f + RowH * 5);

            Row(win.transform, "Item_Network", "Network and Sharing Center", 0, "networkcenter");
            DeadRow(win.transform, "Item_System", "System", 1);
            DeadRow(win.transform, "Item_Devices", "Devices and Printers", 2);
            DeadRow(win.transform, "Item_Programs", "Programs and Features", 3);
            DeadRow(win.transform, "Item_User", "User Accounts", 4);
        }

        void BuildNetworkCenter(Transform parent)
        {
            GameObject win = Window(parent, "NetworkCenter", "networkcenter",
                                    "Network and Sharing Center", 840f, 480f);

            TextMeshProUGUI blurb = Label(win.transform, "Blurb",
                "View your active networks\n\nNetwork 2      Access type: No Internet access\n" +
                "               Connections: Ethernet", FBody, InkBody);
            TopLeft(Rect(blurb), Pad, TitleH + 16f, 840f - Pad * 2, 120f);
            blurb.alignment = TextAlignmentOptions.TopLeft;

            LinkRow(win.transform, "Link_Adapters", "Change adapter settings", 0, "adapters", 170f);
            DeadLink(win.transform, "Link_Sharing", "Change advanced sharing settings", 1, 170f);
        }

        void BuildAdapterList(Transform parent)
        {
            GameObject win = Window(parent, "AdapterList", "adapters", "Network Connections", 900f, 400f);

            TextMeshProUGUI blurb = Label(win.transform, "Blurb",
                "Right-click an adapter and choose Properties.", FSmall, InkMuted);
            TopLeft(Rect(blurb), Pad, TitleH + 14f, 900f - Pad * 2, 28f);

            Row(win.transform, "Item_Ethernet", "Ethernet     Network 2     Realtek PCIe GbE Family Controller",
                0, "ethernetprops", TitleH + 54f);
            DeadRow(win.transform, "Item_WiFi", "Wi-Fi     Not connected", 1, TitleH + 54f);
        }

        void BuildEthernetProps(Transform parent)
        {
            GameObject win = Window(parent, "EthernetProps", "ethernetprops", "Ethernet Properties", 700f, 380f + RowH * 4);

            TextMeshProUGUI blurb = Label(win.transform, "Blurb",
                "This connection uses the following items:", FBody, InkBody);
            TopLeft(Rect(blurb), Pad, TitleH + 14f, 700f - Pad * 2, 28f);

            DeadRow(win.transform, "Item_Client", "Client for Microsoft Networks", 0, TitleH + 52f);
            DeadRow(win.transform, "Item_Sharing", "File and Printer Sharing for Microsoft Networks", 1, TitleH + 52f);
            Row(win.transform, "Item_TcpIp", "Internet Protocol Version 4 (TCP/IPv4)", 2, "tcpip", TitleH + 52f);
            DeadRow(win.transform, "Item_TcpIp6", "Internet Protocol Version 6 (TCP/IPv6)", 3, TitleH + 52f);

            TextMeshProUGUI note = Label(win.transform, "Note",
                "Select Internet Protocol Version 4 (TCP/IPv4), then Properties.", FSmall, InkMuted);
            TopLeft(Rect(note), Pad, TitleH + 52f + RowH * 4 + 16f, 700f - Pad * 2, 44f);
        }

        TMP_InputField[] BuildTcpIpProps(Transform parent, string safe, string ip, bool isClient)
        {
            GameObject win = Window(parent, "TcpIpProps", "tcpip",
                                    "Internet Protocol Version 4 (TCP/IPv4) Properties", 740f, 620f);

            TextMeshProUGUI intro = Label(win.transform, "IntroText",
                "You can get IP settings assigned automatically if your network supports this " +
                "capability. Otherwise, you need to ask your network administrator for the " +
                "appropriate IP settings.", FIntro, InkBody);
            TopLeft(Rect(intro), Pad, TitleH + 16f, 740f - Pad * 2, 68f);
            intro.alignment = TextAlignmentOptions.TopLeft;

            Radio(win.transform, "Radio_Auto", "Obtain an IP address automatically", false, TitleH + 96f);
            Radio(win.transform, "Radio_Manual", "Use the following IP address:", true, TitleH + 130f);

            string[] labels = { "IP address:", "Subnet mask:", "Default gateway:" };
            string[] keys = { "IP", "Mask", "Gateway" };
            TMP_InputField[] inputs = new TMP_InputField[3];

            for (int i = 0; i < 3; i++)
            {
                float y = TitleH + 176f + i * 50f;
                TextMeshProUGUI l = Label(win.transform, "Label_" + keys[i], labels[i], FBody, InkBody);
                TopLeft(Rect(l), Pad + 4f, y + 8f, 200f, 28f);
                inputs[i] = InputField(win.transform, "Field_" + safe + "_" + keys[i],
                                       "___ . ___ . ___ . ___", Pad + 4f + 208f,
                                       740f - Pad - (Pad + 4f + 208f), y, 36f, FieldFace, InkStrong);
            }

            TextMeshProUGUI result = Label(win.transform, "ResultText", "", FBody, ErrorInk);
            TopLeft(Rect(result), Pad, TitleH + 336f, 740f - Pad * 2, 96f);
            result.alignment = TextAlignmentOptions.TopLeft;

            TextMeshProUGUI job = Label(win.transform, "JobOrderText", "", FSmall, InkMuted);
            TopLeft(Rect(job), Pad, TitleH + 440f, 740f - Pad * 2, 60f);
            job.alignment = TextAlignmentOptions.TopLeft;

            Button ok = SmallButton(win.transform, "ApplyButton", "OK", AccentBlue, Color.white);
            BottomRight(Rect(ok), Pad + 116f, Pad, 108f, 38f);

            Button cancel = SmallButton(win.transform, "CloseButton", "Cancel", ButtonFace, InkBody);
            BottomRight(Rect(cancel), Pad, Pad, 108f, 38f);

            if (configStation != null)
            {
                configStation.resultText = result;
                configStation.jobOrderText = job;
                configStation.applyButton = ok;
                configStation.closeButton = cancel;
                // OK dismisses the dialog and leaves the player on the desktop, Windows-style.
                UnityEventTools.AddVoidPersistentListener(ok.onClick, shell.CloseAll);
                UnityEventTools.AddVoidPersistentListener(cancel.onClick, shell.Back);
            }

            return inputs;
        }

        TMP_Text cmdText;
        TMP_InputField cmdInput;
        Button cmdPing;

        void BuildCommandPrompt(Transform parent)
        {
            GameObject win = Window(parent, "CommandPrompt", "cmd", "Command Prompt", 860f, 480f,
                                    ConsoleBg, CmdBar, CmdInk);

            TextMeshProUGUI console = Label(win.transform, "ConsoleText",
                "Microsoft Windows [Version 10.0.19045.4046]\n" +
                "(c) Microsoft Corporation. All rights reserved.\n\nC:\\> _", FConsole, ConsoleInk);
            RectTransform rrt = Rect(console);
            rrt.anchorMin = new Vector2(0f, 0f);
            rrt.anchorMax = new Vector2(1f, 1f);
            rrt.offsetMin = new Vector2(18f, 64f);
            rrt.offsetMax = new Vector2(-16f, -(TitleH + 14f));
            console.alignment = TextAlignmentOptions.TopLeft;
            console.richText = false;
            cmdText = console;

            TextMeshProUGUI prompt = Label(win.transform, "Prompt", "C:\\>", FConsole, ConsoleInk);
            TopLeft(Rect(prompt), 18f, 480f - 54f, 52f, 36f);

            cmdInput = InputField(win.transform, "CommandInput", "ping 192.168.1.20",
                                  76f, 860f - 76f - 140f, 480f - 54f, 36f, CmdField, ConsoleInk);

            cmdPing = SmallButton(win.transform, "PingButton", "Ping", new Color32(0x2B, 0x2B, 0x2B, 255), CmdInk);
            BottomRight(Rect(cmdPing), 18f, 14f, 108f, 36f);
        }

        // ------------------------------------------------------------------ pieces

        /// <summary>A window with a title bar and a close X wired to Back(), registered as a screen.</summary>
        GameObject Window(Transform parent, string name, string id, string title, float w, float h)
        {
            return Window(parent, name, id, title, w, h, WindowBody, TitleBar, InkStrong);
        }

        GameObject Window(Transform parent, string name, string id, string title, float w, float h,
                          Color body, Color bar, Color barInk)
        {
            GameObject win = Panel(parent, name, WindowEdge);
            RectTransform rt = Rect(win);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = Vector2.zero;
            AddFace(win, body);

            GameObject barGo = Panel(win.transform, "TitleBar", bar);
            RectTransform brt = Rect(barGo);
            brt.anchorMin = new Vector2(0f, 1f);
            brt.anchorMax = new Vector2(1f, 1f);
            brt.pivot = new Vector2(0.5f, 1f);
            brt.offsetMin = new Vector2(0f, -TitleH);
            brt.offsetMax = Vector2.zero;

            TextMeshProUGUI t = Label(barGo.transform, "TitleText", title, FTitle, barInk);
            RectTransform trt = Rect(t);
            Stretch(trt);
            trt.offsetMin = new Vector2(12f, 0f);
            trt.offsetMax = new Vector2(-108f, 0f);
            t.alignment = TextAlignmentOptions.Left;

            Button x = SmallButton(barGo.transform, "CloseX", "×", bar, barInk);
            RectTransform xrt = Rect(x);
            xrt.anchorMin = new Vector2(1f, 0f);
            xrt.anchorMax = new Vector2(1f, 1f);
            xrt.pivot = new Vector2(1f, 0.5f);
            xrt.sizeDelta = new Vector2(52f, 0f);
            xrt.anchoredPosition = Vector2.zero;
            ColorBlock cb = x.colors;
            cb.highlightedColor = CloseHot;
            cb.pressedColor = new Color32(0x9C, 0x22, 0x16, 255);
            x.colors = cb;
            UnityEventTools.AddVoidPersistentListener(x.onClick, shell.Back);

            win.SetActive(false);

            WindowsShell.Screen screen = new WindowsShell.Screen();
            screen.id = id;
            screen.panel = win;
            screen.title = title;
            built.Add(screen);

            return win;
        }

        /// <summary>A clickable list row that opens another window.</summary>
        void Row(Transform parent, string name, string text, int index, string opensId, float top = 0f)
        {
            if (top <= 0f) top = TitleH + 12f;
            Button b = SmallButton(parent, name, "", ListFace, InkBody);
            RectTransform rt = Rect(b);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-Pad * 2, RowH);
            rt.anchoredPosition = new Vector2(0f, -(top + index * RowH));

            ColorBlock cb = b.colors;
            cb.highlightedColor = RowHover;
            b.colors = cb;

            TextMeshProUGUI t = Label(b.transform, "Text", text, FRow, InkBody);
            Stretch(Rect(t));
            Rect(t).offsetMin = new Vector2(16f, 0f);
            t.alignment = TextAlignmentOptions.Left;

            Wire(b, opensId);
        }

        /// <summary>A row that goes nowhere - the wrong turns a real Control Panel offers.</summary>
        void DeadRow(Transform parent, string name, string text, int index, float top = 0f)
        {
            if (top <= 0f) top = TitleH + 12f;
            GameObject go = Panel(parent, name, ListFace);
            RectTransform rt = Rect(go);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-Pad * 2, RowH);
            rt.anchoredPosition = new Vector2(0f, -(top + index * RowH));

            TextMeshProUGUI t = Label(go.transform, "Text", text, FRow, InkMuted);
            Stretch(Rect(t));
            Rect(t).offsetMin = new Vector2(16f, 0f);
            t.alignment = TextAlignmentOptions.Left;
        }

        void LinkRow(Transform parent, string name, string text, int index, string opensId, float top)
        {
            Button b = SmallButton(parent, name, "", WindowBody, LinkInk);
            RectTransform rt = Rect(b);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(380f, 32f);
            rt.anchoredPosition = new Vector2(Pad, -(top + index * 36f));

            TextMeshProUGUI t = Label(b.transform, "Text", text, FBody, LinkInk);
            Stretch(Rect(t));
            t.alignment = TextAlignmentOptions.Left;

            Wire(b, opensId);
        }

        void DeadLink(Transform parent, string name, string text, int index, float top)
        {
            TextMeshProUGUI t = Label(parent, name, text, FBody, InkMuted);
            RectTransform rt = Rect(t);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(380f, 32f);
            rt.anchoredPosition = new Vector2(Pad, -(top + index * 36f));
            t.alignment = TextAlignmentOptions.Left;
        }

        void Radio(Transform parent, string name, string text, bool selected, float y)
        {
            GameObject row = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(row, "Build Desktop");
            row.transform.SetParent(parent, false);
            TopLeft(Rect(row), Pad + 4f, y, 560f, 22f);

            GameObject ring = Panel(row.transform, "Ring", selected ? AccentBlue : InkMuted);
            Image ri = ring.GetComponent<Image>();
            ri.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            RectTransform rrt = Rect(ring);
            rrt.anchorMin = rrt.anchorMax = rrt.pivot = new Vector2(0f, 0.5f);
            rrt.sizeDelta = new Vector2(18f, 18f);
            rrt.anchoredPosition = Vector2.zero;

            TextMeshProUGUI t = Label(row.transform, "Label", text, FBody, selected ? InkBody : InkMuted);
            RectTransform trt = Rect(t);
            Stretch(trt);
            trt.offsetMin = new Vector2(28f, 0f);
            t.alignment = TextAlignmentOptions.Left;
        }

        void Wire(Button b, string opensId)
        {
            // Bound through a tiny relay component rather than a string-argument listener, because
            // AddStringPersistentListener needs the method to be resolvable by name at edit time and
            // silently produces a dead button when it is not.
            ShellLink link = b.gameObject.AddComponent<ShellLink>();
            link.shell = shell;
            link.screenId = opensId;
            UnityEventTools.AddVoidPersistentListener(b.onClick, link.Go);
        }

        GameObject FindPanel(string id)
        {
            foreach (WindowsShell.Screen s in built) if (s.id == id) return s.panel;
            return null;
        }

        // ------------------------------------------------------------------ primitives

        Canvas CreateCanvas()
        {
            GameObject go = new GameObject("UI Canvas", typeof(Canvas), typeof(CanvasScaler),
                                           typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(go, "Build Desktop");
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
                Undo.RegisterCreatedObjectUndo(es, "Build Desktop");
            }
            return c;
        }

        GameObject Panel(Transform parent, string name, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build Desktop");
            go.transform.SetParent(parent, false);
            go.AddComponent<Image>().color = color;
            return go;
        }

        TextMeshProUGUI Label(Transform parent, string name, string text, float size, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build Desktop");
            go.transform.SetParent(parent, false);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.raycastTarget = false;
            return t;
        }

        Button SmallButton(Transform parent, string name, string text, Color face, Color ink)
        {
            GameObject go = Panel(parent, name, face);
            Button b = go.AddComponent<Button>();
            b.targetGraphic = go.GetComponent<Image>();
            if (!string.IsNullOrEmpty(text))
            {
                TextMeshProUGUI t = Label(go.transform, "Text", text, FBody, ink);
                Stretch(Rect(t));
                t.alignment = TextAlignmentOptions.Center;
            }
            return b;
        }

        TMP_InputField InputField(Transform parent, string name, string placeholder,
                                  float x, float w, float y, float h, Color face, Color ink)
        {
            GameObject go = Panel(parent, name, face);
            TopLeft(Rect(go), x, y, w, h);

            GameObject area = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            Undo.RegisterCreatedObjectUndo(area, "Build Desktop");
            area.transform.SetParent(go.transform, false);
            RectTransform art = Rect(area);
            Stretch(art);
            art.offsetMin = new Vector2(8f, 4f);
            art.offsetMax = new Vector2(-8f, -4f);

            TextMeshProUGUI ph = Label(area.transform, "Placeholder", placeholder, FBody,
                                       new Color32(0x9A, 0x9A, 0x9A, 255));
            Stretch(Rect(ph));
            ph.alignment = TextAlignmentOptions.Left;

            TextMeshProUGUI txt = Label(area.transform, "Text", "", FBody, ink);
            Stretch(Rect(txt));
            txt.alignment = TextAlignmentOptions.Left;
            txt.richText = false;

            TMP_InputField f = go.AddComponent<TMP_InputField>();
            f.textViewport = art;
            f.textComponent = txt;
            f.placeholder = ph;
            f.lineType = TMP_InputField.LineType.SingleLine;
            f.characterLimit = 60;
            f.caretWidth = 2;
            f.customCaretColor = true;
            f.caretColor = ink;
            f.selectionColor = new Color32(0x00, 0x67, 0xC0, 90);
            return f;
        }

        /// <summary>
        /// Gives a window a one-pixel border: the ROOT carries the border colour, and this adds the
        /// real face as a first-sibling child inset by a pixel.
        ///
        /// The previous version did the opposite - root coloured as the face, a slightly larger
        /// "Edge" child behind it - and that cannot work in uGUI, where children always draw AFTER
        /// their parent. The border rectangle painted straight over the window it was supposed to
        /// outline. On the light windows a #B8B8B8 sheet over #F0F0F0 reads as a slightly grey panel
        /// and is easy to miss; on the Command Prompt the same sheet over #0C0C0C turned the console
        /// light grey, and #CCCCCC text on it was all but invisible.
        ///
        /// Content is still parented to the root, so it draws above this face. Nothing else changes.
        /// </summary>
        void AddFace(GameObject panel, Color face)
        {
            GameObject go = new GameObject("Face", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Build Desktop");
            go.transform.SetParent(panel.transform, false);
            go.transform.SetAsFirstSibling();
            Image img = go.AddComponent<Image>();
            img.color = face;
            img.raycastTarget = false;
            RectTransform rt = Rect(go);
            Stretch(rt);
            rt.offsetMin = new Vector2(1f, 1f);
            rt.offsetMax = new Vector2(-1f, -1f);
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