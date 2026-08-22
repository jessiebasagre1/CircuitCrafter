#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Finds and fixes the things that make UI silently unclickable.
///
/// Tools > COC 1 > Canvas Click Fixer
///
/// Every check here came out of a real hunt through this project. None of them are visible
/// from any single Inspector, which is what makes them expensive.
///
///   1. A STRANDED STATION PANEL.
///      A rebuild creates a fresh panel and repoints the manager at it, but an older copy of
///      the same panel is left in the scene, unreferenced and still switched on. Nothing turns
///      it off at runtime, because nothing knows it exists any more - and on a World Space
///      canvas it floats in front of other screens and eats their clicks.
///
///   2. ACTIVE UI ON A WORLD SPACE CANVAS.
///      World Space canvases share the room. Anything left active on one sits in front of
///      another from some viewpoints. A panel that "looks off" because you cannot see it may
///      simply be behind you.
///
///   3. A WORLD SPACE CANVAS WITH NO EVENT CAMERA.
///      GraphicRaycaster falls back to Camera.main, so in a multi-camera scene the ray is cast
///      from the wrong one. Clicks land away from the cursor and nudging a control's Z looks
///      like a fix.
///
///   4. BLOCKING OBJECTS, a missing raycaster, and leftover Z nudges.
///
/// Scanning changes nothing. Each fix asks first and is one undo step.
/// </summary>
public class COC1CanvasClickFixer : EditorWindow
{
    private Vector2 scroll;
    private string report = "";
    private bool scanned;

    private readonly List<GraphicRaycaster> blocking = new List<GraphicRaycaster>();
    private readonly List<RectTransform> zOffenders = new List<RectTransform>();
    private readonly List<GameObject> stranded = new List<GameObject>();

    private readonly List<Canvas> needCamera = new List<Canvas>();
    private readonly List<Camera> cameraChoice = new List<Camera>();

    [MenuItem("Tools/COC 1/Canvas Click Fixer")]
    public static void Open()
    {
        var w = GetWindow<COC1CanvasClickFixer>("Canvas Clicks");
        if (w != null) w.minSize = new Vector2(500, 620);
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("Why isn't this button clickable?", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Checks for stranded station panels, active UI on World Space canvases, missing " +
            "event cameras, collider blocking, missing raycasters, and leftover Z nudges.\n\n" +
            "Run it after every panel rebuild. Scanning changes nothing.",
            MessageType.Info);

        EditorGUILayout.Space();
        if (GUILayout.Button("Scan Open Scene", GUILayout.Height(30)))
            EditorApplication.delayCall += Scan;

        if (scanned)
        {
            using (new EditorGUI.DisabledScope(stranded.Count == 0))
            {
                if (GUILayout.Button($"Disable {stranded.Count} stranded panel(s)", GUILayout.Height(28)))
                    EditorApplication.delayCall += FixStranded;
            }

            DrawCameraSection();

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(blocking.Count == 0))
            {
                if (GUILayout.Button($"Set Blocking Objects to None on {blocking.Count} raycaster(s)"))
                    EditorApplication.delayCall += FixBlocking;
            }

            using (new EditorGUI.DisabledScope(zOffenders.Count == 0))
            {
                if (GUILayout.Button($"Reset Z to 0 on {zOffenders.Count} control(s)"))
                    EditorApplication.delayCall += FixZ;
            }
        }

        if (!string.IsNullOrEmpty(report))
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Report", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(report, MessageType.None);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawCameraSection()
    {
        if (needCamera.Count == 0) return;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Event Cameras", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "These World Space canvases have no Event Camera, so Unity raycasts them through " +
            "Camera.main - the wrong camera in a multi-camera scene.",
            MessageType.Warning);

        if (GUILayout.Button("Auto-Detect Cameras"))
            EditorApplication.delayCall += GuessCameras;

        for (int i = 0; i < needCamera.Count; i++)
        {
            if (needCamera[i] == null) continue;

            cameraChoice[i] = (Camera)EditorGUILayout.ObjectField(
                new GUIContent(needCamera[i].name), cameraChoice[i], typeof(Camera), true);
        }

        int ready = 0;
        foreach (var c in cameraChoice) if (c != null) ready++;

        using (new EditorGUI.DisabledScope(ready == 0))
        {
            if (GUILayout.Button($"Assign {ready} Event Camera(s)", GUILayout.Height(28)))
                EditorApplication.delayCall += AssignCameras;
        }
    }

    // ==================================================================
    // Scan
    // ==================================================================

    private void Scan()
    {
        blocking.Clear();
        zOffenders.Clear();
        stranded.Clear();
        needCamera.Clear();
        cameraChoice.Clear();

        var sb = new StringBuilder();
        int problems = 0;

        problems += ScanStranded(sb);
        problems += ScanCanvases(sb);

        sb.AppendLine(problems == 0
            ? "No click-blocking problems found."
            : problems + " problem(s) found.");

        report = sb.ToString();
        scanned = true;
        Repaint();
    }

    // ------------------------------------------------------------------
    // 1. Stranded and duplicated station panels
    // ------------------------------------------------------------------

    private int ScanStranded(StringBuilder sb)
    {
        int problems = 0;

        sb.AppendLine("--- STATION VIEWS ---");

        // Types that should exist once in the scene. ChecklistToolView is the exception:
        // Device Manager, Software Center and Windows Update are three copies by design.
        problems += ReportCopies<OSInstallView>(sb, "OSInstallView", 1);
        problems += ReportCopies<PartitionSelectView>(sb, "PartitionSelectView", 1);
        problems += ReportCopies<RufusSetupView>(sb, "RufusSetupView", 1);
        problems += ReportCopies<BootableUSBScreenshotView>(sb, "BootableUSBScreenshotView", 1);
        problems += ReportCopies<DiagnosticsView>(sb, "DiagnosticsView", 1);
        problems += ReportCopies<ServiceReportView>(sb, "ServiceReportView", 1);
        problems += ReportCopies<ChecklistToolView>(sb, "ChecklistToolView", 3);

        // The one case where "which copy is the real one" is answerable rather than guessable:
        // OSInstallView names the partition view it actually drives. Any other one that starts
        // switched on is unreferenced, will never be turned off, and blocks whatever is behind it.
        var installView = FindInScene<OSInstallView>();
        if (installView != null)
        {
            var so = new SerializedObject(installView);
            var live = GetObject(so, "partitionSelectView") as PartitionSelectView;

            foreach (var view in FindAllInScene<PartitionSelectView>())
            {
                if (view == null || view == live) continue;
                if (!view.gameObject.activeSelf) continue;

                sb.AppendLine("   STRANDED AND ON: " + Path(view.transform));
                sb.AppendLine("      OSInstallView drives '" +
                              (live != null ? Path(live.transform) : "nothing") +
                              "', so nothing ever switches this one off.");
                stranded.Add(view.gameObject);
                problems++;
            }
        }

        sb.AppendLine();
        return problems;
    }

    private int ReportCopies<T>(StringBuilder sb, string label, int expected) where T : Component
    {
        var found = FindAllInScene<T>();
        if (found.Count <= expected) return 0;

        sb.AppendLine("   " + found.Count + " x " + label + " (expected " + expected + "):");
        foreach (var c in found)
            sb.AppendLine("      " + (c.gameObject.activeSelf ? "ON  " : "off ") + Path(c.transform));

        return 1;
    }

    // ------------------------------------------------------------------
    // 2-4. Canvases
    // ------------------------------------------------------------------

    private int ScanCanvases(StringBuilder sb)
    {
        int problems = 0;
        var canvases = FindAllInScene<Canvas>();

        foreach (var canvas in canvases)
        {
            // A GraphicRaycaster only does anything on a root canvas; a nested one inherits
            // its parent's, so reporting those would be noise.
            if (!canvas.isRootCanvas) continue;

            sb.AppendLine(canvas.name + "  [" + canvas.renderMode + "]");

            var caster = canvas.GetComponent<GraphicRaycaster>();

            if (caster == null)
            {
                sb.AppendLine("   NO GRAPHIC RAYCASTER - nothing inside can be clicked.");
                problems++;
            }
            else
            {
                bool overlay = canvas.renderMode == RenderMode.ScreenSpaceOverlay;
                bool blocks = caster.blockingObjects != GraphicRaycaster.BlockingObjects.None;

                sb.AppendLine("   Blocking Objects: " + caster.blockingObjects +
                              "   Mask: " + MaskLabel(caster.blockingMask));

                if (blocks && !overlay)
                {
                    sb.AppendLine("   ^ colliders on that mask will occlude UI behind them.");
                    blocking.Add(caster);
                    problems++;
                }
            }

            if (canvas.renderMode == RenderMode.WorldSpace)
            {
                if (canvas.worldCamera == null)
                {
                    sb.AppendLine("   NO EVENT CAMERA. Unity falls back to Camera.main, so in a " +
                                  "multi-camera scene the ray comes from the wrong one - clicks " +
                                  "land away from the cursor.");
                    needCamera.Add(canvas);
                    cameraChoice.Add(null);
                    problems++;
                }

                ReportActiveRaycastTargets(sb, canvas);
            }

            CollectZOffenders(canvas);
            sb.AppendLine();
        }

        if (zOffenders.Count > 0)
        {
            sb.AppendLine("Controls at a non-zero local Z:");
            foreach (var rt in zOffenders)
                sb.AppendLine("   " + Path(rt) + "   z=" + rt.localPosition.z);
            sb.AppendLine("   (Usually a workaround for a click landing elsewhere. Clear it only " +
                          "after the real cause is fixed.)");
            sb.AppendLine();
        }

        return problems;
    }

    /// <summary>
    /// World Space canvases share the room, so anything active on one can sit in front of
    /// another from the player's viewpoint and swallow its clicks. This is informational rather
    /// than an error - you cannot tell statically which overlaps what - but seeing "12 active
    /// raycast targets" on a station nobody is using is usually enough.
    /// </summary>
    private static void ReportActiveRaycastTargets(StringBuilder sb, Canvas canvas)
    {
        // includeInactive false: only what is genuinely live in the saved scene.
        var graphics = canvas.GetComponentsInChildren<Graphic>(false);

        var live = new List<Graphic>();
        foreach (var g in graphics)
            if (g != null && g.raycastTarget) live.Add(g);

        if (live.Count == 0)
        {
            sb.AppendLine("   No active raycast targets - cannot block anything.");
            return;
        }

        sb.AppendLine("   " + live.Count + " ACTIVE raycast target(s). These sit in front of any " +
                      "other World Space canvas behind them:");

        for (int i = 0; i < live.Count && i < 6; i++)
            sb.AppendLine("      " + Path(live[i].transform));

        if (live.Count > 6)
            sb.AppendLine("      ...and " + (live.Count - 6) + " more.");
    }

    /// <summary>
    /// Only real controls count. A World Space canvas legitimately sits at a world position,
    /// and a camera parented under one is not a control - flagging those buried the one line
    /// that mattered.
    /// </summary>
    private void CollectZOffenders(Canvas canvas)
    {
        foreach (var rt in canvas.GetComponentsInChildren<RectTransform>(true))
        {
            if (rt == null) continue;
            if (rt == canvas.transform) continue;
            if (rt.GetComponent<Camera>() != null) continue;

            bool isControl = rt.GetComponent<Graphic>() != null || rt.GetComponent<Selectable>() != null;
            if (!isControl) continue;

            if (Mathf.Abs(rt.localPosition.z) > 0.01f)
                zOffenders.Add(rt);
        }
    }

    // ==================================================================
    // Fixes
    // ==================================================================

    private void FixStranded()
    {
        if (stranded.Count == 0) return;

        var names = new StringBuilder();
        foreach (var go in stranded) if (go != null) names.AppendLine("  " + Path(go.transform));

        bool ok = EditorUtility.DisplayDialog(
            "Disable stranded panels?",
            "These are switched on but nothing references them:\n\n" + names +
            "\nThey are almost certainly older copies left behind by a rebuild. Disabling is " +
            "reversible; delete them once you have played through and confirmed the current " +
            "ones work.\n\nOne undo step.",
            "Disable", "Cancel");

        if (!ok) { report = "Cancelled. Nothing was changed."; Repaint(); return; }

        int group = Undo.GetCurrentGroup();
        int done = 0;

        foreach (var go in stranded)
        {
            if (go == null) continue;

            Undo.RecordObject(go, "Disable Stranded Panel");
            go.SetActive(false);
            EditorUtility.SetDirty(go);
            done++;
        }

        Undo.CollapseUndoOperations(group);
        MarkSceneDirty();

        report = "Disabled " + done + " stranded panel(s). Save the scene (Ctrl+S) - an edit made " +
                 "during Play mode would be thrown away when you stop.";
        stranded.Clear();
        Repaint();
    }

    private void GuessCameras()
    {
        var lines = new List<string>();

        for (int i = 0; i < needCamera.Count; i++)
        {
            var canvas = needCamera[i];
            if (canvas == null || cameraChoice[i] != null) continue;

            // Under the canvas first - a per-station camera is often kept with the screen it
            // renders - then a sibling, then borrowed from another World Space canvas that
            // already has one, which is usually the same station's camera.
            var cam = canvas.GetComponentInChildren<Camera>(true);

            if (cam == null && canvas.transform.parent != null)
                cam = canvas.transform.parent.GetComponentInChildren<Camera>(true);

            if (cam == null)
            {
                foreach (var other in FindAllInScene<Canvas>())
                {
                    if (other == null || other == canvas) continue;
                    if (other.renderMode != RenderMode.WorldSpace) continue;
                    if (other.worldCamera == null) continue;

                    cam = other.worldCamera;
                    break;
                }
            }

            cameraChoice[i] = cam;
            lines.Add("  " + canvas.name + " -> " + (cam != null ? cam.name : "not found, assign by hand"));
        }

        report = "Auto-detect:\n" + string.Join("\n", lines.ToArray()) +
                 "\n\nCheck each before assigning - it must be the camera you actually look " +
                 "through when that screen is on. The wrong one reproduces the exact bug this fixes.";
        Repaint();
    }

    private void AssignCameras()
    {
        int group = Undo.GetCurrentGroup();
        int done = 0;
        var lines = new List<string>();

        for (int i = 0; i < needCamera.Count; i++)
        {
            var canvas = needCamera[i];
            var cam = cameraChoice[i];
            if (canvas == null || cam == null) continue;

            Undo.RecordObject(canvas, "Assign Event Camera");
            canvas.worldCamera = cam;
            EditorUtility.SetDirty(canvas);

            lines.Add("  " + canvas.name + " -> " + cam.name);
            done++;
        }

        Undo.CollapseUndoOperations(group);
        MarkSceneDirty();

        report = "Assigned " + done + " event camera(s):\n" + string.Join("\n", lines.ToArray());
        Repaint();
    }

    private void FixBlocking()
    {
        if (blocking.Count == 0) return;

        var names = new StringBuilder();
        foreach (var c in blocking) names.AppendLine("  " + c.name + "  (" + c.blockingObjects + ")");

        bool ok = EditorUtility.DisplayDialog(
            "Set Blocking Objects to None?",
            "On these raycasters:\n\n" + names +
            "\nWorld colliders will stop occluding UI on these canvases. If you WANT geometry to " +
            "block clicks, cancel and narrow the Blocking Mask by layer instead.\n\nOne undo step.",
            "Set to None", "Cancel");

        if (!ok) { report = "Cancelled. Nothing was changed."; Repaint(); return; }

        int group = Undo.GetCurrentGroup();
        int done = 0;

        foreach (var caster in blocking)
        {
            if (caster == null) continue;

            Undo.RecordObject(caster, "Clear Blocking Objects");
            caster.blockingObjects = GraphicRaycaster.BlockingObjects.None;
            EditorUtility.SetDirty(caster);
            done++;
        }

        Undo.CollapseUndoOperations(group);
        MarkSceneDirty();

        report = "Set Blocking Objects to None on " + done + " raycaster(s).";
        blocking.Clear();
        Repaint();
    }

    private void FixZ()
    {
        if (zOffenders.Count == 0) return;

        bool ok = EditorUtility.DisplayDialog(
            "Reset Z to 0?",
            "On " + zOffenders.Count + " control(s).\n\n" +
            "Do this only once the real cause is fixed. A Z nudge is a workaround for a click " +
            "landing in the wrong place, and clearing it while that is still true makes the " +
            "control dead again.\n\nOne undo step.",
            "Reset Z", "Cancel");

        if (!ok) { report = "Cancelled. Nothing was changed."; Repaint(); return; }

        int group = Undo.GetCurrentGroup();
        int done = 0;

        foreach (var rt in zOffenders)
        {
            if (rt == null) continue;

            Undo.RecordObject(rt, "Reset UI Z");
            var p = rt.localPosition;
            rt.localPosition = new Vector3(p.x, p.y, 0f);
            EditorUtility.SetDirty(rt);
            done++;
        }

        Undo.CollapseUndoOperations(group);
        MarkSceneDirty();

        report = "Reset Z on " + done + " control(s).";
        zOffenders.Clear();
        Repaint();
    }

    // ==================================================================

    private static Object GetObject(SerializedObject so, string field)
    {
        var p = so.FindProperty(field);
        return p != null ? p.objectReferenceValue : null;
    }

    private static string MaskLabel(LayerMask mask)
    {
        int v = mask.value;
        if (v == 0) return "Nothing";
        if (v == ~0) return "Everything";

        var sb = new StringBuilder();
        for (int i = 0; i < 32; i++)
        {
            if ((v & (1 << i)) == 0) continue;

            string layer = LayerMask.LayerToName(i);
            if (string.IsNullOrEmpty(layer)) layer = "Layer " + i;

            if (sb.Length > 0) sb.Append(", ");
            sb.Append(layer);
        }
        return sb.ToString();
    }

    private static string Path(Transform t)
    {
        string path = t.name;
        while (t.parent != null)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    private static T FindInScene<T>() where T : Component
    {
        foreach (var c in FindAllInScene<T>()) return c;
        return null;
    }

    /// <summary>FindObjectsOfTypeAll, because the panels that matter are usually switched off
    /// and the ordinary find skips those.</summary>
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

    private static void MarkSceneDirty()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.IsValid())
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    }
}
#endif