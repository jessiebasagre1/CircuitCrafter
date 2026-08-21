using UnityEngine;

/// <summary>
/// Ties COC 3 into the campaign save system, the same way COC1CompletionManager
/// does for COC 1 - but WITHOUT needing any edit to COC3Manager.
///
/// Put this on the same GameObject as your COC3Manager.
///
/// Two jobs:
///   1. Awake() claims the game mode ("COC3") on SaveManager, so any checkpoint
///      written during this scene records the right mode even when you press Play
///      directly on the COC3 scene instead of coming through the Main Menu.
///   2. MarkCompleted() calls SaveManager.MarkModeComplete("COC3"), which unlocks
///      COC4 on the mode-select screen and saves immediately.
///
/// MarkCompleted() is called for you by COC3CompletionNotifier, which sits on the
/// completion screen and fires the moment COC3Manager switches that panel on.
/// You can also wire it to any button's OnClick() if you prefer to control it manually.
/// </summary>
public class COC3CompletionManager : MonoBehaviour
{
    public const string ModeId = "COC3";

    [Header("Optional Pass Requirements")]
    [Tooltip("The COC3ScoreManager to read the final score/mistakes from. Leave empty to always pass.")]
    public COC3ScoreManager scoreManager;

    [Tooltip("Minimum final score needed for COC3 to count as completed. 0 = no minimum.")]
    public int minimumScore = 0;

    [Tooltip("Maximum mistakes allowed. Set to -1 for unlimited.")]
    public int maximumMistakes = -1;

    [Header("Optional Result Badges")]
    [Tooltip("Shown on the completion screen when the learner passed. Optional.")]
    public GameObject passedBadge;

    [Tooltip("Shown on the completion screen when the learner did not meet the requirements. Optional.")]
    public GameObject failedBadge;

    /// <summary>True once this session's run has been recorded, so it never double-saves.</summary>
    public bool AlreadyMarked { get; private set; }

    // Guards against a false "you finished!" during scene load.
    //
    // If you leave the completion screen ACTIVE in the Editor (very easy to do while
    // you are styling it), Unity fires its OnEnable during scene load - before
    // COC3Manager.Start() gets the chance to switch it off. Without this flag that
    // would instantly mark COC3 complete and unlock COC4 the moment the scene opened.
    // Unity runs every OnEnable before any Start, so anything that arrives before
    // our Start() cannot be a real completion.
    private bool acceptingCompletions;

    private void Awake()
    {
        if (SaveManager.Instance != null)
            SaveManager.Instance.EnsureGameMode(ModeId);
    }

    private void Start()
    {
        acceptingCompletions = true;
    }

    /// <summary>
    /// Evaluates the run and, if it passes, marks COC3 complete in the save file.
    /// Safe to call more than once - only the first successful call writes anything.
    /// </summary>
    public void MarkCompleted()
    {
        if (!acceptingCompletions)
        {
            // Scene-load activation, not a real finish. See the note above.
            return;
        }

        bool passed = Passed();

        if (passedBadge != null) passedBadge.SetActive(passed);
        if (failedBadge != null) failedBadge.SetActive(!passed);

        if (!passed)
        {
            Debug.Log($"[COC3CompletionManager] Finished, but the pass requirements were not met " +
                      $"(score {CurrentScore}/{minimumScore}, mistakes {CurrentMistakes}/{maximumMistakes}). Not marking COC3 complete.");
            return;
        }

        if (AlreadyMarked) return;

        if (SaveManager.Instance == null)
        {
            Debug.LogWarning("[COC3CompletionManager] No SaveManager in the scene - progress was not saved.");
            return;
        }

        AlreadyMarked = true;

        if (!SaveManager.Instance.IsModeCompleted(ModeId))
        {
            Debug.Log("[COC3CompletionManager] All COC3 steps finished - marking COC3 complete (this unlocks COC4).");
            SaveManager.Instance.MarkModeComplete(ModeId);
        }
        else
        {
            // Already completed on a previous run - still write a checkpoint so the
            // newer score/progress label is stored.
            SaveManager.Instance.SaveCheckpoint();
        }
    }

    private int CurrentScore => scoreManager != null ? scoreManager.Score : 0;
    private int CurrentMistakes => scoreManager != null ? scoreManager.Mistakes : 0;

    private bool Passed()
    {
        if (scoreManager == null) return true;
        if (minimumScore > 0 && CurrentScore < minimumScore) return false;
        if (maximumMistakes >= 0 && CurrentMistakes > maximumMistakes) return false;
        return true;
    }

    /// <summary>Wire this to your "Retry" button alongside COC3Manager.RestartActivity()
    /// so a second attempt can be recorded again.</summary>
    public void ResetForRetry()
    {
        AlreadyMarked = false;
        if (passedBadge != null) passedBadge.SetActive(false);
        if (failedBadge != null) failedBadge.SetActive(false);
    }
}