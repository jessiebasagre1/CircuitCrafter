using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Listens to OSInstallManager events and updates the install screen UI.
/// Decoupled the same way DialogView listens to DialogManager.
///
/// continueButton handles stages with requiresUserInput = true (e.g.
/// LanguageSelect, PartitionSelect)  without it, OSInstallManager's
/// _waitingForUserInput flag never clears and the sequence hangs forever
/// on those stages.
/// </summary>
public class OSInstallView : MonoBehaviour
{
    [SerializeField] private GameObject installPanel;
    [SerializeField] private TMP_Text stageLabel;
    [SerializeField] private Slider progressBar;
    [SerializeField] private Button continueButton;
    [SerializeField] private TMP_Dropdown choiceDropdown;
    [SerializeField] private GameObject rebootOverlay;

    private void Start()
    {
        // Start() (not OnEnable()) so this is guaranteed to run AFTER every
        // object's Awake() -- including OSInstallManager's, which is where
        // Instance gets assigned. Subscribing in OnEnable() risked a race:
        // if this ran before OSInstallManager.Awake(), Instance was still
        // null, the whole block bailed out, and this view silently never
        // subscribed to anything for the rest of the scene's life.
        if (OSInstallManager.Instance == null)
        {
            Debug.LogWarning("[OSInstallView] OSInstallManager.Instance is still null in Start(). " +
                "Is OSInstallManager actually present and enabled in this scene?");
            return;
        }

        OSInstallManager.Instance.OnStageChanged += HandleStageChanged;
        OSInstallManager.Instance.OnStageProgress += HandleProgress;
        OSInstallManager.Instance.OnInstallComplete += HandleComplete;
        OSInstallManager.Instance.OnInstallFailed += HandleFailed;

        if (continueButton != null)
            continueButton.onClick.AddListener(HandleContinueClicked);

        // Monitor should look powered-off until the player actually hits
        // Power On and the first stage (PowerOn/POST) fires HandleStageChanged.
        // Reusing rebootOverlay's black Image for this instead of adding a
        // separate "screen off" object -- same visual, no new component.
        if (installPanel != null)
            installPanel.SetActive(false);

        if (rebootOverlay != null)
            rebootOverlay.SetActive(true);
    }

    private void OnDestroy()
    {
        if (OSInstallManager.Instance == null) return;

        OSInstallManager.Instance.OnStageChanged -= HandleStageChanged;
        OSInstallManager.Instance.OnStageProgress -= HandleProgress;
        OSInstallManager.Instance.OnInstallComplete -= HandleComplete;
        OSInstallManager.Instance.OnInstallFailed -= HandleFailed;

        if (continueButton != null)
            continueButton.onClick.RemoveListener(HandleContinueClicked);
    }

    private void HandleStageChanged(OSInstallStageData stage)
    {
        installPanel.SetActive(true);
        stageLabel.text = stage.displayText;

        bool showsProgress = !stage.requiresUserInput;
        if (progressBar != null)
        {
            progressBar.gameObject.SetActive(showsProgress);
            progressBar.value = 0f;
        }

        if (continueButton != null)
            continueButton.gameObject.SetActive(stage.requiresUserInput);

        bool showsDropdown = stage.requiresUserInput && stage.userChoices != null && stage.userChoices.Length > 0;
        if (choiceDropdown != null)
        {
            choiceDropdown.gameObject.SetActive(showsDropdown);
            if (showsDropdown)
            {
                choiceDropdown.ClearOptions();
                choiceDropdown.AddOptions(new List<string>(stage.userChoices));
                choiceDropdown.value = 0;
                choiceDropdown.RefreshShownValue();
            }
        }

        if (rebootOverlay != null)
            rebootOverlay.SetActive(stage.requiresRebootAnimation);
    }

    private void HandleProgress(float t)
    {
        if (progressBar != null) progressBar.value = t;
    }

    private void HandleContinueClicked()
    {
        string choice = (choiceDropdown != null && choiceDropdown.gameObject.activeSelf)
            ? choiceDropdown.options[choiceDropdown.value].text
            : null;

        OSInstallManager.Instance.SubmitUserChoice(choice);
    }

    private void HandleComplete()
    {
        stageLabel.text = "Installation complete.";
        if (progressBar != null)
        {
            progressBar.gameObject.SetActive(true);
            progressBar.value = 1f;
        }
        if (continueButton != null) continueButton.gameObject.SetActive(false);
        if (choiceDropdown != null) choiceDropdown.gameObject.SetActive(false);
        if (rebootOverlay != null) rebootOverlay.SetActive(false);
        // TODO: transition to "desktop" scene/view here
    }

    private void HandleFailed(string reason)
    {
        // DialogManager already shows the Warning dialog; keep panel state clean here.
        installPanel.SetActive(false);
    }
}