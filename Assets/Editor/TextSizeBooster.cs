using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace CircuitCrafter.EditorTools
{
    /// <summary>
    /// Makes the text on a screen bigger without breaking its layout, and can put it all back.
    ///
    /// OPEN IT WITH: CircuitCrafter > Boost Text Size
    ///
    /// HOW TO USE IT
    ///   1. Select the screens you want enlarged - prefabs in the Project window, objects in the
    ///      Hierarchy, or several of both at once. For the OS install flow that is the install
    ///      window, the Rufus window, the partition screen, the checklist, the diagnostics view
    ///      and the service report.
    ///   2. Set the factor and press Boost.
    ///   3. Play it on a phone. If something is still small, boost again; if something is now too
    ///      big, press Revert and try a smaller factor.
    ///
    /// WHY IT DOES NOT SIMPLY MULTIPLY THE FONT SIZE
    /// Multiplying is what breaks layouts: a label that exactly filled its box at 18 does not fit
    /// at 26, and TextMeshPro either spills over its neighbour or - with Ellipsis overflow -
    /// clips the end off the very words the learner needs.
    ///
    /// So with Auto Size Safety on (the default) each text is given a RANGE rather than a number:
    /// its old size as the minimum, the boosted size as the maximum, and auto-sizing switched on.
    /// TMP then draws every label as large as its own box allows, up to the new maximum, and falls
    /// back toward the original where there genuinely is not room. Text that has space gets bigger;
    /// text that has none stays as readable as it was, instead of overflowing.
    ///
    /// IT CAN BE UNDONE, INCLUDING ON PREFABS
    /// Ctrl+Z covers scene objects, but not prefab assets - an edit written into a prefab file is
    /// not on the undo stack, and this tool is most useful on prefabs. So every change is recorded
    /// to Library/CircuitCrafterTextBoost.json (outside Assets, so it is never committed or built)
    /// and Revert Last Boost puts every one of them back exactly as it was, auto-size settings
    /// included.
    ///
    /// ONE BOOST IS REMEMBERED AT A TIME. Boosting again replaces the record, because reverting
    /// through two layers of "old size" would restore sizes that were themselves boosted. Revert
    /// first if you want to try a different factor.
    /// </summary>
    public class TextSizeBooster : EditorWindow
    {
        private const string LogPath = "Library/CircuitCrafterTextBoost.json";

        [SerializeField] private float factor = 1.35f;
        [SerializeField] private float smallestAllowed = 16f;
        [SerializeField] private float largestAllowed = 80f;
        [SerializeField] private bool autoSizeSafety = true;
        [SerializeField] private bool includeInactive = true;
        [SerializeField] private bool skipTextsAlreadyLarge = true;
        [SerializeField] private float alreadyLargeAbove = 48f;

        [MenuItem("CircuitCrafter/Boost Text Size", false, 60)]
        private static void Open()
        {
            // NOTE FOR ANYONE TEMPTED TO CALL THE SLIDER FIELDS minSize/maxSize AGAIN:
            // EditorWindow already owns both of those names, as Vector2 properties for the
            // window's own size limits. Fields with those names in a subclass hide them, and this
            // very line then tries to assign a Vector2 to a float - the Editor assembly stops
            // compiling, and because it is the EDITOR assembly the only symptom is that menu items
            // quietly stop appearing. Hence smallestAllowed / largestAllowed below.
            var window = GetWindow<TextSizeBooster>("Boost Text Size");
            window.minSize = new Vector2(380f, 330f);
        }

        // ==================================================================

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Enlarge text on the selected screens", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "Select prefabs in the Project window and/or objects in the Hierarchy, then Boost.\n\n" +
                "With Auto Size Safety on, each label is given a range - its old size as the " +
                "minimum and the boosted size as the maximum - so text grows where its box has " +
                "room and stays put where it does not, instead of overflowing.",
                MessageType.None);

            EditorGUILayout.Space();

            factor = EditorGUILayout.Slider("Factor", factor, 1f, 3f);
            smallestAllowed = EditorGUILayout.FloatField("Smallest allowed", smallestAllowed);
            largestAllowed = EditorGUILayout.FloatField("Largest allowed", largestAllowed);

            EditorGUILayout.Space();

            autoSizeSafety = EditorGUILayout.Toggle("Auto size safety", autoSizeSafety);
            includeInactive = EditorGUILayout.Toggle("Include inactive", includeInactive);
            skipTextsAlreadyLarge = EditorGUILayout.Toggle("Skip large text", skipTextsAlreadyLarge);

            using (new EditorGUI.DisabledScope(!skipTextsAlreadyLarge))
                alreadyLargeAbove = EditorGUILayout.FloatField("   Already large above", alreadyLargeAbove);

            EditorGUILayout.Space();

            int selected = Selection.gameObjects != null ? Selection.gameObjects.Length : 0;
            EditorGUILayout.LabelField("Selected", selected == 0 ? "nothing" : selected + " object(s)");

            using (new EditorGUI.DisabledScope(selected == 0))
            {
                if (GUILayout.Button("Boost the selection", GUILayout.Height(30f))) Boost();
            }

            EditorGUILayout.Space();

            bool hasLog = File.Exists(LogPath);
            using (new EditorGUI.DisabledScope(!hasLog))
            {
                if (GUILayout.Button(hasLog ? "Revert last boost" : "Nothing to revert"))
                    Revert();
            }
        }

        // ==================================================================

        private void Boost()
        {
            var log = new BoostLog();
            int changed = 0;

            // A copy, because editing prefab contents changes what Selection returns partway
            // through and the loop would then skip objects with nothing to say it had.
            var targets = new List<GameObject>(Selection.gameObjects);

            foreach (GameObject target in targets)
            {
                if (target == null) continue;

                string assetPath = AssetDatabase.GetAssetPath(target);

                if (!string.IsNullOrEmpty(assetPath) && assetPath.EndsWith(".prefab"))
                    changed += BoostPrefab(assetPath, log);
                else
                    changed += BoostSceneObject(target, log);
            }

            SaveLog(log);
            AssetDatabase.SaveAssets();

            Debug.Log($"[TextSizeBooster] Boosted {changed} text(s) by x{factor:0.00}. " +
                      (changed > 0
                          ? "Revert Last Boost puts them all back."
                          : "Nothing matched - check the selection, or turn off Skip Large Text."));
        }

        /// <summary>
        /// A prefab asset is not a scene object: finding it and setting a field either throws or is
        /// silently discarded on the next reimport. LoadPrefabContents opens it into a hidden scene
        /// that can be edited normally, and the try/finally is what stops a failure halfway through
        /// leaving that scene open for the rest of the Editor session.
        /// </summary>
        private int BoostPrefab(string assetPath, BoostLog log)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(assetPath);
            int changed = 0;

            try
            {
                foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(includeInactive))
                    changed += Apply(text, assetPath, log) ? 1 : 0;

                if (changed > 0) PrefabUtility.SaveAsPrefabAsset(root, assetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return changed;
        }

        private int BoostSceneObject(GameObject target, BoostLog log)
        {
            int changed = 0;

            foreach (TMP_Text text in target.GetComponentsInChildren<TMP_Text>(includeInactive))
            {
                // Undo.RecordObject as well as the JSON log: Ctrl+Z is what people reach for first
                // on a scene object, and it would be rude to have only the menu item work.
                Undo.RecordObject(text, "Boost Text Size");

                if (Apply(text, SceneKey(text.gameObject), log))
                {
                    EditorUtility.SetDirty(text);
                    changed++;
                }
            }

            return changed;
        }

        /// <summary>Records the old settings and writes the new ones. False when this text was
        /// skipped, so the count reported at the end is true.</summary>
        private bool Apply(TMP_Text text, string container, BoostLog log)
        {
            if (text == null) return false;

            float old = text.fontSize;
            if (old <= 0f) return false;

            if (skipTextsAlreadyLarge && old >= alreadyLargeAbove) return false;

            float boosted = Mathf.Clamp(old * factor, smallestAllowed, largestAllowed);

            // Nothing to do, and recording it would only make Revert slower and the count a lie.
            if (Mathf.Abs(boosted - old) < 0.01f && !autoSizeSafety) return false;

            log.records.Add(new BoostRecord
            {
                container = container,
                path = PathOf(text.transform),
                oldSize = old,
                oldAutoSize = text.enableAutoSizing,
                oldMin = text.fontSizeMin,
                oldMax = text.fontSizeMax
            });

            text.fontSize = boosted;

            if (autoSizeSafety)
            {
                // The range, not the number. Minimum is the size it was - so it can never end up
                // SMALLER than before this tool touched it, which would be a strange outcome for
                // something called a boost.
                text.fontSizeMin = old;
                text.fontSizeMax = boosted;
                text.enableAutoSizing = true;
            }

            return true;
        }

        // ==================================================================

        private void Revert()
        {
            BoostLog log = LoadLog();

            if (log == null || log.records.Count == 0)
            {
                Debug.Log("[TextSizeBooster] Nothing recorded to revert.");
                return;
            }

            // Grouped by container so each prefab is opened and saved ONCE rather than once per
            // label, which on a busy screen is the difference between a second and a minute.
            var byContainer = new Dictionary<string, List<BoostRecord>>();
            foreach (BoostRecord record in log.records)
            {
                if (!byContainer.TryGetValue(record.container, out var list))
                {
                    list = new List<BoostRecord>();
                    byContainer[record.container] = list;
                }
                list.Add(record);
            }

            int restored = 0;

            foreach (var pair in byContainer)
            {
                restored += pair.Key.EndsWith(".prefab")
                    ? RevertPrefab(pair.Key, pair.Value)
                    : RevertScene(pair.Key, pair.Value);
            }

            File.Delete(LogPath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[TextSizeBooster] Reverted {restored} text(s) to their original sizes.");
        }

        private int RevertPrefab(string assetPath, List<BoostRecord> records)
        {
            if (!File.Exists(assetPath)) return 0;

            GameObject root = PrefabUtility.LoadPrefabContents(assetPath);
            int restored = 0;

            try
            {
                foreach (BoostRecord record in records)
                {
                    TMP_Text text = FindText(root.transform, record.path);
                    if (text == null) continue;

                    Restore(text, record);
                    restored++;
                }

                if (restored > 0) PrefabUtility.SaveAsPrefabAsset(root, assetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return restored;
        }

        private int RevertScene(string sceneKey, List<BoostRecord> records)
        {
            int restored = 0;

            foreach (BoostRecord record in records)
            {
                // Inactive included: a panel that was open when it was boosted is very often shut
                // by the time anyone reverts.
                foreach (TMP_Text text in Resources.FindObjectsOfTypeAll<TMP_Text>())
                {
                    if (text == null) continue;
                    if (PathOf(text.transform) != record.path) continue;
                    if (SceneKey(text.gameObject) != sceneKey) continue;

                    Undo.RecordObject(text, "Revert Text Size");
                    Restore(text, record);
                    EditorUtility.SetDirty(text);
                    restored++;
                    break;
                }
            }

            return restored;
        }

        private static void Restore(TMP_Text text, BoostRecord record)
        {
            // Auto-sizing off FIRST. TMP clamps fontSize against the min/max while auto-sizing is
            // on, so writing the old size before switching it off can quietly land on a different
            // number - and a revert that does not quite revert is worse than none.
            text.enableAutoSizing = false;

            text.fontSize = record.oldSize;
            text.fontSizeMin = record.oldMin;
            text.fontSizeMax = record.oldMax;
            text.enableAutoSizing = record.oldAutoSize;
        }

        // ==================================================================

        private static string SceneKey(GameObject go)
        {
            string scene = go.scene.name;
            return "scene:" + (string.IsNullOrEmpty(scene) ? "unsaved" : scene);
        }

        private static string PathOf(Transform target)
        {
            string path = target.name;

            Transform walk = target.parent;
            while (walk != null)
            {
                path = walk.name + "/" + path;
                walk = walk.parent;
            }

            return path;
        }

        private static TMP_Text FindText(Transform root, string path)
        {
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (PathOf(text.transform) == path) return text;
            }
            return null;
        }

        private static void SaveLog(BoostLog log)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                File.WriteAllText(LogPath, JsonUtility.ToJson(log, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TextSizeBooster] Couldn't write the revert log: {e.Message}. " +
                                 "The sizes changed, but Revert Last Boost will not work.");
            }
        }

        private static BoostLog LoadLog()
        {
            try
            {
                return File.Exists(LogPath)
                    ? JsonUtility.FromJson<BoostLog>(File.ReadAllText(LogPath))
                    : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TextSizeBooster] Couldn't read the revert log: {e.Message}");
                return null;
            }
        }

        // ==================================================================

        [Serializable]
        private class BoostRecord
        {
            public string container;
            public string path;
            public float oldSize;
            public bool oldAutoSize;
            public float oldMin;
            public float oldMax;
        }

        [Serializable]
        private class BoostLog
        {
            public List<BoostRecord> records = new List<BoostRecord>();
        }
    }
}