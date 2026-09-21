// StationPromptHider.cs
using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Keeps the "Press E to ..." prompt off screen while the player is inside a COC2 station.
    ///
    /// ATTACH THIS TO: any object that is always active in the COC2 scene - the Managers object is
    /// the natural home. One is enough. No Inspector references are needed.
    ///
    /// WHY THIS EXISTS. StationExitButton already tells PlayerInteractor to hide the prompt, but it
    /// only knows seven of the station views, and only does it if the scene has a StationExitButton
    /// whose Only These Stations list includes the one you are in. ConnectivityCheckStation is not
    /// one of the seven, so its console left the prompt showing over the top. This component asks
    /// on behalf of every station, and does not depend on StationExitButton being there.
    ///
    /// HOW. It uses the same per-frame request the rest of the project uses
    /// (PlayerInteractor.SuppressPromptThisFrame). It runs in Update and PlayerInteractor decides
    /// the prompt in LateUpdate, so the prompt is held back on the very frame a station opens
    /// instead of flickering for one frame first. Nothing has to be undone on exit: the moment the
    /// player leaves, this stops asking and the prompt comes back by itself.
    ///
    /// NOTE. That request also hides the touch Interact / Pickup buttons, exactly as it already
    /// does for the stations StationExitButton covers. Inside a station those buttons only offer
    /// to re-enter the station the player is already in.
    /// </summary>
    public class StationPromptHider : MonoBehaviour
    {
        [Tooltip("Turn off to let the prompt show inside stations again (for example while testing).")]
        [SerializeField] private bool hidePromptInStations = true;

        // ConnectivityCheckStation has no static "the open one" the way the others do, so the
        // scene's copies are found once and asked each frame. Inactive ones are included: a
        // station may start switched off and be enabled later.
        private ConnectivityCheckStation[] connectivityStations;

        private void Start()
        {
            Rescan();
        }

        /// <summary>Looks for ConnectivityCheckStation objects again. Only needed if you spawn one
        /// at runtime after this component has started.</summary>
        public void Rescan()
        {
            connectivityStations = FindObjectsByType<ConnectivityCheckStation>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        private void Update()
        {
            if (!hidePromptInStations) return;

            if (InsideAStation())
                CSSSimulation.Interaction.PlayerInteractor.SuppressPromptThisFrame();
        }

        private bool InsideAStation()
        {
            // The same seven views StationExitButton.CurrentStation checks, minus its per-scene
            // Only These Stations filter: whether the prompt should be hidden has nothing to do
            // with whether a Back button is wanted.
            if (CrimpStageViewController.Instance != null &&
                CrimpStageViewController.Instance.InCrimpView) return true;

            if (TestingViewController.Instance != null &&
                TestingViewController.Instance.InTestingView) return true;

            if (CrimpingViewController.Instance != null &&
                CrimpingViewController.Instance.InCrimpingView) return true;

            if (StrippingViewController.Instance != null &&
                StrippingViewController.Instance.InStrippingView) return true;

            if (NetworkTestStation.Instance != null &&
                NetworkTestStation.Instance.InTesterView) return true;

            if (PunchDownStation.Instance != null &&
                PunchDownStation.Instance.InStageView) return true;

            if (DeviceConfigStation.OpenStation != null) return true;

            // The one StationExitButton does not know about.
            if (connectivityStations != null)
            {
                for (int i = 0; i < connectivityStations.Length; i++)
                {
                    ConnectivityCheckStation station = connectivityStations[i];
                    if (station != null && station.IsOpen) return true;
                }
            }

            return false;
        }
    }
}
