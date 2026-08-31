using UnityEngine;

/// <summary>
/// A one-flag record of "the player is out on a repair ticket right now",
/// set by TicketDetailPanelView just before it loads a COC1/2/3 scene, read
/// by that scene (ReturnToHubButton, RepairTicketSceneGate, the diagnostic
/// panel, and the fault's own reporter component) and finally consumed by
/// the Hub when the player comes back. Same trick as AssessmentMode: a
/// static survives SceneManager.LoadScene, which is the only lifetime this
/// needs.
///
/// WHY A STATIC AND NOT THE SAVE FILE
/// Same reasoning as AssessmentMode -- being "on a ticket" is a property of
/// this play session's navigation, not of saved progress. It must not be
/// written to savegame.json and must not survive an app restart.
///
/// Diagnosis stage: now carries the whole ActiveTicket reference (instead of
/// just loose id strings) so the diagnostic panel can read symptomText /
/// diagnosisOptions directly, plus IsDiagnosed / MarkDiagnosed() mirroring
/// the existing ActiveTicketFixed / MarkFixed() pair. RequiredTaskId and
/// ActiveTicketId are kept as convenience properties (derived from
/// ActiveTicket) so existing reporter components don't need to change.
/// </summary>
public static class RepairTicketMode
{
    /// <summary>True while the player is inside a COC1/2/3 scene reached via a COC4 ticket.</summary>
    public static bool IsActive { get; private set; }

    /// <summary>The full ticket that sent the player here.</summary>
    public static TicketData ActiveTicket { get; private set; }

    /// <summary>Which ticket sent the player here, e.g. "ticket_coc1_loose_ram". Convenience accessor for ActiveTicket.ticketId.</summary>
    public static string ActiveTicketId => ActiveTicket != null ? ActiveTicket.ticketId : null;

    /// <summary>The TaskManager taskId that must complete to consider this ticket's fault fixed.
    /// Convenience accessor for ActiveTicket.requiredTaskId -- the fault's reporter component in
    /// the target scene reads this instead of a hardcoded id, so the same component works for
    /// whichever ticket sent the player here.</summary>
    public static string RequiredTaskId => ActiveTicket != null ? ActiveTicket.requiredTaskId : null;

    /// <summary>Set the moment the player picks the correct probable cause on the diagnostic
    /// panel. Fault reporters check this before accepting a physical fix -- see
    /// CableFaultTicketReporter / RAMSlotFaultTicketReporter.</summary>
    public static bool IsDiagnosed { get; private set; }

    /// <summary>Set by the fault's reporter component the moment RequiredTaskId completes.</summary>
    public static bool ActiveTicketFixed { get; private set; }

    /// <summary>Called by TicketDetailPanelView immediately before loading the target scene.
    /// alreadyDiagnosed should be true if the ticket's current TicketManager status is already
    /// Diagnosed (i.e. this is a return visit to actually apply a fix that was correctly
    /// diagnosed on an earlier visit) -- without this, every fresh Begin() call would reset
    /// IsDiagnosed back to false purely because TicketData.requiresDiagnosis is still true,
    /// forcing the player to redo a diagnosis they already got right.</summary>
    public static void Begin(TicketData ticket, bool alreadyDiagnosed = false)
    {
        IsActive = true;
        ActiveTicket = ticket;
        IsDiagnosed = ticket == null || !ticket.requiresDiagnosis || alreadyDiagnosed;
        ActiveTicketFixed = false;

        Debug.Log(ticket != null
            ? $"[RepairTicketMode] Started for ticket '{ticket.ticketId}', watching task '{ticket.requiredTaskId}' (requiresDiagnosis={ticket.requiresDiagnosis}, alreadyDiagnosed={alreadyDiagnosed})."
            : "[RepairTicketMode] Started with a null ticket -- check TicketDetailPanelView.");
    }

    /// <summary>Called by TicketDiagnosticPanelView the moment the player picks the correct
    /// probable cause. Does nothing if a ticket doesn't require diagnosis (already true from
    /// Begin) or if there's no active ticket.</summary>
    public static void MarkDiagnosed()
    {
        if (!IsActive) return;
        IsDiagnosed = true;
        Debug.Log($"[RepairTicketMode] Ticket '{ActiveTicketId}' correctly diagnosed.");
    }

    /// <summary>Called by the fault's own reporter (e.g. CableFaultTicketReporter,
    /// RAMSlotFaultTicketReporter) the moment RequiredTaskId completes. Reporters are
    /// expected to check IsDiagnosed themselves before calling this -- see their own
    /// comments -- so this does not re-check it.</summary>
    public static void MarkFixed()
    {
        if (!IsActive) return;
        ActiveTicketFixed = true;
        Debug.Log($"[RepairTicketMode] Ticket '{ActiveTicketId}' fault fixed.");
    }

    /// <summary>
    /// Called once by the Hub right after returning from a ticket site. Returns true if there was
    /// an active ticket to resolve, and always clears all state afterward -- this is the ONLY
    /// place IsActive gets turned back off, so the target scene's ReturnToHubButton no longer needs
    /// to (and must not) clear it itself, or the Hub would never see the result.
    /// </summary>
    public static bool TryConsumeResult(out string ticketId, out bool wasDiagnosed, out bool wasFixed)
    {
        ticketId = ActiveTicketId;
        wasDiagnosed = IsDiagnosed;
        wasFixed = ActiveTicketFixed;
        bool hadTicket = IsActive;

        IsActive = false;
        ActiveTicket = null;
        IsDiagnosed = false;
        ActiveTicketFixed = false;

        return hadTicket;
    }
}