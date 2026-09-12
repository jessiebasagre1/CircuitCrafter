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

    [Header("Status colours")]
    [Tooltip("Four tickets all reading their status in the same grey is a list you have to READ " +
             "rather than scan. Colour is what makes 'which one is still open' a glance instead of " +
             "four comparisons. Leave these at their defaults unless your palette differs.")]
    [SerializeField] private Color openColor = new Color32(0xA9, 0xC6, 0xF0, 0xFF);
    [SerializeField] private Color inProgressColor = new Color32(0x46, 0xC8, 0xF5, 0xFF);
    [SerializeField] private Color diagnosedColor = new Color32(0xFF, 0xC9, 0x3C, 0xFF);
    [SerializeField] private Color resolvedColor = new Color32(0x5A, 0xE0, 0xA8, 0xFF);

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

        statusText.color = status switch
        {
            TicketStatus.Open => openColor,
            TicketStatus.InProgress => inProgressColor,
            TicketStatus.Diagnosed => diagnosedColor,
            TicketStatus.Resolved => resolvedColor,
            _ => openColor
        };
    }
}