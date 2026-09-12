using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The Assessment Mode screen: four buttons, COC1 to COC4.
///
/// ATTACH THIS TO: the Assessment panel on your Main Menu.
///
/// HOW IT DIFFERS FROM THE CAMPAIGN MENU
/// MainMenuController goes through SaveManager.SelectMode(), which decides continue-vs-fresh
/// and unlocks the next mode on completion. An assessment must do neither: it is a clean
/// graded run every time, it must not resume half-finished campaign progress, and passing it
/// must not silently unlock the next module. So this loads the scene directly and sets
/// AssessmentMode instead - the campaign save is never touched.
///
/// Only COC1 is implemented for now. The other three are shown but disabled, with a
/// "Coming soon" label, so the screen reads as finished rather than broken.
/// </summary>
public class AssessmentMenuController : MonoBehaviour
{
    [System.Serializable]
    public class AssessmentOption
    {
        [Tooltip("e.g. \"COC1\". Passed to AssessmentMode so the scene knows what is being assessed.")]
        public string modeId = "COC1";

        [Tooltip("Exact Unity scene name. Must also be in File > Build Settings.")]
        public string sceneName;

        [Tooltip("The button for this module.")]
        public Button button;

        [Tooltip("Optional label under the button - shows Available or Coming soon.")]
        public TMP_Text statusText;

        [Tooltip("Untick for modules that are not built yet. The button is shown but disabled.")]
        public bool available = false;

        [Tooltip("OPTIONAL, and normally left empty.\n\n" +
                 "Wire something here and it is called INSTEAD of the usual 'set the flag, load the " +
                 "scene' pair - the whole of launching this module becomes that call's job, " +
                 "AssessmentMode included.\n\n" +
                 "COC4 needs it: its assessment has no single scene to load. It draws five random " +
                 "tickets and loads the first one's repair site, so wire COC4AssessmentLauncher > " +
                 "StartAssessment() here and leave Scene Name empty.")]
        public UnityEvent customLaunch;

        /// <summary>True when this module launches through customLaunch rather than by loading
        /// Scene Name. Counts only PERSISTENT listeners - the ones wired in the Inspector - because
        /// a listener added in code would make this true for every module the moment anything
        /// subscribed for its own reasons.</summary>
        public bool HasCustomLaunch =>
            customLaunch != null && customLaunch.GetPersistentEventCount() > 0;
    }

    [Header("Panels")]
    [Tooltip("This screen. Hidden when Back is pressed.")]
    [SerializeField] private GameObject assessmentPanel;

    [Tooltip("Whatever should come back when Back is pressed - normally the main menu's first screen.")]
    [SerializeField] private GameObject returnToPanel;

    [Header("Modules")]
    [SerializeField] private AssessmentOption[] modules = new AssessmentOption[4];

    [Header("Buttons")]
    [SerializeField] private Button backButton;

    [Header("Labels")]
    [SerializeField] private string availableLabel = "Available";
    [SerializeField] private string unavailableLabel = "Coming soon";

    private void Awake()
    {
        for (int i = 0; i < modules.Length; i++)
        {
            var m = modules[i];
            if (m == null || m.button == null) continue;

            int index = i;                       // capture for the closure
            m.button.onClick.AddListener(() => OnModuleClicked(index));
        }

        if (backButton != null)
            backButton.onClick.AddListener(OnBackClicked);
    }

    private void OnEnable()
    {
        // Coming back here after an assessment means that run is over.
        AssessmentMode.End();
        RefreshButtons();
    }

    private void RefreshButtons()
    {
        foreach (var m in modules)
        {
            if (m == null || m.button == null) continue;

            // A module with a custom launch needs no Scene Name - there is no single scene to
            // name. Without this it would sit greyed out reading "Coming soon" forever.
            m.button.interactable = m.available && (m.HasCustomLaunch || !string.IsNullOrEmpty(m.sceneName));

            if (m.statusText != null)
                m.statusText.text = m.button.interactable ? availableLabel : unavailableLabel;
        }
    }

    private void OnModuleClicked(int index)
    {
        if (index < 0 || index >= modules.Length) return;

        var m = modules[index];
        if (m == null || !m.available) return;

        if (m.HasCustomLaunch)
        {
            // AssessmentMode is deliberately NOT set here. The custom launcher owns the whole of
            // starting this module, including which module id it declares - COC4's launcher has its
            // own Module Id field, and setting the flag twice from two places is how those two
            // quietly drift apart.
            Debug.Log($"[AssessmentMenuController] '{m.modeId}' launches through its own launcher.", this);
            m.customLaunch.Invoke();
            return;
        }

        if (string.IsNullOrEmpty(m.sceneName))
        {
            Debug.LogWarning($"[AssessmentMenuController] '{m.modeId}' has no scene name set and " +
                             "nothing wired to Custom Launch, so this button can do nothing.", this);
            return;
        }

        // Order matters: the flag must be set BEFORE the load, because the scene's own
        // Awake/Start run as soon as it comes up and need to see it.
        AssessmentMode.Begin(m.modeId);
        SceneManager.LoadScene(m.sceneName);
    }

    private void OnBackClicked()
    {
        AssessmentMode.End();

        if (assessmentPanel != null) assessmentPanel.SetActive(false);
        if (returnToPanel != null) returnToPanel.SetActive(true);
    }

    /// <summary>Wire the Main Menu's "Assessment Mode" button to this.</summary>
    public void Show()
    {
        if (returnToPanel != null) returnToPanel.SetActive(false);
        if (assessmentPanel != null) assessmentPanel.SetActive(true);
        RefreshButtons();
    }
}