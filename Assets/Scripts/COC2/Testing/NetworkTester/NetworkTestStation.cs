// NetworkTestStation.cs
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using CSSSimulation.Interaction;

namespace CSSSimulation.Networking
{
    /// <summary>
    /// The network testing bench. Walk up with a crimped cable in your pocket, press E, the camera
    /// moves in, the cable appears at the tester, you plug it in, and your pass/fail animation plays.
    ///
    /// Whether it passes is NOT random and NOT a setting - it's read from the pinout the player
    /// actually crimped, via CableTestAnalyzer. A correctly built cable passes; a miswired one fails
    /// and the readout says why.
    ///
    /// Setup:
    ///   1. Put this on your tester bench object, with a trigger Collider for the interaction range.
    ///   2. Assign playerCamera and testerCamera.
    ///   3. Assign stationCableVisual - the cable model sitting at the tester. It stays hidden until
    ///      the player arrives carrying one.
    ///   4. Assign testerAnimator and fill in the three trigger names to match your Animator.
    ///   5. Fill playerControlScripts the same way your other view controllers do.
    ///   6. Set promptText to something like "Press E to test cable".
    ///
    /// Everything else is optional.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class NetworkTestStation : Interactable
    {
        public static NetworkTestStation Instance { get; private set; }

        [Header("Cameras")]
        public Camera playerCamera;
        [Tooltip("Close-up on the tester.")]
        public Camera testerCamera;

        [Header("Cable at the tester")]
        [Tooltip("The cable model at the tester bench. Hidden until the player arrives carrying the " +
                 "crimped cable, so it looks like they put it down.")]
        public GameObject stationCableVisual;

        [Header("Plugging in")]
        [Tooltip("The draggable RJ45 end. Assign this and the player drags the plug into the socket, " +
                 "COC1 CableConnector style, and the test starts the moment it seats. Leave empty to " +
                 "fall back to the simpler click-the-cable behaviour below.")]
        public TesterCablePlug cablePlug;

        [Tooltip("Only used when Cable Plug is empty. Tick to let the player click the cable anywhere; " +
                 "untick to drive it purely from a UI button wired to PlugIn().")]
        public bool clickCableToPlug = true;

        [Header("Animation")]
        [Tooltip("The Animator holding your plug-in and pass/fail clips.")]
        public Animator testerAnimator;
        [Tooltip("Trigger for the 'cable goes into the socket' clip. Clear to skip straight to the result.")]
        public string plugInTrigger = "PlugIn";
        [Tooltip("Seconds the plug-in clip runs for.")]
        public float plugInLength = 1f;
        public string passTrigger = "TestPass";
        public string failTrigger = "TestFail";
        [Tooltip("Seconds the pass/fail clip runs for, before the result text and events fire.")]
        public float resultLength = 2f;

        [Header("Controls")]
        [Tooltip("Key that leaves the tester close-up.\n\n" +
                 "Defaulted to E - the same key that opened the station - because that is what a " +
                 "player reaches for, and because this bench is entered from the walking view rather " +
                 "than from a menu. Pressing it to get out cannot re-enter the station: entry is " +
                 "ignored for a frame afterwards, so PlayerInteractor seeing the same keypress does " +
                 "nothing.\n\n" +
                 "Set to None to disable and rely on Escape or a Back button alone.")]
        public KeyCode exitKey = KeyCode.E;

        [Tooltip("Leave on. Escape also leaves the tester, which is what this station did before it " +
                 "had an Exit Key at all - untick only if your pause menu should own Escape here.")]
        public bool alsoExitOnEscape = true;

        [Header("Player control")]
        [Tooltip("Same array you filled in on your other view controllers.")]
        public MonoBehaviour[] playerControlScripts;

        [Header("UI")]
        [Tooltip("Shown while in the tester view - e.g. 'Click the cable to plug it in'.")]
        public GameObject testerUI;
        [Tooltip("Optional. Shows PASS/FAIL and, on a failure, what's actually wrong with the cable.")]
        public TMP_Text resultText;
        public Color passColor = new Color(0.15f, 0.8f, 0.3f);
        public Color failColor = new Color(0.9f, 0.25f, 0.2f);

        [Header("Mobile")]
        public GameObject[] mobileControlsToHide;
        [Tooltip("Shown only in the tester view - e.g. a Plug In button and a Back button.")]
        public GameObject[] mobileTesterControls;

        [Header("Anything else at the bench")]
        [Tooltip("Switched ON while the player is inside the tester, and OFF again when they " +
                 "leave. Anything at all goes in here - a hint panel, a label on the tester, a " +
                 "second camera angle, a particle effect, a Back button.\n\n" +
                 "Separate from Mobile Tester Controls because that list is for touch controls and " +
                 "reads as being about phones. This one has nothing to do with the device, so a " +
                 "thing that should appear at the bench on EVERY platform belongs here.")]
        public GameObject[] objectsToEnable = new GameObject[0];

        [Tooltip("Switched OFF while the player is inside the tester, and back ON when they leave. " +
                 "For things that belong to the room rather than the bench - a floating task " +
                 "arrow, a world-space sign, an ambience emitter.\n\n" +
                 "An object holding the Back button is refused here and says so, because switching " +
                 "it off is what traps a phone player inside the station.")]
        public GameObject[] objectsToDisable = new GameObject[0];

        [Header("Dialog (optional)")]
        [Tooltip("Shown if the player walks up without a crimped cable.")]
        public DialogSequence noCableDialog;

        [Header("Task (optional)")]
        public string taskId = "test_cable";
        public bool reportOnlyOnPass = true;

        [Header("Events")]
        [Tooltip("Fires once the result animation finishes: true if the cable passed.")]
        public UnityEvent<bool> OnTestResult;

        [Tooltip("Fires when the player sits down at the tester.\n\n" +
                 "For anything a plain on/off list cannot express - starting a coroutine, setting a " +
                 "text field, telling another component to reset itself. The two lists above cover " +
                 "showing and hiding; this covers everything else.")]
        public UnityEvent OnEnteredStation;

        [Tooltip("Fires when the player leaves the tester, however they leave - the exit key, the " +
                 "Back button, or the procedure resetting itself between cables.")]
        public UnityEvent OnLeftStation;

        public bool InTesterView { get; private set; }
        public bool IsTesting { get; private set; }
        public bool HasTested { get; private set; }

        Renderer[] cableRenderers;

        // The frame the close-up was left on. PlayerInteractor and this component both read the
        // same keypress in the same frame, in an order Unity does not promise: without this, E to
        // leave can be E to walk straight back in, and the station looks like it ignored the press.
        int exitedOnFrame = -10;

        // The mirror of exitedOnFrame, and needed for the same reason in the other direction. E
        // enters the tester, and GetKeyDown stays true for the whole frame - so this component's
        // own Update, running later in that same frame, saw the press again and left immediately.
        // The player pressed E once and never got in.
        int enteredOnFrame = -10;

        void Awake()
        {
            Instance = this;
            SetTesterViewActive(false);
        }

        void Start()
        {
            // Subscribed in Start so the plug has finished its own Awake first.
            if (cablePlug != null) cablePlug.OnPlugged += HandlePlugSeated;
        }

        void OnDestroy()
        {
            if (cablePlug != null) cablePlug.OnPlugged -= HandlePlugSeated;
        }

        void HandlePlugSeated()
        {
            PlugIn();
        }

        // ------------------------------------------------------------------
        // Entering / leaving
        // ------------------------------------------------------------------

        public override void Interact()
        {
            if (InTesterView) return;

            // Just left with the exit key, and the interactor is reading that same press. Ignoring
            // it here is what makes E work as "get me out" rather than as a toggle that lands on
            // whichever component Unity happened to update second.
            if (Time.frameCount - exitedOnFrame <= 1) return;

            if (CableInventory.Instance == null || !CableInventory.Instance.HasCable)
            {
                if (noCableDialog != null && DialogManager.Instance != null && !DialogManager.Instance.IsDialogOpen)
                    DialogManager.Instance.ShowDialog(noCableDialog);
                else
                    Debug.Log("[NetworkTestStation] The player isn't carrying a crimped cable yet.", this);
                return;
            }

            SetTesterViewActive(true);
        }

        /// <summary>Wire to a mobile Back button. Also what the exit key calls, so the key and the
        /// button can never drift apart.</summary>
        public void RequestExit()
        {
            if (!InTesterView)
            {
                Debug.Log("[NetworkTestStation] Exit ignored - the tester close-up is not open.", this);
                return;
            }

            // Said out loud rather than refused in silence. A test runs for plugInLength +
            // resultLength seconds, and a player pressing E during it gets nothing back; without a
            // line in the Console that reads as the key being broken rather than as the station
            // being busy.
            if (IsTesting)
            {
                Debug.Log("[NetworkTestStation] Exit ignored - the test is still running. It finishes in a moment.", this);
                return;
            }

            exitedOnFrame = Time.frameCount;
            SetTesterViewActive(false);
            Debug.Log("[NetworkTestStation] Left the tester.", this);
        }

        void Update()
        {
            if (!InTesterView) return;

            // Just arrived, and the key that brought us here is still being reported this frame.
            if (Time.frameCount - enteredOnFrame <= 1) return;

            // Read before the IsTesting guard, so a press during the test is answered with a
            // reason instead of being swallowed by an early return.
            bool wantsOut =
                (exitKey != KeyCode.None && Input.GetKeyDown(exitKey)) ||
                (alsoExitOnEscape && Input.GetKeyDown(KeyCode.Escape));

            if (wantsOut)
            {
                RequestExit();
                return;
            }

            if (IsTesting) return;

            // The drag path drives itself - TesterCablePlug fires OnPlugged the moment it seats,
            // so there's nothing to poll here.
            if (cablePlug != null) return;

            if (!HasTested && clickCableToPlug && PointerInput.Down && PointerOverCable())
                PlugIn();
        }

        /// <summary>Picks off the cable's VISIBLE bounds rather than a collider - no colliders to set
        /// up, and it can't go out of sync with where the mesh is drawn.</summary>
        bool PointerOverCable()
        {
            if (stationCableVisual == null || testerCamera == null) return false;

            if (cableRenderers == null || cableRenderers.Length == 0)
                cableRenderers = stationCableVisual.GetComponentsInChildren<Renderer>(true);

            Ray ray = testerCamera.ScreenPointToRay(PointerInput.Position);
            foreach (var renderer in cableRenderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (renderer.bounds.IntersectRay(ray)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // The test
        // ------------------------------------------------------------------

        /// <summary>Wire a "Plug In" button's OnClick to this, or let the player click the cable.</summary>
        public void PlugIn()
        {
            if (!InTesterView || IsTesting || HasTested) return;
            StartCoroutine(TestRoutine());
        }

        IEnumerator TestRoutine()
        {
            IsTesting = true;
            if (testerUI != null) testerUI.SetActive(false);
            if (resultText != null) resultText.text = "";

            // The cable is in the tester now, not in the player's pocket.
            if (CableInventory.Instance != null) CableInventory.Instance.Consume();

            if (testerAnimator != null && !string.IsNullOrEmpty(plugInTrigger))
            {
                testerAnimator.ResetTrigger(plugInTrigger);
                testerAnimator.SetTrigger(plugInTrigger);
                yield return new WaitForSeconds(plugInLength);
            }

            bool passed = EvaluateCable(out string summary);

            string trigger = passed ? passTrigger : failTrigger;
            if (testerAnimator != null && !string.IsNullOrEmpty(trigger))
            {
                testerAnimator.ResetTrigger(trigger);
                testerAnimator.SetTrigger(trigger);
            }

            yield return new WaitForSeconds(resultLength);

            if (resultText != null)
            {
                resultText.text = summary;
                resultText.color = passed ? passColor : failColor;
            }

            if (!string.IsNullOrEmpty(taskId) && TaskManager.Instance != null)
            {
                if (!reportOnlyOnPass || passed)
                    TaskManager.Instance.ReportProgress(taskId);
            }

            Debug.Log($"[NetworkTestStation] Cable test {(passed ? "PASSED" : "FAILED")}. {summary}", this);

            HasTested = true;
            IsTesting = false;
            OnTestResult?.Invoke(passed);
        }

        /// <summary>Reads the pinout the player actually crimped and judges it. No randomness.</summary>
        bool EvaluateCable(out string summary)
        {
            var station = CrimpingStationController.Instance;
            if (station == null)
            {
                summary = "No crimping station found - can't read the cable.";
                return false;
            }

            WireColor[] endOne = station.GetCrimpedArrangement(1);
            WireColor[] endTwo = station.GetCrimpedArrangement(2);

            if (endOne == null || endTwo == null)
            {
                summary = "This cable hasn't been crimped on both ends.";
                return false;
            }

            CableTestReport report = CableTestAnalyzer.Analyze(endOne, endTwo, station.targetCableType);

            summary = report.Passed
                ? "PASS - all 8 conductors correct."
                : "FAIL - " + CableTestAnalyzer.FailureHint(report);

            return report.Passed;
        }

        /// <summary>Clears the station so another cable can be tested. Wire to a Retry button.</summary>
        public void ResetStation()
        {
            StopAllCoroutines();
            IsTesting = false;
            HasTested = false;
            if (resultText != null) resultText.text = "";
            if (cablePlug != null)
            {
                cablePlug.Unplug();
                cablePlug.enabled = InTesterView;
            }
        }

        // ------------------------------------------------------------------

        void SetTesterViewActive(bool active)
        {
            if (active) enteredOnFrame = Time.frameCount;

            InTesterView = active;

            if (playerCamera != null) playerCamera.gameObject.SetActive(!active);
            if (testerCamera != null) testerCamera.gameObject.SetActive(active);

            // The cable only appears once the player brings one, so it reads as them putting it down.
            if (stationCableVisual != null) stationCableVisual.SetActive(active);

            // Draggable only while the close-up is open, so the plug can't be grabbed from across the room.
            if (cablePlug != null) cablePlug.enabled = active && !HasTested;

            foreach (var script in playerControlScripts)
            {
                if (script != null) script.enabled = !active;
            }

            if (testerUI != null) testerUI.SetActive(active && !HasTested);

            foreach (var obj in mobileControlsToHide)
            {
                if (obj != null) SetActiveUnlessItIsTheWayOut(obj, !active);
            }
            foreach (var obj in mobileTesterControls)
            {
                if (obj != null) obj.SetActive(active);
            }

            // The two device-independent lists. Same idea, no assumption about phones.
            foreach (var obj in objectsToEnable)
            {
                if (obj != null) obj.SetActive(active);
            }
            foreach (var obj in objectsToDisable)
            {
                if (obj != null) SetActiveUnlessItIsTheWayOut(obj, !active);
            }

            Cursor.lockState = active ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = active;

            // Fired last, so anything listening sees the station already in its new state - a
            // listener that reads InTesterView, or moves something that was just switched on, would
            // otherwise be looking at the world as it was a moment ago.
            if (!Application.isPlaying || firstApply) { firstApply = false; return; }

            if (active) { if (OnEnteredStation != null) OnEnteredStation.Invoke(); }
            else { if (OnLeftStation != null) OnLeftStation.Invoke(); }
        }

        // Awake calls SetTesterViewActive(false) to put the bench in its closed state. That is
        // housekeeping, not the player leaving, so it must not fire OnLeftStation - otherwise every
        // scene load runs the leaving logic before anybody has arrived.
        bool firstApply = true;

        /// <summary>
        /// Switches an object off, unless doing so would take the Back button down with it.
        ///
        /// WHY THIS GUARD EXISTS
        ///
        /// StationExitButton hides ITSELF, with a CanvasGroup, precisely so that it keeps running
        /// while invisible - it has to, because it is also what listens for the exit key, and a
        /// deactivated component listens for nothing. Switch its object off, or any parent of it,
        /// and it stops running: Back never reappears, the exit key stops working, and the player is
        /// sealed inside the station with no error anywhere to explain it.
        ///
        /// The touch controls canvas is the usual casualty, because the Back button sensibly lives
        /// on it and the canvas sensibly goes in Mobile Controls To Hide. Both decisions are
        /// reasonable; together they lock the player in. So the hide is refused for that one object
        /// and says why, rather than being silently obeyed.
        /// </summary>
        void SetActiveUnlessItIsTheWayOut(GameObject obj, bool wanted)
        {
            if (wanted) { obj.SetActive(true); return; }

            StationExitButton exit = obj.GetComponentInChildren<StationExitButton>(true);
            if (exit == null) { obj.SetActive(false); return; }

            if (reportedExitClash != obj)
            {
                reportedExitClash = obj;
                Debug.LogWarning("[NetworkTestStation] Not switching '" + obj.name + "' off on the " +
                                 "way into the tester, because the Back button '" + exit.name +
                                 "' is inside it.\n\n" +
                                 "Back hides itself with a CanvasGroup so that it keeps running " +
                                 "while invisible - that is also what makes the exit key work. " +
                                 "Switching its object or any parent off stops it running, and the " +
                                 "player is then stuck inside the tester with nothing on screen to " +
                                 "get them out.\n\n" +
                                 "If you need the rest of that object hidden, move the Back button " +
                                 "out from under it and put it on its own always-on canvas.", this);
            }

            // Everything else under it can still go. The button stays, and hides itself.
            for (int i = 0; i < obj.transform.childCount; i++)
            {
                Transform child = obj.transform.GetChild(i);
                if (child.GetComponentInChildren<StationExitButton>(true) == null)
                    child.gameObject.SetActive(false);
            }
        }

        GameObject reportedExitClash;
    }
}