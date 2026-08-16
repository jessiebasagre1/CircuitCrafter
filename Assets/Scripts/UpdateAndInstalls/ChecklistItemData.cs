using UnityEngine;

/// <summary>
/// Data-driven definition of one installable item inside a ChecklistToolManager
/// app (e.g. one row in Device Manager, one tile in the Software Center, one
/// patch in Windows Update). Mirrors ISOImageData/OSInstallStageData  create
/// one asset per item, designers tweak name/duration/dialog without touching code.
/// </summary>
[CreateAssetMenu(fileName = "NewChecklistItem", menuName = "PCSim/System Config/Checklist Item")]
public class ChecklistItemData : ScriptableObject
{
    [Tooltip("Must exactly match a taskId in SystemConfigTaskIds (or a new one you add there), e.g. \"install_driver_network\". Used to register/report this specific item with TaskManager, independent of the other items in the same tool.")]
    public string itemTaskId;

    [Tooltip("Shown in the tool's list/grid UI.")]
    public string displayName;

    [TextArea(2, 4)]
    [Tooltip("Optional flavor/help text, e.g. what this driver or patch actually does.")]
    public string description;

    [Tooltip("Optional icon shown next to the item.")]
    public Sprite icon;

    [Tooltip("How long this item takes to 'install', in seconds.")]
    public float duration = 2f;

    [Tooltip("Optional educational prompt shown when this item finishes installing (CSS NC II tie-in), same asset type used by PCPartItem/OSInstallStageData.")]
    public DialogSequence educationalDialog;
}
