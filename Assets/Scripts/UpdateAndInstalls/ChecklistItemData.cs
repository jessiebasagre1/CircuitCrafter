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

    [Header("Order (leave empty for an item that can be done at any time)")]
    [Tooltip("TaskManager taskId(s) that must ALL be complete before this item may be installed, " +
             "e.g. SystemConfigTaskIds.InstallDriverMotherboard on every other driver so the chipset " +
             "driver genuinely has to go first. Same idea as DiagnosticTestData.prerequisiteTaskIds, " +
             "except this one BLOCKS the install instead of failing a test afterwards.")]
    public string[] prerequisiteTaskIds;

    [TextArea(1, 3)]
    [Tooltip("Shown in the Console (and used as the mistake description) when this item is installed " +
             "out of order, e.g. \"Install the chipset driver first - Windows still lists this device " +
             "as Standard VGA Graphics Adapter.\"")]
    public string blockedHint;

    [Tooltip("Optional popup shown when this item is blocked. This is corrective feedback rather than a " +
             "tutorial, so it still appears while an assessment has tutorial dialogs suppressed.")]
    public DialogSequence blockedDialog;

    [Tooltip("Optional educational prompt shown when this item finishes installing (CSS NC II tie-in), same asset type used by PCPartItem/OSInstallStageData.")]
    public DialogSequence educationalDialog;
}