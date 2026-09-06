using UnityEngine;

namespace CSSSimulation.WindowsSim
{
    /// <summary>
    /// Makes the desktop remember what the learner has already done.
    ///
    /// ATTACH THIS TO: a window root - NOT to the row it reveals. A component on a switched-off
    /// object never runs, so a row that hid itself could never show itself again. The builder puts
    /// these on the window root, which is active whenever the window is open.
    ///
    /// WHAT IT IS FOR
    /// Before this, every window looked the same on every visit. The Users folder was listed in
    /// File Explorer before it had been created; STUDENTS appeared in the permissions list before
    /// the group existed. That is the difference between a simulation and a set of pictures, and
    /// the learner feels it - nothing they do leaves a mark.
    ///
    /// Now: create the folder and it appears. Create the group and it turns up where a group would.
    /// Share the folder and the Sharing tab stops saying "Not Shared".
    ///
    /// STATE COMES FROM WinActionBus, which records every action id that has been accepted. So the
    /// window does not have to have been open when the thing happened - it catches up on open.
    ///
    /// IF A ROW NEVER APPEARS and you need it to, tick the row's GameObject on in the Inspector and
    /// delete this component. Nothing else depends on it.
    /// </summary>
    public class WinRevealOnAction : MonoBehaviour
    {
        [TooltipAttribute("The WinTaskGate action id that makes this true, e.g. \"create_user_folder\".")]
        public string actionId = "";

        [TooltipAttribute("Hidden until that action succeeds, then shown.")]
        public GameObject[] show;

        [TooltipAttribute("Shown until that action succeeds, then hidden - the \"Not Shared\" label.")]
        public GameObject[] hide;

        // ------------------------------------------------------------------

        private void Awake()
        {
            WinActionBus.OnAction += Handle;
            Apply();
        }

        private void OnEnable()
        {
            // The window may have been shut while the action happened elsewhere.
            Apply();
        }

        private void OnDestroy()
        {
            WinActionBus.OnAction -= Handle;
        }

        private void Handle(string id, bool success)
        {
            if (!success || id != actionId) return;
            Apply();
        }

        private void Apply()
        {
            bool done = WinActionBus.HasSucceeded(actionId);

            SetAll(show, done);
            SetAll(hide, !done);
        }

        private static void SetAll(GameObject[] items, bool active)
        {
            if (items == null) return;

            for (int i = 0; i < items.Length; i++)
                if (items[i] != null && items[i].activeSelf != active)
                    items[i].SetActive(active);
        }
    }
}
