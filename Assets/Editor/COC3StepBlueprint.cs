#if UNITY_EDITOR
using System.Collections.Generic;

/// <summary>
/// THE CONTENT TABLE for COC 3 - "Set-up Computer Servers" (TESDA CSS NC II, ELC724333).
///
/// This is the ONLY file you edit to change what the steps say, what the learner has to
/// type, or how many points each step is worth. COC3SceneBuilder reads this table and
/// turns it into ScreenshotStepData assets.
///
/// 35 STEPS. The previous 30 left four gaps against the unit; this version closes them:
///
///   Step 01  OHS and job order        - every TESDA core competency carries OHS criteria
///   Step 06  Promote to domain controller \ the old list used Active Directory, Group
///   Step 11  Join client to the domain  / Policy and domain logon without ever BUILDING
///                                        the domain those depend on
///   Step 18  Prepare the test plan     - LO3 expects a plan BEFORE the testing, not just
///                                        the testing
///   Step 35  Service report            - split out of the old step 30, which was doing
///                                        both the access audit and the documentation
///
/// Two existing steps changed meaning as a result:
///   Step 05  InstallRoles     - now explicitly ticks AD DS as well as DNS and DHCP
///   Step 08  DNSServerConfig  - the forward lookup zone is created automatically by the
///                               promotion in step 06, so this is now the REVERSE lookup
///                               zone, which is not created for you
///
/// ORDER MATTERS: rows here are paired with panels top-to-bottom in the Hierarchy.
/// You now need 35 panels. Tools > COC3 > Scene Builder has a "Rename Panels To Match
/// Blueprint" button so you do not have to rename them by hand.
///
/// Mapped to the three official Learning Outcomes:
///   01-11  LO2 - Configure network services
///   12-17  LO1 - Set-up user access
///   18-30  LO1/LO3 - service delivery and testing
///   31-35  LO3 - Testing, documentation and pre-deployment procedures
///
/// LAB SCHEME - change it here and every step follows:
///     Network .............. 192.168.10.0 / 255.255.255.0
///     Router / Gateway ..... 192.168.10.1
///     Server (static) ...... 192.168.10.10   SERVER01
///     Client ............... CLIENT01 (DHCP)
///     DHCP pool ............ 192.168.10.100 - 192.168.10.200
///     Domain ............... cssncii.local
/// </summary>
public static class COC3StepBlueprint
{
    // ---- Lab constants: edit these and every step below follows automatically ----
    public const string ServerName = "SERVER01";
    public const string ClientName = "CLIENT01";
    public const string ServerIP = "192.168.10.10";
    public const string SubnetMask = "255.255.255.0";
    public const string Gateway = "192.168.10.1";
    public const string DnsServer = "127.0.0.1";
    public const string ScopeStart = "192.168.10.100";
    public const string NetworkId = "192.168.10";
    public const string DomainFQDN = "cssncii.local";
    public const string ShareName = "SharedDocs";
    public const string RedirectShare = "Redirected";
    public const string SampleUser = "student01";
    public const string UserGroup = "STUDENTS";

    /// <summary>One row of the table = one step panel.</summary>
    public class Entry
    {
        public string assetName;        // file name of the generated .asset AND the panel name
        public string stepId;           // shown in the final report log
        public string instruction;      // text shown above the screenshot
        public StepType stepType;

        // NetworkConfigInput only
        public string expectedIP = "";
        public string expectedSubnetMask = "";
        public string expectedGateway = "";

        // TextInput only
        public string textFieldLabel = "Value";
        public string textFieldExpectedValue = "";
        public bool caseSensitive = false;

        public int points = 10;
        public int penalty = 2;

        /// <summary>Reminder of which real screenshot to drop onto this step.</summary>
        public string screenshotHint = "";
    }

    // ---------------- helper constructors, purely for readability ----------------

    private static Entry Info(string asset, string text, string shot, int pts = 5) =>
        new Entry
        {
            assetName = asset,
            stepId = asset,
            instruction = text,
            stepType = StepType.Informational,
            points = pts,
            penalty = 0,
            screenshotHint = shot
        };

    private static Entry Hotspot(string asset, string text, string shot, int pts = 10) =>
        new Entry
        {
            assetName = asset,
            stepId = asset,
            instruction = text,
            stepType = StepType.ButtonInteraction,
            points = pts,
            penalty = 2,
            screenshotHint = shot
        };

    private static Entry Text(string asset, string text, string label, string expected,
                              string shot, bool cs = false, int pts = 15) =>
        new Entry
        {
            assetName = asset,
            stepId = asset,
            instruction = text,
            stepType = StepType.TextInput,
            textFieldLabel = label,
            textFieldExpectedValue = expected,
            caseSensitive = cs,
            points = pts,
            penalty = 3,
            screenshotHint = shot
        };

    private static Entry Net(string asset, string text, string ip, string mask, string gw,
                             string shot, int pts = 25) =>
        new Entry
        {
            assetName = asset,
            stepId = asset,
            instruction = text,
            stepType = StepType.NetworkConfigInput,
            expectedIP = ip,
            expectedSubnetMask = mask,
            expectedGateway = gw,
            points = pts,
            penalty = 5,
            screenshotHint = shot
        };

    private static Entry Ping(string asset, string text, string shot, int pts = 20) =>
        new Entry
        {
            assetName = asset,
            stepId = asset,
            instruction = text,
            stepType = StepType.PingTest,
            points = pts,
            penalty = 5,
            screenshotHint = shot
        };

    // ========================= THE 35 STEPS =========================

    public static readonly List<Entry> Steps = new List<Entry>
    {
        // ===== LO2: Configure network services (1-11) =======================

        // NEW - closes the OHS gap. Every TESDA core competency is assessed on safe
        // working practice, not only on the technical outcome.
        Info("Step_01_OHSAndJobOrder",
             "OCCUPATIONAL HEALTH AND SAFETY\n\nBefore touching any equipment: wear your anti-static wrist strap, check that power cables are secure and walkways are clear, and make sure the workstation is properly ventilated.\n\nRead the job order. You are setting up " + ServerName + " as the domain controller for the " + DomainFQDN + " lab network, then verifying it from " + ClientName + ".\n\nTap NEXT when you are ready to begin.",
             "OHS checklist, PPE, or a photo of the lab bench. A branded title card also works."),

        Text("Step_02_ComputerName",
             "LO2.1 - A server must be identifiable on the network before anything else.\n\nOpen System Properties > Computer Name > Change, and type the host name given in the job order.",
             "Computer Name", ServerName,
             "'Computer Name/Domain Changes' dialog with the Computer name box empty."),

        Net("Step_03_StaticIPConfig",
            "LO2.2 - A server must never lease its own address from DHCP.\n\nIn Internet Protocol Version 4 (TCP/IPv4) Properties, choose 'Use the following IP address' and enter the static configuration for " + ServerName + ".",
            ServerIP, SubnetMask, Gateway,
            "IPv4 Properties dialog with the address fields blank."),

        Text("Step_04_DNSConfig",
             "This server will host the DNS role itself, so it must resolve names through its own loopback address.\n\nEnter the Preferred DNS server.",
             "Preferred DNS Server", DnsServer,
             "Same IPv4 Properties dialog, focused on the DNS section."),

        // CHANGED - now names the three roles explicitly, AD DS included.
        Hotspot("Step_05_InstallRoles",
             "LO2.3 - Install the modules the job order requires.\n\nIn Server Manager > Add roles and features, tick all three: <b>Active Directory Domain Services</b>, <b>DNS Server</b> and <b>DHCP Server</b>, then complete the wizard.",
             "Add Roles and Features Wizard - Server Roles page. Put the hotspot over the AD DS checkbox."),

        // NEW - the domain every later step depends on has to actually be created.
        Text("Step_06_PromoteToDomainController",
             "LO2.3 - Installing AD DS does not create a domain. The server must still be promoted.\n\nClick the notification flag in Server Manager > 'Promote this server to a domain controller' > 'Add a new forest', then type the root domain name.\n\nThe server restarts afterwards and you sign back in as a domain administrator.",
             "Root Domain Name", DomainFQDN,
             "AD DS Configuration Wizard - Deployment Configuration page, root domain name box empty."),

        Net("Step_07_DHCPConfig",
            "Configure the DHCP scope that will serve the lab clients.\n\nIn the New Scope Wizard, enter the starting address of the pool, the subnet mask, and the router address handed out with each lease.\n\nNote the pool starts well clear of " + ServerIP + " - a scope must never overlap a static address.",
            ScopeStart, SubnetMask, Gateway,
            "New Scope Wizard - IP Address Range page."),

        // CHANGED - promotion already created the FORWARD zone, so this is the reverse one.
        Text("Step_08_DNSServerConfig",
             "LO2.4 - Promoting the domain created the forward lookup zone automatically, but not the reverse one, which is what lets a technician resolve an IP address back to a name.\n\nIn DNS Manager > Reverse Lookup Zones > New Zone > IPv4, type the Network ID.",
             "Network ID", NetworkId,
             "New Zone Wizard - Reverse Lookup Zone Name page."),

        Hotspot("Step_09_FileServicesConfig",
             "Enable the role that lets this server host shared folders.\n\nIn the Server Roles list, tick 'File and Storage Services' > 'File Server'.",
             "Server Roles page. Hotspot over the File Server checkbox."),

        Hotspot("Step_10_OtherServices",
             "LO2.4 - Add the remaining service the job order calls for.\n\nTick 'Print and Document Services' so the server can host the shared printer later.",
             "Server Roles page. Hotspot over the Print and Document Services checkbox."),

        // NEW - and the first CLIENT-side step. Nothing that follows works without it.
        Text("Step_11_JoinClientToDomain",
             "LO2.4 - Move to " + ClientName + ".\n\nA client that is not a domain member cannot use domain accounts, receive Group Policy, or be managed from the server - so nothing after this point would work.\n\nOn the client: System Properties > Change > select 'Domain', and type the domain name. Authenticate as the domain administrator, then restart.",
             "Domain", DomainFQDN,
             "CLIENT PC screenshot - 'Computer Name/Domain Changes' dialog, Domain radio selected."),

        // ===== LO1: Set-up user access (12-17) ==============================

        Text("Step_12_CreateUserFolders",
             "LO1.1 - Back on the server. Create the folder that will hold each user's data.\n\nIn File Explorer, create the folder and type its full path.",
             "Folder Path", @"C:\ServerData\Users",
             "File Explorer New Folder dialog on the C: drive."),

        Text("Step_13_CreateUserAccounts",
             "LO1.1 - Create the domain account the learner will sign in with.\n\nIn Active Directory Users and Computers > Users > New > User, type the User logon name.",
             "User Logon Name", SampleUser,
             "New Object - User dialog with the fields blank."),

        Hotspot("Step_14_AccessLevels",
             "LO1.2 - Access levels are assigned to groups, never to individual users.\n\nCreate the " + UserGroup + " global security group and add " + SampleUser + " as a member.",
             "New Object - Group dialog, or the group's Members tab. Hotspot over the Add button."),

        Hotspot("Step_15_AccessPolicies",
             "LO1.2 - Apply the account policy required by the enterprise standard.\n\nIn Group Policy Management Editor > Account Policies > Password Policy, open 'Minimum password length'.",
             "Group Policy Management Editor showing Password Policy. Hotspot over the policy row."),

        Text("Step_16_CreateSharedFolder",
             "LO1.1 - Publish the folder so clients can reach it over the network.\n\nRight-click the folder > Properties > Sharing > Advanced Sharing, tick 'Share this folder', and type the share name.",
             "Share Name", ShareName,
             "Advanced Sharing dialog with the Share name box empty."),

        Hotspot("Step_17_FolderPermissions",
             "LO1.2 - Restrict the share to the group that needs it.\n\nOn the Security tab, grant the " + UserGroup + " group 'Modify' permission.",
             "Security tab permissions grid. Hotspot over the Modify - Allow checkbox."),

        // ===== Testing begins (18-30) ======================================

        // NEW - LO3 asks for a test plan BEFORE the testing.
        Info("Step_18_PrepareTestPlan",
             "LO3.1 - PREPARE THE TEST PLAN\n\nA technician writes down what will be tested and what counts as a pass BEFORE testing, so nothing is skipped and the result is not decided after the fact.\n\nYour plan for this job:\n\n  1. Shared folder reachable from the client by UNC path\n  2. Shared printer prints a test page from the client\n  3. Redirected Documents folder points at the server\n  4. Remote Desktop connects from the client\n  5. Connectivity confirmed by ping\n\nEach one is performed on " + ClientName + ", because a service that has only been checked on the server has not been tested at all.\n\nTap NEXT to begin testing.",
             "A written test plan / job order form. A plain background works if you have no screenshot."),

        Text("Step_19_TestClientAccess",
             "LO3.1 - Test 1. Confirm the share is reachable from the client.\n\nIn the Run box or File Explorer address bar, type the full UNC path to the shared folder.",
             "UNC Path", @"\\" + ServerName + @"\" + ShareName,
             "CLIENT PC - Windows Run dialog, or File Explorer with an empty address bar."),

        Hotspot("Step_20_InstallSharedPrinter",
             "LO1.1 - Back to the server. Install the printer and publish it.\n\nIn Devices and Printers > Printer Properties > Sharing, tick 'Share this printer'.",
             "Printer Properties - Sharing tab. Hotspot over the 'Share this printer' checkbox."),

        Hotspot("Step_21_PrinterPermissions",
             "LO1.2 - Control who is allowed to print.\n\nOn the printer's Security tab, grant the " + UserGroup + " group the 'Print' permission and remove Everyone.",
             "Printer Properties - Security tab. Hotspot over the Print - Allow checkbox."),

        Hotspot("Step_22_TestPrintFromClient",
             "LO3.1 - Test 2. Verify the shared printer from the client side.\n\nConnect to the shared printer from " + ClientName + " and click 'Print Test Page'.",
             "CLIENT PC - Printer Properties, General tab. Hotspot over the 'Print Test Page' button."),

        Text("Step_23_CreateRedirectionFolder",
             "LO1.1 - Folder redirection keeps user documents on the server, not on the client.\n\nOn the server, create the folder that will receive the redirected data and type its full path.",
             "Folder Path", @"C:\ServerData\" + RedirectShare,
             "File Explorer New Folder dialog on the C: drive."),

        Hotspot("Step_24_RedirectionPolicy",
             "LO1.2 - Redirection is applied through Group Policy, not per machine - which is only possible because " + ClientName + " joined the domain in step 11.\n\nIn Group Policy Management Editor > User Configuration > Policies > Windows Settings > Folder Redirection, right-click 'Documents' and choose Properties.",
             "Group Policy Management Editor showing the Folder Redirection node. Hotspot over 'Documents'."),

        Text("Step_25_RedirectFoldersTarget",
             "Set where the redirected Documents folder should live.\n\nChoose 'Basic - Redirect everyone's folder to the same location' and type the Root Path.",
             "Root Path", @"\\" + ServerName + @"\" + RedirectShare,
             "Documents Properties - Target tab, Root Path box empty."),

        Hotspot("Step_26_TestRedirectionClient",
             "LO3.1 - Test 3. Confirm the policy took effect.\n\nSign in to " + ClientName + " as " + SampleUser + " - a domain account, which only works because the client is a domain member. Open Documents > Properties > Location and check it now points at the server.",
             "CLIENT PC - Documents Properties, Location tab showing the server path."),

        Hotspot("Step_27_EnableRDP",
             "LO1.1 - Enable remote administration of the server.\n\nIn System Properties > Remote, select 'Allow remote connections to this computer'.",
             "System Properties - Remote tab. Hotspot over the 'Allow remote connections' radio button."),

        Text("Step_28_AllowedRDPUsers",
             "LO1.2 - Remote Desktop must be limited to authorised accounts only.\n\nClick 'Select Users' > Add, and type the account allowed to connect remotely.",
             "User Account", SampleUser,
             "'Select Users or Groups' dialog with the object-name box empty."),

        Text("Step_29_ConnectFromClient",
             "LO3.1 - Test 4. Connect remotely from the client.\n\nOpen Remote Desktop Connection (mstsc) on " + ClientName + " and type the address of the server.",
             "Computer", ServerIP,
             "CLIENT PC - Remote Desktop Connection dialog with the Computer box empty."),

        Ping("Step_30_VerifyRemoteAccess",
             "LO3.1 - Test 5. Before blaming Remote Desktop, prove the two machines can see each other at all.\n\nOn " + ClientName + ", open Command Prompt and run:  ping " + ServerIP + "\n\nTap RUN PING to execute the test.",
             "CLIENT PC - Command Prompt. The output is drawn by PingSimulation, so an empty window works best."),

        // ===== LO3: Security check, documentation, pre-deployment (31-35) ===

        Hotspot("Step_31_FirewallCheck",
             "LO3.2 - A server that blocks its own services is the most common false fault.\n\nIn Windows Defender Firewall > Advanced Settings > Inbound Rules, confirm the File and Printer Sharing rules are enabled.",
             "Windows Defender Firewall with Advanced Security - Inbound Rules list."),

        Hotspot("Step_32_UserPermissionsCheck",
             "LO1.3 - Verify what the user can ACTUALLY do, not just what you intended.\n\nOn the shared folder: Security > Advanced > Effective Access, select " + SampleUser + " and click 'View effective access'.",
             "Advanced Security Settings - Effective Access tab. Hotspot over 'View effective access'."),

        Hotspot("Step_33_SharedFolderSecurityCheck",
             "LO1.3 - Confirm no unintended account inherited access to the share.\n\nOpen Security > Advanced on " + ShareName + " and review the permission entries. Disable inheritance if Everyone still appears.",
             "Advanced Security Settings - Permissions tab. Hotspot over 'Disable inheritance'."),

        // CHANGED - now only the audit. The report moved to its own step below.
        Hotspot("Step_34_UnauthorizedAccessCheck",
             "LO1.3 - Prove the restrictions actually hold.\n\nOpen Event Viewer > Windows Logs > Security and filter for Audit Failure entries. Any failed access attempt from an account outside " + UserGroup + " is evidence the permissions are doing their job.",
             "Event Viewer Security log showing Audit Failure entries. Hotspot over the Filter Current Log option."),

        // NEW - documentation is its own assessment deliverable, not a footnote.
        Info("Step_35_ServiceReport",
             "LO3.3 - PREPARE AND COMPLETE THE REPORT\n\nDocument the completed job on the service report form:\n\n  - Server name and IP configuration\n  - Roles installed and the domain created\n  - Accounts, groups and share permissions\n  - Printer and folder redirection settings\n  - Result of each of the five tests from your test plan\n  - Date, technician name, and supervisor's signature\n\nAn undocumented job is an incomplete job - the report is what the client and the assessor actually receive.\n\nTap NEXT to see your final score.",
             "A filled-in TESDA service report / job order form."),
    };
}
#endif