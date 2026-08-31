using UnityEngine;

/// <summary>
/// Attach for a COC4 ticket whose fault is "this one part isn't seated" --
/// generic over any single PartSocket (GPU, SSD, CPU, etc; anything that
/// isn't RAM's special "any ONE of several slots" rule, which stays on
/// RAMPresenceFaultTicketReporter). Reports the ticket fixed the moment the
/// assigned socket has an occupant.
///
/// IMPORTANT: gated by activeForTicketId, exactly like every other fault
/// reporter/setup script in COC4 -- see CableFaultTicketReporter's class
/// comment for the full reasoning (multiple tickets sharing one scene).
/// </summary>
public class PartSocketFaultTicketReporter : MonoBehaviour
{
    [Tooltip("The socket that must end up filled for this ticket to count as fixed, e.g. the GPU slot or the SSD bay.")]
    [SerializeField] private PartSocket targetSocket;

    [Tooltip("This reporter only activates when this is the ticket currently active, e.g. \"ticket_coc1_gpu_missing\". Must match the ticket's Ticket Id exactly. Leave blank ONLY if this scene will never be shared with another ticket.")]
    [SerializeField] private string activeForTicketId;

    [Tooltip("Optional -- nudges the player to diagnose first if they seat the part before running the diagnostic tool.")]
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
            enabled = false;
            return;
        }

        if (targetSocket == null)
        {
            Debug.LogWarning("[PartSocketFaultTicketReporter] No Target Socket assigned.", this);
            enabled = false;
            return;
        }

        if (string.IsNullOrEmpty(RepairTicketMode.RequiredTaskId))
        {
            Debug.LogWarning("[PartSocketFaultTicketReporter] RepairTicketMode is active but has no RequiredTaskId -- check the ticket's Required Task Id field.", this);
            enabled = false;
            return;
        }

        // A 1-step task: this fault is either fixed or it isn't.
        TaskManager.Instance.RegisterTask(RepairTicketMode.RequiredTaskId, 1);
    }

    private void Update()
    {
        if (reported) return;
        if (targetSocket.IsEmpty) return;

        if (!RepairTicketMode.IsDiagnosed)
        {
            if (!warnedAboutDiagnosis)
            {
                warnedAboutDiagnosis = true;
                Debug.Log($"[PartSocketFaultTicketReporter] '{targetSocket.name}' filled, but the fault hasn't been diagnosed yet -- run the diagnostic tool first.", this);
                if (diagnoseFirstWarning != null && DialogManager.Instance != null)
                    DialogManager.Instance.ShowDialog(diagnoseFirstWarning);
            }
            return;
        }

        reported = true;
        TaskManager.Instance.ReportProgress(RepairTicketMode.RequiredTaskId);
        RepairTicketMode.MarkFixed();

        Debug.Log($"[PartSocketFaultTicketReporter] '{targetSocket.name}' filled -- ticket fault fixed.", this);
    }
}
