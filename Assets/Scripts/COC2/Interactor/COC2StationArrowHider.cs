// COC2StationArrowHider.cs
using System.Collections.Generic;
using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Hides the TaskWaypointArrow while the player is inside ANY COC 2 station, and brings it back
    /// the moment they step out. One component for the whole scene - nothing to wire per station.
    ///
    /// ATTACH THIS TO: any always-active object in the COC 2 scene - the Managers object is right.
    /// Every field is optional; with none filled in it finds the arrow and every station itself.
    ///
    /// STATIONS IT WATCHES (all found automatically)
    ///   - Crimping station: cable choice panel, stripping view, wiring view, crimp view, testing view
    ///   - Network cable tester
    ///   - Punch-down (patch panel) station
    ///   - Device configuration and connectivity check stations
    ///   - Anything you add to Extra Views below
    ///
    /// WHY HIDE IT
    /// Inside a station the player looks through a close-up camera at a workbench. The arrow points
    /// at the NEXT place to walk to, somewhere behind them - so in here it is only clutter, and on a
    /// phone it sits on top of the wires they are trying to drag.
    ///
    /// It uses the arrow's own Suppress / Resume, so the arrow keeps tracking the current step while
    /// hidden and is correct the moment it reappears. In an assessment the arrow is off for the whole
    /// run unless its Allow In Assessment is ticked - this never overrides that either way.
    ///
    /// If any station already has its On Stage Opened / On Stage Closed wired to the arrow's
    /// Suppress / Resume, that wiring can stay - it does the same thing - or be removed.
    /// </summary>
    [DisallowMultipleComponent]
    public class COC2StationArrowHider : MonoBehaviour
    {
        [Tooltip("The scene's TaskWaypointArrow. Leave empty to use the one in the scene.")]
        public TaskWaypointArrow arrow;

        [Header("Label")]
        [Tooltip("Hide the arrow's instruction label as well while in a station, not only the arrow. " +
                 "Leave off to keep the label visible (the arrow's normal behaviour when suppressed).")]
        public bool hideLabelToo = true;

        [Tooltip("The label's panel, if the text sits on a background. Leave empty to hide just the " +
                 "arrow's Label Text and Distance Text.")]
        public GameObject labelRoot;

        [Header("Extra views (optional)")]
        [Tooltip("Any other close-up panel or camera that counts as 'inside a station'. While any of " +
                 "these is active, the arrow hides too.")]
        public GameObject[] extraViews = new GameObject[0];

        [Header("Diagnostics")]
        public bool logChanges = false;

        // ------------------------------------------------------------------

        private PunchDownStation[] punchStations = new PunchDownStation[0];
        private ConnectivityCheckStation[] connectivityStations = new ConnectivityCheckStation[0];
        private DeviceConfigStation[] configStations = new DeviceConfigStation[0];
        private CableTypeSelectionUI[] cableChoices = new CableTypeSelectionUI[0];

        private readonly List<GameObject> hiddenLabels = new List<GameObject>();
        private bool hiding;

        private TaskWaypointArrow Arrow => arrow != null ? arrow : TaskWaypointArrow.Instance;

        private void Start()
        {
            // Stations are built into the scene, not spawned, so one search is enough. Include is
            // needed: some of them sit on objects that start switched off.
            punchStations = FindObjectsByType<PunchDownStation>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            connectivityStations = FindObjectsByType<ConnectivityCheckStation>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            configStations = FindObjectsByType<DeviceConfigStation>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            cableChoices = FindObjectsByType<CableTypeSelectionUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (Arrow == null)
                Debug.LogWarning("[COC2StationArrowHider] No TaskWaypointArrow in this scene - nothing to hide.", this);
        }

        private void LateUpdate()
        {
            bool inStation = AnyStationOpen();
            if (inStation == hiding) return;

            hiding = inStation;
            Apply();
        }

        private void OnDisable()
        {
            // Never leave the arrow hidden behind a component that has stopped watching.
            if (!hiding) return;
            hiding = false;
            Apply();
        }

        // ------------------------------------------------------------------

        /// <summary>True while the player is inside any COC 2 station.</summary>
        public bool AnyStationOpen()
        {
            // The crimping station is really five views in a row, one after another.
            if (StrippingViewController.Instance != null && StrippingViewController.Instance.InStrippingView) return true;
            if (CrimpStageViewController.Instance != null && CrimpStageViewController.Instance.InCrimpView) return true;
            if (CrimpingViewController.Instance != null && CrimpingViewController.Instance.InCrimpingView) return true;
            if (TestingViewController.Instance != null && TestingViewController.Instance.InTestingView) return true;
            if (NetworkTestStation.Instance != null && NetworkTestStation.Instance.InTesterView) return true;

            foreach (CableTypeSelectionUI c in cableChoices) if (c != null && c.IsOpen) return true;
            foreach (PunchDownStation s in punchStations) if (s != null && s.InStageView) return true;
            foreach (ConnectivityCheckStation s in connectivityStations) if (s != null && s.IsOpen) return true;
            foreach (DeviceConfigStation s in configStations) if (s != null && s.IsOpen) return true;

            if (extraViews != null)
                foreach (GameObject view in extraViews) if (view != null && view.activeInHierarchy) return true;

            return false;
        }

        private void Apply()
        {
            TaskWaypointArrow a = Arrow;
            if (a == null) return;

            if (hiding) a.Suppress();
            else a.Resume();

            // When an assessment has switched the label off (the whole arrow is off, or it is kept
            // without its label), leave it alone - or stepping out of a station would switch it back on.
            if (hideLabelToo && !a.LabelLockedForAssessment) SetLabelVisible(a, !hiding);

            if (logChanges)
                Debug.Log("[COC2StationArrowHider] " + (hiding ? "Inside a station - arrow hidden." : "Out of the station - arrow back."), this);
        }

        private void SetLabelVisible(TaskWaypointArrow a, bool visible)
        {
            if (visible)
            {
                // Only what this component switched off - never something that was already off.
                foreach (GameObject go in hiddenLabels) if (go != null) go.SetActive(true);
                hiddenLabels.Clear();
                return;
            }

            hiddenLabels.Clear();
            if (labelRoot != null)
            {
                Hide(labelRoot);
                return;
            }

            if (a.labelText != null) Hide(a.labelText.gameObject);
            if (a.distanceText != null && a.distanceText != a.labelText) Hide(a.distanceText.gameObject);
        }

        private void Hide(GameObject go)
        {
            if (go == null || !go.activeSelf) return;
            go.SetActive(false);
            hiddenLabels.Add(go);
        }
    }
}