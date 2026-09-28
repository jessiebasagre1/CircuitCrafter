// PunchStageBoard.cs
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// The rules of the punch-down stage: what may be seated where, what may be struck, and the
    /// judgement at the end.
    ///
    /// WHAT THE STAGE IS. A small 3D set built somewhere off on its own in the scene - eight pins,
    /// eight conductors, and a camera pointed at them. It is deliberately NOWHERE NEAR the real
    /// patch panel model. Only the stage camera ever sees it, so it can be built at whatever size
    /// reads clearly, and moving or rebuilding the trainer FBX cannot break it.
    ///
    /// The pins and conductors are ordinary 3D GameObjects with meshes. The punch-down tool is the
    /// one thing that is NOT - it is a UI icon on a canvas, dragged over the top of the 3D view.
    /// That mix is deliberate: dragging a wire onto a pin wants to read as a physical act in the
    /// scene, while the tool reads better as something you pick up off the workbench UI.
    ///
    /// NO COLLIDERS ANYWHERE. Conductors are picked with Renderer.bounds and dropped by nearest-pin
    /// distance; the tool icon resolves through a ray from the stage camera against the same bounds.
    /// Nothing here depends on physics, layers, or Physics.autoSyncTransforms.
    ///
    /// WORKING ORDER. Any order by default: a conductor can go into any empty position, and the
    /// termination is judged against the standard only once all eight are struck - which pin each
    /// colour ended up in is what counts, not the order they went in. Tick Work In Pin Order for the
    /// old rule (position 1 first, then 2, and so on). Require Punch Before Next Wire still applies
    /// either way: seat one, punch it, then the next.
    ///
    /// Setup:
    ///   1. Put this on the root of the stage set.
    ///   2. Assign Stage Camera and Target Standard.
    ///   3. Assign Tool - the PunchToolIcon on your UI canvas.
    ///   4. Leave Pins and Conductors empty. They are found from the children.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class PunchStageBoard : MonoBehaviour
    {
        [Header("The view")]
        [Tooltip("The camera that looks at this stage. Assign it explicitly - Camera.main finds " +
                 "whichever camera happens to be tagged MainCamera, which is never this one.")]
        public Camera stageCamera;

        [Header("Judging")]
        [Tooltip("T568A or T568B - the standard this installation uses. The termination is judged " +
                 "against this and nothing else.")]
        public WireStandardSO targetStandard;

        [Header("Rules")]
        [Tooltip("OFF (default): a conductor can be seated in ANY empty position, in any order - the " +
                 "way a technician lays pairs into a real block. The result is still judged against " +
                 "the standard, pin by pin, once all eight are struck.\n\n" +
                 "ON: positions must be filled 1 to 8 in order - the original rule.")]
        public bool workInPinOrder = false;

        [Tooltip("ON: the active pin must be struck before the next one accepts a conductor - seat, " +
                 "punch, seat, punch.\n\nOFF: the player may seat 1-8 in order first and strike " +
                 "them afterwards.")]
        public bool requirePunchBeforeNextWire = true;

        [Tooltip("ON: a conductor that has been seated but NOT yet struck can be picked up again and " +
                 "moved. Striking it is what makes it permanent.\n\n" +
                 "This is how the real job works - a conductor laid into a channel is just resting " +
                 "there until the punch-down tool seats and trims it, and a learner who realises " +
                 "they have laid the wrong colour in should be able to lift it back out. Nothing is " +
                 "judged until all eight are struck, so allowing this costs no rigour.\n\n" +
                 "Lifting a conductor out and dropping it somewhere that is not a valid position " +
                 "sends it back to the cable, which is the way to undo a placement. A press that " +
                 "does not actually move it puts it straight back in its channel, so a stray click " +
                 "cannot disturb work already done.\n\n" +
                 "OFF: a seated conductor is fixed where it landed, struck or not.")]
        public bool allowRepositioningBeforePunch = true;

        [Tooltip("How close (world units) a released conductor must be to a pin's seat anchor to " +
                 "land in it. Set this against the size you actually built the stage at - on a set " +
                 "where the pins are 50 mm apart, 0.04 is about right.")]
        public float snapDistance = 0.04f;

        [Tooltip("Fall back to a ray from the stage camera when the release lands outside Snap " +
                 "Distance of every pin. Much friendlier, and it means a conductor dropped visually " +
                 "on top of a pin lands there even if it is nearer another one in depth.")]
        public bool alsoAcceptRayHit = true;

        [Tooltip("How close, IN SCREEN PIXELS, the conductor has to look to a pin to land in it.\n\n" +
                 "This is the forgiving test and usually the one that fires. A conductor is dragged " +
                 "on a plane through where it started - the tray - so if the tray sits at a different " +
                 "distance from the camera than the pins do, a conductor sitting right on top of a " +
                 "pin on screen can still be a long way from it in world space, and a distance-only " +
                 "test refuses every drop. Measuring on screen is what the player is actually judging " +
                 "by.\n\n" +
                 "90 suits a 1080p view. Set 0 to switch it off and use world distance alone.")]
        public float screenSnapPixels = 90f;

        [Tooltip("Treat Screen Snap Pixels as authored for Reference Screen Height and scale it to " +
                 "whatever the device actually has.\n\n" +
                 "LEAVE THIS ON FOR TOUCH. A raw pixel count means completely different things on " +
                 "different screens: 90 px is about 8 mm on a 1080p monitor but under 2 mm on a " +
                 "1440p phone - far tighter than a fingertip, so the stage that felt forgiving in " +
                 "the editor refuses almost every drop on device. Scaling by screen height keeps the " +
                 "tolerance the same PROPORTION of the screen, which is what the player's aim " +
                 "actually scales with.")]
        public bool scaleSnapToScreen = true;

        [Tooltip("The screen height Screen Snap Pixels was tuned at. 1080 unless you tuned it " +
                 "somewhere else.")]
        public float referenceScreenHeight = 1080f;

        [Tooltip("How far the pointer must actually TRAVEL, in pixels, before a release counts as a " +
                 "drag rather than a click.\n\n" +
                 "A conductor that was picked up and let go without moving was never dragged " +
                 "anywhere, so it must not be able to terminate itself - and it can, because a wire " +
                 "resting in the tray may already be within Snap Distance of a pin. Below this, the " +
                 "conductor simply goes home.\n\n" +
                 "Scaled for the real screen alongside Screen Snap Pixels. 0 switches the check off.")]
        public float minDragPixels = 14f;

        [Tooltip("When a drop finds no pin, log how close it actually got - in world units AND in " +
                 "pixels - so you can see instantly whether the numbers or the aim are at fault. " +
                 "Worth leaving on until the stage plays, then off.")]
        public bool logRefusedDrops = true;

        [Tooltip("How close the dragged wire has to get, ON SCREEN, before its target pin lights up.\n\n" +
                 "Leave at 0 to use Effective Screen Snap - the same forgiveness the drop itself uses - " +
                 "so the highlight means \"let go now and it will seat\" rather than \"this is the pin " +
                 "you want eventually\". Raise it above the snap if you want the pin to light just " +
                 "BEFORE it will actually take the wire.")]
        public float highlightPixels = 0f;

        [Tooltip("Light the NEXT pin as soon as the stage is idle - on entry, and again after every " +
                 "seat and every punch - instead of only while a wire is being dragged near it.\n\n" +
                 "OFF is the default and is what makes the stage a test of aim. ON turns it into a " +
                 "guided walkthrough: the answer is on screen before the learner has picked anything " +
                 "up. Worth switching on for a first lesson and off for anything that counts.")]
        public bool showNextPinWhenIdle = false;

        [Header("Retrying a failed port")]
        [Tooltip("ON: a failed termination clears itself. THIS PORT's eight conductors come back out " +
                 "of the block, unlock, and return to the cable; the pins empty; the tool goes back " +
                 "on its hook - and the player carries straight on from position 1 without leaving " +
                 "the station.\n\n" +
                 "Ports already terminated are NOT touched. A failure on port 5 costs port 5 and " +
                 "nothing else, which is what happens on a real panel: you re-punch the port you got " +
                 "wrong, not the whole panel.\n\n" +
                 "OFF: the board sits on the failed result and waits for something else - the " +
                 "station, a Try Again button - to call ResetStage().")]
        public bool autoResetOnFail = true;

        [Tooltip("Seconds to hold the failed result before clearing it, so the player sees WHICH " +
                 "positions were wrong rather than the block emptying the instant they finish.\n\n" +
                 "Runs on unscaled time, so it still counts down while the fail dialog has the clock " +
                 "stopped - by the time that dialog is dismissed the port is already reset and ready.")]
        public float autoResetDelay = 0.8f;

        [Tooltip("Log each retry, with the attempt number for this port. Useful while balancing; " +
                 "noise once it plays.")]
        public bool logRetries = true;

        [Header("Where to look")]
        [Tooltip("The hierarchies the pins, conductors and panel blocks live under.\n\n" +
                 "Leave EMPTY and everything is searched for under this object, which is right when " +
                 "the stage is a self-contained set.\n\n" +
                 "Working on the REAL panel in the environment instead? Fill this with the roots the " +
                 "pieces actually sit under - typically PatchPanel_8Port and UTPCables. They are " +
                 "separate hierarchies in the scene, so one root cannot reach both.")]
        public Transform[] searchRoots;

        [Header("Pieces (found automatically - leave empty)")]
        public PunchStagePin[] pins;
        public PunchStageConductor[] conductors;

        [Header("The tool")]
        [Tooltip("The PunchToolIcon on your UI canvas. It lives outside this stage's hierarchy, so " +
                 "unlike the pins and conductors it has to be assigned. Left empty, the first one in " +
                 "the scene is used.")]
        public PunchToolIcon tool;
        [Tooltip("An empty RectTransform stretched over the tool's canvas and ordered LAST, so the " +
                 "tool draws above the rest of the UI while being dragged. Optional.")]
        public RectTransform dragLayer;

        [Header("Readouts (optional)")]
        [Tooltip("'3 of 8 terminated', then the verdict.")]
        public TMP_Text progressText;
        [Tooltip("Short nudges - why a drop was refused, what to do next.")]
        public TMP_Text hintText;
        [Tooltip("Seconds a hint stays up. 0 to leave it until the next one.")]
        public float hintSeconds = 2.5f;

        public Color passColor = new Color(0.15f, 0.8f, 0.3f);
        public Color failColor = new Color(0.9f, 0.25f, 0.2f);
        public Color progressColor = new Color(0.85f, 0.85f, 0.88f);

        [Header("Events")]
        [Tooltip("Deliberately non-generic. An open generic UnityEvent<bool> only serialises from " +
                 "Unity 2020.1 and comes back null when it fails, taking the whole flow down " +
                 "silently - the trap noted in COC2StageDialogs.")]
        public UnityEvent OnConductorPunched;
        public UnityEvent OnStagePassed;
        public UnityEvent OnStageFailed;

        /// <summary>Raised once all eight are struck and judged.</summary>
        public System.Action<bool, string> OnFinished;

        /// <summary>
        /// False once the verdict is in, so a stray drag can't change anything - and false while
        /// something else has suspended input, which the camera does while it is flying between
        /// ports. A drag started mid-flight would be computed from a camera that is still moving,
        /// so the conductor would slide across the screen on its own.
        ///
        /// AND false whenever the stage camera is not rendering, which is the important one.
        ///
        /// A disabled Camera still answers ScreenPointToRay perfectly happily - it is only its
        /// RENDERING that stops. So without this check the punch stage keeps scoring every click
        /// in the game against its own camera while the player is somewhere else entirely: at the
        /// crimping bench laying wires into the guide, each drop also lands on a conductor here,
        /// and the Console fills with conductors being lifted out of pins nobody is looking at.
        ///
        /// The station assigns this camera on entry and switches its GameObject off on exit, so
        /// "is that camera live" is the same question as "is the player standing at this stage".
        /// </summary>
        public bool AcceptsInput
        {
            get
            {
                if (!acceptsInput || InputSuspended) return false;
                return stageCamera != null && stageCamera.isActiveAndEnabled;
            }
        }

        /// <summary>Set by PunchStageCamera during a move. Nothing else should touch it.</summary>
        public bool InputSuspended { get; set; }

        bool acceptsInput;
        public bool HasFinished { get; private set; }
        public bool LastResultPassed { get; private set; }

        /// <summary>True when this board clears a failed port itself. The station reads it so the
        /// two never both reset the same failure.</summary>
        public bool AutoResetsOnFail { get { return autoResetOnFail; } }

        /// <summary>How many times the CURRENT port has been attempted. Reset to 0 whenever the
        /// sequence moves to a new port.</summary>
        public int PortAttempts { get; private set; }

        Coroutine autoResetRoutine;

        /// <summary>The camera conductors are dragged in.</summary>
        public Camera StageCamera { get { return stageCamera; } }

        /// <summary>
        /// Screen Snap Pixels, corrected for the screen this is actually running on.
        ///
        /// Every screen-distance test goes through here rather than reading the field directly, so
        /// the editor and a phone agree on how forgiving the stage is.
        /// </summary>
        public float EffectiveScreenSnap
        {
            get
            {
                if (!scaleSnapToScreen || referenceScreenHeight <= 0f) return screenSnapPixels;
                return screenSnapPixels * (Screen.height / referenceScreenHeight);
            }
        }

        /// <summary>Min Drag Pixels, corrected for the screen this is actually running on - the same
        /// correction Screen Snap Pixels gets, for the same reason.</summary>
        public float EffectiveMinDrag
        {
            get
            {
                if (!scaleSnapToScreen || referenceScreenHeight <= 0f) return minDragPixels;
                return minDragPixels * (Screen.height / referenceScreenHeight);
            }
        }

        PunchStageConductor claimedBy;
        Canvas toolCanvas;
        float hintClearAt;

        int pressFrame = -1;
        PunchStageConductor pressWinner;

        // False from the moment the stage opens until the pointer has been seen released at least
        // once. See ConductorForPress for why.
        bool pressArmed;

        /// <summary>True while the board is deliberately deaf to a press that was already held down
        /// when the stage opened.</summary>
        public bool IgnoringHeldPress { get { return !pressArmed; } }

        /// <summary>
        /// Throw away whatever press is in progress: no conductor may be picked up until the pointer
        /// has been released and pressed again.
        ///
        /// Called when the stage opens - by OnEnable, by ResetStage, and by PunchDownStation as it
        /// hands over the camera. Three call sites rather than one because the board may or may not
        /// live under the stage root, and only the station knows for certain when the player
        /// arrived.
        /// </summary>
        public void IgnorePressUntilReleased()
        {
            pressArmed = false;
            claimedBy = null;
            pressFrame = -1;
            pressWinner = null;
        }

        void OnEnable()
        {
            IgnorePressUntilReleased();
        }

        /// <summary>Null for a Screen Space - Overlay canvas, the canvas camera otherwise. Used by
        /// the tool icon; getting it wrong is what makes a UI drag land in the wrong place.</summary>
        public Camera UICamera
        {
            get
            {
                if (toolCanvas == null && tool != null) toolCanvas = tool.GetComponentInParent<Canvas>();
                if (toolCanvas == null) return null;
                return toolCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : toolCanvas.worldCamera;
            }
        }

        /// <summary>
        /// The hierarchies to search. Falls back to this object, which is what a self-contained
        /// stage set wants; filled in, it lets the board reach pieces that live on the real panel
        /// and the real cables - two separate roots in the scene that no single parent covers.
        /// </summary>
        public Transform[] Roots
        {
            get
            {
                if (searchRoots != null)
                {
                    var live = new System.Collections.Generic.List<Transform>();
                    foreach (var t in searchRoots) { if (t != null) live.Add(t); }
                    if (live.Count > 0) return live.ToArray();
                }
                return new Transform[] { transform };
            }
        }

        /// <summary>
        /// Hands the board a different set of eight conductors to work with.
        ///
        /// This is what makes a multi-port run keep its finished work. Each port is terminated by
        /// its OWN cable, so at the end of a port the board simply stops referring to that cable's
        /// conductors and picks up the next one's. The ones already punched are no longer anything's
        /// business - they stay locked, seated and visible in the port they were terminated into,
        /// which is exactly what a real panel looks like halfway through a job.
        /// </summary>
        public void SetConductors(PunchStageConductor[] set)
        {
            conductors = set;
            if (conductors == null) return;
            foreach (var c in conductors)
            {
                if (c != null) c.Bind(this);
            }
        }

        /// <summary>First object with this exact name under any search root. Used by the sequence
        /// and the camera to locate each port's block, so all three agree on where to look.</summary>
        public Transform FindByName(string wanted)
        {
            if (string.IsNullOrEmpty(wanted)) return null;
            foreach (var root in Roots)
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == wanted) return t;
                }
            }
            return null;
        }

        T[] CollectAll<T>() where T : Component
        {
            var found = new System.Collections.Generic.List<T>();
            foreach (var root in Roots)
            {
                foreach (var c in root.GetComponentsInChildren<T>(true))
                {
                    if (c != null && !found.Contains(c)) found.Add(c);
                }
            }
            return found.ToArray();
        }

        void Awake()
        {
            if (pins == null || pins.Length == 0)
                pins = CollectAll<PunchStagePin>();
            if (conductors == null || conductors.Length == 0)
                conductors = CollectAll<PunchStageConductor>();
            // Include inactive: the tool lives on the stage canvas, which the station switches OFF
            // at Awake. A search that excludes inactive objects finds nothing at all here.
            if (tool == null)
                tool = Object.FindFirstObjectByType<PunchToolIcon>(FindObjectsInactive.Include);

            foreach (var c in conductors) { if (c != null) c.Bind(this); }
            if (tool != null) tool.Bind(this);

            ResetStage();
        }

        void Start()
        {
            StartCoroutine(ValidateWhenSettled());
        }

        /// <summary>
        /// Waits a couple of frames before checking the setup.
        ///
        /// A multi-port run has one set of eight conductors PER PORT, and PunchStageSequence hands
        /// the board whichever set the current port uses. Until it has, this board's own Awake
        /// search has found every conductor on every cable - and a check that runs then reports
        /// sixteen conductors and two of every colour, which is not a fault at all. Two frames is
        /// long enough for the sequence to have spoken.
        /// </summary>
        System.Collections.IEnumerator ValidateWhenSettled()
        {
            yield return null;
            yield return null;
            ValidateSetup();
        }

        void Update()
        {
            // Arm on the first frame the pointer is NOT held. Entering with the keyboard arms the
            // board immediately; entering with a click or a tap arms it the moment that click ends,
            // so the press that opened the stage can never also grab a wire.
            if (!pressArmed && !PointerInput.Held) pressArmed = true;

            if (hintSeconds > 0f && hintClearAt > 0f && Time.unscaledTime >= hintClearAt)
            {
                hintClearAt = 0f;
                if (hintText != null) hintText.text = "";
            }
        }

        // ------------------------------------------------------------------
        // One drag at a time
        // ------------------------------------------------------------------

        /// <summary>
        /// Which conductor a press at this screen point should pick up - the one whose WIRE the
        /// touch landed nearest to, not whichever component Unity happened to update first.
        ///
        /// This exists because renderer bounds are axis-aligned boxes. Eight wires fanning out from
        /// one cable have boxes that overlap almost completely, so several conductors will report
        /// "the pointer is over me" for the same press, and letting each one claim for itself makes
        /// the winner depend on component order - which is arbitrary, and is why touching the blue
        /// wire could pick up the one beside it.
        ///
        /// Computed once per frame and cached: all eight conductors ask this in the same frame, and
        /// without the cache that is sixty-four bounds tests for one press.
        /// </summary>
        public PunchStageConductor ConductorForPress(Vector2 screenPoint)
        {
            if (pressFrame == Time.frameCount) return pressWinner;
            pressFrame = Time.frameCount;
            pressWinner = null;

            if (conductors == null || stageCamera == null) return null;

            // The press that OPENED the stage is not a press on a conductor.
            //
            // Entering is a click - the mobile Interact button, or a click on the bench. The stage
            // root switches on during that same frame, and Unity gives no order between the
            // station's Update and a conductor's, so a conductor can read PointerInput.Down from
            // the very click that brought the player here. The player then releases, the drop
            // lands wherever that wire was already sitting, and if the tray is within Snap
            // Distance of the block it seats itself - a conductor terminated without anyone
            // having dragged anything.
            if (IgnoringHeldPress) return null;

            // Once per press, not once per conductor. A tap on the tool icon can also line up with a
            // conductor behind it, since none of this knows about uGUI; the parameterless call only
            // checks the mouse, so touch needs the active finger's id.
            if (EventSystem.current != null)
            {
                bool overUI = Input.touchCount > 0
                    ? EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId)
                    : EventSystem.current.IsPointerOverGameObject();
                if (overUI) return null;
            }

            float best = float.MaxValue;
            foreach (var c in conductors)
            {
                if (c == null || c.IsLocked || !c.isActiveAndEnabled) continue;

                // A seated conductor is still loose in its channel until the tool goes through it,
                // so it stays pickable - that is what Allow Repositioning Before Punch means. Once
                // struck, IsLocked above has already ruled it out.
                if (c.IsSeated && !allowRepositioningBeforePunch) continue;

                if (!c.IsPointerOverBounds(screenPoint)) continue;

                float score = c.GrabScore(screenPoint);
                if (score < best) { best = score; pressWinner = c; }
            }
            return pressWinner;
        }

        /// <summary>Two conductors whose pick bounds overlap would otherwise both be picked up by a
        /// single press. First one to ask wins.</summary>
        public bool TryClaimDrag(PunchStageConductor who)
        {
            if (claimedBy != null && claimedBy != who) return false;
            claimedBy = who;
            return true;
        }

        public void ReleaseDrag(PunchStageConductor who)
        {
            if (claimedBy == who) claimedBy = null;
        }

        /// <summary>
        /// Lifts an unstruck conductor back out of its channel, so it is loose in the player's hand
        /// again and the position it was in is free.
        ///
        /// Called by the conductor as a drag begins. Emptying the pin here rather than at the drop
        /// is what lets the wire be put back into the SAME position: leave the pin occupied and
        /// PinAcceptingWire would refuse it as "position 3 already has a conductor in it" - by its
        /// own former occupant.
        /// </summary>
        public void ReleaseFromPin(PunchStageConductor conductor)
        {
            if (conductor == null) return;

            PunchStagePin pin = conductor.SeatedIn;
            if (pin != null) pin.ClearSeat();      // refuses by itself if the pin was struck

            conductor.LeavePin();
            COC2Sfx.ConductorLifted();
            SetHint(null);
            RefreshUI();

            if (pin != null)
                Debug.Log("[PunchStageBoard] " + WireColorPalette.ToDisplayName(conductor.color) +
                          " lifted back out of position " + pin.pinNumber +
                          " - it had not been punched yet.", this);
        }

        /// <summary>
        /// Puts a conductor back into the position it was just lifted from - the undo for a press
        /// that turned out not to be a drag at all.
        /// </summary>
        public void ReturnToPin(PunchStageConductor conductor, PunchStagePin pin)
        {
            if (conductor == null || pin == null) return;
            if (pin.IsPunched || pin.IsSeated) return;

            pin.Seat_(conductor);
            conductor.SeatInto(pin);

            // The undo for a press that was not a drag. It still gets the seat sound - from the
            // learner's side a conductor just went into a channel, and silence here would read
            // as the press having done nothing.
            COC2Sfx.ConductorSeated();

            RefreshUI();
        }

        // ------------------------------------------------------------------
        // Where things stand
        // ------------------------------------------------------------------

        public PunchStagePin Pin(int pinNumber)
        {
            if (pins == null) return null;
            foreach (var p in pins)
            {
                if (p != null && p.pinNumber == pinNumber) return p;
            }
            return null;
        }

        /// <summary>The pin the player should be working on - the lowest not yet struck.</summary>
        public PunchStagePin ActivePin()
        {
            for (int n = 1; n <= 8; n++)
            {
                PunchStagePin p = Pin(n);
                if (p != null && !p.IsPunched) return p;
            }
            return null;
        }

        /// <summary>The one pin that would accept a conductor right now, or null.</summary>
        public PunchStagePin PinAcceptingWire()
        {
            if (requirePunchBeforeNextWire)
            {
                PunchStagePin active = ActivePin();
                return (active != null && !active.IsSeated) ? active : null;
            }

            for (int n = 1; n <= 8; n++)
            {
                PunchStagePin p = Pin(n);
                if (p != null && !p.IsSeated) return p;
            }
            return null;
        }

        /// <summary>
        /// True if a conductor dropped on this pin right now would seat. The single rule the drop and
        /// the drag highlight both use, so the highlight never promises a seat the drop then refuses.
        /// </summary>
        public bool PinAccepts(PunchStagePin p)
        {
            if (p == null || p.IsSeated || p.IsPunched) return false;
            if (workInPinOrder) return p == PinAcceptingWire();
            return !requirePunchBeforeNextWire || SeatedUnpunched() == null;
        }

        /// <summary>A position with a conductor laid in but not struck yet, or null.</summary>
        public PunchStagePin SeatedUnpunched()
        {
            if (pins == null) return null;
            foreach (var p in pins)
                if (p != null && p.IsSeated && !p.IsPunched) return p;
            return null;
        }

        public bool CanPunch(PunchStagePin p)
        {
            return AcceptsInput && p != null && p.IsSeated && !p.IsPunched;
        }

        public bool AnyReadyToPunch
        {
            get
            {
                if (!AcceptsInput || pins == null) return false;
                foreach (var p in pins) { if (CanPunch(p)) return true; }
                return false;
            }
        }

        public int PunchedCount()
        {
            int n = 0;
            if (pins != null)
            {
                foreach (var p in pins) { if (p != null && p.IsPunched) n++; }
            }
            return n;
        }

        // ------------------------------------------------------------------
        // Hit testing
        // ------------------------------------------------------------------

        /// <summary>Nearest pin to a world position, within Snap Distance.</summary>
        public PunchStagePin PinNearest(Vector3 worldPosition)
        {
            if (pins == null) return null;
            PunchStagePin best = null;
            float bestDist = snapDistance;

            foreach (var p in pins)
            {
                if (p == null) continue;
                float d = p.DistanceTo(worldPosition);
                if (d <= bestDist) { bestDist = d; best = p; }
            }
            return best;
        }

        /// <summary>
        /// Nearest pin measured ON SCREEN, in pixels, from a world position.
        ///
        /// This is the test that makes the stage feel right. World distance alone fails whenever the
        /// conductor tray sits at a different depth from the pins, because a conductor can look like
        /// it is on a pin while being far from it in 3D. The player is judging by what they see, so
        /// this measures the same thing they do.
        /// </summary>
        public PunchStagePin PinNearestOnScreen(Vector3 worldPosition, out float pixels)
        {
            pixels = float.MaxValue;
            if (pins == null || stageCamera == null) return null;

            Vector3 from = stageCamera.WorldToScreenPoint(worldPosition);
            PunchStagePin best = null;

            foreach (var p in pins)
            {
                if (p == null) continue;
                Vector3 to = stageCamera.WorldToScreenPoint(p.Seat.position);
                if (to.z <= 0f) continue;          // behind the camera - not on screen at all
                float d = Vector2.Distance(new Vector2(from.x, from.y), new Vector2(to.x, to.y));
                if (d < pixels) { pixels = d; best = p; }
            }
            return best;
        }

        /// <summary>Frontmost pin under a screen point. This is how the UI tool icon - which only
        /// has a screen position - reaches into the 3D stage.</summary>
        public PunchStagePin PinUnderScreenPoint(Vector2 screenPoint)
        {
            if (pins == null || stageCamera == null) return null;

            Ray ray = stageCamera.ScreenPointToRay(screenPoint);
            PunchStagePin best = null;
            float bestDist = float.MaxValue;

            foreach (var p in pins)
            {
                if (p == null) continue;
                if (p.IsUnderRay(ray, out float d) && d < bestDist) { bestDist = d; best = p; }
            }
            return best;
        }

        // ------------------------------------------------------------------
        // Drops
        // ------------------------------------------------------------------

        /// <summary>Called by a conductor when the player lets go. True if it was seated - the
        /// conductor sends itself home when this is false.</summary>
        public bool HandleConductorDropped(PunchStageConductor conductor, Vector2 screenPoint)
        {
            ClearAllHighlights();
            if (!AcceptsInput || conductor == null) { RefreshUI(); return false; }

            // A click is not a drag.
            //
            // This is the guard that actually stops a conductor terminating itself. Snap Distance
            // is a world measurement, and on a stage where the tray sits close to the block a wire
            // AT REST is already inside it - so a press and release that never moved would seat a
            // conductor the player never carried anywhere. Requiring real travel means the only
            // way into a pin is to have taken the wire there.
            float minDrag = EffectiveMinDrag;
            if (minDrag > 0f)
            {
                float travelled = Vector2.Distance(conductor.DragStartScreen, screenPoint);
                if (travelled < minDrag)
                {
                    Debug.Log("[PunchStageBoard] Ignored a press on " +
                              WireColorPalette.ToDisplayName(conductor.color) +
                              " that never moved (" + travelled.ToString("F0") + " px, needs " +
                              minDrag.ToString("F0") + "). Treated as a click, not a drag - the " +
                              "conductor stays where it is.", this);

                    // Straight back into the channel it was lifted from. Returning false here would
                    // send it home to the cable instead, which would mean a single stray click on a
                    // seated conductor silently undid a placement the player had already made.
                    if (conductor.DragOriginPin != null)
                    {
                        ReturnToPin(conductor, conductor.DragOriginPin);
                        return true;
                    }

                    SetHint(null);
                    RefreshUI();
                    return false;
                }
            }

            // Three tests, cheapest and strictest first.
            //   1. World distance  - exact, and right when the tray and the pins share a depth.
            //   2. Screen distance - what the player is actually aiming by. Usually the one that fires.
            //   3. Pointer ray     - last resort; note the pointer is offset from the conductor by
            //                        however far off-centre it was grabbed, so this is the weakest.
            PunchStagePin target = PinNearest(conductor.transform.position);

            float px;
            PunchStagePin onScreen = PinNearestOnScreen(conductor.transform.position, out px);
            float snapPx = EffectiveScreenSnap;
            if (target == null && snapPx > 0f && onScreen != null && px <= snapPx)
                target = onScreen;

            if (target == null && alsoAcceptRayHit) target = PinUnderScreenPoint(screenPoint);

            if (target == null)
            {
                if (logRefusedDrops) LogRefusal(conductor, onScreen, px);
                COC2Sfx.ActionRefused();
                SetHint("Bring the conductor down onto a pin.");
                RefreshUI();
                return false;
            }

            if (target.IsSeated)
            {
                COC2Sfx.ActionRefused();
                SetHint("Position " + target.pinNumber + " already has a conductor in it.");
                RefreshUI();
                return false;
            }

            if (workInPinOrder)
            {
                PunchStagePin allowed = PinAcceptingWire();
                if (allowed == null)
                {
                    COC2Sfx.ActionRefused();
                    SetHint(requirePunchBeforeNextWire
                        ? "Punch the conductor already seated before starting the next one."
                        : "Every pin already has a conductor.");
                    RefreshUI();
                    return false;
                }

                if (target != allowed)
                {
                    COC2Sfx.ActionRefused();
                    SetHint("Work the block in order - position " + allowed.pinNumber + " is next.");
                    RefreshUI();
                    return false;
                }
            }
            else if (!PinAccepts(target))
            {
                // Any order - the only thing that can still refuse a free position is a conductor
                // elsewhere waiting to be struck (Require Punch Before Next Wire).
                PunchStagePin waiting = SeatedUnpunched();
                COC2Sfx.ActionRefused();
                SetHint(waiting != null
                    ? "Punch the conductor in position " + waiting.pinNumber + " before seating the next one."
                    : "That position can't take a conductor right now.");
                RefreshUI();
                return false;
            }

            target.Seat_(conductor);
            conductor.SeatInto(target);
            COC2Sfx.ConductorSeated();
            SetHint("Now bring the punch-down tool onto position " + target.pinNumber + ".");
            RefreshUI();
            return true;
        }

        /// <summary>
        /// Says exactly how close the drop got, both ways, so one line in the Console separates the
        /// two possible faults instead of leaving you to guess.
        ///
        ///   world distance small, screen distance small  -> raise Snap Distance a little
        ///   world distance LARGE, screen distance small  -> the tray sits at a different depth from
        ///                                                   the pins; that is what Screen Snap
        ///                                                   Pixels exists for, so raise it
        ///   both large                                   -> the drop really was nowhere near
        /// </summary>
        void LogRefusal(PunchStageConductor conductor, PunchStagePin nearestOnScreen, float pixels)
        {
            PunchStagePin nearest = null;
            float world = float.MaxValue;
            if (pins != null)
            {
                foreach (var p in pins)
                {
                    if (p == null) continue;
                    float d = p.DistanceTo(conductor.transform.position);
                    if (d < world) { world = d; nearest = p; }
                }
            }

            Debug.LogWarning(string.Format(
                "[PunchStageBoard] Drop refused - no pin found.\n" +
                "    nearest by WORLD distance : {0}   {1:F4} world units   (Snap Distance is {2})\n" +
                "    nearest ON SCREEN         : {3}   {4:F0} pixels        (Screen Snap Pixels is {5})\n" +
                "    If the world number is large but the pixel number is small, the conductor tray " +
                "sits at a different depth from the pins - raise Screen Snap Pixels, not Snap Distance.",
                nearest == null ? "none" : "pin " + nearest.pinNumber, world, snapDistance,
                nearestOnScreen == null ? "none" : "pin " + nearestOnScreen.pinNumber, pixels,
                EffectiveScreenSnap.ToString("F0") + (scaleSnapToScreen
                    ? " - " + screenSnapPixels + " scaled for a " + Screen.height + "px screen"
                    : "")), this);
        }

        /// <summary>
        /// Which pin the tool is over. Used by BOTH the hover highlight and the strike, so what
        /// lights up and what gets struck can never disagree - which would read as the tool hitting
        /// the wrong pin.
        /// </summary>
        public PunchStagePin PinForToolAt(Vector2 screenPoint)
        {
            PunchStagePin hit = PinUnderScreenPoint(screenPoint);
            if (hit != null) return hit;

            // Same forgiveness the conductors get. The tool is a flat icon over a 3D set, so screen
            // distance is the only thing it can sensibly be judged by anyway.
            float snapPx = EffectiveScreenSnap;
            if (snapPx <= 0f || stageCamera == null || pins == null) return null;

            PunchStagePin best = null;
            float bestPx = snapPx;
            foreach (var p in pins)
            {
                if (p == null) continue;
                Vector3 to = stageCamera.WorldToScreenPoint(p.Seat.position);
                if (to.z <= 0f) continue;
                float d = Vector2.Distance(screenPoint, new Vector2(to.x, to.y));
                if (d <= bestPx) { bestPx = d; best = p; }
            }
            return best;
        }

        /// <summary>Called by the UI tool icon when the player lets go.</summary>
        public void HandleToolDropped(Vector2 screenPoint)
        {
            ClearAllHighlights();
            if (!AcceptsInput) { RefreshUI(); return; }

            PunchStagePin target = PinForToolAt(screenPoint);
            if (target == null)
            {
                COC2Sfx.ActionRefused();
                SetHint("Bring the tool down onto the pin you want to strike.");
                RefreshUI();
                return;
            }

            if (!target.IsSeated)
            {
                COC2Sfx.ActionRefused();
                SetHint("Position " + target.pinNumber + " is empty - seat a conductor in it first.");
                RefreshUI();
                return;
            }

            if (target.IsPunched)
            {
                COC2Sfx.ActionRefused();
                SetHint("Position " + target.pinNumber + " is already terminated.");
                RefreshUI();
                return;
            }

            Punch(target);
        }

        void Punch(PunchStagePin pin)
        {
            // A punched conductor is seated in an IDC contact and trimmed. It does not come back out
            // by hand, right or wrong - the same finality as a crimp.
            pin.MarkPunched();
            COC2Sfx.ConductorPunched();
            OnConductorPunched?.Invoke();

            if (PunchedCount() >= 8) { Finish(); return; }

            if (workInPinOrder)
            {
                PunchStagePin next = ActivePin();
                SetHint(next != null ? "Position " + next.pinNumber + " next." : null);
            }
            else
            {
                SetHint("Seat the next conductor in any empty position.");
            }
            RefreshUI();
        }

        // ------------------------------------------------------------------
        // Verdict
        // ------------------------------------------------------------------

        void Finish()
        {
            string reason;
            bool passed = Evaluate(out reason);

            PortAttempts++;
            HasFinished = true;
            LastResultPassed = passed;
            acceptsInput = false;
            ClearAllHighlights();

            if (progressText != null)
            {
                progressText.text = passed ? "PASS - panel terminated correctly." : "FAIL";
                progressText.color = passed ? passColor : failColor;
            }
            if (hintText != null)
            {
                // GUIDANCE: the fail reason explains the fault - Story only. The review screen has it.
                hintText.text = passed || !CircuitCrafter.Guidance.GuidancePolicy.Allows(CircuitCrafter.Guidance.GuidanceKind.CorrectiveFeedback)
                    ? "" : reason;
                hintClearAt = 0f;
            }

            if (passed) OnStagePassed?.Invoke();
            else OnStageFailed?.Invoke();

            OnFinished?.Invoke(passed, reason);

            // Clear the failed port ourselves rather than waiting to be told to. Nothing else has to
            // be wired up, nothing has to close, and the player never leaves the station - which is
            // the whole point: a mis-terminated port is re-punched on the spot, exactly as it would
            // be on a real panel.
            if (!passed && autoResetOnFail)
            {
                if (autoResetRoutine != null) StopCoroutine(autoResetRoutine);
                autoResetRoutine = StartCoroutine(AutoResetAfterFail());
            }
        }

        /// <summary>
        /// Holds the failed result briefly so the player can see which positions were wrong, then
        /// puts the port back exactly as it was before they started it.
        ///
        /// UNSCALED, deliberately. The fail dialog can stop the clock, and this has to keep counting
        /// through it - so by the time the player dismisses that dialog the conductors are already
        /// back on the cable and the block is empty, ready to go again.
        /// </summary>
        System.Collections.IEnumerator AutoResetAfterFail()
        {
            float t = 0f;
            while (t < autoResetDelay)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            autoResetRoutine = null;
            ResetStage();

            PunchStagePin first = ActivePin();
            SetHint(workInPinOrder && first != null
                ? "Try again - start from position " + first.pinNumber + "."
                : "Try again.");

            if (logRetries)
                Debug.LogWarning("[PunchStageBoard] Port reset after a failed termination - all " +
                                 "eight conductors are back on the cable and the block is empty. " +
                                 "Attempt " + (PortAttempts + 1) + " on this port. Ports already " +
                                 "terminated were not touched.", this);
        }

        /// <summary>Called by PunchStageSequence when it moves to a new block, so the attempt count
        /// belongs to the port being worked rather than to the whole visit.</summary>
        public void BeginNewPort()
        {
            PortAttempts = 0;
        }

        public bool Evaluate(out string reason)
        {
            if (targetStandard == null)
            {
                reason = "No wiring standard assigned to this stage - nothing to judge against.";
                return false;
            }

            WireColor[] arrangement = Arrangement();
            if (targetStandard.Matches(arrangement))
            {
                reason = "All eight conductors follow " + targetStandard.standard + ".";
                return true;
            }
            reason = DescribeMismatches(arrangement);
            return false;
        }

        /// <summary>What is currently in positions 1-8.</summary>
        public WireColor[] Arrangement()
        {
            var result = new WireColor[8];
            for (int n = 1; n <= 8; n++)
            {
                PunchStagePin p = Pin(n);
                if (p != null && p.IsSeated) result[n - 1] = p.SeatedColor;
            }
            return result;
        }

        string DescribeMismatches(WireColor[] arrangement)
        {
            const int maxNamed = 4;
            var detail = new StringBuilder();
            int wrong = 0, named = 0;

            for (int n = 1; n <= 8; n++)
            {
                WireColor expected = targetStandard.GetExpectedColorAtPin(n);
                WireColor actual = arrangement[n - 1];
                if (actual == expected) continue;

                wrong++;
                if (named >= maxNamed) continue;
                named++;

                if (detail.Length > 0) detail.Append(" ");
                detail.Append("Position " + n + " has " + WireColorPalette.ToDisplayName(actual) +
                              ", it should be " + WireColorPalette.ToDisplayName(expected) + ".");
            }

            if (wrong == 0) return "The termination doesn't follow " + targetStandard.standard + ".";

            string message = "This panel is wired to " + targetStandard.standard + ", and " + wrong +
                             " position(s) don't follow it. " + detail;
            if (wrong > named) message += " (" + (wrong - named) + " more are also wrong.)";
            return message;
        }

        // ------------------------------------------------------------------
        // Presentation
        // ------------------------------------------------------------------

        /// <summary>Called every frame a conductor is being dragged - shows where it belongs.</summary>
        /// <summary>
        /// Lights the pin this wire would seat into - but only once the wire is
        /// actually near it.
        ///
        /// It used to light PinAcceptingWire() the instant a drag began, wherever
        /// the wire happened to be, and hold it lit until the drop. That is a
        /// different statement: "this is the pin you want eventually" rather than
        /// "let go now and it will seat". The first hands the learner the answer
        /// before they have aimed at anything, which is exactly the aiming this
        /// stage exists to teach.
        ///
        /// Measured with PinNearestOnScreen against EffectiveScreenSnap - the same
        /// test the DROP uses - so the highlight can never promise a seat that
        /// releasing would then refuse, and it scales to the device the same way.
        ///
        /// nearest == target matters as much as the distance: hovering over the
        /// WRONG pin should light nothing, not light the right pin somewhere else
        /// on the board.
        /// </summary>
        public void HighlightForConductor(PunchStageConductor conductor)
        {
            if (conductor == null) { SetOnlyHighlight(null); return; }

            float pixels;
            PunchStagePin nearest = PinNearestOnScreen(conductor.transform.position, out pixels);

            float range = highlightPixels > 0f ? highlightPixels : EffectiveScreenSnap;

            // Any order: light whichever free position the wire is over. In order: only the one
            // position that is next, and nothing while hovering any other.
            SetOnlyHighlight(PinAccepts(nearest) && pixels <= range ? nearest : null);
        }

        /// <summary>Called every frame the tool is being dragged - shows what it would strike.</summary>
        public void HighlightPunchableUnder(Vector2 screenPoint)
        {
            PunchStagePin under = PinForToolAt(screenPoint);
            SetOnlyHighlight(CanPunch(under) ? under : null);
        }

        void SetOnlyHighlight(PunchStagePin only)
        {
            if (pins == null) return;
            foreach (var p in pins) { if (p != null) p.SetHighlighted(p == only); }
        }

        void ClearAllHighlights() { SetOnlyHighlight(null); }

        public void SetHint(string message)
        {
            if (hintText == null) return;

            // GUIDANCE: these lines coach the punch-down - Story Mode only. Empty in an assessment.
            if (!CircuitCrafter.Guidance.GuidancePolicy.Allows(CircuitCrafter.Guidance.GuidanceKind.Hints)) message = null;
            hintText.text = message ?? "";
            hintClearAt = (hintSeconds > 0f && !string.IsNullOrEmpty(message))
                ? Time.unscaledTime + hintSeconds
                : 0f;
        }

        void RefreshUI()
        {
            if (!HasFinished && progressText != null)
            {
                progressText.text = PunchedCount() + " of 8 terminated";
                progressText.color = progressColor;
            }
            // The idle highlight is a teaching aid, not part of the interaction, so
            // it is off by default. It used to be unconditional - which is why the
            // first pin lit up the moment the station opened, before the learner
            // had touched a wire, and stayed lit through every drag.
            // In any order there is no "next" position - only, at most, one waiting to be struck.
            SetOnlyHighlight(!showNextPinWhenIdle ? null
                             : workInPinOrder ? ActivePin()
                             : SeatedUnpunched());
        }

        // ------------------------------------------------------------------

        /// <summary>Empties every pin, sends all eight conductors back to where they started, and
        /// re-opens input. Wire to a Try Again button, or let the station call it after a failure.</summary>
        [ContextMenu("Reset stage")]
        public void ResetStage()
        {
            // Cancel a pending auto-reset. Without this, a port cleared early - by the station on
            // entry, or by a Try Again button - would be cleared a second time a moment later, in
            // the middle of the player's next drag.
            if (autoResetRoutine != null) { StopCoroutine(autoResetRoutine); autoResetRoutine = null; }

            // A reset is a fresh start, and the click on the Try Again button must not carry
            // through into a grab on the conductor sitting behind it.
            IgnorePressUntilReleased();

            HasFinished = false;
            LastResultPassed = false;
            acceptsInput = true;
            claimedBy = null;

            if (pins != null)
            {
                foreach (var p in pins) { if (p != null) p.ResetPin(); }
            }
            if (conductors != null)
            {
                foreach (var c in conductors) { if (c != null) c.ResetConductor(); }
            }
            if (tool != null) tool.ReturnHome();

            SetHint(null);
            RefreshUI();
        }

        /// <summary>
        /// Measures the real gap between neighbouring pins and checks the two distance settings
        /// against it.
        ///
        /// This is the check worth having. Snap Distance and Pick Radius are absolute world units,
        /// and a panel model reused at its real size has channels about 2.2 mm apart, while a set
        /// built from scratch might have them 50 mm apart - a factor of more than twenty. Get it
        /// wrong in one direction and every drop is refused; wrong in the other and conductors land
        /// in the pin next door. Neither produces an error, and both look like broken code.
        /// </summary>
        void CheckScale(StringBuilder sb)
        {
            if (pins == null || pins.Length < 2) return;

            float minGap = float.MaxValue;
            for (int a = 0; a < pins.Length; a++)
            {
                if (pins[a] == null) continue;
                for (int b = a + 1; b < pins.Length; b++)
                {
                    if (pins[b] == null) continue;
                    float d = Vector3.Distance(pins[a].Seat.position, pins[b].Seat.position);
                    if (d > 0.00001f && d < minGap) minGap = d;
                }
            }
            if (minGap == float.MaxValue) return;

            string gap = minGap.ToString("F4");

            if (snapDistance >= minGap)
                sb.AppendLine("  - Snap Distance (" + snapDistance + ") is at least as big as the gap " +
                              "between neighbouring pins (" + gap + "). A conductor will land in the " +
                              "wrong pin. Try " + (minGap * 0.6f).ToString("F4") + ".");
            else if (snapDistance < minGap * 0.15f)
                sb.AppendLine("  - Snap Distance (" + snapDistance + ") is tiny next to the gap " +
                              "between pins (" + gap + "), so almost every drop will be refused. " +
                              "About " + (minGap * 0.6f).ToString("F4") + " would suit this set.");

            foreach (var p in pins)
            {
                if (p == null || !p.UsesRadius) continue;
                if (p.pickRadius >= minGap * 0.5f)
                {
                    sb.AppendLine("  - Pin " + p.pinNumber + " has no renderer and its Pick Radius (" +
                                  p.pickRadius + ") reaches past halfway to the next pin (" + gap +
                                  " apart), so the tool will strike whichever it hits first. Try " +
                                  (minGap * 0.35f).ToString("F4") + ".");
                    break;   // one is enough; they are almost always all set the same
                }
            }
        }

        /// <summary>Says out loud what is missing, rather than the stage quietly doing nothing.</summary>
        [ContextMenu("Check my setup")]
        public void ValidateSetup()
        {
            var sb = new StringBuilder();

            if (stageCamera == null)
                sb.AppendLine("  - Stage Camera is empty. Nothing can be picked up: every drag test " +
                              "starts with a ray from this camera.");

            int pinCount = pins == null ? 0 : pins.Length;
            if (pinCount != 8)
                sb.AppendLine("  - Found " + pinCount + " PunchStagePin components under the search " +
                              "root(s); there should be 8. If they live on the real panel, fill " +
                              "Search Roots.");

            bool[] seen = new bool[9];
            if (pins != null)
            {
                foreach (var p in pins)
                {
                    if (p == null || p.pinNumber < 1 || p.pinNumber > 8) continue;
                    if (seen[p.pinNumber])
                        sb.AppendLine("  - Two pins both claim position " + p.pinNumber +
                                      ". One of them can never be reached.");
                    seen[p.pinNumber] = true;
                }
                for (int n = 1; n <= 8; n++)
                {
                    if (!seen[n]) sb.AppendLine("  - No pin is set to position " + n + ".");
                }
            }

            int wireCount = conductors == null ? 0 : conductors.Length;
            if (wireCount != 8)
                sb.AppendLine("  - Found " + wireCount + " PunchStageConductor components under the " +
                              "search root(s); there should be 8, one per colour. If they live on " +
                              "the real cables, add UTPCables to Search Roots.");
            if (conductors != null)
            {
                var used = new System.Collections.Generic.HashSet<WireColor>();
                foreach (var c in conductors)
                {
                    if (c == null) continue;
                    if (!used.Add(c.color))
                        sb.AppendLine("  - Two conductors are both set to " +
                                      WireColorPalette.ToDisplayName(c.color) +
                                      ", so one colour is missing from the stage.");
                }
            }

            if (tool == null)
                sb.AppendLine("  - No PunchToolIcon found. Nothing can be struck.");
            if (targetStandard == null)
                sb.AppendLine("  - No Target Standard, so every termination will be judged a FAIL.");

            CheckScale(sb);
            CheckConductorsAtRest(sb);

            if (sb.Length == 0)
            {
                Debug.LogWarning("[PunchStageBoard] Setup looks complete - camera, 8 pins, 8 " +
                                 "conductors, tool and standard all present.", this);
                return;
            }
            Debug.LogWarning("[PunchStageBoard] Setup is incomplete:\n" + sb, this);
        }

        /// <summary>
        /// Names any conductor that is ALREADY within Snap Distance of a pin while sitting in the
        /// tray, untouched.
        ///
        /// This is the scene fault behind "a wire was terminated and nobody dragged it". A drop is
        /// judged on where the conductor ended up, so a wire whose resting place is already inside
        /// a pin's catchment needs no drag at all - any press-and-release on it, including a stray
        /// one, seats it. Min Drag Pixels now refuses that release, but the wire is still sitting
        /// somewhere it visually reads as plugged in, and the honest fix is to move the tray or
        /// lower Snap Distance.
        ///
        /// Reported per colour, because it is almost never all eight - it is the one or two wires
        /// in the middle of the fan whose tips reach furthest towards the block.
        /// </summary>
        void CheckConductorsAtRest(StringBuilder sb)
        {
            if (conductors == null || pins == null) return;

            foreach (var c in conductors)
            {
                if (c == null || c.IsSeated || c.IsLocked) continue;

                PunchStagePin nearest = null;
                float best = float.MaxValue;

                foreach (var p in pins)
                {
                    if (p == null) continue;
                    float d = p.DistanceTo(c.transform.position);
                    if (d < best) { best = d; nearest = p; }
                }

                if (nearest == null || best > snapDistance) continue;

                sb.AppendLine("  - " + WireColorPalette.ToDisplayName(c.color) + " is resting " +
                              best.ToString("F4") + " from position " + nearest.pinNumber +
                              ", which is inside Snap Distance (" + snapDistance + "). It looks " +
                              "plugged in before anyone has touched it, and any press on it would " +
                              "terminate it. Move that conductor's tray position further from the " +
                              "block, or lower Snap Distance.");
            }
        }
    }
}