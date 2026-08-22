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

    [Header("User Choice (only used when Requires User Input is true)")]
    [Tooltip("Options shown in a dropdown for this stage, e.g. \"English\", \"Filipino\" for LanguageSelect. Leave empty if this stage uses Partition Options below instead, or falls back to a plain Continue button.")]
    public string[] userChoices;

    [Header("Answer Checking (only used when Requires User Input is true)")]
    [Tooltip("The option from User Choices that is CORRECT. Leave empty to accept whatever the learner picks - right for genuinely free choices like language, wrong for choices with a defensible answer like the boot device or the installation type.")]
    public string correctChoice = "";

    [Tooltip("Shown when the learner picks the wrong option. They stay on this stage and choose again, exactly like a wrong answer in COC 3.")]
    public DialogSequence wrongChoiceDialog;

    [Header("Partition Options (only used on the PartitionSelect stage)")]
    [Tooltip("Starting disk layout shown in the interactive partition table. Leave empty for stages other than PartitionSelect.")]
    public PartitionOption[] partitionOptions;
}

/// <summary>
/// One row of the partition table's starting state. The runtime UI
/// (PartitionSelectView) works off a copy of this data, not the asset
/// itself, so New/Format/Delete during play never modifies the asset.
/// </summary>
[System.Serializable]
public class PartitionOption
{
    public string partitionName = "Drive 0 Unallocated Space";
    public float totalSizeGB = 256f;
    public float freeSpaceGB = 256f;
    public PartitionKind kind = PartitionKind.Unallocated;

    [Tooltip("Runtime-only in practice � leave false on the asset. New/Format/Delete during play mutate a copy of this data, never the asset itself.")]
    public bool isFormatted = false;
}

public enum PartitionKind
{
    Primary,
    Extended,
    Unallocated
}