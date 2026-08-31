using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central manager for COC4 repair tickets. Holds the list of TicketData
/// assets assigned in the Inspector and each one's current TicketStatus,
/// and fires an event when a status changes so TicketHubView can refresh
/// without polling.
///
/// PERSISTENCE: backed by TicketSessionState, an in-memory-only static (see
/// its own comment for the full reasoning). This means:
///   - A ticket correctly stays Resolved through a Hub -> repair-site scene
///     -> Hub round trip, since TicketSessionState survives the scene loads.
///   - Every ticket resets to Open the moment the player returns to the
///     Main Menu (MenuNavigator.Start() calls TicketSessionState.Clear()),
///     so entering COC4 fresh always starts a new "assignment sheet" rather
///     than resuming wherever a previous session left off.
///   - Nothing here is written to disk. This is a deliberate design choice,
///     not a limitation -- do not add file/SaveManager persistence back in
///     without re-confirming that's actually what's wanted, since it was
///     explicitly removed twice now for good reasons (see TicketSessionState
///     and the earlier SaveManager.SaveCheckpoint() incident).
///
/// Status flow: Open -> InProgress (StartTicket) -> Diagnosed / Resolved
/// (ApplyVisitResult, called by the Hub when the player returns from a
/// ticket site).
/// </summary>
public class TicketManager : MonoBehaviour
{
    public static TicketManager Instance { get; private set; }

    [Tooltip("Every ticket available in this build. Add a TicketData asset here for each ticket you create -- same idea as ChecklistToolManager's item list.")]
    [SerializeField] private List<TicketData> allTickets = new List<TicketData>();

    /// <summary>Fired whenever a ticket's status changes, e.g. so TicketHubView can repaint one row.</summary>
    public event Action<string, TicketStatus> OnTicketStatusChanged;

    public IReadOnlyList<TicketData> AllTickets => allTickets;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // Nothing else to do here -- GetStatus() reads straight from TicketSessionState, so
        // there's no local cache to seed or restore.
    }

    public TicketStatus GetStatus(TicketData ticket)
    {
        if (ticket == null || string.IsNullOrEmpty(ticket.ticketId)) return TicketStatus.Open;
        return TicketSessionState.Get(ticket.ticketId);
    }

    /// <summary>Called by the detail panel's "Start Ticket" button. Moves an Open
    /// ticket to InProgress.</summary>
    public void StartTicket(TicketData ticket)
    {
        if (ticket == null || string.IsNullOrEmpty(ticket.ticketId)) return;
        if (GetStatus(ticket) != TicketStatus.Open) return;

        SetStatus(ticket.ticketId, TicketStatus.InProgress);
    }

    /// <summary>Called once by TicketHubView right after RepairTicketMode reports the result of
    /// a site visit. Looks the ticket up by id since the caller only has the id, not the
    /// TicketData asset reference. Promotes InProgress -> Diagnosed -> Resolved as far as the
    /// visit actually got -- never regresses an already-Resolved ticket, and never skips a
    /// player straight to Resolved without passing through Diagnosed first unless the ticket
    /// doesn't require diagnosis (RepairTicketMode.Begin already treats that as
    /// pre-diagnosed).</summary>
    public void ApplyVisitResult(string ticketId, bool wasDiagnosed, bool wasFixed)
    {
        if (string.IsNullOrEmpty(ticketId)) return;

        TicketData ticket = allTickets.Find(t => t != null && t.ticketId == ticketId);
        if (ticket == null)
        {
            Debug.LogWarning($"[TicketManager] Tried to apply a visit result to unknown ticket id '{ticketId}'.");
            return;
        }

        TicketStatus current = GetStatus(ticket);
        if (current == TicketStatus.Resolved) return;

        if (wasFixed)
        {
            SetStatus(ticketId, TicketStatus.Resolved);
        }
        else if (wasDiagnosed && current == TicketStatus.InProgress)
        {
            SetStatus(ticketId, TicketStatus.Diagnosed);
        }
    }

    private void SetStatus(string ticketId, TicketStatus status)
    {
        TicketSessionState.Set(ticketId, status);
        OnTicketStatusChanged?.Invoke(ticketId, status);
    }
}