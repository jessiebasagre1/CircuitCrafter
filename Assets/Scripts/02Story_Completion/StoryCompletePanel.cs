using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// "You've completed story mode." A panel with four ways out: Retry, Main Menu, Take Assessment,
/// and Next - straight into the next COC's Story Mode.
///
/// ATTACH THIS TO: the completion panel object in each module scene. Build it with
/// CircuitCrafter → Build Story Complete Panel, or by hand. Add the Next button with
/// CircuitCrafter → Add Next Button To Story Complete Panel.
///
/// SHOW IT by calling Show() - from a button's OnClick, or from any script with
/// StoryCompletePanel.ShowNow().
///
/// Title, message and the scene names are all in the Inspector. The Next button is the one
/// exception: it follows the Main Menu's own COC list (MainMenuController > Game Modes) and starts
/// the next COC exactly as that menu's button would, through SaveManager.SelectMode - so the order
/// and the scene names live in one place, and the next COC is only offered once the menu itself
/// would unlock it.
/// </summary>
public class StoryCompletePanel : MonoBehaviour
{
    public static StoryCompletePanel Instance { get; private set; }

    [Header("References")]
    [Tooltip("The object switched on and off. Leave empty to use this GameObject.")]
    [SerializeField] private GameObject panelRoot;

    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text messageText;

    [Header("Words")]
    [SerializeField] private string title = "Story mode complete";

    [TextArea(2, 4)]
    [SerializeField]
    private string message =
        "You have finished every task in this module. Practise it again, or take the assessment when you are ready to be judged on it.";

    [Header("Buttons")]
    [SerializeField] private Button retryButton;
    [SerializeField] private Button mainMenuButton;
    [SerializeField] private Button assessmentButton;

    [Tooltip("Goes to the next COC's Story Mode. Added by CircuitCrafter → Add Next Button To Story " +
             "Complete Panel. Hidden when there is no next COC (COC4) or it is not unlocked yet.")]
    [SerializeField] private Button nextButton;

    [Header("Where the buttons go")]
    [Tooltip("Leave empty to reload the scene this panel is in - which is what Retry almost always means.")]
    [SerializeField] private string retrySceneName = "";

    [Tooltip("Your main menu scene, exactly as it appears in Build Settings.")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Tooltip("The assessment scene for this module. Leave empty to fire On Take Assessment instead - use that if your assessment starts from a panel rather than a scene.")]
    [SerializeField] private string assessmentSceneName = "";

    [Header("Behaviour")]
    [Tooltip("Leave on. Frees the mouse and freezes the player while the panel is up - without it the cursor stays locked and none of the three buttons can be clicked.")]
    [SerializeField] private bool freeCursorWhileOpen = true;

    [Header("Next COC")]
    [Tooltip("Offer the Next button when the next COC is unlocked. The order and scene names come from " +
             "the Main Menu (MainMenuController > Game Modes), so nothing here changes when a scene is " +
             "renamed. Play from the MainMenu scene to see it - started straight from a COC scene in the " +
             "Editor, the list has not been read yet and the button stays hidden.")]
    [SerializeField] private bool showNextButton = true;

    [Tooltip("The Next button's label. {0} becomes the next COC's id, e.g. \"Next: COC2\".")]
    [SerializeField] private string nextButtonText = "Next: {0}";

    [Header("Events (optional)")]
    public UnityEvent onShown;
    public UnityEvent onRetry;
    public UnityEvent onMainMenu;
    public UnityEvent onTakeAssessment;
    public UnityEvent onNext;

    // ======================================================================

    private bool openRequested;
    private bool tookControl;
    private bool leaving;
    private float nextCheckAt;

    public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

    private void Awake()
    {
        if (panelRoot == null) panelRoot = gameObject;

        Instance = this;

        // Wired here, not in the Inspector. Add an OnClick entry as well and every
        // press fires twice - which on a scene-loading button means loading during
        // a load. Leave the buttons' OnClick lists EMPTY.
        if (retryButton != null) retryButton.onClick.AddListener(Retry);
        if (mainMenuButton != null) mainMenuButton.onClick.AddListener(MainMenu);
        if (assessmentButton != null) assessmentButton.onClick.AddListener(TakeAssessment);
        if (nextButton != null) nextButton.onClick.AddListener(Next);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        // Guarded by openRequested. If this panel's object starts INACTIVE, Unity
        // never runs Start - but a UnityEvent can still reach Show(), which
        // activates the object, which makes Unity run Start in the same frame,
        // which would hide it again. The learner sees a flash and has to trigger it
        // twice.
        if (!openRequested) panelRoot.SetActive(false);
    }

    private void OnDisable()
    {
        ReleaseControl();
    }

    // ======================================================================

    /// <summary>Call this when the module ends. Static, so the calling script needs
    /// no reference: <c>StoryCompletePanel.ShowNow();</c></summary>
    public static void ShowNow()
    {
        if (Instance != null) { Instance.Show(); return; }
        Debug.LogWarning("[StoryCompletePanel] ShowNow() was called but there is no StoryCompletePanel in this scene.");
    }

    /// <summary>Wire this to your last button's OnClick, or call it from code.</summary>
    [ContextMenu("Show now (test)")]
    public void Show()
    {
        if (IsOpen) return;

        openRequested = true;
        panelRoot.SetActive(true);

        if (titleText != null) titleText.text = title;
        if (messageText != null) messageText.text = message;

        // Hidden rather than left dead. A button that does nothing is worse than
        // no button - the learner presses it, nothing happens, and they conclude
        // the game is broken.
        if (assessmentButton != null)
            assessmentButton.gameObject.SetActive(
                !string.IsNullOrWhiteSpace(assessmentSceneName) || onTakeAssessment.GetPersistentEventCount() > 0);

        // Same rule: no next COC (COC4) or not unlocked yet → no Next button.
        RefreshNextButton();

        TakeControl();

        Debug.Log("[StoryCompletePanel] Shown.", this);
        onShown?.Invoke();
    }

    public void Hide()
    {
        ReleaseControl();
        panelRoot.SetActive(false);
    }

    // ======================================================================

    public void Retry()
    {
        onRetry?.Invoke();

        string scene = string.IsNullOrWhiteSpace(retrySceneName)
            ? SceneManager.GetActiveScene().name
            : retrySceneName;

        Go(scene, "Retry");
    }

    public void MainMenu()
    {
        onMainMenu?.Invoke();
        Go(mainMenuSceneName, "Main Menu");
    }

    public void TakeAssessment()
    {
        onTakeAssessment?.Invoke();

        if (string.IsNullOrWhiteSpace(assessmentSceneName))
        {
            // The event has already fired; a scene name was never set, so the event
            // IS the destination. Releasing control matters either way.
            ReleaseControl();
            return;
        }

        Go(assessmentSceneName, "Take Assessment");
    }

    /// <summary>
    /// Starts the next COC's Story Mode - exactly what that COC's button on the Main Menu does
    /// (SaveManager.SelectMode), so it starts fresh or continues by the same rule, and the save
    /// knows which COC is being played.
    /// </summary>
    public void Next()
    {
        if (leaving) return;   // a double tap would load the scene twice

        if (!TryGetNext(out MainMenuController.GameModeOption next))
        {
            Debug.LogWarning("[StoryCompletePanel] Next was pressed, but there is no unlocked COC after this one.", this);
            RefreshNextButton();
            return;
        }

        leaving = true;
        onNext?.Invoke();

        // Same reason as Go(): a lock still held when the scene changes is still held in the next one.
        tookControl = false;
        PlayerInputLock.ReleaseAll();

        Debug.Log($"[StoryCompletePanel] Next → {next.modeId} ('{next.sceneName}').", this);
        SaveManager.Instance.SelectMode(next.modeId.Trim(), next.sceneName.Trim());
    }

    private void Update()
    {
        // COC1-3 mark themselves complete from their own scripts. If that lands a moment after the
        // panel opened, the Next button appears as soon as it does instead of never.
        if (nextButton == null || !IsOpen || nextButton.gameObject.activeSelf) return;
        if (Time.unscaledTime < nextCheckAt) return;
        nextCheckAt = Time.unscaledTime + 0.5f;
        RefreshNextButton();
    }

    private void RefreshNextButton()
    {
        if (nextButton == null) return;

        bool show = TryGetNext(out MainMenuController.GameModeOption next);
        nextButton.gameObject.SetActive(show);
        if (!show) return;

        TMP_Text label = nextButton.GetComponentInChildren<TMP_Text>(true);
        if (label != null && !string.IsNullOrEmpty(nextButtonText))
            label.text = string.Format(nextButtonText, next.modeId.Trim());
    }

    /// <summary>
    /// The COC after the one being played, if the learner may go there now. The same rule as the
    /// Main Menu's lock: the next COC opens when THIS one is recorded as complete.
    /// </summary>
    private bool TryGetNext(out MainMenuController.GameModeOption next)
    {
        next = null;
        if (!showNextButton || AssessmentMode.IsActive || SaveManager.Instance == null) return false;

        string current = SaveManager.Instance.CurrentGameMode;
        if (string.IsNullOrWhiteSpace(current))
            current = MainMenuController.ModeIdForScene(SceneManager.GetActiveScene().name);
        if (string.IsNullOrWhiteSpace(current)) return false;

        if (!MainMenuController.TryGetNextMode(current, out next)) return false;
        return SaveManager.Instance.IsModeCompleted(current.Trim());
    }

    private void Go(string sceneName, string what)
    {
        // ReleaseAll, not Release. PlayerInputLock is a static counter and statics
        // SURVIVE A SCENE LOAD - so any lock still held when the scene changes is
        // still held in the new one, and the player spawns unable to move with
        // nothing in the Console to explain it.
        //
        // Releasing only this panel's own lock is not enough, because it is rarely
        // the only one outstanding: the learner presses Submit while standing at a
        // station, and the station is holding a lock of its own. Tearing the scene
        // down is exactly the moment where zeroing the counter is the correct
        // thing to do rather than a workaround.
        tookControl = false;
        PlayerInputLock.ReleaseAll();

        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning($"[StoryCompletePanel] {what} has no scene name set, and no event wired. Nothing happened.", this);
            return;
        }

        Debug.Log($"[StoryCompletePanel] {what} → loading '{sceneName}'.", this);
        SceneManager.LoadScene(sceneName);
    }

    // ======================================================================

    private void TakeControl()
    {
        if (!freeCursorWhileOpen || tookControl) return;

        tookControl = true;
        PlayerInputLock.Acquire();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void ReleaseControl()
    {
        if (!tookControl) return;

        tookControl = false;
        PlayerInputLock.Release();
    }
}