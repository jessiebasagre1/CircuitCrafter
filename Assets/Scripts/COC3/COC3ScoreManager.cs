using UnityEngine;
using TMPro;

/// <summary>
/// Tracks the learner's score and mistake count for the whole COC 3 activity,
/// and keeps the on-screen score/mistake labels up to date.
/// </summary>
public class COC3ScoreManager : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Text element showing the current score. Optional.")]
    public TMP_Text scoreText;

    [Tooltip("Text element showing the current mistake count. Optional.")]
    public TMP_Text mistakeText;

    /// <summary>Current score. Read-only from outside this script.</summary>
    public int Score { get; private set; }

    /// <summary>Total mistakes made so far. Read-only from outside this script.</summary>
    public int Mistakes { get; private set; }

    /// <summary>Call when a step is completed correctly.</summary>
    public void AddScore(int amount)
    {
        Score += amount;
        RefreshUI();
    }

    /// <summary>Call when the learner gets a step wrong. Deducts a penalty and logs the mistake.</summary>
    public void AddMistake(int penalty)
    {
        Mistakes++;
        Score = Mathf.Max(0, Score - penalty); // never let the score go negative
        RefreshUI();
    }

    /// <summary>Resets score and mistakes to zero, used when starting or restarting the activity.</summary>
    public void ResetScore()
    {
        Score = 0;
        Mistakes = 0;
        RefreshUI();
    }

    private void RefreshUI()
    {
        if (scoreText != null) scoreText.text = $"Score: {Score}";
        if (mistakeText != null) mistakeText.text = $"Mistakes: {Mistakes}";
    }
}