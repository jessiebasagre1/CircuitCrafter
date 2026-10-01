using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UI-facing counterpart to TicketManager. Spawns one TicketRowView per
/// ticket, keeps rows in sync with TicketManager's status events, and opens
/// TicketDetailPanelView when a row is clicked. Same manager/view split as
/// ChecklistToolManager/ChecklistToolView.
///
/// Put this on the Hub screen's list panel, alongside a TicketDetailPanelView
/// on the (separate) detail panel object.
/// </summary>
public class TicketHubView : MonoBehaviour
{
    [SerializeField] private TicketManager manager;

    [Header("List")]
    [SerializeField] private Transform rowsContent;
    [SerializeField] private TicketRowView rowPrefab;

    [Header("Detail panel")]
    [SerializeField] private TicketDetailPanelView detailPanel;

    private readonly Dictionary<string, TicketRowView> _rows = new Dictionary<string, TicketRowView>();

    private void Start()
    {
        if (manager == null)
        {
            Debug.LogWarning("[TicketHubView] No TicketManager assigned.");
            return;
        }

        // Pick up the result of a repair-ticket visit, if the player just came back from one.
        // This is the ONLY place RepairTicketMode's state gets cleared -- see its own comment
        // on TryConsumeResult for why the target scene's Return button doesn't do this.
        if (RepairTicketMode.TryConsumeResult(out string ticketId, out bool wasDiagnosed, out bool wasFixed))
        {
            manager.ApplyVisitResult(ticketId, wasDiagnosed, wasFixed);
        }

        manager.OnTicketStatusChanged += HandleStatusChanged;

        Populate();

        if (detailPanel != null)
            detailPanel.Hide();
    }

    private void OnDisable()
    {
        if (manager == null) return;
        manager.OnTicketStatusChanged -= HandleStatusChanged;
    }

    private void Populate()
    {
        foreach (Transform child in rowsContent)
            Destroy(child.gameObject);
        _rows.Clear();

        foreach (var ticket in manager.AllTickets)
        {
            if (ticket == null || string.IsNullOrEmpty(ticket.ticketId)) continue;

            var row = Instantiate(rowPrefab, rowsContent);
            row.Setup(ticket, manager.GetStatus(ticket), HandleRowClicked);
            row.SetLocked(!manager.IsUnlocked(ticket));
            _rows[ticket.ticketId] = row;
        }
    }

    /// <summary>Resolving one ticket unlocks the next, so every row's lock is re-read on any change.</summary>
    private void RefreshLocks()
    {
        foreach (var row in _rows.Values)
            if (row != null && row.Data != null) row.SetLocked(!manager.IsUnlocked(row.Data));
    }

    private void HandleRowClicked(TicketData ticket)
    {
        if (detailPanel == null) return;
        detailPanel.Show(ticket, manager.GetStatus(ticket));
    }

    private void HandleStatusChanged(string ticketId, TicketStatus status)
    {
        if (_rows.TryGetValue(ticketId, out var row))
            row.SetStatus(status);

        RefreshLocks();
    }
}