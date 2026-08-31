using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Drop this into a COC1/2/3 scene (anywhere -- a small corner button works
/// well, same spirit as PauseMenuController's corner "Menu" button) to give
/// the player a way back to the Ticket Hub when they arrived via a COC4
/// ticket. Hides itself completely during normal Story Mode / Assessment
/// play, so it's safe to leave in the scene permanently.
///
/// Stage 2 only returns to the Hub -- it does not resolve the ticket or
/// check for a fixed fault. That arrives once fault logic exists.
/// </summary>
public class ReturnToHubButton : MonoBehaviour
{
    [Tooltip("The button itself, or a parent container -- whichever should be hidden outside of repair-ticket play. Assign the same object this script is on if you don't have a separate container.")]
    [SerializeField] private GameObject visibleRoot;

    [SerializeField] private Button button;

    [Tooltip("Exact scene name of the COC4 Ticket Hub.")]
    [SerializeField] private string hubSceneName = "COC4-Hub";

    private void Start()
    {
        bool showButton = RepairTicketMode.IsActive;

        if (visibleRoot != null) visibleRoot.SetActive(showButton);
        else gameObject.SetActive(showButton);

        if (!showButton) return;

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(HandleClicked);
        }
    }

    private void HandleClicked()
    {
        // Deliberately does NOT call RepairTicketMode.End() -- the Hub needs to read
        // RequiredTaskId/ActiveTicketFixed when it loads, and does the clearing itself via
        // TryConsumeResult(). Clearing here would erase the result before the Hub ever sees it.
        SceneManager.LoadScene(hubSceneName);
    }
}