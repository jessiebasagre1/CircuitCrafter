using UnityEngine;
using UnityEngine.SceneManagement;
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

            m.button.interactable = m.available && !string.IsNullOrEmpty(m.sceneName);

            if (m.statusText != null)
                m.statusText.text = m.button.interactable ? availableLabel : unavailableLabel;
        }
    }

    private void OnModuleClicked(int index)
    {
        if (index < 0 || index >= modules.Length) return;

        var m = modules[index];
        if (m == null || !m.available) return;

        if (string.IsNullOrEmpty(m.sceneName))
        {
            Debug.LogWarning($"[AssessmentMenuController] '{m.modeId}' has no scene name set.", this);
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
