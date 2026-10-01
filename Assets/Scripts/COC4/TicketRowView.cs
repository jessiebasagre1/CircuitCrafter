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

    [Header("Locked (tickets unlock in order)")]
    [Tooltip("Shown in place of the status while the ticket is locked.")]
    [SerializeField] private string lockedText = "Locked";
    [SerializeField] private Color lockedColor = new Color32(0x6B, 0x77, 0x88, 0xFF);

    [Tooltip("Optional: a lock icon or overlay on the row, switched on while the ticket is locked.")]
    [SerializeField] private GameObject lockedIndicator;

    [Tooltip("How faded the title is while locked (1 = not faded).")]
    [Range(0.2f, 1f)]
    [SerializeField] private float lockedTitleAlpha = 0.45f;

    [Tooltip("Leave ON: a locked row can still be clicked, and the detail panel says which ticket to " +
             "finish first. OFF greys the View button out instead, with no explanation.")]
    [SerializeField] private bool clickableWhenLocked = true;

    public TicketData Data { get; private set; }

    /// <summary>True while this ticket waits for the ones above it.</summary>
    public bool IsLocked { get; private set; }

    private TicketStatus _status;
    private Color _titleColor;
    private bool _titleColorRead;

    /// <summary>Call once when the row is spawned/refreshed. onViewClicked is invoked
    /// with this row's data when the row/View button is pressed -- the caller
    /// (TicketHubView) is what actually opens the detail panel.</summary>
    public void Setup(TicketData data, TicketStatus status, Action<TicketData> onViewClicked)
    {
        Data = data;

        if (titleText != null) titleText.text = data.title;
        if (titleText != null && !_titleColorRead) { _titleColor = titleText.color; _titleColorRead = true; }
        if (iconImage != null && data.icon != null) iconImage.sprite = data.icon;

        SetStatus(status);

        if (viewButton != null)
        {
            viewButton.onClick.RemoveAllListeners();
            viewButton.onClick.AddListener(() => onViewClicked?.Invoke(data));
        }
    }

    /// <summary>Locks or unlocks the row. Called by TicketHubView; the status underneath is kept.</summary>
    public void SetLocked(bool locked)
    {
        IsLocked = locked;

        if (lockedIndicator != null) lockedIndicator.SetActive(locked);
        if (viewButton != null) viewButton.interactable = clickableWhenLocked || !locked;

        if (titleText != null)
        {
            if (!_titleColorRead) { _titleColor = titleText.color; _titleColorRead = true; }
            Color c = _titleColor;
            if (locked) c.a *= lockedTitleAlpha;
            titleText.color = c;
        }

        SetStatus(_status);
    }

    public void SetStatus(TicketStatus status)
    {
        _status = status;
        if (statusText == null) return;

        if (IsLocked)
        {
            statusText.text = lockedText;
            statusText.color = lockedColor;
            return;
        }

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