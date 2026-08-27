// PunchStageCamera.cs
using UnityEngine;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// Drives the stage camera through the shot the stage wants: open wide on the whole panel while
    /// the briefing plays, push in on port 1 when the player is ready to work, then slide along to
    /// each next port as it is finished.
    ///
    /// GOES ON THE STAGE CAMERA ITSELF. PunchDownStation switches that object on and off with the
    /// stage, so OnEnable is exactly "the player just entered" - no event to subscribe to and
    /// nothing to reset by hand between visits.
    ///
    /// IT ASKS, IT IS NOT TOLD. The move to the next port is triggered by watching
    /// PunchStageSequence.CurrentPort change, not by a call. That means the sequence needs no
    /// reference to this and the camera is entirely optional - delete it and the stage still plays,
    /// just without the framing.
    ///
    /// EVERYTHING RUNS ON UNSCALED TIME. The briefing dialog can stop the clock, and a camera move
    /// driven by Time.deltaTime would sit frozen mid-flight.
    ///
    /// Setup:
    ///   1. Put this on StageCamera.
    ///   2. Assign Board.
    ///   3. Create two empties, Shot_Wide and Shot_Close, and pose them (Part 09b of the guide).
    ///   4. Assign them to Wide View and Close View. Close View is framed on PORT 1.
    /// </summary>
    [DefaultExecutionOrder(160)]
    public class PunchStageCamera : MonoBehaviour
    {
        public enum Phase { Wide, MovingIn, AtPort, MovingToPort }

        [Header("Refs")]
        [Tooltip("The board this camera is looking at.")]
        public PunchStageBoard board;

        [Tooltip("Leave empty and the sequence is found in the scene. Without one, the camera pushes " +
                 "in on port 1 and stays there, which is right for a single-port run.")]
        public PunchStageSequence sequence;

        [Header("Poses")]
        [Tooltip("The establishing shot - far enough back to see all eight ports. Held while the " +
                 "briefing is on screen.")]
        public Transform wideView;

        [Tooltip("The working shot, framed on PORT 1. Every other port is this pose slid sideways by " +
                 "the distance between blocks, measured from the model.")]
        public Transform closeView;

        [Header("Timing")]
        [Tooltip("Hold the wide shot at least this long after the briefing closes, so the push-in " +
                 "doesn't start the instant the player clicks.")]
        public float wideHoldSeconds = 0.6f;

        [Tooltip("Hold wide until the briefing dialog has been read. With no dialog assigned, the " +
                 "hold above is all you get.")]
        public bool waitForDialog = true;

        [Tooltip("Seconds for the push-in from wide to port 1. This is the shot that tells the " +
                 "player the work has started - don't rush it.")]
        public float moveInDuration = 1.2f;

        [Tooltip("Seconds to slide from one port to the next. Shorter: it happens seven times.")]
        public float portMoveDuration = 0.7f;

        [Header("Ports")]
        [Range(1, 8)] public int portCount = 8;
        [Tooltip("Same format as PunchStageSequence, and for the same reason: the sideways offsets " +
                 "are measured from the model's own blocks so a rescaled stage stays correct.")]
        public string blockNameFormat = "PunchDown_B_{0:00}";
        [Tooltip("Optional override, one transform per port. Ignores the names above.")]
        public Transform[] portAnchors;

        [Header("Behaviour")]
        [Tooltip("Block dragging while the camera is flying. Leave ON - a drag started mid-move is " +
                 "computed from a camera that is still travelling, so the conductor slides across " +
                 "the screen on its own.")]
        public bool suspendInputWhileMoving = true;

        public bool logResult = true;

        public Phase CurrentPhase { get; private set; }

        Vector3[] offsets;
        Vector3 fromPos, toPos;
        Quaternion fromRot, toRot;
        float t, duration;
        float held;
        int shownPort = -1;
        bool sawDialog;

        void OnEnable()
        {
            if (board == null) board = Object.FindFirstObjectByType<PunchStageBoard>(FindObjectsInactive.Include);
            if (sequence == null) sequence = Object.FindFirstObjectByType<PunchStageSequence>(FindObjectsInactive.Include);

            Measure();

            held = 0f;
            sawDialog = false;
            shownPort = -1;
            CurrentPhase = Phase.Wide;

            if (wideView != null) Snap(wideView.position, wideView.rotation);
            Suspend(true);
        }

        void OnDisable()
        {
            Suspend(false);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;

            switch (CurrentPhase)
            {
                case Phase.Wide:
                    TickWide(dt);
                    break;

                case Phase.MovingIn:
                case Phase.MovingToPort:
                    TickMove(dt);
                    break;

                case Phase.AtPort:
                    WatchForPortChange();
                    break;
            }
        }

        // ------------------------------------------------------------------

        void TickWide(float dt)
        {
            bool dialogOpen = DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen;

            if (waitForDialog)
            {
                if (dialogOpen)
                {
                    // Reading. Keep the wide shot up and start the hold again once it closes.
                    sawDialog = true;
                    held = 0f;
                    return;
                }

                // No dialog ever appeared - nothing was assigned. Fall through to the plain hold
                // rather than sitting on the establishing shot forever.
            }

            held += dt;
            if (held < wideHoldSeconds) return;

            int port = sequence != null ? sequence.CurrentPort : 0;
            BeginMove(PoseFor(port), moveInDuration, Phase.MovingIn);
            shownPort = port;

            if (logResult)
                Debug.LogWarning("[PunchStageCamera] Pushing in on port " + (port + 1) +
                                 (sawDialog ? " - briefing read." : " - no briefing was shown."), this);
        }

        void WatchForPortChange()
        {
            if (sequence == null) return;
            if (sequence.CurrentPort == shownPort) return;

            shownPort = sequence.CurrentPort;
            BeginMove(PoseFor(shownPort), portMoveDuration, Phase.MovingToPort);

            if (logResult)
                Debug.LogWarning("[PunchStageCamera] Moving to port " + (shownPort + 1) + ".", this);
        }

        void TickMove(float dt)
        {
            if (duration <= 0f) { Arrive(); return; }

            t += dt / duration;
            float f = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            transform.position = Vector3.Lerp(fromPos, toPos, f);
            transform.rotation = Quaternion.Slerp(fromRot, toRot, f);

            if (t >= 1f) Arrive();
        }

        void Arrive()
        {
            transform.SetPositionAndRotation(toPos, toRot);
            CurrentPhase = Phase.AtPort;
            Suspend(false);
        }

        // ------------------------------------------------------------------

        void BeginMove(Pose target, float seconds, Phase phase)
        {
            fromPos = transform.position;
            fromRot = transform.rotation;
            toPos = target.position;
            toRot = target.rotation;
            t = 0f;
            duration = Mathf.Max(0f, seconds);
            CurrentPhase = phase;
            Suspend(true);
        }

        void Snap(Vector3 p, Quaternion r)
        {
            transform.SetPositionAndRotation(p, r);
        }

        void Suspend(bool on)
        {
            if (!suspendInputWhileMoving) return;
            if (board != null) board.InputSuspended = on;
        }

        /// <summary>The close-up pose for a port: port 1's framing, slid sideways by however far
        /// that port's block sits from the first one.</summary>
        Pose PoseFor(int index)
        {
            if (closeView == null)
            {
                Debug.LogWarning("[PunchStageCamera] No Close View assigned - staying wide.", this);
                return new Pose(transform.position, transform.rotation);
            }

            Vector3 offset = Vector3.zero;
            if (offsets != null && index >= 0 && index < offsets.Length) offset = offsets[index];
            return new Pose(closeView.position + offset, closeView.rotation);
        }

        void Measure()
        {
            offsets = new Vector3[portCount];

            if (portAnchors != null && portAnchors.Length >= portCount && portAnchors[0] != null)
            {
                for (int i = 0; i < portCount; i++)
                {
                    if (portAnchors[i] == null) continue;
                    offsets[i] = portAnchors[i].position - portAnchors[0].position;
                }
                return;
            }

            Transform first = FindBlock(1);
            if (first == null)
            {
                if (logResult && portCount > 1)
                    Debug.LogWarning("[PunchStageCamera] No block named '" +
                                     string.Format(blockNameFormat, 1) + "' in the scene, so the " +
                                     "camera cannot slide between ports. It will stay on port 1's " +
                                     "framing. Fill Port Anchors, or fix Block Name Format.", this);
                return;
            }

            for (int i = 0; i < portCount; i++)
            {
                Transform b = FindBlock(i + 1);
                if (b != null) offsets[i] = b.position - first.position;
            }
        }

        Transform FindBlock(int portNumber)
        {
            // Through the board, so the camera looks wherever the board looks - including the real
            // panel out in the environment, which is nowhere near this camera in the hierarchy.
            if (board == null) return null;
            return board.FindByName(string.Format(blockNameFormat, portNumber));
        }
    }
}