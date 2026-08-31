using UnityEngine;

/// <summary>
/// Attach alongside/pointing at a CableConnector that acts as a COC4 repair
/// ticket's "fault" -- set that connector to start unplugged in the editor
/// (CableConnector's own "Start Already Plugged" section: leave startPlugged
/// off, or set a startSocket with startPlugged unticked) and this component
/// reports the ticket's task complete once the player plugs it back in
/// correctly -- AFTER first correctly diagnosing the fault, if the ticket
/// requires it (TicketData.requiresDiagnosis).
///
/// IMPORTANT: gated by activeForTicketId, not just RepairTicketMode.IsActive.
/// If a repair-site scene is shared by more than one ticket (e.g. this same
/// COC1-Copy also hosts a RAM ticket), leaving this ungated would let it
/// register against and complete THE WRONG TICKET'S task the moment the
/// cable happens to already be in whatever state it's normally left in for
/// a different ticket's visit. Scoping to activeForTicketId keeps this
/// component completely inert unless ITS OWN ticket is the one currently
/// active.
/// </summary>
public class CableFaultTicketReporter : MonoBehaviour
{
    [SerializeField] private CableConnector cable;

    [Tooltip("This reporter only activates when this is the ticket currently active, e.g. \"ticket_coc1_psu_cable_fix\". Must match the ticket's Ticket Id exactly. Leave blank ONLY if this scene will never be shared with another ticket.")]
    [SerializeField] private string activeForTicketId;

    [Tooltip("Optional -- shown once via Debug.Log (and DialogManager if assigned) the first time the player plugs the cable back in before diagnosing it. Doesn't undo the plug, just nudges them to run the diagnostic tool first.")]
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

        if (cable == null)
        {
            Debug.LogWarning("[CableFaultTicketReporter] No CableConnector assigned.", this);
            enabled = false;
            return;
        }

        if (string.IsNullOrEmpty(RepairTicketMode.RequiredTaskId))
        {
            Debug.LogWarning("[CableFaultTicketReporter] RepairTicketMode is active but has no RequiredTaskId -- check the ticket's Required Task Id field.", this);
            enabled = false;
            return;
        }

        // A 1-step task: this fault is either fixed or it isn't.
        TaskManager.Instance.RegisterTask(RepairTicketMode.RequiredTaskId, 1);
    }

    private void Update()
    {
        if (reported) return;
        if (!cable.isPlugged) return;

        if (!RepairTicketMode.IsDiagnosed)
        {
            // Physically fixed, but the player skipped diagnosis. Don't report progress --
            // real technicians document a probable cause before closing a ticket.
            if (!warnedAboutDiagnosis)
            {
                warnedAboutDiagnosis = true;
                Debug.Log("[CableFaultTicketReporter] Cable reconnected, but the fault hasn't been diagnosed yet -- run the diagnostic tool first.", this);
                if (diagnoseFirstWarning != null && DialogManager.Instance != null)
                    DialogManager.Instance.ShowDialog(diagnoseFirstWarning);
            }
            return;
        }

        reported = true;
        TaskManager.Instance.ReportProgress(RepairTicketMode.RequiredTaskId);
        RepairTicketMode.MarkFixed();

        Debug.Log($"[CableFaultTicketReporter] '{cable.name}' plugged back in -- ticket fault fixed.", this);
    }
}