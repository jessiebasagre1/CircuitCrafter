#if UNITY_EDITOR
using CSSSimulation.WindowsSim;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws the simulated Windows Server desktop and the windows for COC 3 steps 1-4.
///
/// Tools ▸ COC 3 ▸ Windows Desktop Builder
///
/// WHY A TOOL RATHER THAN THE INSPECTOR
/// Five Windows dialogs is several hundred RectTransforms. Placing those by hand is a day of work
/// and every one of them is a chance to be two pixels out. COC3WinWidgets already knows how to
/// draw a Windows window; this decides what goes in them and wires the behaviour.
///
/// IT IS SAFE TO RUN TWICE. It deletes and rebuilds only the object named below, and collapses
/// the whole thing into one undo step. Nothing outside that object is touched.
/// </summary>
public class COC3DesktopBuilder : EditorWindow
{
    private const string RootName = "COC3Desktop";
    private const string UndoLabel = "Build COC3 Desktop";

    // The design space. Everything below is placed in these units and stretched to the parent,
    // so the desktop is laid out once and scales to whatever panel it lives in.
    private const float DW = 1600f;
    private const float DH = 900f;

    // Lab values. These must match COC3StepBlueprint - change them there and here together.
    private const string ServerName = "SERVER01";
    private const string ServerIP = "192.168.10.10";
    private const string Mask = "255.255.255.0";
    private const string Gateway = "192.168.10.1";
    private const string Dns = "127.0.0.1";
    private const string DomainFQDN = "cssncii.local";
    private const string ClientName = "CLIENT01";
    private const string ScopeStart = "192.168.10.100";
    private const string NetworkId = "192.168.10";

    // LO1. DataRoot is the folder the server was imaged with; UsersFolder is the one the learner
    // creates inside it at step 12, so the full path is C:\ServerData\Users - what the job order
    // asks for.
    private const string DataRoot = @"C:\ServerData";
    private const string UsersFolder = "Users";
    private const string SampleUser = "student01";
    private const string UserGroup = "STUDENTS";
    private const string ShareName = "SharedDocs";
    private const string MinPassword = "8";

    private RectTransform parent;
    private COC3WinTheme theme;
    private string status = "";
    private Vector2 scroll;

    [MenuItem("Tools/COC 3/Windows Desktop Builder")]
    private static void Open()
    {
        GetWindow<COC3DesktopBuilder>(false, "COC3 Desktop", true).minSize = new Vector2(420f, 340f);
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("COC 3 - Windows desktop", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Builds the desktop shell plus the windows for LO2 and LO1 (steps 1-17):\n\n" +
            "  LO2 - build the server\n" +
            "  System Properties + Computer Name/Domain Changes\n" +
            "  Network Connections + Ethernet + TCP/IPv4\n" +
            "  Server Manager + Add Roles + AD DS Configuration\n" +
            "  DHCP console + New Scope Wizard\n" +
            "  DNS Manager + New Zone Wizard\n" +
            "  CLIENT01 System Properties + domain join\n\n" +
            "  LO1 - set up user access\n" +
            "  File Explorer + New Folder\n" +
            "  AD Users and Computers + New User + New Group\n" +
            "  STUDENTS Properties (Members)\n" +
            "  Group Policy Editor + Minimum password length\n" +
            "  Users Properties (Sharing / Security) + Advanced Sharing\n" +
            "  Permissions for Users\n\n" +
            "Rebuilding replaces the '" + RootName + "' object only.",
            MessageType.None);

        EditorGUILayout.Space();

        parent = (RectTransform)EditorGUILayout.ObjectField(
            new GUIContent("Parent", "The Canvas or panel the desktop is drawn into. It is " +
                                     "stretched to fill this."),
            parent, typeof(RectTransform), true);

        theme = (COC3WinTheme)EditorGUILayout.ObjectField(
            new GUIContent("Theme", "Your COC3WinTheme asset. Left empty, a default is used for " +
                                    "this build and the look will not match your other windows."),
            theme, typeof(COC3WinTheme), false);

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(parent == null))
        {
            if (GUILayout.Button("Build Desktop + LO2 and LO1 (steps 1-17)", GUILayout.Height(32f)))
                EditorApplication.delayCall += Build;
        }

        if (!string.IsNullOrEmpty(status))
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(status, MessageType.Info);
        }

        EditorGUILayout.EndScrollView();
    }

    // ==================================================================

    private void Build()
    {
        if (parent == null) return;

        var t = theme != null ? theme : COC3WinTheme.CreateDefault();

        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(UndoLabel);

        try
        {
            var existing = parent.Find(RootName);
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

            var root = Stretch(parent, RootName);
            var desktop = Undo.AddComponent<WinDesktop>(root.gameObject);

            // The wallpaper STRETCHES, so it covers the whole area whatever shape the canvas is
            // and there are no bars down the sides.
            var wallpaper = Stretch(root, "Wallpaper");
            wallpaper.gameObject.AddComponent<Image>().color = Hex(0x1E3A5F);

            // Everything else lives inside a fixed 1600 x 900 area that is SCALED to fit, not
            // stretched. Windows dialogs are laid out in absolute pixels - stretching the
            // container would resize it and leave the contents at their authored size, anchored
            // top-left, which is a desktop that covers part of the screen and spills off the rest.
            var design = Design(root, "DesignRoot");

            var windows = Stretch(design, "Windows");

            // Windows first, so the shell's launch buttons have something to point at.
            var error = BuildErrorDialog(windows, t);

            var sysProps = BuildSystemProperties(windows, t);
            var nameDlg = BuildComputerNameChanges(windows, t, sysProps, error);
            var netConns = BuildNetworkConnections(windows, t);
            var ethProps = BuildEthernetProperties(windows, t, netConns, error);
            var tcpip = BuildTcpIpv4(windows, t, error);

            var serverMgr = BuildServerManager(windows, t);
            var rolesWiz = BuildAddRolesWizard(windows, t, error);
            BuildAdDsWizard(windows, t, error);

            var dhcp = BuildDhcpConsole(windows, t);
            BuildNewScopeWizard(windows, t, error);

            var dns = BuildDnsManager(windows, t, error);
            var zoneWiz = BuildNewZoneWizard(windows, t, error);

            var dnsGate = dns.GetComponentInChildren<WinTaskGate>(true);
            if (dnsGate != null)
            {
                dnsGate.openOnSuccess = zoneWiz;
                dnsGate.openOnSuccessParent = dns;
            }

            var clientSys = BuildClientSystemProperties(windows, t);
            BuildClientDomainJoin(windows, t, clientSys, error);

            // ---- LO1, steps 12-17 ----
            // Built in dependency order: a gate needs a real reference to the window it opens, so
            // anything that is opened BY a gate is built before the window holding that gate.
            BuildSecurityPermissions(windows, t, error);
            BuildAdvancedSharing(windows, t, error);
            var folderProps = BuildFolderProperties(windows, t);

            BuildFileExplorer(windows, t, error, folderProps);
            BuildNewFolderDialog(windows, t, error);

            var newUser = BuildNewUserDialog(windows, t, error);
            var groupProps = BuildGroupProperties(windows, t, error);
            var newGroup = BuildNewGroupDialog(windows, t, error, groupProps);
            var adUsers = BuildAdUsersComputers(windows, t, error, newUser, newGroup);

            // Cancelling out of STUDENTS Properties should return to the console it was opened
            // from, and that console does not exist yet when the group dialog is built.
            var groupGate = newGroup.GetComponentInChildren<WinTaskGate>(true);
            if (groupGate != null) groupGate.openOnSuccessParent = adUsers;

            var minPwd = BuildMinPasswordDialog(windows, t, error);
            BuildGpoEditor(windows, t, error, minPwd);

            // The guarded Properties button on Ethernet Properties opens TCP/IPv4.
            var ethGate = ethProps.GetComponentInChildren<WinTaskGate>(true);
            if (ethGate != null)
            {
                ethGate.openOnSuccess = tcpip;
                ethGate.openOnSuccessParent = ethProps;
            }

            BuildShell(design, t, desktop);

            desktop.windows = new System.Collections.Generic.List<WinWindow>(
                root.GetComponentsInChildren<WinWindow>(true));

            foreach (var w in desktop.windows)
                w.gameObject.SetActive(false);

            error.SetActive(false);

            EditorUtility.SetDirty(root.gameObject);

            int windowCount = desktop.windows.Count;

            status = "Built '" + RootName + "'.\n\n" +
                     "  " + windowCount + " windows + error dialog\n" +
                     "  Taskbar, Start menu, 5 desktop icons\n\n" +
                     "  Graded actions:\n" +
                     "    set_computer_name, set_ipv4, install_roles, promote_dc,\n" +
                     "    create_scope, create_reverse_zone, join_domain,\n" +
                     "    create_user_folder, create_user_account, add_group_member,\n" +
                     "    set_password_policy, share_folder, set_folder_permissions\n\n" +
                     "  Internal guards (leave unbound):\n" +
                     "    open_ipv4, open_new_zone, open_folder_properties,\n" +
                     "    open_new_user, open_new_group, create_group,\n" +
                     "    open_password_policy\n\n" +
                     "Next: add COC3StepGate to your Managers object and bind the graded action ids " +
                     "to your step ids. See the guide.";
        }
        catch (System.Exception e)
        {
            status = "Failed: " + e.Message;
            Debug.LogException(e);
        }
        finally
        {
            Undo.CollapseUndoOperations(group);
            Repaint();
        }
    }

    // ==================================================================
    // Shell
    // ==================================================================

    private static void BuildShell(RectTransform root, COC3WinTheme t, WinDesktop desktop)
    {
        // --- desktop icons ---
        var icons = Stretch(root, "Icons");
        DesktopIcon(icons, t, "Icon_ThisPC", 40f, 40f, "This PC", "SystemProperties");
        DesktopIcon(icons, t, "Icon_Network", 40f, 160f, "Network", "NetworkConnections");
        DesktopIcon(icons, t, "Icon_ServerManager", 40f, 280f, "Server Manager", "ServerManager");
        DesktopIcon(icons, t, "Icon_Explorer", 40f, 400f, "File Explorer", "FileExplorer");
        DesktopIcon(icons, t, "Icon_Client", 40f, 520f, ClientName, "ClientSystemProperties");

        // --- taskbar ---
        var taskbar = Box(root, "Taskbar", 0f, DH - 44f, DW, 44f, Hex(0x1F1F1F));

        var startBtn = FlatButton(taskbar.rectTransform, "StartButton", 0f, 0f, 56f, 44f, "");
        var sa = Undo.AddComponent<WinButtonAction>(startBtn.gameObject);
        sa.action = WinButtonAction.Kind.ToggleStartMenu;

        // Four panes rather than a Windows glyph: the flag character is not in LiberationSans and
        // renders as a hollow box with a Console warning on every rebuild.
        Box(startBtn.transform, "P1", 18f, 14f, 8f, 8f, Color.white);
        Box(startBtn.transform, "P2", 28f, 14f, 8f, 8f, Color.white);
        Box(startBtn.transform, "P3", 18f, 24f, 8f, 8f, Color.white);
        Box(startBtn.transform, "P4", 28f, 24f, 8f, 8f, Color.white);

        var clock = Label(taskbar.transform, t, "Clock", DW - 150f, 0f, 130f, 44f,
                          "10:42\n" + ServerName, TextAlignmentOptions.Right);
        clock.color = Color.white;
        clock.fontSize = 13f;

        // --- start menu ---
        var menu = Rect(root, "StartMenu", 0f, DH - 44f - 440f, 320f, 440f);
        var menuBg = menu.gameObject.AddComponent<Image>();
        menuBg.color = Hex(0x2B2B2B);

        Label(menu, t, "Header", 16f, 12f, 280f, 24f, "Windows Administrative Tools",
              TextAlignmentOptions.Left).color = Hex(0x9A9A9A);

        StartEntry(menu, t, "Entry_System", 8f, 48f, "System", "SystemProperties");
        StartEntry(menu, t, "Entry_Network", 8f, 92f, "Network Connections", "NetworkConnections");
        StartEntry(menu, t, "Entry_ServerManager", 8f, 136f, "Server Manager", "ServerManager");
        StartEntry(menu, t, "Entry_DHCP", 8f, 180f, "DHCP", "DhcpConsole");
        StartEntry(menu, t, "Entry_DNS", 8f, 224f, "DNS Manager", "DnsManager");
        StartEntry(menu, t, "Entry_Explorer", 8f, 268f, "File Explorer", "FileExplorer");
        StartEntry(menu, t, "Entry_ADUC", 8f, 312f,
                   "Active Directory Users and Computers", "AdUsersComputers");
        StartEntry(menu, t, "Entry_GPO", 8f, 356f, "Group Policy Management", "GpoEditor");

        desktop.startMenu = menu.gameObject;
        menu.gameObject.SetActive(false);
    }

    private static void DesktopIcon(Transform parent, COC3WinTheme t, string name,
                                    float x, float y, string label, string windowId)
    {
        var rt = Rect(parent, name, x, y, 110f, 100f);

        var hit = rt.gameObject.AddComponent<Image>();
        hit.color = new Color(1f, 1f, 1f, 1f / 255f);   // invisible but never culled
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = hit;

        Box(rt, "Tile", 31f, 6f, 48f, 48f, Hex(0xDCE6F0));

        var lbl = Label(rt, t, "Label", 0f, 60f, 110f, 34f, label, TextAlignmentOptions.Top);
        lbl.color = Color.white;
        lbl.fontSize = 13f;

        var a = Undo.AddComponent<WinButtonAction>(rt.gameObject);
        a.action = WinButtonAction.Kind.OpenWindow;
        a.windowId = windowId;
    }

    private static void StartEntry(Transform parent, COC3WinTheme t, string name,
                                   float x, float y, string label, string windowId)
    {
        float w = ((RectTransform)parent).rect.width - 2f * x;

        var btn = FlatButton((RectTransform)parent, name, x, y, w, 36f, "");

        var lbl = Label(btn.transform, t, "Label", 12f, 0f, w - 20f, 36f, label);
        lbl.color = Color.white;
        lbl.fontSize = t.fontSize - 1f;

        var a = Undo.AddComponent<WinButtonAction>(btn.gameObject);
        a.action = WinButtonAction.Kind.OpenWindow;
        a.windowId = windowId;
    }

    // ==================================================================
    // Windows
    // ==================================================================

    private static WinWindow BuildSystemProperties(Transform parent, COC3WinTheme t)
    {
        var body = COC3WinWidgets.Window(parent, t, "SystemProperties", "System Properties",
                                         470f, 380f, out RectTransform root);
        var win = Register(root, "SystemProperties", "System Properties");
        FixCloseGlyph(root);

        float w = body.rect.width;

        Tabs(body, t, new[] { "Computer Name", "Hardware", "Advanced", "Remote" }, 0);

        Label(body, t, "Blurb", 0f, 46f, w, 40f,
              "Windows uses the following information to identify your computer on the network.");

        COC3WinWidgets.Field(body, t, "Description", 0f, 100f, 150f, w - 160f,
                             "Computer description:", "", "", true);

        COC3WinWidgets.Field(body, t, "FullName", 0f, 140f, 150f, w - 160f,
                             "Full computer name:", "WIN-8KJ2M4QX", "", true);

        COC3WinWidgets.Field(body, t, "Workgroup", 0f, 180f, 150f, w - 160f,
                             "Workgroup:", "WORKGROUP", "", true);

        Label(body, t, "Hint", 0f, 226f, w, 40f,
              "To rename this computer or change its domain or workgroup, click Change.")
            .color = t.disabledText;

        var change = COC3WinWidgets.Button(body, t, "ChangeButton",
                                           w - 100f, 268f, 100f, t.buttonSize.y, "Change...");
        var ca = Undo.AddComponent<WinButtonAction>(change.gameObject);
        ca.action = WinButtonAction.Kind.OpenWindow;
        ca.windowId = "ComputerNameChanges";
        ca.window = win;

        CancelButton(body, t, win, w, 310f);
        return win;
    }

    private static WinWindow BuildComputerNameChanges(Transform parent, COC3WinTheme t,
                                                      WinWindow owner, GameObject error)
    {
        var body = COC3WinWidgets.Window(parent, t, "ComputerNameChanges",
                                         "Computer Name/Domain Changes",
                                         420f, 340f, out RectTransform root);
        var win = Register(root, "ComputerNameChanges", "Computer Name/Domain Changes");
        FixCloseGlyph(root);

        float w = body.rect.width;

        Label(body, t, "Blurb", 0f, 0f, w, 40f,
              "You can change the name and the membership of this computer.");

        var nameField = COC3WinWidgets.Field(body, t, "ComputerName", 0f, 50f, 120f, w - 130f,
                                             "Computer name:", "", "", false);

        var group = COC3WinWidgets.GroupBox(body, t, "MemberOf", 0f, 100f, w, 110f, "Member of");

        RadioRow(group, t, "Radio_Domain", 8f, 6f, 200f, "Domain:", "membership");

        var workgroup = RadioRow(group, t, "Radio_Workgroup", 8f, 56f, 200f,
                                 "Workgroup:", "membership");
        workgroup.isOn = true;

        var ok = COC3WinWidgets.Button(body, t, "OK", PrimaryX(w, t), 270f,
                                       t.buttonSize.x, t.buttonSize.y, "OK", true);

        var gate = Undo.AddComponent<WinTaskGate>(root.gameObject);
        gate.actionId = "set_computer_name";
        gate.submitButton = ok;
        gate.closeOnSuccess = win;
        gate.errorDialog = error;
        gate.errorText = FindLabel(error.transform, "Message");
        gate.fields = new[]
        {
            new WinTaskGate.FieldRequirement
            {
                field = nameField, expected = ServerName, displayName = "computer name"
            }
        };
        gate.mustBeOn = new WinToggle[0];
        gate.mustBeOff = new WinToggle[0];

        CancelButton(body, t, win, w, 270f);
        return win;
    }

    private static WinWindow BuildNetworkConnections(Transform parent, COC3WinTheme t)
    {
        var body = COC3WinWidgets.Window(parent, t, "NetworkConnections", "Network Connections",
                                         560f, 320f, out RectTransform root);
        var win = Register(root, "NetworkConnections", "Network Connections");
        FixCloseGlyph(root);

        float w = body.rect.width;

        Label(body, t, "Blurb", 0f, 0f, w, 24f, "Organise      Disable this network device");

        var listArea = Box(body, "List", 0f, 32f, w, 190f, t.fieldBackground);

        var row = FlatButton(listArea.rectTransform, "Row_Ethernet", 1f, 1f, w - 2f, 46f, "");
        row.GetComponent<Image>().color = t.selection;

        Box(row.transform, "Icon", 10f, 9f, 28f, 28f, Hex(0x5B87C7));
        Label(row.transform, t, "Name", 48f, 4f, 300f, 20f, "Ethernet");
        Label(row.transform, t, "Sub", 48f, 22f, 400f, 20f, "Unidentified network")
            .color = t.disabledText;

        var ra = Undo.AddComponent<WinButtonAction>(row.gameObject);
        ra.action = WinButtonAction.Kind.OpenWindow;
        ra.windowId = "EthernetProperties";
        ra.window = win;

        Label(body, t, "Tip", 0f, 232f, w, 24f,
              "Double-click a connection to open its properties.").color = t.disabledText;

        CancelButton(body, t, win, w, 258f, "Close");
        return win;
    }

    private static WinWindow BuildEthernetProperties(Transform parent, COC3WinTheme t,
                                                     WinWindow owner, GameObject error)
    {
        var body = COC3WinWidgets.Window(parent, t, "EthernetProperties", "Ethernet Properties",
                                         460f, 440f, out RectTransform root);
        var win = Register(root, "EthernetProperties", "Ethernet Properties");
        FixCloseGlyph(root);

        float w = body.rect.width;

        Tabs(body, t, new[] { "Networking", "Sharing" }, 0);

        Label(body, t, "Blurb", 0f, 46f, w, 24f, "This connection uses the following items:");

        var listArea = Box(body, "Items", 0f, 76f, w, 190f, t.fieldBackground);

        // The IPv4 row is the one the step needs. The others are real Windows entries, and they
        // are what make selecting the right one an actual decision rather than the only thing
        // on screen that can be clicked.
        string[] items =
        {
            "Client for Microsoft Networks",
            "File and Printer Sharing for Microsoft Networks",
            "QoS Packet Scheduler",
            "Internet Protocol Version 4 (TCP/IPv4)",
            "Internet Protocol Version 6 (TCP/IPv6)"
        };

        WinToggle ipv4Toggle = null;

        for (int i = 0; i < items.Length; i++)
        {
            var rowBtn = FlatButton(listArea.rectTransform, "Item_" + i, 1f, 1f + i * 32f,
                                    w - 2f, 32f, "");
            // Alpha 1/255, never 0. A fully transparent Image is culled by
            // CanvasRenderer.cullTransparentMesh and stops receiving clicks - the row would
            // look fine and simply never select, so Properties would reject forever.
            rowBtn.GetComponent<Image>().color = new Color(1f, 1f, 1f, 1f / 255f);

            var sel = Box(rowBtn.transform, "Selected", 0f, 0f, w - 2f, 32f, t.selection);
            sel.raycastTarget = false;

            Box(rowBtn.transform, "Check", 8f, 9f, 14f, 14f, t.accent).raycastTarget = false;
            Label(rowBtn.transform, t, "Label", 30f, 0f, w - 40f, 32f, items[i]);

            var toggle = Undo.AddComponent<WinToggle>(rowBtn.gameObject);
            toggle.groupId = "eth_items";
            toggle.indicator = sel;
            toggle.isOn = false;

            if (i == 3) ipv4Toggle = toggle;
        }

        var props = COC3WinWidgets.Button(body, t, "PropertiesButton",
                                          w - 100f, 276f, 100f, t.buttonSize.y, "Properties");

        // Guarded rather than a plain open: pressing Properties with the wrong row selected gets
        // the error a real dialog would give, which is the point of drawing the other four rows.
        var gate = Undo.AddComponent<WinTaskGate>(root.gameObject);
        gate.actionId = "open_ipv4";
        gate.submitButton = props;
        gate.errorDialog = error;
        gate.errorText = FindLabel(error.transform, "Message");
        gate.missingOptionMessage =
            "Select the item you want to configure first.\n\n" +
            "The address settings for this connection live under Internet Protocol Version 4 (TCP/IPv4).";
        gate.fields = new WinTaskGate.FieldRequirement[0];
        gate.mustBeOn = ipv4Toggle != null ? new[] { ipv4Toggle } : new WinToggle[0];
        gate.mustBeOff = new WinToggle[0];

        CancelButton(body, t, win, w, 320f);
        return win;
    }

    private static WinWindow BuildTcpIpv4(Transform parent, COC3WinTheme t, GameObject error)
    {
        var body = COC3WinWidgets.Window(parent, t, "TcpIpv4",
                                         "Internet Protocol Version 4 (TCP/IPv4) Properties",
                                         470f, 520f, out RectTransform root);
        var win = Register(root, "TcpIpv4", "Internet Protocol Version 4 (TCP/IPv4) Properties");
        FixCloseGlyph(root);

        float w = body.rect.width;

        Tabs(body, t, new[] { "General" }, 0);

        RadioRow(body, t, "Radio_AutoIP", 0f, 52f, w,
                 "Obtain an IP address automatically", "ip_mode");

        var staticIpToggle = RadioRow(body, t, "Radio_StaticIP", 0f, 82f, w,
                                      "Use the following IP address:", "ip_mode");

        var ip = COC3WinWidgets.Field(body, t, "IP", 20f, 116f, 150f, w - 180f,
                                        "IP address:", "", "", false);
        var mask = COC3WinWidgets.Field(body, t, "Mask", 20f, 150f, 150f, w - 180f,
                                        "Subnet mask:", "", "", false);
        var gw = COC3WinWidgets.Field(body, t, "Gateway", 20f, 184f, 150f, w - 180f,
                                        "Default gateway:", "", "", false);

        RadioRow(body, t, "Radio_AutoDNS", 0f, 226f, w,
                 "Obtain DNS server address automatically", "dns_mode");

        var staticDnsToggle = RadioRow(body, t, "Radio_StaticDNS", 0f, 256f, w,
                                       "Use the following DNS server addresses:", "dns_mode");

        var dns1 = COC3WinWidgets.Field(body, t, "DNS1", 20f, 290f, 150f, w - 180f,
                                        "Preferred DNS server:", "", "", false);
        COC3WinWidgets.Field(body, t, "DNS2", 20f, 324f, 150f, w - 180f,
                             "Alternate DNS server:", "", "", false);

        var ok = COC3WinWidgets.Button(body, t, "OK", PrimaryX(w, t), 400f,
                                       t.buttonSize.x, t.buttonSize.y, "OK", true);

        var gate = Undo.AddComponent<WinTaskGate>(root.gameObject);
        gate.actionId = "set_ipv4";
        gate.submitButton = ok;
        gate.closeOnSuccess = win;
        gate.errorDialog = error;
        gate.errorText = FindLabel(error.transform, "Message");
        gate.missingOptionMessage =
            "A server must not take its address from DHCP.\n\n" +
            "Choose 'Use the following IP address' and 'Use the following DNS server addresses'.";
        gate.mustBeOn = new[] { staticIpToggle, staticDnsToggle };
        gate.mustBeOff = new WinToggle[0];
        gate.fields = new[]
        {
            new WinTaskGate.FieldRequirement { field = ip,   expected = ServerIP, displayName = "IP address" },
            new WinTaskGate.FieldRequirement { field = mask, expected = Mask,     displayName = "subnet mask" },
            new WinTaskGate.FieldRequirement { field = gw,   expected = Gateway,  displayName = "default gateway" },
            new WinTaskGate.FieldRequirement { field = dns1, expected = Dns,      displayName = "preferred DNS server" }
        };

        CancelButton(body, t, win, w, 400f);
        return win;
    }

    private static GameObject BuildErrorDialog(Transform parent, COC3WinTheme t)
    {
        var body = COC3WinWidgets.Window(parent, t, "ErrorDialog", "Windows",
                                         420f, 200f, out RectTransform root);
        FixCloseGlyph(root);

        float w = body.rect.width;

        Box(body, "Icon", 0f, 6f, 34f, 34f, Hex(0xD83B01));

        var msg = Label(body, t, "Message", 48f, 0f, w - 48f, 90f, "");
        msg.alignment = TextAlignmentOptions.TopLeft;

        var ok = COC3WinWidgets.Button(body, t, "OK", w - t.buttonSize.x, 110f,
                                       t.buttonSize.x, t.buttonSize.y, "OK", true);

        // Not a WinWindow: it must be able to sit on top of one without becoming its child, and
        // it is dismissed by its own OK rather than by the desktop.
        var closer = Undo.AddComponent<ErrorDialogCloser>(ok.gameObject);
        closer.target = root.gameObject;

        return root.gameObject;
    }


    // ==================================================================
    // Server Manager and its wizards - steps 5 and 6
    // ==================================================================

    private static WinWindow BuildServerManager(Transform parent, COC3WinTheme t)
    {
        var body = COC3WinWidgets.Window(parent, t, "ServerManager", "Server Manager",
                                         760f, 460f, out RectTransform root);
        var win = Register(root, "ServerManager", "Server Manager");
        FixCloseGlyph(root);

        float w = body.rect.width;
        float h = body.rect.height;

        // Left navigation. Decoration - the tasks in this module are all reached from the
        // Manage menu, and a nav pane that did nothing but look clickable would waste the
        // learner's time.
        var nav = Box(body, "Nav", 0f, 0f, 170f, h, Hex(0xEAEAEA));
        string[] navItems = { "Dashboard", "Local Server", "All Servers", "File and Storage Services" };

        for (int i = 0; i < navItems.Length; i++)
        {
            var item = Box(nav.transform, "Nav_" + i, 0f, 12f + i * 30f, 170f, 28f,
                           i == 0 ? t.selection : new Color(0f, 0f, 0f, 0f));
            item.raycastTarget = false;
            Label(item.transform, t, "Label", 14f, 0f, 150f, 28f, navItems[i]);
        }

        // The Manage menu is where Add Roles and Features actually lives.
        var manage = FlatButton(body, "ManageMenu", 186f, 0f, 90f, 26f, "");
        Label(manage.transform, t, "Label", 8f, 0f, 80f, 26f, "Manage");

        var ma = Undo.AddComponent<WinButtonAction>(manage.gameObject);
        ma.action = WinButtonAction.Kind.OpenWindow;
        ma.windowId = "AddRolesWizard";
        ma.window = win;

        Label(body, t, "Welcome", 186f, 40f, w - 200f, 30f, "WELCOME TO SERVER MANAGER")
            .color = t.disabledText;

        Tile(body, t, "Tile_1", 186f, 76f, w - 200f, "1", "Configure this local server", null, win);
        Tile(body, t, "Tile_2", 186f, 136f, w - 200f, "2", "Add roles and features",
             "AddRolesWizard", win);

        // The notification flag. After the roles are installed this is where Windows offers to
        // promote the server, and it is genuinely how a technician reaches that wizard.
        var flag = FlatButton(body, "NotificationFlag", w - 40f, 0f, 30f, 26f, "");
        Box(flag.transform, "Dot", 8f, 6f, 14f, 14f, Hex(0xD83B01)).raycastTarget = false;

        var fa = Undo.AddComponent<WinButtonAction>(flag.gameObject);
        fa.action = WinButtonAction.Kind.OpenWindow;
        fa.windowId = "AdDsWizard";
        fa.window = win;

        Label(body, t, "FlagHint", 186f, h - 60f, w - 200f, 40f,
              "After the roles are installed, the notification flag offers to promote this server " +
              "to a domain controller.").color = t.disabledText;

        CancelButton(body, t, win, w, h - 30f, "Close");
        return win;
    }

    private static void Tile(RectTransform body, COC3WinTheme t, string name,
                             float x, float y, float w, string number, string label,
                             string windowId, WinWindow owner)
    {
        var tile = FlatButton(body, name, x, y, w, 50f, "");
        tile.GetComponent<Image>().color = Hex(0xF5F5F5);

        Label(tile.transform, t, "Number", 12f, 0f, 24f, 50f, number).color = t.accent;
        Label(tile.transform, t, "Label", 42f, 0f, w - 50f, 50f, label);

        if (string.IsNullOrEmpty(windowId)) return;

        var a = Undo.AddComponent<WinButtonAction>(tile.gameObject);
        a.action = WinButtonAction.Kind.OpenWindow;
        a.windowId = windowId;
        a.window = owner;
    }

    private static WinWindow BuildAddRolesWizard(Transform parent, COC3WinTheme t, GameObject error)
    {
        var body = COC3WinWidgets.Window(parent, t, "AddRolesWizard",
                                         "Add Roles and Features Wizard",
                                         680f, 500f, out RectTransform root);
        var win = Register(root, "AddRolesWizard", "Add Roles and Features Wizard");
        FixCloseGlyph(root);

        float w = body.rect.width;
        float h = body.rect.height;

        WizardSteps(body, t, h, new[]
        {
            "Before You Begin", "Installation Type", "Server Selection",
            "Server Roles", "Features", "Confirmation"
        }, 3);

        float cx = 196f;
        float cw = w - cx;

        Label(body, t, "Prompt", cx, 0f, cw, 24f,
              "Select one or more roles to install on the selected server.");
        Label(body, t, "ColHead", cx, 28f, cw, 20f, "Roles").color = t.disabledText;

        var list = Box(body, "Roles", cx, 50f, cw, 250f, t.fieldBackground);

        // All five the job order calls for. Steps 5, 9 and 10 all live on this page - a
        // technician ticks them in one pass and presses Install once, so they are one action here.
        string[] roles =
        {
            "Active Directory Domain Services",
            "DHCP Server",
            "DNS Server",
            "File and Storage Services",
            "Print and Document Services"
        };

        var required = new WinToggle[roles.Length];

        for (int i = 0; i < roles.Length; i++)
            required[i] = CheckRow(list.rectTransform, t, "Role_" + i, 8f, 10f + i * 32f,
                                   cw - 16f, roles[i]);

        Label(body, t, "Note", cx, 310f, cw, 40f,
              "Ticking a role may offer to add the management tools it needs. Accept them.")
            .color = t.disabledText;

        var install = COC3WinWidgets.Button(body, t, "Install", PrimaryX(w, t), h - 34f,
                                            t.buttonSize.x, t.buttonSize.y, "Install", true);

        var gate = Undo.AddComponent<WinTaskGate>(root.gameObject);
        gate.actionId = "install_roles";
        gate.submitButton = install;
        gate.closeOnSuccess = win;
        gate.errorDialog = error;
        gate.errorText = FindLabel(error.transform, "Message");
        gate.missingOptionMessage =
            "The job order asks for more roles than are currently selected.\\n\\n" +
            "Active Directory Domain Services, DHCP Server, DNS Server, File and Storage Services " +
            "and Print and Document Services are all required on this server.";
        gate.fields = new WinTaskGate.FieldRequirement[0];
        gate.mustBeOn = required;
        gate.mustBeOff = new WinToggle[0];

        CancelButton(body, t, win, w, h - 34f);
        return win;
    }

    private static WinWindow BuildAdDsWizard(Transform parent, COC3WinTheme t, GameObject error)
    {
        var body = COC3WinWidgets.Window(parent, t, "AdDsWizard",
                                         "Active Directory Domain Services Configuration Wizard",
                                         680f, 480f, out RectTransform root);
        var win = Register(root, "AdDsWizard", "Active Directory Domain Services Configuration Wizard");
        FixCloseGlyph(root);

        float w = body.rect.width;
        float h = body.rect.height;

        WizardSteps(body, t, h, new[]
        {
            "Deployment Configuration", "Domain Controller Options", "DNS Options",
            "Additional Options", "Paths", "Review Options", "Prerequisites Check"
        }, 0);

        float cx = 210f;
        float cw = w - cx;

        Label(body, t, "Prompt", cx, 0f, cw, 24f, "Select the deployment operation");

        RadioRow(body, t, "Radio_ExistingDomain", cx, 32f, cw,
                 "Add a domain controller to an existing domain", "deployment");

        RadioRow(body, t, "Radio_NewDomain", cx, 62f, cw,
                 "Add a new domain to an existing forest", "deployment");

        var forestToggle = RadioRow(body, t, "Radio_NewForest", cx, 92f, cw,
                                    "Add a new forest", "deployment");

        Label(body, t, "Sub", cx, 130f, cw, 24f, "Specify the domain information for this operation");

        var domain = COC3WinWidgets.Field(body, t, "RootDomain", cx, 162f, 150f, cw - 160f,
                                          "Root domain name:", "", "", false);

        Label(body, t, "Note", cx, 206f, cw, 60f,
              "A new forest creates the first domain. Every account, group and policy in this " +
              "module lives inside it, so the name chosen here is the one the client will join.")
            .color = t.disabledText;

        var next = COC3WinWidgets.Button(body, t, "Next", PrimaryX(w, t), h - 34f,
                                         t.buttonSize.x, t.buttonSize.y, "Next", true);

        var gate = Undo.AddComponent<WinTaskGate>(root.gameObject);
        gate.actionId = "promote_dc";
        gate.submitButton = next;
        gate.closeOnSuccess = win;
        gate.errorDialog = error;
        gate.errorText = FindLabel(error.transform, "Message");
        gate.missingOptionMessage =
            "Installing AD DS does not create a domain.\\n\\n" +
            "This is the first domain controller on this network, so choose 'Add a new forest'.";
        gate.mustBeOn = new[] { forestToggle };
        gate.mustBeOff = new WinToggle[0];
        gate.fields = new[]
        {
            new WinTaskGate.FieldRequirement
            {
                field = domain, expected = DomainFQDN, displayName = "root domain name"
            }
        };

        CancelButton(body, t, win, w, h - 34f);
        return win;
    }

    /// <summary>The step list down the left of a Windows wizard.</summary>
    private static void WizardSteps(RectTransform body, COC3WinTheme t, float h,
                                    string[] steps, int current)
    {
        var pane = Box(body, "WizardSteps", 0f, 0f, 190f, h - 44f, Hex(0xF7F7F7));

        for (int i = 0; i < steps.Length; i++)
        {
            var row = Label(pane.transform, t, "Step_" + i, 12f, 8f + i * 26f, 170f, 24f, steps[i]);
            row.color = i == current ? t.text : t.disabledText;
            if (i == current) row.fontStyle = FontStyles.Bold;
        }
    }

    /// <summary>
    /// A radio drawn from shapes rather than COC3WinWidgets.Radio.
    ///
    /// TWO REASONS THE FACTORY VERSION CANNOT BE USED HERE.
    ///
    /// It is not clickable: its hit Image is created at alpha 0, and a fully transparent Image is
    /// culled by CanvasRenderer.cullTransparentMesh and stops receiving pointer events. The row
    /// looks right and never responds - which is why every radio in TCP/IPv4 stayed unselected
    /// however many times it was pressed.
    ///
    /// And it has no state: the dot is painted once at build time from the `selected` argument,
    /// as the character "◉" or "○" - neither of which LiberationSans has. This draws a ring and a
    /// filled dot as Images, and WinToggle switches the dot.
    /// </summary>
    private static WinToggle RadioRow(RectTransform parent, COC3WinTheme t, string name,
                                      float x, float y, float w, string label, string groupId)
    {
        var btn = FlatButton(parent, name, x, y, w, 26f, "");

        var ring = COC3WinWidgets.BorderedBox(btn.transform, "Ring", 0f, 5f, 16f, 16f,
                                              t.fieldBackground, t.fieldBorder);

        var dot = Box(ring, "Dot", 4f, 4f, 8f, 8f, t.accent);
        dot.raycastTarget = false;

        // Off at build time. WinToggle.Awake would clear it on Play anyway, but until then every
        // radio in the scene looks selected in the Editor - which is confusing to read and makes a
        // screenshot of an untouched dialog look like a filled-in one.
        dot.enabled = false;

        Label(btn.transform, t, "Label", 26f, 0f, w - 30f, 26f, label);

        var toggle = Undo.AddComponent<WinToggle>(btn.gameObject);
        toggle.groupId = groupId;
        toggle.indicator = dot;
        toggle.isOn = false;
        return toggle;
    }

    /// <summary>
    /// A checkbox drawn from shapes rather than COC3WinWidgets.Checkbox.
    ///
    /// The factory version paints a fixed tick using "✓", which LiberationSans does not have -
    /// it comes out as a hollow box and logs a warning on every rebuild. It also has no state:
    /// whatever was passed at build time is what it shows forever. This one is a real toggle with
    /// an Image for the tick.
    /// </summary>
    private static WinToggle CheckRow(RectTransform parent, COC3WinTheme t, string name,
                                      float x, float y, float w, string label)
    {
        var btn = FlatButton(parent, name, x, y, w, 28f, "");

        var box = COC3WinWidgets.BorderedBox(btn.transform, "Box", 0f, 6f, 16f, 16f,
                                             t.fieldBackground, t.fieldBorder);

        var tick = Box(box, "Tick", 3f, 3f, 10f, 10f, t.accent);
        tick.raycastTarget = false;
        tick.enabled = false;      // as above - cleared on Play, but the Editor should match

        Label(btn.transform, t, "Label", 26f, 0f, w - 30f, 28f, label);

        var toggle = Undo.AddComponent<WinToggle>(btn.gameObject);
        toggle.indicator = tick;
        toggle.isOn = false;
        return toggle;
    }


    // ==================================================================
    // DHCP, DNS and the client - steps 7, 8 and 11
    // ==================================================================

    private static WinWindow BuildDhcpConsole(Transform parent, COC3WinTheme t)
    {
        var body = COC3WinWidgets.Window(parent, t, "DhcpConsole", "DHCP",
                                         620f, 360f, out RectTransform root);
        var win = Register(root, "DhcpConsole", "DHCP");
        FixCloseGlyph(root);

        float w = body.rect.width;
        float h = body.rect.height;

        var tree = Box(body, "Tree", 0f, 0f, 220f, h - 44f, t.fieldBackground);

        Label(tree.transform, t, "Root", 10f, 8f, 200f, 24f, ServerName + "." + DomainFQDN);

        // The IPv4 node opens the wizard directly. Unlike the DNS console below, choosing IPv4
        // over IPv6 is not the thing being taught here - the scope values are - so a guard would
        // add a click without adding a lesson.
        var ipv4 = FlatButton(tree.rectTransform, "Node_IPv4", 18f, 36f, 200f, 26f, "");
        Label(ipv4.transform, t, "Label", 12f, 0f, 180f, 26f, "IPv4");

        var a = Undo.AddComponent<WinButtonAction>(ipv4.gameObject);
        a.action = WinButtonAction.Kind.OpenWindow;
        a.windowId = "NewScopeWizard";
        a.window = win;

        Label(tree.transform, t, "Node_IPv6", 30f, 66f, 180f, 26f, "IPv6").color = t.disabledText;

        Label(body, t, "Hint", 236f, 8f, w - 250f, 60f,
              "Right-click IPv4 and choose New Scope to hand out addresses to the lab clients.\n" +
              "Here, clicking IPv4 opens the wizard.").color = t.disabledText;

        CancelButton(body, t, win, w, h - 34f, "Close");
        return win;
    }

    private static WinWindow BuildNewScopeWizard(Transform parent, COC3WinTheme t, GameObject error)
    {
        var body = COC3WinWidgets.Window(parent, t, "NewScopeWizard", "New Scope Wizard",
                                         620f, 400f, out RectTransform root);
        var win = Register(root, "NewScopeWizard", "New Scope Wizard");
        FixCloseGlyph(root);

        float w = body.rect.width;
        float h = body.rect.height;

        Label(body, t, "Head", 0f, 0f, w, 24f, "IP Address Range");
        Label(body, t, "Blurb", 0f, 26f, w, 44f,
              "You define the scope address range by identifying a set of consecutive IP addresses.")
            .color = t.disabledText;

        // The real wizard spreads these over three pages - Address Range, Exclusions, and Router.
        // Collapsed to one here: the pages in between add clicks without adding a decision, and
        // the three values are what the job order actually specifies.
        var start = COC3WinWidgets.Field(body, t, "StartIP", 0f, 84f, 180f, w - 190f,
                                         "Start IP address:", "", "", false);
        var mask = COC3WinWidgets.Field(body, t, "ScopeMask", 0f, 122f, 180f, w - 190f,
                                         "Subnet mask:", "", "", false);
        var router = COC3WinWidgets.Field(body, t, "Router", 0f, 160f, 180f, w - 190f,
                                          "Router (default gateway):", "", "", false);

        Label(body, t, "Note", 0f, 206f, w, 60f,
              "The pool must start clear of any static address already in use. " + ServerName +
              " holds " + ServerIP + ", so a scope that began at .10 would eventually hand a " +
              "client the server's own address.").color = t.disabledText;

        var next = COC3WinWidgets.Button(body, t, "Next", PrimaryX(w, t), h - 34f,
                                         t.buttonSize.x, t.buttonSize.y, "Next", true);

        var gate = Undo.AddComponent<WinTaskGate>(root.gameObject);
        gate.actionId = "create_scope";
        gate.submitButton = next;
        gate.closeOnSuccess = win;
        gate.errorDialog = error;
        gate.errorText = FindLabel(error.transform, "Message");
        gate.mustBeOn = new WinToggle[0];
        gate.mustBeOff = new WinToggle[0];
        gate.fields = new[]
        {
            new WinTaskGate.FieldRequirement { field = start,  expected = ScopeStart, displayName = "start IP address" },
            new WinTaskGate.FieldRequirement { field = mask,   expected = Mask,       displayName = "subnet mask" },
            new WinTaskGate.FieldRequirement { field = router, expected = Gateway,    displayName = "router address" }
        };

        CancelButton(body, t, win, w, h - 34f);
        return win;
    }

    private static WinWindow BuildDnsManager(Transform parent, COC3WinTheme t, GameObject error)
    {
        var body = COC3WinWidgets.Window(parent, t, "DnsManager", "DNS Manager",
                                         640f, 380f, out RectTransform root);
        var win = Register(root, "DnsManager", "DNS Manager");
        FixCloseGlyph(root);

        float w = body.rect.width;
        float h = body.rect.height;

        var tree = Box(body, "Tree", 0f, 0f, 260f, h - 44f, t.fieldBackground);
        Label(tree.transform, t, "Root", 10f, 8f, 240f, 24f, ServerName);

        // Forward vs reverse IS the lesson in this step - promotion created the forward zone
        // automatically, and the learner has to notice the reverse one is missing. So the node
        // choice is guarded rather than being a free click.
        RadioRow(tree.rectTransform, t, "Node_Forward", 18f, 36f, 230f,
                 "Forward Lookup Zones", "dns_node");

        var reverse = RadioRow(tree.rectTransform, t, "Node_Reverse", 18f, 66f, 230f,
                               "Reverse Lookup Zones", "dns_node");

        RadioRow(tree.rectTransform, t, "Node_Forwarders", 18f, 96f, 230f,
                 "Conditional Forwarders", "dns_node");

        Label(body, t, "Blurb", 276f, 8f, w - 290f, 80f,
              "Promoting the domain created the forward lookup zone for " + DomainFQDN +
              " automatically. The reverse zone is what lets a technician resolve an address " +
              "back to a name, and it is not created for you.").color = t.disabledText;

        var newZone = COC3WinWidgets.Button(body, t, "NewZone", w - 110f, 110f,
                                            110f, t.buttonSize.y, "New Zone...");

        var gate = Undo.AddComponent<WinTaskGate>(root.gameObject);
        gate.actionId = "open_new_zone";
        gate.submitButton = newZone;
        gate.errorDialog = error;
        gate.errorText = FindLabel(error.transform, "Message");
        gate.missingOptionMessage =
            "Select the zone type you want to create first.\n\n" +
            "The forward zone for this domain already exists. What is missing is the REVERSE " +
            "lookup zone, which maps addresses back to names.";
        gate.fields = new WinTaskGate.FieldRequirement[0];
        gate.mustBeOn = new[] { reverse };
        gate.mustBeOff = new WinToggle[0];

        CancelButton(body, t, win, w, h - 34f, "Close");
        return win;
    }

    private static WinWindow BuildNewZoneWizard(Transform parent, COC3WinTheme t, GameObject error)
    {
        var body = COC3WinWidgets.Window(parent, t, "NewZoneWizard", "New Zone Wizard",
                                         620f, 380f, out RectTransform root);
        var win = Register(root, "NewZoneWizard", "New Zone Wizard");
        FixCloseGlyph(root);

        float w = body.rect.width;
        float h = body.rect.height;

        Label(body, t, "Head", 0f, 0f, w, 24f, "Reverse Lookup Zone Name");
        Label(body, t, "Blurb", 0f, 26f, w, 40f,
              "A reverse lookup zone translates IP addresses into DNS names.").color = t.disabledText;

        var ipv4 = RadioRow(body, t, "Radio_IPv4Zone", 0f, 74f, w,
                            "IPv4 Reverse Lookup Zone", "zone_kind");
        RadioRow(body, t, "Radio_IPv6Zone", 0f, 104f, w,
                 "IPv6 Reverse Lookup Zone", "zone_kind");

        var netId = COC3WinWidgets.Field(body, t, "NetworkId", 20f, 148f, 150f, w - 190f,
                                         "Network ID:", "", "", false);

        Label(body, t, "Note", 20f, 192f, w - 30f, 60f,
              "The network ID is the part of the address the hosts share. Type it in the same " +
              "order as the address itself - the wizard reverses it for you when it names the zone.")
            .color = t.disabledText;

        var next = COC3WinWidgets.Button(body, t, "Next", PrimaryX(w, t), h - 34f,
                                         t.buttonSize.x, t.buttonSize.y, "Next", true);

        var gate = Undo.AddComponent<WinTaskGate>(root.gameObject);
        gate.actionId = "create_reverse_zone";
        gate.submitButton = next;
        gate.closeOnSuccess = win;
        gate.errorDialog = error;
        gate.errorText = FindLabel(error.transform, "Message");
        gate.missingOptionMessage = "Choose whether this is an IPv4 or an IPv6 reverse lookup zone.";
        gate.mustBeOn = new[] { ipv4 };
        gate.mustBeOff = new WinToggle[0];
        gate.fields = new[]
        {
            new WinTaskGate.FieldRequirement
            {
                field = netId, expected = NetworkId, displayName = "network ID"
            }
        };

        CancelButton(body, t, win, w, h - 34f);
        return win;
    }

    /// <summary>
    /// The client's System Properties.
    ///
    /// A SEPARATE WINDOW FROM THE SERVER'S, on purpose. Step 11 is performed on CLIENT01, and the
    /// gate on the server's Computer Name dialog is already spoken for by step 2 - two WinTaskGates
    /// cannot share one OK button, because both would run and one would always reject.
    ///
    /// This is the honest seam in the current design: one desktop is standing in for two machines.
    /// When the walk-up station system decides which PC the learner is sitting at, this pair should
    /// become the whole desktop's content while they are at the client.
    /// </summary>
    private static WinWindow BuildClientSystemProperties(Transform parent, COC3WinTheme t)
    {
        var body = COC3WinWidgets.Window(parent, t, "ClientSystemProperties",
                                         "System Properties  -  " + ClientName,
                                         470f, 340f, out RectTransform root);
        var win = Register(root, "ClientSystemProperties", "System Properties - " + ClientName);
        FixCloseGlyph(root);

        float w = body.rect.width;

        Tabs(body, t, new[] { "Computer Name", "Hardware", "Advanced", "Remote" }, 0);

        Label(body, t, "Blurb", 0f, 46f, w, 40f,
              "This computer is not yet a member of the domain.");

        COC3WinWidgets.Field(body, t, "FullName", 0f, 100f, 150f, w - 160f,
                             "Full computer name:", ClientName, "", true);
        COC3WinWidgets.Field(body, t, "Workgroup", 0f, 140f, 150f, w - 160f,
                             "Workgroup:", "WORKGROUP", "", true);

        Label(body, t, "Hint", 0f, 186f, w, 40f,
              "A client that is not a domain member cannot use domain accounts or receive Group Policy.")
            .color = t.disabledText;

        var change = COC3WinWidgets.Button(body, t, "ChangeButton", w - 100f, 232f,
                                           100f, t.buttonSize.y, "Change...");
        var ca = Undo.AddComponent<WinButtonAction>(change.gameObject);
        ca.action = WinButtonAction.Kind.OpenWindow;
        ca.windowId = "ClientDomainJoin";
        ca.window = win;

        CancelButton(body, t, win, w, 274f);
        return win;
    }

    private static WinWindow BuildClientDomainJoin(Transform parent, COC3WinTheme t,
                                                   WinWindow owner, GameObject error)
    {
        var body = COC3WinWidgets.Window(parent, t, "ClientDomainJoin",
                                         "Computer Name/Domain Changes",
                                         420f, 330f, out RectTransform root);
        var win = Register(root, "ClientDomainJoin", "Computer Name/Domain Changes");
        FixCloseGlyph(root);

        float w = body.rect.width;

        Label(body, t, "Blurb", 0f, 0f, w, 40f,
              "You can change the name and the membership of this computer.");

        COC3WinWidgets.Field(body, t, "ComputerName", 0f, 50f, 120f, w - 130f,
                             "Computer name:", ClientName, "", true);

        var group = COC3WinWidgets.GroupBox(body, t, "MemberOf", 0f, 100f, w, 120f, "Member of");

        var domainRadio = RadioRow(group, t, "Radio_Domain", 8f, 4f, 200f, "Domain:", "client_membership");
        var domainField = COC3WinWidgets.Field(group, t, "DomainName", 26f, 32f, 0f, w - 60f,
                                               "", "", "", false);

        var workgroup = RadioRow(group, t, "Radio_Workgroup", 8f, 62f, 200f,
                                 "Workgroup:", "client_membership");
        workgroup.isOn = true;

        var ok = COC3WinWidgets.Button(body, t, "OK", PrimaryX(w, t), 260f,
                                       t.buttonSize.x, t.buttonSize.y, "OK", true);

        var gate = Undo.AddComponent<WinTaskGate>(root.gameObject);
        gate.actionId = "join_domain";
        gate.submitButton = ok;
        gate.closeOnSuccess = win;
        gate.errorDialog = error;
        gate.errorText = FindLabel(error.transform, "Message");
        gate.missingOptionMessage =
            "Select Domain rather than Workgroup.\n\n" +
            "A workgroup member cannot use domain accounts, receive Group Policy, or reach the " +
            "shares you are about to publish.";
        gate.mustBeOn = new[] { domainRadio };
        gate.mustBeOff = new WinToggle[0];
        gate.fields = new[]
        {
            new WinTaskGate.FieldRequirement
            {
                field = domainField, expected = DomainFQDN, displayName = "domain name"
            }
        };

        CancelButton(body, t, win, w, 260f);
        return win;
    }

    // ==================================================================
    // LO1 - set up user access, steps 12-17
    // ==================================================================

    /// <summary>
    /// File Explorer, showing the contents of C:\ServerData.
    ///
    /// The Users folder is NOT listed until the learner creates it - a WinRevealOnAction keyed to
    /// create_user_folder switches it on. That is what turns the window from a picture of an
    /// explorer into one: what you do leaves a mark, and the Properties button has nothing to act
    /// on until you have done the work.
    /// </summary>
    private static WinWindow BuildFileExplorer(Transform parent, COC3WinTheme t, GameObject error,
                                               WinWindow folderProperties)
    {
        var body = COC3WinWidgets.Window(parent, t, "FileExplorer", "ServerData",
                                         800f, 470f, out RectTransform root);
        var win = Register(root, "FileExplorer", "File Explorer");
        FixCloseGlyph(root);

        float w = body.rect.width;
        float h = body.rect.height;

        // --- address bar ---
        var address = Box(body, "AddressBar", 0f, 0f, w, 28f, t.fieldBackground);
        Label(address.transform, t, "Path", 10f, 0f, w - 20f, 28f, DataRoot);

        // --- toolbar ---
        var newFolder = COC3WinWidgets.Button(body, t, "NewFolder", 0f, 36f, 120f,
                                              t.buttonSize.y, "New folder");
        var nfa = Undo.AddComponent<WinButtonAction>(newFolder.gameObject);
        nfa.action = WinButtonAction.Kind.OpenWindow;
        nfa.windowId = "NewFolderDialog";
        nfa.window = win;

        var properties = COC3WinWidgets.Button(body, t, "Properties", 130f, 36f, 120f,
                                               t.buttonSize.y, "Properties");

        // --- navigation pane, decoration ---
        float paneTop = 76f;
        float paneHeight = h - paneTop - 44f;

        var nav = Box(body, "Nav", 0f, paneTop, 200f, paneHeight, t.fieldBackground);
        Label(nav.transform, t, "Nav1", 10f, 8f, 180f, 22f, "This PC");
        Label(nav.transform, t, "Nav2", 24f, 32f, 170f, 22f, "Local Disk (C:)");
        Label(nav.transform, t, "Nav3", 38f, 56f, 160f, 22f, "ServerData");
        Label(nav.transform, t, "Nav4", 10f, 88f, 180f, 22f, "Network").color = t.disabledText;

        // --- item list ---
        var items = Box(body, "Items", 210f, paneTop, w - 210f, paneHeight, t.fieldBackground);
        float itemW = w - 230f;

        Label(items.transform, t, "Head", 10f, 6f, itemW, 22f, "Name").color = t.disabledText;
        Box(items.transform, "HeadRule", 6f, 30f, itemW, 1f, t.separator).raycastTarget = false;

        RadioRow(items.rectTransform, t, "Row_Public", 6f, 38f, itemW, "Public", "explorer_item");

        var users = RadioRow(items.rectTransform, t, "Row_Users", 6f, 68f, itemW,
                             UsersFolder, "explorer_item");

        // The row itself is ALWAYS present. Only this marker is revealed.
        //
        // It used to be the row that was hidden, which read better and was a deadlock waiting to
        // happen: the Properties gate requires this toggle, so any reason the record failed to
        // stick left the learner unable to select the only folder the step accepts, with nothing
        // on screen to explain why. Nothing a gate depends on is hidden any more - see the note
        // over Reveal().
        var notYet = Label(items.transform, t, "NotYet", 10f, 96f, itemW, 22f,
                           "(" + UsersFolder + " has not been created yet)");
        notYet.color = t.disabledText;

        Label(items.transform, t, "Note", 10f, 124f, itemW, 80f,
              "Select a folder and press Properties to reach its Sharing and Security settings. " +
              "Right-clicking would do the same on a real machine.").color = t.disabledText;

        Reveal(root, "create_user_folder", null, new[] { notYet.gameObject });

        var gate = Gate(root, "Gate_Properties", "open_folder_properties", properties, error);
        gate.mustBeOn = new[] { users };
        gate.openOnSuccess = folderProperties;
        gate.openOnSuccessParent = win;
        gate.missingOptionMessage =
            "Select the folder you want to inspect first.\n\n" +
            "If " + UsersFolder + " is not listed, it has not been created yet - use New folder.";

        CancelButton(body, t, win, w, h - 34f, "Close");
        return win;
    }

    private static WinWindow BuildNewFolderDialog(Transform parent, COC3WinTheme t, GameObject error)
    {
        var body = COC3WinWidgets.Window(parent, t, "NewFolderDialog", "New Folder",
                                         460f, 260f, out RectTransform root);
        var win = Register(root, "NewFolderDialog", "New Folder");
        FixCloseGlyph(root);

        float w = body.rect.width;
        float h = body.rect.height;

        COC3WinWidgets.Field(body, t, "CreateIn", 0f, 0f, 100f, w - 110f,
                             "Create in:", DataRoot, "", true);

        var folderName = COC3WinWidgets.Field(body, t, "FolderName", 0f, 42f, 100f, w - 110f,
                                              "Folder name:", "", "", false);

        Label(body, t, "Note", 0f, 88f, w, 80f,
              "Windows creates the folder inside the location shown above, so type the folder's " +
              "own name here and not the whole path. The result is " + DataRoot + @"\" +
              UsersFolder + " - which is what the job order asks for.").color = t.disabledText;

        var ok = COC3WinWidgets.Button(body, t, "OK", PrimaryX(w, t), h - 34f,
                                       t.buttonSize.x, t.buttonSize.y, "OK", true);

        var gate = Gate(root, "Gate_NewFolder", "create_user_folder", ok, error);
        gate.closeOnSuccess = win;
        gate.fields = new[] { Req(folderName, UsersFolder, "folder name") };
        gate.wrongValueMessage =
            "That is not a valid {field}.\n\n" +
            "A folder name cannot contain a backslash - the box above already says where the " +
            "folder is being created. Check the job order for the name it asks for.";

        CancelButton(body, t, win, w, h - 34f);
        return win;
    }

    /// <summary>
    /// Active Directory Users and Computers.
    ///
    /// TWO GATES, ON CHILD OBJECTS. New User and New Group are separate buttons with separate
    /// conditions, and a WinTaskGate guards exactly one button. Each sits on its own child so the
    /// window root's Inspector stays readable and neither can be mistaken for the other.
    ///
    /// Both require the Users container to be selected, because that is the actual mistake:
    /// creating an account in Computers or in the domain root is the thing a first-timer does, and
    /// nothing in Windows stops them.
    /// </summary>
    private static WinWindow BuildAdUsersComputers(Transform parent, COC3WinTheme t, GameObject error,
                                                   WinWindow newUser, WinWindow newGroup)
    {
        var body = COC3WinWidgets.Window(parent, t, "AdUsersComputers",
                                         "Active Directory Users and Computers",
                                         800f, 460f, out RectTransform root);
        var win = Register(root, "AdUsersComputers", "Active Directory Users and Computers");
        FixCloseGlyph(root);

        float w = body.rect.width;
        float h = body.rect.height;
        float paneHeight = h - 86f;

        // --- tree ---
        var tree = Box(body, "Tree", 0f, 0f, 300f, paneHeight, t.fieldBackground);
        Label(tree.transform, t, "Root", 8f, 6f, 284f, 22f,
              "Active Directory Users and Computers");
        Label(tree.transform, t, "Domain", 20f, 32f, 270f, 22f, DomainFQDN);

        RadioRow(tree.rectTransform, t, "Node_Builtin", 34f, 58f, 256f, "Builtin", "adds_node");
        RadioRow(tree.rectTransform, t, "Node_Computers", 34f, 88f, 256f, "Computers", "adds_node");
        RadioRow(tree.rectTransform, t, "Node_DCs", 34f, 118f, 256f,
                 "Domain Controllers", "adds_node");

        var usersNode = RadioRow(tree.rectTransform, t, "Node_Users", 34f, 148f, 256f,
                                 "Users", "adds_node");

        // --- object list ---
        var list = Box(body, "List", 310f, 0f, w - 310f, paneHeight, t.fieldBackground);
        float listW = w - 330f;

        Label(list.transform, t, "Head", 10f, 6f, listW, 22f, "Name").color = t.disabledText;
        Box(list.transform, "HeadRule", 6f, 30f, listW, 1f, t.separator).raycastTarget = false;

        Label(list.transform, t, "Obj1", 10f, 38f, listW, 22f, "Administrator");
        Label(list.transform, t, "Obj2", 10f, 64f, listW, 22f, "Domain Admins");
        Label(list.transform, t, "Obj3", 10f, 90f, listW, 22f, "Domain Users");

        var userRow = Label(list.transform, t, "Obj_User", 10f, 116f, listW, 22f, SampleUser);
        var groupRow = Label(list.transform, t, "Obj_Group", 10f, 142f, listW, 22f, UserGroup);

        Reveal(root, "create_user_account", new[] { userRow.gameObject }, null);
        Reveal(root, "create_group", new[] { groupRow.gameObject }, null);

        // --- the two New buttons ---
        var newUserBtn = COC3WinWidgets.Button(body, t, "NewUser", 0f, h - 34f, 130f,
                                               t.buttonSize.y, "New > User");
        var newGroupBtn = COC3WinWidgets.Button(body, t, "NewGroup", 140f, h - 34f, 130f,
                                                t.buttonSize.y, "New > Group");

        string wrongContainer =
            "Select the container the new object belongs in.\n\n" +
            "Domain accounts and groups live in the Users container. An object created in " +
            "Computers or in the domain root still works, but nobody looking for it will find it " +
            "where they expect.";

        var userGate = Gate(root, "Gate_NewUser", "open_new_user", newUserBtn, error);
        userGate.mustBeOn = new[] { usersNode };
        userGate.openOnSuccess = newUser;
        userGate.openOnSuccessParent = win;
        userGate.missingOptionMessage = wrongContainer;

        var groupGate = Gate(root, "Gate_NewGroup", "open_new_group", newGroupBtn, error);
        groupGate.mustBeOn = new[] { usersNode };
        groupGate.openOnSuccess = newGroup;
        groupGate.openOnSuccessParent = win;
        groupGate.missingOptionMessage = wrongContainer;

        CancelButton(body, t, win, w, h - 34f, "Close");
        return win;
    }

    private static WinWindow BuildNewUserDialog(Transform parent, COC3WinTheme t, GameObject error)
    {
        var body = COC3WinWidgets.Window(parent, t, "NewUserDialog", "New Object - User",
                                         560f, 360f, out RectTransform root);
        var win = Register(root, "NewUserDialog", "New Object - User");
        FixCloseGlyph(root);

        float w = body.rect.width;
        float h = body.rect.height;

        Label(body, t, "CreateIn", 0f, 0f, w, 22f, "Create in:  " + DomainFQDN + "/Users");
        Box(body, "Rule", 0f, 28f, w, 1f, t.separator).raycastTarget = false;

        COC3WinWidgets.Field(body, t, "FirstName", 0f, 42f, 150f, 180f, "First name:", "", "", false);
        COC3WinWidgets.Field(body, t, "LastName", 0f, 80f, 150f, 180f, "Last name:", "", "", false);

        float logonW = w - 320f;
        var logon = COC3WinWidgets.Field(body, t, "Logon", 0f, 128f, 150f, logonW,
                                         "User logon name:", "", "", false);

        Label(body, t, "Suffix", 158f + logonW, 128f, 150f, 26f, "@" + DomainFQDN)
            .color = t.disabledText;

        Label(body, t, "Note", 0f, 172f, w, 80f,
              "The logon name is what the account signs in with, and it is the one field here that " +
              "cannot be changed casually afterwards. The job order names it - use exactly that.")
            .color = t.disabledText;

        var ok = COC3WinWidgets.Button(body, t, "OK", PrimaryX(w, t), h - 34f,
                                       t.buttonSize.x, t.buttonSize.y, "OK", true);

        var gate = Gate(root, "Gate_NewUser", "create_user_account", ok, error);
        gate.closeOnSuccess = win;
        gate.fields = new[] { Req(logon, SampleUser, "user logon name") };

        CancelButton(body, t, win, w, h - 34f);
        return win;
    }

    private static WinWindow BuildNewGroupDialog(Transform parent, COC3WinTheme t, GameObject error,
                                                 WinWindow groupProperties)
    {
        var body = COC3WinWidgets.Window(parent, t, "NewGroupDialog", "New Object - Group",
                                         540f, 440f, out RectTransform root);
        var win = Register(root, "NewGroupDialog", "New Object - Group");
        FixCloseGlyph(root);

        float w = body.rect.width;
        float h = body.rect.height;

        Label(body, t, "CreateIn", 0f, 0f, w, 22f, "Create in:  " + DomainFQDN + "/Users");
        Box(body, "Rule", 0f, 28f, w, 1f, t.separator).raycastTarget = false;

        var groupName = COC3WinWidgets.Field(body, t, "GroupName", 0f, 42f, 140f, w - 150f,
                                             "Group name:", "", "", false);

        float half = w / 2f - 8f;

        // Nothing is preselected. Windows preselects Global and Security, which would hand the
        // learner both answers - and knowing WHY those two is the point of the step.
        var scope = COC3WinWidgets.GroupBox(body, t, "Scope", 0f, 90f, half, 140f, "Group scope");
        RadioRow(scope, t, "Radio_DomainLocal", 8f, 4f, half - 20f, "Domain local", "group_scope");
        var global = RadioRow(scope, t, "Radio_Global", 8f, 34f, half - 20f, "Global", "group_scope");
        RadioRow(scope, t, "Radio_Universal", 8f, 64f, half - 20f, "Universal", "group_scope");

        var kind = COC3WinWidgets.GroupBox(body, t, "Kind", half + 16f, 90f, half, 140f, "Group type");
        var security = RadioRow(kind, t, "Radio_Security", 8f, 4f, half - 20f, "Security", "group_type");
        RadioRow(kind, t, "Radio_Distribution", 8f, 34f, half - 20f, "Distribution", "group_type");

        Label(body, t, "Note", 0f, 244f, w, 90f,
              "Only a SECURITY group can be given permissions - a distribution group is a mailing " +
              "list and nothing else. GLOBAL is the scope that can hold accounts from this domain " +
              "and be used anywhere in the forest.").color = t.disabledText;

        var ok = COC3WinWidgets.Button(body, t, "OK", PrimaryX(w, t), h - 34f,
                                       t.buttonSize.x, t.buttonSize.y, "OK", true);

        var gate = Gate(root, "Gate_NewGroup", "create_group", ok, error);
        gate.closeOnSuccess = win;
        gate.openOnSuccess = groupProperties;
        gate.fields = new[] { Req(groupName, UserGroup, "group name") };
        gate.mustBeOn = new[] { global, security };
        gate.missingOptionMessage =
            "Choose the group's scope and type.\n\n" +
            "Permissions can only be granted to a SECURITY group, and a GLOBAL group is the one " +
            "that holds accounts from this domain.";

        CancelButton(body, t, win, w, h - 34f);
        return win;
    }

    /// <summary>
    /// STUDENTS Properties, opened straight after the group is created.
    ///
    /// It opens on GENERAL, not on Members. Finding the Members tab is the work: LO1.2 is about
    /// granting access to a group rather than to a person, and the tab is where that actually
    /// happens.
    ///
    /// The window stays open when the member is added, unlike every other dialog here, so the
    /// learner sees the name land in the list. Closing on success would take the evidence away in
    /// the same frame it appeared.
    /// </summary>
    private static WinWindow BuildGroupProperties(Transform parent, COC3WinTheme t, GameObject error)
    {
        var body = COC3WinWidgets.Window(parent, t, "GroupProperties", UserGroup + " Properties",
                                         540f, 440f, out RectTransform root);
        var win = Register(root, "GroupProperties", UserGroup + " Properties");
        FixCloseGlyph(root);

        float w = body.rect.width;
        float h = body.rect.height;

        RectTransform[] panels;
        RealTabs(body, t, root, new[] { "General", "Members", "Member Of" }, out panels);

        // --- General ---
        COC3WinWidgets.Field(panels[0], t, "GroupName", 0f, 8f, 150f, w - 160f,
                             "Group name:", UserGroup, "", true);
        COC3WinWidgets.Field(panels[0], t, "Description", 0f, 48f, 150f, w - 160f,
                             "Description:", "", "", false);
        Label(panels[0], t, "Scope", 0f, 92f, w, 22f, "Group scope:   Global");
        Label(panels[0], t, "Kind", 0f, 118f, w, 22f, "Group type:     Security");

        // --- Members ---
        Label(panels[1], t, "Head", 0f, 0f, w, 22f, "Members:");

        var listBox = Box(panels[1], "MemberList", 0f, 26f, w, 120f, t.fieldBackground);

        var empty = Label(listBox.transform, t, "Empty", 10f, 10f, w - 20f, 22f,
                          "There are no items to show in this view.");
        empty.color = t.disabledText;

        var member = Label(listBox.transform, t, "Member", 10f, 10f, w - 20f, 22f,
                           SampleUser + "        " + DomainFQDN + "/Users");

        var objectName = COC3WinWidgets.Field(panels[1], t, "ObjectName", 0f, 158f, 200f, w - 210f,
                                              "Enter the object name:", "", "", false);

        var add = COC3WinWidgets.Button(panels[1], t, "Add", 0f, 198f, 100f, t.buttonSize.y, "Add");

        Label(panels[1], t, "Note", 0f, 230f, w, 76f,
              "Access is granted to the group, never to the person. When " + SampleUser +
              " leaves, you remove them from " + UserGroup + " once and every permission the " +
              "group has goes with them.").color = t.disabledText;

        Reveal(root, "add_group_member", new[] { member.gameObject }, new[] { empty.gameObject });

        // --- Member Of ---
        Label(panels[2], t, "MemberOf", 0f, 8f, w, 22f, "Domain Users").color = t.disabledText;

        var gate = Gate(root, "Gate_AddMember", "add_group_member", add, error);
        gate.fields = new[] { Req(objectName, SampleUser, "object name") };
        gate.wrongValueMessage =
            "An object named that cannot be found in " + DomainFQDN + ".\n\n" +
            "Check the {field} against the account you created.";

        CancelButton(body, t, win, w, h - 34f, "OK");
        return win;
    }

    private static WinWindow BuildGpoEditor(Transform parent, COC3WinTheme t, GameObject error,
                                            WinWindow minPassword)
    {
        var body = COC3WinWidgets.Window(parent, t, "GpoEditor",
                                         "Group Policy Management Editor",
                                         840f, 480f, out RectTransform root);
        var win = Register(root, "GpoEditor", "Group Policy Management Editor");
        FixCloseGlyph(root);

        float w = body.rect.width;
        float h = body.rect.height;
        float paneHeight = h - 54f;

        // --- tree ---
        var tree = Box(body, "Tree", 0f, 0f, 340f, paneHeight, t.fieldBackground);
        Label(tree.transform, t, "Root", 8f, 6f, 324f, 40f,
              "Default Domain Policy [" + ServerName + "." + DomainFQDN + "]").fontSize = 13f;

        Label(tree.transform, t, "N1", 16f, 50f, 320f, 22f, "Computer Configuration");
        Label(tree.transform, t, "N2", 30f, 74f, 306f, 22f, "Policies");
        Label(tree.transform, t, "N3", 44f, 98f, 292f, 22f, "Windows Settings");
        Label(tree.transform, t, "N4", 58f, 122f, 278f, 22f, "Security Settings");
        Label(tree.transform, t, "N5", 72f, 146f, 264f, 22f, "Account Policies");

        var pwdNode = RadioRow(tree.rectTransform, t, "Node_Password", 86f, 172f, 244f,
                               "Password Policy", "gpo_node");
        RadioRow(tree.rectTransform, t, "Node_Lockout", 86f, 202f, 244f,
                 "Account Lockout Policy", "gpo_node");
        RadioRow(tree.rectTransform, t, "Node_Kerberos", 86f, 232f, 244f,
                 "Kerberos Policy", "gpo_node");

        // --- policy list ---
        var list = Box(body, "Policies", 350f, 0f, w - 350f, paneHeight, t.fieldBackground);
        float listW = w - 370f;
        float settingX = listW - 170f;

        Label(list.transform, t, "H1", 10f, 6f, settingX - 10f, 22f, "Policy").color = t.disabledText;
        Label(list.transform, t, "H2", settingX, 6f, 170f, 22f, "Policy Setting").color = t.disabledText;
        Box(list.transform, "HeadRule", 6f, 30f, listW, 1f, t.separator).raycastTarget = false;

        Label(list.transform, t, "P1", 10f, 40f, settingX - 10f, 22f, "Enforce password history");
        Label(list.transform, t, "V1", settingX, 40f, 170f, 22f, "24 passwords remembered");

        Label(list.transform, t, "P2", 10f, 70f, settingX - 10f, 22f, "Maximum password age");
        Label(list.transform, t, "V2", settingX, 70f, 170f, 22f, "42 days");

        var minRow = FlatButton(list.rectTransform, "Row_MinLength", 6f, 98f, listW, 26f, "");
        Label(minRow.transform, t, "Label", 4f, 0f, settingX - 10f, 26f, "Minimum password length");

        var notSet = Label(minRow.transform, t, "Unset", settingX - 6f, 0f, 170f, 26f,
                           "0 characters");
        var isSet = Label(minRow.transform, t, "Set", settingX - 6f, 0f, 170f, 26f,
                          MinPassword + " characters");

        Label(list.transform, t, "P4", 10f, 130f, settingX - 10f, 22f,
              "Password must meet complexity requirements");
        Label(list.transform, t, "V4", settingX, 130f, 170f, 22f, "Enabled");

        Label(list.transform, t, "Note", 10f, 170f, listW, 70f,
              "A setting of 0 means no minimum at all - a one-character password would be " +
              "accepted. Open the policy to define it.").color = t.disabledText;

        Reveal(root, "set_password_policy", new[] { isSet.gameObject }, new[] { notSet.gameObject });

        var gate = Gate(root, "Gate_OpenPolicy", "open_password_policy", minRow, error);
        gate.mustBeOn = new[] { pwdNode };
        gate.openOnSuccess = minPassword;
        gate.openOnSuccessParent = win;
        gate.missingOptionMessage =
            "Select the policy branch in the tree first.\n\n" +
            "Password length lives under Password Policy. Account Lockout Policy governs what " +
            "happens after failed sign-ins, which is a different control.";

        CancelButton(body, t, win, w, h - 34f, "Close");
        return win;
    }

    private static WinWindow BuildMinPasswordDialog(Transform parent, COC3WinTheme t, GameObject error)
    {
        var body = COC3WinWidgets.Window(parent, t, "MinPasswordDialog",
                                         "Minimum password length Properties",
                                         480f, 360f, out RectTransform root);
        var win = Register(root, "MinPasswordDialog", "Minimum password length Properties");
        FixCloseGlyph(root);

        float w = body.rect.width;
        float h = body.rect.height;

        Tabs(body, t, new[] { "Security Policy Setting", "Explain" }, 0);

        Label(body, t, "Title", 0f, 46f, w, 24f, "Minimum password length").fontStyle = FontStyles.Bold;

        var define = CheckRow(body, t, "Define", 0f, 80f, w, "Define this policy setting");

        var length = COC3WinWidgets.Field(body, t, "Length", 20f, 120f, 190f, 70f,
                                          "Password must be at least", "", "", false);

        Label(body, t, "Unit", 292f, 120f, 120f, 26f, "characters");

        Label(body, t, "Note", 0f, 166f, w, 100f,
              "A policy that has not been DEFINED is not applied at all - the tick box is what " +
              "turns it on, and leaving it clear is the quiet way this step goes wrong. " +
              MinPassword + " characters is the length the enterprise standard in the job order " +
              "asks for.").color = t.disabledText;

        var ok = COC3WinWidgets.Button(body, t, "OK", PrimaryX(w, t), h - 34f,
                                       t.buttonSize.x, t.buttonSize.y, "OK", true);

        var gate = Gate(root, "Gate_MinPassword", "set_password_policy", ok, error);
        gate.closeOnSuccess = win;
        gate.mustBeOn = new[] { define };
        gate.fields = new[] { Req(length, MinPassword, "number of characters") };
        gate.missingOptionMessage =
            "Tick 'Define this policy setting'.\n\n" +
            "Until it is ticked the value in the box is ignored and the policy stays undefined.";

        CancelButton(body, t, win, w, h - 34f);
        return win;
    }

    /// <summary>
    /// The folder's property sheet - the one window in the module with tabs that really work.
    ///
    /// Steps 16 and 17 are both performed here, on DIFFERENT tabs, and telling them apart is the
    /// skill: Sharing publishes the folder on the network, Security decides who may do what once
    /// they reach it. A learner who sets one and not the other has a share nobody can use, or a
    /// folder nobody can find - the two classic outcomes.
    /// </summary>
    private static WinWindow BuildFolderProperties(Transform parent, COC3WinTheme t)
    {
        var body = COC3WinWidgets.Window(parent, t, "FolderProperties", UsersFolder + " Properties",
                                         540f, 460f, out RectTransform root);
        var win = Register(root, "FolderProperties", UsersFolder + " Properties");
        FixCloseGlyph(root);

        float w = body.rect.width;
        float h = body.rect.height;

        RectTransform[] panels;
        RealTabs(body, t, root,
                 new[] { "General", "Sharing", "Security", "Previous Versions" }, out panels);

        // --- General ---
        Label(panels[0], t, "G1", 0f, 8f, w, 22f, "Type:        File folder");
        Label(panels[0], t, "G2", 0f, 34f, w, 22f, "Location:  " + DataRoot);
        Label(panels[0], t, "G3", 0f, 60f, w, 22f, "Size:         0 bytes");
        Label(panels[0], t, "G4", 0f, 86f, w, 22f, "Contains:  0 Files, 0 Folders");

        // --- Sharing ---
        Label(panels[1], t, "S1", 0f, 8f, w, 22f, "Network File and Folder Sharing")
            .fontStyle = FontStyles.Bold;
        Label(panels[1], t, "S2", 0f, 36f, w, 22f, UsersFolder);

        var notShared = Label(panels[1], t, "NotShared", 16f, 60f, w - 16f, 22f, "Not Shared");
        notShared.color = t.disabledText;

        var sharedPath = Label(panels[1], t, "SharedPath", 16f, 60f, w - 16f, 22f,
                               "Network Path:  " + @"\\" + ServerName + @"\" + ShareName);

        Label(panels[1], t, "S3", 0f, 100f, w, 22f, "Advanced Sharing").fontStyle = FontStyles.Bold;
        Label(panels[1], t, "S4", 0f, 126f, w, 44f,
              "Set custom permissions, create multiple shares, and set other advanced sharing " +
              "options.").color = t.disabledText;

        var advanced = COC3WinWidgets.Button(panels[1], t, "AdvancedSharing", 0f, 178f, 180f,
                                             t.buttonSize.y, "Advanced Sharing...");
        var aa = Undo.AddComponent<WinButtonAction>(advanced.gameObject);
        aa.action = WinButtonAction.Kind.OpenWindow;
        aa.windowId = "AdvancedSharing";
        aa.window = win;

        // --- Security ---
        Label(panels[2], t, "SecPath", 0f, 8f, w, 22f,
              "Object name:  " + DataRoot + @"\" + UsersFolder).color = t.disabledText;
        Label(panels[2], t, "SecHead", 0f, 34f, w, 22f, "Group or user names:");

        var principals = Box(panels[2], "Principals", 0f, 60f, w, 116f, t.fieldBackground);
        Label(principals.transform, t, "P1", 10f, 8f, w - 20f, 22f, "Authenticated Users");
        Label(principals.transform, t, "P2", 10f, 34f, w - 20f, 22f, "SYSTEM");
        Label(principals.transform, t, "P3", 10f, 60f, w - 20f, 22f,
              "Administrators (" + ServerName + @"\Administrators)");

        var studentsRow = Label(principals.transform, t, "P4", 10f, 86f, w - 20f, 22f,
                                UserGroup + " (CSSNCII" + @"\" + UserGroup + ")");

        Label(panels[2], t, "SecNote", 0f, 186f, w, 66f,
              "The list shows who has an entry on this folder. Press Edit to change what any of " +
              "them is allowed to do.").color = t.disabledText;

        var edit = COC3WinWidgets.Button(panels[2], t, "Edit", w - 110f, 256f, 110f,
                                         t.buttonSize.y, "Edit...");
        var ea = Undo.AddComponent<WinButtonAction>(edit.gameObject);
        ea.action = WinButtonAction.Kind.OpenWindow;
        ea.windowId = "SecurityPermissions";
        ea.window = win;

        // --- Previous Versions ---
        Label(panels[3], t, "PV", 0f, 8f, w, 44f,
              "There are no previous versions available.").color = t.disabledText;

        Reveal(root, "share_folder",
               new[] { sharedPath.gameObject }, new[] { notShared.gameObject });
        Reveal(root, "set_folder_permissions", new[] { studentsRow.gameObject }, null);

        CancelButton(body, t, win, w, h - 34f, "OK");
        return win;
    }

    private static WinWindow BuildAdvancedSharing(Transform parent, COC3WinTheme t, GameObject error)
    {
        var body = COC3WinWidgets.Window(parent, t, "AdvancedSharing", "Advanced Sharing",
                                         480f, 340f, out RectTransform root);
        var win = Register(root, "AdvancedSharing", "Advanced Sharing");
        FixCloseGlyph(root);

        float w = body.rect.width;
        float h = body.rect.height;

        var share = CheckRow(body, t, "ShareThisFolder", 0f, 4f, w, "Share this folder");

        var shareName = COC3WinWidgets.Field(body, t, "ShareName", 20f, 44f, 110f, w - 150f,
                                             "Share name:", "", "", false);

        Label(body, t, "Limit", 20f, 84f, w - 20f, 22f,
              "Limit the number of simultaneous users to:  16777216").color = t.disabledText;

        Label(body, t, "Note", 0f, 120f, w, 110f,
              "The share name is what clients type, and it does not have to match the folder name " +
              "on disk - the folder is " + UsersFolder + " and the share is " + ShareName + ". " +
              "Sharing publishes the folder on the network; it does not decide who may open it. " +
              "That is the Security tab.").color = t.disabledText;

        var ok = COC3WinWidgets.Button(body, t, "OK", PrimaryX(w, t), h - 34f,
                                       t.buttonSize.x, t.buttonSize.y, "OK", true);

        var gate = Gate(root, "Gate_Share", "share_folder", ok, error);
        gate.closeOnSuccess = win;
        gate.mustBeOn = new[] { share };
        gate.fields = new[] { Req(shareName, ShareName, "share name") };
        gate.missingOptionMessage =
            "Tick 'Share this folder'.\n\n" +
            "Typing a share name does nothing on its own - the box is what publishes the folder.";

        CancelButton(body, t, win, w, h - 34f);
        return win;
    }

    /// <summary>
    /// Permissions for Users - the Security tab's Edit dialog, and the one place in the module
    /// where the wrong right answer is available.
    ///
    /// Full control is easier and it works. It also lets the group change the folder's own
    /// permissions and take ownership of it, which is why the standard says Modify. The gate
    /// requires Modify AND requires Full control to be clear, so the shortcut is caught rather
    /// than quietly passed.
    /// </summary>
    private static WinWindow BuildSecurityPermissions(Transform parent, COC3WinTheme t,
                                                      GameObject error)
    {
        var body = COC3WinWidgets.Window(parent, t, "SecurityPermissions",
                                         "Permissions for " + UsersFolder,
                                         600f, 560f, out RectTransform root);
        var win = Register(root, "SecurityPermissions", "Permissions for " + UsersFolder);
        FixCloseGlyph(root);

        float w = body.rect.width;
        float h = body.rect.height;

        Label(body, t, "Head", 0f, 0f, w, 22f, "Group or user names:");

        var listBox = Box(body, "Principals", 0f, 26f, w, 122f, t.fieldBackground);

        RadioRow(listBox.rectTransform, t, "Prin_Auth", 6f, 4f, w - 12f,
                 "Authenticated Users", "perm_principal");
        RadioRow(listBox.rectTransform, t, "Prin_System", 6f, 34f, w - 12f,
                 "SYSTEM", "perm_principal");
        RadioRow(listBox.rectTransform, t, "Prin_Admins", 6f, 64f, w - 12f,
                 "Administrators", "perm_principal");

        // ALWAYS VISIBLE. This row was hidden until create_group had been accepted, which is why
        // step 17 could present a list with no STUDENTS in it and an error message naming a group
        // that was not there to select. The gate below requires this toggle, so it must never
        // depend on anything having been recorded.
        //
        // Nothing is lost: the step order already guarantees the group exists by the time anyone
        // reaches step 17.
        var students = RadioRow(listBox.rectTransform, t, "Prin_Students", 6f, 94f, w - 12f,
                                UserGroup + " (CSSNCII" + @"\" + UserGroup + ")", "perm_principal");

        Label(body, t, "GridHead", 0f, 160f, 260f, 22f, "Permissions for " + UserGroup);

        float allowX = w - 170f;
        float denyX = w - 90f;

        Label(body, t, "ColAllow", allowX, 160f, 60f, 22f, "Allow", TextAlignmentOptions.Center)
            .color = t.disabledText;
        Label(body, t, "ColDeny", denyX, 160f, 60f, 22f, "Deny", TextAlignmentOptions.Center)
            .color = t.disabledText;

        Box(body, "GridRule", 0f, 186f, w, 1f, t.separator).raycastTarget = false;

        string[] rights =
        {
            "Full control", "Modify", "Read & execute",
            "List folder contents", "Read", "Write"
        };

        WinToggle fullControl = null;
        WinToggle modify = null;

        for (int i = 0; i < rights.Length; i++)
        {
            float y = 196f + i * 32f;

            Label(body, t, "Right_" + i, 10f, y, 280f, 26f, rights[i]);

            var allow = CheckRow(body, t, "Allow_" + i, allowX + 20f, y - 1f, 30f, "");

            // Deny is drawn but dead. Every task in this module is an Allow, and a live Deny column
            // would let a learner produce a folder that looks configured and denies everyone -
            // a failure mode with no lesson attached to it.
            COC3WinWidgets.BorderedBox(body, "Deny_" + i, denyX + 20f, y + 5f, 16f, 16f,
                                       t.fieldBackground, t.fieldBorder);

            if (i == 0) fullControl = allow;
            if (i == 1) modify = allow;
        }

        Label(body, t, "Note", 0f, 196f + rights.Length * 32f + 8f, w, 66f,
              "Select the group first, then tick what it is allowed to do. Full control also lets " +
              "the group change these permissions and take ownership - which is why the standard " +
              "asks for Modify.").color = t.disabledText;

        var ok = COC3WinWidgets.Button(body, t, "OK", PrimaryX(w, t), h - 34f,
                                       t.buttonSize.x, t.buttonSize.y, "OK", true);

        var gate = Gate(root, "Gate_Permissions", "set_folder_permissions", ok, error);
        gate.closeOnSuccess = win;
        gate.mustBeOn = new[] { students, modify };
        gate.mustBeOff = new[] { fullControl };
        gate.missingOptionMessage =
            "Select " + UserGroup + " in the list and grant it Modify.\n\n" +
            "Modify covers reading, writing and deleting the group's own work. Full control adds " +
            "the right to change these permissions and to take ownership, which no user group " +
            "should have on a shared folder.";

        CancelButton(body, t, win, w, h - 34f);
        return win;
    }

    // ==================================================================
    // Small drawing helpers
    // ==================================================================

    private static WinWindow Register(RectTransform root, string id, string title)
    {
        var w = Undo.AddComponent<WinWindow>(root.gameObject);
        w.windowId = id;
        w.title = title;
        return w;
    }

    /// <summary>
    /// COC3WinWidgets.Window draws its close box with "✕", which LiberationSans does not have -
    /// it renders as a hollow square and logs a warning every time the text rebuilds. A plain
    /// capital X exists in every font and reads the same at this size.
    /// </summary>
    private static void FixCloseGlyph(RectTransform windowRoot)
    {
        foreach (var label in windowRoot.GetComponentsInChildren<TextMeshProUGUI>(true))
            if (label.name == "Close") label.text = "X";
    }

    /// <summary>
    /// Finds a label by name.
    ///
    /// NOT GetComponentInChildren&lt;TMP_Text&gt;: the first text in any window built by
    /// COC3WinWidgets.Window is the TITLE BAR caption, so grabbing "the first one" wrote every
    /// error message into the title bar and left the body empty.
    /// </summary>
    private static TextMeshProUGUI FindLabel(Transform root, string labelName)
    {
        foreach (var label in root.GetComponentsInChildren<TextMeshProUGUI>(true))
            if (label.name == labelName) return label;

        Debug.LogWarning($"[COC3DesktopBuilder] No label named '{labelName}' under '{root.name}'.");
        return null;
    }

    /// <summary>
    /// A gate on its own child object rather than on the window root.
    ///
    /// A WinTaskGate guards exactly ONE button, and AD Users and Computers has two that need
    /// guarding. Two gates on one GameObject compile and run, but the Inspector shows them as
    /// identical stacked components with no way to tell which is which. A named child says what it
    /// guards from the Hierarchy alone.
    /// </summary>
    private static WinTaskGate Gate(RectTransform windowRoot, string name, string actionId,
                                    Button submit, GameObject error)
    {
        var holder = Rect(windowRoot, name, 0f, 0f, 0f, 0f);

        var gate = Undo.AddComponent<WinTaskGate>(holder.gameObject);
        gate.actionId = actionId;
        gate.submitButton = submit;
        gate.errorDialog = error;
        gate.errorText = FindLabel(error.transform, "Message");
        gate.fields = new WinTaskGate.FieldRequirement[0];
        gate.mustBeOn = new WinToggle[0];
        gate.mustBeOff = new WinToggle[0];
        return gate;
    }

    private static WinTaskGate.FieldRequirement Req(TMP_InputField field, string expected,
                                                    string displayName)
    {
        return new WinTaskGate.FieldRequirement
        {
            field = field,
            expected = expected,
            displayName = displayName
        };
    }

    /// <summary>
    /// Hides something until an action has been accepted, and shows something else in the meantime.
    ///
    /// The component goes on the WINDOW ROOT, never on the row it hides - a switched-off object
    /// runs no code and so could never switch itself back on. The rows are switched off here, at
    /// build time, and WinRevealOnAction turns them on when it sees the action has succeeded.
    ///
    /// THE RULE: NEVER HIDE ANYTHING A GATE REFERENCES.
    /// A WinTaskGate's mustBeOn toggle that is switched off screen cannot be selected, so the step
    /// becomes unpassable and the error message names a control the learner cannot see. That
    /// happened to the STUDENTS row on the permissions list. Reveals are for rows that report
    /// what has been done - a name appearing in a list, a value changing, "Not Shared" going away.
    /// Anything the learner has to click stays on screen from the start.
    /// </summary>
    private static void Reveal(RectTransform windowRoot, string actionId,
                               GameObject[] show, GameObject[] hide)
    {
        var reveal = Undo.AddComponent<WinRevealOnAction>(windowRoot.gameObject);
        reveal.actionId = actionId;
        reveal.show = show ?? new GameObject[0];
        reveal.hide = hide ?? new GameObject[0];

        // The starting state, so the window looks right before anything has been done to it.
        if (show != null)
            foreach (var go in show)
                if (go != null) go.SetActive(false);
    }

    /// <summary>
    /// Tabs that actually switch pages, unlike the painted Tabs() below.
    ///
    /// Used only where finding the tab IS the lesson - Sharing versus Security on a folder, and
    /// the Members page of a group. Everywhere else a live tab would be three more things to click
    /// and nothing to learn.
    /// </summary>
    private static void RealTabs(RectTransform body, COC3WinTheme t, RectTransform windowRoot,
                                 string[] names, out RectTransform[] panels)
    {
        float w = body.rect.width;
        float h = body.rect.height;

        var entries = new WinTabs.Tab[names.Length];
        panels = new RectTransform[names.Length];

        float x = 0f;

        for (int i = 0; i < names.Length; i++)
        {
            float tabW = 30f + names[i].Length * 7f;

            var face = Box(body, "Tab_" + i, x, 0f, tabW, 30f, t.buttonFace);
            var btn = Undo.AddComponent<Button>(face.gameObject);
            btn.targetGraphic = face;

            // The "selected" look is an Image over the face, switched by WinToggle - the same
            // indicator mechanism as a radio dot, and for the same reason: no glyph to be missing
            // from the font.
            var selected = Box(face.transform, "Selected", 0f, 0f, tabW, 30f, t.windowBackground);
            selected.raycastTarget = false;
            selected.enabled = i == 0;

            Label(face.transform, t, "Label", 0f, 0f, tabW, 30f, names[i],
                  TextAlignmentOptions.Center);

            var toggle = Undo.AddComponent<WinToggle>(face.gameObject);
            toggle.groupId = "sheet_tab";
            toggle.indicator = selected;
            toggle.isOn = i == 0;

            panels[i] = Rect(body, "Panel_" + names[i].Replace(" ", ""), 0f, 44f, w, h - 88f);

            entries[i] = new WinTabs.Tab { tab = toggle, panel = panels[i].gameObject };

            x += tabW + 2f;
        }

        Box(body, "TabUnderline", 0f, 30f, w, 1f, t.separator).raycastTarget = false;

        var tabs = Undo.AddComponent<WinTabs>(windowRoot.gameObject);
        tabs.tabs = entries;
        tabs.defaultTab = 0;
    }

    private static void Tabs(RectTransform parent, COC3WinTheme t, string[] names, int selected)
    {
        float x = 0f;

        for (int i = 0; i < names.Length; i++)
        {
            float tabW = 34f + names[i].Length * 7f;

            var tab = Box(parent, "Tab_" + i, x, 0f, tabW, 30f,
                          i == selected ? t.windowBackground : t.buttonFace);
            tab.raycastTarget = false;

            Label(tab.transform, t, "Label", 0f, 0f, tabW, 30f, names[i],
                  TextAlignmentOptions.Center);

            x += tabW + 2f;
        }

        Box(parent, "TabUnderline", 0f, 30f, parent.rect.width, 1f, t.separator).raycastTarget = false;
    }

    /// <summary>
    /// Where the primary button goes so that it sits immediately LEFT of Cancel.
    ///
    /// This was written out by hand as "w - 190", which is only right while the theme's button is
    /// exactly 90 wide - and four wizards were built with "w - 100" instead. That puts the primary
    /// button almost entirely UNDERNEATH Cancel, and since CancelButton is called afterwards it
    /// draws on top: all that showed of Install or Next was a ten-pixel sliver down its left edge.
    ///
    /// Deriving the position from the theme removes both the typo and the assumption.
    /// </summary>
    private static float PrimaryX(float w, COC3WinTheme t)
    {
        return w - 2f * t.buttonSize.x - 10f;
    }

    private static void CancelButton(RectTransform body, COC3WinTheme t, WinWindow win,
                                     float w, float y, string label = "Cancel")
    {
        var cancel = COC3WinWidgets.Button(body, t, label + "Button",
                                           w - t.buttonSize.x, y, t.buttonSize.x, t.buttonSize.y,
                                           label);

        var a = Undo.AddComponent<WinButtonAction>(cancel.gameObject);
        a.action = WinButtonAction.Kind.CloseWindow;
        a.window = win;
    }

    private static Button FlatButton(RectTransform parent, string name,
                                     float x, float y, float w, float h, string label)
    {
        var rt = Rect(parent, name, x, y, w, h);

        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 1f / 255f);

        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        return btn;
    }

    private static Image Box(Transform parent, string name,
                             float x, float y, float w, float h, Color color)
    {
        var rt = Rect(parent, name, x, y, w, h);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        return img;
    }

    private static TextMeshProUGUI Label(Transform parent, COC3WinTheme t, string name,
                                         float x, float y, float w, float h, string text,
                                         TextAlignmentOptions align = TextAlignmentOptions.Left)
    {
        var rt = Rect(parent, name, x, y, w, h);

        var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = t.fontSize;
        label.color = t.text;
        label.alignment = align;
        label.raycastTarget = false;

        if (t.uiFont != null) label.font = t.uiFont;

        // Escape parsing off everywhere, for the same reason it had to go off on the input
        // fields: a path written into a label would otherwise lose a backslash.
        label.parseCtrlCharacters = false;

        return label;
    }

    /// <summary>Top-left placement in the parent's own space, the model the whole file uses.</summary>
    private static RectTransform Rect(Transform parent, string name,
                                      float x, float y, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);

        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
        rt.localScale = Vector3.one;
        return rt;
    }

    /// <summary>
    /// A fixed DW x DH area centred in its parent and scaled to fit by UniformScaleToFit. Children
    /// use the same top-left pixel model as everywhere else in this file, so all the layout maths
    /// above stays in design pixels and never has to know the real canvas size.
    /// </summary>
    private static RectTransform Design(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);

        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(DW, DH);
        rt.localScale = Vector3.one;

        var fitter = Undo.AddComponent<UniformScaleToFit>(go);
        fitter.designSize = new Vector2(DW, DH);

        return rt;
    }

    private static RectTransform Stretch(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, UndoLabel);

        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        return rt;
    }

    private static Color Hex(int rgb)
    {
        return new Color(((rgb >> 16) & 0xFF) / 255f,
                         ((rgb >> 8) & 0xFF) / 255f,
                         (rgb & 0xFF) / 255f, 1f);
    }
}
#endif