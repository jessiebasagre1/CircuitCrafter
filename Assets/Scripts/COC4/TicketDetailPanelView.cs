using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

/// <summary>
/// Detail panel shown when a ticket row is clicked. Displays the ticket's
/// title/description and lets the player start it, then travel to the site.
/// Same idea as ServiceReportView, but for a single selected ticket instead
/// of a fixed checklist.
///
/// "Go to Site" is interactable once the ticket is InProgress OR Diagnosed
/// (the player may need a second visit to actually apply the fix after
/// diagnosing it) -- it loads the ticket's target scene via
/// RepairTicketMode.Begin(ticket) + SceneManager.LoadScene(), same
/// LoadScene convention as PauseMenuController/MenuNavigator.
/// </summary>
public class TicketDetailPanelView : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button startButton;

    [Tooltip("Enabled once the ticket is InProgress or Diagnosed. Loads the ticket's Scene Name.")]
    [SerializeField] private Button goToSiteButton;

    [SerializeField] private TicketManager manager;

    [Header("Status colours")]
    [Tooltip("Kept in step with TicketRowView's. A ticket that reads gold in the list and white in " +
             "the panel beside it looks like two different tickets.")]
    [SerializeField] private Color openColor = new Color32(0xA9, 0xC6, 0xF0, 0xFF);
    [SerializeField] private Color inProgressColor = new Color32(0x46, 0xC8, 0xF5, 0xFF);
    [SerializeField] private Color diagnosedColor = new Color32(0xFF, 0xC9, 0x3C, 0xFF);
    [SerializeField] private Color resolvedColor = new Color32(0x5A, 0xE0, 0xA8, 0xFF);

    [Header("Locked (tickets unlock in order)")]
    [Tooltip("The status line for a locked ticket. {0} is the title of the ticket to finish first.")]
    [SerializeField] private string lockedStatusFormat = "Locked - finish \"{0}\" first";
    [SerializeField] private Color lockedColor = new Color32(0x6B, 0x77, 0x88, 0xFF);

    private TicketData _current;

    /// <summary>The Manager field, or the scene's TicketManager if it was left empty.</summary>
    private TicketManager Manager => manager != null ? manager : TicketManager.Instance;

    /// <summary>The ticket that has to be finished before the one showing, or null.</summary>
    private TicketData BlockingCurrent => _current != null && Manager != null ? Manager.BlockingTicket(_current) : null;

    public void Show(TicketData ticket, TicketStatus status)
    {
        _current = ticket;

        if (panelRoot != null) panelRoot.SetActive(true);
        if (titleText != null) titleText.text = ticket.title;
        if (descriptionText != null) descriptionText.text = ticket.description;

        Refresh(status);
    }

    public void Hide()
    {
        _current = null;
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private void Refresh(TicketStatus status)
    {
        // Locked: say which ticket comes first, and neither button works.
        TicketData blocking = BlockingCurrent;
        if (blocking != null)
        {
            if (statusText != null)
            {
                statusText.text = string.Format(lockedStatusFormat, blocking.title);
                statusText.color = lockedColor;
            }
            if (startButton != null)
            {
                startButton.interactable = false;
                startButton.onClick.RemoveAllListeners();
                startButton.onClick.AddListener(HandleStartClicked);
            }
            if (goToSiteButton != null)
            {
                goToSiteButton.interactable = false;
                goToSiteButton.onClick.RemoveAllListeners();
                goToSiteButton.onClick.AddListener(HandleGoToSiteClicked);
            }
            return;
        }

        if (statusText != null)
        {
            statusText.text = status switch
            {
                TicketStatus.Open => "Open",
                TicketStatus.InProgress => "In Progress",
                TicketStatus.Diagnosed => "Diagnosed - repair pending",
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

        if (startButton != null)
        {
            startButton.interactable = status == TicketStatus.Open;
            startButton.onClick.RemoveAllListeners();
            startButton.onClick.AddListener(HandleStartClicked);
        }

        if (goToSiteButton != null)
        {
            // Once diagnosed, the player may still need to return and apply the fix.
            goToSiteButton.interactable = status == TicketStatus.InProgress || status == TicketStatus.Diagnosed;
            goToSiteButton.onClick.RemoveAllListeners();
            goToSiteButton.onClick.AddListener(HandleGoToSiteClicked);
        }
    }

    private void HandleGoToSiteClicked()
    {
        if (_current == null) return;
        if (BlockingCurrent != null) return;   // locked

        if (string.IsNullOrEmpty(_current.sceneName))
        {
            Debug.LogWarning($"[TicketDetailPanelView] Ticket '{_current.ticketId}' has no Scene Name set -- add one in its TicketData asset.");
            return;
        }

        // Defensive reset: Time.timeScale is a global engine setting that survives
        // SceneManager.LoadScene. A dialog earlier in this panel may have paused the game via
        // DialogManager's pauseGameWhileOpen, and leaving it at 0 here freezes the target
        // scene's own intro dialog/animations before they even start.
        Time.timeScale = 1f;

        // A ticket already sitting at Diagnosed means this is a return visit to apply a fix
        // that was correctly diagnosed on an earlier trip -- tell RepairTicketMode so it
        // doesn't reset IsDiagnosed and force the (now auto-popup) diagnostic panel on the
        // player again for something they already got right.
        bool alreadyDiagnosed = manager != null && manager.GetStatus(_current) == TicketStatus.Diagnosed;

        RepairTicketMode.Begin(_current, alreadyDiagnosed);
        SceneManager.LoadScene(_current.sceneName);
    }

    private void HandleStartClicked()
    {
        if (_current == null || manager == null) return;
        if (BlockingCurrent != null) return;   // locked

        var ticket = _current;
        manager.StartTicket(ticket);

        if (ticket.briefingDialog != null && DialogManager.Instance != null)
        {
            DialogManager.Instance.ShowDialog(ticket.briefingDialog, () => Refresh(manager.GetStatus(ticket)));
        }
        else
        {
            Refresh(manager.GetStatus(ticket));
        }
    }
}