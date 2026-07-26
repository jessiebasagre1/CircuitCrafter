using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// First screen of the Campaign flow: Continue vs New Game.
///   - Continue is only shown/interactable if a save file exists, and jumps
///     straight into whatever mode the save last held (skips mode select).
///   - New Game is only shown/interactable if NO save exists yet, and just
///     reveals the mode select panel (MainMenuController) — the player then
///     clicks COC1, which SaveManager.SelectMode() treats as a fresh start
///     since there's nothing saved for it.
///
/// This script does not touch MainMenuController's locking logic at all —
/// it only toggles which panel is visible, and (for Continue) calls
/// SaveManager directly using MainMenuController.GetSceneNameForMode()
/// to resolve the scene name.
/// </summary>
public class CampaignMenuController : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject campaignPanel;   // this Continue/New Game screen
    [SerializeField] private GameObject modeSelectPanel; // the panel MainMenuController lives on

    [Header("Buttons")]
    [SerializeField] private Button continueButton;
    [SerializeField] private Button newGameButton;

    [Tooltip("Now lives on CampaignPanel, so this script owns its logic — deletes the save AND refreshes both this panel's Continue/New Game state and MainMenuController's mode lock/status state.")]
    [SerializeField] private Button resetProgressButton;

    [Header("References")]
    [SerializeField] private MainMenuController mainMenuController;

    private void Awake()
    {
        continueButton.onClick.AddListener(OnContinueClicked);
        newGameButton.onClick.AddListener(OnNewGameClicked);

        if (resetProgressButton != null)
            resetProgressButton.onClick.AddListener(OnResetProgressClicked);
    }

    private void OnEnable()
    {
        RefreshCampaignButtons();
    }

    private void RefreshCampaignButtons()
    {
        bool hasSave = SaveManager.Instance.HasSaveFile();

        // Per your call: New Game only ever shows when there's no save at
        // all, so the two states are simply mutually exclusive.
        continueButton.gameObject.SetActive(hasSave);
        newGameButton.gameObject.SetActive(!hasSave);
    }

    private void OnContinueClicked()
    {
        string savedMode = SaveManager.Instance.GetSavedGameMode();
        if (string.IsNullOrEmpty(savedMode))
        {
            Debug.LogWarning("[CampaignMenuController] Continue clicked but no saved game mode found.");
            return;
        }

        string sceneName = mainMenuController.GetSceneNameForMode(savedMode);
        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogWarning($"[CampaignMenuController] No scene name registered for saved mode '{savedMode}'.");
            return;
        }

        // SelectMode sees GetSavedGameMode() == savedMode and sets
        // pendingLoadExisting, so the scene's save data restores automatically.
        SaveManager.Instance.SelectMode(savedMode, sceneName);
    }

    private void OnNewGameClicked()
    {
        // Nothing to save yet — just reveal mode select so the player picks
        // COC1 themselves. MainMenuController's own RefreshButtons() already
        // shows COC1 unlocked and everything else locked.
        campaignPanel.SetActive(false);
        modeSelectPanel.SetActive(true);
    }

    private void OnResetProgressClicked()
    {
        SaveManager.Instance.DeleteSave();

        // Re-check HasSaveFile() so Continue/New Game swap correctly right away.
        RefreshCampaignButtons();

        // Also refresh MainMenuController's own buttons — otherwise mode
        // lock/status stays stale for the next time modeSelectPanel is shown,
        // even though this button lives on CampaignPanel now.
        if (mainMenuController != null)
            mainMenuController.RefreshButtons();
    }
}