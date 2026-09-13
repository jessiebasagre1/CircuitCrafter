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
        {
            // Returning here used to mean the cursor was never captured at all on Continue.
            //
            // The main menu frees the cursor deliberately - you need it to click Continue - and
            // Cursor.lockState is a global that survives LoadScene. On a fresh start that gets put
            // right by OnIntroFinished below, which is the only thing in this scene that locks it.
            // Skip the intro and that line never runs: the learner arrives in a first-person scene
            // with a free cursor, MouseLook does nothing, and the game reads as broken on the one
            // path most learners use every lesson after the first.
            //
            // Through CursorControl rather than Cursor.lockState directly, so a phone - where there
            // is no cursor to capture and grabbing it pins every UI tap to the screen centre - is
            // left alone. It also does nothing if a dialog or a station already holds an input lock.
            if (!PlayerInputLock.IsLocked) CursorControl.Capture();
            return;
        }

        if (introDialog != null)
        {
            // Free the cursor and lock player movement/look while the intro plays -- same
            // pattern as this project's other full-screen panels (TicketDiagnosticPanelView,
            // COC4StationController). Without this, nothing stops MouseLook from spinning the
            // camera to any mouse movement made while the player is just trying to read.
            // Checked BEFORE the lock is taken. Unguarded, a scene without a DialogManager threw
            // here - after PlayerInputLock.Acquire() had already run - and the lock has no owner
            // left to release it. The learner stands in the scene unable to move or look, with a
            // NullReferenceException as the only clue. Harmless in COC1, where a DialogManager is
            // always on Managers; a real trap the first time this component is dropped into a
            // scene that does not have one.
            if (DialogManager.Instance == null)
            {
                Debug.LogWarning("[IntroDialogTrigger] No DialogManager in this scene, so the intro " +
                                 "cannot play. Add the Managers prefab, or clear Intro Dialog on " +
                                 "this component.", this);

                if (!PlayerInputLock.IsLocked) CursorControl.Capture();
                return;
            }

            PlayerInputLock.Acquire();
            CursorControl.Free();

            DialogManager.Instance.ShowDialog(introDialog, OnIntroFinished);
        }
    }

    private void OnIntroFinished()
    {
        PlayerInputLock.Release();

        // Only re-lock if nothing else (another dialog, a station) still needs the cursor free.
        // Same route as the Continue path above, for the same mobile reason.
        if (!PlayerInputLock.IsLocked) CursorControl.Capture();

        if (pcTarget != null && WaypointIndicator.Instance != null)
        {
            WaypointIndicator.Instance.SetTarget(pcTarget);
        }
    }
}