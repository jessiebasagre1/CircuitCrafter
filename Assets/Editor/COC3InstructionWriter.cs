#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes the Instruction Text on every COC 3 step, with the click path the desktop now needs.
///
/// Tools ▸ COC 3 ▸ Instruction Writer
///
/// WHY A TOOL
/// Thirteen of the thirty-five still carried the ORIGINAL blueprint wording - written when a step
/// meant typing into a labelled box. Step 7 said "In the New Scope Wizard, enter the starting
/// address" and never mentioned Start ▸ DHCP, because when it was written there was no Start menu
/// to mention. The learner is told what to fill in and not where to find it.
///
/// Fixing that by hand is thirty-five copy-pastes into thirty-five assets, in the right order,
/// without a typo. This matches on stepId, so a step cannot get another step's instruction.
///
/// SAFE TO RE-RUN. It writes the same text every time and records an undo step.
///
/// NO BACKSLASHES IN THE TEXT. TextMeshPro eats one of every pair unless Parse Escape Characters
/// is off on that specific label, and the panels these land on vary. Paths that need backslashes
/// are described in words here; the HINT carries the literal value, and the hint path handles
/// escaping properly.
/// </summary>
public class COC3InstructionWriter : EditorWindow
{
    private const string UndoLabel = "Write COC3 Instructions";

    private COC3Manager manager;
    private bool onlyEmptyOrUnchanged;
    private string report = "";
    private Vector2 scroll;

    [MenuItem("Tools/COC 3/Instruction Writer")]
    private static void Open()
    {
        GetWindow<COC3InstructionWriter>(false, "COC3 Instructions", true).minSize =
            new Vector2(440f, 340f);
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("COC 3 - write the step instructions", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Sets Instruction Text on each step's StepData asset, naming the route through the " +
            "simulated desktop.\n\n" +
            "Matched on stepId, so a reordered list cannot misfile one. Values stay in the hints; " +
            "these say where to go.",
            MessageType.None);

        EditorGUILayout.Space();

        manager = (COC3Manager)EditorGUILayout.ObjectField("COC3 Manager", manager,
                                                           typeof(COC3Manager), true);

        onlyEmptyOrUnchanged = EditorGUILayout.Toggle(
            new GUIContent("Skip steps I edited", "Leaves any step whose text is not the original " +
                                                  "blueprint wording exactly as you wrote it."),
            onlyEmptyOrUnchanged);

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(manager == null))
        {
            if (GUILayout.Button("Write instructions", GUILayout.Height(30f)))
                EditorApplication.delayCall += Write;
        }

        if (!string.IsNullOrEmpty(report))
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(report, MessageType.Info);
        }

        EditorGUILayout.EndScrollView();
    }

    private void Write()
    {
        if (manager == null || manager.steps == null) return;

        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(UndoLabel);

        var log = new StringBuilder();
        int written = 0, skipped = 0, unknown = 0;

        try
        {
            foreach (var step in manager.steps)
            {
                if (step == null || step.stepData == null) continue;

                string id = step.stepData.stepId;
                string text = TextFor(id);

                if (string.IsNullOrEmpty(text))
                {
                    log.AppendLine("  " + id + " - no text here, left alone");
                    unknown++;
                    continue;
                }

                if (onlyEmptyOrUnchanged && step.stepData.instructionText == text)
                {
                    skipped++;
                    continue;
                }

                Undo.RecordObject(step.stepData, UndoLabel);
                step.stepData.instructionText = text;
                EditorUtility.SetDirty(step.stepData);

                log.AppendLine("  " + id);
                written++;
            }

            AssetDatabase.SaveAssets();

            report = written + " written" +
                     (skipped > 0 ? ", " + skipped + " already current" : "") +
                     (unknown > 0 ? ", " + unknown + " not listed" : "") + ".\n\n" + log;
        }
        catch (System.Exception e)
        {
            report = "Failed: " + e.Message;
            Debug.LogException(e);
        }
        finally
        {
            Undo.CollapseUndoOperations(group);
            Repaint();
        }
    }

    // ==================================================================

    private const string Server = "SERVER01";
    private const string Client = "CLIENT01";

    private static string TextFor(string stepId)
    {
        switch (stepId)
        {
            case "Step_01_OHSAndJobOrder":
                return "LO2.1 - Read the job order before touching anything.\n\n" +
                       "It names every value you will be asked for. Press NEXT when you have read it.";

            case "Step_02_ComputerName":
                return "LO2.1 - A server is found by name long before anyone knows its address.\n\n" +
                       "Start > System, then Change. Enter the computer name from the job order.";

            case "Step_03_StaticIPConfig":
                return "LO2.2 - A server must never lease its own address from DHCP.\n\n" +
                       "Start > Network Connections. Open Ethernet, then Properties. Select " +
                       "Internet Protocol Version 4 (TCP/IPv4) and press Properties again.\n\n" +
                       "Choose 'Use the following IP address' and enter the static configuration.";

            case "Step_04_DNSConfig":
                return "LO2.2 - This server will host DNS itself, so it resolves names through its " +
                       "own loopback address rather than asking anyone else.\n\n" +
                       "Nothing to configure here. Press NEXT.";

            case "Step_05_InstallRoles":
                return "LO2.3 - Install every role this job order needs, in one pass.\n\n" +
                       "Server Manager is on the desktop and on the Start menu. Use Manage, or the " +
                       "tile '2 Add roles and features'.\n\n" +
                       "On the Server Roles page tick all five roles, then Install.";

            case "Step_06_PromoteToDomainController":
                return "LO2.3 - Installing the role did not create the domain. Promotion does.\n\n" +
                       "Back in Server Manager, click the notification flag at the top right.\n\n" +
                       "Add a new forest, and enter the root domain name from the job order.";

            case "Step_07_DHCPConfig":
                return "LO2.3 - Configure the scope that will serve the lab clients.\n\n" +
                       "Start > DHCP, then click IPv4 in the tree to open the New Scope Wizard.\n\n" +
                       "Enter the starting address of the pool, the subnet mask, and the router " +
                       "address handed out with each lease. The pool must start clear of the " +
                       "server's own static address.";

            case "Step_08_DNSServerConfig":
                return "LO2.3 - Promotion created the forward lookup zone automatically. The " +
                       "reverse one is not created for you.\n\n" +
                       "Start > DNS Manager. Select Reverse Lookup Zones in the tree, then New " +
                       "Zone. Choose the IPv4 reverse zone and enter the network ID.";

            case "Step_09_FileServicesConfig":
                return "LO2.3 - File and Storage Services > File Server is what lets this machine " +
                       "host a shared folder over SMB.\n\n" +
                       "You ticked it at step 5. Press NEXT.";

            case "Step_10_OtherServices":
                return "LO2.3 - Print and Document Services does the same job for printers - it is " +
                       "what allows one to be published to the domain by name.\n\n" +
                       "You ticked it at step 5. Press NEXT.";

            case "Step_11_JoinClientToDomain":
                return "LO2.4 - Walk to " + Client + " and sit down at it.\n\n" +
                       "A client that is not a domain member cannot use domain accounts or receive " +
                       "Group Policy, so nothing after this point would work.\n\n" +
                       "Start > System, then Change. Select Domain and enter the domain name.";

            case "Step_12_CreateUserFolders":
                return "LO1.1 - Back on the server. Create the folder that will hold each user's data.\n\n" +
                       "File Explorer is on the desktop, already showing the ServerData folder.\n\n" +
                       "Press New folder and type the folder's own name - not the whole path.";

            case "Step_13_CreateUserAccounts":
                return "LO1.1 - Create the domain account the learner will sign in with.\n\n" +
                       "Start > Active Directory Users and Computers.\n\n" +
                       "Select the Users container in the tree FIRST, then New > User. An account " +
                       "created elsewhere works, but nobody will find it where they expect.";

            case "Step_14_AccessLevels":
                return "LO1.2 - Access levels are assigned to groups, never to individual users.\n\n" +
                       "Same console, Users still selected: New > Group. It must be a Global " +
                       "Security group - only a security group can be given permissions.\n\n" +
                       "Creating it is not the step. Its Properties sheet opens on General; the " +
                       "MEMBERS tab is where the account goes in.";

            case "Step_15_AccessPolicies":
                return "LO1.2 - Apply the account policy the enterprise standard requires.\n\n" +
                       "Start > Group Policy Management. Under Computer Configuration > Policies > " +
                       "Windows Settings > Security Settings > Account Policies, select Password " +
                       "Policy, then open Minimum password length.\n\n" +
                       "Tick 'Define this policy setting' - the value alone does nothing.";

            case "Step_16_CreateSharedFolder":
                return "LO1.1 - Publish the folder so clients can reach it over the network.\n\n" +
                       "File Explorer, select the Users folder, then Properties.\n\n" +
                       "The sheet opens on General. Go to the SHARING tab, then Advanced Sharing.";

            case "Step_17_FolderPermissions":
                return "LO1.2 - Restrict the share to the group that needs it.\n\n" +
                       "Same property sheet, the SECURITY tab - Sharing puts the folder on the " +
                       "network, Security decides who may do what with it.\n\n" +
                       "Press Edit, select the group, and grant Modify. Not Full control.";

            case "Step_18_PrepareTestPlan":
                return "LO3.1 - PREPARE THE TEST PLAN\n\n" +
                       "A technician writes down what will be tested and what counts as a pass " +
                       "BEFORE testing, so nothing is skipped and the result is not decided after " +
                       "the fact.\n\n" +
                       "  1. Shared folder reachable from the client\n" +
                       "  2. Shared printer prints a test page from the client\n" +
                       "  3. Redirected Documents folder points at the server\n" +
                       "  4. Remote Desktop connects from the client\n" +
                       "  5. Connectivity confirmed by ping\n\n" +
                       "Each one is performed on " + Client + ", because a service checked only on " +
                       "the server has not been tested at all.\n\nPress NEXT to begin testing.";

            case "Step_19_TestClientAccess":
                return "LO3.1 - TEST 1. Walk to " + Client + ".\n\n" +
                       "Start > Run. Type the full UNC path to the shared folder and press OK.\n\n" +
                       "The test passes when the folder opens.";

            case "Step_20_InstallSharedPrinter":
                return "LO1.1 - Back to the server. Publish the printer so clients can install it " +
                       "by name.\n\n" +
                       "Start > Devices and Printers. Select the printer, open Printer properties, " +
                       "and go to the SHARING tab.\n\n" +
                       "Share it, and list it in the directory. Sharing puts it on the network; " +
                       "listing is what lets a domain client find it.";

            case "Step_21_PrinterPermissions":
                return "LO1.2 - Control who is allowed to print.\n\n" +
                       "Same property sheet, the SECURITY tab.\n\n" +
                       "Remove Everyone first, then grant the group the Print permission. A " +
                       "permission granted underneath a broader one changes nothing.";

            case "Step_22_TestPrintFromClient":
                return "LO3.1 - TEST 2. Walk to " + Client + ".\n\n" +
                       "Start > Devices and Printers. Select the printer shared from the server - " +
                       "not the local PDF printer - open Printer properties, and print a test page " +
                       "from the General tab.";

            case "Step_23_CreateRedirectionFolder":
                return "LO1.1 - Folder redirection keeps user documents on the server, not on the " +
                       "client.\n\n" +
                       "On the server: File Explorer, New folder. Create the folder that will " +
                       "receive the redirected data inside ServerData.\n\n" +
                       "Type only the folder's own name.";

            case "Step_24_RedirectionPolicy":
                return "LO1.2 - Folder redirection is a USER policy, not a computer one.\n\n" +
                       "Start > Group Policy Management. Under User Configuration > Policies > " +
                       "Windows Settings > Folder Redirection, select Documents and press " +
                       "Properties.";

            case "Step_25_RedirectFoldersTarget":
                return "LO1.2 - Point the policy at the folder you just made.\n\n" +
                       "Set it to Basic, then enter the Root Path. It must be a network path - the " +
                       "client cannot reach the server's C: drive.";

            case "Step_26_TestRedirectionClient":
                return "LO3.1 - TEST 3. Walk to " + Client + ", signed in as the domain account.\n\n" +
                       "Start > Documents, then Properties.\n\n" +
                       "Check the LOCATION tab. A redirected folder looks completely normal until " +
                       "you look there.";

            case "Step_27_EnableRDP":
                return "LO1.1 - Back to the server. Enable remote administration.\n\n" +
                       "Start > System, then the REMOTE tab.\n\n" +
                       "Allow remote connections and apply. Enabling the service is not the same " +
                       "as permitting anyone to use it.";

            case "Step_28_AllowedRDPUsers":
                return "LO1.2 - Remote Desktop must be limited to authorised accounts only.\n\n" +
                       "Same tab: Select Users, then add the domain account.\n\n" +
                       "An administrator can already connect without being listed - which is why " +
                       "testing as one would prove nothing.";

            case "Step_29_ConnectFromClient":
                return "LO3.1 - TEST 4. Walk to " + Client + ".\n\n" +
                       "Start > Remote Desktop Connection. Enter the server's address and press " +
                       "Connect.\n\n" +
                       "Sign-in is as the student account, not as an administrator.";

            case "Step_30_VerifyRemoteAccess":
                return "LO3.1 - TEST 5. Still at " + Client + ".\n\n" +
                       "Run the ping and watch the replies. Nothing to type.\n\n" +
                       "A reply proves the two machines can reach each other. It proves nothing " +
                       "above that - which is why it is the last test, not the first.";

            case "Step_31_FirewallCheck":
                return "LO3.2 - A server that blocks its own services is the commonest cause of a " +
                       "connection that should work and does not.\n\n" +
                       "Back on the server: Start > Windows Defender Firewall. Under Inbound " +
                       "Rules, find the Remote Desktop rule and enable it.\n\n" +
                       "Turning the firewall off would also make it work, and would be wrong. The " +
                       "rule is the fix, not the firewall.";

            case "Step_32_UserPermissionsCheck":
                return "LO1.3 - Verify what the user can ACTUALLY do, not just what you intended.\n\n" +
                       "File Explorer, select the Users folder, Properties > Security > Advanced, " +
                       "then the EFFECTIVE ACCESS tab.\n\n" +
                       "Check the account, not the group.";

            case "Step_33_SharedFolderSecurityCheck":
                return "LO1.3 - Confirm no unintended account inherited access to the share.\n\n" +
                       "Same window, the PERMISSIONS tab. Read where each entry came from.\n\n" +
                       "An entry inherited from the parent folder cannot be removed while " +
                       "inheritance is on - it comes straight back. Disable inheritance first.";

            case "Step_34_UnauthorizedAccessCheck":
                return "LO1.3 - Prove the restrictions actually hold.\n\n" +
                       "Start > Event Viewer. Under Windows Logs select Security, then Filter " +
                       "Current Log.\n\n" +
                       "Filter for Audit Failure. A refused attempt is evidence the permissions " +
                       "are working; the successes are thousands of entries and no finding.";

            case "Step_35_ServiceReport":
                return "LO3.3 - Record what was configured, what was tested, and what the result " +
                       "was.\n\n" +
                       "Undocumented work cannot be handed over or verified by whoever comes next. " +
                       "Press NEXT to finish.";

            default:
                return null;
        }
    }
}
#endif
