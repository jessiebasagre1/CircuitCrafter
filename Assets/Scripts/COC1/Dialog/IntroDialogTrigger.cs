using UnityEngine;

/// <summary>
/// Shows a one-time introduction dialog automatically when the scene starts.
/// Once the player finishes reading it, points the waypoint arrow at the PC.
/// Attach to Managers alongside DialogManager/TaskManager.
/// </summary>
public class IntroDialogTrigger : MonoBehaviour
{
    [SerializeField] private DialogSequence introDialog;
    [SerializeField] private Transform pcTarget;

    private void Start()
    {
        if (introDialog != null)
        {
            DialogManager.Instance.ShowDialog(introDialog, OnIntroFinished);
        }
    }

    private void OnIntroFinished()
    {
        if (pcTarget != null && WaypointIndicator.Instance != null)
        {
            WaypointIndicator.Instance.SetTarget(pcTarget);
        }
    }
}