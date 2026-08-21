#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// COC 3 SCENE BUILDER  -  Tools > COC3 > Scene Builder
///
/// Turns your 30 empty-ish step panels into a working "Set-up Computer Servers"
/// simulation without you having to drag 200 references by hand.
///
/// What it does, in order:
///   1. Generates (or updates) one ScreenshotStepData asset per row of COC3StepBlueprint.
///   2. Walks the children of the panel container you assign, and for EACH panel:
///        - makes sure it has a CanvasGroup and a ScreenshotStep component
///        - REUSES any screenshot Image / instruction Text / InputField / Button
///          that is already there (matched by name, then by type)
///        - CREATES only the pieces that are missing, laid out sensibly
///        - assigns the matching StepData asset
///        - wires the submit / hotspot / ping button to COC3Manager.SubmitCurrentStep
///   3. Fills COC3Manager.steps with all 30 panels, in order, and sanity-checks
///      the sub-manager references.
///
/// It is SAFE TO RUN REPEATEDLY. Nothing is deleted; existing objects are reused,
/// and duplicate persistent listeners on the buttons are cleared before rewiring.
/// Everything is registered with Undo, so Ctrl+Z reverts a build.
///
/// Put this file in a folder named "Editor" (e.g. Assets/Editor/COC3/).
/// </summary>
public class COC3SceneBuilder : EditorWindow
{
    // ------------------------------------------------------------------
    // Window state
    // ------------------------------------------------------------------
    private Transform stepPanelsParent;
    private COC3Manager manager;

    private string stepDataFolder = "Assets/COC3/StepData";
    private string panelNameFilter = "";
    private bool sortPanelsByName = false;

    private bool createMissingUI = true;
    private bool overwriteExistingAssets = true;
    private bool fixDisabledParents = true;
    private bool autoAssignCompletionScripts = true;

    private Vector2 scroll;
    private readonly List<string> report = new List<string>();

    private const string HotspotColorHint = "Hotspots are created as visible yellow boxes so you can find and " +
                                            "position them. Set their Image alpha to 0 once they sit over the " +
                                            "right part of the screenshot.";

    [MenuItem("Tools/COC3/Scene Builder")]
    public static void Open()
    {
        var win = GetWindow<COC3SceneBuilder>("COC3 Builder");
        win.minSize = new Vector2(430, 560);
        win.TryAutoFind();
    }

    /// <summary>
    /// Reports panels that cannot be shown at runtime.
    ///
    /// Only ONE case is actually broken, and it is worth being precise about which:
    ///
    ///   A panel switched off by itself is FINE. COC3Manager.ShowCurrentStep() calls
    ///   Show(), which does gameObject.SetActive(true) before touching anything else.
    ///   (This is only safe with the lazy-CanvasGroup version of ScreenshotStep -
    ///   the original cached it in Awake(), which never ran on an inactive panel.)
    ///
    ///   A panel whose PARENT is switched off is BROKEN. Show() activates the panel,
    ///   not its ancestors, so the panel stays invisible and the activity appears to
    ///   freeze on a blank screen with no error in the Console.
    ///
    /// Click any Console line to select the offending object in the Hierarchy.
    /// </summary>
    [MenuItem("Tools/COC3/Diagnose Inactive Panels")]
    public static void DiagnoseInactivePanels()
    {
        int broken = 0, benign = 0;

        foreach (var s in FindStepsInOpenScenes())
        {
            if (s.gameObject.activeInHierarchy) continue;

            var blocker = DisabledAncestorOf(s.gameObject);
            if (blocker != null)
            {
                broken++;
                Debug.LogWarning($"[COC3] BROKEN: '{s.name}' can never be shown - its parent '{blocker.name}' " +
                                 $"is switched off, and Show() only activates the panel itself. " +
                                 $"Switch '{blocker.name}' on.", blocker);
            }
            else
            {
                benign++;
            }
        }

        if (benign > 0)
            Debug.Log($"[COC3] {benign} panel(s) are switched off individually. That is FINE - COC3Manager " +
                      $"activates each one when its turn comes. Leave them off so the Scene view stays readable.");

        Debug.Log(broken == 0
            ? "[COC3] No blocking problems. Every step panel can be shown at runtime."
            : $"[COC3] {broken} panel(s) are blocked by a disabled parent - see the warnings above.");
    }

    /// <summary>
    /// Every ScreenshotStep in the open scene(s), INCLUDING inactive ones.
    ///
    /// Uses Resources.FindObjectsOfTypeAll rather than FindObjectsByType on purpose:
    /// it has never been deprecated, it has always included inactive objects, and it
    /// needs no version #if. The FindObjectsByType overloads have been churning
    /// (FindObjectsSortMode was deprecated, then FindFirstObjectByType), and each
    /// change fires obsolete warnings on newer Unity versions.
    /// </summary>
    private static List<ScreenshotStep> FindStepsInOpenScenes()
    {
        var result = new List<ScreenshotStep>();
        foreach (var s in Resources.FindObjectsOfTypeAll<ScreenshotStep>())
        {
            if (s == null) continue;
            if (EditorUtility.IsPersistent(s)) continue;          // prefab asset on disk
            if (s.hideFlags != HideFlags.None) continue;          // internal / hidden object
            if (!s.gameObject.scene.IsValid()) continue;          // not part of an open scene
            result.Add(s);
        }
        return result;
    }

    /// <summary>
    /// Undoes a build that was pointed at the wrong objects.
    ///
    /// Select the offending GameObjects in the Hierarchy (the ones sitting in
    /// COC3Manager's Steps list that are not really step panels) and run this. It
    /// removes the ScreenshotStep and CanvasGroup components, and deletes only the
    /// children whose names exactly match what the builder creates - so anything you
    /// made yourself is left alone.
    ///
    /// Everything is one undo step, so Ctrl+Z puts it all back if you mis-select.
    /// </summary>
    [MenuItem("Tools/COC3/Remove COC3 Wiring From Selection")]
    public static void RemoveWiringFromSelection()
    {
        // Exactly the names CreateImage/CreateText/CreateButton/CreateInputField use.
        // Anything the builder REUSED rather than created has a different name and
        // therefore survives.
        string[] createdNames =
        {
            "Screenshot", "InstructionLabel", "PingOutput",
            "IPInputField", "SubnetInputField", "GatewayInputField", "TextInputField",
            "SubmitButton", "NextButton", "RunPingButton",
        };

        var targets = Selection.gameObjects;
        if (targets == null || targets.Length == 0)
        {
            EditorUtility.DisplayDialog("Nothing selected",
                "Select the GameObjects you want to un-wire in the Hierarchy first.\n\n" +
                "Tip: click COC3Manager, then click each entry in its Steps list to locate them.",
                "OK");
            return;
        }

        int undoGroup = Undo.GetCurrentGroup();
        int components = 0, deleted = 0;

        foreach (var go in targets)
        {
            if (go == null) continue;

            // Delete builder-created children (walk backwards - we are removing as we go).
            for (int i = go.transform.childCount - 1; i >= 0; i--)
            {
                var child = go.transform.GetChild(i).gameObject;
                bool isCreated = child.name.StartsWith("Hotspot_", System.StringComparison.Ordinal);

                if (!isCreated)
                    foreach (var n in createdNames)
                        if (child.name == n) { isCreated = true; break; }

                if (!isCreated) continue;

                Debug.Log($"[COC3] Removing generated child '{child.name}' from '{go.name}'.");
                Undo.DestroyObjectImmediate(child);
                deleted++;
            }

            var step = go.GetComponent<ScreenshotStep>();
            if (step != null) { Undo.DestroyObjectImmediate(step); components++; }

            var cg = go.GetComponent<CanvasGroup>();
            if (cg != null) { Undo.DestroyObjectImmediate(cg); components++; }
        }

        Undo.CollapseUndoOperations(undoGroup);
        Debug.Log($"[COC3] Un-wired {targets.Length} object(s): removed {components} component(s) " +
                  $"and {deleted} generated child object(s). Ctrl+Z restores everything.");
    }

    /// <summary>
    /// Renames every panel to its blueprint row's name, in Hierarchy order.
    ///
    /// Needed whenever the blueprint gains or loses a step: inserting a new step 1 shifts
    /// all 30 existing names by one, and renaming them by hand is 35 trips to F2. The
    /// builder pairs panels to rows BY ORDER, so this only makes the names agree with what
    /// is already happening - it never changes which panel gets which content.
    ///
    /// Check the Hierarchy order first. This trusts it completely.
    /// </summary>
    private void RenamePanelsToMatchBlueprint()
    {
        report.Clear();

        var panels = CollectPanels();
        var steps = COC3StepBlueprint.Steps;

        if (panels.Count == 0) { Log("ERROR: no panels found."); Finish(); return; }

        if (panels.Count != steps.Count)
        {
            bool proceed = EditorUtility.DisplayDialog(
                "Panel count does not match",
                $"There are {panels.Count} panels but {steps.Count} blueprint steps.\n\n" +
                (panels.Count < steps.Count
                    ? $"You need {steps.Count - panels.Count} more panel(s) under '{stepPanelsParent.name}'. " +
                      "Duplicate an existing one to create them.\n\n"
                    : $"There are {panels.Count - steps.Count} panel(s) too many.\n\n") +
                $"Renaming anyway will name only the first {Mathf.Min(panels.Count, steps.Count)}.",
                "Rename anyway", "Cancel");

            if (!proceed) { Log("CANCELLED."); Finish(); return; }
        }

        int renamed = 0, alreadyCorrect = 0;

        for (int i = 0; i < panels.Count && i < steps.Count; i++)
        {
            string wanted = steps[i].assetName;
            if (panels[i].name == wanted) { alreadyCorrect++; continue; }

            Log($"[{i + 1:00}] '{panels[i].name}'  ->  '{wanted}'");
            Undo.RecordObject(panels[i].gameObject, "COC3 Rename Panel");
            panels[i].name = wanted;
            EditorUtility.SetDirty(panels[i].gameObject);
            renamed++;
        }

        Log("");
        Log($"Renamed {renamed}, already correct {alreadyCorrect}. Ctrl+Z undoes all of it.");
        Finish();
    }

    /// <summary>
    /// Finds the real step-panel container by looking for objects named Step_01_...,
    /// Step_02_... and so on, then picking whichever parent holds the most of them.
    ///
    /// This exists because assigning the Canvas instead of the panel container is an
    /// easy mistake with no obvious symptom: the builder finds the Canvas's handful of
    /// direct children, wires THOSE as if they were step panels, and the activity opens
    /// on a blank screen.
    /// </summary>
    private void AutoDetectStepPanels()
    {
        var tally = new Dictionary<Transform, int>();

        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (t == null || EditorUtility.IsPersistent(t)) continue;
            if (t.hideFlags != HideFlags.None) continue;
            if (!t.gameObject.scene.IsValid()) continue;
            if (t.parent == null) continue;
            if (!LooksLikeStepPanel(t.name)) continue;

            tally.TryGetValue(t.parent, out int n);
            tally[t.parent] = n + 1;
        }

        if (tally.Count == 0)
        {
            EditorUtility.DisplayDialog("Auto-Detect",
                "No objects named like 'Step_01_Something' were found in the open scene.\n\n" +
                "Assign the parent manually - it is the object whose direct children are your 30 panels.",
                "OK");
            return;
        }

        Transform best = null;
        int bestCount = 0;
        foreach (var pair in tally)
            if (pair.Value > bestCount) { best = pair.Key; bestCount = pair.Value; }

        stepPanelsParent = best;
        report.Clear();
        Log($"Auto-detect: '{best.name}' holds {bestCount} step panels. Assigned it as Step Panels Parent.");
        if (bestCount != COC3StepBlueprint.Steps.Count)
            Log($"NOTE: that is {bestCount}, not {COC3StepBlueprint.Steps.Count}. Check for missing or extra panels.");
        Repaint();
    }

    /// <summary>Matches "Step_01_ComputerName", "Step 1 Whatever", "step_7", etc.</summary>
    private static bool LooksLikeStepPanel(string name)
    {
        string n = name.Replace(" ", "").Replace("-", "_");
        if (!n.StartsWith("Step", System.StringComparison.OrdinalIgnoreCase)) return false;

        // Needs at least one digit right after the "Step" prefix (allowing a separator).
        int i = 4;
        while (i < n.Length && (n[i] == '_' || n[i] == '.')) i++;
        return i < n.Length && char.IsDigit(n[i]);
    }

    private void TryAutoFind()
    {
        if (manager != null) return;

        // Same reasoning as FindStepsInOpenScenes - no deprecated Find* overloads.
        foreach (var m in Resources.FindObjectsOfTypeAll<COC3Manager>())
        {
            if (m == null || EditorUtility.IsPersistent(m)) continue;
            if (m.hideFlags != HideFlags.None) continue;
            if (!m.gameObject.scene.IsValid()) continue;
            manager = m;
            return;
        }
    }

    // ------------------------------------------------------------------
    // GUI
    // ------------------------------------------------------------------
    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("COC 3 - Set-up Computer Servers", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Assign the GameObject whose CHILDREN are your 30 step panels, and your COC3Manager. " +
            "Then press \"Build Everything\".", MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Scene References", EditorStyles.boldLabel);
        stepPanelsParent = (Transform)EditorGUILayout.ObjectField(
            new GUIContent("Step Panels Parent", "The parent whose DIRECT CHILDREN are the 30 step panels (e.g. Canvas/StepsRoot). Assigning the Canvas itself is the classic mistake - it only has a handful of direct children."),
            stepPanelsParent, typeof(Transform), true);

        if (GUILayout.Button("Auto-Detect Step Panels"))
            AutoDetectStepPanels();

        using (new EditorGUI.DisabledScope(stepPanelsParent == null))
        {
            if (GUILayout.Button("Rename Panels To Match Blueprint"))
                RenamePanelsToMatchBlueprint();
        }

        // Live feedback, so a wrong parent is obvious BEFORE you press Build.
        if (stepPanelsParent != null)
        {
            int found = CollectPanels().Count;
            int wanted = COC3StepBlueprint.Steps.Count;
            if (found == wanted)
                EditorGUILayout.HelpBox($"Found {found} panels. Matches the blueprint.", MessageType.Info);
            else
                EditorGUILayout.HelpBox(
                    $"Found {found} direct children, but the blueprint has {wanted} steps.\n\n" +
                    (found < wanted
                        ? "This parent is probably too high up - the step panels are likely nested one level deeper. Press Auto-Detect."
                        : "Use the Name Filter below to exclude the extra children."),
                    MessageType.Warning);
        }
        manager = (COC3Manager)EditorGUILayout.ObjectField(
            new GUIContent("COC3 Manager", "The GameObject with the COC3Manager component."),
            manager, typeof(COC3Manager), true);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Step Data Assets", EditorStyles.boldLabel);
        stepDataFolder = EditorGUILayout.TextField(
            new GUIContent("Output Folder", "Created if it does not exist. Must start with Assets/."),
            stepDataFolder);
        overwriteExistingAssets = EditorGUILayout.Toggle(
            new GUIContent("Overwrite Content", "ON: re-applies the blueprint text/answers to existing assets. Turn OFF once you have hand-edited assets you want to keep."),
            overwriteExistingAssets);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Panel Matching", EditorStyles.boldLabel);
        panelNameFilter = EditorGUILayout.TextField(
            new GUIContent("Name Filter", "Only children whose name contains this are treated as step panels. Leave blank to use every child."),
            panelNameFilter);
        sortPanelsByName = EditorGUILayout.Toggle(
            new GUIContent("Sort By Name", "OFF = use Hierarchy order (recommended). ON = natural sort, so Step2 comes before Step10."),
            sortPanelsByName);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Build Options", EditorStyles.boldLabel);
        createMissingUI = EditorGUILayout.Toggle(
            new GUIContent("Create Missing UI", "Adds input fields / buttons / labels that a step needs but the panel does not have yet."),
            createMissingUI);
        fixDisabledParents = EditorGUILayout.Toggle(
            new GUIContent("Fix Disabled Parents", "Switches on any disabled PARENT above a step panel. Show() only activates the panel itself, so a disabled parent means that step can never appear. The panels' own on/off state is left exactly as you set it."),
            fixDisabledParents);
        autoAssignCompletionScripts = EditorGUILayout.Toggle(
            new GUIContent("Add Completion Scripts", "Adds COC3CompletionManager + COC3CompletionNotifier so finishing COC3 unlocks COC4 in the save file."),
            autoAssignCompletionScripts);

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(stepPanelsParent == null || manager == null))
        {
            if (GUILayout.Button("BUILD EVERYTHING", GUILayout.Height(38)))
                BuildEverything();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Run individually", EditorStyles.miniBoldLabel);
        if (GUILayout.Button("1. Generate Step Data assets only"))
        { report.Clear(); GenerateStepDataAssets(); Finish(); }

        using (new EditorGUI.DisabledScope(stepPanelsParent == null || manager == null))
        {
            if (GUILayout.Button("2. Wire panels only"))
            { report.Clear(); WirePanels(); Finish(); }
            if (GUILayout.Button("3. Validate setup only"))
            { report.Clear(); Validate(); Finish(); }
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(HotspotColorHint, MessageType.None);

        if (report.Count > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Report", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(string.Join("\n", report), GUILayout.MinHeight(220));
        }

        EditorGUILayout.EndScrollView();
    }

    // ------------------------------------------------------------------
    // Top-level actions
    // ------------------------------------------------------------------
    private void BuildEverything()
    {
        report.Clear();
        GenerateStepDataAssets();
        WirePanels();
        Validate();
        Finish();
    }

    private void Finish()
    {
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        if (stepPanelsParent != null)
            EditorSceneManager.MarkSceneDirty(stepPanelsParent.gameObject.scene);
        Repaint();
        Debug.Log("[COC3SceneBuilder]\n" + string.Join("\n", report));
    }

    private void Log(string line) => report.Add(line);

    // ------------------------------------------------------------------
    // 1. Step data assets
    // ------------------------------------------------------------------
    private void GenerateStepDataAssets()
    {
        Log("--- STEP DATA ASSETS ---");

        if (!stepDataFolder.StartsWith("Assets"))
        {
            Log("ERROR: Output folder must start with 'Assets/'. Aborted.");
            return;
        }

        EnsureFolder(stepDataFolder);

        int created = 0, updated = 0, skipped = 0;

        foreach (var entry in COC3StepBlueprint.Steps)
        {
            string path = $"{stepDataFolder}/{entry.assetName}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<ScreenshotStepData>(path);

            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<ScreenshotStepData>();
                AssetDatabase.CreateAsset(asset, path);
                Apply(entry, asset);
                created++;
            }
            else if (overwriteExistingAssets)
            {
                // Keep whatever screenshot the user already assigned - only the
                // text/answers/scoring come from the blueprint.
                Undo.RecordObject(asset, "COC3 Update Step Data");
                Apply(entry, asset);
                updated++;
            }
            else
            {
                skipped++;
            }
        }

        Log($"Created {created}, updated {updated}, left untouched {skipped}  (folder: {stepDataFolder})");
    }

    private static void Apply(COC3StepBlueprint.Entry e, ScreenshotStepData a)
    {
        a.stepId = e.stepId;
        a.instructionText = e.instruction;
        a.stepType = e.stepType;
        a.expectedIP = e.expectedIP;
        a.expectedSubnetMask = e.expectedSubnetMask;
        a.expectedGateway = e.expectedGateway;
        a.textFieldLabel = e.textFieldLabel;
        a.textFieldExpectedValue = e.textFieldExpectedValue;
        a.textFieldCaseSensitive = e.caseSensitive;
        a.pointsForCorrect = e.points;
        a.pointsPenaltyForMistake = e.penalty;
        EditorUtility.SetDirty(a);
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string[] parts = folder.Split('/');
        string running = parts[0]; // "Assets"
        for (int i = 1; i < parts.Length; i++)
        {
            string next = running + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(running, parts[i]);
            running = next;
        }
    }

    // ------------------------------------------------------------------
    // 2. Panel wiring
    // ------------------------------------------------------------------
    private void WirePanels()
    {
        Log("");
        Log("--- PANELS ---");

        var panels = CollectPanels();
        if (panels.Count == 0) { Log("ERROR: No panels found under " + stepPanelsParent.name); return; }

        int blueprintCount = COC3StepBlueprint.Steps.Count;

        // Stop rather than quietly wiring the wrong objects. A mismatch here almost
        // always means Step Panels Parent is pointing one level too high (the Canvas
        // instead of the panel container), and building anyway adds ScreenshotStep
        // components and stray buttons to whatever those children happen to be.
        if (panels.Count != blueprintCount)
        {
            bool proceed = EditorUtility.DisplayDialog(
                "Panel count does not match",
                $"'{stepPanelsParent.name}' has {panels.Count} matching direct children, " +
                $"but the blueprint has {blueprintCount} steps.\n\n" +
                (panels.Count < blueprintCount
                    ? "This usually means the parent is one level too high - your step panels are nested deeper. " +
                      "Cancel, then press \"Auto-Detect Step Panels\".\n\n"
                    : "Extra children are being picked up. Cancel and set a Name Filter such as \"Step\".\n\n") +
                $"Building anyway will wire only the first {Mathf.Min(panels.Count, blueprintCount)}.",
                "Build anyway", "Cancel");

            if (!proceed)
            {
                Log($"CANCELLED: '{stepPanelsParent.name}' has {panels.Count} children, expected {blueprintCount}.");
                return;
            }

            Log($"NOTE: building {Mathf.Min(panels.Count, blueprintCount)} of {blueprintCount} steps (you chose to continue).");
        }

        var built = new List<ScreenshotStep>();

        for (int i = 0; i < panels.Count && i < blueprintCount; i++)
        {
            var entry = COC3StepBlueprint.Steps[i];
            var panel = panels[i];
            var step = BuildPanel(panel, entry, i + 1);
            if (step != null) built.Add(step);
        }

        // Fill the manager's ordered list.
        Undo.RecordObject(manager, "COC3 Wire Manager");
        manager.steps = built;
        EditorUtility.SetDirty(manager);
        Log($"COC3Manager.steps filled with {built.Count} steps, in order.");

        if (autoAssignCompletionScripts)
            AttachCompletionScripts();
    }

    /// <summary>Switches on every disabled ANCESTOR of this object, leaving the object
    /// itself alone, and names each one it touched in the report rather than changing
    /// the scene silently.</summary>
    private void ActivateAncestors(GameObject go)
    {
        var chain = new List<GameObject>();
        for (Transform t = go.transform.parent; t != null; t = t.parent)
            if (!t.gameObject.activeSelf) chain.Add(t.gameObject);

        chain.Reverse(); // switch outer parents on before inner ones

        foreach (var obj in chain)
        {
            Undo.RecordObject(obj, "COC3 Activate Parent");
            obj.SetActive(true);
            EditorUtility.SetDirty(obj);
            Log($"      switched ON parent '{obj.name}' (was disabled - the step could never have been shown)");
        }
    }

    /// <summary>Returns the disabled ancestor that is keeping this object out of the
    /// hierarchy, or null if the object itself is the problem.</summary>
    private static GameObject DisabledAncestorOf(GameObject go)
    {
        for (Transform t = go.transform.parent; t != null; t = t.parent)
            if (!t.gameObject.activeSelf) return t.gameObject;
        return null;
    }

    private List<Transform> CollectPanels()
    {
        var list = new List<Transform>();
        foreach (Transform child in stepPanelsParent)
        {
            if (!string.IsNullOrEmpty(panelNameFilter) &&
                child.name.IndexOf(panelNameFilter, System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            list.Add(child);
        }

        if (sortPanelsByName)
            list = list.OrderBy(t => t.name, new NaturalComparer()).ToList();

        return list;
    }

    private ScreenshotStep BuildPanel(Transform panel, COC3StepBlueprint.Entry entry, int number)
    {
        var go = panel.gameObject;

        // Deliberately does NOT touch the panel's own activeSelf. Keeping the panels
        // switched off in the Editor is the sane way to work - COC3Manager activates
        // each one when its turn comes. Only a disabled ANCESTOR is a real problem,
        // because Show() cannot reach up the hierarchy to switch it on.
        if (fixDisabledParents && DisabledAncestorOf(go) != null)
            ActivateAncestors(go);

        // ScreenshotStep has [RequireComponent(typeof(CanvasGroup))], so adding it
        // adds the CanvasGroup too - but add it explicitly first so ordering is stable.
        if (go.GetComponent<CanvasGroup>() == null)
            Undo.AddComponent<CanvasGroup>(go);

        var step = go.GetComponent<ScreenshotStep>();
        if (step == null) step = Undo.AddComponent<ScreenshotStep>(go);

        Undo.RecordObject(step, "COC3 Wire Step");

        string dataPath = $"{stepDataFolder}/{entry.assetName}.asset";
        var data = AssetDatabase.LoadAssetAtPath<ScreenshotStepData>(dataPath);
        if (data == null) { Log($"[{number:00}] ERROR: missing asset {dataPath}"); return null; }
        step.stepData = data;

        var notes = new StringBuilder();

        // The builder pairs blueprint row N with panel N by ORDER, not by name. Since
        // the blueprint rows are named after your panels, a mismatch here almost always
        // means the Hierarchy order and the blueprint order have drifted apart - which
        // would silently put the wrong instructions on the wrong screenshot.
        if (!string.Equals(panel.name.Replace(" ", ""), entry.assetName.Replace(" ", ""),
                           System.StringComparison.OrdinalIgnoreCase))
            Log($"     WARNING: panel #{number} is named '{panel.name}' but blueprint row #{number} is " +
                $"'{entry.assetName}'. Check that your Hierarchy order matches the blueprint order.");

        // ---- screenshot image ------------------------------------------------
        if (step.screenshotDisplay == null)
        {
            step.screenshotDisplay = FindImage(panel, "screenshot", "shot", "screen", "picture", "photo", "image");
            if (step.screenshotDisplay == null && createMissingUI)
            {
                var img = CreateImage(panel, "Screenshot", new Vector2(0.05f, 0.20f), new Vector2(0.95f, 0.82f));
                img.color = new Color(0.85f, 0.85f, 0.85f, 1f);
                img.preserveAspect = true;
                img.transform.SetAsFirstSibling();   // behind the text and controls
                step.screenshotDisplay = img;
                notes.Append(" +Screenshot");
            }
        }

        // ---- instruction label ----------------------------------------------
        if (step.instructionLabel == null)
        {
            step.instructionLabel = FindText(panel, "instruction", "label", "description", "task", "text");
            if (step.instructionLabel == null && createMissingUI)
            {
                var t = CreateText(panel, "InstructionLabel", new Vector2(0.05f, 0.83f), new Vector2(0.95f, 0.98f), 26);
                t.alignment = TextAlignmentOptions.TopLeft;
                step.instructionLabel = t;
                notes.Append(" +Instruction");
            }
        }
        if (step.instructionLabel != null)
            step.instructionLabel.text = entry.instruction;   // preview it in the editor too

        // ---- per-type controls ----------------------------------------------
        switch (entry.stepType)
        {
            case StepType.NetworkConfigInput:
                step.ipInputField = ResolveField(panel, step.ipInputField, "IPInputField", "IP Address",
                                                 new Vector2(0.10f, 0.135f), new Vector2(0.65f, 0.185f), notes, "ip", "address");
                step.subnetInputField = ResolveField(panel, step.subnetInputField, "SubnetInputField", "Subnet Mask",
                                                 new Vector2(0.10f, 0.075f), new Vector2(0.65f, 0.125f), notes, "subnet", "mask");
                step.gatewayInputField = ResolveField(panel, step.gatewayInputField, "GatewayInputField", "Default Gateway",
                                                 new Vector2(0.10f, 0.015f), new Vector2(0.65f, 0.065f), notes, "gateway", "router");
                EnsureSubmitButton(panel, entry, "SubmitButton", "SUBMIT", notes);
                break;

            case StepType.TextInput:
                // NOTE the key list: "input" and "field" are deliberately NOT here. Contains()
                // is a substring test, so "input" matches "IPInputField" - which meant a
                // TextInput step landing on a panel left over from a NetworkConfigInput step
                // would quietly reuse the IP box as its answer box.
                step.textInputField = ResolveField(panel, step.textInputField, "TextInputField", entry.textFieldLabel,
                                                 new Vector2(0.10f, 0.055f), new Vector2(0.65f, 0.115f), notes,
                                                 "textinput", "answer", "textfield", "value");
                EnsureSubmitButton(panel, entry, "SubmitButton", "SUBMIT", notes);
                break;

            case StepType.ButtonInteraction:
                var hotspots = FindButtons(panel, "hotspot", "interact", "click", "target", "area");
                if (hotspots.Count == 0 && createMissingUI)
                {
                    var b = CreateHotspot(panel, "Hotspot_" + entry.stepId);
                    hotspots.Add(b);
                    notes.Append(" +Hotspot");
                }
                foreach (var b in hotspots) WireToSubmit(b);
                step.interactionButtons = hotspots.ToArray();
                break;

            case StepType.PingTest:
                EnsureSubmitButton(panel, entry, "RunPingButton", "RUN PING", notes);
                WirePingPanel(panel, notes);
                break;

            case StepType.Informational:
            default:
                EnsureSubmitButton(panel, entry, "NextButton", "NEXT", notes);
                break;
        }

        SyncUnusedControls(panel, step, entry, notes);

        EditorUtility.SetDirty(step);
        Log($"[{number:00}] {panel.name}  ->  {entry.stepId}  ({entry.stepType}){notes}");
        return step;
    }

    /// <summary>
    /// Switches off controls this step does not use, and clears their references.
    ///
    /// The builder never deletes anything - which is right, because it cannot know what you
    /// made by hand. But that means a panel keeps whatever it had from a previous build, and
    /// after a renumbering a panel often changes step TYPE. A panel that used to be a
    /// NetworkConfigInput step still carries its Subnet Mask and Default Gateway boxes, so a
    /// single-answer step renders three input fields and looks broken.
    ///
    /// Hiding rather than deleting keeps this reversible: rebuild with the old type and the
    /// same fields are found by name and switched straight back on.
    /// </summary>
    private void SyncUnusedControls(Transform panel, ScreenshotStep step, COC3StepBlueprint.Entry entry, StringBuilder notes)
    {
        bool wantsNetwork = entry.stepType == StepType.NetworkConfigInput;
        bool wantsText = entry.stepType == StepType.TextInput;

        // Stale references make ScreenshotStep.Setup() clear text on boxes this step does
        // not own, and make the Inspector misleading. Drop them.
        if (!wantsNetwork)
        {
            step.ipInputField = null;
            step.subnetInputField = null;
            step.gatewayInputField = null;
        }
        if (!wantsText) step.textInputField = null;

        int hidden = 0, shown = 0;

        foreach (var f in panel.GetComponentsInChildren<TMP_InputField>(true))
        {
            bool used = wantsNetwork
                ? (f == step.ipInputField || f == step.subnetInputField || f == step.gatewayInputField)
                : wantsText && f == step.textInputField;

            if (f.gameObject.activeSelf == used) continue;

            Undo.RecordObject(f.gameObject, "COC3 Toggle Input Field");
            f.gameObject.SetActive(used);
            EditorUtility.SetDirty(f.gameObject);
            if (used) shown++; else hidden++;
        }

        // Same story for hotspots left behind by a step that used to be ButtonInteraction.
        bool wantsHotspots = entry.stepType == StepType.ButtonInteraction;

        foreach (var b in panel.GetComponentsInChildren<Button>(true))
        {
            if (!b.name.StartsWith("Hotspot_", System.StringComparison.Ordinal)) continue;

            bool used = wantsHotspots && step.interactionButtons != null &&
                        System.Array.IndexOf(step.interactionButtons, b) >= 0;

            if (b.gameObject.activeSelf == used) continue;

            Undo.RecordObject(b.gameObject, "COC3 Toggle Hotspot");
            b.gameObject.SetActive(used);
            EditorUtility.SetDirty(b.gameObject);
            if (used) shown++; else hidden++;
        }

        if (hidden > 0) notes.Append($" -{hidden}unused");
        if (shown > 0) notes.Append($" +{shown}restored");
    }

    /// <summary>Finds an existing submit/next button (or makes one) and points it at SubmitCurrentStep.</summary>
    private void EnsureSubmitButton(Transform panel, COC3StepBlueprint.Entry entry, string createName, string label, StringBuilder notes)
    {
        var btn = FindButton(panel, "runping", "submit", "next", "check", "continue", "confirm", "proceed", "done");
        if (btn == null && createMissingUI)
        {
            btn = CreateButton(panel, createName, label, new Vector2(0.70f, 0.03f), new Vector2(0.95f, 0.13f));
            notes.Append(" +" + createName);
        }
        if (btn != null) WireToSubmit(btn);
    }

    /// <summary>Gives PingSimulation its output text + button if they are not assigned yet.</summary>
    private void WirePingPanel(Transform panel, StringBuilder notes)
    {
        var ping = manager.pingSimulation;
        if (ping == null) { notes.Append(" !NoPingSimulation"); return; }

        Undo.RecordObject(ping, "COC3 Wire Ping");

        if (ping.pingOutputText == null)
        {
            var existing = FindText(panel, "pingoutput", "output", "console", "cmd", "terminal");
            if (existing == null && createMissingUI)
            {
                existing = CreateText(panel, "PingOutput", new Vector2(0.10f, 0.24f), new Vector2(0.90f, 0.72f), 22);
                existing.alignment = TextAlignmentOptions.TopLeft;
                existing.color = new Color(0.75f, 1f, 0.75f);
                notes.Append(" +PingOutput");
            }
            ping.pingOutputText = existing;
        }

        if (ping.pingButton == null)
        {
            var b = FindButton(panel, "runping", "ping", "submit", "next");
            if (b != null) ping.pingButton = b.gameObject;
        }

        EditorUtility.SetDirty(ping);
    }

    private void WireToSubmit(Button btn)
    {
        Undo.RecordObject(btn, "COC3 Wire Button");

        // Remove any listener we (or a previous run) added that targets the manager,
        // so re-running the builder never stacks duplicates.
        for (int i = btn.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            if (btn.onClick.GetPersistentTarget(i) is COC3Manager)
                UnityEventTools.RemovePersistentListener(btn.onClick, i);
        }

        UnityEventTools.AddPersistentListener(btn.onClick, manager.SubmitCurrentStep);
        EditorUtility.SetDirty(btn);
    }

    private void AttachCompletionScripts()
    {
        var managerGO = manager.gameObject;

        if (managerGO.GetComponent<COC3CompletionManager>() == null)
        {
            var cm = Undo.AddComponent<COC3CompletionManager>(managerGO);
            cm.scoreManager = manager.scoreManager;
            EditorUtility.SetDirty(cm);
            Log("Added COC3CompletionManager to " + managerGO.name);
        }

        if (manager.completionScreen != null)
        {
            var notifier = manager.completionScreen.GetComponent<COC3CompletionNotifier>();
            if (notifier == null)
            {
                notifier = Undo.AddComponent<COC3CompletionNotifier>(manager.completionScreen);
                Log("Added COC3CompletionNotifier to " + manager.completionScreen.name);
            }
            notifier.completionManager = managerGO.GetComponent<COC3CompletionManager>();
            EditorUtility.SetDirty(notifier);
        }
        else
        {
            Log("WARNING: COC3Manager.completionScreen is empty - completion will not be saved.");
        }
    }

    // ------------------------------------------------------------------
    // 3. Validation
    // ------------------------------------------------------------------
    private void Validate()
    {
        Log("");
        Log("--- VALIDATION ---");

        if (manager.progressManager == null) Log("WARNING: progressManager not assigned (progress bar will not move).");
        if (manager.scoreManager == null) Log("WARNING: scoreManager not assigned (no scoring).");
        if (manager.feedbackManager == null) Log("WARNING: feedbackManager not assigned (no Correct/Try again popups).");
        if (manager.pingSimulation == null) Log("WARNING: pingSimulation not assigned (the PingTest step will do nothing).");
        if (manager.completionScreen == null) Log("WARNING: completionScreen not assigned (the run will end on a blank screen).");
        if (manager.reportText == null) Log("WARNING: reportText not assigned (no final report).");

        if (manager.pingSimulation != null && !manager.pingSimulation.gameObject.activeInHierarchy)
            Log("ERROR: the PingSimulation GameObject is INACTIVE. It runs a coroutine, so it must be active at scene start.");

        if (manager.feedbackManager != null && manager.feedbackManager.feedbackPanel == null)
            Log("WARNING: FeedbackManager.feedbackPanel is empty - feedback will be silently skipped.");

        int problems = 0;
        for (int i = 0; i < manager.steps.Count; i++)
        {
            var s = manager.steps[i];
            if (s == null) { Log($"ERROR: steps[{i}] is empty."); problems++; continue; }
            if (s.stepData == null) { Log($"ERROR: {s.name} has no StepData."); problems++; continue; }

            switch (s.stepData.stepType)
            {
                case StepType.NetworkConfigInput:
                    if (s.ipInputField == null || s.subnetInputField == null || s.gatewayInputField == null)
                    { Log($"ERROR: {s.name} is NetworkConfigInput but is missing an input field - this WILL throw a NullReference on submit."); problems++; }
                    break;
                case StepType.TextInput:
                    if (s.textInputField == null)
                    { Log($"ERROR: {s.name} is TextInput but has no textInputField."); problems++; }
                    break;
                case StepType.ButtonInteraction:
                    if (s.interactionButtons == null || s.interactionButtons.Length == 0)
                    { Log($"WARNING: {s.name} is ButtonInteraction but has no hotspot - the learner cannot advance."); problems++; }
                    break;
            }

            // A panel switched off by itself is fine - Show() activates it. Only a
            // disabled parent is fatal, because Show() cannot reach up the hierarchy.
            var blocker = DisabledAncestorOf(s.gameObject);
            if (blocker != null)
            {
                Log($"ERROR: {s.name} can never be shown - its parent '{blocker.name}' is switched OFF. " +
                    $"Show() only activates the panel itself. Switch '{blocker.name}' on.");
                problems++;
            }

            if (s.stepData.screenshotImage == null)
                Log($"TODO: {s.stepData.stepId} still has no screenshot sprite.");
        }

        Log(problems == 0 ? "No blocking problems found." : $"{problems} problem(s) above need attention.");
    }

    // ==================================================================
    // Finders  -  reuse whatever is already in the panel
    // ==================================================================

    private static Image FindImage(Transform panel, params string[] keys)
    {
        var candidates = panel.GetComponentsInChildren<Image>(true)
            .Where(c => c.transform != panel)                    // not the panel's own background
            .Where(c => c.GetComponent<Button>() == null)        // not a button graphic
            .Where(c => c.GetComponentInParent<TMP_InputField>() == null)
            .ToList();

        foreach (var key in keys)
        {
            var hit = candidates.FirstOrDefault(c => Contains(c.name, key));
            if (hit != null) return hit;
        }
        // Fall back to the biggest image in the panel - usually the screenshot frame.
        return candidates.OrderByDescending(c => c.rectTransform.rect.width * c.rectTransform.rect.height)
                         .FirstOrDefault();
    }

    private static TMP_Text FindText(Transform panel, params string[] keys)
    {
        var candidates = panel.GetComponentsInChildren<TMP_Text>(true)
            .Where(c => c.GetComponentInParent<Button>() == null)
            .Where(c => c.GetComponentInParent<TMP_InputField>() == null)
            .ToList();

        foreach (var key in keys)
        {
            var hit = candidates.FirstOrDefault(c => Contains(c.name, key));
            if (hit != null) return hit;
        }
        return candidates.FirstOrDefault();
    }

    /// <summary>Name-match only, on purpose. If nothing matches we would rather CREATE a
    /// clean submit button than hijack some unrelated button (a Back or Pause button)
    /// that happens to be the only one on the panel.</summary>
    private static Button FindButton(Transform panel, params string[] keys)
    {
        var candidates = panel.GetComponentsInChildren<Button>(true).ToList();
        foreach (var key in keys)
        {
            var hit = candidates.FirstOrDefault(c => Contains(c.name, key));
            if (hit != null) return hit;
        }
        return null;
    }

    private static List<Button> FindButtons(Transform panel, params string[] keys)
    {
        var all = panel.GetComponentsInChildren<Button>(true);
        var hits = all.Where(b => keys.Any(k => Contains(b.name, k))).ToList();
        if (hits.Count == 0 && all.Length == 1) hits.Add(all[0]);
        return hits;
    }

    /// <summary>Reuse an already-assigned field, else find one by name, else create it.</summary>
    private TMP_InputField ResolveField(Transform panel, TMP_InputField current, string createName, string placeholder,
                                        Vector2 min, Vector2 max, StringBuilder notes, params string[] keys)
    {
        if (current != null) { SetPlaceholder(current, placeholder); return current; }

        var all = panel.GetComponentsInChildren<TMP_InputField>(true).ToList();
        foreach (var key in keys)
        {
            var hit = all.FirstOrDefault(f => Contains(f.name, key));
            if (hit != null) { SetPlaceholder(hit, placeholder); return hit; }
        }

        if (!createMissingUI) return null;

        var made = CreateInputField(panel, createName, placeholder, min, max);
        notes.Append(" +" + createName);
        return made;
    }

    private static bool Contains(string name, string key) =>
        name.Replace(" ", "").Replace("_", "").IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0;

    private static void SetPlaceholder(TMP_InputField field, string text)
    {
        if (field.placeholder is TMP_Text p) p.text = text;
    }

    // ==================================================================
    // Creators  -  built by hand so this works on every TMP version
    // ==================================================================

    private static TMP_FontAsset DefaultFont()
    {
        var f = TMP_Settings.defaultFontAsset;
        if (f == null) f = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        return f;
    }

    private static RectTransform NewChild(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "COC3 Create " + name);
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
        return rt;
    }

    private static Image CreateImage(Transform parent, string name, Vector2 min, Vector2 max)
    {
        var rt = NewChild(parent, name, min, max);
        return rt.gameObject.AddComponent<Image>();
    }

    private static TextMeshProUGUI CreateText(Transform parent, string name, Vector2 min, Vector2 max, float size)
    {
        var rt = NewChild(parent, name, min, max);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        var font = DefaultFont();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.enableAutoSizing = true;
        t.fontSizeMin = 12;
        t.fontSizeMax = size;
        t.color = Color.white;
        t.alignment = TextAlignmentOptions.TopLeft;
        t.text = name;
        return t;
    }

    private static Button CreateButton(Transform parent, string name, string label, Vector2 min, Vector2 max)
    {
        var rt = NewChild(parent, name, min, max);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(0.16f, 0.45f, 0.78f, 1f);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;

        var t = CreateText(rt, "Label", Vector2.zero, Vector2.one, 26);
        t.text = label;
        t.alignment = TextAlignmentOptions.Center;
        t.color = Color.white;

        return btn;
    }

    private static Button CreateHotspot(Transform parent, string name)
    {
        // Deliberately visible so you can find and drag it; drop the alpha to 0 when placed.
        var rt = NewChild(parent, name, new Vector2(0.40f, 0.46f), new Vector2(0.60f, 0.54f));
        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(1f, 0.92f, 0.23f, 0.45f);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        return btn;
    }

    private static TMP_InputField CreateInputField(Transform parent, string name, string placeholder, Vector2 min, Vector2 max)
    {
        var rt = NewChild(parent, name, min, max);
        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = Color.white;

        var field = rt.gameObject.AddComponent<TMP_InputField>();
        field.targetGraphic = bg;
        field.lineType = TMP_InputField.LineType.SingleLine;

        // Text Area (viewport)
        var area = NewChild(rt, "Text Area", Vector2.zero, Vector2.one);
        area.offsetMin = new Vector2(12, 6);
        area.offsetMax = new Vector2(-12, -6);
        area.gameObject.AddComponent<RectMask2D>();

        var ph = CreateText(area, "Placeholder", Vector2.zero, Vector2.one, 24);
        ph.text = placeholder;
        ph.color = new Color(0.35f, 0.35f, 0.35f, 0.85f);
        ph.fontStyle = FontStyles.Italic;
        ph.alignment = TextAlignmentOptions.Left;
        ph.enableAutoSizing = false;

        var txt = CreateText(area, "Text", Vector2.zero, Vector2.one, 24);
        txt.text = string.Empty;
        txt.color = Color.black;
        txt.alignment = TextAlignmentOptions.Left;
        txt.enableAutoSizing = false;

        field.textViewport = area;
        field.textComponent = txt;
        field.placeholder = ph;
        var font = DefaultFont();
        if (font != null) field.fontAsset = font;
        field.pointSize = 24;

        return field;
    }

    // ------------------------------------------------------------------
    /// <summary>Sorts "Step2" before "Step10" instead of alphabetically.</summary>
    private class NaturalComparer : IComparer<string>
    {
        public int Compare(string a, string b)
        {
            int ia = 0, ib = 0;
            while (ia < a.Length && ib < b.Length)
            {
                if (char.IsDigit(a[ia]) && char.IsDigit(b[ib]))
                {
                    int sa = ia, sb = ib;
                    while (ia < a.Length && char.IsDigit(a[ia])) ia++;
                    while (ib < b.Length && char.IsDigit(b[ib])) ib++;
                    long na = long.Parse(a.Substring(sa, ia - sa));
                    long nb = long.Parse(b.Substring(sb, ib - sb));
                    if (na != nb) return na.CompareTo(nb);
                }
                else
                {
                    int c = char.ToLowerInvariant(a[ia]).CompareTo(char.ToLowerInvariant(b[ib]));
                    if (c != 0) return c;
                    ia++; ib++;
                }
            }
            return (a.Length - ia).CompareTo(b.Length - ib);
        }
    }
}
#endif