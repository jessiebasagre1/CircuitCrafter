using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// One row in the Ticket Hub list, representing a single TicketData. Put
/// this on a prefab and assign it to TicketHubView.rowPrefab -- same idea
/// as ChecklistItemRowView/rowPrefab on ChecklistToolView.
///
/// Suggested row layout: [icon] [title] [status text] [View button]. Only
/// titleText is required -- leave the rest unassigned to simplify the row.
/// </summary>
public class TicketRowView : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button viewButton;

    public TicketData Data { get; private set; }

    /// <summary>Call once when the row is spawned/refreshed. onViewClicked is invoked
    /// with this row's data when the row/View button is pressed -- the caller
    /// (TicketHubView) is what actually opens the detail panel.</summary>
    public void Setup(TicketData data, TicketStatus status, Action<TicketData> onViewClicked)
    {
        Data = data;

        if (titleText != null) titleText.text = data.title;
        if (iconImage != null && data.icon != null) iconImage.sprite = data.icon;

        SetStatus(status);

        if (viewButton != null)
        {
            viewButton.onClick.RemoveAllListeners();
            viewButton.onClick.AddListener(() => onViewClicked?.Invoke(data));
        }
    }

    public void SetStatus(TicketStatus status)
    {
        if (statusText == null) return;

        statusText.text = status switch
        {
            TicketStatus.Open => "Open",
            TicketStatus.InProgress => "In Progress",
            TicketStatus.Diagnosed => "Diagnosed",
            TicketStatus.Resolved => "Resolved",
            _ => status.ToString()
        };
    }
}
