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

        [Tooltip("Shown the first time this station is entered on this visit. Optional.")]
        [SerializeField] private DialogSequence firstEnterDialog;

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

        private Vector3 homePosition;
        private Quaternion homeRotation;
        private bool homeRecorded;

        /// <summary>True from the moment the learner starts entering until the
        /// walking view is fully back.</summary>
        public bool IsInside => state != State.Outside;

        public string StationName => stationName;

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
        }

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
                bool available = IsPlayerInRange() && (Current == null || Current == this);

                if (enterButton != null) enterButton.gameObject.SetActive(available);
                if (promptText != null)
                    promptText.text = available ? $"Press {interactKey} to use {stationName}" : "";

                if (available && Input.GetKeyDown(interactKey) && !IsDialogBlocking()) Enter();
                return;
            }

            if (state == State.Inside)
            {
                if (Input.GetKeyDown(interactKey) && !IsDialogBlocking()) Exit();
            }
        }

        // ==================================================================
        // Enter / exit
        // ==================================================================

        /// <summary>Wire this to the mobile enter button. Also called by the E key.</summary>
        public void Enter()
        {
            if (state != State.Outside) return;

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

            Debug.Log($"[RepairStation] Entered '{name}'.", this);
            onEnter?.Invoke();

            if (!shownFirstEnterDialog && firstEnterDialog != null && DialogManager.Instance != null)
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

            Vector3 fromPosition = movedObject.position;
            Quaternion fromRotation = movedObject.rotation;

            float elapsed = 0f;
            while (elapsed < moveSeconds)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / moveSeconds);

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

        private static bool IsDialogBlocking()
        {
            return DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen;
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
