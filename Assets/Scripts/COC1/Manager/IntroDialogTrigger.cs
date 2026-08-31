using UnityEngine;

/// <summary>
/// Shows a one-time introduction dialog automatically when the scene starts.
/// Once the player finishes reading it, points the waypoint arrow at the PC.
/// Attach to Managers alongside DialogManager/TaskManager.
///
/// Only plays on a genuinely fresh start (SaveManager.IsContinuingSave ==
/// false). Without this check, this Start() would fire again every single
/// time the player hits Continue from the main menu (this is a normal scene
/// reload, same as a first-time load), reopening the intro dialog on top of
/// whatever the player was doing. PlayerInteractor and PartClickDetector both
/// refuse to process any interaction while DialogManager.IsDialogOpen is
/// true, so until that leftover dialog is manually dismissed, every part on
/// the PC looks completely unresponsive.
/// </summary>
public class IntroDialogTrigger : MonoBehaviour
{
    [SerializeField] private DialogSequence introDialog;
    [SerializeField] private Transform pcTarget;

    private void Start()
    {
        if (SaveManager.Instance != null && SaveManager.Instance.IsContinuingSave)
            return;

        if (introDialog != null)
        {
            // Free the cursor and lock player movement/look while the intro plays -- same
            // pattern as this project's other full-screen panels (TicketDiagnosticPanelView,
            // COC4StationController). Without this, nothing stops MouseLook from spinning the
            // camera to any mouse movement made while the player is just trying to read.
            PlayerInputLock.Acquire();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            DialogManager.Instance.ShowDialog(introDialog, OnIntroFinished);
        }
    }

    private void OnIntroFinished()
    {
        PlayerInputLock.Release();

        // Only re-lock if nothing else (another dialog, a station) still needs the cursor free.
        if (!PlayerInputLock.IsLocked)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (pcTarget != null && WaypointIndicator.Instance != null)
        {
            WaypointIndicator.Instance.SetTarget(pcTarget);
        }
    }
}