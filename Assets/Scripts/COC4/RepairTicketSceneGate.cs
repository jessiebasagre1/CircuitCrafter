using UnityEngine;

/// <summary>
/// Drop this on the Managers object in a COC1/2/3 scene. Whenever the
/// player is here via a COC4 repair ticket (RepairTicketMode.IsActive), it
/// turns off whatever objects are listed for the duration -- same idea, and
/// the same reasons, as COC1AssessmentManager's disableInAssessment array:
///
///  - TaskCheckpointAutosaver would otherwise checkpoint the WHOLE save file
///    the moment the ticket's fault task completes. A repair-ticket visit
///    uses a fresh TaskManager instance that was never restored from the
///    player's real save (loaded via plain SceneManager.LoadScene, not
///    SaveManager.SelectMode), so that checkpoint would silently overwrite
///    their actual campaign progress with this mostly-empty session.
///  - IntroDialogTrigger would replay the "welcome, let's learn PC assembly"
///    tutorial every single time a ticket sends the player here.
///  - QuizManager (if present) would similarly fire quizzes meant for a
///    first-time campaign playthrough, not a repair errand.
///
/// Assign the actual GameObjects carrying those components in the
/// Inspector -- this script doesn't know or care what they are, it just
/// flips them off. Does nothing during normal Story Mode / Assessment play.
/// </summary>
public class RepairTicketSceneGate : MonoBehaviour
{
    [Tooltip("Objects to disable for the duration of a repair-ticket visit, e.g. TaskCheckpointAutosaver, IntroDialogTrigger, QuizManager.")]
    [SerializeField] private GameObject[] disableWhileOnTicket;

    private void Start()
    {
        if (!RepairTicketMode.IsActive) return;

        foreach (var go in disableWhileOnTicket)
        {
            if (go != null) go.SetActive(false);
        }
    }
}
