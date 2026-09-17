#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns COC3QuizBlueprint into real QuizQuestionData and QuizSetData assets.
///
/// RUN IT: Tools > COC3 > Generate Quiz Assets
///
/// Creating these by hand would mean roughly 23 question assets, each with four choices, a
/// correct index and an explanation - and then keeping them in step with the blueprint by
/// memory. Generating them means the content lives in one readable file that can be edited,
/// reviewed and version-controlled as text.
///
/// SAFE TO RE-RUN. Existing assets are updated in place rather than duplicated, so the
/// references already sitting in your QuizSetData and COC3QuizBridge survive.
/// </summary>
public static class COC3QuizBuilder
{
    private const string QuestionFolder = "Assets/COC3/Quiz/Questions";
    private const string SetFolder = "Assets/COC3/Quiz/Sets";

    [MenuItem("Tools/COC3/Generate Quiz Assets")]
    public static void Generate()
    {
        EnsureFolder(QuestionFolder);
        EnsureFolder(SetFolder);

        var log = new List<string>();
        int questionsCreated = 0, questionsUpdated = 0, setsCreated = 0, setsUpdated = 0;

        foreach (var setDef in COC3QuizBlueprint.Sets)
        {
            var questionAssets = new List<QuizQuestionData>();

            for (int i = 0; i < setDef.pool.Count; i++)
            {
                var q = setDef.pool[i];
                string path = $"{QuestionFolder}/{setDef.assetName}_Q{i + 1:00}.asset";

                var asset = AssetDatabase.LoadAssetAtPath<QuizQuestionData>(path);
                bool isNew = asset == null;

                if (isNew)
                {
                    asset = ScriptableObject.CreateInstance<QuizQuestionData>();
                    AssetDatabase.CreateAsset(asset, path);
                    questionsCreated++;
                }
                else
                {
                    Undo.RecordObject(asset, "Update COC3 Quiz Question");
                    questionsUpdated++;
                }

                asset.questionText = q.text;
                asset.choices = q.choices;
                asset.correctChoiceIndex = q.correct;
                asset.points = q.points;
                asset.explanation = q.explanation;
                EditorUtility.SetDirty(asset);

                questionAssets.Add(asset);
            }

            string setPath = $"{SetFolder}/{setDef.assetName}.asset";
            var set = AssetDatabase.LoadAssetAtPath<QuizSetData>(setPath);

            if (set == null)
            {
                set = ScriptableObject.CreateInstance<QuizSetData>();
                AssetDatabase.CreateAsset(set, setPath);
                setsCreated++;
            }
            else
            {
                Undo.RecordObject(set, "Update COC3 Quiz Set");
                setsUpdated++;
            }

            set.taskId = setDef.taskId;
            set.questionPool = questionAssets;
            set.questionsToAsk = Mathf.Min(setDef.questionsToAsk, questionAssets.Count);
            EditorUtility.SetDirty(set);

            log.Add($"  {setDef.assetName}  -  {questionAssets.Count} questions, asks {set.questionsToAsk}");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[COC3QuizBuilder] Sets: {setsCreated} created, {setsUpdated} updated. " +
                  $"Questions: {questionsCreated} created, {questionsUpdated} updated.\n" +
                  string.Join("\n", log) +
                  $"\n\nAssets are in {SetFolder} - drag them into COC3QuizBridge's trigger list.");

        // Put the sets in front of the user so the next step is obvious.
        var first = AssetDatabase.LoadAssetAtPath<Object>(SetFolder);
        if (first != null) EditorGUIUtility.PingObject(first);
    }

    private static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string[] parts = folder.Split('/');
        string running = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = running + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(running, parts[i]);
            running = next;
        }
    }
}
#endif
