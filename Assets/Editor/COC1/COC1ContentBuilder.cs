using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns COC1ContentBlueprint into real assets and wires them into the open COC 1 scene.
///
/// Tools > COC 1 > Content Builder
///
/// TWO BUTTONS, IN ORDER:
///   1. Build Content Assets - writes every OSInstallStageData, ISOImageData,
///      ChecklistItemData, DiagnosticTestData and DialogSequence into
///      Assets/COC1/Content/...
///   2. Wire Scene Components - drops those assets into the lists on OSInstallManager,
///      BootableUSBCreator, the three ChecklistToolManagers and DiagnosticsTestManager.
///
/// SAFE TO RUN AGAIN. Rebuilding writes over the FIELDS of assets that already exist rather
/// than deleting and recreating them, so every reference you have dragged into an Inspector
/// survives. Change the wording in the blueprint, press Build, done.
///
/// This is an Editor script: it must live under a folder called Editor
/// (Assets/Editor/COC1/), or the project will not build.
/// </summary>
public class COC1ContentBuilder : EditorWindow
{
    // Scene targets - filled by Auto-Detect, or dragged in by hand.
    private OSInstallManager osInstall;
    private BootableUSBCreator usbCreator;
    private ChecklistToolManager driversTool;
    private ChecklistToolManager appSoftwareTool;
    private ChecklistToolManager updatesTool;
    private DiagnosticsTestManager diagnostics;

    private Vector2 scroll;
    private string status = "";

    [MenuItem("Tools/COC 1/Content Builder")]
    public static void Open()
    {
        var w = GetWindow<COC1ContentBuilder>("COC 1 Content");
        if (w != null) w.minSize = new Vector2(420, 560);
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("COC 1 - OS installation and configuration", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Builds " + COC1ContentBlueprint.TotalAssets + " content assets from COC1ContentBlueprint:\n" +
            "  " + COC1ContentBlueprint.Stages.Count + " OS install stages (" +
            COC1ContentBlueprint.TotalGradedChoices + " of them graded)\n" +
            "  " + COC1ContentBlueprint.Images.Count + " ISO images\n" +
            "  " + COC1ContentBlueprint.Drivers.Count + " drivers, " +
            COC1ContentBlueprint.AppSoftware.Count + " applications, " +
            COC1ContentBlueprint.Updates.Count + " updates\n" +
            "  " + COC1ContentBlueprint.Tests.Count + " diagnostic tests\n\n" +
            "Nothing here touches assembly or disassembly.",
            MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Step 1", EditorStyles.boldLabel);

        if (GUILayout.Button("Build / Rebuild Content Assets", GUILayout.Height(30)))
            BuildContent();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Step 2 - scene components", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Open your COC 1 scene first. Auto-Detect finds these by type; the three checklist " +
            "tools are told apart by their Group Task Id, so drag them in by hand if it guesses wrong.",
            MessageType.None);

        if (GUILayout.Button("Auto-Detect Scene Components"))
            AutoDetect();

        osInstall = (OSInstallManager)EditorGUILayout.ObjectField(
            new GUIContent("OS Install Manager"), osInstall, typeof(OSInstallManager), true);
        usbCreator = (BootableUSBCreator)EditorGUILayout.ObjectField(
            new GUIContent("Bootable USB Creator"), usbCreator, typeof(BootableUSBCreator), true);
        driversTool = (ChecklistToolManager)EditorGUILayout.ObjectField(
            new GUIContent("Device Manager (drivers)"), driversTool, typeof(ChecklistToolManager), true);
        appSoftwareTool = (ChecklistToolManager)EditorGUILayout.ObjectField(
            new GUIContent("Software Center (apps)"), appSoftwareTool, typeof(ChecklistToolManager), true);
        updatesTool = (ChecklistToolManager)EditorGUILayout.ObjectField(
            new GUIContent("Windows Update (patches)"), updatesTool, typeof(ChecklistToolManager), true);
        diagnostics = (DiagnosticsTestManager)EditorGUILayout.ObjectField(
            new GUIContent("Diagnostics Test Manager"), diagnostics, typeof(DiagnosticsTestManager), true);

        EditorGUILayout.Space();
        if (GUILayout.Button("Wire Scene Components", GUILayout.Height(30)))
            WireScene();

        EditorGUILayout.Space();
        if (GUILayout.Button("Verify Setup"))
            Verify();

        if (!string.IsNullOrEmpty(status))
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(status, MessageType.None);
        }

        EditorGUILayout.EndScrollView();
    }

    // ==================================================================
    // Folders
    // ==================================================================

    private const string FolderOS = "OSInstall";
    private const string FolderISO = "ISO";
    private const string FolderDrivers = "Drivers";
    private const string FolderApps = "AppSoftware";
    private const string FolderUpdates = "Updates";
    private const string FolderTests = "Diagnostics";
    private const string FolderDialogs = "Dialogs";

    private static string Sub(string name)
    {
        return COC1ContentBlueprint.RootFolder + "/" + name;
    }

    /// <summary>Creates "Assets/A/B/C" one level at a time - AssetDatabase.CreateFolder
    /// only ever makes one level, and silently does nothing if the parent is missing.</summary>
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string[] parts = path.Split('/');
        string running = parts[0];               // "Assets"
        for (int i = 1; i < parts.Length; i++)
        {
            string next = running + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(running, parts[i]);
            running = next;
        }
    }

    private static void EnsureAllFolders()
    {
        EnsureFolder(COC1ContentBlueprint.RootFolder);
        EnsureFolder(Sub(FolderOS));
        EnsureFolder(Sub(FolderISO));
        EnsureFolder(Sub(FolderDrivers));
        EnsureFolder(Sub(FolderApps));
        EnsureFolder(Sub(FolderUpdates));
        EnsureFolder(Sub(FolderTests));
        EnsureFolder(Sub(FolderDialogs));
    }

    // ==================================================================
    // Asset helpers
    // ==================================================================

    private int created;
    private int updated;

    private T GetOrCreate<T>(string folder, string assetName) where T : ScriptableObject
    {
        string path = folder + "/" + assetName + ".asset";

        var existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null)
        {
            updated++;
            return existing;
        }

        var asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        created++;
        return asset;
    }

    /// <summary>One-line DialogSequence. Returns null for empty text so the field stays
    /// unassigned rather than pointing at an empty popup.</summary>
    private DialogSequence MakeDialog(string assetName, string speaker, string text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var seq = GetOrCreate<DialogSequence>(Sub(FolderDialogs), assetName);
        if (seq == null) return null;

        seq.lines = new List<DialogLine>
        {
            new DialogLine { speakerName = speaker, text = text }
        };

        EditorUtility.SetDirty(seq);
        return seq;
    }

    // ==================================================================
    // Step 1 - build content
    // ==================================================================

    private void BuildContent()
    {
        created = 0;
        updated = 0;

        EnsureAllFolders();

        var stageAssets = new List<OSInstallStageData>();

        foreach (var def in COC1ContentBlueprint.Stages)
        {
            var asset = GetOrCreate<OSInstallStageData>(Sub(FolderOS), def.assetName);
            if (asset == null) continue;

            asset.stage = def.stage;
            asset.displayText = def.displayText;
            asset.duration = def.duration;
            asset.requiresRebootAnimation = def.reboot;
            asset.requiresUserInput = def.userInput;
            asset.userChoices = def.choices ?? new string[0];
            asset.correctChoice = def.correct ?? "";
            asset.partitionOptions = def.partitions ?? new PartitionOption[0];

            asset.educationalDialog = MakeDialog("Dlg_Teach_" + def.assetName, "Guide", def.teachText);
            asset.wrongChoiceDialog = MakeDialog("Dlg_Wrong_" + def.assetName, "Setup", def.wrongText);

            EditorUtility.SetDirty(asset);
            stageAssets.Add(asset);
        }

        // ISO images. Only the required one carries the install sequence: writing the wrong
        // image is refused before it can start, so a decoy needs no stages of its own.
        foreach (var def in COC1ContentBlueprint.Images)
        {
            var asset = GetOrCreate<ISOImageData>(Sub(FolderISO), def.assetName);
            if (asset == null) continue;

            asset.displayName = def.displayName;
            asset.fileSizeLabel = def.sizeLabel;
            asset.installStages = def.isRequired ? stageAssets.ToArray() : new OSInstallStageData[0];

            EditorUtility.SetDirty(asset);
        }

        BuildItems(COC1ContentBlueprint.Drivers, Sub(FolderDrivers));
        BuildItems(COC1ContentBlueprint.AppSoftware, Sub(FolderApps));
        BuildItems(COC1ContentBlueprint.Updates, Sub(FolderUpdates));

        foreach (var def in COC1ContentBlueprint.Tests)
        {
            var asset = GetOrCreate<DiagnosticTestData>(Sub(FolderTests), def.assetName);
            if (asset == null) continue;

            asset.testName = def.testName;
            asset.description = def.description;
            asset.duration = def.duration;
            asset.prerequisiteTaskIds = def.prerequisites ?? new string[0];
            asset.failureHint = def.failureHint ?? "";

            EditorUtility.SetDirty(asset);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        status = "Content built.\n" +
                 created + " asset(s) created, " + updated + " updated in place.\n" +
                 "Folder: " + COC1ContentBlueprint.RootFolder + "\n\n" +
                 "Now open your COC 1 scene, Auto-Detect, and press Wire Scene Components.";
        Debug.Log("[COC1ContentBuilder] " + status);
    }

    private void BuildItems(List<COC1ContentBlueprint.ItemDef> defs, string folder)
    {
        foreach (var def in defs)
        {
            var asset = GetOrCreate<ChecklistItemData>(folder, def.assetName);
            if (asset == null) continue;

            asset.itemTaskId = def.itemTaskId;
            asset.displayName = def.displayName;
            asset.description = def.description;
            asset.duration = def.duration;
            asset.prerequisiteTaskIds = def.prerequisites ?? new string[0];
            asset.blockedHint = def.blockedHint ?? "";

            asset.educationalDialog = MakeDialog("Dlg_Teach_" + def.assetName, "Guide", def.teachText);
            asset.blockedDialog = MakeDialog("Dlg_Blocked_" + def.assetName, "Guide", def.blockedHint);

            EditorUtility.SetDirty(asset);
        }
    }

    // ==================================================================
    // Step 2 - wire the scene
    // ==================================================================

    private void AutoDetect()
    {
        osInstall = FindInScene<OSInstallManager>();
        usbCreator = FindInScene<BootableUSBCreator>();
        diagnostics = FindInScene<DiagnosticsTestManager>();

        driversTool = null;
        appSoftwareTool = null;
        updatesTool = null;

        var tools = FindAllInScene<ChecklistToolManager>();
        var unclaimed = new List<ChecklistToolManager>();

        foreach (var t in tools)
        {
            string id = ReadString(t, "groupTaskId");

            if (id == SystemConfigTaskIds.InstallDrivers && driversTool == null) driversTool = t;
            else if (id == SystemConfigTaskIds.InstallAppSoftware && appSoftwareTool == null) appSoftwareTool = t;
            else if (id == SystemConfigTaskIds.InstallUpdates && updatesTool == null) updatesTool = t;
            else unclaimed.Add(t);
        }

        // Every ChecklistToolManager ships with groupTaskId defaulted to install_drivers, so
        // in a scene where nobody changed it all three look identical. Fill the empty slots in
        // hierarchy order rather than guessing, and say so.
        foreach (var t in unclaimed)
        {
            if (driversTool == null) driversTool = t;
            else if (appSoftwareTool == null) appSoftwareTool = t;
            else if (updatesTool == null) updatesTool = t;
        }

        status = "Auto-detect:\n" +
                 Line("OS Install Manager", osInstall) +
                 Line("Bootable USB Creator", usbCreator) +
                 Line("Device Manager (drivers)", driversTool) +
                 Line("Software Center (apps)", appSoftwareTool) +
                 Line("Windows Update (patches)", updatesTool) +
                 Line("Diagnostics Test Manager", diagnostics) +
                 (unclaimed.Count > 0
                     ? "\n" + unclaimed.Count + " checklist tool(s) had no recognisable Group Task Id " +
                       "and were slotted in hierarchy order. Check the three fields above before wiring."
                     : "");
    }

    private static string Line(string label, Object o)
    {
        return "  " + label + ": " + (o != null ? o.name : "NOT FOUND") + "\n";
    }

    private void WireScene()
    {
        var problems = new List<string>();
        int wired = 0;

        // Wiring can create a dialog asset of its own (the wrong-image warning), so make sure
        // the folders exist even if someone presses this button before Build.
        EnsureAllFolders();

        // --- OS install stages, in blueprint order ---
        if (osInstall == null) problems.Add("OS Install Manager not assigned - install sequence not wired.");
        else
        {
            var stages = new List<Object>();
            foreach (var def in COC1ContentBlueprint.Stages)
            {
                var a = AssetDatabase.LoadAssetAtPath<OSInstallStageData>(
                    Sub(FolderOS) + "/" + def.assetName + ".asset");
                if (a == null) problems.Add("Missing stage asset: " + def.assetName + " - run Build first.");
                else stages.Add(a);
            }

            if (WriteList(osInstall, "stages", stages)) wired++;
        }

        // --- ISO images on the reference PC ---
        if (usbCreator == null) problems.Add("Bootable USB Creator not assigned - ISO list not wired.");
        else
        {
            var images = new List<Object>();
            ISOImageData required = null;

            foreach (var def in COC1ContentBlueprint.Images)
            {
                var a = AssetDatabase.LoadAssetAtPath<ISOImageData>(
                    Sub(FolderISO) + "/" + def.assetName + ".asset");
                if (a == null) { problems.Add("Missing ISO asset: " + def.assetName); continue; }

                images.Add(a);
                if (def.isRequired) required = a;
            }

            if (WriteList(usbCreator, "availableImages", images)) wired++;

            var so = new SerializedObject(usbCreator);
            var req = so.FindProperty("requiredImage");
            if (req != null) req.objectReferenceValue = required;

            var warn = so.FindProperty("wrongImageWarning");
            if (warn != null && warn.objectReferenceValue == null)
            {
                warn.objectReferenceValue = MakeDialog(
                    "Dlg_Wrong_ISOImage", "Guide",
                    "That is not the image on the job order. This unit is being built with " +
                    COC1ContentBlueprint.TargetImageName + ". Check the order, pick the right image, " +
                    "and write the drive again.");
            }
            so.ApplyModifiedProperties();
        }

        // --- the three checklist tools ---
        wired += WireTool(driversTool, "Device Manager", SystemConfigTaskIds.InstallDrivers,
                          COC1ContentBlueprint.Drivers, Sub(FolderDrivers), problems);
        wired += WireTool(appSoftwareTool, "Software Center", SystemConfigTaskIds.InstallAppSoftware,
                          COC1ContentBlueprint.AppSoftware, Sub(FolderApps), problems);
        wired += WireTool(updatesTool, "Windows Update", SystemConfigTaskIds.InstallUpdates,
                          COC1ContentBlueprint.Updates, Sub(FolderUpdates), problems);

        // --- diagnostics ---
        if (diagnostics == null) problems.Add("Diagnostics Test Manager not assigned - tests not wired.");
        else
        {
            var tests = new List<Object>();
            foreach (var def in COC1ContentBlueprint.Tests)
            {
                var a = AssetDatabase.LoadAssetAtPath<DiagnosticTestData>(
                    Sub(FolderTests) + "/" + def.assetName + ".asset");
                if (a == null) problems.Add("Missing test asset: " + def.assetName);
                else tests.Add(a);
            }

            if (WriteList(diagnostics, "tests", tests)) wired++;

            var so = new SerializedObject(diagnostics);
            var gid = so.FindProperty("groupTaskId");
            if (gid != null) gid.stringValue = SystemConfigTaskIds.TestComputer;
            so.ApplyModifiedProperties();
        }

        AssetDatabase.SaveAssets();
        MarkSceneDirty();

        status = "Wired " + wired + " component list(s).\n\n" +
                 (problems.Count == 0
                     ? "No problems found. Save the scene (Ctrl+S)."
                     : "PROBLEMS:\n  " + string.Join("\n  ", problems.ToArray()));

        Debug.Log("[COC1ContentBuilder] " + status);
    }

    private int WireTool(ChecklistToolManager tool, string label, string groupTaskId,
                         List<COC1ContentBlueprint.ItemDef> defs, string folder, List<string> problems)
    {
        if (tool == null)
        {
            problems.Add(label + " not assigned - its item list was not wired.");
            return 0;
        }

        var items = new List<Object>();
        foreach (var def in defs)
        {
            var a = AssetDatabase.LoadAssetAtPath<ChecklistItemData>(folder + "/" + def.assetName + ".asset");
            if (a == null) problems.Add("Missing item asset: " + def.assetName);
            else items.Add(a);
        }

        bool ok = WriteList(tool, "items", items);

        var so = new SerializedObject(tool);
        var gid = so.FindProperty("groupTaskId");
        if (gid != null) gid.stringValue = groupTaskId;
        so.ApplyModifiedProperties();

        return ok ? 1 : 0;
    }

    /// <summary>Writes a private [SerializeField] List/array on a component. Going through
    /// SerializedObject is what makes this legal at all - the field is private, and it is also
    /// what puts the change on the undo stack and marks the scene dirty.</summary>
    private static bool WriteList(Object target, string fieldName, List<Object> values)
    {
        var so = new SerializedObject(target);
        var prop = so.FindProperty(fieldName);

        if (prop == null)
        {
            Debug.LogWarning("[COC1ContentBuilder] '" + target.name + "' has no serialized field named '" +
                             fieldName + "'. Did the script change?", target);
            return false;
        }

        prop.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];

        so.ApplyModifiedProperties();
        return true;
    }

    private static string ReadString(Object target, string fieldName)
    {
        var so = new SerializedObject(target);
        var prop = so.FindProperty(fieldName);
        return prop != null ? prop.stringValue : null;
    }

    // ==================================================================
    // Verify
    // ==================================================================

    private void Verify()
    {
        var notes = new List<string>();

        int missing = 0;
        missing += CountMissing<OSInstallStageData>(Sub(FolderOS), Names(COC1ContentBlueprint.Stages));
        missing += CountMissingItems(Sub(FolderDrivers), COC1ContentBlueprint.Drivers);
        missing += CountMissingItems(Sub(FolderApps), COC1ContentBlueprint.AppSoftware);
        missing += CountMissingItems(Sub(FolderUpdates), COC1ContentBlueprint.Updates);

        if (missing > 0) notes.Add(missing + " content asset(s) missing - run Build.");

        // The one mistake that would hang the install forever.
        foreach (var def in COC1ContentBlueprint.Stages)
        {
            if (def.partitions == null || def.partitions.Length == 0) continue;
            if (!string.IsNullOrEmpty(def.correct))
                notes.Add("STAGE " + def.assetName + " has both partitionOptions and a correctChoice. " +
                          "PartitionSelectView submits the partition's own name, so that can never " +
                          "match and the stage would loop forever. Clear correctChoice.");
        }

        // Every graded stage's correct answer has to actually be one of its options.
        foreach (var def in COC1ContentBlueprint.Stages)
        {
            if (string.IsNullOrEmpty(def.correct)) continue;

            bool found = false;
            if (def.choices != null)
                foreach (var c in def.choices)
                    if (c == def.correct) found = true;

            if (!found)
                notes.Add("STAGE " + def.assetName + ": correctChoice \"" + def.correct +
                          "\" is not one of its User Choices - the learner could never pick it.");
        }

        // Prerequisites must point at ids that exist somewhere in the blueprint.
        var known = new HashSet<string>
        {
            SystemConfigTaskIds.InstallDrivers,
            SystemConfigTaskIds.InstallAppSoftware,
            SystemConfigTaskIds.InstallUpdates,
            SystemConfigTaskIds.TestComputer,
            SystemConfigTaskIds.DocumentWork
        };
        AddIds(known, COC1ContentBlueprint.Drivers);
        AddIds(known, COC1ContentBlueprint.AppSoftware);
        AddIds(known, COC1ContentBlueprint.Updates);

        CheckPrereqs(known, COC1ContentBlueprint.Drivers, notes);
        CheckPrereqs(known, COC1ContentBlueprint.AppSoftware, notes);
        CheckPrereqs(known, COC1ContentBlueprint.Updates, notes);

        foreach (var t in COC1ContentBlueprint.Tests)
        {
            if (t.prerequisites == null) continue;
            foreach (var id in t.prerequisites)
                if (!string.IsNullOrEmpty(id) && !known.Contains(id))
                    notes.Add("TEST " + t.assetName + " requires unknown task id \"" + id + "\".");
        }

        if (osInstall == null) notes.Add("Scene: OS Install Manager not assigned in this window.");
        if (usbCreator == null) notes.Add("Scene: Bootable USB Creator not assigned in this window.");
        if (diagnostics == null) notes.Add("Scene: Diagnostics Test Manager not assigned in this window.");

        status = notes.Count == 0
            ? "Verify: no problems found.\n\n" +
              COC1ContentBlueprint.Stages.Count + " stages, " +
              COC1ContentBlueprint.TotalGradedChoices + " graded decisions, " +
              (COC1ContentBlueprint.Drivers.Count + COC1ContentBlueprint.AppSoftware.Count +
               COC1ContentBlueprint.Updates.Count) + " checklist items, " +
              COC1ContentBlueprint.Tests.Count + " tests."
            : "Verify found " + notes.Count + " thing(s):\n  " + string.Join("\n  ", notes.ToArray());

        Debug.Log("[COC1ContentBuilder] " + status);
    }

    private static List<string> Names(List<COC1ContentBlueprint.StageDef> defs)
    {
        var list = new List<string>();
        foreach (var d in defs) list.Add(d.assetName);
        return list;
    }

    private static void AddIds(HashSet<string> set, List<COC1ContentBlueprint.ItemDef> defs)
    {
        foreach (var d in defs)
            if (!string.IsNullOrEmpty(d.itemTaskId)) set.Add(d.itemTaskId);
    }

    private static void CheckPrereqs(HashSet<string> known, List<COC1ContentBlueprint.ItemDef> defs,
                                     List<string> notes)
    {
        foreach (var d in defs)
        {
            if (d.prerequisites == null) continue;
            foreach (var id in d.prerequisites)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (!known.Contains(id))
                    notes.Add("ITEM " + d.assetName + " requires unknown task id \"" + id + "\".");
                if (id == d.itemTaskId)
                    notes.Add("ITEM " + d.assetName + " lists itself as its own prerequisite - it could " +
                              "never be installed.");
            }
        }
    }

    private int CountMissing<T>(string folder, List<string> names) where T : ScriptableObject
    {
        int n = 0;
        foreach (var name in names)
            if (AssetDatabase.LoadAssetAtPath<T>(folder + "/" + name + ".asset") == null) n++;
        return n;
    }

    private int CountMissingItems(string folder, List<COC1ContentBlueprint.ItemDef> defs)
    {
        int n = 0;
        foreach (var d in defs)
            if (AssetDatabase.LoadAssetAtPath<ChecklistItemData>(folder + "/" + d.assetName + ".asset") == null) n++;
        return n;
    }

    // ==================================================================
    // Scene lookups
    // ==================================================================

    /// <summary>FindObjectsOfTypeAll rather than FindAnyObjectByType, because a station's
    /// panels are usually switched off in the Hierarchy and the ordinary find skips those.
    /// Prefab assets come back from it too, hence the IsPersistent filter.</summary>
    private static List<T> FindAllInScene<T>() where T : Component
    {
        var result = new List<T>();
        foreach (var c in Resources.FindObjectsOfTypeAll<T>())
        {
            if (c == null) continue;
            if (EditorUtility.IsPersistent(c)) continue;
            if (c.hideFlags != HideFlags.None) continue;
            result.Add(c);
        }
        return result;
    }

    private static T FindInScene<T>() where T : Component
    {
        var all = FindAllInScene<T>();
        return all.Count > 0 ? all[0] : null;
    }

    private static void MarkSceneDirty()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.IsValid())
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    }
}
