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
    /// WORKING ORDER. Position 1 first, then 2, and so on. Only the active pin accepts a conductor,
    /// and by default it has to be struck before the next opens - seat, punch, seat, punch.
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
        [Tooltip("ON: the active pin must be struck before the next one accepts a conductor - seat, " +
                 "punch, seat, punch.\n\nOFF: the player may seat 1-8 in order first and strike " +
                 "them afterwards.")]
        public bool requirePunchBeforeNextWire = true;

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

        [Tooltip("When a drop finds no pin, log how close it actually got - in world units AND in " +
                 "pixels - so you can see instantly whether the numbers or the aim are at fault. " +
                 "Worth leaving on until the stage plays, then off.")]
        public bool logRefusedDrops = true;

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
        /// </summary>
        public bool AcceptsInput { get { return acceptsInput && !InputSuspended; } }

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

        PunchStageConductor claimedBy;
        Canvas toolCanvas;
        float hintClearAt;

        int pressFrame = -1;
        PunchStageConductor pressWinner;

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
            ValidateSetup();
        }

        void Update()
        {
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
                if (c == null || c.IsLocked || c.IsSeated || !c.isActiveAndEnabled) continue;
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
                SetHint("Bring the conductor down onto a pin.");
                RefreshUI();
                return false;
            }

            if (target.IsSeated)
            {
                SetHint("Position " + target.pinNumber + " already has a conductor in it.");
                RefreshUI();
                return false;
            }

            PunchStagePin allowed = PinAcceptingWire();
            if (allowed == null)
            {
                SetHint(requirePunchBeforeNextWire
                    ? "Punch the conductor already seated before starting the next one."
                    : "Every pin already has a conductor.");
                RefreshUI();
                return false;
            }

            if (target != allowed)
            {
                SetHint("Work the block in order - position " + allowed.pinNumber + " is next.");
                RefreshUI();
                return false;
            }

            target.Seat_(conductor);
            conductor.SeatInto(target);
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
                SetHint("Bring the tool down onto the pin you want to strike.");
                RefreshUI();
                return;
            }

            if (!target.IsSeated)
            {
                SetHint("Position " + target.pinNumber + " is empty - seat a conductor in it first.");
                RefreshUI();
                return;
            }

            if (target.IsPunched)
            {
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
            OnConductorPunched?.Invoke();

            if (PunchedCount() >= 8) { Finish(); return; }

            PunchStagePin next = ActivePin();
            SetHint(next != null ? "Position " + next.pinNumber + " next." : null);
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
                hintText.text = passed ? "" : reason;
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
            SetHint(first != null
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
        public void HighlightForConductor(PunchStageConductor conductor)
        {
            SetOnlyHighlight(PinAcceptingWire());
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
            SetOnlyHighlight(ActivePin());
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

            if (sb.Length == 0)
            {
                Debug.LogWarning("[PunchStageBoard] Setup looks complete - camera, 8 pins, 8 " +
                                 "conductors, tool and standard all present.", this);
                return;
            }
            Debug.LogWarning("[PunchStageBoard] Setup is incomplete:\n" + sb, this);
        }
    }
}