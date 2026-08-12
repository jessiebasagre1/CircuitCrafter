using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// In-game pause menu, usable from any gameplay scene (COC1-4). Opens via a
/// corner "Menu" button (works on mobile, matching the rest of the project's
/// mobile support) or the Escape key on desktop. Offers Resume and
/// Main Menu.
///
/// Main Menu always checkpoints first — this is a deliberate, infrequent
/// player action (unlike per-part drags), so it's worth the extra save even
/// though regular gameplay only autosaves on task completion. This guarantees
/// nobody loses progress just by backing out mid-task.
/// </summary>
public class PauseMenuController : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject pausePanel; // contains Resume + Main Menu buttons

    [Header("Buttons")]
    [Tooltip("Small corner button, always visible during gameplay, that opens the pause panel. Needed for mobile since there's no Escape key.")]
    [SerializeField] private Button openMenuButton;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button mainMenuButton;

    [Header("Other Panels (for Escape back-stack)")]
    [Tooltip("Optional — if assigned, Escape closes the Camera Settings panel first (if it's open) instead of opening the pause menu on top of it. Settings itself is still opened via its own dedicated gameplay button, unchanged.")]
    [SerializeField] private CameraSettingsUI cameraSettingsUI;

    [Header("Scene")]
    [Tooltip("Exact scene name of your Main Menu scene.")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    [Tooltip("If true, Time.timeScale is set to 0 while paused (freezes gameplay, animations, and OSInstallManager's timed stages). Turn off if you don't want gameplay to freeze.")]
    [SerializeField] private bool freezeTimeWhilePaused = true;

    public bool IsPaused { get; private set; }

    // Whatever the cursor's lock/visibility state was right before we opened
    // the pause panel (Locked+hidden while walking via MouseLook/
    // StationCameraSwitch, or None+visible at the assembly station) — restored
    // exactly on Resume so we don't clobber whichever mode the player was in.
    private CursorLockMode previousLockState;
    private bool previousCursorVisible;

    private void Awake()
    {
        if (openMenuButton != null) openMenuButton.onClick.AddListener(OpenMenu);
        if (resumeButton != null) resumeButton.onClick.AddListener(CloseMenu);
        if (mainMenuButton != null) mainMenuButton.onClick.AddListener(OnMainMenuClicked);

        if (pausePanel != null) pausePanel.SetActive(false);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            HandleEscape();
    }

    private void HandleEscape()
    {
        // Back-stack: if Settings is open, Escape closes that first instead
        // of opening the pause menu on top of it. Settings is still only
        // ever opened via its own dedicated gameplay button (openButton on
        // CameraSettingsUI) — Escape here only ever closes it.
        if (cameraSettingsUI != null && cameraSettingsUI.settingsPanel != null
            && cameraSettingsUI.settingsPanel.activeSelf)
        {
            cameraSettingsUI.ClosePanel();
            return;
        }

        TogglePause();
    }

    private void TogglePause()
    {
        if (IsPaused) CloseMenu();
        else OpenMenu();
    }

    private void OpenMenu()
    {
        IsPaused = true;
        if (pausePanel != null) pausePanel.SetActive(true);
        if (freezeTimeWhilePaused) Time.timeScale = 0f;

        // MouseLook/StationCameraSwitch lock and hide the cursor while
        // walking - without this, the pause panel's buttons are on screen
        // but the player has no way to move a (locked, invisible) cursor
        // onto them to click.
        previousLockState = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void CloseMenu()
    {
        IsPaused = false;
        if (pausePanel != null) pausePanel.SetActive(false);
        if (freezeTimeWhilePaused) Time.timeScale = 1f;

        Cursor.lockState = previousLockState;
        Cursor.visible = previousCursorVisible;
    }

    private void OnMainMenuClicked()
    {
        // FIX: previously this jumped straight to SaveCheckpoint()+LoadScene()
        // without ever closing the pause panel or clearing IsPaused/timeScale/
        // cursor state first. If this GameObject ever survives the scene load
        // (e.g. it's sitting on a DontDestroyOnLoad "Managers" root — see the
        // TaskManager/HotbarManager/DialogManager note below), an active,
        // input-frozen pausePanel would be left sitting on top of whatever
        // loads next, eating every click. Closing it properly first makes
        // this safe regardless of how the Managers hierarchy is set up.
        CloseMenu();

        if (SaveManager.Instance != null)
            SaveManager.Instance.SaveCheckpoint();

        SceneManager.LoadScene(mainMenuSceneName);
    }
}