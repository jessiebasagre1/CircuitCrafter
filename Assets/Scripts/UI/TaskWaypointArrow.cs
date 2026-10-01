// TaskWaypointArrow.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using TMPro;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// The "where do I go next" arrow.
    ///
    /// You give it an ORDERED list of steps. Each step names a place in the world and a condition
    /// that means "done". Every frame it walks the list from the top, finds the first step that is
    /// NOT done, and points at that one. Nothing else in the project has to know this exists - the
    /// arrow reads the state that is already there.
    ///
    /// WHY IT READS STATE INSTEAD OF BEING TOLD
    ///
    /// The obvious design is a queue that other scripts push to: the crimping station calls
    /// NextWaypoint(), the tester calls NextWaypoint() again, and so on. That breaks the first time
    /// the player does something out of order, reloads, or a step is skipped for testing - the queue
    /// and the world drift apart and the arrow starts pointing at a step the player finished ten
    /// minutes ago. Reading the same booleans the rest of the sim already keeps means the arrow
    /// cannot disagree with reality. Untick a gate, press F11, restart a procedure: the arrow just
    /// recomputes and is right again.
    ///
    /// EXECUTION ORDER 300, deliberately - after every board, station and carrier has had its Start.
    /// The arrow only ever reads, so it wants to run last and see settled values.
    ///
    /// SETUP - the short version:
    ///   1. Empty GameObject under Managers, call it "TaskWaypoint". Add this component.
    ///   2. Drop your arrow model in the scene as a separate object, drag it into World Arrow.
    ///      Any mesh will do. It gets moved and switched on/off for you - do not parent it here.
    ///   3. Fill the Steps list: a Label, a Target transform, and how the step is judged done.
    ///   4. Play. If a step is set up wrongly the Console says which one and why.
    ///
    /// Everything else on this component is optional polish.
    /// </summary>
    [DefaultExecutionOrder(300)]
    [DisallowMultipleComponent]
    public class TaskWaypointArrow : MonoBehaviour
    {
        /// <summary>How a step decides it is finished. Each one reads something that already exists.</summary>
        public enum DoneWhen
        {
            /// <summary>TaskManager says this task id is complete. Registered ids in COC2 are
            /// 'crimp_cable', 'test_cable' and 'punch_patch_panel'.</summary>
            TaskComplete,
            /// <summary>A PatchCordBoard phase has finished and passed.</summary>
            PhasePassed,
            /// <summary>A CableCarrier has both ends plugged in.</summary>
            CableRun,
            /// <summary>A CableCarrier has appeared on the bench - use this to point AT the cable
            /// only once it exists, on the step before running it.</summary>
            CableAvailable,
            /// <summary>A GameObject is active. Good for "the crimped cable is on the table".</summary>
            ObjectActive,
            /// <summary>A GameObject is inactive. The mirror of the above - good for "the tool is
            /// gone from the rack because the player picked it up".</summary>
            ObjectInactive,
            /// <summary>The finished cable is lying on the crimping table, waiting to be picked up.
            /// Use it for the CRIMP step: done the moment the cable exists.</summary>
            CableOnTable,
            /// <summary>The player has the crimped cable in their pocket. Note this is cleared again
            /// when the cable goes into the tester, so a step using it wants Latch When Done ticked
            /// unless you actually want the arrow to come back.</summary>
            CableInPocket,
            /// <summary>The player simply walked close enough. A pure signposting step.</summary>
            PlayerArrived,
            /// <summary>Nothing detects this - you call CompleteStep from a UnityEvent. The escape
            /// hatch for anything the others cannot see.</summary>
            Manual,

            // ---- ADDED LATER - AND THEREFORE ADDED AT THE END --------------
            //
            // Unity serialises an enum by its NUMBER, not its name. Slotting a new value into the
            // middle of this list renumbers every value below it, and every Step already saved in
            // every scene silently changes meaning - a step set to Object Active would reopen as
            // something else entirely, with nothing to show it had moved.
            //
            // New conditions go here, at the bottom, however out of place they read.

            /// <summary>The player has TAKEN a CableCarrier off the bench. Use it for the
            /// "go and pick up the cable" step: the moment it is in hand the arrow moves on to
            /// where the first end goes.
            ///
            /// It stays true for the rest of the run, so it needs no latch - unlike IsHeld, which
            /// blinks off every time an end seats.</summary>
            CablePickedUp,

            /// <summary>A CableCarrier has its FIRST end plugged in, whichever socket that was.
            /// Use it for the "plug it into the patch panel" step, so the arrow then sends the
            /// player on to the far end of the run.</summary>
            CableFirstEndIn,

            /// <summary>COC1. The USB flash drive is off the bench - either in the player's hand or
            /// already plugged into a port. Use it for the "go and get the flash drive" step.
            ///
            /// Deliberately counts "plugged in" as taken as well. CarriedItemHolder clears HeldDrive
            /// the instant PlayerInteractor plugs the drive in, so a hand-only test would blink off
            /// at the exact moment the next step begins - the same flicker CableInPocket has, and the
            /// reason that one needs a latch. Reading "not on the bench any more" instead means this
            /// step goes done once and stays done, with nothing to tick.</summary>
            DriveTaken,

            /// <summary>COC1. The USB flash drive is plugged into a port. Use it for the "plug it
            /// into the PC" step.
            ///
            /// ASSIGN PORT, NOT JUST DRIVE. USBDriveItem.IsInsertedInPort is true for ANY port, so a
            /// drive still sitting in the reference PC would mark the target PC's step done and send
            /// the player to the BIOS with an empty USB socket. With Port filled, the step asks that
            /// one port whether it has a drive in it.</summary>
            DriveInPort,

            /// <summary>COC1. The assigned Port is empty again. Use it for the SECOND pickup - "go
            /// back and take the flash drive out of the first PC" - with Port set to the Rufus PC's
            /// port and Target pointing at that PC.
            ///
            /// Phrased as "that port is empty" rather than "the drive is in hand" on purpose. The
            /// drive leaves the player's hand again the moment it goes into the second PC, so a
            /// hand test would go back to false there and throw the arrow across the room to the
            /// step the player just finished. An emptied port stays empty.
            ///
            /// This reads as already-done before the drive is ever plugged in, which is harmless:
            /// the steps above it are still waiting, so the arrow never reaches it early.</summary>
            DriveRemovedFromPort,

            /// <summary>COC1. OSInstallManager has reached the Complete stage. The Windows install
            /// is the one part of COC1 that never reports to TaskManager - TaskGate reads the stage
            /// directly, and so does this - so Task Complete cannot end the run.</summary>
            OSInstallComplete,

            /// <summary>COC2. The assigned DeviceConfigStation has been configured correctly - its IP
            /// settings were applied and accepted. Use one step per machine: "Configure the server"
            /// with Config Station = the server's station, then the same for the client.
            ///
            /// WHY NOT TASK COMPLETE. The server and the client share one Task Id
            /// ('configure_network', two steps), so the task only completes when BOTH are done - it
            /// cannot tell the arrow which machine is next. Each station's own IsConfigured can, and
            /// it is restored with the save, so a Continue resumes on the right machine.</summary>
            MachineConfigured,

            /// <summary>COC2. The assigned ConnectivityCheckStation's ping has passed. Use it for the
            /// "test the link" step, one per machine that has to ping. Same reason as above:
            /// 'verify_connectivity' is shared by both machines.</summary>
            PingPassed
        }

        [System.Serializable]
        public class Step
        {
            [Tooltip("Shown to the player, e.g. 'Crimp the cable'. Keep it an instruction, not a " +
                     "place name - the arrow already shows the place.")]
            public string label = "Next step";

            [Tooltip("Where the arrow points. Usually the station's own transform, but an empty " +
                     "child placed at eye height reads better than a pivot on the floor.")]
            public Transform target;

            [Tooltip("How this step decides it is finished.")]
            public DoneWhen doneWhen = DoneWhen.TaskComplete;

            [Header("The one field your Done When needs")]
            [Tooltip("Done When = Task Complete. The id as registered with TaskManager.")]
            public string taskId = "";
            [Tooltip("Done When = Phase Passed.")]
            public PatchCordBoard phase;
            [Tooltip("Done When = Cable Run, Cable Available, Cable Picked Up or Cable First End In.\n\n" +
                     "One cable usually wants THREE steps in a row, all pointing at the same " +
                     "CableCarrier but at different places:\n" +
                     "  1. Cable Picked Up   -> the spawn point\n" +
                     "  2. Cable First End In -> the patch panel\n" +
                     "  3. Cable Run          -> the server (or client)")]
            public CableCarrier cable;
            [Tooltip("Done When = Object Active or Object Inactive.")]
            public GameObject witness;
            [Tooltip("Done When = Cable On Table.")]
            public CrimpedCablePickup pickup;
            [Tooltip("Done When = Drive Taken or Drive In Port. The USBDriveItem in the scene.\n\n" +
                     "Optional for Drive In Port when Port is filled - fill both only if that port " +
                     "must hold THAT drive and no other.")]
            public USBDriveItem drive;
            [Tooltip("Done When = Drive In Port or Drive Removed From Port. The USBPort on the PC " +
                     "the player is being sent to. Fill this whenever the scene has more than one " +
                     "port - see the Drive In Port tooltip.")]
            public USBPort port;
            [Tooltip("Done When = Machine Configured. The DeviceConfigStation on the PC this step " +
                     "sends the player to - the server's for \"Configure the server\", the client's for " +
                     "\"Configure the client\".")]
            public DeviceConfigStation configStation;
            [Tooltip("Done When = Ping Passed. The ConnectivityCheckStation whose ping has to pass - " +
                     "the one on the PC this step sends the player to.")]
            public ConnectivityCheckStation pingStation;
            [Tooltip("Done When = Player Arrived. Metres. 0 falls back to Arrival Distance.")]
            public float arriveWithin = 0f;

            [Header("Optional")]
            [Tooltip("Tick for a step that happens inside a station view, where a world arrow " +
                     "floating in the room is just clutter. The label still updates.")]
            public bool hideArrowDuringStep = false;

            [Tooltip("Once this step goes done, keep it done - even if the thing it watches goes " +
                     "back to how it was.\n\n" +
                     "This is what stops the arrow walking backwards. 'Crimp the cable' is judged " +
                     "by the cable being on the table; the moment the player picks that cable up " +
                     "it is not on the table any more, and without a latch the arrow would send " +
                     "them back to the crimping station while they are carrying it to the tester. " +
                     "Latches are cleared by Reset Steps, so the second cable still rewinds them.")]
            public bool latchWhenDone = false;

            [Tooltip("Raised once, the first time this step becomes the active one.")]
            public UnityEvent OnStepBegan;

            // Runtime only.
            [System.NonSerialized] public bool manualDone;
            [System.NonSerialized] public bool latched;
            [System.NonSerialized] public bool announced;
        }

        [Header("Steps, in order")]
        [Tooltip("Walked from the top every frame. The first one that is not done is the one the " +
                 "arrow points at, so the ORDER here is the order the player is sent around.")]
        public List<Step> steps = new List<Step>();

        [Header("World Arrow")]
        [Tooltip("Your arrow model, sitting loose in the scene. Moved to hover over the current " +
                 "target and switched off when there is nothing to point at. Leave empty if you " +
                 "only want the screen arrow or the label.")]
        public Transform worldArrow;
        [Tooltip("Height above the target the arrow floats at.")]
        public float hoverHeight = 0.6f;
        [Tooltip("How far it bobs up and down. 0 = perfectly still.")]
        public float bobAmount = 0.08f;
        [Tooltip("Bobs per second.")]
        public float bobSpeed = 1.5f;
        [Tooltip("Spin around the WORLD vertical axis, degrees per second. 0 = no spin.\n\n" +
                 "The spin is applied on top of however you rotated the arrow in the editor, so a " +
                 "cone tilted to point down keeps pointing down while it turns.")]
        public float spinSpeed = 45f;
        [Tooltip("Turn the arrow to face the camera as well as spinning. Tick this if your model " +
                 "is a flat sprite-like arrow; untick it for a 3D cone that reads from any angle.\n\n" +
                 "Like the spin, this is a yaw applied on top of your authored tilt - it will not " +
                 "stand a downward-pointing arrow back up.")]
        public bool billboardToCamera = false;
        [Tooltip("Extra rotation applied to the arrow, on top of how it sits in the editor.\n\n" +
                 "Leave this at 0,0,0 and the arrow keeps exactly the orientation you gave it in " +
                 "the Scene view. Use it when the model itself is built pointing the wrong way and " +
                 "you would rather not re-rotate it: X 180 flips a cone from up to down, X 90 tips " +
                 "a forward-pointing arrow to face the floor.")]
        public Vector3 rotationOffset = Vector3.zero;

        [Header("Screen Arrow (optional)")]
        [Tooltip("A UI arrow on your canvas. It swings around the middle of the screen pointing " +
                 "the way the player should turn, and hides itself once the target is on screen " +
                 "and the world arrow has taken over. Its image must point UP at 0 rotation.")]
        public RectTransform screenArrow;
        [Tooltip("Distance from screen centre the screen arrow orbits at, as a fraction of screen " +
                 "height. 0.25 puts it a quarter of the way out - clear of a crosshair, well " +
                 "inside the edges on any aspect ratio.")]
        public float screenArrowRadius = 0.25f;

        [Header("Label (optional)")]
        [Tooltip("Shows the current step's label.")]
        public TMP_Text labelText;
        [Tooltip("Shows how far away it is. Can be the same object as Label Text - then you get " +
                 "one line with the distance appended.")]
        public TMP_Text distanceText;
        [Tooltip("Format for the distance. {0} is metres, already rounded.")]
        public string distanceFormat = "{0} m";

        [Header("Behaviour")]
        [Tooltip("Within this many metres the player has clearly arrived, so the arrow gets out of " +
                 "the way. The label stays up.")]
        public float arrivalDistance = 2.5f;
        [Tooltip("Hide everything once every step is done, rather than leaving the last label up.")]
        public bool hideWhenAllDone = true;
        [Tooltip("The player. Left empty, the object tagged 'Player' is found at Start.")]
        public Transform player;

        [Header("Assessment")]
        [Tooltip("Keep this arrow on during an assessment as well.\n\n" +
                 "OFF (default): the arrow and its label are switched off for the whole graded run - " +
                 "an arrow pointing at the next station is teaching scaffolding.\n\n" +
                 "ON: the arrow works in the assessment exactly as in Story Mode. Tick it only in the " +
                 "scene whose assessment should keep it (COC2, say); every other scene stays off.")]
        public bool allowInAssessment = false;

        [Tooltip("With Allow In Assessment ticked: hide the step label and keep only the arrow, so an " +
                 "assessment shows WHERE the next station is but not WHAT to do there.")]
        public bool hideLabelInAssessment = false;

        /// <summary>True while this run is an assessment and this arrow's label is switched off for it -
        /// either because the whole arrow is off, or because only the arrow is being kept. Other scripts
        /// that hide and show the label (COC2StationArrowHider) leave it alone while this is true.</summary>
        public bool LabelLockedForAssessment =>
            AssessmentMode.IsActive && (!allowInAssessment || hideLabelInAssessment);

        [Header("Diagnostics")]
        [Tooltip("Console line every time the active step changes. Leave this on while you are " +
                 "building the list - it is one line per step, not per frame.")]
        public bool logStepChanges = true;

        [Header("Events")]
        [Tooltip("Raised when the last step is finished.")]
        public UnityEvent OnAllStepsDone;

        /// <summary>
        /// The arrow in this scene, so a station can hide it without holding a reference.
        ///
        /// COC2's stations suppress the arrow through UnityEvents wired in the Inspector, which is
        /// fine when there are two of them. COC1 has a dozen StationCameraSwitch objects and they
        /// all do their hiding in code, so wiring each one by hand would be twelve chances to miss
        /// one - and a missed one leaves an arrow hovering over a bench behind the player while they
        /// are looking at a motherboard in close-up.
        ///
        /// First one wins, the way TaskManager and DialogManager do it here.
        /// </summary>
        public static TaskWaypointArrow Instance { get; private set; }

        Camera cam;
        int activeIndex = -1;

        /// <summary>True once LateUpdate has executed. Tells "-1 because everything is finished"
        /// apart from "-1 because this component has never run", which look identical otherwise.</summary>
        bool hasRun;
        bool suppressed;
        bool lockedOffForAssessment;
        bool allDoneFired;
        float bobPhase;
        float spinAngle;

        // The arrow's orientation as you left it in the Scene view, read once in Awake - which runs
        // before anything in the scene has had a chance to move it. Every frame's rotation is built
        // from this, so however you tilted the model is the pose that survives.
        Quaternion authoredRotation = Quaternion.identity;

        /// <summary>Index of the step being pointed at, or -1 when there is nothing to point at.</summary>
        public int ActiveIndex { get { return activeIndex; } }

        /// <summary>The step being pointed at, or null.</summary>
        public Step ActiveStep
        {
            get { return activeIndex >= 0 && activeIndex < steps.Count ? steps[activeIndex] : null; }
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[TaskWaypointArrow] A second arrow on '" + name + "' - the one " +
                                 "that woke first keeps the Instance. Stations will hide that one, " +
                                 "not this. Two arrows in a scene is almost always a leftover.", this);
            }
            else
            {
                Instance = this;
            }

            // Awake, not Start. Nothing else has run yet, so this is the last moment the editor
            // pose is guaranteed untouched.
            if (worldArrow != null) authoredRotation = worldArrow.rotation;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            if (player == null)
            {
                GameObject tagged = GameObject.FindGameObjectWithTag("Player");
                if (tagged != null) player = tagged.transform;
            }

            // No guidance during a graded run - unless this scene's assessment keeps the arrow (Allow
            // In Assessment). Done before ValidateSetup so an assessment does not also print a page of
            // setup warnings about steps nobody is going to see.
            if (AssessmentMode.IsActive && !allowInAssessment)
            {
                LockOffForAssessment();
                Debug.Log("[TaskWaypointArrow] Assessment mode - the arrow and its label stay off " +
                          "for this run.", this);
                return;
            }

            if (AssessmentMode.IsActive)
            {
                if (hideLabelInAssessment)
                {
                    if (labelText != null) labelText.gameObject.SetActive(false);
                    if (distanceText != null && distanceText != labelText)
                        distanceText.gameObject.SetActive(false);
                }

                Debug.Log("[TaskWaypointArrow] Assessment mode - Allow In Assessment is ticked, so the " +
                          "arrow stays on" + (hideLabelInAssessment ? " without its label." : "."), this);
            }

            ValidateSetup();
        }

        void LateUpdate()
        {
            hasRun = true;

            // LateUpdate, not Update. Stations move cameras and the player in Update; pointing at
            // where things were a frame ago is exactly the kind of one-frame swim that reads as the
            // arrow lagging behind the player's head.
            //
            // Unscaled time throughout - dialogs set timeScale to 0 and the arrow should keep
            // bobbing rather than freezing mid-air.
            float dt = Time.unscaledDeltaTime;
            bobPhase += dt * bobSpeed;
            spinAngle += dt * spinSpeed;

            int next = FirstUnfinishedStep();
            if (next != activeIndex) ChangeStep(next);

            Step step = ActiveStep;

            if (step == null)
            {
                ShowWorldArrow(false);
                ShowScreenArrow(false);
                if (hideWhenAllDone)
                {
                    SetText(labelText, "");
                    if (distanceText != labelText) SetText(distanceText, "");
                }
                return;
            }

            // The LABEL is updated before the suppression check, the arrows after it.
            //
            // Suppression exists to stop a marker hovering over a bench somewhere behind the
            // player's head while they are looking at a motherboard in close-up. It was never meant
            // to freeze the instruction text, but that is what it did: the early return skipped this
            // line, so the label kept whatever it said when the station view opened.
            //
            // That matters most at exactly the moment it is least obvious. The learner unplugs the
            // flash drive INSIDE the station view - the step advances correctly, the arrow is
            // correctly hidden, and the label still reads "Take the flash drive back out", the thing
            // they just did. Every visible sign says the waypoint is stuck. It is not; it is
            // suppressed, and pointing somewhere they cannot see from in here.
            //
            // Hide Arrow During Step already draws this distinction for a single step - arrow off,
            // label live. Suppress now behaves the same way.
            SetText(labelText, step.label);

            if (suppressed)
            {
                ShowWorldArrow(false);
                ShowScreenArrow(false);
                return;
            }

            if (step.target == null) { ShowWorldArrow(false); ShowScreenArrow(false); return; }

            float distance = player != null
                ? Vector3.Distance(player.position, step.target.position)
                : 0f;

            if (distanceText != null)
            {
                string d = string.Format(distanceFormat, Mathf.RoundToInt(distance));
                SetText(distanceText, distanceText == labelText ? step.label + "   " + d : d);
            }

            bool arrived = player != null && distance <= arrivalDistance;
            bool wanted = !step.hideArrowDuringStep && !arrived;

            PlaceWorldArrow(step.target, wanted);
            PlaceScreenArrow(step.target, wanted);
        }

        // ---------------------------------------------------------------- step selection

        /// <summary>
        /// First step in the list that is not finished, or -1 if they all are.
        ///
        /// Walked top to bottom EVERY frame rather than remembering a cursor. It costs a handful of
        /// boolean reads and it means a step finishing out of order - or a procedure restart putting
        /// an earlier one back - is picked up on the very next frame with no bookkeeping.
        /// </summary>
        int FirstUnfinishedStep()
        {
            for (int i = 0; i < steps.Count; i++)
            {
                Step s = steps[i];
                if (s == null) continue;
                if (!IsStepDone(s)) return i;
            }
            return -1;
        }

        /// <summary>
        /// Whether a step counts as finished, latch included.
        ///
        /// The latch is checked FIRST and set here rather than anywhere else, so there is exactly one
        /// place in the file where "done" is decided. Anything that asks - the arrow, the report,
        /// the validator - gets the same answer.
        /// </summary>
        bool IsStepDone(Step s)
        {
            if (s.latched) return true;

            bool done = EvaluateCondition(s);
            if (done && s.latchWhenDone) s.latched = true;
            return done;
        }

        bool EvaluateCondition(Step s)
        {
            switch (s.doneWhen)
            {
                case DoneWhen.TaskComplete:
                    if (string.IsNullOrEmpty(s.taskId)) return false;
                    // No TaskManager means nothing can ever report in, so treating the step as
                    // unfinished would pin the arrow there forever. Better to fall through.
                    if (TaskManager.Instance == null) return true;
                    return TaskManager.Instance.IsTaskComplete(s.taskId);

                case DoneWhen.PhasePassed:
                    return s.phase != null && s.phase.HasFinished && s.phase.LastResultPassed;

                case DoneWhen.CableRun:
                    return s.cable != null && s.cable.IsComplete;

                case DoneWhen.CableAvailable:
                    return s.cable != null && s.cable.IsAvailable;

                case DoneWhen.CablePickedUp:
                    return s.cable != null && s.cable.HasBeenPickedUp;

                case DoneWhen.CableFirstEndIn:
                    return s.cable != null && s.cable.FirstEndPlugged;

                case DoneWhen.ObjectActive:
                    return s.witness != null && s.witness.activeInHierarchy;

                case DoneWhen.ObjectInactive:
                    return s.witness != null && !s.witness.activeInHierarchy;

                case DoneWhen.CableOnTable:
                    return s.pickup != null && s.pickup.IsAvailable;

                case DoneWhen.CableInPocket:
                    return CableInventory.Instance != null && CableInventory.Instance.HasCable;

                case DoneWhen.PlayerArrived:
                    if (s.target == null || player == null) return false;
                    float within = s.arriveWithin > 0f ? s.arriveWithin : arrivalDistance;
                    return Vector3.Distance(player.position, s.target.position) <= within;

                case DoneWhen.DriveTaken:
                    {
                        if (s.drive == null) return false;
                        if (s.drive.IsInsertedInPort) return true;
                        return CarriedItemHolder.Instance != null &&
                               CarriedItemHolder.Instance.HeldDrive == s.drive;
                    }

                case DoneWhen.DriveInPort:
                    {
                        // Port first: it is the specific question. Drive alone answers the vague one.
                        if (s.port != null)
                            return s.port.CurrentDrive != null &&
                                   (s.drive == null || s.port.CurrentDrive == s.drive);

                        return s.drive != null && s.drive.IsInsertedInPort;
                    }

                case DoneWhen.DriveRemovedFromPort:
                    {
                        if (s.port == null) return false;
                        if (s.port.CurrentDrive == null) return true;
                        return s.drive != null && s.port.CurrentDrive != s.drive;
                    }

                case DoneWhen.OSInstallComplete:
                    return OSInstallManager.Instance != null &&
                           OSInstallManager.Instance.CurrentStage == OSInstallManager.InstallStage.Complete;

                case DoneWhen.MachineConfigured:
                    return s.configStation != null && s.configStation.IsConfigured;

                case DoneWhen.PingPassed:
                    return s.pingStation != null && s.pingStation.IsConnected;

                case DoneWhen.Manual:
                    return s.manualDone;
            }
            return false;
        }

        void ChangeStep(int next)
        {
            activeIndex = next;

            Step s = ActiveStep;
            if (s != null)
            {
                if (!s.announced)
                {
                    s.announced = true;
                    s.OnStepBegan?.Invoke();
                }
                if (logStepChanges)
                    Debug.LogWarning("[TaskWaypointArrow] Now pointing at step " + activeIndex +
                                     ", '" + s.label + "'" +
                                     (s.target != null ? " -> " + s.target.name : " (NO TARGET SET)") +
                                     ".", this);
            }
            else
            {
                if (logStepChanges)
                    Debug.LogWarning("[TaskWaypointArrow] Every step is done. The arrow is off.", this);
                if (!allDoneFired) { allDoneFired = true; OnAllStepsDone?.Invoke(); }
            }
        }

        // ---------------------------------------------------------------- the two arrows

        void PlaceWorldArrow(Transform target, bool wanted)
        {
            if (worldArrow == null) return;
            if (!wanted) { ShowWorldArrow(false); return; }

            ShowWorldArrow(true);

            float bob = bobAmount * Mathf.Sin(bobPhase * Mathf.PI * 2f);
            worldArrow.position = target.position + Vector3.up * (hoverHeight + bob);

            // Yaw is computed on its own and MULTIPLIED ONTO the pose the arrow was authored with,
            // never assigned over it.
            //
            // The first version assigned Quaternion.Euler(0, spin, 0) straight to .rotation, which
            // is a complete orientation, not a nudge - so every frame it threw away the X 180 that
            // made the cone point down and stood the arrow back upright. Anything that spins or
            // billboards a marker has to keep the tilt the artist gave it, so: build a yaw, apply it
            // to the authored rotation, done. With Spin Speed 0 and Billboard off, yaw is identity
            // and the arrow is left exactly as it sits in the Scene view.
            Quaternion yaw = Quaternion.identity;

            if (billboardToCamera)
            {
                Camera c = ActiveCamera();
                if (c != null)
                {
                    // Yaw only. A full LookAt tips the arrow over whenever the player looks up or
                    // down, which makes a floating marker look like it is falling.
                    Vector3 flat = worldArrow.position - c.transform.position;
                    flat.y = 0f;
                    if (flat.sqrMagnitude > 0.0001f)
                        yaw = Quaternion.LookRotation(flat.normalized);
                }
            }
            else if (spinSpeed != 0f)
            {
                yaw = Quaternion.Euler(0f, spinAngle, 0f);
            }

            worldArrow.rotation = yaw * authoredRotation * Quaternion.Euler(rotationOffset);
        }

        void PlaceScreenArrow(Transform target, bool wanted)
        {
            if (screenArrow == null) return;

            Camera c = ActiveCamera();
            if (!wanted || c == null) { ShowScreenArrow(false); return; }

            Vector3 vp = c.WorldToViewportPoint(target.position);

            // z <= 0 means the target is BEHIND the camera, where the viewport maths mirrors the
            // point and the arrow would confidently send the player the wrong way. Flip it back.
            bool behind = vp.z <= 0f;
            if (behind) { vp.x = 1f - vp.x; vp.y = 1f - vp.y; }

            bool onScreen = !behind && vp.x > 0.05f && vp.x < 0.95f && vp.y > 0.05f && vp.y < 0.95f;
            if (onScreen) { ShowScreenArrow(false); return; }   // world arrow has it covered

            ShowScreenArrow(true);

            Vector2 fromCentre = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
            if (fromCentre.sqrMagnitude < 0.000001f) fromCentre = new Vector2(0f, 1f);
            fromCentre = fromCentre.normalized;

            float radius = Screen.height * screenArrowRadius;
            screenArrow.anchoredPosition = new Vector2(fromCentre.x * radius, fromCentre.y * radius);

            // The image is authored pointing up, so 0 degrees is +Y and the angle is measured from
            // there, clockwise-negative - which is what Unity's Z rotation wants.
            float angle = Mathf.Atan2(fromCentre.y, fromCentre.x) * Mathf.Rad2Deg - 90f;
            screenArrow.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        void ShowWorldArrow(bool on)
        {
            if (worldArrow != null && worldArrow.gameObject.activeSelf != on)
                worldArrow.gameObject.SetActive(on);
        }

        void ShowScreenArrow(bool on)
        {
            if (screenArrow != null && screenArrow.gameObject.activeSelf != on)
                screenArrow.gameObject.SetActive(on);
        }

        /// <summary>
        /// The camera to measure against. Re-fetched whenever the cached one has gone away or been
        /// switched off, because the stations swap cameras and a stale reference points the screen
        /// arrow using a camera nobody is looking through.
        /// </summary>
        Camera ActiveCamera()
        {
            if (cam == null || !cam.isActiveAndEnabled) cam = Camera.main;
            return cam;
        }

        void SetText(TMP_Text t, string s)
        {
            if (t != null && t.text != s) t.text = s;
        }

        // ---------------------------------------------------------------- public API

        /// <summary>Whether the step at this index counts as done right now, latch included - the
        /// same answer the arrow itself uses, for any script that wants to follow this arrow's steps
        /// instead of keeping a second copy of their conditions. Read-only; changes nothing.</summary>
        public bool IsStepComplete(int index)
        {
            if (steps == null || index < 0 || index >= steps.Count || steps[index] == null) return false;
            return IsStepDone(steps[index]);
        }

        /// <summary>
        /// Marks a Manual step done, by index. Hook this to a UnityEvent when none of the automatic
        /// conditions can see what you need.
        /// </summary>
        public void CompleteStep(int index)
        {
            if (index < 0 || index >= steps.Count) return;
            steps[index].manualDone = true;
        }

        /// <summary>Marks the step currently being pointed at as done. For a UnityEvent with no
        /// argument - a station's On Stage Closed, say.</summary>
        public void CompleteCurrentStep()
        {
            if (activeIndex >= 0) CompleteStep(activeIndex);
        }

        /// <summary>
        /// Hides the arrow without forgetting where it was.
        ///
        /// Hook a station's On Stage Opened here and On Stage Closed to Resume(). Inside a station
        /// the player is looking through a fixed camera at a patch panel; an arrow hovering over a
        /// bench somewhere behind them is noise.
        /// </summary>
        public void Suppress() { SetSuppressed(true); }

        /// <summary>The other half of Suppress.</summary>
        public void Resume() { SetSuppressed(false); }

        public void SetSuppressed(bool on)
        {
            // An assessment run has the arrow off for the whole run, and nothing gets to turn it
            // back on. Stations call Resume() when their close-up closes - PunchDownStation's On
            // Stage Closed is wired to it - so without this latch the arrow would reappear the
            // moment the learner stepped out of the first station, which is precisely the guidance
            // an assessment is supposed to withhold.
            if (lockedOffForAssessment && !on) return;

            if (suppressed == on) return;
            suppressed = on;
            if (on) { ShowWorldArrow(false); ShowScreenArrow(false); }
        }

        /// <summary>
        /// Switches the arrow off for a graded run and keeps it off.
        ///
        /// WHY THE ARROW MUST GO
        /// The waypoint is teaching scaffolding: it tells the learner where the next step happens.
        /// Under assessment the whole point is to find out whether they know where to go, so an
        /// arrow pointing at the patch panel hands them the answer to the question being asked.
        /// TESDA competency evidence gathered with the arrow up is not evidence of competence.
        ///
        /// Called from Start when AssessmentMode is active. Also public so an assessment manager
        /// can call it directly.
        /// </summary>
        public void LockOffForAssessment()
        {
            lockedOffForAssessment = false;   // let the SetSuppressed call through
            SetSuppressed(true);
            lockedOffForAssessment = true;

            // labelText and distanceText are allowed to be the SAME object - that is how you get
            // one line with the distance appended - so the second call would be switching off
            // something already off. Harmless, but the null checks are what matter: both are
            // optional fields and a scene may have neither.
            if (labelText != null) labelText.gameObject.SetActive(false);
            if (distanceText != null && distanceText != labelText)
                distanceText.gameObject.SetActive(false);
        }

        /// <summary>
        /// Rewinds the list: every Manual step back to not-done, every latch released.
        ///
        /// Hook this to COC2ProcedureReset's On Procedure Restarted. The player builds two cables in
        /// COC2, and the second run puts the crimping table back to how it was - the waypoints have
        /// to rewind with it or the arrow starts the second cable pointing at the tester.
        /// </summary>
        public void ResetSteps()
        {
            foreach (Step s in steps)
                if (s != null) { s.manualDone = false; s.latched = false; s.announced = false; }
            allDoneFired = false;
        }

        /// <summary>Old name for ResetSteps, kept so existing UnityEvent wiring keeps working.</summary>
        public void ResetManualSteps() { ResetSteps(); }

        // ---------------------------------------------------------------- self-reporting

        /// <summary>
        /// Says what is wrong with the list, at Play, in one message. A waypoint that silently
        /// points nowhere is worse than no waypoint - the player trusts it and walks the wrong way.
        /// </summary>
        void ValidateSetup()
        {
            List<string> problems = new List<string>();

            if (steps.Count == 0)
                problems.Add("Steps is empty. Nothing will ever be pointed at.");

            if (worldArrow == null && screenArrow == null && labelText == null)
                problems.Add("World Arrow, Screen Arrow and Label Text are all empty, so this " +
                             "component has no way to show the player anything. Assign at least one.");

            if (worldArrow != null && worldArrow.IsChildOf(transform))
                problems.Add("World Arrow '" + worldArrow.name + "' is a child of this object. It " +
                             "gets moved around the room, so parenting it here drags this object's " +
                             "children with it. Move it out to the scene root.");

            if (player == null)
                problems.Add("No object tagged 'Player' was found, so distance checks and Player " +
                             "Arrived steps cannot work. Tag the player, or fill the Player field.");

            for (int i = 0; i < steps.Count; i++)
            {
                Step s = steps[i];
                string where = "Step " + i + " ('" + (s != null ? s.label : "null") + "')";

                if (s == null) { problems.Add("Step " + i + " is empty."); continue; }
                if (s.target == null) problems.Add(where + " has no Target, so it cannot be pointed at.");

                switch (s.doneWhen)
                {
                    case DoneWhen.TaskComplete:
                        if (string.IsNullOrEmpty(s.taskId))
                            problems.Add(where + " is judged by Task Complete but Task Id is blank. " +
                                         "COC2 registers 'crimp_cable', 'test_cable' and " +
                                         "'punch_patch_panel'.");
                        break;
                    case DoneWhen.PhasePassed:
                        if (s.phase == null) problems.Add(where + " is judged by Phase Passed but Phase is empty.");
                        break;
                    case DoneWhen.CableRun:
                    case DoneWhen.CableAvailable:
                    case DoneWhen.CablePickedUp:
                    case DoneWhen.CableFirstEndIn:
                        if (s.cable == null) problems.Add(where + " is judged by a cable but Cable is empty.");
                        break;
                    case DoneWhen.ObjectActive:
                    case DoneWhen.ObjectInactive:
                        if (s.witness == null) problems.Add(where + " is judged by an object but Witness is empty.");
                        break;
                    case DoneWhen.CableOnTable:
                        if (s.pickup == null) problems.Add(where + " is judged by Cable On Table but Pickup is empty.");
                        break;
                    case DoneWhen.CableInPocket:
                        if (!s.latchWhenDone)
                            problems.Add(where + " is judged by Cable In Pocket without Latch When " +
                                         "Done. The pocket is emptied again when the cable goes into " +
                                         "the tester, so the arrow will jump back here mid-run.");
                        break;
                    case DoneWhen.DriveTaken:
                        if (s.drive == null)
                            problems.Add(where + " is judged by Drive Taken but Drive is empty.");
                        break;
                    case DoneWhen.DriveInPort:
                        if (s.port == null && s.drive == null)
                            problems.Add(where + " is judged by Drive In Port but both Port and " +
                                         "Drive are empty. Fill Port.");
                        else if (s.port == null && USBPort.AllPorts.Count > 1)
                            problems.Add(where + " is judged by Drive In Port with no Port assigned, " +
                                         "and this scene has " + USBPort.AllPorts.Count + " ports. " +
                                         "The drive being in ANY of them will mark this step done - " +
                                         "assign the port the player is actually being sent to.");

                        // The reason the arrow walks backwards.
                        //
                        // Drive In Port is a RIGHT NOW question, and in a two-PC flow the answer
                        // goes back to no: the learner plugs into the Rufus PC, builds the image,
                        // then pulls the drive out - and that step, ten rows up, quietly un-finishes
                        // itself. FirstUnfinishedStep scans from the top, so it finds the reopened
                        // step before the one the learner is actually on and marches them back to
                        // the PC they just left, holding the drive, with no way to satisfy it except
                        // to plug it in again.
                        //
                        // Only worth warning about when a later step is waiting for that same port
                        // to empty - that is what makes the reopening certain rather than possible.
                        if (s.port != null && !s.latchWhenDone && RemovedLater(s.port, i))
                            problems.Add(where + " is judged by Drive In Port without Latch When " +
                                         "Done, and a later step waits for that same port to be " +
                                         "emptied. Taking the drive back out will reopen this step " +
                                         "and the arrow will walk backwards to it. Tick Latch When " +
                                         "Done.");
                        break;
                    case DoneWhen.MachineConfigured:
                        if (s.configStation == null)
                            problems.Add(where + " is judged by Machine Configured but Config Station is " +
                                         "empty. Drag in the DeviceConfigStation of the PC this step points at.");
                        break;
                    case DoneWhen.PingPassed:
                        if (s.pingStation == null)
                            problems.Add(where + " is judged by Ping Passed but Ping Station is empty. Drag " +
                                         "in the ConnectivityCheckStation of the PC this step points at.");
                        break;
                    case DoneWhen.DriveRemovedFromPort:
                        if (s.port == null)
                            problems.Add(where + " is judged by Drive Removed From Port but Port is " +
                                         "empty. Without it there is no port to be empty, so this " +
                                         "step can never finish and the arrow will stop here.");
                        break;
                }

                // A step that is already finished at Play is not an error - the player may be
                // resuming - but a step that can NEVER be finished is, and it is worth saying which.
                if (s.doneWhen == DoneWhen.Manual && !HasManualCaller())
                    problems.Add(where + " is Manual. Remember to call CompleteStep(" + i + ") from " +
                                 "a UnityEvent, or the arrow will stop here forever.");
            }

            if (problems.Count == 0)
            {
                Debug.LogWarning("[TaskWaypointArrow] " + steps.Count + " step(s), all wired. " +
                                 ArrowPoseLine() +
                                 " Right-click the component > Report waypoints to see the run order.", this);
                return;
            }

            string msg = "[TaskWaypointArrow] Setup is incomplete:";
            foreach (string p in problems) msg += "\n  - " + p;
            Debug.LogWarning(msg, this);
        }

        /// <summary>
        /// Prints the orientation the arrow was captured with, so "my arrow is not pointing down"
        /// has a number attached to it rather than being a matter of opinion.
        /// </summary>
        string ArrowPoseLine()
        {
            if (worldArrow == null) return "No World Arrow assigned.";

            Vector3 e = authoredRotation.eulerAngles;
            string line = "World Arrow '" + worldArrow.name + "' kept at its editor rotation (" +
                          e.x.ToString("F0") + ", " + e.y.ToString("F0") + ", " + e.z.ToString("F0") + ")";
            if (rotationOffset != Vector3.zero)
                line += " plus a Rotation Offset of (" + rotationOffset.x.ToString("F0") + ", " +
                        rotationOffset.y.ToString("F0") + ", " + rotationOffset.z.ToString("F0") + ")";
            return line + ". If it points the wrong way, rotate the arrow object itself in the " +
                   "Scene view - the spin no longer overwrites it.";
        }

        // There is no way to inspect UnityEvent wiring from another object at runtime, so this is a
        // deliberate stub: it exists so the Manual warning above reads as a reminder rather than an
        // accusation, and so the intent is obvious to whoever changes this next.
        bool HasManualCaller() { return false; }

        /// <summary>
        /// Prints the whole run order and what each step is waiting for. The fastest way to answer
        /// "why is the arrow stuck there".
        /// </summary>
        [ContextMenu("Report waypoints")]
        public void LogWaypoints()
        {
            string msg = "[TaskWaypointArrow] " + steps.Count + " step(s):";
            for (int i = 0; i < steps.Count; i++)
            {
                Step s = steps[i];
                if (s == null) { msg += "\n    " + i + ". (empty)"; continue; }

                bool done = IsStepDone(s);
                msg += "\n    " + (i == activeIndex ? " >" : "  ") + i + ". " + s.label +
                       "   target: " + (s.target != null ? Path(s.target) : "NONE") +
                       "   done when: " + s.doneWhen + " " + WaitingOn(s) +
                       "   now: " + (done ? "DONE" : "waiting") +
                       (s.latched ? " (latched)" : s.latchWhenDone ? " (latching)" : "");
            }
            // "activeIndex < 0" used to print "none, all done" on its own, and that was wrong in a
            // way that actively sent people the wrong direction.
            //
            // activeIndex STARTS at -1 and is only ever changed by LateUpdate. So -1 means one of
            // two opposite things: every step is finished, or LateUpdate has never run once. The
            // second is what an inactive GameObject looks like - and a ContextMenu still works on
            // an inactive object, so the report happily printed "all done" for a component that had
            // never executed a single line.
            //
            // The giveaway was there all along and was not being pointed out: a step listed as
            // "waiting" in the list above, and "all done" on this line, cannot both be true. The
            // list is evaluated fresh when you right-click; this line was a cached value from a
            // LateUpdate that never happened.
            msg += "\n    ---- active step: " +
                   (!hasRun
                       ? "NEVER RAN. LateUpdate has not executed once, so nothing above has been " +
                         "acted on. This GameObject is inactive in the Hierarchy, or the component " +
                         "itself is unticked - Awake and Start have not run either, which is why " +
                         "there is no setup line from this component in the Console. Tick it on."
                       : activeIndex < 0
                           ? "none, every step is done"
                           : activeIndex.ToString()) +
                   (suppressed ? "   (SUPPRESSED - something called Suppress and never called Resume)" : "");
            msg += DriveStateLine();
            Debug.LogWarning(msg, this);
        }

        /// <summary>
        /// What the drive and every port in the scene actually think, right now.
        ///
        /// Two USBPorts on two PCs are usually both called "USBPort", and the step list shows the
        /// same word twice whether they are the same component or not. That is the one thing you
        /// cannot see by reading the Inspector, and it is the most common reason a drive step points
        /// at the PC the player just left - so the full hierarchy path is printed, not the name.
        ///
        /// Only printed when a step actually uses a drive condition; COC2 gets nothing extra.
        /// </summary>
        string DriveStateLine()
        {
            bool relevant = false;
            foreach (Step s in steps)
            {
                if (s == null) continue;
                if (s.doneWhen == DoneWhen.DriveTaken || s.doneWhen == DoneWhen.DriveInPort ||
                    s.doneWhen == DoneWhen.DriveRemovedFromPort) { relevant = true; break; }
            }
            if (!relevant) return "";

            string msg = "\n    ---- drives and ports:";

            msg += "\n       in hand: " +
                   (CarriedItemHolder.Instance == null ? "no CarriedItemHolder in this scene"
                    : CarriedItemHolder.Instance.HeldDrive == null ? "nothing"
                    : "'" + CarriedItemHolder.Instance.HeldDrive.name + "'");

            if (USBPort.AllPorts.Count == 0)
            {
                msg += "\n       NO USBPorts are enabled in this scene. A port on a disabled " +
                       "object does not register, so every Drive In Port step will wait forever.";
                return msg;
            }

            foreach (USBPort p in USBPort.AllPorts)
            {
                if (p == null) continue;
                msg += "\n       port " + Path(p.transform) + " holds: " +
                       (p.CurrentDrive != null ? "'" + p.CurrentDrive.name + "'" : "nothing");
            }

            foreach (USBDriveItem d in USBDriveItem.AllDrives)
            {
                if (d == null) continue;
                msg += "\n       drive '" + d.name + "' IsInsertedInPort=" + d.IsInsertedInPort +
                       ", CurrentPort=" + (d.CurrentPort != null ? Path(d.CurrentPort.transform) : "none");
            }

            return msg;
        }

        /// <summary>Is some step after <paramref name="afterIndex"/> waiting for this port to be
        /// emptied? If so, the Drive In Port step above it is guaranteed to reopen.</summary>
        bool RemovedLater(USBPort port, int afterIndex)
        {
            for (int i = afterIndex + 1; i < steps.Count; i++)
            {
                Step s = steps[i];
                if (s == null) continue;
                if (s.doneWhen == DoneWhen.DriveRemovedFromPort && s.port == port) return true;
            }
            return false;
        }

        /// <summary>Hierarchy path, so two objects with the same name are told apart.</summary>
        static string Path(Transform t)
        {
            if (t == null) return "NONE";
            string p = t.name;
            Transform parent = t.parent;
            int guard = 0;
            while (parent != null && guard++ < 6) { p = parent.name + "/" + p; parent = parent.parent; }
            return p;
        }

        string WaitingOn(Step s)
        {
            switch (s.doneWhen)
            {
                case DoneWhen.TaskComplete: return "'" + s.taskId + "'";
                case DoneWhen.PhasePassed: return s.phase != null ? "'" + s.phase.phaseName + "'" : "(none)";
                case DoneWhen.CableRun:
                case DoneWhen.CableAvailable:
                case DoneWhen.CablePickedUp:
                case DoneWhen.CableFirstEndIn: return s.cable != null ? "'" + s.cable.cableName + "'" : "(none)";
                case DoneWhen.ObjectActive:
                case DoneWhen.ObjectInactive: return s.witness != null ? "'" + s.witness.name + "'" : "(none)";
                case DoneWhen.CableOnTable: return s.pickup != null ? "'" + s.pickup.name + "'" : "(none)";
                case DoneWhen.CableInPocket: return "CableInventory";
                case DoneWhen.DriveTaken: return s.drive != null ? "'" + s.drive.name + "'" : "(none)";
                case DoneWhen.DriveInPort:
                    return s.port != null ? "port " + Path(s.port.transform)
                         : s.drive != null ? "'" + s.drive.name + "' in ANY port"
                         : "(none)";
                case DoneWhen.DriveRemovedFromPort:
                    return s.port != null ? "port " + Path(s.port.transform) + " to be empty" : "(none)";
                case DoneWhen.OSInstallComplete: return "OSInstallManager stage Complete";
                case DoneWhen.MachineConfigured: return s.configStation != null ? "'" + s.configStation.stationName + "' configured" : "(none)";
                case DoneWhen.PingPassed: return s.pingStation != null ? "'" + s.pingStation.stationName + "' ping passed" : "(none)";
                case DoneWhen.PlayerArrived: return "within " + (s.arriveWithin > 0f ? s.arriveWithin : arrivalDistance) + "m";
                default: return "";
            }
        }
    }
}