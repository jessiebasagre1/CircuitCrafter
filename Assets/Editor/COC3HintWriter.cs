#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes a hint for every COC 3 step, each one naming the exact value the step wants.
///
/// Tools ▸ COC 3 ▸ Hint Writer
///
/// WHY A TOOL AND NOT THIRTY-FIVE ASSETS BY HAND
/// COC3Tutorial.stepHintDialogs is an array indexed BY POSITION. Authoring it by hand means
/// creating 35 assets, naming them, and dropping each into the right slot - and a single slot off
/// by one gives step 14 step 13's hint, which reads as a content mistake rather than a wiring one
/// and is nearly impossible to spot. This matches on stepId and fills the slot that step actually
/// occupies, so the two cannot drift.
///
/// EXISTING ASSETS ARE UPDATED, NOT REPLACED. If a hint asset already exists at the same path its
/// lines are rewritten in place, so anything else pointing at it keeps working.
///
/// A step with no hint here is LEFT ALONE - its slot is not cleared. COC3Tutorial falls back to
/// BuildGeneratedHint() for an empty slot, which is still correct for a plain typed step.
///
/// BACKSLASHES. The text below is written exactly as the learner must type it, single backslashes
/// and all. COC3Tutorial.ForDisplay() doubles them on the way to the label, and TextMeshPro
/// collapses each pair back to one - so what is written here is what appears on screen.
/// </summary>
public class COC3HintWriter : EditorWindow
{
    private const string UndoLabel = "Write COC3 Hints";

    private COC3Tutorial tutorial;
    private string folder = "Assets/Data/COC3/Hints";
    private bool overwriteExisting = true;
    private bool fillEmptySlotsOnly;

    private string report = "";
    private Vector2 scroll;

    [MenuItem("Tools/COC 3/Hint Writer")]
    private static void Open()
    {
        GetWindow<COC3HintWriter>(false, "COC3 Hints", true).minSize = new Vector2(440f, 360f);
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("COC 3 - write the step hints", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Creates one DialogSequence per step and drops it into the matching slot on " +
            "COC3Tutorial ▸ Step Hint Dialogs.\n\n" +
            "Each hint says where the control lives and what to type, with the value in bold.\n\n" +
            "Matched on stepId, not on position, so a reordered step list cannot misfile a hint.",
            MessageType.None);

        EditorGUILayout.Space();

        tutorial = (COC3Tutorial)EditorGUILayout.ObjectField(
            new GUIContent("COC3 Tutorial", "The component holding Step Hint Dialogs. Its manager " +
                                            "supplies the step order."),
            tutorial, typeof(COC3Tutorial), true);

        folder = EditorGUILayout.TextField(
            new GUIContent("Folder", "Created if it does not exist."), folder);

        overwriteExisting = EditorGUILayout.Toggle(
            new GUIContent("Rewrite existing assets", "On: an existing hint asset has its text " +
                                                      "replaced. Off: it is left exactly as you " +
                                                      "wrote it and only the slot is filled."),
            overwriteExisting);

        fillEmptySlotsOnly = EditorGUILayout.Toggle(
            new GUIContent("Empty slots only", "Skips any step that already has something in its " +
                                               "slot, so your own hints survive."),
            fillEmptySlotsOnly);

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(tutorial == null))
        {
            if (GUILayout.Button("Write hints", GUILayout.Height(30f)))
                EditorApplication.delayCall += Write;
        }

        if (!string.IsNullOrEmpty(report))
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(report, MessageType.Info);
        }

        EditorGUILayout.EndScrollView();
    }

    // ==================================================================

    private void Write()
    {
        if (tutorial == null) return;

        var manager = tutorial.manager;
        if (manager == null || manager.steps == null || manager.steps.Count == 0)
        {
            report = "COC3Tutorial has no manager, or the manager has no steps.";
            Repaint();
            return;
        }

        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(UndoLabel);

        var log = new StringBuilder();
        int written = 0, skipped = 0, unknown = 0;

        try
        {
            EnsureFolder(folder);

            // The array is indexed by position and must be exactly as long as the step list, or
            // the last few steps silently have no slot to write into.
            var slots = tutorial.stepHintDialogs;
            if (slots == null || slots.Length != manager.steps.Count)
                System.Array.Resize(ref slots, manager.steps.Count);

            Undo.RecordObject(tutorial, UndoLabel);

            for (int i = 0; i < manager.steps.Count; i++)
            {
                var step = manager.steps[i];
                if (step == null || step.stepData == null) continue;

                string id = step.stepData.stepId;
                string body = HintFor(id);

                if (string.IsNullOrEmpty(body))
                {
                    log.AppendLine("  " + (i + 1) + ". " + id + " - no hint written");
                    unknown++;
                    continue;
                }

                if (fillEmptySlotsOnly && slots[i] != null)
                {
                    log.AppendLine("  " + (i + 1) + ". " + id + " - slot already filled, left alone");
                    skipped++;
                    continue;
                }

                slots[i] = CreateOrUpdate(id, body);
                log.AppendLine("  " + (i + 1) + ". " + id);
                written++;
            }

            tutorial.stepHintDialogs = slots;
            EditorUtility.SetDirty(tutorial);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            report = written + " hint(s) written into " + folder + ".\n" +
                     (skipped > 0 ? skipped + " slot(s) left as they were.\n" : "") +
                     (unknown > 0 ? unknown + " step(s) had no hint text here.\n" : "") +
                     "\n" + log;
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

    private DialogSequence CreateOrUpdate(string stepId, string body)
    {
        string path = folder + "/Hint_" + stepId + ".asset";

        var existing = AssetDatabase.LoadAssetAtPath<DialogSequence>(path);

        if (existing != null && !overwriteExisting) return existing;

        var sequence = existing != null ? existing : ScriptableObject.CreateInstance<DialogSequence>();

        sequence.lines = new List<DialogLine>
        {
            // The speaker name becomes the hint panel's TITLE, so it says what the step is about
            // rather than repeating the word "Hint" thirty-five times.
            new DialogLine { speakerName = "Hint", text = body }
        };

        if (existing == null) AssetDatabase.CreateAsset(sequence, path);

        EditorUtility.SetDirty(sequence);
        return sequence;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        var parts = path.Split('/');
        string built = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = built + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(built, parts[i]);
            built = next;
        }
    }

    // ==================================================================
    // The hints
    //
    // Written as the learner must type them - single backslashes. ForDisplay() doubles them and
    // TextMeshPro collapses each pair, so these arrive on screen unchanged.
    // ==================================================================

    private const string Server = "SERVER01";
    private const string Client = "CLIENT01";
    private const string Domain = "cssncii.local";

    private static string HintFor(string stepId)
    {
        switch (stepId)
        {
            case "Step_01_OHSAndJobOrder":
                return "Nothing to configure yet - this is the job order.\n\n" +
                       "Read what the client has asked for, then press <b>NEXT</b>. Everything you " +
                       "type from here comes from this page.";

            case "Step_02_ComputerName":
                return "<b>Start ▸ System</b>, then <b>Change...</b>\n\n" +
                       "Computer name:  <b>" + Server + "</b>\n\n" +
                       "A server is found by name long before anyone knows its address, so this is " +
                       "the first thing the job order fixes.";

            case "Step_03_StaticIPConfig":
                return "<b>Start ▸ Network Connections ▸ Ethernet ▸ Properties</b>, select " +
                       "<b>Internet Protocol Version 4 (TCP/IPv4)</b>, then <b>Properties</b>.\n\n" +
                       "Choose <b>Use the following IP address</b>, then:\n\n" +
                       "IP address:  <b>192.168.10.10</b>\n" +
                       "Subnet mask:  <b>255.255.255.0</b>\n" +
                       "Default gateway:  <b>192.168.10.1</b>\n\n" +
                       "A server must never lease its own address from DHCP.";

            case "Step_04_DNSConfig":
                return "Reading only - press <b>NEXT</b>.\n\n" +
                       "The preferred DNS server is <b>127.0.0.1</b>, the loopback address. This " +
                       "machine is about to host DNS itself, so it resolves names by asking itself.";

            case "Step_05_InstallRoles":
                return "<b>Server Manager</b> is on the desktop and on the Start menu.\n\n" +
                       "<b>Manage ▾</b>, or the tile \"2  Add roles and features\". On the Server " +
                       "Roles page tick <b>all five</b>:\n\n" +
                       "Active Directory Domain Services\nDHCP Server\nDNS Server\n" +
                       "File and Storage Services\nPrint and Document Services\n\n" +
                       "Then <b>Install</b>.";

            case "Step_06_PromoteToDomainController":
                return "Installing the role does not create the domain.\n\n" +
                       "Back in Server Manager, click the <b>notification flag</b> - the orange dot " +
                       "at the top right.\n\n" +
                       "Choose <b>Add a new forest</b>.\n" +
                       "Root domain name:  <b>" + Domain + "</b>";

            case "Step_07_DHCPConfig":
                return "<b>Start ▸ DHCP</b>, then click <b>IPv4</b> in the tree.\n\n" +
                       "Start IP address:  <b>192.168.10.100</b>\n" +
                       "Subnet mask:  <b>255.255.255.0</b>\n" +
                       "Router (default gateway):  <b>192.168.10.1</b>\n\n" +
                       "The pool starts at .100 because " + Server + " holds .10 - a scope that " +
                       "began lower would eventually hand a client the server's own address.";

            case "Step_08_DNSServerConfig":
                return "<b>Start ▸ DNS Manager</b>.\n\n" +
                       "Select <b>Reverse Lookup Zones</b> in the tree - promotion already created " +
                       "the forward zone, and noticing the reverse one is missing is the step.\n\n" +
                       "<b>New Zone...</b>, choose <b>IPv4 Reverse Lookup Zone</b>.\n" +
                       "Network ID:  <b>192.168.10</b>";

            case "Step_09_FileServicesConfig":
                return "Reading only - press <b>NEXT</b>.\n\n" +
                       "<b>File and Storage Services ▸ File Server</b> is what lets this machine " +
                       "host a share over SMB. You ticked it back at step 5.";

            case "Step_10_OtherServices":
                return "Reading only - press <b>NEXT</b>.\n\n" +
                       "<b>Print and Document Services</b> does the same job for printers - it is " +
                       "what lets a printer be published to the domain by name.";

            case "Step_11_JoinClientToDomain":
                return "This one happens on the client. Use the <b>" + Client + "</b> icon on the " +
                       "desktop, then <b>Change...</b>\n\n" +
                       "Select <b>Domain</b> and type:  <b>" + Domain + "</b>\n\n" +
                       "A workgroup member cannot use domain accounts or receive Group Policy, so " +
                       "nothing after this point would work.";

            case "Step_12_CreateUserFolders":
                return "<b>File Explorer</b> is on the desktop, already showing C:\\ServerData.\n\n" +
                       "<b>New folder</b>, then\nFolder name:  <b>Users</b>\n\n" +
                       "Type the folder's own name, not the whole path - the box above already " +
                       "says where it goes. The result is <b>C:\\ServerData\\Users</b>.";

            case "Step_13_CreateUserAccounts":
                return "<b>Start ▸ Active Directory Users and Computers</b>.\n\n" +
                       "Select the <b>Users</b> container in the tree first, then " +
                       "<b>New > User</b>.\n\n" +
                       "User logon name:  <b>student01</b>";

            case "Step_14_AccessLevels":
                return "Two halves, and only the second one completes the step.\n\n" +
                       "Same console, Users still selected: <b>New > Group</b>\n" +
                       "Group name:  <b>STUDENTS</b>\n" +
                       "Scope <b>Global</b>, type <b>Security</b> - only a security group can be " +
                       "given permissions.\n\n" +
                       "STUDENTS Properties opens on General. Click the <b>Members</b> tab, type " +
                       "<b>student01</b>, and press <b>Add</b>. That is the step.";

            case "Step_15_AccessPolicies":
                return "<b>Start ▸ Group Policy Management</b>.\n\n" +
                       "Computer Configuration > Policies > Windows Settings > Security Settings > " +
                       "Account Policies, then select <b>Password Policy</b> in the tree.\n\n" +
                       "Open <b>Minimum password length</b>.\n" +
                       "Tick <b>Define this policy setting</b>, then enter <b>8</b>.\n\n" +
                       "The tick box is what turns the policy on - the number alone does nothing.";

            case "Step_16_CreateSharedFolder":
                return "<b>File Explorer</b>, select the <b>Users</b> folder, then " +
                       "<b>Properties</b>.\n\n" +
                       "It opens on General. Go to the <b>Sharing</b> tab, then " +
                       "<b>Advanced Sharing...</b>\n\n" +
                       "Tick <b>Share this folder</b>.\n" +
                       "Share name:  <b>SharedDocs</b>\n\n" +
                       "The share name does not have to match the folder name on disk.";

            case "Step_17_FolderPermissions":
                return "Same property sheet, the <b>Security</b> tab - Sharing puts the folder on " +
                       "the network, Security decides who may do what with it.\n\n" +
                       "<b>Edit...</b>, select <b>STUDENTS</b> in the list, then tick " +
                       "<b>Modify</b> under Allow.\n\n" +
                       "Leave <b>Full control</b> clear. It also lets the group change these " +
                       "permissions and take ownership, which no user group should have.";

            case "Step_18_PrepareTestPlan":
                return "Reading only - press <b>NEXT</b>.\n\n" +
                       "The plan is written before the testing, so nothing is skipped and the " +
                       "result is not decided after the fact. Five tests, all performed on " +
                       Client + ".";

            case "Step_19_TestClientAccess":
                return "The full UNC path to the share:\n\n" +
                       "<b>\\\\" + Server + "\\SharedDocs</b>\n\n" +
                       "Two backslashes at the front, one in the middle. The share name is the one " +
                       "you typed at step 16, not the folder name on disk.";

            case "Step_20_InstallSharedPrinter":
                return "<b>Devices and Printers ▸ Printer Properties ▸ Sharing</b>.\n\n" +
                       "Tick <b>Share this printer</b>.\n\n" +
                       "Publishing it to the domain is what lets a client install it by name " +
                       "rather than by hunting for a driver.";

            case "Step_21_PrinterPermissions":
                return "The printer's <b>Security</b> tab.\n\n" +
                       "Grant <b>STUDENTS</b> the <b>Print</b> permission, and remove " +
                       "<b>Everyone</b>.\n\n" +
                       "Same principle as the shared folder: the right goes to the group, and " +
                       "Everyone left in place undoes the restriction you just made.";

            case "Step_22_TestPrintFromClient":
                return "On " + Client + ": connect to the shared printer, then open its " +
                       "properties and press <b>Print Test Page</b> on the General tab.\n\n" +
                       "A printer that has only been checked on the server has not been tested.";

            case "Step_23_CreateRedirectionFolder":
                return "On the server, the folder that will receive the redirected data:\n\n" +
                       "<b>C:\\ServerData\\Redirected</b>\n\n" +
                       "Separate from the SharedDocs folder on purpose - one is a share people " +
                       "open, the other is where their own documents actually live.";

            case "Step_24_RedirectionPolicy":
                return "<b>Group Policy Management Editor</b>: User Configuration > Policies > " +
                       "Windows Settings > <b>Folder Redirection</b> > <b>Documents</b>.\n\n" +
                       "Set it to <b>Basic - Redirect everyone's folder to the same location</b>.";

            case "Step_25_RedirectFoldersTarget":
                return "The root path the policy points at:\n\n" +
                       "<b>\\\\" + Server + "\\Redirected</b>\n\n" +
                       "A UNC path, not a local one - the client has to reach it over the network, " +
                       "and C:\\ on the client is not the same C:\\ as on the server.";

            case "Step_26_TestRedirectionClient":
                return "Sign in to " + Client + " as <b>student01</b> - a domain account, which " +
                       "only works because you joined the client at step 11.\n\n" +
                       "Open <b>Documents ▸ Properties ▸ Location</b> and check it now points at " +
                       "the server.";

            case "Step_27_EnableRDP":
                return "<b>System Properties ▸ Remote</b> tab.\n\n" +
                       "Select <b>Allow remote connections to this computer</b>.\n\n" +
                       "Enabling it is not the same as permitting anyone - that is the next step.";

            case "Step_28_AllowedRDPUsers":
                return "<b>Select Users...</b> on the Remote tab.\n\n" +
                       "User account:  <b>student01</b>\n\n" +
                       "Administrators already have access without being listed, which is why " +
                       "testing as an administrator proves nothing here.";

            case "Step_29_ConnectFromClient":
                return "On " + Client + ", open <b>Remote Desktop Connection</b> (mstsc).\n\n" +
                       "Computer:  <b>192.168.10.10</b>\n\n" +
                       "The server's static address - the one you set at step 3.";

            case "Step_30_VerifyRemoteAccess":
                return "Press the ping button and watch the replies.\n\n" +
                       "Nothing to type. This passes only if the addressing from step 3 was right, " +
                       "so a failure here is a fault further back, not on this page.";

            case "Step_31_FirewallCheck":
                return "<b>Windows Defender Firewall ▸ Advanced Settings ▸ Inbound Rules</b>.\n\n" +
                       "Confirm <b>Remote Desktop</b> is allowed on the profile in use.\n\n" +
                       "Switching the firewall off would also make it work, and would be the wrong " +
                       "answer - the rule is the fix, not the firewall.";

            case "Step_32_UserPermissionsCheck":
                return "On the shared folder: <b>Security ▸ Advanced ▸ Effective Access</b>.\n\n" +
                       "Select <b>student01</b> and press <b>View effective access</b>.\n\n" +
                       "This shows what the account can ACTUALLY do, after every group and " +
                       "inherited entry is resolved - which is not always what you intended.";

            case "Step_33_SharedFolderSecurityCheck":
                return "<b>Security ▸ Advanced</b> on SharedDocs.\n\n" +
                       "Read the permission entries and <b>disable inheritance</b> if " +
                       "<b>Everyone</b> still appears.\n\n" +
                       "An inherited entry from the parent folder is the usual way a share ends up " +
                       "open to people you never granted.";

            case "Step_34_UnauthorizedAccessCheck":
                return "<b>Event Viewer ▸ Windows Logs ▸ Security</b>, filtered for " +
                       "<b>Audit Failure</b>.\n\n" +
                       "A failed access attempt from an account outside STUDENTS is evidence the " +
                       "permissions are doing their job. No failures at all can mean nobody has " +
                       "tried, not that the folder is safe.";

            case "Step_35_ServiceReport":
                return "Reading only - press <b>NEXT</b>.\n\n" +
                       "The report is what the client is actually paying for: what was configured, " +
                       "what was tested, and what the result was. Undocumented work cannot be " +
                       "handed over.";

            default:
                return null;
        }
    }
}
#endif
