#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// THE WINDOW DEFINITIONS. One entry per step whose panel should show a recreated
/// Windows dialog instead of a screenshot.
///
/// PHASE 1 covers three windows across six steps:
///   Computer Name/Domain Changes  ->  steps 2 and 11
///   IPv4 Properties               ->  steps 3 and 4
///   Run                           ->  step 19
///
/// Steps with no entry here are left exactly as they are, so the activity stays playable
/// while the remaining phases are built.
///
/// WHY EACH DIALOG IS A SMALL METHOD, NOT A DATA TREE
/// A declarative tree of control descriptors reads nicely for the five simple dialogs and
/// then fights you for the rest - Event Viewer and Group Policy need real layout decisions
/// that a descriptor language would only end up re-inventing. A few lines of positioning
/// code per window is shorter, easier to read, and can express anything.
///
/// Every builder receives:
///   body  - the client area, coordinates measured DOWN from its top-left
///   t     - the theme
///   bind  - fill in the controls that should be graded (see DialogBindings)
///   w, h  - the body's size in window pixels
/// </summary>
public static class COC3DialogBlueprint
{
    /// <summary>
    /// The controls a dialog hands back to be wired into ScreenshotStep. Whichever fields a
    /// dialog fills in become the graded ones - the learner types into the dialog's own
    /// boxes, and clicks the dialog's own checkbox, rather than a separate control floating
    /// on top of a picture.
    /// </summary>
    public class DialogBindings
    {
        public TMP_InputField answer;     // TextInput steps
        public TMP_InputField ip, mask, gateway;   // NetworkConfigInput steps
        public Button target;             // ButtonInteraction steps
        public TMP_Text console;          // PingTest steps - handed to PingSimulation
    }

    /// <summary>Which window frame a dialog is drawn in.</summary>
    public enum Frame { Standard, Wizard, Bare }

    public delegate void BuildFn(RectTransform body, COC3WinTheme t, DialogBindings bind, float w, float h);

    public class DialogDef
    {
        public string stepName;           // must match the panel / step asset name
        public Frame frame = Frame.Standard;
        public string title;              // window title bar
        public string heading;            // wizard heading strip
        public string[] stages;           // wizard stage list
        public int activeStage;
        public Vector2 size = new Vector2(460f, 430f);
        public string[] buttons = { "OK", "Cancel" };
        public int submitButtonIndex = 0; // which button advances the step; -1 for none
        public BuildFn build;
    }

    // ------------------------------------------------------------------

    private static readonly Dictionary<string, DialogDef> byStep = new Dictionary<string, DialogDef>();

    /// <summary>Returns the window definition for a step, or null if that step has none yet.</summary>
    public static DialogDef For(string stepName)
    {
        if (byStep.Count == 0) Register();
        return byStep.TryGetValue(stepName, out var def) ? def : null;
    }

    public static int DefinedCount
    {
        get { if (byStep.Count == 0) Register(); return byStep.Count; }
    }

    private static void Add(DialogDef def) => byStep[def.stepName] = def;

    // ==================================================================
    // PHASE 1
    // ==================================================================

    private static void Register()
    {
        var K = COC3StepBlueprint.ServerName;      // shorthand used below

        // ---- Computer Name/Domain Changes  (step 2, server) --------------
        Add(new DialogDef
        {
            stepName = "Step_02_ComputerName",
            title = "Computer Name/Domain Changes",
            size = new Vector2(430f, 400f),
            buttons = new[] { "OK", "Cancel" },
            submitButtonIndex = 0,
            build = (body, t, bind, w, h) =>
            {
                COC3WinWidgets.Label(body, t, "Intro", 0f, 0f, w, 40f,
                    "You can change the name and the membership of this\ncomputer. Changes might affect access to network resources.");

                bind.answer = COC3WinWidgets.Field(body, t, "ComputerName", 0f, 58f, 0f, w,
                    "Computer name:", "", "", false);

                COC3WinWidgets.Label(body, t, "FullNameLabel", 0f, 96f, w, 20f, "Full computer name:");
                COC3WinWidgets.Field(body, t, "FullName", 0f, 118f, 0f, w, "",
                    K + "." + COC3StepBlueprint.DomainFQDN, "", true);

                var group = COC3WinWidgets.GroupBox(body, t, "MemberOf", 0f, 168f, w, 150f, "Member of");
                float gw = w - t.padding * 2f;

                COC3WinWidgets.Radio(group, t, "DomainRadio", 0f, 0f, gw, "Domain:", true);
                COC3WinWidgets.Field(group, t, "DomainValue", 20f, 30f, 0f, gw - 20f, "",
                    COC3StepBlueprint.DomainFQDN, "", true);

                COC3WinWidgets.Radio(group, t, "WorkgroupRadio", 0f, 66f, gw, "Workgroup:", false);
                COC3WinWidgets.Field(group, t, "WorkgroupValue", 20f, 96f, 0f, gw - 20f, "",
                    "WORKGROUP", "", true);
            }
        });

        // ---- Computer Name/Domain Changes  (step 11, client) -------------
        Add(new DialogDef
        {
            stepName = "Step_11_JoinClientToDomain",
            title = "Computer Name/Domain Changes",
            size = new Vector2(430f, 400f),
            buttons = new[] { "OK", "Cancel" },
            submitButtonIndex = 0,
            build = (body, t, bind, w, h) =>
            {
                COC3WinWidgets.Label(body, t, "Intro", 0f, 0f, w, 40f,
                    "You can change the name and the membership of this\ncomputer. Changes might affect access to network resources.");

                COC3WinWidgets.Field(body, t, "ComputerName", 0f, 58f, 0f, w,
                    "Computer name:", COC3StepBlueprint.ClientName, "", true);

                var group = COC3WinWidgets.GroupBox(body, t, "MemberOf", 0f, 120f, w, 150f, "Member of");
                float gw = w - t.padding * 2f;

                // The Domain radio is already selected - this step is about typing the domain,
                // not about choosing the radio, so the choice is shown as already made.
                COC3WinWidgets.Radio(group, t, "DomainRadio", 0f, 0f, gw, "Domain:", true);

                bind.answer = COC3WinWidgets.Field(group, t, "DomainValue", 20f, 30f, 0f, gw - 20f,
                    "", "", "", false);

                COC3WinWidgets.Radio(group, t, "WorkgroupRadio", 0f, 66f, gw, "Workgroup:", false);
                COC3WinWidgets.Field(group, t, "WorkgroupValue", 20f, 96f, 0f, gw - 20f, "",
                    "WORKGROUP", "", true);
            }
        });

        // ---- IPv4 Properties  (step 3, addresses) ------------------------
        Add(new DialogDef
        {
            stepName = "Step_03_StaticIPConfig",
            title = "Internet Protocol Version 4 (TCP/IPv4) Properties",
            size = new Vector2(470f, 470f),
            buttons = new[] { "OK", "Cancel" },
            submitButtonIndex = 0,
            build = (body, t, bind, w, h) =>
            {
                COC3WinWidgets.TabStrip(body, t, "Tabs", 0f, 0f, w,
                    new[] { "General", "Alternate Configuration" }, 0);

                COC3WinWidgets.Label(body, t, "Intro", 0f, 40f, w, 44f,
                    "You can get IP settings assigned automatically if your network\nsupports this capability.");

                COC3WinWidgets.Radio(body, t, "Auto", 0f, 96f, w,
                    "Obtain an IP address automatically", false);
                COC3WinWidgets.Radio(body, t, "Manual", 0f, 126f, w,
                    "Use the following IP address:", true);

                float lw = 150f, fw = w - lw - 24f;

                bind.ip = COC3WinWidgets.Field(body, t, "IPAddress", 24f, 164f, lw, fw,
                    "IP address:", "", "", false);
                bind.mask = COC3WinWidgets.Field(body, t, "SubnetMask", 24f, 200f, lw, fw,
                    "Subnet mask:", "", "", false);
                bind.gateway = COC3WinWidgets.Field(body, t, "DefaultGateway", 24f, 236f, lw, fw,
                    "Default gateway:", "", "", false);

                COC3WinWidgets.Radio(body, t, "DnsAuto", 0f, 286f, w,
                    "Obtain DNS server address automatically", false);
                COC3WinWidgets.Radio(body, t, "DnsManual", 0f, 316f, w,
                    "Use the following DNS server addresses:", true);
            }
        });

        // ---- IPv4 Properties  (step 4, DNS section) ----------------------
        Add(new DialogDef
        {
            stepName = "Step_04_DNSConfig",
            title = "Internet Protocol Version 4 (TCP/IPv4) Properties",
            size = new Vector2(470f, 470f),
            buttons = new[] { "OK", "Cancel" },
            submitButtonIndex = 0,
            build = (body, t, bind, w, h) =>
            {
                COC3WinWidgets.TabStrip(body, t, "Tabs", 0f, 0f, w,
                    new[] { "General", "Alternate Configuration" }, 0);

                // The addresses from step 3 are shown filled in and locked, so the learner can
                // see the continuity between the two steps rather than a blank dialog.
                COC3WinWidgets.Radio(body, t, "Manual", 0f, 44f, w,
                    "Use the following IP address:", true);

                float lw = 150f, fw = w - lw - 24f;

                COC3WinWidgets.Field(body, t, "IPAddress", 24f, 78f, lw, fw,
                    "IP address:", COC3StepBlueprint.ServerIP, "", true);
                COC3WinWidgets.Field(body, t, "SubnetMask", 24f, 114f, lw, fw,
                    "Subnet mask:", COC3StepBlueprint.SubnetMask, "", true);
                COC3WinWidgets.Field(body, t, "DefaultGateway", 24f, 150f, lw, fw,
                    "Default gateway:", COC3StepBlueprint.Gateway, "", true);

                COC3WinWidgets.Radio(body, t, "DnsAuto", 0f, 200f, w,
                    "Obtain DNS server address automatically", false);
                COC3WinWidgets.Radio(body, t, "DnsManual", 0f, 230f, w,
                    "Use the following DNS server addresses:", true);

                bind.answer = COC3WinWidgets.Field(body, t, "PreferredDNS", 24f, 268f, lw, fw,
                    "Preferred DNS server:", "", "", false);
                COC3WinWidgets.Field(body, t, "AlternateDNS", 24f, 304f, lw, fw,
                    "Alternate DNS server:", "", "", true);
            }
        });

        // ---- Run  (step 19) ----------------------------------------------
        Add(new DialogDef
        {
            stepName = "Step_19_TestClientAccess",
            title = "Run",
            size = new Vector2(420f, 190f),
            buttons = new[] { "OK", "Cancel", "Browse..." },
            submitButtonIndex = 0,
            build = (body, t, bind, w, h) =>
            {
                COC3WinWidgets.Label(body, t, "Intro", 0f, 0f, w, 46f,
                    "Type the name of a program, folder, document, or Internet\nresource, and Windows will open it for you.");

                bind.answer = COC3WinWidgets.Field(body, t, "Open", 0f, 62f, 52f, w - 52f,
                    "Open:", "", "", false);
            }
        });

        // ==============================================================
        // PHASE 2
        // ==============================================================

        // ---- AD DS Configuration Wizard  (step 6) ------------------------
        Add(new DialogDef
        {
            stepName = "Step_06_PromoteToDomainController",
            frame = Frame.Wizard,
            title = "Active Directory Domain Services Configuration Wizard",
            heading = "Deployment Configuration",
            stages = new[] { "Deployment Configuration", "Domain Controller Options", "DNS Options",
                             "Additional Options", "Paths", "Review Options", "Prerequisites Check",
                             "Installation", "Results" },
            activeStage = 0,
            size = new Vector2(660f, 470f),
            buttons = new[] { "< Back", "Next >", "Cancel" },
            submitButtonIndex = 1,
            build = (body, t, bind, w, h) =>
            {
                COC3WinWidgets.Label(body, t, "Prompt", 0f, 0f, w, 22f, "Select the deployment operation");

                COC3WinWidgets.Radio(body, t, "ExistingDomain", 12f, 28f, w - 12f,
                    "Add a domain controller to an existing domain", false);
                COC3WinWidgets.Radio(body, t, "ExistingForest", 12f, 56f, w - 12f,
                    "Add a new domain to an existing forest", false);
                COC3WinWidgets.Radio(body, t, "NewForest", 12f, 84f, w - 12f,
                    "Add a new forest", true);

                COC3WinWidgets.Label(body, t, "Specify", 0f, 124f, w, 22f,
                    "Specify the domain information for this operation");

                bind.answer = COC3WinWidgets.Field(body, t, "RootDomain", 12f, 152f, 150f, w - 174f,
                    "Root domain name:", "", "", false);
            }
        });

        // ---- New Scope Wizard  (step 7) ----------------------------------
        Add(new DialogDef
        {
            stepName = "Step_07_DHCPConfig",
            frame = Frame.Wizard,
            title = "New Scope Wizard",
            heading = "IP Address Range",
            stages = new[] { "Scope Name", "IP Address Range", "Add Exclusions", "Lease Duration",
                             "Configure DHCP Options", "Router (Default Gateway)", "Domain Name and DNS",
                             "Activate Scope" },
            activeStage = 1,
            size = new Vector2(660f, 470f),
            buttons = new[] { "< Back", "Next >", "Cancel" },
            submitButtonIndex = 1,
            build = (body, t, bind, w, h) =>
            {
                COC3WinWidgets.Label(body, t, "Prompt", 0f, 0f, w, 22f,
                    "Configuration settings for DHCP Server", TextAlignmentOptions.Left, true);

                float lw = 170f, fw = w - lw - 24f;

                bind.ip = COC3WinWidgets.Field(body, t, "StartIP", 12f, 32f, lw, fw,
                    "Start IP address:", "", "", false);
                COC3WinWidgets.Field(body, t, "EndIP", 12f, 68f, lw, fw,
                    "End IP address:", "192.168.10.200", "", true);
                bind.mask = COC3WinWidgets.Field(body, t, "SubnetMask", 12f, 104f, lw, fw,
                    "Subnet mask:", "", "", false);

                COC3WinWidgets.Label(body, t, "Prompt2", 0f, 150f, w, 22f,
                    "Configuration settings that propagate to DHCP Client",
                    TextAlignmentOptions.Left, true);

                bind.gateway = COC3WinWidgets.Field(body, t, "Router", 12f, 182f, lw, fw,
                    "Router (default gateway):", "", "", false);
            }
        });

        // ---- New Zone Wizard  (step 8) -----------------------------------
        Add(new DialogDef
        {
            stepName = "Step_08_DNSServerConfig",
            frame = Frame.Wizard,
            title = "New Zone Wizard",
            heading = "Reverse Lookup Zone Name",
            stages = new[] { "Zone Type", "Reverse Lookup Zone Name", "Zone File",
                             "Dynamic Update", "Completing" },
            activeStage = 1,
            size = new Vector2(660f, 440f),
            buttons = new[] { "< Back", "Next >", "Cancel" },
            submitButtonIndex = 1,
            build = (body, t, bind, w, h) =>
            {
                COC3WinWidgets.Label(body, t, "Prompt", 0f, 0f, w, 40f,
                    "A reverse lookup zone translates IP addresses into DNS names.");

                COC3WinWidgets.Radio(body, t, "IPv4", 12f, 48f, w - 12f,
                    "IPv4 Reverse Lookup Zone", true);
                COC3WinWidgets.Radio(body, t, "IPv6", 12f, 76f, w - 12f,
                    "IPv6 Reverse Lookup Zone", false);

                float lw = 190f, fw = w - lw - 24f;

                bind.answer = COC3WinWidgets.Field(body, t, "NetworkID", 12f, 122f, lw, fw,
                    "Network ID:", "", "", false);
                COC3WinWidgets.Field(body, t, "ZoneName", 12f, 158f, lw, fw,
                    "Reverse lookup zone name:", "10.168.192.in-addr.arpa", "", true);
            }
        });

        // ---- New Object - User  (step 13) --------------------------------
        Add(new DialogDef
        {
            stepName = "Step_13_CreateUserAccounts",
            title = "New Object - User",
            size = new Vector2(500f, 400f),
            buttons = new[] { "< Back", "Next >", "Cancel" },
            submitButtonIndex = 1,
            build = (body, t, bind, w, h) =>
            {
                COC3WinWidgets.Label(body, t, "CreateIn", 0f, 0f, w, 22f,
                    "Create in:  " + COC3StepBlueprint.DomainFQDN + "/Users");

                float lw = 110f, fw = w - lw - 12f;

                COC3WinWidgets.Field(body, t, "FirstName", 0f, 36f, lw, fw,
                    "First name:", "Student", "", true);
                COC3WinWidgets.Field(body, t, "LastName", 0f, 72f, lw, fw,
                    "Last name:", "One", "", true);
                COC3WinWidgets.Field(body, t, "FullName", 0f, 108f, lw, fw,
                    "Full name:", "Student One", "", true);

                COC3WinWidgets.Label(body, t, "LogonLabel", 0f, 156f, w, 22f, "User logon name:");

                bind.answer = COC3WinWidgets.Field(body, t, "LogonName", 0f, 182f, 0f, fw - 130f,
                    "", "", "", false);
                COC3WinWidgets.Field(body, t, "Suffix", fw - 124f, 182f, 0f, 130f,
                    "", "@" + COC3StepBlueprint.DomainFQDN, "", true);
            }
        });

        // ---- File Explorer  (steps 12 and 23) ----------------------------
        Add(ExplorerDialog("Step_12_CreateUserFolders", "ServerData"));
        Add(ExplorerDialog("Step_23_CreateRedirectionFolder", "ServerData"));

        // ---- Select Users or Groups  (step 28) ---------------------------
        Add(new DialogDef
        {
            stepName = "Step_28_AllowedRDPUsers",
            title = "Select Users or Groups",
            size = new Vector2(520f, 320f),
            buttons = new[] { "OK", "Cancel" },
            submitButtonIndex = 0,
            build = (body, t, bind, w, h) =>
            {
                float lw = 170f, fw = w - lw - 100f;

                COC3WinWidgets.Field(body, t, "ObjectType", 0f, 0f, lw, fw,
                    "Select this object type:", "Users or Built-in security principals", "", true);
                COC3WinWidgets.Button(body, t, "ObjectTypes", w - 92f, 0f, 92f, t.rowHeight,
                    "Object Types...");

                COC3WinWidgets.Field(body, t, "Location", 0f, 36f, lw, fw,
                    "From this location:", COC3StepBlueprint.DomainFQDN, "", true);
                COC3WinWidgets.Button(body, t, "Locations", w - 92f, 36f, 92f, t.rowHeight,
                    "Locations...");

                COC3WinWidgets.Label(body, t, "EnterLabel", 0f, 82f, w, 22f,
                    "Enter the object name to select (examples):");

                bind.answer = COC3WinWidgets.Field(body, t, "ObjectName", 0f, 108f, 0f, w - 100f,
                    "", "", "", false);
                COC3WinWidgets.Button(body, t, "CheckNames", w - 92f, 108f, 92f, t.rowHeight,
                    "Check Names");
            }
        });

        // ---- Remote Desktop Connection  (step 29) ------------------------
        Add(new DialogDef
        {
            stepName = "Step_29_ConnectFromClient",
            title = "Remote Desktop Connection",
            size = new Vector2(440f, 250f),
            buttons = new[] { "Connect", "Help" },
            submitButtonIndex = 0,
            build = (body, t, bind, w, h) =>
            {
                COC3WinWidgets.Label(body, t, "Brand", 0f, 0f, w, 30f,
                    "Remote Desktop Connection", TextAlignmentOptions.Left, true);

                float lw = 100f, fw = w - lw;

                bind.answer = COC3WinWidgets.Field(body, t, "Computer", 0f, 54f, lw, fw,
                    "Computer:", "", "Example: computer.fabrikam.com", false);
                COC3WinWidgets.Field(body, t, "UserName", 0f, 90f, lw, fw,
                    "User name:", "None specified", "", true);

                COC3WinWidgets.Label(body, t, "Note", 0f, 126f, w, 22f,
                    "You will be asked for credentials when you connect.",
                    TextAlignmentOptions.Left, false, t.disabledText);
            }
        });

        // ---- Command Prompt  (step 30, PingTest) -------------------------
        Add(new DialogDef
        {
            stepName = "Step_30_VerifyRemoteAccess",
            title = "Administrator: Command Prompt",
            size = new Vector2(660f, 380f),
            buttons = new[] { "RUN PING" },
            submitButtonIndex = 0,
            build = (body, t, bind, w, h) =>
            {
                bind.console = COC3WinWidgets.Console(body, t, "Console", 0f, 0f, w, h - 36f,
                    "Microsoft Windows [Version 10.0.17763.1]\n" +
                    "(c) Microsoft Corporation. All rights reserved.\n\nC:\\>");
            }
        });

        // ==============================================================
        // PHASE 3  -  the click-steps, where a real control is the target
        // ==============================================================

        // ---- STUDENTS Properties > Members  (step 14) --------------------
        Add(new DialogDef
        {
            stepName = "Step_14_AccessLevels",
            title = COC3StepBlueprint.UserGroup + " Properties",
            size = new Vector2(470f, 430f),
            buttons = new[] { "OK", "Cancel", "Apply" },
            submitButtonIndex = -1,      // the Add button is the target, not a dialog button
            build = (body, t, bind, w, h) =>
            {
                COC3WinWidgets.TabStrip(body, t, "Tabs", 0f, 0f, w,
                    new[] { "General", "Members", "Member Of", "Managed By" }, 1);

                COC3WinWidgets.Label(body, t, "MembersLabel", 0f, 40f, w, 22f, "Members:");

                COC3WinWidgets.ListRows(body, t, "Members", 0f, 66f, w, 150f,
                    new[] { "Name", "Active Directory Domain Services Folder" },
                    new string[0][], -1);

                bind.target = COC3WinWidgets.Button(body, t, "Add", 0f, 232f, 88f, t.rowHeight, "Add...");
                COC3WinWidgets.Button(body, t, "Remove", 96f, 232f, 88f, t.rowHeight, "Remove");
            }
        });

        // ---- Group Policy Editor > Password Policy  (step 15) ------------
        Add(new DialogDef
        {
            stepName = "Step_15_AccessPolicies",
            frame = Frame.Bare,
            title = "Group Policy Management Editor",
            size = new Vector2(760f, 440f),
            buttons = new string[0],
            submitButtonIndex = -1,
            build = (body, t, bind, w, h) =>
            {
                var mmc = COC3WinWidgets.Mmc(body, t, body, w, h,
                    new[] { "File", "Action", "View", "Help" }, 280f);

                COC3WinWidgets.TreeNode(mmc.tree, t, "N0", 4f, mmc.treeW, 0, "Default Domain Policy", true, true, false);
                COC3WinWidgets.TreeNode(mmc.tree, t, "N1", 26f, mmc.treeW, 1, "Computer Configuration", true, true, false);
                COC3WinWidgets.TreeNode(mmc.tree, t, "N2", 48f, mmc.treeW, 2, "Policies", true, true, false);
                COC3WinWidgets.TreeNode(mmc.tree, t, "N3", 70f, mmc.treeW, 3, "Windows Settings", true, true, false);
                COC3WinWidgets.TreeNode(mmc.tree, t, "N4", 92f, mmc.treeW, 4, "Security Settings", true, true, false);
                COC3WinWidgets.TreeNode(mmc.tree, t, "N5", 114f, mmc.treeW, 5, "Account Policies", true, true, false);
                COC3WinWidgets.TreeNode(mmc.tree, t, "N6", 136f, mmc.treeW, 6, "Password Policy", false, false, true);

                var rows = COC3WinWidgets.ListRows(mmc.list, t, "Policies", 0f, 0f, mmc.listW, mmc.listH,
                    new[] { "Policy", "Policy Setting" },
                    new[]
                    {
                        new[] { "Enforce password history", "24 passwords remembered" },
                        new[] { "Maximum password age", "42 days" },
                        new[] { "Minimum password age", "1 days" },
                        new[] { "Minimum password length", "7 characters" },
                        new[] { "Password must meet complexity requirements", "Enabled" },
                    }, -1);

                if (rows.Length > 3) bind.target = rows[3];   // Minimum password length
            }
        });

        // ---- Security tab: folder permissions  (step 17) -----------------
        Add(new DialogDef
        {
            stepName = "Step_17_FolderPermissions",
            title = COC3StepBlueprint.ShareName + " Properties",
            size = new Vector2(490f, 470f),
            buttons = new[] { "OK", "Cancel", "Apply" },
            submitButtonIndex = -1,
            build = (body, t, bind, w, h) =>
            {
                COC3WinWidgets.TabStrip(body, t, "Tabs", 0f, 0f, w,
                    new[] { "General", "Sharing", "Security", "Previous Versions" }, 2);

                COC3WinWidgets.Label(body, t, "ObjectName", 0f, 40f, w, 22f,
                    "Object name:  C:\\ServerData\\" + COC3StepBlueprint.ShareName);

                COC3WinWidgets.Label(body, t, "GroupsLabel", 0f, 68f, w, 22f,
                    "Group or user names:");
                COC3WinWidgets.ListView(body, t, "Groups", 0f, 92f, w, 74f,
                    new[] { "Authenticated Users", "SYSTEM", COC3StepBlueprint.UserGroup + " (CSSNCII\\" + COC3StepBlueprint.UserGroup + ")" }, 2);

                var allow = COC3WinWidgets.PermissionGrid(body, t, "Perms", 0f, 176f, w, 190f,
                    "Permissions for " + COC3StepBlueprint.UserGroup,
                    new[] { "Full control", "Modify", "Read & execute", "List folder contents", "Read", "Write" },
                    -1);

                if (allow.Length > 1) bind.target = allow[1];   // Modify
            }
        });

        // ---- Security tab: printer permissions  (step 21) ----------------
        Add(new DialogDef
        {
            stepName = "Step_21_PrinterPermissions",
            title = "HP LaserJet Properties",
            size = new Vector2(490f, 440f),
            buttons = new[] { "OK", "Cancel", "Apply" },
            submitButtonIndex = -1,
            build = (body, t, bind, w, h) =>
            {
                COC3WinWidgets.TabStrip(body, t, "Tabs", 0f, 0f, w,
                    new[] { "General", "Sharing", "Ports", "Security" }, 3);

                COC3WinWidgets.Label(body, t, "GroupsLabel", 0f, 44f, w, 22f, "Group or user names:");
                COC3WinWidgets.ListView(body, t, "Groups", 0f, 68f, w, 74f,
                    new[] { "Everyone", "CREATOR OWNER", COC3StepBlueprint.UserGroup + " (CSSNCII\\" + COC3StepBlueprint.UserGroup + ")" }, 2);

                var allow = COC3WinWidgets.PermissionGrid(body, t, "Perms", 0f, 152f, w, 140f,
                    "Permissions for " + COC3StepBlueprint.UserGroup,
                    new[] { "Print", "Manage this printer", "Manage documents" }, -1);

                if (allow.Length > 0) bind.target = allow[0];   // Print
            }
        });

        // ---- Group Policy Editor > Folder Redirection  (step 24) ---------
        Add(new DialogDef
        {
            stepName = "Step_24_RedirectionPolicy",
            frame = Frame.Bare,
            title = "Group Policy Management Editor",
            size = new Vector2(760f, 440f),
            buttons = new string[0],
            submitButtonIndex = -1,
            build = (body, t, bind, w, h) =>
            {
                var mmc = COC3WinWidgets.Mmc(body, t, body, w, h,
                    new[] { "File", "Action", "View", "Help" }, 280f);

                COC3WinWidgets.TreeNode(mmc.tree, t, "N0", 4f, mmc.treeW, 0, "Folder Redirection Policy", true, true, false);
                COC3WinWidgets.TreeNode(mmc.tree, t, "N1", 26f, mmc.treeW, 1, "User Configuration", true, true, false);
                COC3WinWidgets.TreeNode(mmc.tree, t, "N2", 48f, mmc.treeW, 2, "Policies", true, true, false);
                COC3WinWidgets.TreeNode(mmc.tree, t, "N3", 70f, mmc.treeW, 3, "Windows Settings", true, true, false);
                COC3WinWidgets.TreeNode(mmc.tree, t, "N4", 92f, mmc.treeW, 4, "Folder Redirection", false, true, true);

                var rows = COC3WinWidgets.ListRows(mmc.list, t, "Folders", 0f, 0f, mmc.listW, mmc.listH,
                    new[] { "Folder", "Setting" },
                    new[]
                    {
                        new[] { "AppData(Roaming)", "Not configured" },
                        new[] { "Desktop", "Not configured" },
                        new[] { "Documents", "Not configured" },
                        new[] { "Pictures", "Not configured" },
                        new[] { "Favorites", "Not configured" },
                    }, -1);

                if (rows.Length > 2) bind.target = rows[2];   // Documents
            }
        });

        // ---- System Properties > Remote  (step 27) -----------------------
        Add(new DialogDef
        {
            stepName = "Step_27_EnableRDP",
            title = "System Properties",
            size = new Vector2(470f, 440f),
            buttons = new[] { "OK", "Cancel", "Apply" },
            submitButtonIndex = -1,
            build = (body, t, bind, w, h) =>
            {
                COC3WinWidgets.TabStrip(body, t, "Tabs", 0f, 0f, w,
                    new[] { "Computer Name", "Hardware", "Advanced", "Remote" }, 3);

                var assist = COC3WinWidgets.GroupBox(body, t, "Assistance", 0f, 44f, w, 76f,
                    "Remote Assistance");
                COC3WinWidgets.Checkbox(assist, t, "AllowAssist", 0f, 0f, w - t.padding * 2f,
                    "Allow Remote Assistance connections to this computer", false);

                var rd = COC3WinWidgets.GroupBox(body, t, "RemoteDesktop", 0f, 136f, w, 150f,
                    "Remote Desktop");
                float gw = w - t.padding * 2f;

                COC3WinWidgets.Radio(rd, t, "DontAllow", 0f, 0f, gw,
                    "Don't allow remote connections to this computer", true);

                bind.target = COC3WinWidgets.Radio(rd, t, "AllowRemote", 0f, 30f, gw,
                    "Allow remote connections to this computer", false);

                COC3WinWidgets.Checkbox(rd, t, "NLA", 20f, 62f, gw - 20f,
                    "Allow connections only with Network Level Authentication", true);
            }
        });

        // ---- Advanced Security Settings > Effective Access  (step 32) ----
        Add(new DialogDef
        {
            stepName = "Step_32_UserPermissionsCheck",
            title = "Advanced Security Settings for " + COC3StepBlueprint.ShareName,
            size = new Vector2(600f, 420f),
            buttons = new[] { "OK", "Cancel", "Apply" },
            submitButtonIndex = -1,
            build = (body, t, bind, w, h) =>
            {
                COC3WinWidgets.TabStrip(body, t, "Tabs", 0f, 0f, w,
                    new[] { "Permissions", "Auditing", "Effective Access" }, 2);

                COC3WinWidgets.Label(body, t, "Blurb", 0f, 44f, w, 56f,
                    "Effective Access lets you view the effective permissions for a user, group or\ndevice account. Select a user and click View effective access.");

                COC3WinWidgets.Label(body, t, "UserLabel", 0f, 112f, 90f, 22f, "User/Group:");
                COC3WinWidgets.Field(body, t, "UserValue", 90f, 110f, 0f, 200f, "",
                    COC3StepBlueprint.SampleUser, "", true);
                COC3WinWidgets.Button(body, t, "SelectUser", 300f, 110f, 100f, t.rowHeight, "Select a user");

                bind.target = COC3WinWidgets.Button(body, t, "ViewEffective", 0f, 156f, 170f, t.rowHeight,
                    "View effective access", true);
            }
        });

        // ---- Advanced Security Settings > Permissions  (step 33) ---------
        Add(new DialogDef
        {
            stepName = "Step_33_SharedFolderSecurityCheck",
            title = "Advanced Security Settings for " + COC3StepBlueprint.ShareName,
            size = new Vector2(640f, 440f),
            buttons = new[] { "OK", "Cancel", "Apply" },
            submitButtonIndex = -1,
            build = (body, t, bind, w, h) =>
            {
                COC3WinWidgets.TabStrip(body, t, "Tabs", 0f, 0f, w,
                    new[] { "Permissions", "Auditing", "Effective Access" }, 0);

                COC3WinWidgets.Label(body, t, "Owner", 0f, 44f, w, 22f,
                    "Owner:  Administrators (CSSNCII\\Administrators)");

                COC3WinWidgets.Label(body, t, "EntriesLabel", 0f, 74f, w, 22f, "Permission entries:");

                COC3WinWidgets.ListRows(body, t, "Entries", 0f, 100f, w, 150f,
                    new[] { "Type", "Principal", "Access", "Inherited from" },
                    new[]
                    {
                        new[] { "Allow", "Everyone", "Read & execute", "C:\\" },
                        new[] { "Allow", COC3StepBlueprint.UserGroup, "Modify", "None" },
                        new[] { "Allow", "SYSTEM", "Full control", "C:\\" },
                    }, -1);

                bind.target = COC3WinWidgets.Button(body, t, "DisableInheritance", 0f, 264f, 150f,
                    t.rowHeight, "Disable inheritance", true);
            }
        });

        // ==============================================================
        // PHASE 4  -  Server Manager, printers, and the dense consoles
        // ==============================================================

        // Steps 5, 9 and 10 are the SAME wizard page with a different role ticked, so they
        // share one builder. This is the payoff of the archetype approach: three "hard"
        // steps cost one layout.
        Add(RolesPage("Step_05_InstallRoles", 1));    // Active Directory Domain Services
        Add(RolesPage("Step_09_FileServicesConfig", 6));   // File Server
        Add(RolesPage("Step_10_OtherServices", 7));   // Print and Document Services

        // ---- Printer Properties > Sharing  (step 20) ---------------------
        Add(new DialogDef
        {
            stepName = "Step_20_InstallSharedPrinter",
            title = "HP LaserJet Properties",
            size = new Vector2(470f, 430f),
            buttons = new[] { "OK", "Cancel", "Apply" },
            submitButtonIndex = -1,
            build = (body, t, bind, w, h) =>
            {
                COC3WinWidgets.TabStrip(body, t, "Tabs", 0f, 0f, w,
                    new[] { "General", "Sharing", "Ports", "Security" }, 1);

                COC3WinWidgets.Label(body, t, "Blurb", 0f, 44f, w, 56f,
                    "You can share this printer with other users on your network.\nThe printer will not be available when the computer is asleep.");

                bind.target = COC3WinWidgets.Checkbox(body, t, "SharePrinter", 0f, 112f, w,
                    "Share this printer", false);

                COC3WinWidgets.Field(body, t, "ShareName", 20f, 148f, 90f, w - 130f,
                    "Share name:", "HPLaserJet", "", true);

                COC3WinWidgets.Checkbox(body, t, "RenderClient", 20f, 190f, w - 20f,
                    "Render print jobs on client computers", true);
                COC3WinWidgets.Checkbox(body, t, "ListDirectory", 20f, 220f, w - 20f,
                    "List in the directory", false);
            }
        });

        // ---- Printer Properties > General  (step 22, client) -------------
        Add(new DialogDef
        {
            stepName = "Step_22_TestPrintFromClient",
            title = "HP LaserJet on " + COC3StepBlueprint.ServerName + " Properties",
            size = new Vector2(470f, 430f),
            buttons = new[] { "OK", "Cancel", "Apply" },
            submitButtonIndex = -1,
            build = (body, t, bind, w, h) =>
            {
                COC3WinWidgets.TabStrip(body, t, "Tabs", 0f, 0f, w,
                    new[] { "General", "Sharing", "Ports", "Security" }, 0);

                COC3WinWidgets.Field(body, t, "PrinterName", 0f, 46f, 0f, w, "",
                    "HP LaserJet on " + COC3StepBlueprint.ServerName, "", true);

                COC3WinWidgets.Field(body, t, "Location", 0f, 84f, 90f, w - 90f,
                    "Location:", "Server Room", "", true);
                COC3WinWidgets.Field(body, t, "Comment", 0f, 120f, 90f, w - 90f,
                    "Comment:", "", "", true);

                COC3WinWidgets.Label(body, t, "Model", 0f, 162f, w, 22f, "Model:  HP LaserJet");
                COC3WinWidgets.Label(body, t, "Ready", 0f, 188f, w, 22f, "Status:  Ready",
                    TextAlignmentOptions.Left, false, t.disabledText);

                COC3WinWidgets.Button(body, t, "Preferences", 0f, 232f, 110f, t.rowHeight,
                    "Preferences...");

                bind.target = COC3WinWidgets.Button(body, t, "PrintTestPage", 120f, 232f, 130f,
                    t.rowHeight, "Print Test Page", true);
            }
        });

        // ---- Documents Properties > Target  (step 25) --------------------
        Add(new DialogDef
        {
            stepName = "Step_25_RedirectFoldersTarget",
            title = "Documents Properties",
            size = new Vector2(490f, 420f),
            buttons = new[] { "OK", "Cancel", "Apply" },
            submitButtonIndex = 0,
            build = (body, t, bind, w, h) =>
            {
                COC3WinWidgets.TabStrip(body, t, "Tabs", 0f, 0f, w,
                    new[] { "Target", "Settings" }, 0);

                COC3WinWidgets.Label(body, t, "Blurb", 0f, 46f, w, 22f,
                    "You can specify the location of the Documents folder.");

                COC3WinWidgets.Field(body, t, "Setting", 0f, 82f, 60f, w - 60f, "Setting:",
                    "Basic - Redirect everyone's folder to the same location", "", true);

                var group = COC3WinWidgets.GroupBox(body, t, "TargetGroup", 0f, 126f, w, 150f,
                    "Target folder location");
                float gw = w - t.padding * 2f;

                COC3WinWidgets.Field(group, t, "FolderLocation", 0f, 0f, 0f, gw, "",
                    "Create a folder for each user under the root path", "", true);

                COC3WinWidgets.Label(group, t, "RootLabel", 0f, 44f, 80f, 22f, "Root Path:");
                bind.answer = COC3WinWidgets.Field(group, t, "RootPath", 80f, 42f, 0f, gw - 180f,
                    "", "", "", false);
                COC3WinWidgets.Button(group, t, "Browse", gw - 92f, 42f, 92f, t.rowHeight, "Browse...");

                COC3WinWidgets.Label(group, t, "Preview", 0f, 84f, gw, 22f,
                    "For user Clair, this folder will be redirected to:",
                    TextAlignmentOptions.Left, false, t.disabledText);
            }
        });

        // ---- Documents Properties > Location  (step 26, client) ----------
        Add(new DialogDef
        {
            stepName = "Step_26_TestRedirectionClient",
            title = "Documents Properties",
            size = new Vector2(470f, 420f),
            buttons = new[] { "OK", "Cancel", "Apply" },
            submitButtonIndex = -1,
            build = (body, t, bind, w, h) =>
            {
                COC3WinWidgets.TabStrip(body, t, "Tabs", 0f, 0f, w,
                    new[] { "General", "Sharing", "Security", "Location" }, 3);

                COC3WinWidgets.Label(body, t, "Blurb", 0f, 46f, w, 56f,
                    "Files in the Documents folder are stored in the target location below.\nYou can change where files in this folder are stored on this hard disk\nor another drive.");

                // The path itself is the target: the learner clicks the location to confirm
                // redirection actually took effect, which is what the step is checking.
                var rows = COC3WinWidgets.ListRows(body, t, "LocationPath", 0f, 116f, w, 30f,
                    null,
                    new[] { new[] { "\\\\" + COC3StepBlueprint.ServerName + "\\" +
                                    COC3StepBlueprint.RedirectShare + "\\" +
                                    COC3StepBlueprint.SampleUser + "\\Documents" } }, 0);

                if (rows.Length > 0) bind.target = rows[0];

                COC3WinWidgets.Button(body, t, "Restore", 0f, 168f, 110f, t.rowHeight, "Restore Default");
                COC3WinWidgets.Button(body, t, "Move", 120f, 168f, 90f, t.rowHeight, "Move...");
                COC3WinWidgets.Button(body, t, "FindTarget", 220f, 168f, 110f, t.rowHeight, "Find Target...");
            }
        });

        // ---- Windows Defender Firewall > Inbound Rules  (step 31) --------
        Add(new DialogDef
        {
            stepName = "Step_31_FirewallCheck",
            frame = Frame.Bare,
            title = "Windows Defender Firewall with Advanced Security",
            size = new Vector2(820f, 440f),
            buttons = new string[0],
            submitButtonIndex = -1,
            build = (body, t, bind, w, h) =>
            {
                var mmc = COC3WinWidgets.Mmc(body, t, body, w, h,
                    new[] { "File", "Action", "View", "Help" }, 250f);

                COC3WinWidgets.TreeNode(mmc.tree, t, "N0", 4f, mmc.treeW, 0,
                    "Windows Defender Firewall with Adv", true, true, false);
                COC3WinWidgets.TreeNode(mmc.tree, t, "N1", 26f, mmc.treeW, 1, "Inbound Rules", false, false, true);
                COC3WinWidgets.TreeNode(mmc.tree, t, "N2", 48f, mmc.treeW, 1, "Outbound Rules", false, false, false);
                COC3WinWidgets.TreeNode(mmc.tree, t, "N3", 70f, mmc.treeW, 1, "Connection Security Rules", false, false, false);
                COC3WinWidgets.TreeNode(mmc.tree, t, "N4", 92f, mmc.treeW, 1, "Monitoring", true, false, false);

                var rows = COC3WinWidgets.ListRows(mmc.list, t, "Rules", 0f, 0f, mmc.listW, mmc.listH,
                    new[] { "Name", "Group", "Profile", "Enabled" },
                    new[]
                    {
                        new[] { "Active Directory Domain Controller", "AD DS", "All", "Yes" },
                        new[] { "Core Networking - DNS", "Core Networking", "All", "Yes" },
                        new[] { "File and Printer Sharing (SMB-In)", "File and Printer Sharing", "All", "No" },
                        new[] { "Remote Desktop - User Mode (TCP-In)", "Remote Desktop", "All", "Yes" },
                        new[] { "Windows Management Instrumentation", "WMI", "All", "No" },
                    }, -1);

                if (rows.Length > 2) bind.target = rows[2];   // File and Printer Sharing
            }
        });

        // ---- Event Viewer > Security log  (step 34) ----------------------
        Add(new DialogDef
        {
            stepName = "Step_34_UnauthorizedAccessCheck",
            frame = Frame.Bare,
            title = "Event Viewer",
            size = new Vector2(860f, 450f),
            buttons = new string[0],
            submitButtonIndex = -1,
            build = (body, t, bind, w, h) =>
            {
                var mmc = COC3WinWidgets.Mmc(body, t, body, w, h,
                    new[] { "File", "Action", "View", "Help" }, 220f, 170f);

                COC3WinWidgets.TreeNode(mmc.tree, t, "N0", 4f, mmc.treeW, 0, "Event Viewer (Local)", true, true, false);
                COC3WinWidgets.TreeNode(mmc.tree, t, "N1", 26f, mmc.treeW, 1, "Custom Views", true, false, false);
                COC3WinWidgets.TreeNode(mmc.tree, t, "N2", 48f, mmc.treeW, 1, "Windows Logs", true, true, false);
                COC3WinWidgets.TreeNode(mmc.tree, t, "N3", 70f, mmc.treeW, 2, "Application", false, false, false);
                COC3WinWidgets.TreeNode(mmc.tree, t, "N4", 92f, mmc.treeW, 2, "Security", false, false, true);
                COC3WinWidgets.TreeNode(mmc.tree, t, "N5", 114f, mmc.treeW, 2, "Setup", false, false, false);
                COC3WinWidgets.TreeNode(mmc.tree, t, "N6", 136f, mmc.treeW, 2, "System", false, false, false);

                COC3WinWidgets.ListRows(mmc.list, t, "Events", 0f, 0f, mmc.listW, mmc.listH,
                    new[] { "Keywords", "Date and Time", "Source", "Event ID" },
                    new[]
                    {
                        new[] { "Audit Success", "21/08/2026 09:14:02", "Security-Auditing", "4624" },
                        new[] { "Audit Failure", "21/08/2026 09:15:47", "Security-Auditing", "4625" },
                        new[] { "Audit Failure", "21/08/2026 09:16:03", "Security-Auditing", "4656" },
                        new[] { "Audit Success", "21/08/2026 09:17:20", "Security-Auditing", "4672" },
                    }, -1);

                // The Filter Current Log action lives in the Actions pane on the right, which is
                // where a technician actually reaches for it.
                bind.target = COC3WinWidgets.Button(mmc.actions, t, "FilterLog", 8f, 34f,
                    mmc.actionsW - 18f, t.rowHeight, "Filter Current Log...", true);

                COC3WinWidgets.Button(mmc.actions, t, "ClearLog", 8f, 66f, mmc.actionsW - 18f,
                    t.rowHeight, "Clear Log...");
                COC3WinWidgets.Button(mmc.actions, t, "Properties", 8f, 98f, mmc.actionsW - 18f,
                    t.rowHeight, "Properties");
            }
        });

        // ==============================================================
        // PHASE 5  -  the last dialog, and the three document pages
        // ==============================================================

        // ---- Advanced Sharing  (step 16) ---------------------------------
        Add(new DialogDef
        {
            stepName = "Step_16_CreateSharedFolder",
            title = "Advanced Sharing",
            size = new Vector2(440f, 340f),
            buttons = new[] { "OK", "Cancel", "Apply" },
            submitButtonIndex = 0,
            build = (body, t, bind, w, h) =>
            {
                COC3WinWidgets.Checkbox(body, t, "ShareFolder", 0f, 0f, w, "Share this folder", true);

                var group = COC3WinWidgets.GroupBox(body, t, "Settings", 0f, 40f, w, 160f, "Settings");
                float gw = w - t.padding * 2f;

                COC3WinWidgets.Label(group, t, "ShareNameLabel", 0f, 0f, 90f, 22f, "Share name:");
                bind.answer = COC3WinWidgets.Field(group, t, "ShareName", 90f, 0f, 0f, gw - 90f,
                    "", "", "", false);

                COC3WinWidgets.Button(group, t, "AddShare", gw - 180f, 34f, 84f, t.rowHeight, "Add");
                COC3WinWidgets.Button(group, t, "RemoveShare", gw - 90f, 34f, 84f, t.rowHeight, "Remove");

                COC3WinWidgets.Label(group, t, "LimitLabel", 0f, 72f, gw - 90f, 22f,
                    "Limit the number of simultaneous users to:");
                COC3WinWidgets.Field(group, t, "UserLimit", gw - 80f, 70f, 0f, 80f, "", "16777216", "", true);

                COC3WinWidgets.Button(body, t, "Permissions", 0f, 214f, 100f, t.rowHeight, "Permissions");
                COC3WinWidgets.Button(body, t, "Caching", 110f, 214f, 100f, t.rowHeight, "Caching");
            }
        });

        // ---- Job order and safety briefing  (step 1) ---------------------
        Add(new DialogDef
        {
            stepName = "Step_01_OHSAndJobOrder",
            title = "Job Order - CSS NC II  |  COC 3 Set-up Computer Servers",
            size = new Vector2(620f, 460f),
            buttons = new[] { "ACKNOWLEDGE" },
            submitButtonIndex = 0,
            build = (body, t, bind, w, h) =>
            {
                COC3WinWidgets.Label(body, t, "Heading", 0f, 0f, w, 26f,
                    "OCCUPATIONAL HEALTH AND SAFETY", TextAlignmentOptions.Left, true);

                COC3WinWidgets.Checkbox(body, t, "Strap", 0f, 32f, w,
                    "Anti-static wrist strap worn before handling equipment", true);
                COC3WinWidgets.Checkbox(body, t, "Power", 0f, 60f, w,
                    "Power cables secure; server shut down before any physical work", true);
                COC3WinWidgets.Checkbox(body, t, "UPS", 0f, 88f, w,
                    "UPS checked - a domain promotion must not be interrupted", true);
                COC3WinWidgets.Checkbox(body, t, "Walkway", 0f, 116f, w,
                    "Walkways clear and workstation properly ventilated", true);
                COC3WinWidgets.Checkbox(body, t, "Report", 0f, 144f, w,
                    "Unplanned events (power dips, damaged cables) to be reported", true);

                COC3WinWidgets.Label(body, t, "JobHeading", 0f, 188f, w, 26f,
                    "JOB ORDER", TextAlignmentOptions.Left, true);

                COC3WinWidgets.ListRows(body, t, "Job", 0f, 218f, w, 130f,
                    new[] { "Item", "Specification" },
                    new[]
                    {
                        new[] { "Server", COC3StepBlueprint.ServerName + "  (" + COC3StepBlueprint.ServerIP + ")" },
                        new[] { "Client", COC3StepBlueprint.ClientName + "  (DHCP)" },
                        new[] { "Domain", COC3StepBlueprint.DomainFQDN },
                        new[] { "Network", COC3StepBlueprint.NetworkId + ".0 / " + COC3StepBlueprint.SubnetMask },
                    }, -1);
            }
        });

        // ---- Test plan  (step 18) ----------------------------------------
        Add(new DialogDef
        {
            stepName = "Step_18_PrepareTestPlan",
            title = "Test Plan - COC 3 Set-up Computer Servers",
            size = new Vector2(660f, 430f),
            buttons = new[] { "BEGIN TESTING" },
            submitButtonIndex = 0,
            build = (body, t, bind, w, h) =>
            {
                COC3WinWidgets.Label(body, t, "Heading", 0f, 0f, w, 26f,
                    "Prepared before testing, as LO3 requires",
                    TextAlignmentOptions.Left, true);

                COC3WinWidgets.Label(body, t, "Blurb", 0f, 30f, w, 40f,
                    "Every test below is performed on " + COC3StepBlueprint.ClientName +
                    ". A service checked only on the server has not been tested.",
                    TextAlignmentOptions.Left, false, t.disabledText);

                COC3WinWidgets.ListRows(body, t, "Tests", 0f, 82f, w, 200f,
                    new[] { "#", "Test", "Pass criteria" },
                    new[]
                    {
                        new[] { "1", "Shared folder access", "UNC path opens from client" },
                        new[] { "2", "Shared printer", "Test page prints" },
                        new[] { "3", "Folder redirection", "Documents points at server" },
                        new[] { "4", "Remote Desktop", "Session connects" },
                        new[] { "5", "Connectivity", "Ping replies received" },
                    }, -1);
            }
        });

        // ---- Service report  (step 35) -----------------------------------
        Add(new DialogDef
        {
            stepName = "Step_35_ServiceReport",
            title = "Service Report - COC 3",
            size = new Vector2(640f, 470f),
            buttons = new[] { "SUBMIT REPORT" },
            submitButtonIndex = 0,
            build = (body, t, bind, w, h) =>
            {
                COC3WinWidgets.Label(body, t, "Heading", 0f, 0f, w, 26f,
                    "Work completed", TextAlignmentOptions.Left, true);

                COC3WinWidgets.ListRows(body, t, "Summary", 0f, 30f, w, 176f,
                    new[] { "Item", "Recorded" },
                    new[]
                    {
                        new[] { "Server name / IP", COC3StepBlueprint.ServerName + "  " + COC3StepBlueprint.ServerIP },
                        new[] { "Domain created", COC3StepBlueprint.DomainFQDN },
                        new[] { "Roles installed", "AD DS, DNS, DHCP, File, Print" },
                        new[] { "Accounts / groups", COC3StepBlueprint.SampleUser + "  /  " + COC3StepBlueprint.UserGroup },
                        new[] { "Shares", COC3StepBlueprint.ShareName + ", " + COC3StepBlueprint.RedirectShare },
                        new[] { "Tests passed", "5 of 5" },
                    }, -1);

                COC3WinWidgets.Label(body, t, "SignHeading", 0f, 222f, w, 26f,
                    "Sign-off", TextAlignmentOptions.Left, true);

                float lw = 120f, fw = w - lw - 12f;
                COC3WinWidgets.Field(body, t, "Technician", 0f, 252f, lw, fw,
                    "Technician:", "", "", true);
                COC3WinWidgets.Field(body, t, "Date", 0f, 288f, lw, fw,
                    "Date:", "", "", true);
                COC3WinWidgets.Field(body, t, "Supervisor", 0f, 324f, lw, fw,
                    "Supervisor:", "", "", true);
            }
        });
    }

    /// <summary>
    /// The Add Roles and Features wizard's Server Roles page. Steps 5, 9 and 10 are the same
    /// page with a different role as the target, so one builder covers all three - and the
    /// learner ticks the actual role checkbox rather than an invisible rectangle.
    /// </summary>
    private static DialogDef RolesPage(string stepName, int targetIndex)
    {
        return new DialogDef
        {
            stepName = stepName,
            frame = Frame.Wizard,
            title = "Add Roles and Features Wizard",
            heading = "Select server roles",
            stages = new[] { "Before You Begin", "Installation Type", "Server Selection",
                             "Server Roles", "Features", "Confirmation", "Results" },
            activeStage = 3,
            size = new Vector2(700f, 470f),
            buttons = new[] { "< Previous", "Next >", "Install", "Cancel" },
            submitButtonIndex = -1,
            build = (body, t, bind, w, h) =>
            {
                COC3WinWidgets.Label(body, t, "Prompt", 0f, 0f, w, 22f,
                    "Select one or more roles to install on the selected server.");

                COC3WinWidgets.Label(body, t, "RolesLabel", 0f, 30f, w, 22f, "Roles",
                    TextAlignmentOptions.Left, true);

                var list = COC3WinWidgets.BorderedBox(body, "RolesList", 0f, 54f, w, h - 100f,
                    t.fieldBackground, t.fieldBorder);

                var roles = new Button[10];
                float lw = w - 4f;

                roles[0] = COC3WinWidgets.CheckTreeNode(list, t, "R0", 4f, lw, 0,
                    "Active Directory Certificate Services", false);
                roles[1] = COC3WinWidgets.CheckTreeNode(list, t, "R1", 26f, lw, 0,
                    "Active Directory Domain Services", false);
                roles[2] = COC3WinWidgets.CheckTreeNode(list, t, "R2", 48f, lw, 0,
                    "DHCP Server", false);
                roles[3] = COC3WinWidgets.CheckTreeNode(list, t, "R3", 70f, lw, 0,
                    "DNS Server", false);
                roles[4] = COC3WinWidgets.CheckTreeNode(list, t, "R4", 92f, lw, 0,
                    "File and Storage Services", true, true);
                roles[5] = COC3WinWidgets.CheckTreeNode(list, t, "R5", 114f, lw, 1,
                    "File and iSCSI Services", false);
                roles[6] = COC3WinWidgets.CheckTreeNode(list, t, "R6", 136f, lw, 2,
                    "File Server", false);
                roles[7] = COC3WinWidgets.CheckTreeNode(list, t, "R7", 158f, lw, 0,
                    "Print and Document Services", false);
                roles[8] = COC3WinWidgets.CheckTreeNode(list, t, "R8", 180f, lw, 0,
                    "Remote Desktop Services", false);
                roles[9] = COC3WinWidgets.CheckTreeNode(list, t, "R9", 202f, lw, 0,
                    "Web Server (IIS)", false);

                if (targetIndex >= 0 && targetIndex < roles.Length)
                    bind.target = roles[targetIndex];
            }
        };
    }

    /// <summary>
    /// Steps 12 and 23 are the same window with a different target folder, so they share one
    /// definition rather than being copy-pasted. The address bar IS the graded field - the
    /// learner types the path exactly where Explorer shows it.
    /// </summary>
    private static DialogDef ExplorerDialog(string stepName, string folderName)
    {
        return new DialogDef
        {
            stepName = stepName,
            title = folderName,
            size = new Vector2(620f, 400f),
            buttons = new[] { "Create" },
            submitButtonIndex = 0,
            build = (body, t, bind, w, h) =>
            {
                COC3WinWidgets.Label(body, t, "Nav", 0f, 4f, 60f, 22f, "←  →  ↑");

                bind.answer = COC3WinWidgets.AddressBar(body, t, "AddressBar", 66f, 0f, w - 66f,
                    "Type the full folder path");

                COC3WinWidgets.Label(body, t, "PaneLabel", 0f, 46f, 160f, 22f, "Quick access",
                    TextAlignmentOptions.Left, true);
                COC3WinWidgets.ListView(body, t, "NavPane", 0f, 72f, 160f, h - 116f,
                    new[] { "Desktop", "Downloads", "Documents", "This PC", "Network" }, -1);

                COC3WinWidgets.ListView(body, t, "Files", 172f, 46f, w - 172f, h - 90f,
                    new[] { "Program Files", "Program Files (x86)", "Users", "Windows" }, -1);
            }
        };
    }
}
#endif