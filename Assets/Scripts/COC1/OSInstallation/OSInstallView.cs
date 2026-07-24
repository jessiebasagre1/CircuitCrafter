using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Listens to OSInstallManager events and updates the install screen UI.
/// Decoupled the same way DialogView listens to DialogManager.
/// </summary>
public class OSInstallView : MonoBehaviour
{
    [SerializeField] private GameObject installPanel;
    [SerializeField] private Text stageLabel;
    [SerializeField] private Slider progressBar;
    [SerializeField] private GameObject rebootOverlay;

    private void OnEnable()
    {
        if (OSInstallManager.Instance == null) return;

        OSInstallManager.Instance.OnStageChanged += HandleStageChanged;
        OSInstallManager.Instance.OnStageProgress += HandleProgress;
        OSInstallManager.Instance.OnInstallComplete += HandleComplete;
        OSInstallManager.Instance.OnInstallFailed += HandleFailed;
    }

    private void OnDisable()
    {
        if (OSInstallManager.Instance == null) return;

        OSInstallManager.Instance.OnStageChanged -= HandleStageChanged;
        OSInstallManager.Instance.OnStageProgress -= HandleProgress;
        OSInstallManager.Instance.OnInstallComplete -= HandleComplete;
        OSInstallManager.Instance.OnInstallFailed -= HandleFailed;
    }

    private void HandleStageChanged(OSInstallStageData stage)
    {
        installPanel.SetActive(true);
        stageLabel.text = stage.displayText;
        progressBar.value = 0f;
    }

    private void HandleProgress(float t)
    {
        progressBar.value = t;
    }

    private void HandleComplete()
    {
        stageLabel.text = "Installation complete.";
        progressBar.value = 1f;
        // TODO: transition to "desktop" scene/view here
    }

    private void HandleFailed(string reason)
    {
        // DialogManager already shows the Warning dialog; keep panel state clean here.
        installPanel.SetActive(false);
    }
}