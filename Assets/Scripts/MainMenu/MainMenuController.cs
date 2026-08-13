using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Main menu: one button per COC mode. COC1 is always playable. COC2-COC4
/// are locked (non-interactable) until the previous mode is marked complete
/// via SaveManager.MarkModeComplete(). Clicking an unlocked button either
/// continues that mode's saved progress or starts it fresh, decided
/// automatically by SaveManager.SelectMode().
/// </summary>
public class MainMenuController : MonoBehaviour
{
    [System.Serializable]
    public class GameModeOption
    {
        public string modeId;       // e.g. "COC1" � stored in the save file
        public string displayName;  // e.g. "COC1 - Computer Assembly"
        public string sceneName;    // exact Unity scene name to load
    }

    [Header("Game Modes (in order � index 0 must be COC1)")]
    [SerializeField] private GameModeOption[] gameModes;

    [Header("Mode Buttons (same order as gameModes)")]
    [SerializeField] private Button[] modeButtons;

    [Tooltip("Optional � shows 'Continue' vs 'New' under/near each button's label based on saved progress.")]
    [SerializeField] private TMP_Text[] modeStatusTexts;

    [Header("Optional")]
    [SerializeField] private Button resetProgressButton;

    private void Awake()
    {
        // MouseLook.cs locks and hides the cursor during gameplay and never
        // resets it, so returning to the Main Menu from any gameplay scene
        // otherwise leaves the cursor locked/invisible, making Continue and
        // every other menu button unclickable.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        for (int i = 0; i < modeButtons.Length && i < gameModes.Length; i++)
        {
            int index = i; // capture for the closure
            modeButtons[i].onClick.AddListener(() => OnModeClicked(index));
        }

        if (resetProgressButton != null)
            resetProgressButton.onClick.AddListener(OnResetProgressClicked);

        RefreshButtons();
    }

    /// <summary>Public so CampaignMenuController can force a re-check of mode
    /// lock/status state after actions like Reset Progress.</summary>
    public void RefreshButtons()
    {
        string savedMode = SaveManager.Instance.GetSavedGameMode();

        for (int i = 0; i < gameModes.Length && i < modeButtons.Length; i++)
        {
            var mode = gameModes[i];
            bool unlocked = IsUnlocked(i);
            modeButtons[i].interactable = unlocked;

            var label = modeButtons[i].GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = mode.displayName;

            if (modeStatusTexts != null && i < modeStatusTexts.Length && modeStatusTexts[i] != null)
            {
                if (!unlocked)
                    modeStatusTexts[i].text = "Locked";
                else if (savedMode == mode.modeId)
                    modeStatusTexts[i].text = SaveManager.Instance.GetSavedProgressLabel() ?? "In progress";
                else if (SaveManager.Instance.IsModeCompleted(mode.modeId))
                    modeStatusTexts[i].text = "Completed";
                else
                    modeStatusTexts[i].text = "New";
            }
        }
    }

    /// <summary>COC1 (index 0) is always unlocked. Every later mode requires
    /// the immediately preceding mode to be marked complete.</summary>
    private bool IsUnlocked(int index)
    {
        if (index == 0) return true;
        return SaveManager.Instance.IsModeCompleted(gameModes[index - 1].modeId);
    }

    private void OnModeClicked(int index)
    {
        if (!IsUnlocked(index)) return; // safety check � button should already be non-interactable
        var mode = gameModes[index];
        SaveManager.Instance.SelectMode(mode.modeId, mode.sceneName);
    }

    private void OnResetProgressClicked()
    {
        SaveManager.Instance.DeleteSave();
        RefreshButtons();
    }

    /// <summary>Looks up the scene name for a given mode ID � used by
    /// CampaignMenuController's Continue button to resume the saved mode
    /// without going through the mode select panel.</summary>
    public string GetSceneNameForMode(string modeId)
    {
        foreach (var mode in gameModes)
        {
            if (mode.modeId == modeId) return mode.sceneName;
        }
        return null;
    }
}