#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// COC3 SCREENSHOT ASSIGNER  -  Tools > COC3 > Assign Screenshots
///
/// Bulk-fills the "Screenshot Image" field on all 30 ScreenshotStepData assets
/// from a folder of imported sprites, instead of dragging them one at a time.
///
/// WHY THIS EXISTS AT ALL - the field that matters:
///
///   The sprite must go on the ScreenshotStepData ASSET, not on the panel's
///   Image component. ScreenshotStep.Setup() runs every time a step becomes
///   active and does:
///
///       screenshotDisplay.sprite = stepData.screenshotImage;
///
///   So whatever you drop into the Image component's "Source Image" slot gets
///   overwritten the moment the activity starts - and if the StepData's field is
///   empty, it is overwritten with NOTHING, which is why the picture vanishes.
///   Setting it on the asset is the only assignment that survives.
///
/// Two matching modes:
///   By Name  - sprite file name starts with the same Step_NN prefix
///              (Step_01_whatever.png -> Step_01_ComputerName)
///   In Order - natural-sorts the folder and pairs 1st sprite to 1st step, etc.
///              Use this when your files are named 01.png, 02.png, ...
///
/// Always press PREVIEW first - it shows every pairing without changing anything.
/// </summary>
public class COC3ScreenshotAssigner : EditorWindow
{
    private enum MatchMode { ByName, InOrder }

    private string screenshotFolder = "Assets/COC3/Screenshots";
    private string stepDataFolder = "Assets/COC3/StepData";
    private MatchMode mode = MatchMode.ByName;
    private bool skipAlreadyAssigned = false;

    private Vector2 scroll;
    private readonly List<string> report = new List<string>();

    [MenuItem("Tools/COC3/Assign Screenshots")]
    public static void Open()
    {
        var win = GetWindow<COC3ScreenshotAssigner>("COC3 Screenshots");
        win.minSize = new Vector2(430, 480);
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("Assign Screenshots to Step Data", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "The sprite belongs on the ScreenshotStepData ASSET, not on the panel's Image " +
            "component. ScreenshotStep.Setup() overwrites the Image's sprite from the asset " +
            "every time the step is shown - that is why a sprite dropped on the Image vanishes.",
            MessageType.Info);

        EditorGUILayout.Space();

        EditorGUILayout.BeginHorizontal();
        screenshotFolder = EditorGUILayout.TextField(
            new GUIContent("Screenshots Folder", "Folder containing your imported screenshot sprites."),
            screenshotFolder);
        if (GUILayout.Button("Browse", GUILayout.Width(64)))
        {
            string picked = EditorUtility.OpenFolderPanel("Screenshots folder", screenshotFolder, "");
            if (!string.IsNullOrEmpty(picked)) screenshotFolder = ToProjectPath(picked);
        }
        EditorGUILayout.EndHorizontal();

        stepDataFolder = EditorGUILayout.TextField(
            new GUIContent("Step Data Folder", "Where the builder put the 30 .asset files."),
            stepDataFolder);

        mode = (MatchMode)EditorGUILayout.EnumPopup(
            new GUIContent("Match By", "By Name: sprite file starts with the same Step_NN prefix. In Order: pair them 1-to-1 after a natural sort."),
            mode);

        skipAlreadyAssigned = EditorGUILayout.Toggle(
            new GUIContent("Keep Existing", "ON: leave steps that already have a screenshot alone."),
            skipAlreadyAssigned);

        EditorGUILayout.Space();

        if (GUILayout.Button("PREVIEW (changes nothing)", GUILayout.Height(28)))
            Run(false);

        if (GUILayout.Button("ASSIGN", GUILayout.Height(34)))
            Run(true);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("If no sprites are found", EditorStyles.miniBoldLabel);
        EditorGUILayout.HelpBox(
            "Unity only treats an image as a Sprite if its Texture Type is set to " +
            "'Sprite (2D and UI)'. Imported PNGs sometimes default to 'Default' instead, " +
            "and then nothing here can see them.", MessageType.None);

        if (GUILayout.Button("Fix Texture Type on every image in that folder"))
            FixTextureTypes();

        if (report.Count > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Report", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(string.Join("\n", report), GUILayout.MinHeight(200));
        }

        EditorGUILayout.EndScrollView();
    }

    // ------------------------------------------------------------------

    private void Run(bool apply)
    {
        report.Clear();

        var steps = LoadStepAssets();
        if (steps.Count == 0)
        {
            Log($"ERROR: no ScreenshotStepData assets found in '{stepDataFolder}'.");
            Log("Run Tools > COC3 > Scene Builder first, or correct the folder path.");
            Finish();
            return;
        }

        var sprites = LoadSprites();
        if (sprites.Count == 0)
        {
            Log($"ERROR: no sprites found in '{screenshotFolder}'.");
            Log("Either the folder is wrong, or the images are not imported as sprites -");
            Log("press 'Fix Texture Type on every image in that folder' below.");
            Finish();
            return;
        }

        Log($"{steps.Count} step assets, {sprites.Count} sprites, matching {mode}.");
        Log(apply ? "--- ASSIGNING ---" : "--- PREVIEW ONLY, nothing changed ---");

        int assigned = 0, skipped = 0, unmatched = 0;

        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];

            if (skipAlreadyAssigned && step.screenshotImage != null)
            {
                Log($"[{i + 1:00}] {step.name}  ->  kept '{step.screenshotImage.name}'");
                skipped++;
                continue;
            }

            Sprite match = mode == MatchMode.ByName
                ? MatchByName(step.name, sprites)
                : (i < sprites.Count ? sprites[i] : null);

            if (match == null)
            {
                Log($"[{i + 1:00}] {step.name}  ->  NO MATCH");
                unmatched++;
                continue;
            }

            Log($"[{i + 1:00}] {step.name}  ->  {match.name}");

            if (apply)
            {
                Undo.RecordObject(step, "Assign COC3 Screenshot");
                step.screenshotImage = match;
                EditorUtility.SetDirty(step);
            }
            assigned++;
        }

        Log("");
        Log(apply
            ? $"Assigned {assigned}, kept {skipped}, unmatched {unmatched}."
            : $"Would assign {assigned}, keep {skipped}, leave {unmatched} unmatched.");

        if (unmatched > 0 && mode == MatchMode.ByName)
            Log("TIP: rename your files to start with the same Step_NN prefix, or switch Match By to 'In Order'.");

        if (apply) { AssetDatabase.SaveAssets(); AssetDatabase.Refresh(); }
        Finish();
    }

    /// <summary>Matches on the leading "Step_07" style prefix only, so the rest of the
    /// file name is free-form ("Step_07_fileserver_dialog.png" still matches
    /// "Step_07_FileServicesConfig").</summary>
    private static Sprite MatchByName(string stepAssetName, List<Sprite> sprites)
    {
        string prefix = StepPrefix(stepAssetName);
        if (prefix == null) return null;

        // Exact whole-name match wins if one exists.
        var exact = sprites.FirstOrDefault(s =>
            string.Equals(s.name, stepAssetName, System.StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        return sprites.FirstOrDefault(s => StepPrefix(s.name) == prefix);
    }

    /// <summary>"Step_07_Whatever" -> "step07". Null if the name has no step number.</summary>
    private static string StepPrefix(string name)
    {
        string n = name.Replace(" ", "").Replace("-", "_");
        int i = 0;

        if (n.StartsWith("Step", System.StringComparison.OrdinalIgnoreCase)) i = 4;
        while (i < n.Length && (n[i] == '_' || n[i] == '.')) i++;

        int start = i;
        while (i < n.Length && char.IsDigit(n[i])) i++;
        if (i == start) return null;

        // Strip leading zeros so "Step_07" and "Step7" match each other.
        return "step" + int.Parse(n.Substring(start, i - start));
    }

    private List<ScreenshotStepData> LoadStepAssets()
    {
        var list = new List<ScreenshotStepData>();
        if (!AssetDatabase.IsValidFolder(stepDataFolder)) return list;

        foreach (var guid in AssetDatabase.FindAssets("t:ScreenshotStepData", new[] { stepDataFolder }))
        {
            var a = AssetDatabase.LoadAssetAtPath<ScreenshotStepData>(AssetDatabase.GUIDToAssetPath(guid));
            if (a != null) list.Add(a);
        }

        // FindAssets returns them in an arbitrary order - sort so Step_2 precedes Step_10.
        list.Sort((x, y) => NumberIn(x.name).CompareTo(NumberIn(y.name)));
        return list;
    }

    private List<Sprite> LoadSprites()
    {
        var list = new List<Sprite>();
        if (!AssetDatabase.IsValidFolder(screenshotFolder)) return list;

        foreach (var guid in AssetDatabase.FindAssets("t:Sprite", new[] { screenshotFolder }))
        {
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guid));
            if (s != null) list.Add(s);
        }

        list.Sort((x, y) => NumberIn(x.name).CompareTo(NumberIn(y.name)));
        return list;
    }

    /// <summary>First run of digits in the name, used for natural ordering. 9999 if none.</summary>
    private static int NumberIn(string name)
    {
        int i = 0;
        while (i < name.Length && !char.IsDigit(name[i])) i++;
        if (i >= name.Length) return 9999;

        int start = i;
        while (i < name.Length && char.IsDigit(name[i])) i++;
        return int.Parse(name.Substring(start, i - start));
    }

    /// <summary>Switches every texture in the folder to Sprite (2D and UI), which is what
    /// makes Unity expose it as a Sprite in the first place.</summary>
    private void FixTextureTypes()
    {
        report.Clear();

        if (!AssetDatabase.IsValidFolder(screenshotFolder))
        {
            Log($"ERROR: '{screenshotFolder}' is not a folder inside this project.");
            Finish();
            return;
        }

        int changed = 0, already = 0;

        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { screenshotFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;

            if (importer.textureType == TextureImporterType.Sprite) { already++; continue; }

            importer.textureType = TextureImporterType.Sprite;
            importer.SaveAndReimport();
            Log($"Converted to Sprite: {System.IO.Path.GetFileName(path)}");
            changed++;
        }

        Log("");
        Log($"Converted {changed}, already correct {already}.");
        if (changed + already == 0)
            Log("No textures found there at all - check the folder path.");

        Finish();
    }

    private static string ToProjectPath(string absolute)
    {
        absolute = absolute.Replace('\\', '/');
        string root = Application.dataPath.Replace('\\', '/');
        return absolute.StartsWith(root) ? "Assets" + absolute.Substring(root.Length) : absolute;
    }

    private void Log(string line) => report.Add(line);

    private void Finish()
    {
        Debug.Log("[COC3ScreenshotAssigner]\n" + string.Join("\n", report));
        Repaint();
    }
}
#endif