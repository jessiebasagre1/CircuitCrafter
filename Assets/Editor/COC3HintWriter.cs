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
            "Each hint is just the ANSWER - the values and options that step wants, in bold. " +
            "Navigation belongs on the instruction label, not here.\n\n" +
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
            // The speaker name becomes the hint panel's TITLE. These are answers, not coaching,
            // and the title should say so - a learner who opens this is asking "what goes in the
            // box", not "how do I get there". The instruction label already told them that.
            new DialogLine { speakerName = "Required input", text = body }
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
            // ---- nothing to enter ----
            case "Step_01_OHSAndJobOrder":
            case "Step_09_FileServicesConfig":
            case "Step_10_OtherServices":
            case "Step_18_PrepareTestPlan":
            case "Step_35_ServiceReport":
                return "Nothing to enter. Read the panel and press <b>NEXT</b>.";

            case "Step_30_VerifyRemoteAccess":
                return "Nothing to enter. Run the ping and watch the replies.";

            // ---- LO2: build the server ----
            case "Step_02_ComputerName":
                return "Computer name:  <b>" + Server + "</b>";

            case "Step_03_StaticIPConfig":
                return "<b>Use the following IP address</b>\n\n" +
                       "IP address:  <b>192.168.10.10</b>\n" +
                       "Subnet mask:  <b>255.255.255.0</b>\n" +
                       "Default gateway:  <b>192.168.10.1</b>";

            case "Step_04_DNSConfig":
                return "Preferred DNS server:  <b>127.0.0.1</b>";

            case "Step_05_InstallRoles":
                return "Tick all five:\n\n" +
                       "<b>Active Directory Domain Services</b>\n" +
                       "<b>DHCP Server</b>\n" +
                       "<b>DNS Server</b>\n" +
                       "<b>File and Storage Services</b>\n" +
                       "<b>Print and Document Services</b>";

            case "Step_06_PromoteToDomainController":
                return "<b>Add a new forest</b>\n\n" +
                       "Root domain name:  <b>" + Domain + "</b>";

            case "Step_07_DHCPConfig":
                return "Start IP address:  <b>192.168.10.100</b>\n" +
                       "Subnet mask:  <b>255.255.255.0</b>\n" +
                       "Router:  <b>192.168.10.1</b>";

            case "Step_08_DNSServerConfig":
                return "Tree node:  <b>Reverse Lookup Zones</b>\n" +
                       "Zone type:  <b>IPv4 Reverse Lookup Zone</b>\n\n" +
                       "Network ID:  <b>192.168.10</b>";

            case "Step_11_JoinClientToDomain":
                return "<b>Domain</b>\n\n" +
                       "Domain name:  <b>" + Domain + "</b>";

            // ---- LO1: user access ----
            case "Step_12_CreateUserFolders":
                return "Folder name:  <b>Users</b>";

            case "Step_13_CreateUserAccounts":
                return "Container:  <b>Users</b>\n\n" +
                       "User logon name:  <b>student01</b>";

            case "Step_14_AccessLevels":
                return "Group name:  <b>STUDENTS</b>\n" +
                       "Scope:  <b>Global</b>      Type:  <b>Security</b>\n\n" +
                       "Members tab, object name:  <b>student01</b>";

            case "Step_15_AccessPolicies":
                return "Tree node:  <b>Password Policy</b>\n\n" +
                       "Tick <b>Define this policy setting</b>\n" +
                       "Characters:  <b>8</b>";

            case "Step_16_CreateSharedFolder":
                return "Tick <b>Share this folder</b>\n\n" +
                       "Share name:  <b>SharedDocs</b>";

            case "Step_17_FolderPermissions":
                return "Select:  <b>STUDENTS</b>\n\n" +
                       "Tick <b>Modify</b> under Allow.\n" +
                       "Leave <b>Full control</b> clear.";

            // ---- LO3: the tests ----
            case "Step_19_TestClientAccess":
                return "Open:  <b>\\\\" + Server + "\\SharedDocs</b>";

            case "Step_20_InstallSharedPrinter":
                return "Sharing tab:\n\n" +
                       "Tick <b>Share this printer</b>\n" +
                       "Tick <b>List in the directory</b>";

            case "Step_21_PrinterPermissions":
                return "Security tab:\n\n" +
                       "Select <b>Everyone</b> and press <b>Remove</b>\n" +
                       "Select <b>STUDENTS</b>, tick <b>Print</b> under Allow\n" +
                       "Leave <b>Manage this printer</b> clear.";

            case "Step_22_TestPrintFromClient":
                return "Select:  <b>HP LaserJet 1020 on " + Server + "</b>\n\n" +
                       "Nothing to type - press <b>Print Test Page</b>.";

            case "Step_23_CreateRedirectionFolder":
                return "Folder name:  <b>Redirected</b>";

            case "Step_24_RedirectionPolicy":
                return "Tree node:  <b>User Configuration > Policies > Windows Settings > " +
                       "Folder Redirection > Documents</b>";

            case "Step_25_RedirectFoldersTarget":
                return "Setting:  <b>Basic - Redirect everyone's folder to the same location</b>\n\n" +
                       "Root Path:  <b>\\\\" + Server + "\\Redirected</b>";

            case "Step_26_TestRedirectionClient":
                return "Tab:  <b>Location</b>\n\n" +
                       "Nothing to type - select that tab and press <b>OK</b>.";

            case "Step_27_EnableRDP":
                return "Remote tab:\n\n" +
                       "<b>Allow remote connections to this computer</b>";

            case "Step_28_AllowedRDPUsers":
                return "Object name:  <b>student01</b>";

            case "Step_29_ConnectFromClient":
                return "Computer:  <b>192.168.10.10</b>";

            case "Step_31_FirewallCheck":
                return "Tree node:  <b>Inbound Rules</b>\n" +
                       "Rule:  <b>Remote Desktop - User Mode (TCP-In)</b>\n\n" +
                       "Press <b>Enable Rule</b>.";

            case "Step_32_UserPermissionsCheck":
                return "Tab:  <b>Effective Access</b>\n\n" +
                       "Select a user:  <b>student01</b>";

            case "Step_33_SharedFolderSecurityCheck":
                return "Tab:  <b>Permissions</b>\n\n" +
                       "Nothing to type - press <b>Disable inheritance</b>.";

            case "Step_34_UnauthorizedAccessCheck":
                return "Log:  <b>Security</b>\n\n" +
                       "Filter keyword:  <b>Audit Failure</b>";

            default:
                return null;
        }
    }
}
#endif