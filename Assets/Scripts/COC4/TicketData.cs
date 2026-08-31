using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Data-driven definition of one COC4 repair ticket. Mirrors
/// ChecklistItemData/DiagnosticTestData -- create one asset per ticket,
/// designers tweak title/description/target/diagnosis without touching code.
///
/// Diagnosis stage: if requiresDiagnosis is on, the player must run a
/// diagnostic tool at the target scene (see TicketDiagnosticPanelView) and
/// pick the correct probable cause from diagnosisOptions BEFORE any fault
/// reporter (CableFaultTicketReporter, RAMSlotFaultTicketReporter, ...) will
/// accept the physical fix. This mirrors real CSS NC II troubleshooting
/// method: Identify -> Isolate -> Repair -> Test -> Document.
/// </summary>
[CreateAssetMenu(fileName = "NewTicket", menuName = "PCSim/COC4/Ticket")]
public class TicketData : ScriptableObject
{
    [Tooltip("Must be unique across every TicketData asset, e.g. \"ticket_coc1_loose_ram\". Used as the save-data key, independent of asset file name.")]
    public string ticketId;

    [Tooltip("Which existing COC scene this ticket sends the player to.")]
    public TicketTargetModule targetModule;

    [Tooltip("Exact Unity scene name to load for this ticket, e.g. \"COC1-Copy\". This should be a dedicated repair-site scene authored with the fault already present (e.g. a RAM socket left empty, or a cable left unplugged) -- same idea as a fresh customer unit arriving broken.")]
    public string sceneName;

    [Tooltip("Shown as the row/detail title, e.g. \"Ticket #001 - Computer won't POST\".")]
    public string title;

    [TextArea(3, 6)]
    [Tooltip("The work order text shown in the detail panel -- what the (fictional) customer reported. Deliberately vague about the actual cause; that's what the diagnostic step is for.")]
    public string description;

    [Tooltip("Optional icon shown next to the ticket row.")]
    public Sprite icon;

    [Tooltip("Optional briefing popup shown via DialogManager when the player clicks 'Start Ticket'.")]
    public DialogSequence briefingDialog;

    [FormerlySerializedAs("faultId")]
    [Tooltip("The TaskManager taskId that must complete for this ticket's fault to count as fixed, e.g. \"ticket_coc1_loose_ram_fix\". Pick a name that won't collide with real campaign taskIds. The fault itself is set up directly in the target scene -- its reporter component reads RepairTicketMode.RequiredTaskId rather than a hardcoded id, so it doesn't need to know which ticket sent the player there.")]
    public string requiredTaskId;

    [Header("Diagnosis")]
    [Tooltip("If on, the player must correctly diagnose the fault at the site before any repair reporter will accept the fix. Turn off for a simple ticket with no diagnostic step.")]
    public bool requiresDiagnosis = true;

    [TextArea(3, 6)]
    [Tooltip("Shown on the diagnostic tool screen -- symptoms only, e.g. \"System powers on. No display output. Beep code: 1 long, 3 short.\" Should not name the cause outright.")]
    public string symptomText;

    [Tooltip("2-4 probable-cause choices shown as buttons on the diagnostic tool. Exactly one should have isCorrect checked.")]
    public DiagnosisOption[] diagnosisOptions;

    [System.Serializable]
    public class DiagnosisOption
    {
        [Tooltip("Shown on the button, e.g. \"Faulty or improperly seated RAM\".")]
        public string label;

        [Tooltip("Tick for exactly one option per ticket.")]
        public bool isCorrect;

        [TextArea(1, 3)]
        [Tooltip("Optional short explanation shown after the player picks THIS option, whether right or wrong -- reinforces the reasoning either way.")]
        public string feedback;
    }
}

/// <summary>
/// Which existing COC module a ticket's fault lives in. Deliberately its own
/// enum, separate from SaveManager's CurrentGameMode (which is a plain
/// string like "COC1") -- TicketManager converts one to the other later
/// with a single ToString() when it needs to call EnsureGameMode/load a
/// scene, so this stays a clean Inspector dropdown instead of a free-text
/// field designers could typo.
/// </summary>
public enum TicketTargetModule
{
    COC1,
    COC2,
    COC3
}
