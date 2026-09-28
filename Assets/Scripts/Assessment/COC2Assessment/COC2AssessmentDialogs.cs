using System.Collections.Generic;
using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Which dialogs the learner still sees during the COC 2 ASSESSMENT.
    ///
    /// An assessment normally withholds every tutorial dialog. That is right for the teaching - how to
    /// strip, which colour goes where - but it also silenced the messages that belong to the JOB:
    /// "you need the crimping tool first", "the cable failed", "the link is verified". Without them
    /// the learner presses E and nothing happens, or a port resets with no word of why.
    ///
    /// ATTACH THIS TO: the Managers object in the COC 2 scene, next to COC2AssessmentManager. Optional -
    /// COC2AssessmentManager adds one with these defaults if the scene has none. Nothing to assign:
    /// it finds the dialogs on the stations itself. It does nothing outside the COC 2 assessment.
    ///
    ///   Warnings  - no crimping tool, no cable for the tester, patch panel not unlocked yet
    ///   Results   - cable test passed or failed, patch panel passed or failed, crimped cable ready,
    ///               a patch cord run complete, network configuration and connectivity success or
    ///               failure, final inspection passed or failed
    ///
    /// A failed cable or patch panel port says only THAT it failed, unless Show Failure Details is
    /// ticked - then the full Story Mode message, with the tester's reading of what is wrong.
    /// </summary>
    [DisallowMultipleComponent]
    public class COC2AssessmentDialogs : MonoBehaviour
    {
        public static COC2AssessmentDialogs Instance { get; private set; }

        [Header("Shown during the COC 2 assessment")]
        [Tooltip("Warnings that something is missing: entering the crimping station without the " +
                 "crimping tool, the tester without a cable, the patch panel before it is unlocked. " +
                 "They say what is missing, not how to do the job.")]
        public bool allowWarnings = true;

        [Tooltip("Messages that say a task passed, failed or is complete: the cable test, the patch " +
                 "panel, the crimped cable being ready, a patch cord run, the network configuration " +
                 "and connectivity checks, and the final inspection.\n\n" +
                 "A failed cable or panel port always gets a short 'it failed' message, even with this " +
                 "off - otherwise it would reset with no word of why.")]
        public bool allowResults = true;

        [Tooltip("When a cable or a patch panel port fails.\n\n" +
                 "OFF (default): the message says only that it failed and has to be redone.\n\n" +
                 "ON: the full Story Mode message, including the tester's reading of what is wrong - " +
                 "which pin is open, which pair is crossed, which positions are out of order.")]
        public bool showFailureDetails = false;

        [Tooltip("Any other dialog asset that should also be shown during the assessment.")]
        public DialogSequence[] alsoAllow = new DialogSequence[0];

        [Header("Diagnostics")]
        public bool logWhatIsAllowed = true;

        /// <summary>Pass / complete messages allowed right now: a COC 2 assessment with Allow Results
        /// ticked. (The short failure messages that hold a reset are shown regardless.)</summary>
        public static bool ResultsAllowed =>
            Instance != null && Instance.allowResults && AssessmentMode.IsAssessing("COC2");

        /// <summary>Show the tester's diagnosis in a failure message during the COC 2 assessment.</summary>
        public static bool FailureDetailsShown =>
            Instance != null && Instance.showFailureDetails && AssessmentMode.IsAssessing("COC2");

        private readonly List<DialogSequence> added = new List<DialogSequence>();

        // ==================================================================

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            if (!AssessmentMode.IsAssessing("COC2")) return;
            Register();
        }

        private void OnDestroy()
        {
            // The list is static and outlives the scene - take back exactly what was put in.
            foreach (DialogSequence d in added) DialogManager.AllowedWhileSuppressed.Remove(d);
            added.Clear();

            if (Instance == this) Instance = null;
        }

        // ==================================================================

        private void Register()
        {
            if (allowWarnings)
            {
                foreach (var v in Find<CrimpingViewController>()) Add(v.noToolDialog);
                foreach (var t in Find<NetworkTestStation>()) Add(t.noCableDialog);
                foreach (var s in Find<PunchDownStation>()) Add(s.lockedDialog);
            }

            if (allowResults)
            {
                foreach (var d in Find<COC2StageDialogs>())
                {
                    Add(d.passAsset);
                    if (showFailureDetails) Add(d.failAsset);
                }

                foreach (var p in Find<CrimpedCablePickup>()) Add(p.availableDialog);
                foreach (var c in Find<CableCarrier>()) Add(c.completeDialog);

                foreach (var s in Find<DeviceConfigStation>())
                {
                    Add(s.successDialog); Add(s.failDialog); Add(s.allMachinesDialog);
                }

                foreach (var s in Find<ConnectivityCheckStation>())
                {
                    Add(s.successDialog); Add(s.failDialog); Add(s.allMachinesDialog);
                }

                foreach (var i in Find<COC2FinalInspection>())
                {
                    Add(i.passedDialog); Add(i.failedDialog);
                }
            }

            if (alsoAllow != null)
                foreach (DialogSequence d in alsoAllow) Add(d);

            if (logWhatIsAllowed)
            {
                var names = new List<string>();
                foreach (DialogSequence d in added) names.Add(d.name);
                Debug.Log("[COC2AssessmentDialogs] Shown during this assessment: " +
                          (names.Count > 0 ? string.Join(", ", names) : "no dialog assets") +
                          ". Built-in pass/fail messages: " + (allowResults ? "on" : "failures only") +
                          ", failure details: " + (showFailureDetails ? "on" : "off") + ".", this);
            }
        }

        private void Add(DialogSequence d)
        {
            if (d == null || added.Contains(d)) return;
            if (DialogManager.AllowedWhileSuppressed.Add(d)) added.Add(d);
        }

        private static T[] Find<T>() where T : Object
        {
            // Include: some stations start the scene switched off.
            return FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }
    }
}
