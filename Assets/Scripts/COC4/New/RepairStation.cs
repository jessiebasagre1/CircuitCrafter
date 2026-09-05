using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// A place at a repair site the learner walks up to and presses E to use.
    ///
    /// ATTACH THIS TO: an empty GameObject per station. A repair site has two:
    ///
    ///   MonitorStation      - where you find out whether the machine works.
    ///                         Entering brings up the boot screen; the POWER
    ///                         button lives on it.
    ///   SystemUnitStation   - where you fix it. Entering lays the tower on its
    ///                         side and hands over the close-up camera.
    ///
    /// WHY ONE COMPONENT FOR BOTH
    /// They are the same interaction with different contents: stand near it,
    /// press E, the camera changes, some UI appears, press E again to step back.
    /// Writing that twice would mean two places to fix the day the prompt or the
    /// input lock needs changing, and two subtly different feels for what the
    /// learner reads as one verb.
    ///
    /// IT IS StationCameraSwitch, PLUS THE TWO THINGS A REPAIR NEEDS.
    /// The camera fields and the enable/disable arrays are deliberately identical
    /// to StationCameraSwitch's, so anything you already had wired there drags
    /// straight across. What is added is the entry animation - a tower has to lie
    /// down before you can work in it, and watching that happen is what makes the
    /// station feel like a machine rather than a menu - and a player input lock,
    /// because WASD are also letters and a learner leaning on the keyboard at a
    /// station should not walk away from it.
    ///
    /// ONE STATION AT A TIME, enforced by the static Current. Two stations both
    /// believing they own the camera is a bug you only find in a playtest.
    /// </summary>
    public class RepairStation : MonoBehaviour
    {
        private enum State { Outside, Entering, Inside, Leaving }

        /// <summary>The station the learner is currently inside, or null.</summary>
        public static RepairStation Current { get; private set; }

        // ==================================================================
        // Inspector
        // ==================================================================

        [Header("Identity")]
        [Tooltip("Used in the walk-up prompt: \"Press E to use the monitor\". Write it lower-case, as the end of that sentence.")]
        [SerializeField] private string stationName = "the monitor";

        [Header("Walking up to it")]
        [Tooltip("What the learner has to be near. Leave empty to use this object's own position - fine if you place this object at the station.")]
        [SerializeField] private Transform stationPoint;

        [Min(0.5f)]
        [SerializeField] private float interactRange = 2.5f;

        [SerializeField] private KeyCode interactKey = KeyCode.E;

        [Tooltip("Found automatically by the Player tag if left empty.")]
        [SerializeField] private Transform player;

        [Header("Cameras")]
        [Tooltip("The walking camera. Switched off while the learner is at this station.")]
        [SerializeField] private Camera playerCamera;

        [Tooltip("The camera for this station. For the monitor, framed on the screen; for the system unit, framed on where the tower ENDS UP once it is lying down - not where it stands.")]
        [SerializeField] private Camera stationCamera;

        [Tooltip("Turned on at this station - the boot screen, the hotbar, the part description panel.")]
        [SerializeField] private GameObject[] enableOnEnter;

        [Tooltip("Turned off at this station - mobile movement controls, the waypoint arrow, the walk-up prompt.")]
        [SerializeField] private GameObject[] disableOnEnter;

        [Header("Entry animation (system unit only)")]
        [Tooltip("The object that moves as the learner enters - the system unit. Use the ROOT of the machine: the case AND everything mounted inside it, or the case will tip over while the parts stay floating.\n\nLeave empty for a station where nothing moves, like the monitor.")]
        [SerializeField] private Transform movedObject;

        [Tooltip("A marker object placed where Moved Object should end up, posed by hand. Duplicate the system unit, lay the copy on its side, copy its Transform onto an empty, delete the copy.")]
        [SerializeField] private Transform enteredPose;

        [Tooltip("Fallback, used only when Entered Pose is empty. Rotation relative to how the object sits now. Which axis tips a tower over depends on the model - try 90 and -90 on Z, then on X.")]
        [SerializeField] private Vector3 enteredEuler = new Vector3(0f, 0f, -90f);

        [Tooltip("Fallback, used only when Entered Pose is empty. World-space movement applied as it tips - usually a small drop.")]
        [SerializeField] private Vector3 enteredOffset = new Vector3(0f, -0.3f, 0f);

        [Tooltip("How long the move takes. Long enough to read as a deliberate action, short enough not to become annoying by the fifth ticket.")]
        [Min(0.05f)]
        [SerializeField] private float moveSeconds = 0.9f;

        [Header("UI")]
        [Tooltip("Optional. The walk-up prompt, e.g. \"Press E to use the monitor\".")]
        [SerializeField] private TMP_Text promptText;

        [Tooltip("Optional, for mobile. Shown only while the learner is close enough and outside. Wire its OnClick to Enter().")]
        [SerializeField] private Button enterButton;

        [Tooltip("Optional. Shown only while the learner is at this station. Wire its OnClick to Exit().")]
        [SerializeField] private Button exitButton;

        [Header("Rules")]
        [Tooltip("Leave on. Standing the case up with a part still in the learner's hand would drop it somewhere unrecoverable, and there is no honest place for it to land.")]
        [SerializeField] private bool blockExitWhileHoldingPart = true;

        [Tooltip("Turn this ON for the SYSTEM UNIT station. The case stays shut until the fault has been named.\n\nWHY: the moment the learner sees inside, the diagnosis stops being a diagnosis. A missing stick of RAM, an unplugged cable, a fan buried in dust - all of them are simply VISIBLE, and the panel turns into a reading comprehension test. Locking the case forces the call to be made from the symptom, which is the skill the panel is supposed to be measuring.\n\nLeave it OFF on the monitor station - looking at the screen is how the symptom is gathered in the first place.")]
        [SerializeField] private bool requireDiagnosisToEnter = false;

        [Tooltip("Shown the first time the learner tries to open a case they have not diagnosed. Leave empty and a line is written for you.")]
        [SerializeField] private DialogSequence lockedUntilDiagnosedDialog;

        [Tooltip("The walk-up prompt while the station is locked. The diagnose key is prepended for you, so write only the end of the sentence.")]
        [SerializeField] private string lockedPrompt = "the case stays shut until you do";

        [Tooltip("Shown the first time this station is entered on this visit. Optional.")]
        [SerializeField] private DialogSequence firstEnterDialog;

        [Tooltip("Shown instead of the above when the learner arrives and the fault has NOT been diagnosed yet - the nudge that tells them to run the diagnosis before touching anything. Leave empty and one is written for you.\n\nAssign this on the SYSTEM UNIT station, where the temptation to start pulling parts actually lives.")]
        [SerializeField] private DialogSequence diagnoseFirstDialog;

        [Tooltip("Turn off if this station should never nag about diagnosing - the monitor, for instance, where looking is the whole point.")]
        [SerializeField] private bool nagWhenUndiagnosed = true;

        [Header("Service call record (optional)")]
        [Tooltip("Recorded as an observation the first time the learner enters this station, e.g. \"open_case\" for the system unit. Leave blank to record nothing.\n\nThis is what lets a site drop the Examine panel entirely: opening the machine and looking inside IS the observation, so it should count as one. The evidence gate on the Diagnose panel keeps working because the evidence is now the learner's own actions rather than a list of buttons.")]
        [SerializeField] private string observationId = "";

        [Tooltip("Seconds this adds to the service call. Walking over and opening a machine up is real work and should cost something.")]
        [Min(0)]
        [SerializeField] private int observationTimeCostSeconds = 30;

        [Header("Events")]
        [Tooltip("Fires the moment the station view appears - after the entry animation, if there is one. Wire the boot screen's Open() here.")]
        public UnityEvent onEnter;

        [Tooltip("Fires the moment the walking view comes back, before anything animates home. Wire the boot screen's Close() here.")]
        public UnityEvent onExit;

        // ==================================================================
        // State
        // ==================================================================

        private State state = State.Outside;
        private bool lockHeld;
        private bool shownFirstEnterDialog;
        private DialogSequence generatedDiagnoseDialog;
        private DialogSequence generatedLockedDialog;

        private Vector3 homePosition;
        private Quaternion homeRotation;
        private bool homeRecorded;

        /// <summary>True from the moment the learner starts entering until the
        /// walking view is fully back.</summary>
        public bool IsInside => state != State.Outside;

        public string StationName => stationName;

        /// <summary>
        /// True while this station refuses to open because the fault has not been
        /// named yet.
        ///
        /// Only during a ticket, and only when the ticket asks for a diagnosis -
        /// ordinary COC1 campaign play in the same scene walks straight in, and so
        /// does a ticket whose TicketData has Requires Diagnosis off.
        /// </summary>
        public bool IsLockedByDiagnosis =>
            requireDiagnosisToEnter && RepairTicketMode.IsActive && !RepairTicketMode.IsDiagnosed;

        // ==================================================================
        // Lifecycle
        // ==================================================================

        /// <summary>A static survives entering Play mode in the Editor, so a station
        /// left Current when the last session ended would block every station on the
        /// next one, with nothing in the Console to say why.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetOnPlay()
        {
            Current = null;
            lastInReachFrame = -10;
        }

        // ==================================================================
        // "Is the learner standing at a station?" - asked by RepairWorkGate
        // ==================================================================

        private static int lastInReachFrame = -10;

        /// <summary>
        /// True while the learner is inside a station OR standing next to one.
        ///
        /// WHY THE SECOND HALF EXISTS
        /// RepairWorkGate uses this to decide whether the diagnose key is live, and
        /// "inside a station" alone is not enough the moment a station can REFUSE to
        /// be entered. A learner standing at a locked case is exactly the person who
        /// needs the diagnosis, and they are, by definition, not inside anything -
        /// so a rule written as "inside" makes the key dead in the one place it
        /// matters most.
        ///
        /// The one-frame tolerance is not sloppiness. RepairWorkGate runs at
        /// execution order -50 and stations run at 0, so on any given frame the gate
        /// reads this BEFORE the stations have written it. Accepting last frame's
        /// answer is what makes the two orders agree; without it the key would work
        /// only on frames where the learner had already been in range the frame
        /// before, which is most of them, and would fail on the first - the press
        /// that follows walking up to the machine.
        /// </summary>
        public static bool PlayerCanReachAStation =>
            Current != null || (Time.frameCount - lastInReachFrame) <= 1;

        private void Awake()
        {
            if (stationPoint == null) stationPoint = transform;

            if (movedObject != null)
            {
                // Recorded in Awake, before anything has had a chance to move it, so
                // "home" always means where the scene was authored - not wherever the
                // object happened to be when the station was first used.
                homePosition = movedObject.position;
                homeRotation = movedObject.rotation;
                homeRecorded = true;
            }

            if (enterButton != null) enterButton.onClick.AddListener(Enter);
            if (exitButton != null) exitButton.onClick.AddListener(Exit);
        }

        private void Start()
        {
            if (player == null)
            {
                var tagged = GameObject.FindGameObjectWithTag("Player");
                if (tagged != null) player = tagged.transform;
            }

            if (player == null)
                Debug.LogWarning($"[RepairStation] '{name}': no player found. Assign Player, or tag your player object \"Player\" - without it the station can only be entered from a button.", this);

            // Says out loud that this station is alive and where it is watching.
            // Without it the failure mode is silence: a station left at the origin
            // and a station whose object is switched off look exactly the same from
            // the Console, and both look like "E does nothing".
            Transform point = stationPoint != null ? stationPoint : transform;
            Debug.Log($"[RepairStation] '{name}' ready - press {interactKey} within {interactRange}m of {point.position}.", this);

            if (stationCamera != null) stationCamera.gameObject.SetActive(false);
            if (enterButton != null) enterButton.gameObject.SetActive(false);
            if (exitButton != null) exitButton.gameObject.SetActive(false);
            if (promptText != null) promptText.text = "";
        }

        private void OnDisable()
        {
            // Never leave the learner frozen because this object was switched off
            // mid-animation. A held input lock with nothing alive to release it stops
            // movement with nothing in the Console to explain it.
            ReleaseLock();
            if (Current == this) Current = null;
        }

        private void Update()
        {
            if (state == State.Outside)
            {
                bool inRange = IsPlayerInRange() && (Current == null || Current == this);
                bool locked = IsLockedByDiagnosis;

                // Told even while locked - especially while locked. This is what
                // keeps the diagnose key alive at a case that will not open.
                if (inRange) lastInReachFrame = Time.frameCount;

                // The button and the key stay LIVE while locked, and the refusal is
                // what they trigger. A greyed-out button teaches nothing: the learner
                // reads it as a bug, walks away, and never finds out there was a rule.
                // Being told why - and then being handed the diagnosis - is the whole
                // lesson, and it only happens if they are allowed to try.
                if (enterButton != null) enterButton.gameObject.SetActive(inRange);

                if (promptText != null)
                {
                    if (!inRange)
                    {
                        promptText.text = "";
                    }
                    else if (locked)
                    {
                        // The prompt names the DIAGNOSE key, not the interact key.
                        // A locked door should tell you where the key is, not repeat
                        // the handle you already tried.
                        KeyCode diagnoseKey = RepairWorkGate.DiagnosisKey;
                        promptText.text = diagnoseKey != KeyCode.None
                            ? $"Press {diagnoseKey} to diagnose - {lockedPrompt}"
                            : lockedPrompt;
                    }
                    else
                    {
                        promptText.text = $"Press {interactKey} to use {stationName}";
                    }
                }

                if (inRange && Input.GetKeyDown(interactKey) && !IsInputTaken()) Enter();
                return;
            }

            if (state == State.Inside)
            {
                if (Input.GetKeyDown(interactKey) && !IsInputTaken()) Exit();
            }
        }

        // ==================================================================
        // Enter / exit
        // ==================================================================

        /// <summary>Wire this to the mobile enter button. Also called by the E key.</summary>
        public void Enter()
        {
            if (state != State.Outside) return;

            // Checked here rather than in Update so the mobile enter button obeys the
            // same rule as the key. The button is wired straight to this method, and
            // a rule enforced only in Update would be a rule mobile players never met.
            if (IsLockedByDiagnosis)
            {
                RefuseEntry();
                return;
            }

            if (Current != null && Current != this)
            {
                Debug.Log($"[RepairStation] '{name}': cannot enter while the learner is at '{Current.name}'.", this);
                return;
            }

            Current = this;
            AcquireLock();
            state = State.Entering;

            if (promptText != null) promptText.text = "";
            if (enterButton != null) enterButton.gameObject.SetActive(false);

            StartCoroutine(EnterSequence());
        }

        /// <summary>
        /// What happens when the learner tries to open a case they have not
        /// diagnosed.
        ///
        /// It explains, every time, and it does NOT open the diagnosis itself. The
        /// diagnose key is the one way in, so the refusal's whole job is to say what
        /// that key is - a door that silently did the thing the key does would teach
        /// the learner nothing about how to reach it next time.
        ///
        /// Repeated presses cannot spam the dialog: IsInputTaken() ignores the
        /// interact key while a dialog is on screen, so the learner has to read and
        /// dismiss this one before they can ask again.
        /// </summary>
        private void RefuseEntry()
        {
            Debug.Log($"[RepairStation] '{name}' is shut - the fault has not been diagnosed yet. " +
                      $"Press {RepairWorkGate.DiagnosisKey} to diagnose.", this);


            if (DialogManager.Instance == null) return;

            if (lockedUntilDiagnosedDialog != null)
            {
                DialogManager.Instance.ShowDialog(lockedUntilDiagnosedDialog);
                return;
            }

            if (generatedLockedDialog == null)
                generatedLockedDialog = ScriptableObject.CreateInstance<DialogSequence>();

            KeyCode diagnoseKey = RepairWorkGate.DiagnosisKey;
            string howTo = diagnoseKey != KeyCode.None
                ? $"Press {diagnoseKey} and name the cause first, then I can open it up and prove myself right."
                : "Open the diagnosis and name the cause first, then I can open it up and prove myself right.";

            generatedLockedDialog.lines.Clear();
            generatedLockedDialog.lines.Add(new DialogLine
            {
                speakerName = "You",
                text = "Not yet. If I open it up now I will just see the fault and call it - " +
                       "and that is not diagnosing, that is reading. " + howTo
            });

            DialogManager.Instance.ShowDialog(generatedLockedDialog);
        }

        /// <summary>Wire this to the exit button. Also called by pressing E again.</summary>
        public void Exit()
        {
            if (state != State.Inside) return;

            if (blockExitWhileHoldingPart && DragController.Instance != null && DragController.Instance.IsDragging)
            {
                Debug.Log("[RepairStation] Put the part down before stepping away.", this);
                return;
            }

            state = State.Leaving;

            // The view changes first and the object animates home behind it, the
            // mirror of entering: the learner watches the tower come back upright
            // from where they are standing.
            ShowStationView(false);
            onExit?.Invoke();

            StartCoroutine(ExitSequence());
        }

        public void Toggle()
        {
            if (state == State.Outside) Enter();
            else if (state == State.Inside) Exit();
        }

        private IEnumerator EnterSequence()
        {
            // Watched from the walking camera. Cutting to the station camera first
            // and animating behind it would waste the one moment that sells the
            // whole interaction.
            yield return AnimateTo(EnteredPosition(), EnteredRotation());

            state = State.Inside;
            ShowStationView(true);

            RecordAsObservation();

            Debug.Log($"[RepairStation] Entered '{name}'.", this);
            onEnter?.Invoke();

            ShowEntryDialog();
        }

        /// <summary>
        /// Counts arriving at this station as an observation.
        ///
        /// WHY A STATION CAN BE AN OBSERVATION
        /// The Examine panel exists to let a learner gather evidence before naming
        /// a cause. Once the site has a machine you can actually walk up to, power
        /// on, and open, that panel is describing in text what the learner could
        /// just go and look at - and a list of five buttons is a poorer teacher
        /// than the thing itself. So the physical acts become the evidence:
        /// pressing POWER is one observation, opening the system unit is another,
        /// and the Diagnose gate is satisfied by having done the job rather than
        /// by having clicked through a menu.
        ///
        /// Repeating it costs nothing - RecordObservation returns false for an id
        /// already seen - so walking in and out of the case does not inflate the
        /// count or the clock.
        /// </summary>
        private void RecordAsObservation()
        {
            if (string.IsNullOrEmpty(observationId)) return;
            if (!RepairTicketMode.IsActive) return;

            ServiceCallState.EnsureTicket(RepairTicketMode.ActiveTicketId);

            if (ServiceCallState.RecordObservation(observationId, observationTimeCostSeconds))
            {
                Debug.Log($"[RepairStation] '{name}' recorded observation '{observationId}'. " +
                          $"{ServiceCallState.ObservationCount} observation(s) so far.", this);
            }
        }

        /// <summary>
        /// The one-per-visit line shown on arrival.
        ///
        /// An undiagnosed machine gets the nudge instead of the ordinary welcome,
        /// because the ordinary welcome is not the thing the learner needs to hear
        /// standing over an open case with a stick of RAM in reach. It is shown
        /// once, not every entry - a nag that repeats is a nag that gets skipped
        /// without reading.
        /// </summary>
        private void ShowEntryDialog()
        {
            if (DialogManager.Instance == null) return;
            if (shownFirstEnterDialog) return;

            bool undiagnosed = nagWhenUndiagnosed && RepairTicketMode.IsActive && !RepairTicketMode.IsDiagnosed;

            if (undiagnosed)
            {
                shownFirstEnterDialog = true;

                if (diagnoseFirstDialog != null)
                {
                    DialogManager.Instance.ShowDialog(diagnoseFirstDialog);
                    return;
                }

                if (generatedDiagnoseDialog == null)
                    generatedDiagnoseDialog = ScriptableObject.CreateInstance<DialogSequence>();

                generatedDiagnoseDialog.lines.Clear();
                generatedDiagnoseDialog.lines.Add(new DialogLine
                {
                    speakerName = "You",
                    text = "Right - the case is open and I can see inside. Before I touch anything, " +
                           "I need to say what I think is actually wrong. Open the diagnosis and name the cause."
                });

                DialogManager.Instance.ShowDialog(generatedDiagnoseDialog);
                return;
            }

            if (firstEnterDialog != null)
            {
                shownFirstEnterDialog = true;
                DialogManager.Instance.ShowDialog(firstEnterDialog);
            }
        }

        private IEnumerator ExitSequence()
        {
            yield return AnimateTo(homePosition, homeRotation);

            state = State.Outside;
            ReleaseLock();
            if (Current == this) Current = null;

            Debug.Log($"[RepairStation] Left '{name}'.", this);
        }

        private IEnumerator AnimateTo(Vector3 targetPosition, Quaternion targetRotation)
        {
            if (movedObject == null || !homeRecorded) yield break;

            // Reduce Motion turns this into a cut. The pose still changes - the
            // tower still ends up on its side - it just gets there this frame.
            float duration = moveSeconds * CircuitCrafter.Settings.GameSettings.MotionScale;
            if (duration <= 0f)
            {
                movedObject.SetPositionAndRotation(targetPosition, targetRotation);
                yield break;
            }

            Vector3 fromPosition = movedObject.position;
            Quaternion fromRotation = movedObject.rotation;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                // Eased rather than linear. A tower tipping over at a constant speed
                // and stopping dead looks like a spreadsheet; this looks like weight.
                t = t * t * (3f - 2f * t);

                movedObject.SetPositionAndRotation(
                    Vector3.Lerp(fromPosition, targetPosition, t),
                    Quaternion.Slerp(fromRotation, targetRotation, t));

                yield return null;
            }

            movedObject.SetPositionAndRotation(targetPosition, targetRotation);
        }

        // ==================================================================
        // View
        // ==================================================================

        /// <summary>The camera swap. Same contract as StationCameraSwitch.ToggleCamera,
        /// deliberately - the cameras and arrays you already had wired there can be
        /// dragged straight across.</summary>
        private void ShowStationView(bool inside)
        {
            if (playerCamera != null) playerCamera.gameObject.SetActive(!inside);
            if (stationCamera != null) stationCamera.gameObject.SetActive(inside);

            Cursor.lockState = inside ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = inside;

            if (enableOnEnter != null)
                foreach (var go in enableOnEnter) if (go != null) go.SetActive(inside);

            if (disableOnEnter != null)
                foreach (var go in disableOnEnter) if (go != null) go.SetActive(!inside);

            if (exitButton != null) exitButton.gameObject.SetActive(inside);
            if (promptText != null) promptText.text = inside ? $"Press {interactKey} to step back" : "";
        }

        // ==================================================================
        // Small helpers
        // ==================================================================

        private Vector3 EnteredPosition()
        {
            if (enteredPose != null) return enteredPose.position;
            return homePosition + enteredOffset;
        }

        private Quaternion EnteredRotation()
        {
            if (enteredPose != null) return enteredPose.rotation;
            return homeRotation * Quaternion.Euler(enteredEuler);
        }

        private bool IsPlayerInRange()
        {
            if (player == null || stationPoint == null) return false;
            return Vector3.Distance(player.position, stationPoint.position) <= interactRange;
        }

        /// <summary>True when something else owns the keyboard this frame: a dialog
        /// is up, or the diagnosis panel is bound to the same key and has it. Without
        /// the second half, binding both to E would open the diagnosis and walk the
        /// learner out of the station on one press.</summary>
        private static bool IsInputTaken()
        {
            if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return true;
            return TicketDiagnosisPanel.BlocksStationInput;
        }

        private void AcquireLock()
        {
            if (lockHeld) return;
            lockHeld = true;
            PlayerInputLock.Acquire();
        }

        private void ReleaseLock()
        {
            if (!lockHeld) return;
            lockHeld = false;
            PlayerInputLock.Release();
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Transform point = stationPoint != null ? stationPoint : transform;

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(point.position, interactRange);

            if (movedObject != null && enteredPose != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(movedObject.position, enteredPose.position);
                Gizmos.DrawWireCube(enteredPose.position, Vector3.one * 0.25f);
            }
        }
#endif
    }
}