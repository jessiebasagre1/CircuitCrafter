using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// First screen of the Campaign flow: Continue vs New Game.
///
///   - Continue is shown only if a save file exists. Clicking it reveals the
///     mode select panel (MainMenuController) WITHOUT touching the save --
///     every mode button there already shows the right Locked/New/In
///     Progress/Completed status, and clicking one resumes it in place via
///     SaveManager.SelectMode(), exactly as before. This is the fix for:
///     once COC1 was finished, Continue used to jump straight back into the
///     COC1 scene with no way to reach COC2 -- now it opens the same picker
///     New Game uses, so every unlocked mode (including newly-unlocked
///     ones) is reachable from Continue too.
///   - New Game is now ALWAYS shown, save or no save. Clicking it wipes any
///     existing save via SaveManager.DeleteSave() -- same as the Reset
///     Progress button -- then reveals the same mode select panel, so the
///     player picks COC1 themselves and SelectMode() treats it as a fresh
///     start since nothing is saved anymore.
///
/// Both buttons land on the exact same modeSelectPanel now; the only
/// difference is whether New Game deletes the save first. Neither button
/// loads a scene directly anymore -- MainMenuController.OnModeClicked()
/// (via SaveManager.SelectMode()) is the single place that decides
/// continue-vs-fresh per mode and actually loads a scene, so that logic --
/// including restoring TaskManager/parts/cables progress on Continue -- is
/// completely untouched by this change.
/// </summary>
public class CampaignMenuController : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject campaignPanel;   // this Continue/New Game screen
    [SerializeField] private GameObject modeSelectPanel; // the panel MainMenuController lives on

    [Header("Buttons")]
    [SerializeField] private Button continueButton;
    [SerializeField] private Button newGameButton;

    [Tooltip("Optional -- lets the player return from the mode select panel back to this Continue/New Game screen. Leave empty to skip; modeSelectPanel just stays up once shown.")]
    [SerializeField] private Button backButton;

    [Tooltip("Deletes the save AND refreshes both this panel's Continue/New Game state and MainMenuController's mode lock/status state. Separate from New Game -- this is a destructive 'wipe everything' control, New Game just starts a fresh run.")]
    [SerializeField] private Button resetProgressButton;

    [Header("References")]
    [SerializeField] private MainMenuController mainMenuController;

    private void Awake()
    {
        continueButton.onClick.AddListener(OnContinueClicked);
        newGameButton.onClick.AddListener(OnNewGameClicked);

        if (backButton != null)
            backButton.onClick.AddListener(OnBackClicked);

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

        // Continue only ever makes sense if there's something to continue.
        // New Game no longer depends on hasSave at all -- it's always
        // available, since clicking it wipes any existing save itself
        // before handing off to mode select.
        continueButton.gameObject.SetActive(hasSave);
        newGameButton.gameObject.SetActive(true);
    }

    private void OnContinueClicked()
    {
        // Used to call SaveManager.SelectMode() directly here and load
        // straight into GetSavedGameMode()'s scene, skipping mode select
        // entirely. That's exactly what broke reaching COC2 after finishing
        // COC1: GetSavedGameMode() stays "COC1" forever (finishing a mode
        // only adds it to completedModes -- it never clears
        // CurrentGameMode/gameMode), so Continue always reloaded COC1 with
        // no way to reach anything past it.
        //
        // Showing mode select instead fixes that: every unlocked mode is
        // clickable there, and clicking the one matching
        // GetSavedGameMode() still resumes it in place via
        // MainMenuController.OnModeClicked -> SaveManager.SelectMode(),
        // so in-progress task/part/cable state restores exactly as before.
        ShowModeSelect();
    }

    private void OnNewGameClicked()
    {
        // Unlike Continue, New Game always means "start over" -- wipe
        // whatever save exists (completed modes included) so every mode
        // button resets to its fresh-start state, then let the player pick
        // COC1 themselves same as before.
        if (SaveManager.Instance.HasSaveFile())
            SaveManager.Instance.DeleteSave();

        ShowModeSelect();
    }

    /// <summary>Shared by both Continue and New Game -- refreshes
    /// MainMenuController's lock/status labels (they depend on save state,
    /// which New Game may have just wiped) and swaps to the mode select
    /// panel.</summary>
    private void ShowModeSelect()
    {
        if (mainMenuController != null)
            mainMenuController.RefreshButtons();

        campaignPanel.SetActive(false);
        modeSelectPanel.SetActive(true);
    }

    private void OnBackClicked()
    {
        modeSelectPanel.SetActive(false);
        campaignPanel.SetActive(true);
        RefreshCampaignButtons();
    }

    private void OnResetProgressClicked()
    {
        SaveManager.Instance.DeleteSave();

        // Re-check HasSaveFile() so Continue/New Game reflect the wipe
        // right away.
        RefreshCampaignButtons();

        // Also refresh MainMenuController's own buttons -- otherwise mode
        // lock/status stays stale for the next time modeSelectPanel is
        // shown, even though this button lives on CampaignPanel now.
        if (mainMenuController != null)
            mainMenuController.RefreshButtons();
    }
}