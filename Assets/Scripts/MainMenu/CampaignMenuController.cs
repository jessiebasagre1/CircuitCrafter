using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// First screen of the Campaign flow: Continue vs New Game.
///
///   - Continue is shown only if a save file exists. Clicking it pops up the mode select panel
///     (MainMenuController) WITHOUT touching the save -- every mode button there already shows
///     the right Locked/New/In Progress/Completed status, and clicking one resumes it in place
///     via SaveManager.SelectMode(), exactly as before.
///   - New Game is always shown, save or no save. Clicking it wipes any existing save via
///     SaveManager.DeleteSave() -- same as the Reset Progress button -- then pops up the same
///     mode select panel, so the player picks COC1 themselves and SelectMode() treats it as a
///     fresh start since nothing is saved anymore.
///
/// POP-UP, NOT A SCREEN SWAP
/// modeSelectPanel now opens ON TOP of campaignPanel instead of replacing it -- campaignPanel is
/// never deactivated here. That means:
///   - modeSelectPanel must NOT be listed in MenuNavigator's Screens array. MenuNavigator's
///     Apply() deactivates every screen except the one being shown, which would hide
///     campaignPanel out from under the popup and break the dimmed-backdrop effect.
///   - modeSelectPanel needs to actually render on top. Easiest way: give it its own Canvas
///     component with a higher "Order in Layer" than the main menu Canvas (e.g. main = 0,
///     popup = 10), rather than relying on sibling order.
///   - Put a full-screen semi-transparent Image as modeSelectPanel's first child for the dimmed
///     backdrop. Optional: add a Button component to that same Image and wire its OnClick to
///     CloseModeSelect() below, so clicking outside the card also dismisses it.
///
/// Neither button loads a scene directly -- MainMenuController.OnModeClicked() (via
/// SaveManager.SelectMode()) is the single place that decides continue-vs-fresh per mode and
/// actually loads a scene, so that logic -- including restoring TaskManager/parts/cables
/// progress on Continue -- is completely untouched by this change.
///
/// Setup:
///   1. Add this to your Story Mode panel (the one MenuNavigator shows for "Story Mode" on the
///      Main Menu) -- this panel IS campaignPanel.
///   2. Build modeSelectPanel as a separate popup object per the notes above: its own Canvas,
///      higher sort order, dim backdrop + centered card holding MainMenuController's COC1-COC4
///      buttons. Leave it inactive by default and do NOT add it to MenuNavigator's Screens array.
///   3. Assign campaignPanel (this panel) and modeSelectPanel (the popup).
///   4. Assign continueButton and newGameButton.
///   5. Optional: assign backButton -- a close/"X" button ON the popup card. Wiring it here (not
///      via the popup's own OnClick list) is what lets this script keep the pop-up and the
///      Continue/New Game state in sync.
///   6. Optional: assign resetProgressButton if you still want a destructive "wipe everything"
///      control on this screen.
///   7. Assign mainMenuController -- the MainMenuController component living on modeSelectPanel.
/// </summary>
public class CampaignMenuController : MonoBehaviour
{
    [Header("Panels")]
    [Tooltip("This Continue/New Game screen. Stays active behind the pop-up -- never deactivated by this script.")]
    [SerializeField] private GameObject campaignPanel;

    [Tooltip("The COC1-COC4 pop-up. Its own Canvas, higher sort order, NOT listed in MenuNavigator's Screens array.")]
    [SerializeField] private GameObject modeSelectPanel;

    [Header("Buttons")]
    [SerializeField] private Button continueButton;
    [SerializeField] private Button newGameButton;

    [Tooltip("Optional -- a close/back button ON the pop-up card. Leave empty to skip; modeSelectPanel just stays up once shown, dismissible only by picking a mode.")]
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
            backButton.onClick.AddListener(CloseModeSelect);

        if (resetProgressButton != null)
            resetProgressButton.onClick.AddListener(OnResetProgressClicked);

        // Popup starts closed -- campaignPanel starts however MenuNavigator/Unity leaves it.
        if (modeSelectPanel != null) modeSelectPanel.SetActive(false);
    }

    private void OnEnable()
    {
        RefreshCampaignButtons();
    }

    private void RefreshCampaignButtons()
    {
        if (SaveManager.Instance == null)
        {
            // Can happen on the very first frame if this panel starts active in the Hierarchy -
            // OnEnable can fire before SaveManager's own Awake() has set Instance. Bail out
            // quietly; OnEnable will fire again for real once MenuNavigator actually shows this
            // panel later, well after every Awake() in the scene has run.
            Debug.LogWarning("[CampaignMenuController] SaveManager.Instance not ready yet - skipping refresh.", this);
            return;
        }

        bool hasSave = SaveManager.Instance.HasSaveFile();

        // Continue only ever makes sense if there's something to continue.
        // New Game no longer depends on hasSave at all -- it's always
        // available, since clicking it wipes any existing save itself
        // before popping up mode select.
        continueButton.gameObject.SetActive(hasSave);
        newGameButton.gameObject.SetActive(true);
    }

    private void OnContinueClicked()
    {
        if (SaveManager.Instance == null)
        {
            Debug.LogError("[CampaignMenuController] No SaveManager in the scene - add a Managers " +
                           "object with a SaveManager component. Nothing to continue without it.", this);
            return;
        }

        // Used to call SaveManager.SelectMode() directly here and load
        // straight into GetSavedGameMode()'s scene, skipping mode select
        // entirely. That's exactly what broke reaching COC2 after finishing
        // COC1: GetSavedGameMode() stays "COC1" forever (finishing a mode
        // only adds it to completedModes -- it never clears
        // CurrentGameMode/gameMode), so Continue always reloaded COC1 with
        // no way to reach anything past it.
        //
        // Popping up mode select instead fixes that: every unlocked mode is
        // clickable there, and clicking the one matching
        // GetSavedGameMode() still resumes it in place via
        // MainMenuController.OnModeClicked -> SaveManager.SelectMode(),
        // so in-progress task/part/cable state restores exactly as before.
        ShowModeSelect();
    }

    private void OnNewGameClicked()
    {
        if (SaveManager.Instance == null)
        {
            Debug.LogError("[CampaignMenuController] No SaveManager in the scene - add a Managers " +
                           "object with a SaveManager component. Can't start a new game without it.", this);
            return;
        }

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
    /// which New Game may have just wiped) and pops the mode select panel
    /// up on top of campaignPanel, which stays visible (dimmed) behind it.</summary>
    private void ShowModeSelect()
    {
        if (mainMenuController != null)
            mainMenuController.RefreshButtons();

        if (modeSelectPanel != null) modeSelectPanel.SetActive(true);
    }

    /// <summary>Closes the pop-up and returns to the plain Continue/New Game screen underneath.
    /// Public so a backdrop-click button can call it too, in addition to a dedicated close
    /// button wired through backButton above.</summary>
    public void CloseModeSelect()
    {
        if (modeSelectPanel != null) modeSelectPanel.SetActive(false);

        // Re-check hasSave -- picking a mode can create a save file where none existed before,
        // which should flip Continue on if the player backs out and reopens the popup.
        RefreshCampaignButtons();
    }

    private void OnResetProgressClicked()
    {
        if (SaveManager.Instance == null)
        {
            Debug.LogError("[CampaignMenuController] No SaveManager in the scene - add a Managers " +
                           "object with a SaveManager component. Nothing to reset without it.", this);
            return;
        }

        SaveManager.Instance.DeleteSave();

        // Re-check HasSaveFile() so Continue/New Game reflect the wipe
        // right away.
        RefreshCampaignButtons();

        // Also refresh MainMenuController's own buttons -- otherwise mode
        // lock/status stays stale for the next time modeSelectPanel is
        // popped up.
        if (mainMenuController != null)
            mainMenuController.RefreshButtons();
    }
}