using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Loads a named scene when its Button is pressed. Built for the Ticket Hub's Back button, which
/// has to leave COC4 for the Main Menu and had nothing to call: PauseMenuController's equivalent is
/// private, and MenuNavigator lives in the Main Menu scene rather than this one.
///
/// ATTACH THIS TO: the Back button object the builder made (TicketHubScreen > Column > Footer >
/// BackButton). Leave Button empty and it finds the one on its own object.
///
/// WHY NOTHING CLEARS THE TICKETS HERE
/// It looks like leaving the hub should reset every ticket to Open, and it does happen - just not
/// here. MenuNavigator.Start() calls TicketSessionState.Clear() when the Main Menu loads, so the
/// reset is owned by the place the learner arrives at rather than each of the several places they
/// might leave from. Clearing here as well would be a second copy of that rule, free to drift out
/// of step with the first.
/// </summary>
[RequireComponent(typeof(Button))]
public class LoadSceneButton : MonoBehaviour
{
    [Tooltip("Exact Unity scene name, as spelled in Build Settings. For the hub's Back button this " +
             "is your Main Menu scene.")]
    [SerializeField] private string sceneName = "MainMenu";

    [Tooltip("Optional. Left empty, the Button on this object is used.")]
    [SerializeField] private Button button;

    private void Awake()
    {
        if (button == null) button = GetComponent<Button>();
        if (button != null) button.onClick.AddListener(Load);
    }

    /// <summary>Also public so it can be called from another UnityEvent.</summary>
    public void Load()
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning("[LoadSceneButton] No Scene Name set, so this button does nothing.", this);
            return;
        }

        // The same defensive reset TicketDetailPanelView does before it loads a site scene.
        // Time.timeScale is a global that survives LoadScene, so a briefing dialog that paused the
        // game and was closed by this button instead of by itself would leave the NEXT scene frozen
        // before its own intro ever ran.
        Time.timeScale = 1f;

        SceneManager.LoadScene(sceneName.Trim());
    }
}
