using UnityEngine;

/// <summary>
/// Worked example for the PC-building scenario:
/// - Clicking the motherboard shows a "how to disassemble" tutorial (via InteractableObject).
/// - Removing every PCPart reports progress to TaskManager (TaskId).
/// - Reinstalling every part reports progress to TaskManager (AssembleTaskId).
/// - When all parts are removed/reinstalled, a TaskDialogTrigger shows the matching popup.
/// </summary>
public class PCDisassemblySetup : MonoBehaviour
{
    [SerializeField] private int totalParts = 6;
    public const string TaskId = "disassemble_pc";
    public const string AssembleTaskId = "assemble_pc";

    private void Start()
    {
        TaskManager.Instance.RegisterTask(TaskId, totalParts);
        TaskManager.Instance.RegisterTask(AssembleTaskId, totalParts);
    }
}