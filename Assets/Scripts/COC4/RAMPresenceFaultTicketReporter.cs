using UnityEngine;

/// <summary>
/// Attach for a COC4 ticket whose fault is "no RAM installed at all."
/// Watches ALL of the case's RAM sockets and reports the ticket fixed the
/// moment ANY ONE of them is occupied -- matching this game's rule that a
/// single seated stick is enough to boot.
///
/// IMPORTANT: gated by activeForTicketId, not just RepairTicketMode.IsActive.
/// If a repair-site scene is shared by more than one ticket (e.g. this same
/// COC1-Copy also hosts a PSU cable ticket), leaving this ungated would let
/// it fire for the WRONG ticket the instant it sees RAM already normally
/// seated -- which is trivially true whenever a different ticket's task is
/// the one actually active, since RAMRemovedFaultSetup only ever removes RAM
/// for its own ticket. Scoping to activeForTicketId keeps this component
/// completely inert unless ITS OWN ticket is the one currently active.
/// </summary>
public class RAMPresenceFaultTicketReporter : MonoBehaviour
{
    [Tooltip("Every RAM slot in the case (e.g. all 4). The fault counts as fixed the moment any ONE of these has an occupant.")]
    [SerializeField] private RAMSocket[] sockets;

    [Tooltip("This reporter only activates when this is the ticket currently active, e.g. \"ticket_coc1_ram_missing\". Must match the ticket's Ticket Id exactly. Leave blank ONLY if this scene will never be shared with another ticket.")]
    [SerializeField] private string activeForTicketId;

    [Tooltip("Optional -- nudges the player to diagnose first if they seat a stick before running the diagnostic tool.")]
    [SerializeField] private DialogSequence diagnoseFirstWarning;

    private bool reported;
    private bool warnedAboutDiagnosis;

    private void Start()
    {
        if (!RepairTicketMode.IsActive)
        {
            enabled = false;
            return;
        }

        if (!string.IsNullOrEmpty(activeForTicketId) && RepairTicketMode.ActiveTicketId != activeForTicketId)
        {
            // A different ticket sent the player here -- this fault isn't relevant to it.
            enabled = false;
            return;
        }

        if (sockets == null || sockets.Length == 0)
        {
            Debug.LogWarning("[RAMPresenceFaultTicketReporter] No RAM sockets assigned.", this);
            enabled = false;
            return;
        }

        if (string.IsNullOrEmpty(RepairTicketMode.RequiredTaskId))
        {
            Debug.LogWarning("[RAMPresenceFaultTicketReporter] RepairTicketMode is active but has no RequiredTaskId -- check the ticket's Required Task Id field.", this);
            enabled = false;
            return;
        }

        // A 1-step task: this fault is either fixed or it isn't.
        TaskManager.Instance.RegisterTask(RepairTicketMode.RequiredTaskId, 1);
    }

    private void Update()
    {
        if (reported) return;
        if (!AnySocketOccupied()) return;

        if (!RepairTicketMode.IsDiagnosed)
        {
            if (!warnedAboutDiagnosis)
            {
                warnedAboutDiagnosis = true;
                Debug.Log("[RAMPresenceFaultTicketReporter] RAM seated, but the fault hasn't been diagnosed yet -- run the diagnostic tool first.", this);
                if (diagnoseFirstWarning != null && DialogManager.Instance != null)
                    DialogManager.Instance.ShowDialog(diagnoseFirstWarning);
            }
            return;
        }

        reported = true;
        TaskManager.Instance.ReportProgress(RepairTicketMode.RequiredTaskId);
        RepairTicketMode.MarkFixed();

        Debug.Log("[RAMPresenceFaultTicketReporter] At least one RAM slot is now occupied -- ticket fault fixed.", this);
    }

    private bool AnySocketOccupied()
    {
        foreach (var socket in sockets)
        {
            if (socket != null && !socket.IsEmpty) return true;
        }
        return false;
    }
}