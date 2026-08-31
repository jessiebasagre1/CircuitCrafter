/// <summary>
/// Lifecycle of a single COC4 repair ticket.
///
/// Open        -> player has not accepted the ticket yet.
/// InProgress  -> player accepted it ("Start Ticket") and may travel to the site.
/// Diagnosed   -> player ran the correct diagnostic tool at the site and identified
///                the fault, but has not (yet, or on this visit) applied the fix.
///                Fault reporters (CableFaultTicketReporter, RAMSlotFaultTicketReporter,
///                etc.) refuse to report a fix until this has happened -- see
///                RepairTicketMode.IsDiagnosed. A ticket can also become Resolved
///                directly from InProgress if TicketData.requiresDiagnosis is off.
/// Resolved    -> the underlying fault condition has been fixed and verified.
/// </summary>
public enum TicketStatus
{
    Open,
    InProgress,
    Diagnosed,
    Resolved
}
