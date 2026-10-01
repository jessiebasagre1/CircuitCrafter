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
///
/// UNLOCKING IN ORDER (Unlock In Order, on by default): the tickets open one at
/// a time, in the order of the All Tickets list. A ticket can be started only
/// once every ticket above it is Resolved - repaired AND tested - so the
/// learner works the jobs in the sequence they were written. "Locked" is not a
/// fifth status: it is worked out from the tickets above, so there is nothing
/// extra to save and nothing that can disagree with the statuses. A ticket that
/// was already started stays available whatever happens above it.
/// </summary>
public class TicketManager : MonoBehaviour
{
    public static TicketManager Instance { get; private set; }

    [Tooltip("Every ticket available in this build. Add a TicketData asset here for each ticket you create -- same idea as ChecklistToolManager's item list.")]
    [SerializeField] private List<TicketData> allTickets = new List<TicketData>();

    [Tooltip("Tickets unlock one at a time, in the order of the list above: each one can be started only " +
             "after every ticket above it is Resolved (repaired and tested). Untick to make every ticket " +
             "available from the start.")]
    [SerializeField] private bool unlockInOrder = true;

    /// <summary>True when tickets unlock one at a time, in list order.</summary>
    public bool UnlockInOrder => unlockInOrder;

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

    /// <summary>
    /// True when this ticket can be taken. Always true with Unlock In Order off, and for a ticket
    /// that has already been started. Otherwise true once every ticket above it in the list is
    /// Resolved.
    /// </summary>
    public bool IsUnlocked(TicketData ticket)
    {
        return BlockingTicket(ticket) == null;
    }

    /// <summary>The first ticket above this one that still has to be finished, or null when this
    /// ticket is unlocked. Used to tell the learner which job to finish first.</summary>
    public TicketData BlockingTicket(TicketData ticket)
    {
        if (!unlockInOrder || ticket == null || string.IsNullOrEmpty(ticket.ticketId)) return null;
        if (GetStatus(ticket) != TicketStatus.Open) return null;   // already started - never re-lock work in hand

        foreach (TicketData t in allTickets)
        {
            if (t == null || string.IsNullOrEmpty(t.ticketId)) continue;
            if (t == ticket || t.ticketId == ticket.ticketId) return null;   // reached it: everything above is done
            if (GetStatus(t) != TicketStatus.Resolved) return t;
        }
        return null;   // not in the list - nothing to wait for
    }

    /// <summary>Called by the detail panel's "Start Ticket" button. Moves an Open
    /// ticket to InProgress.</summary>
    public void StartTicket(TicketData ticket)
    {
        if (ticket == null || string.IsNullOrEmpty(ticket.ticketId)) return;
        if (GetStatus(ticket) != TicketStatus.Open) return;

        TicketData blocking = BlockingTicket(ticket);
        if (blocking != null)
        {
            Debug.Log($"[TicketManager] '{ticket.ticketId}' is locked until '{blocking.ticketId}' is resolved.");
            return;
        }

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