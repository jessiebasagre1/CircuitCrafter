// PatchCordBoard.cs
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// PHASE 2 of the punch-down station: cross-connecting the panel to the switch.
    ///
    /// WHAT IT IS. Once all eight ports are terminated, the conductors' job is done - they dead-end
    /// in the IDC block and go no further. What links the panel to the switch is a separate patch
    /// cord, plugged into the panel's FRONT port at one end and a switch port at the other. The panel
    /// end is already in place on the model; the player drags the free end into the matching switch
    /// port. Panel 1 to switch 1, panel 2 to switch 2, and so on.
    ///
    /// WHY IT LIVES IN THE SAME STATION. This is one continuous piece of work in the rack, and the
    /// player should not be dumped back into the world between punching and patching. PunchDownStation
    /// asks this board to Begin() the moment the last port passes, and the view simply stays open.
    ///
    /// ONE SHOT, NOT EIGHT. Unlike the punch phase there is no per-port choreography here - the whole
    /// point is that the player can see the panel and the switch at the same time to run a cord
    /// between them. So the camera pulls back to a single framing on the rack front and stays there.
    ///
    /// NO COLLIDERS. Same as everything else on this stage: plugs are picked from Renderer.bounds,
    /// sockets from a radius box, and a drop is resolved by world distance, then screen distance,
    /// then a pointer ray.
    ///
    /// Setup:
    ///   1. Put this on an empty child of the station called PatchCordPhase.
    ///   2. Fill Search Roots with the roots the cords and the switch live under.
    ///   3. Assign Camera Pose - an empty framing the rack front.
    ///   4. Assign it to Patch Cords on PunchDownStation.
    ///   5. Leave Sockets and Plugs empty. They are found for you.
    /// </summary>
    [DefaultExecutionOrder(120)]
    public class PatchCordBoard : MonoBehaviour
    {
        [Header("The view")]
        [Tooltip("The camera the cords are dragged in. PunchDownStation hands this over on entry, so " +
                 "you can normally leave it empty.")]
        public Camera stageCamera;

        [Tooltip("An empty transform posed so the panel front AND the switch are both comfortably in " +
                 "frame. The camera flies here when this phase begins.\n\n" +
                 "Leave empty and the camera stays wherever the punch phase left it, which is close " +
                 "in on port 8 - far too tight to run a cord.")]
        public Transform cameraPose;

        [Tooltip("Seconds for the pull-back to Camera Pose.")]
        public float cameraMoveDuration = 1.0f;

        [Header("Where to look")]
        [Tooltip("The hierarchies the plugs and switch sockets live under - typically PatchCords and " +
                 "Switch_24Port. Leave empty to search under this object only.")]
        public Transform[] searchRoots;

        [Header("Pieces (found automatically - leave empty)")]
        public PatchCordSocket[] sockets;
        public PatchCordPlug[] plugs;

        [Header("Rules")]
        [Tooltip("How many cords this phase expects. Normally the same as the number of ports you " +
                 "punched.")]
        [Range(1, 24)] public int cordCount = 8;

        [Tooltip("ON: a cord dropped on the wrong switch port is refused with a hint naming the " +
                 "right one. Immediate, and it teaches the one-to-one mapping directly.\n\n" +
                 "OFF: any free socket accepts any cord and the whole patching is judged at the end. " +
                 "Harder, and closer to a graded assessment.")]
        public bool requireCorrectPort = true;

        [Tooltip("ON: cords must be run 1, 2, 3... in order.\n\nOFF (default): any order, as long as " +
                 "each lands in its matching port. Real patching has no required order.")]
        public bool requireInOrder = false;

        [Tooltip("Light up the socket a dragged cord belongs in. A good teaching aid; untick for an " +
                 "assessment run where the player should read the labels themselves.")]
        public bool showTargetHighlight = true;

        [Header("Snapping")]
        [Tooltip("How close (WORLD units) a released plug has to be to a socket to land in it. Set " +
                 "against the real port spacing on your switch - the check on Play will tell you if " +
                 "it is wrong.")]
        public float snapDistance = 0.02f;

        [Tooltip("How close, IN SCREEN PIXELS, the plug has to look to a socket. This is the " +
                 "forgiving test and usually the one that fires, because a plug is dragged on a plane " +
                 "through where it started - so a plug sitting right on a port on screen can still be " +
                 "some way from it in depth.\n\n" +
                 "Switch ports sit close together, so keep this SMALLER than you would for the punch " +
                 "block. 45 suits a 1080p view. 0 switches it off.")]
        public float screenSnapPixels = 45f;

        [Tooltip("Treat Screen Snap Pixels as authored for Reference Screen Height and scale it to " +
                 "whatever the device actually has.\n\n" +
                 "LEAVE THIS ON FOR TOUCH. A raw pixel count means different things on different " +
                 "screens - 45 px is about 4 mm on a 1080p monitor and under 1 mm on a 1440p phone, " +
                 "which no fingertip can hit. Scaling by screen height keeps the tolerance the same " +
                 "proportion of the screen on both.")]
        public bool scaleSnapToScreen = true;

        [Tooltip("The screen height Screen Snap Pixels was tuned at. 1080 unless you tuned it " +
                 "somewhere else.")]
        public float referenceScreenHeight = 1080f;

        [Tooltip("Last resort: a ray from the stage camera through the pointer. Weakest of the three, " +
                 "because the pointer is offset from the plug by however far off-centre it was grabbed.")]
        public bool alsoAcceptRayHit = true;

        [Tooltip("When a drop finds no socket, log how close it got - in world units AND pixels - so " +
                 "one Console line separates 'the numbers are wrong' from 'the aim was wrong'.")]
        public bool logRefusedDrops = true;

        [Header("On begin")]
        [Tooltip("Switched OFF when this phase starts. Put the punch-down tool icon here - there is " +
                 "nothing left to strike, and leaving it on screen invites a pointless drag.")]
        public GameObject[] hideOnBegin;

        [Tooltip("Switched ON when this phase starts. Anything specific to patching - a labelled " +
                 "port map, a different instruction panel.")]
        public GameObject[] showOnBegin;

        [Tooltip("Disabled when this phase starts. Put PunchStageCamera here: it is still sitting in " +
                 "its AtPort phase watching the sequence, and while it will not move on its own, " +
                 "switching it off makes certain nothing fights this board for the camera.")]
        public MonoBehaviour[] disableOnBegin;

        [Tooltip("Optional. Shown once when this phase begins - what a cross-connect is, why the " +
                 "numbers have to line up, why the cord goes through the manager.")]
        public DialogSequence introDialog;

        [Header("Readouts (optional)")]
        [Tooltip("'3 of 8 patched', then the verdict.")]
        public TMP_Text progressText;
        [Tooltip("Short nudges - why a drop was refused, what to do next.")]
        public TMP_Text hintText;
        [Tooltip("Seconds a hint stays up. 0 to leave it until the next one.")]
        public float hintSeconds = 2.5f;

        public Color passColor = new Color(0.15f, 0.8f, 0.3f);
        public Color failColor = new Color(0.9f, 0.25f, 0.2f);
        public Color progressColor = new Color(0.85f, 0.85f, 0.88f);

        [Header("Task (optional)")]
        [Tooltip("Reported to TaskManager when this phase passes. Leave empty for none. It must be " +
                 "registered by COC2TaskSetup or nothing will listen for it.")]
        public string taskId = "";

        [Header("Events")]
        [Tooltip("Deliberately non-generic - an open generic UnityEvent<bool> only serialises from " +
                 "Unity 2020.1 and comes back null when it fails, taking the whole flow down silently.")]
        public UnityEvent OnPhaseBegan;
        public UnityEvent OnCordPatched;
        public UnityEvent OnPhasePassed;
        public UnityEvent OnPhaseFailed;

        /// <summary>Raised once every cord is in and judged. PunchDownStation subscribes to this.</summary>
        public System.Action<bool, string> OnFinished;

        /// <summary>True between Begin() and the verdict.</summary>
        public bool IsRunning { get; private set; }
        public bool HasFinished { get; private set; }
        public bool LastResultPassed { get; private set; }

        /// <summary>Set while the camera is flying. A drag started mid-flight would be computed from
        /// a camera that is still moving, so the plug slides across the screen on its own.</summary>
        public bool InputSuspended { get; set; }

        public bool AcceptsInput { get { return IsRunning && !HasFinished && !InputSuspended; } }

        public Camera StageCamera { get { return stageCamera; } }

        /// <summary>Screen Snap Pixels, corrected for the screen this is actually running on, so the
        /// editor and a phone agree on how forgiving the stage is.</summary>
        public float EffectiveScreenSnap
        {
            get
            {
                if (!scaleSnapToScreen || referenceScreenHeight <= 0f) return screenSnapPixels;
                return screenSnapPixels * (Screen.height / referenceScreenHeight);
            }
        }

        PatchCordPlug claimedBy;
        int pressFrame = -1;
        PatchCordPlug pressWinner;
        Coroutine cameraRoutine;
        float hintClearAt;
        int wrongAttempts;

        // ------------------------------------------------------------------

        /// <summary>Same fallback rule as PunchStageBoard: the roots you named, or this object.</summary>
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
            if (sockets == null || sockets.Length == 0) sockets = CollectAll<PatchCordSocket>();
            if (plugs == null || plugs.Length == 0) plugs = CollectAll<PatchCordPlug>();

            foreach (var p in plugs) { if (p != null) p.Bind(this); }

            // NOT ResetStage() here. The punch phase is what runs first, and a reset would yank every
            // plug back to a home pose captured before the scene had finished settling.
            IsRunning = false;
            HasFinished = false;
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
        // Starting the phase
        // ------------------------------------------------------------------

        /// <summary>
        /// Starts the patch-cord phase. Returns TRUE if it took over, which is what tells
        /// PunchDownStation to keep the view open instead of dropping the player back in the world.
        ///
        /// Returns FALSE when there is nothing to do - no plugs in the scene, or the phase has
        /// already been completed this visit - so a station with this assigned but not yet built
        /// still behaves exactly as it did before.
        /// </summary>
        public bool Begin()
        {
            if (IsRunning) return true;
            if (HasFinished) return false;
            if (plugs == null || plugs.Length == 0)
            {
                Debug.LogWarning("[PatchCordBoard] Begin() was called but there are no PatchCordPlug " +
                                 "components under the search root(s), so there is nothing to patch. " +
                                 "The station will close as it did before.", this);
                return false;
            }

            IsRunning = true;
            wrongAttempts = 0;
            claimedBy = null;

            foreach (var s in sockets) { if (s != null) s.ResetSocket(); }
            foreach (var p in plugs) { if (p != null) { p.ResetPlug(); p.Bind(this); } }

            foreach (var go in hideOnBegin) { if (go != null) go.SetActive(false); }
            foreach (var go in showOnBegin) { if (go != null) go.SetActive(true); }
            foreach (var mb in disableOnBegin) { if (mb != null) mb.enabled = false; }

            MoveCameraToPose();
            RefreshUI();
            SetHint("Run each patch cord from the panel to the matching switch port.");

            OnPhaseBegan?.Invoke();

            if (introDialog != null && DialogManager.Instance != null && !DialogManager.Instance.IsDialogOpen)
                DialogManager.Instance.ShowDialog(introDialog);

            Debug.LogWarning("[PatchCordBoard] Patch-cord phase started - " + plugs.Length +
                             " cords, " + sockets.Length + " sockets.", this);
            return true;
        }

        void MoveCameraToPose()
        {
            if (cameraPose == null || stageCamera == null) return;
            if (cameraRoutine != null) StopCoroutine(cameraRoutine);
            cameraRoutine = StartCoroutine(FlyCamera(cameraPose.position, cameraPose.rotation));
        }

        System.Collections.IEnumerator FlyCamera(Vector3 toPos, Quaternion toRot)
        {
            InputSuspended = true;

            Transform cam = stageCamera.transform;
            Vector3 fromPos = cam.position;
            Quaternion fromRot = cam.rotation;

            float t = 0f;
            float duration = Mathf.Max(0.0001f, cameraMoveDuration);
            while (t < 1f)
            {
                // Unscaled: the intro dialog can stop the clock, and a move on Time.deltaTime would
                // sit frozen mid-flight with the player staring at nothing.
                t += Time.unscaledDeltaTime / duration;
                float f = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                cam.position = Vector3.Lerp(fromPos, toPos, f);
                cam.rotation = Quaternion.Slerp(fromRot, toRot, f);
                yield return null;
            }
            cam.SetPositionAndRotation(toPos, toRot);

            InputSuspended = false;
            cameraRoutine = null;
        }

        // ------------------------------------------------------------------
        // One drag at a time
        // ------------------------------------------------------------------

        /// <summary>
        /// Which plug a press at this screen point should pick up - the nearest one on screen, not
        /// whichever component Unity happened to update first.
        ///
        /// Same reasoning as PunchStageBoard.ConductorForPress: renderer bounds are axis-aligned
        /// boxes, eight cords hanging together overlap, and letting each plug claim for itself makes
        /// the winner depend on component order. Cached per frame so eight plugs asking in the same
        /// frame costs one pass, not eight.
        /// </summary>
        public PatchCordPlug PlugForPress(Vector2 screenPoint)
        {
            if (pressFrame == Time.frameCount) return pressWinner;
            pressFrame = Time.frameCount;
            pressWinner = null;

            if (plugs == null || stageCamera == null) return null;

            // Once per press, not once per plug. Touch needs the active finger's id - the
            // parameterless call only ever checks the mouse.
            if (EventSystem.current != null)
            {
                bool overUI = Input.touchCount > 0
                    ? EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId)
                    : EventSystem.current.IsPointerOverGameObject();
                if (overUI) return null;
            }

            float best = float.MaxValue;
            foreach (var p in plugs)
            {
                if (p == null || p.IsLocked || p.IsSeated || !p.isActiveAndEnabled) continue;
                if (!p.IsPointerOverBounds(screenPoint)) continue;

                float score = p.GrabScore(screenPoint);
                if (score < best) { best = score; pressWinner = p; }
            }
            return pressWinner;
        }

        public bool TryClaimDrag(PatchCordPlug who)
        {
            if (claimedBy != null && claimedBy != who) return false;
            claimedBy = who;
            return true;
        }

        public void ReleaseDrag(PatchCordPlug who)
        {
            if (claimedBy == who) claimedBy = null;
        }

        // ------------------------------------------------------------------
        // Where things stand
        // ------------------------------------------------------------------

        public PatchCordSocket Socket(int portNumber)
        {
            if (sockets == null) return null;
            foreach (var s in sockets)
            {
                if (s != null && s.portNumber == portNumber) return s;
            }
            return null;
        }

        public int PatchedCount()
        {
            int n = 0;
            if (plugs != null)
            {
                foreach (var p in plugs) { if (p != null && p.IsSeated) n++; }
            }
            return n;
        }

        /// <summary>The lowest cord not yet run. Only meaningful when Require In Order is on.</summary>
        public int NextExpectedPort()
        {
            for (int n = 1; n <= cordCount; n++)
            {
                PatchCordSocket s = Socket(n);
                if (s == null || !s.IsOccupied) return n;
            }
            return -1;
        }

        // ------------------------------------------------------------------
        // Hit testing - the same three tests, cheapest and strictest first
        // ------------------------------------------------------------------

        public PatchCordSocket SocketNearest(Vector3 worldPosition)
        {
            if (sockets == null) return null;
            PatchCordSocket best = null;
            float bestDist = snapDistance;

            foreach (var s in sockets)
            {
                if (s == null) continue;
                float d = s.DistanceTo(worldPosition);
                if (d <= bestDist) { bestDist = d; best = s; }
            }
            return best;
        }

        public PatchCordSocket SocketNearestOnScreen(Vector3 worldPosition, out float pixels)
        {
            pixels = float.MaxValue;
            if (sockets == null || stageCamera == null) return null;

            Vector3 from = stageCamera.WorldToScreenPoint(worldPosition);
            PatchCordSocket best = null;

            foreach (var s in sockets)
            {
                if (s == null) continue;
                Vector3 to = stageCamera.WorldToScreenPoint(s.Seat.position);
                if (to.z <= 0f) continue;          // behind the camera - not on screen at all
                float d = Vector2.Distance(new Vector2(from.x, from.y), new Vector2(to.x, to.y));
                if (d < pixels) { pixels = d; best = s; }
            }
            return best;
        }

        public PatchCordSocket SocketUnderScreenPoint(Vector2 screenPoint)
        {
            if (sockets == null || stageCamera == null) return null;

            Ray ray = stageCamera.ScreenPointToRay(screenPoint);
            PatchCordSocket best = null;
            float bestDist = float.MaxValue;

            foreach (var s in sockets)
            {
                if (s == null) continue;
                if (s.IsUnderRay(ray, out float d) && d < bestDist) { bestDist = d; best = s; }
            }
            return best;
        }

        // ------------------------------------------------------------------
        // Drops
        // ------------------------------------------------------------------

        /// <summary>Called by a plug when the player lets go. True if it went in - the plug sends
        /// itself home when this is false.</summary>
        public bool HandlePlugDropped(PatchCordPlug plug, Vector2 screenPoint)
        {
            ClearAllHighlights();
            if (!AcceptsInput || plug == null) { RefreshUI(); return false; }

            PatchCordSocket target = SocketNearest(plug.transform.position);

            float px;
            PatchCordSocket onScreen = SocketNearestOnScreen(plug.transform.position, out px);
            float snapPx = EffectiveScreenSnap;
            if (target == null && snapPx > 0f && onScreen != null && px <= snapPx)
                target = onScreen;

            if (target == null && alsoAcceptRayHit) target = SocketUnderScreenPoint(screenPoint);

            if (target == null)
            {
                if (logRefusedDrops) LogRefusal(plug, onScreen, px);
                SetHint("Bring the plug down onto a switch port.");
                RefreshUI();
                return false;
            }

            if (target.IsOccupied)
            {
                SetHint("Switch port " + target.portNumber + " already has a cord in it.");
                RefreshUI();
                return false;
            }

            if (requireInOrder)
            {
                int expected = NextExpectedPort();
                if (expected > 0 && plug.portNumber != expected)
                {
                    SetHint("Work the panel in order - port " + expected + " is next.");
                    RefreshUI();
                    return false;
                }
            }

            if (requireCorrectPort && target.portNumber != plug.portNumber)
            {
                wrongAttempts++;
                SetHint("Panel port " + plug.portNumber + " goes to switch port " + plug.portNumber +
                        ", not " + target.portNumber + ".");
                RefreshUI();
                return false;
            }

            target.Seat_(plug);
            plug.SeatInto(target);
            plug.Lock();                 // a patched cord stays put, the same finality as a punch

            OnCordPatched?.Invoke();

            if (PatchedCount() >= cordCount) { Finish(); return true; }

            SetHint(requireInOrder
                ? "Port " + NextExpectedPort() + " next."
                : PatchedCount() + " of " + cordCount + " patched.");
            RefreshUI();
            return true;
        }

        /// <summary>
        /// Says exactly how close the drop got, both ways, so one Console line separates the two
        /// possible faults instead of leaving you to guess.
        ///
        ///   world small, screen small  -> raise Snap Distance a little
        ///   world LARGE, screen small  -> the cords hang at a different depth from the switch ports;
        ///                                 that is what Screen Snap Pixels exists for
        ///   both large                 -> the drop really was nowhere near
        /// </summary>
        void LogRefusal(PatchCordPlug plug, PatchCordSocket nearestOnScreen, float pixels)
        {
            PatchCordSocket nearest = null;
            float world = float.MaxValue;
            if (sockets != null)
            {
                foreach (var s in sockets)
                {
                    if (s == null) continue;
                    float d = s.DistanceTo(plug.transform.position);
                    if (d < world) { world = d; nearest = s; }
                }
            }

            Debug.LogWarning(string.Format(
                "[PatchCordBoard] Drop refused - no switch port found.\n" +
                "    nearest by WORLD distance : {0}   {1:F4} world units   (Snap Distance is {2})\n" +
                "    nearest ON SCREEN         : {3}   {4:F0} pixels        (Screen Snap Pixels is {5})\n" +
                "    If the world number is large but the pixel number is small, the cords hang at a " +
                "different depth from the switch ports - raise Screen Snap Pixels, not Snap Distance.",
                nearest == null ? "none" : "port " + nearest.portNumber, world, snapDistance,
                nearestOnScreen == null ? "none" : "port " + nearestOnScreen.portNumber, pixels,
                EffectiveScreenSnap.ToString("F0") + (scaleSnapToScreen
                    ? " - " + screenSnapPixels + " scaled for a " + Screen.height + "px screen"
                    : "")), this);
        }

        // ------------------------------------------------------------------
        // Verdict
        // ------------------------------------------------------------------

        void Finish()
        {
            string reason;
            bool passed = Evaluate(out reason);

            HasFinished = true;
            IsRunning = false;
            LastResultPassed = passed;
            ClearAllHighlights();

            if (progressText != null)
            {
                progressText.text = passed ? "PASS - cross-connect complete." : "FAIL";
                progressText.color = passed ? passColor : failColor;
            }
            if (hintText != null)
            {
                hintText.text = passed ? "" : reason;
                hintClearAt = 0f;
            }

            if (passed)
            {
                if (!string.IsNullOrEmpty(taskId) && TaskManager.Instance != null)
                    TaskManager.Instance.ReportProgress(taskId);
                OnPhasePassed?.Invoke();
            }
            else
            {
                OnPhaseFailed?.Invoke();
            }

            Debug.LogWarning("[PatchCordBoard] Patching " + (passed ? "PASSED" : "FAILED") + ". " +
                             reason, this);

            OnFinished?.Invoke(passed, reason);
        }

        public bool Evaluate(out string reason)
        {
            var wrong = new StringBuilder();
            int bad = 0;

            if (plugs != null)
            {
                foreach (var p in plugs)
                {
                    if (p == null) continue;
                    if (p.SeatedIn == null)
                    {
                        bad++;
                        if (bad <= 4)
                        {
                            if (wrong.Length > 0) wrong.Append(" ");
                            wrong.Append("Panel port " + p.portNumber + " was never patched.");
                        }
                        continue;
                    }
                    if (p.SeatedIn.portNumber != p.portNumber)
                    {
                        bad++;
                        if (bad <= 4)
                        {
                            if (wrong.Length > 0) wrong.Append(" ");
                            wrong.Append("Panel port " + p.portNumber + " is in switch port " +
                                         p.SeatedIn.portNumber + ".");
                        }
                    }
                }
            }

            if (bad == 0)
            {
                reason = wrongAttempts == 0
                    ? "Every panel port is cross-connected to its matching switch port."
                    : "Every panel port is cross-connected correctly, after " + wrongAttempts +
                      " wrong attempt(s).";
                return true;
            }

            reason = "The cross-connect doesn't line up. " + wrong;
            if (bad > 4) reason += " (" + (bad - 4) + " more are also wrong.)";
            return false;
        }

        // ------------------------------------------------------------------
        // Presentation
        // ------------------------------------------------------------------

        /// <summary>Called every frame a cord is being dragged - shows where it belongs.</summary>
        public void HighlightForPlug(PatchCordPlug plug)
        {
            if (!showTargetHighlight || plug == null) { ClearAllHighlights(); return; }
            SetOnlyHighlight(Socket(plug.portNumber));
        }

        void SetOnlyHighlight(PatchCordSocket only)
        {
            if (sockets == null) return;
            foreach (var s in sockets) { if (s != null) s.SetHighlighted(s == only && !s.IsOccupied); }
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
                progressText.text = PatchedCount() + " of " + cordCount + " patched";
                progressText.color = progressColor;
            }
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Pulls every cord back out for another go, WITHOUT leaving the phase.
        ///
        /// Distinct from ResetStage() on purpose. ResetStage undoes Begin() entirely - it puts the
        /// punch tool back on screen, re-enables the punch camera, and would make the next Begin()
        /// fly the camera to a pose it is already sitting at. That is right for a fresh visit to the
        /// station and wrong for a retry, where the player should simply find the cords back at the
        /// panel and the shot unchanged.
        /// </summary>
        public void Retry()
        {
            HasFinished = false;
            LastResultPassed = false;
            IsRunning = true;
            claimedBy = null;
            wrongAttempts = 0;

            if (sockets != null)
            {
                foreach (var s in sockets) { if (s != null) s.ResetSocket(); }
            }
            if (plugs != null)
            {
                foreach (var p in plugs) { if (p != null) { p.ResetPlug(); p.Bind(this); } }
            }

            SetHint("Try again - each cord goes to the switch port with its own number.");
            RefreshUI();
        }

        /// <summary>
        /// Pulls every cord back out, puts them where they started, and undoes everything Begin()
        /// switched.
        ///
        /// That last part matters more than it looks. Begin() DISABLES the components in Disable On
        /// Begin - PunchStageCamera among them - and a disabled component never runs OnEnable again.
        /// Without putting them back, the second visit to the station would open with a camera script
        /// that never re-arms, and the punch phase would play from wherever the last shot left off.
        /// </summary>
        [ContextMenu("Reset patch-cord phase")]
        public void ResetStage()
        {
            IsRunning = false;
            HasFinished = false;
            LastResultPassed = false;
            claimedBy = null;
            wrongAttempts = 0;

            if (cameraRoutine != null) { StopCoroutine(cameraRoutine); cameraRoutine = null; }
            InputSuspended = false;

            foreach (var go in hideOnBegin) { if (go != null) go.SetActive(true); }
            foreach (var go in showOnBegin) { if (go != null) go.SetActive(false); }
            foreach (var mb in disableOnBegin) { if (mb != null) mb.enabled = true; }

            if (sockets != null)
            {
                foreach (var s in sockets) { if (s != null) s.ResetSocket(); }
            }
            if (plugs != null)
            {
                foreach (var p in plugs) { if (p != null) p.ResetPlug(); }
            }

            SetHint(null);
            RefreshUI();
        }

        /// <summary>
        /// Measures the real gap between neighbouring switch ports and checks the snap settings
        /// against it.
        ///
        /// Worth having twice over here: switch ports sit far closer together than IDC positions do,
        /// so settings carried over from the punch board will happily drop every cord into the port
        /// next door - which produces no error at all and looks exactly like broken code.
        /// </summary>
        void CheckScale(StringBuilder sb)
        {
            if (sockets == null || sockets.Length < 2) return;

            float minGap = float.MaxValue;
            for (int a = 0; a < sockets.Length; a++)
            {
                if (sockets[a] == null) continue;
                for (int b = a + 1; b < sockets.Length; b++)
                {
                    if (sockets[b] == null) continue;
                    float d = Vector3.Distance(sockets[a].Seat.position, sockets[b].Seat.position);
                    if (d > 0.00001f && d < minGap) minGap = d;
                }
            }
            if (minGap == float.MaxValue) return;

            string gap = minGap.ToString("F4");

            if (snapDistance >= minGap)
                sb.AppendLine("  - Snap Distance (" + snapDistance + ") is at least as big as the gap " +
                              "between neighbouring switch ports (" + gap + "). A cord will land in " +
                              "the wrong port. Try " + (minGap * 0.6f).ToString("F4") + ".");
            else if (snapDistance < minGap * 0.15f)
                sb.AppendLine("  - Snap Distance (" + snapDistance + ") is tiny next to the gap " +
                              "between ports (" + gap + "), so almost every drop will be refused. " +
                              "About " + (minGap * 0.6f).ToString("F4") + " would suit this switch.");

            foreach (var s in sockets)
            {
                if (s == null || !s.UsesRadius) continue;
                if (s.pickRadius >= minGap * 0.5f)
                {
                    sb.AppendLine("  - Socket " + s.portNumber + " has no renderer and its Pick Radius (" +
                                  s.pickRadius + ") reaches past halfway to the next port (" + gap +
                                  " apart), so a cord will go into whichever it hits first. Try " +
                                  (minGap * 0.35f).ToString("F4") + ".");
                    break;   // one is enough; they are almost always all set the same
                }
            }
        }

        /// <summary>Says out loud what is missing, rather than the phase quietly doing nothing.</summary>
        [ContextMenu("Check my setup")]
        public void ValidateSetup()
        {
            var sb = new StringBuilder();

            int socketCount = sockets == null ? 0 : sockets.Length;
            if (socketCount < cordCount)
                sb.AppendLine("  - Found " + socketCount + " PatchCordSocket components under the " +
                              "search root(s); Cord Count is " + cordCount + ". If the sockets live " +
                              "on the switch model, add it to Search Roots.");

            int plugCount = plugs == null ? 0 : plugs.Length;
            if (plugCount != cordCount)
                sb.AppendLine("  - Found " + plugCount + " PatchCordPlug components under the search " +
                              "root(s); Cord Count is " + cordCount + ". If the cords live under " +
                              "PatchCords, add that to Search Roots.");

            var seenSocket = new System.Collections.Generic.HashSet<int>();
            if (sockets != null)
            {
                foreach (var s in sockets)
                {
                    if (s == null) continue;
                    if (!seenSocket.Add(s.portNumber))
                        sb.AppendLine("  - Two sockets both claim switch port " + s.portNumber +
                                      ". One of them can never be reached.");
                }
            }

            var seenPlug = new System.Collections.Generic.HashSet<int>();
            if (plugs != null)
            {
                foreach (var p in plugs)
                {
                    if (p == null) continue;
                    if (!seenPlug.Add(p.portNumber))
                        sb.AppendLine("  - Two cords are both set to panel port " + p.portNumber +
                                      ", so one port has no cord and another has two.");
                    if (Socket(p.portNumber) == null)
                        sb.AppendLine("  - Cord " + p.portNumber + " has no socket with a matching " +
                                      "Port Number, so it can never be plugged in anywhere.");
                }
            }

            if (cameraPose == null)
                sb.AppendLine("  - Camera Pose is empty, so the camera will stay closed in on the " +
                              "last punched port. The player will not be able to see the switch.");

            CheckScale(sb);

            if (sb.Length == 0)
            {
                Debug.LogWarning("[PatchCordBoard] Setup looks complete - " + plugCount + " cords, " +
                                 socketCount + " sockets, camera pose set.", this);
                return;
            }
            Debug.LogWarning("[PatchCordBoard] Setup is incomplete:\n" + sb, this);
        }
    }
}