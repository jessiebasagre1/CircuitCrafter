using System;
using UnityEngine;

/// <summary>
/// Central, persistent score tracker. Put it on the same persistent
/// "Managers" object as SaveManager (DontDestroyOnLoad) so score survives
/// scene changes and Continue/Load, exactly like SaveManager itself.
///
/// Reusable everywhere: any system in any COC module can call
/// ScoreManager.Instance.AddScore(amount, "some source label") to award
/// points -- quizzes, minigames, bonus objectives, whatever comes later.
///
/// Extensibility for badges/achievements (not built yet, on purpose):
/// a future AchievementManager can subscribe to OnScoreChanged to react to
/// score milestones without touching this file at all.
/// </summary>
public class ScoreManager : MonoBehaviour, ISaveable
{
    public static ScoreManager Instance { get; private set; }

    public int TotalScore { get; private set; }

    /// <summary>Fires every time score changes, anywhere. newTotal = score after
    /// the change, delta = how much was just added (can be negative if you
    /// ever add penalties), source = free-form label of what caused it (e.g.
    /// "quiz:disassemble_pc"). A future AchievementManager can subscribe to
    /// this alone to unlock badges at score thresholds.</summary>
    public event Action<int, int, string> OnScoreChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>Adds (or subtracts, with a negative amount) points to the running total.</summary>
    public void AddScore(int amount, string source = "")
    {
        if (amount == 0) return;
        TotalScore += amount;
        Debug.Log($"[ScoreManager] {(amount > 0 ? "+" : "")}{amount} from '{source}' -- total now {TotalScore}");
        OnScoreChanged?.Invoke(TotalScore, amount, source);
    }

    // ---------------------------------------------------------------
    // ISaveable -- lets SaveManager persist/restore score automatically,
    // same pattern as TaskManager. No changes needed on SaveManager's end.
    // ---------------------------------------------------------------

    public string SaveKey => "ScoreManager";

    public string CaptureState()
    {
        return JsonUtility.ToJson(new ScoreSaveData { totalScore = TotalScore });
    }

    public void RestoreState(string data)
    {
        if (string.IsNullOrEmpty(data)) return;
        var loaded = JsonUtility.FromJson<ScoreSaveData>(data);
        TotalScore = loaded.totalScore;
        OnScoreChanged?.Invoke(TotalScore, 0, "restore");
    }

    [Serializable]
    private struct ScoreSaveData
    {
        public int totalScore;
    }
}