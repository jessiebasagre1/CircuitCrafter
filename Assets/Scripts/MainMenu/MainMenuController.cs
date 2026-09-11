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

    [Header("Testing")]
    [Tooltip("TESTING ONLY. Makes every COC button clickable without finishing the one before it, " +
             "so you can jump straight into COC3 or COC4 to work on them.\n\n" +
             "It does NOT write anything to the save file - it only stops the menu refusing the " +
             "click. Real progress stays exactly as it was, so ticking this cannot fake a " +
             "learner's record, and unticking it puts every lock straight back.\n\n" +
             "It is also ignored in a release build. It works in the Editor, and in a build made " +
             "with Development Build ticked; a normal build for students behaves as if this were " +
             "off, even if you leave it ticked by accident.")]
    [SerializeField] private bool unlockAllForTesting = false;

    /// <summary>
    /// Whether the testing unlock is actually in force.
    ///
    /// The build check is the whole point of this being a property rather than a plain field read.
    /// A tick box left on by mistake is the easiest thing in the world to ship, and the cost of
    /// shipping this one would be every student able to skip to COC4 on day one - which, for a
    /// module that is meant to certify a sequence of competencies, is not a cosmetic bug.
    /// </summary>
    private bool TestingUnlockActive
    {
        get
        {
#if UNITY_EDITOR
            return unlockAllForTesting;
#else
            return unlockAllForTesting && Debug.isDebugBuild;
#endif
        }
    }

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

        if (TestingUnlockActive)
            Debug.LogWarning("[MainMenuController] Unlock All For Testing is ON - every COC is " +
                             "clickable regardless of progress. Untick it before building for " +
                             "students. (Nothing has been written to the save.)", this);

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
                // Says so on the button itself. A mode that is only open because of the testing
                // switch should never be mistaken for one the learner actually earned - not by a
                // student, and not by you three weeks from now wondering why COC4 is open.
                bool onlyOpenForTesting = TestingUnlockActive && i > 0 &&
                                          !SaveManager.Instance.IsModeCompleted(gameModes[i - 1].modeId);

                if (!unlocked)
                    modeStatusTexts[i].text = "Locked";
                else if (onlyOpenForTesting)
                    modeStatusTexts[i].text = "TESTING";
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

        // The testing override lives here rather than at the button, so OnModeClicked's own safety
        // check agrees with it. Put it only on the button and the button would light up while the
        // click was still refused - which looks exactly like a broken menu.
        if (TestingUnlockActive) return true;

        return SaveManager.Instance.IsModeCompleted(gameModes[index - 1].modeId);
    }

    // ===================================================================
    // Testing helpers - right-click this component's header in the Inspector.
    //
    // These WRITE TO THE SAVE FILE, unlike the tick box above. Use them when something other than
    // this menu needs to believe a mode is finished - checking that COC2's completion really does
    // unlock COC3, say, without replaying COC2 - and use Reset All Progress afterwards.
    // ===================================================================

    [ContextMenu("Testing: mark every mode complete (writes to the save)")]
    private void TestingMarkEverythingComplete()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[MainMenuController] Press Play first - SaveManager only exists at " +
                             "runtime, so there is nothing to write to yet.", this);
            return;
        }

        if (SaveManager.Instance == null || gameModes == null) return;

        foreach (GameModeOption mode in gameModes)
        {
            if (mode == null || string.IsNullOrWhiteSpace(mode.modeId)) continue;
            SaveManager.Instance.MarkModeComplete(mode.modeId.Trim());
        }

        Debug.LogWarning("[MainMenuController] Every mode is now recorded as COMPLETE in the save " +
                         "file. This is real saved progress, not a display trick - use 'Testing: " +
                         "reset all progress' to clear it.", this);
        RefreshButtons();
    }

    [ContextMenu("Testing: reset all progress")]
    private void TestingResetProgress()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[MainMenuController] Press Play first.", this);
            return;
        }

        if (SaveManager.Instance == null) return;

        SaveManager.Instance.DeleteSave();
        Debug.LogWarning("[MainMenuController] Save deleted - every mode is locked again except COC1.", this);
        RefreshButtons();
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