using System.Collections.Generic;
using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Feeds COC 2's real mistakes into COC2AssessmentManager.
    ///
    /// ATTACH THIS TO: the same object as COC2AssessmentManager - the Managers object in the COC 2
    /// scene. There is nothing to fill in: it finds the stations itself.
    ///
    /// WHY THIS EXISTS
    /// Before this, the only mistake COC 2 could record was a cable that failed the tester, wired
    /// by hand from COC2StageDialogs > On Test Failed. Everything else a learner can get wrong went
    /// unrecorded - a patch panel terminated out of colour order, a cord run to the wrong switch
    /// port, an IP address the machine rejected, a ping that never answered. Your own Console shows
    /// them happening ("Server: rejected, attempt 1") while the assessment report says "Mistakes: 0".
    /// A report that cannot see four of the five ways to fail is not assessment evidence.
    ///
    /// WHY IN CODE RATHER THAN INSPECTOR EVENTS
    /// Every one of these stations already raises an event. They could be dragged onto this
    /// manager's methods one by one - eight wires across six objects, re-done whenever a station is
    /// duplicated for a second port or a second machine, and silent when one is missed. Subscribing
    /// in code cannot be wired wrong and cannot be half-done, and a station added later is picked
    /// up with no work at all.
    ///
    /// IT DOES NOTHING OUTSIDE ASSESSMENT MODE. A story run never reaches past the first line.
    /// </summary>
    [DefaultExecutionOrder(50)]   // after the stations' own Awake/Start, so their events exist
    public class COC2AssessmentHooks : MonoBehaviour
    {
        [Tooltip("Leave on. Prints what it managed to subscribe to, so a station that is not being " +
                 "watched says so at the start of the run instead of being discovered missing when " +
                 "a report comes back with no mistakes on it.")]
        [SerializeField] private bool logWhatIsWatched = true;

        private PunchStageBoard[] punchBoards;
        private PatchCordBoard[] cordBoards;
        private DeviceConfigStation[] configStations;
        private ConnectivityCheckStation[] pingStations;

        private readonly List<KeyValuePair<PatchCordBoard, System.Action<bool, string>>> cordHandlers =
            new List<KeyValuePair<PatchCordBoard, System.Action<bool, string>>>();

        private bool subscribed;

        // ==================================================================

        private void Start()
        {
            if (!AssessmentMode.IsAssessing("COC2")) return;

            // FindObjectsInactive.Include matters here: both ConnectivityCheckStations start the
            // scene switched off - your InteractButtonProbe report shows ServerCheckPoint and
            // ClientCheckPoint at active=False - and an Exclude search would silently watch neither
            // of the two stations that grade the final competency.
            punchBoards = FindObjectsByType<PunchStageBoard>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            cordBoards = FindObjectsByType<PatchCordBoard>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            configStations = FindObjectsByType<DeviceConfigStation>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            pingStations = FindObjectsByType<ConnectivityCheckStation>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (PunchStageBoard b in punchBoards)
                if (b != null) b.OnFinished += HandlePunchFinished;

            foreach (PatchCordBoard b in cordBoards)
            {
                if (b == null) continue;

                // Captured into a local, or every phase would report the LAST board's name - the
                // classic closure-over-the-loop-variable bug, and one that would be invisible here
                // because both phases really do fail the same way.
                PatchCordBoard captured = b;
                System.Action<bool, string> handler =
                    (passed, reason) => HandleCordFinished(captured, passed, reason);

                captured.OnFinished += handler;
                cordHandlers.Add(new KeyValuePair<PatchCordBoard, System.Action<bool, string>>(captured, handler));
            }

            foreach (DeviceConfigStation s in configStations)
            {
                if (s == null || s.OnFailed == null) continue;
                DeviceConfigStation captured = s;
                captured.OnFailed.AddListener(() => HandleConfigRejected(captured));
            }

            foreach (ConnectivityCheckStation s in pingStations)
            {
                if (s == null || s.OnFailed == null) continue;
                ConnectivityCheckStation captured = s;
                captured.OnFailed.AddListener(() => HandlePingFailed(captured));
            }

            subscribed = true;

            if (logWhatIsWatched)
            {
                Debug.Log($"[COC2AssessmentHooks] Watching {punchBoards.Length} punch-down board(s), " +
                          $"{cordBoards.Length} patch-cord phase(s), {configStations.Length} device " +
                          $"config station(s) and {pingStations.Length} connectivity station(s).", this);

                if (punchBoards.Length == 0 || cordBoards.Length == 0 ||
                    configStations.Length == 0 || pingStations.Length == 0)
                {
                    Debug.LogWarning("[COC2AssessmentHooks] One of those is zero. Mistakes made at " +
                                     "that station can never be recorded, and the report will " +
                                     "understate what happened.", this);
                }
            }
        }

        private void OnDestroy()
        {
            if (!subscribed) return;

            // Only the C# events need taking off by hand - and the patch-cord ones only come off
            // because the exact delegate was kept. A lambda cannot be unsubscribed by writing it
            // out a second time: each -= would build a NEW delegate that matches nothing, remove
            // nothing, and leave a handler pointing at a destroyed component.
            //
            // The UnityEvent listeners were added at runtime and go with the scene.
            if (punchBoards != null)
                foreach (PunchStageBoard b in punchBoards)
                    if (b != null) b.OnFinished -= HandlePunchFinished;

            foreach (var pair in cordHandlers)
                if (pair.Key != null) pair.Key.OnFinished -= pair.Value;

            cordHandlers.Clear();
        }

        // ==================================================================

        /// <summary>
        /// A punch-down block that did not pass T568A.
        ///
        /// Graded as heavily as a ruined cable, and for the same reason: an insulation-displacement
        /// contact is a one-shot connection. Punching a conductor into the wrong position means
        /// cutting it back and re-terminating with less slack, and the trainee has spent part of
        /// the 13 mm of untwist the standard allows them.
        /// </summary>
        private void HandlePunchFinished(bool passed, string reason)
        {
            if (passed) return;

            COC2AssessmentManager mgr = COC2AssessmentManager.Instance;
            if (mgr == null) return;

            mgr.ReportTerminationFailure(reason, this);
        }

        /// <summary>A cord run to a socket outside the group the phase asked for.</summary>
        private void HandleCordFinished(PatchCordBoard board, bool passed, string reason)
        {
            if (passed) return;

            COC2AssessmentManager mgr = COC2AssessmentManager.Instance;
            if (mgr == null) return;

            string phase = board != null && !string.IsNullOrWhiteSpace(board.phaseName)
                ? board.phaseName
                : "patch cords";

            string description = string.IsNullOrEmpty(reason)
                ? $"'{phase}' - a cord went to the wrong socket."
                : $"'{phase}' - {reason}";

            mgr.AddMistake(description, board);
        }

        /// <summary>An address, mask or gateway the machine would not accept.</summary>
        private void HandleConfigRejected(DeviceConfigStation station)
        {
            COC2AssessmentManager mgr = COC2AssessmentManager.Instance;
            if (mgr == null) return;

            string who = station != null && !string.IsNullOrWhiteSpace(station.stationName)
                ? station.stationName
                : "a machine";

            mgr.AddMistake($"{who} - the network configuration was rejected.", station);
        }

        /// <summary>
        /// A ping that did not answer.
        ///
        /// Worth recording even though it is usually a SYMPTOM of something already penalised - a
        /// mis-terminated panel, a cord in the wrong port, an address typed wrong. The report is
        /// read by an assessor, not only scored, and "configured the client wrongly, then could not
        /// ping the server" is the sequence that shows whether the trainee understood what they had
        /// done. It carries the ordinary penalty rather than the heavy one for that reason.
        /// </summary>
        private void HandlePingFailed(ConnectivityCheckStation station)
        {
            COC2AssessmentManager mgr = COC2AssessmentManager.Instance;
            if (mgr == null) return;

            string who = station != null && !string.IsNullOrWhiteSpace(station.stationName)
                ? station.stationName
                : "a machine";

            mgr.AddMistake($"{who} - the connectivity check could not reach its peer.", station);
        }
    }
}
