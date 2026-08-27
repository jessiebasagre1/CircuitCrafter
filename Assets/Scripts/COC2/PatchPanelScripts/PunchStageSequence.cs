// PunchStageSequence.cs
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Runs the punch-down stage across all eight ports instead of one.
    ///
    /// HOW IT WORKS, and why it is this small. Every port on a patch panel is terminated to the SAME
    /// colour order - there is no per-port variation to model. So eight ports is the same eight
    /// conductors, the same eight pins and the same judgement, done eight times against a different
    /// block. Rather than build eight of everything, this moves the one set of pin markers along to
    /// the next port's block and resets the board.
    ///
    /// It finds the blocks by name from the panel model, so there is nothing to place: as long as
    /// your pin markers start on port 1, the offsets come from the model's own geometry and stay
    /// correct if you rescale or reposition the stage.
    ///
    /// A FAILED PORT DOES NOT ADVANCE. The player repeats the same port until it passes, which is
    /// what an assessor would make them do.
    ///
    /// Setup:
    ///   1. Put this on PunchDownStage, next to PunchStageBoard.
    ///   2. Assign Pin Group - the parent holding your eight PunchStagePin markers, placed on PORT 1.
    ///   3. Assign it to PunchDownStation > Sequence, so the station knows to keep the view open
    ///      between ports.
    ///   4. Set COC2TaskSetup > Patch Panel Steps to 8, or the task completes after the first port.
    /// </summary>
    [DefaultExecutionOrder(150)]     // after PunchStageBoard has found its pins
    public class PunchStageSequence : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("Leave empty to use the PunchStageBoard on this object.")]
        public PunchStageBoard board;

        [Tooltip("The parent object holding the eight PunchStagePin markers. This is what gets moved " +
                 "from port to port, so the markers must be children of it and placed on PORT 1.")]
        public Transform pinGroup;

        [Header("Conductors - pick ONE of these two")]
        [Tooltip("ONE SET PER PORT. Eight parents, one per port, each holding that port's eight " +
                 "conductors - typically UTPCable_01 through UTPCable_08.\n\n" +
                 "USE THIS ONE. At the end of a port the board simply stops referring to that " +
                 "cable's conductors and picks up the next one's, so everything already punched " +
                 "STAYS terminated and visible in its port. That is what a real panel looks like " +
                 "halfway through a job.")]
        public Transform[] conductorSets;

        [Tooltip("A SINGLE tray that slides from port to port. Only for a one-cable stage set.\n\n" +
                 "Be aware of what this means: there is only one set of eight conductors, so the " +
                 "ones you punched into port 1 are the same objects port 2 needs. Moving on RESETS " +
                 "them - port 1 ends up empty again. Fine for testing one port, wrong for a run of " +
                 "eight. Fill Conductor Sets above instead.\n\n" +
                 "Ignored when Conductor Sets has entries.")]
        public Transform conductorGroup;

        [Header("Ports")]
        [Range(1, 8)] public int portCount = 8;

        [Tooltip("Name of each port's block, with {0:00} standing in for the port number. Use the " +
                 "A blocks or the B blocks to match your Target Standard - it only decides which " +
                 "row the offsets are measured from, and the two rows are aligned anyway.")]
        public string blockNameFormat = "PunchDown_B_{0:00}";

        [Tooltip("Optional override. Fill this with eight transforms and the block names above are " +
                 "ignored - useful if your panel is named differently or the ports aren't evenly " +
                 "spaced.")]
        public Transform[] portAnchors;

        [Header("Readout (optional)")]
        [Tooltip("Shows 'Port 3 of 8'.")]
        public TMP_Text portText;
        public string portFormat = "Port {0} of {1}";

        [Header("Events")]
        [Tooltip("Fires each time a port is terminated correctly and the run moves on.")]
        public UnityEvent OnPortPassed;
        [Tooltip("Fires once all eight ports are done.")]
        public UnityEvent OnAllPortsComplete;

        [Header("Reporting")]
        public bool logResult = true;

        /// <summary>0-based index of the port being worked on.</summary>
        public int CurrentPort { get; private set; }
        public bool AllComplete { get; private set; }

        Vector3[] offsets;
        Vector3 pinHome;
        Vector3 trayHome;
        bool measured;

        /// <summary>True when each port has its own cable, which is the arrangement that keeps
        /// finished terminations on screen.</summary>
        public bool UsingSets
        {
            get { return conductorSets != null && conductorSets.Length > 0 && conductorSets[0] != null; }
        }

        void SwapToSet(int index)
        {
            if (board == null) return;
            if (conductorSets == null || index < 0 || index >= conductorSets.Length) return;

            Transform set = conductorSets[index];
            if (set == null)
            {
                Debug.LogWarning("[PunchStageSequence] Conductor Sets has no entry for port " +
                                 (index + 1) + ", so the board is still holding the previous port's " +
                                 "cable. Fill all " + portCount + " slots.", this);
                return;
            }

            var found = set.GetComponentsInChildren<PunchStageConductor>(true);
            if (found.Length == 0)
            {
                Debug.LogWarning("[PunchStageSequence] '" + set.name + "' has no PunchStageConductor " +
                                 "under it. If its wires use PunchStageConductorRig, remember the " +
                                 "component lands on the _Tip objects the rig builds - which are " +
                                 "siblings of the meshes, so point this at their shared parent.", this);
                return;
            }
            if (found.Length != 8)
            {
                Debug.LogWarning("[PunchStageSequence] '" + set.name + "' holds " + found.Length +
                                 " conductors; a port needs 8.", this);
            }

            board.SetConductors(found);
            if (logResult)
                Debug.LogWarning("[PunchStageSequence] Port " + (index + 1) + " will be terminated " +
                                 "with '" + set.name + "' (" + found.Length + " conductors).", this);
        }

        void RecaptureConductorHomes()
        {
            if (board == null || board.conductors == null) return;
            foreach (var c in board.conductors)
            {
                if (c != null) c.RecaptureHome();
            }
        }

        void Start()
        {
            if (board == null) board = GetComponent<PunchStageBoard>();
            Measure();
            GoToPort(0);
        }

        /// <summary>
        /// Works out how far the pin markers travel between ports, straight from the model. Doing it
        /// this way rather than with a typed step means a rescaled stage, a re-imported FBX or a
        /// nudged panel all stay correct with nothing to update by hand.
        /// </summary>
        void Measure()
        {
            offsets = new Vector3[portCount];

            if (portAnchors != null && portAnchors.Length >= portCount)
            {
                for (int i = 0; i < portCount; i++)
                {
                    if (portAnchors[i] == null || portAnchors[0] == null) return;
                    offsets[i] = portAnchors[i].position - portAnchors[0].position;
                }
                measured = true;
                return;
            }

            Transform first = FindBlock(1);
            if (first == null)
            {
                Debug.LogWarning("[PunchStageSequence] No object named '" +
                                 string.Format(blockNameFormat, 1) + "' under the board's search root(s), so " +
                                 "the run cannot move between ports. Add the panel to " +
                                 "PunchStageBoard > Search Roots, fill Port Anchors, or fix Block " +
                                 "Name Format.", this);
                return;
            }

            for (int i = 0; i < portCount; i++)
            {
                Transform b = FindBlock(i + 1);
                if (b == null)
                {
                    Debug.LogWarning("[PunchStageSequence] Port " + (i + 1) + " has no block named '" +
                                     string.Format(blockNameFormat, i + 1) + "'. Stopping the run at " +
                                     i + " port(s).", this);
                    portCount = Mathf.Max(1, i);
                    break;
                }
                offsets[i] = b.position - first.position;
            }
            measured = true;
        }

        /// <summary>
        /// Asks the board, so all three components agree on where the panel is. That matters once
        /// the stage works on the REAL panel: the blocks are then nowhere near this object, and a
        /// search of its own children would find nothing.
        /// </summary>
        Transform FindBlock(int portNumber)
        {
            string want = string.Format(blockNameFormat, portNumber);
            if (board != null) return board.FindByName(want);

            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == want) return t;
            }
            return null;
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Called by PunchDownStation once the player has read the pass dialog. Returns TRUE if
        /// there is another port to do, which tells the station to keep the view open instead of
        /// dropping the player back into the world after one termination.
        /// </summary>
        public bool AdvanceOnPass()
        {
            OnPortPassed?.Invoke();

            if (CurrentPort + 1 >= portCount)
            {
                AllComplete = true;
                if (logResult)
                    Debug.LogWarning("[PunchStageSequence] All " + portCount + " ports terminated.", this);
                OnAllPortsComplete?.Invoke();
                return false;
            }

            // ORDER MATTERS, and differently for the two modes.
            //
            // With CONDUCTOR SETS, GoToPort hands the board the NEXT cable's conductors before
            // anything is reset - so ResetStage only ever touches the incoming set. The cable just
            // finished is left exactly as the player left it: locked, seated, terminated. Reset
            // first and it would unlock those eight and send them home, emptying the port that was
            // just completed.
            //
            // With a single sliding TRAY there is only one set, so it has to be reset to its old
            // home first, then moved, then told where home is now.
            if (UsingSets)
            {
                GoToPort(CurrentPort + 1);
                if (board != null) board.ResetStage();
            }
            else
            {
                if (board != null) board.ResetStage();
                GoToPort(CurrentPort + 1);
                RecaptureConductorHomes();
            }
            return true;
        }

        void GoToPort(int index)
        {
            CurrentPort = Mathf.Clamp(index, 0, portCount - 1);

            if (pinGroup == null)
            {
                Debug.LogWarning("[PunchStageSequence] Pin Group is empty, so the markers stay on " +
                                 "port 1 for every round.", this);
            }
            else if (measured)
            {
                // Recorded on the first move, not at Start - so it is the pose you actually placed
                // the markers in, whatever else has run by then.
                if (CurrentPort == 0 && pinHome == Vector3.zero) pinHome = pinGroup.position;
                pinGroup.position = pinHome + offsets[CurrentPort];
            }

            if (UsingSets)
            {
                SwapToSet(CurrentPort);
            }
            else if (conductorGroup != null && measured)
            {
                if (CurrentPort == 0 && trayHome == Vector3.zero) trayHome = conductorGroup.position;
                conductorGroup.position = trayHome + offsets[CurrentPort];
            }

            if (portText != null)
                portText.text = string.Format(portFormat, CurrentPort + 1, portCount);

            if (logResult)
                Debug.LogWarning("[PunchStageSequence] Now terminating port " + (CurrentPort + 1) +
                                 " of " + portCount + ".", this);
        }

        /// <summary>Back to port 1. Call from a Redo button, or when the whole procedure restarts.</summary>
        [ContextMenu("Restart from port 1")]
        public void RestartRun()
        {
            AllComplete = false;
            if (UsingSets)
            {
                // Every cable goes back, not just the one in hand - a restart means the whole panel
                // is untouched again.
                foreach (var set in conductorSets)
                {
                    if (set == null) continue;
                    foreach (var c in set.GetComponentsInChildren<PunchStageConductor>(true))
                    {
                        if (c != null) c.ResetConductor();
                    }
                }
                GoToPort(0);
                if (board != null) board.ResetStage();
            }
            else
            {
                if (board != null) board.ResetStage();
                GoToPort(0);
                RecaptureConductorHomes();
            }
        }
    }
}