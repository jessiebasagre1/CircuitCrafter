using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// In-memory-only record of every COC4 ticket's status for the CURRENT play
/// session. Deliberately NOT written to disk anywhere: entering COC4 from
/// the Main Menu should always hand the player a fresh set of Open tickets,
/// like a new assignment sheet -- not resume whatever was left resolved
/// from a previous visit.
///
/// Survives SceneManager.LoadScene() the same way AssessmentMode and
/// RepairTicketMode do (a plain static field) -- that's what makes a ticket
/// correctly stay Resolved through a Hub -> repair-site scene -> Hub round
/// trip WITHIN one COC4 session. What makes it start over between sessions
/// is Clear() -- see MenuNavigator, which calls it every time the Main Menu
/// screen appears, exactly like it already does for AssessmentMode.End().
/// </summary>
public static class TicketSessionState
{
    private static readonly Dictionary<string, TicketStatus> statusById = new Dictionary<string, TicketStatus>();

    /// <summary>Returns TicketStatus.Open for any ticket id not yet recorded this session.</summary>
    public static TicketStatus Get(string ticketId)
    {
        if (string.IsNullOrEmpty(ticketId)) return TicketStatus.Open;
        return statusById.TryGetValue(ticketId, out var status) ? status : TicketStatus.Open;
    }

    public static void Set(string ticketId, TicketStatus status)
    {
        if (string.IsNullOrEmpty(ticketId)) return;
        statusById[ticketId] = status;
        Debug.Log($"[TicketSessionState] Set '{ticketId}' = {status}. ({statusById.Count} ticket(s) tracked this session.)");
    }

    /// <summary>Wipes every ticket back to Open for the next session. Call this whenever the
    /// Main Menu is reached -- see MenuNavigator.Start().</summary>
    public static void Clear()
    {
        int had = statusById.Count;
        statusById.Clear();
        Debug.Log($"[TicketSessionState] Clear() called -- wiped {had} tracked ticket(s).");
    }
}