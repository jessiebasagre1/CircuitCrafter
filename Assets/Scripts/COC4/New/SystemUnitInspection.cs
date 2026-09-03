using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// Lays the system unit down on its side so the learner can work inside it,
    /// and stands it back up when they are done.
    ///
    /// ATTACH THIS TO: an empty GameObject in each repair-site scene - the
    /// Managers object is fine. It is not attached to the system unit itself,
    /// because it moves the system unit, and a component that animates its own
    /// transform is much harder to reason about when something goes wrong.
    ///
    /// WHY IT IS NOT JUST A CAMERA CUT
    /// StationCameraSwitch already snaps between a walking camera and a station
    /// camera, and for a workbench that is exactly right. A tower on the floor is
    /// different: nobody works on a standing tower. Laying it over is a real step
    /// of the job, it is the moment the learner commits to opening the machine up,
    /// and seeing it happen is what makes the repair feel like handling hardware
    /// rather than opening a menu. It also gives the closing step somewhere to
    /// live - standing the unit back up before testing is a step technicians skip
    /// and get marked down for, and now the game can notice.
    ///
    /// IT REPLACES StationCameraSwitch ON THIS ONE OBJECT, rather than driving
    /// it. The two of them taking turns with the same cameras is a race waiting
    /// to happen, so this owns the swap outright and keeps the same contract:
    /// the same camera fields, the same enable/disable arrays, the same cursor
    /// behaviour. Existing wiring moves across unchanged.
    ///
    /// TWO WAYS TO POSE IT. Assign Laid Down Pose - a marker object you position
    /// by hand - and the unit moves to exactly that. It is the reliable option
    /// and the one to use. The Euler/offset fields below it are a fallback for
    /// when you cannot be bothered to make a marker; they guess, and a guess
    /// about which way a model lies down is usually wrong the first time.
    /// </summary>
    public class SystemUnitInspection : MonoBehaviour
    {
        private enum State { Closed, Opening, Open, Closing }

        // ==================================================================
        // Inspector
        // ==================================================================

        [Header("The system unit")]
        [Tooltip("The object that gets laid down. Use the ROOT of the machine - the case AND everything mounted inside it - so the parts travel with it. If your motherboard, RAM and drives are not children of the case, parent them to an empty object and assign that instead, or they will stay standing while the case tips over.\n\nLeave empty to skip the animation entirely and just switch cameras.")]
        [SerializeField] private Transform systemUnit;

        [Tooltip("RECOMMENDED. An empty marker object placed where the unit should end up, posed by hand in the Scene view. Duplicate the system unit, rotate the copy onto its side, drag an empty to that position, then delete the copy.")]
        [SerializeField] private Transform laidDownPose;

        [Tooltip("Fallback, used only when Laid Down Pose is empty. Rotation applied to the unit, relative to how it stands now. Which axis tips a tower onto its side depends on how the model was built - try 90 and -90 on Z, then on X, and watch it in Play mode.")]
        [SerializeField] private Vector3 laidDownEuler = new Vector3(0f, 0f, -90f);

        [Tooltip("Fallback, used only when Laid Down Pose is empty. World-space movement applied as it tips - usually a small drop, since a tower on its side sits lower than one standing up.")]
        [SerializeField] private Vector3 laidDownOffset = new Vector3(0f, -0.3f, 0f);

        [Tooltip("How long the tip-over takes. Long enough to read as a deliberate action, short enough not to become annoying by the fifth ticket.")]
        [Min(0.05f)]
        [SerializeField] private float tiltSeconds = 0.9f;

        [Header("Cameras")]
        [Tooltip("The walking camera. Disabled while the unit is open.")]
        [SerializeField] private Camera playerCamera;

        [Tooltip("The close-up camera looking down at the laid-open unit. This is the equivalent of the assembly station camera in COC1 - point it at where the unit ENDS UP, not where it stands now.")]
        [SerializeField] private Camera inspectionCamera;

        [Tooltip("Turned on while the unit is open - the hotbar, the part description panel, the close button.")]
        [SerializeField] private GameObject[] enableWhileOpen;

        [Tooltip("Turned off while the unit is open - mobile movement controls, the waypoint arrow, the walk-up prompt.")]
        [SerializeField] private GameObject[] disableWhileOpen;

        [Header("Walking up to it")]
        [Tooltip("How close the player has to be before they can open it.")]
        [Min(0.5f)]
        [SerializeField] private float interactRange = 3f;

        [Tooltip("Found automatically by the Player tag if left empty.")]
        [SerializeField] private Transform player;

        [SerializeField] private KeyCode interactKey = KeyCode.E;

        [Tooltip("Optional. \"Press E to open the system unit\".")]
        [SerializeField] private TMP_Text promptText;

        [Tooltip("Optional, for mobile. Shown only while the player is close enough and the unit is shut. Wire its OnClick to Open().")]
        [SerializeField] private Button openButton;

        [Tooltip("Optional. Shown only while the unit is open. Wire its OnClick to Close().")]
        [SerializeField] private Button closeButton;

        [Header("Dialogs (optional)")]
        [Tooltip("Shown the first time the unit is opened this visit.")]
        [SerializeField] private DialogSequence firstOpenDialog;

        [Header("Events")]
        public UnityEvent onOpened;
        public UnityEvent onClosed;

        // ==================================================================
        // State
        // ==================================================================

        private State state = State.Closed;
        private bool lockHeld;
        private bool shownFirstOpenDialog;

        private Vector3 uprightPosition;
        private Quaternion uprightRotation;
        private bool poseRecorded;

        /// <summary>True from the moment it starts tipping over until it is standing
        /// upright again. PCPowerStation reads this to refuse a power-on while the
        /// machine is still open.</summary>
        public bool IsOpen => state != State.Closed;

        // ==================================================================
        // Lifecycle
        // ==================================================================

        private void Awake()
        {
            if (systemUnit != null)
            {
                // Recorded in Awake, before anything has had a chance to move it, so
                // "upright" always means where the scene was authored - not wherever
                // the unit happened to be sitting when it was first opened.
                uprightPosition = systemUnit.position;
                uprightRotation = systemUnit.rotation;
                poseRecorded = true;
            }

            if (openButton != null) openButton.onClick.AddListener(Open);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
        }

        private void Start()
        {
            if (player == null)
            {
                var tagged = GameObject.FindGameObjectWithTag("Player");
                if (tagged != null) player = tagged.transform;
            }

            if (player == null)
                Debug.LogWarning("[SystemUnitInspection] No player found. Assign Player, or tag your player object \"Player\" - without it the unit can only be opened from a button.", this);

            if (inspectionCamera != null) inspectionCamera.gameObject.SetActive(false);
            if (closeButton != null) closeButton.gameObject.SetActive(false);
            if (openButton != null) openButton.gameObject.SetActive(false);
            if (promptText != null) promptText.text = "";
        }

        private void OnDisable()
        {
            // Never leave the player frozen because this object was switched off
            // mid-animation. A held input lock with nothing left alive to release it
            // is the worst kind of bug: movement simply stops, with nothing in the
            // Console to say why.
            ReleaseLock();
        }

        private void Update()
        {
            bool inRange = IsPlayerInRange();

            if (state == State.Closed)
            {
                if (openButton != null) openButton.gameObject.SetActive(inRange);
                if (promptText != null)
                    promptText.text = inRange ? $"Press {interactKey} to open the system unit" : "";

                if (inRange && Input.GetKeyDown(interactKey) && !IsDialogBlocking()) Open();
                return;
            }

            if (state == State.Open)
            {
                if (Input.GetKeyDown(interactKey) && !IsDialogBlocking()) Close();
            }
        }

        // ==================================================================
        // Open / close
        // ==================================================================

        /// <summary>Wire this to the InteractableObject on the system unit, and to
        /// the mobile open button.</summary>
        public void Open()
        {
            if (state != State.Closed) return;

            AcquireLock();
            state = State.Opening;

            if (promptText != null) promptText.text = "";
            if (openButton != null) openButton.gameObject.SetActive(false);

            StartCoroutine(OpenSequence());
        }

        /// <summary>Wire this to the close button.</summary>
        public void Close()
        {
            if (state != State.Open) return;

            // Standing the case up with a part still in the learner's hand would
            // drop it somewhere unrecoverable, and there is no honest place for it
            // to land. Refusing is kinder than guessing.
            if (DragController.Instance != null && DragController.Instance.IsDragging)
            {
                Debug.Log("[SystemUnitInspection] Put the part down before standing the unit back up.", this);
                return;
            }

            state = State.Closing;
            ShowInspectionView(false);
            StartCoroutine(CloseSequence());
        }

        public void Toggle()
        {
            if (state == State.Closed) Open();
            else if (state == State.Open) Close();
        }

        private IEnumerator OpenSequence()
        {
            // The tilt is watched from the walking camera. Cutting to the close-up
            // first and animating behind it would waste the one moment that sells
            // the whole interaction.
            yield return AnimateTo(LaidDownPosition(), LaidDownRotation());

            state = State.Open;
            ShowInspectionView(true);

            if (!shownFirstOpenDialog && firstOpenDialog != null && DialogManager.Instance != null)
            {
                shownFirstOpenDialog = true;
                DialogManager.Instance.ShowDialog(firstOpenDialog);
            }

            Debug.Log("[SystemUnitInspection] System unit laid open.", this);
            onOpened?.Invoke();
        }

        private IEnumerator CloseSequence()
        {
            // Cut back to the walking camera FIRST here, the mirror of opening: the
            // learner watches it come back upright from where they are standing.
            yield return AnimateTo(uprightPosition, uprightRotation);

            state = State.Closed;
            ReleaseLock();

            Debug.Log("[SystemUnitInspection] System unit closed up and standing.", this);
            onClosed?.Invoke();
        }

        private IEnumerator AnimateTo(Vector3 targetPosition, Quaternion targetRotation)
        {
            if (systemUnit == null || !poseRecorded) yield break;

            Vector3 fromPosition = systemUnit.position;
            Quaternion fromRotation = systemUnit.rotation;

            float elapsed = 0f;
            while (elapsed < tiltSeconds)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / tiltSeconds);

                // Eased rather than linear. A tower tipping over at a constant speed
                // and stopping dead looks like a spreadsheet; this looks like weight.
                t = t * t * (3f - 2f * t);

                systemUnit.SetPositionAndRotation(
                    Vector3.Lerp(fromPosition, targetPosition, t),
                    Quaternion.Slerp(fromRotation, targetRotation, t));

                yield return null;
            }

            systemUnit.SetPositionAndRotation(targetPosition, targetRotation);
        }

        // ==================================================================
        // View
        // ==================================================================

        /// <summary>The camera swap. Same contract as StationCameraSwitch.ToggleCamera,
        /// deliberately - so the arrays and cameras you already had wired there can be
        /// dragged straight across.</summary>
        private void ShowInspectionView(bool inspecting)
        {
            if (playerCamera != null) playerCamera.gameObject.SetActive(!inspecting);
            if (inspectionCamera != null) inspectionCamera.gameObject.SetActive(inspecting);

            Cursor.lockState = inspecting ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = inspecting;

            if (enableWhileOpen != null)
                foreach (var go in enableWhileOpen) if (go != null) go.SetActive(inspecting);

            if (disableWhileOpen != null)
                foreach (var go in disableWhileOpen) if (go != null) go.SetActive(!inspecting);

            if (closeButton != null) closeButton.gameObject.SetActive(inspecting);
            if (promptText != null) promptText.text = inspecting ? $"Press {interactKey} to close it up" : "";
        }

        // ==================================================================
        // Small helpers
        // ==================================================================

        private Vector3 LaidDownPosition()
        {
            if (laidDownPose != null) return laidDownPose.position;
            return uprightPosition + laidDownOffset;
        }

        private Quaternion LaidDownRotation()
        {
            if (laidDownPose != null) return laidDownPose.rotation;
            return uprightRotation * Quaternion.Euler(laidDownEuler);
        }

        private bool IsPlayerInRange()
        {
            if (player == null || systemUnit == null) return false;
            return Vector3.Distance(player.position, systemUnit.position) <= interactRange;
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
            if (systemUnit == null) return;

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(systemUnit.position, interactRange);

            if (laidDownPose != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(systemUnit.position, laidDownPose.position);
                Gizmos.DrawWireCube(laidDownPose.position, Vector3.one * 0.25f);
            }
        }
#endif
    }
}
