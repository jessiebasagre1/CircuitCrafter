using UnityEngine;

/// <summary>
/// Data-driven definition of a single OS install stage.
/// Mirrors the DialogDatabase pattern: designers can tweak text/duration
/// without touching OSInstallManager code.
/// </summary>
[CreateAssetMenu(fileName = "NewOSInstallStage", menuName = "PCSim/OS Install Stage")]
public class OSInstallStageData : ScriptableObject
{
    public OSInstallManager.InstallStage stage;

    [Tooltip("Text shown on the install screen for this stage")]
    [TextArea(2, 4)]
    public string displayText;

    [Tooltip("How long this stage takes to 'complete', in seconds")]
    public float duration = 2f;

    [Tooltip("If true, plays the CRT-off/on reboot animation before this stage starts")]
    public bool requiresRebootAnimation = false;

    [Tooltip("Optional educational prompt shown during this stage (CSS NC II tie-in). Assign a DialogSequence asset, same as removalDialog/installDialog on PCPartItem.")]
    public DialogSequence educationalDialog;

    [Tooltip("If true, this stage pauses and waits for a user choice (e.g. partition/language select) instead of auto-advancing")]
    public bool requiresUserInput = false;
}