#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes the Assessment Prompt on every COC 3 step: the short task line the instruction banner
/// shows during an ASSESSMENT, in place of the Story Mode step-by-step instruction.
///
/// Tools ▸ COC 3 ▸ Assessment Prompt Writer
///
///   Story Mode   "LO2.1 - A server is found by name long before anyone knows its address.
///                 Start > System, then Change. Enter the computer name from the job order."
///   Assessment   "Change server computer name"
///
/// The prompt names the TASK, never the route to it or the value to type: in an assessment the
/// learner has to know where to go and what to enter from the job order. Steps with nothing to
/// do (read the job order, confirm a role already installed) also say to press NEXT, because
/// there is nothing else on the panel that tells them so.
///
/// NOTHING TO CHANGE AT RUN TIME. ScreenshotStep and COC3StepGate already show a step's
/// Assessment Prompt in an assessment - it was only ever empty, so every step showed the same
/// generic line ("Continue the server configuration required by the job order.").
///
/// Matched on stepId, like the Instruction Writer, so a reordered list cannot misfile one.
/// SAFE TO RE-RUN. Records one undo step. Tick "Keep prompts I edited" to leave alone any step
/// whose prompt you have already changed by hand.
/// </summary>
public class COC3AssessmentPromptWriter : EditorWindow
{
    private const string UndoLabel = "Write COC3 Assessment Prompts";

    private COC3Manager manager;
    private bool keepEdited = true;
    private string report = "";
    private Vector2 scroll;

    [MenuItem("Tools/COC 3/Assessment Prompt Writer")]
    private static void Open()
    {
        var window = GetWindow<COC3AssessmentPromptWriter>(false, "COC3 Assessment Prompts", true);
        window.minSize = new Vector2(440f, 340f);
        if (window.manager == null) window.manager = FindAnyObjectByType<COC3Manager>();
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("COC 3 - write the assessment prompts", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Sets Assessment Prompt on each step's StepData asset. This is what the instruction " +
            "banner shows during an assessment: the task, without the click path or the value.\n\n" +
            "Story Mode keeps its step-by-step Instruction Text - nothing here touches it.",
            MessageType.None);

        EditorGUILayout.Space();

        manager = (COC3Manager)EditorGUILayout.ObjectField("COC3 Manager", manager, typeof(COC3Manager), true);

        keepEdited = EditorGUILayout.Toggle(
            new GUIContent("Keep prompts I edited",
                           "Leaves a step alone if its Assessment Prompt already has text that is not " +
                           "this tool's wording - so your own changes survive a re-run."),
            keepEdited);

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(manager == null))
        {
            if (GUILayout.Button("Write assessment prompts", GUILayout.Height(30f)))
                EditorApplication.delayCall += Write;
        }

        if (manager == null)
            EditorGUILayout.HelpBox("Open the COC 3 scene and drag its COC3Manager in here.", MessageType.Warning);

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
        int written = 0, current = 0, kept = 0, unknown = 0;

        try
        {
            foreach (var step in manager.steps)
            {
                if (step == null || step.stepData == null) continue;

                string id = step.stepData.stepId;
                string prompt = PromptFor(id);

                if (string.IsNullOrEmpty(prompt))
                {
                    log.AppendLine("  " + id + " - not in this tool's list, left alone");
                    unknown++;
                    continue;
                }

                string existing = step.stepData.assessmentPrompt;

                if (existing == prompt)
                {
                    current++;
                    continue;
                }

                if (keepEdited && !string.IsNullOrEmpty(existing))
                {
                    log.AppendLine("  " + id + " - kept your prompt: \"" + existing + "\"");
                    kept++;
                    continue;
                }

                Undo.RecordObject(step.stepData, UndoLabel);
                step.stepData.assessmentPrompt = prompt;
                EditorUtility.SetDirty(step.stepData);

                log.AppendLine("  " + id + "  ->  " + prompt);
                written++;
            }

            AssetDatabase.SaveAssets();

            report = written + " written" +
                     (current > 0 ? ", " + current + " already current" : "") +
                     (kept > 0 ? ", " + kept + " kept as you edited them" : "") +
                     (unknown > 0 ? ", " + unknown + " not listed" : "") + ".\n\n" + log;

            Debug.Log("[COC3AssessmentPromptWriter] " + report);
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
    // The task each step asks for. Short, no click path, no values - the job order has those.
    // ==================================================================

    private static string PromptFor(string stepId)
    {
        switch (stepId)
        {
            case "Step_01_OHSAndJobOrder":            return "Read the job order, then press NEXT";
            case "Step_02_ComputerName":              return "Change server computer name";
            case "Step_03_StaticIPConfig":            return "Set server static IP address";
            case "Step_04_DNSConfig":                 return "Check server DNS setting, then press NEXT";
            case "Step_05_InstallRoles":              return "Install server roles";
            case "Step_06_PromoteToDomainController": return "Promote server to domain controller";
            case "Step_07_DHCPConfig":                return "Create DHCP scope";
            case "Step_08_DNSServerConfig":           return "Create DNS reverse lookup zone";
            case "Step_09_FileServicesConfig":        return "Check File Server role, then press NEXT";
            case "Step_10_OtherServices":             return "Check Print and Document Services role, then press NEXT";
            case "Step_11_JoinClientToDomain":        return "Join client to domain";
            case "Step_12_CreateUserFolders":         return "Create user data folder on server";
            case "Step_13_CreateUserAccounts":        return "Create domain user account";
            case "Step_14_AccessLevels":              return "Create security group and add user";
            case "Step_15_AccessPolicies":            return "Set password policy";
            case "Step_16_CreateSharedFolder":        return "Share user data folder";
            case "Step_17_FolderPermissions":         return "Set shared folder permissions";
            case "Step_18_PrepareTestPlan":           return "Prepare test plan, then press NEXT";
            case "Step_19_TestClientAccess":          return "Test shared folder access from client";
            case "Step_20_InstallSharedPrinter":      return "Share and publish printer";
            case "Step_21_PrinterPermissions":        return "Set printer permissions";
            case "Step_22_TestPrintFromClient":       return "Print test page from client";
            case "Step_23_CreateRedirectionFolder":   return "Create folder redirection target on server";
            case "Step_24_RedirectionPolicy":         return "Configure Documents folder redirection";
            case "Step_25_RedirectFoldersTarget":     return "Set folder redirection target path";
            case "Step_26_TestRedirectionClient":     return "Verify folder redirection on client";
            case "Step_27_EnableRDP":                 return "Enable Remote Desktop on server";
            case "Step_28_AllowedRDPUsers":           return "Allow domain user to use Remote Desktop";
            case "Step_29_ConnectFromClient":         return "Connect to server by Remote Desktop from client";
            case "Step_30_VerifyRemoteAccess":        return "Verify client-server connectivity";
            case "Step_31_FirewallCheck":             return "Allow Remote Desktop through server firewall";
            case "Step_32_UserPermissionsCheck":      return "Check user's effective access";
            case "Step_33_SharedFolderSecurityCheck": return "Remove unintended access to shared folder";
            case "Step_34_UnauthorizedAccessCheck":   return "Check security log for failed access attempts";
            case "Step_35_ServiceReport":             return "Complete service report, then press NEXT";
            default:                                  return null;
        }
    }
}
#endif
