using UnityEngine;

/// <summary>
/// Data-driven definition of an OS image the player can pick when creating
/// a bootable USB. Mirrors OSInstallStageData/DialogSequence pattern —
/// create one asset per selectable OS (e.g. "Ubuntu 22.04", "Windows 10").
/// </summary>
[CreateAssetMenu(fileName = "NewISOImage", menuName = "PCSim/ISO Image")]
public class ISOImageData : ScriptableObject
{
    [Tooltip("Shown in the ISO selection dropdown")]
    public string displayName;

    [Tooltip("Shown as a subtitle/size estimate in the dropdown, purely cosmetic")]
    public string fileSizeLabel = "4.7 GB";

    [Tooltip("Optional icon shown next to the dropdown entry")]
    public Sprite icon;

    [Tooltip("Which OSInstallStageData sequence this image installs — lets one USB creator support multiple OS options that lead to different install flows")]
    public OSInstallStageData[] installStages;
}