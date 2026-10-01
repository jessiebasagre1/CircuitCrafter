using UnityEngine;
using CircuitCrafter.Assessment;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// Stops the learner touching the hardware until they have named the fault.
    ///
    /// ATTACH THIS TO: the Managers object in each repair-site scene. One per
    /// scene. It needs no references - assign the dialog if you want your own
    /// wording, and nothing else.
    ///
    /// WHY THE RULE MOVED FROM "NOT CREDITED" TO "NOT ALLOWED"
    /// TicketFaultCoordinator used to let the repair happen and simply refuse to
    /// count it. That is defensible - a technician CAN reach for a screwdriver
    /// early - but in practice it taught the wrong thing twice over: the learner
    /// saw the machine start working, believed the job was done, and only found
    /// out at the hub that the ticket was still open. The feedback arrived long
    /// after the decision that caused it, which is the worst possible place for
    /// feedback to arrive.
    ///
    /// Refusing at the moment they reach for the part puts the message exactly
    /// where the mistake is, and the mistake is still recorded - so the report
    /// still shows who reached for the hardware before thinking.
    ///
    /// IT ALSO OWNS THE DIAGNOSE SHORTCUT KEY, and that is not an arbitrary
    /// choice. TicketDiagnosisPanel cannot read the key itself: opening it hides
    /// its own GameObject, and Unity does not run Update on a component whose
    /// object is switched off - so a key polled from there would work exactly
    /// once, to CLOSE the panel, and could never open it. This component lives on
    /// Managers, which is never deactivated, so it can always hear the key. It is
    /// also the right owner in meaning: the gate is what says "diagnose first", so
    /// it should be what offers the way to.
    ///
    /// [DefaultExecutionOrder(-50)] puts this ahead of RepairStation. Unity does
    /// not define the order of Update between components, and if both are bound to
    /// the same key it would be a coin toss each frame whether the diagnosis opened
    /// or the learner walked out of the station.
    ///
    /// IT ONLY BITES DURING A TICKET. Outside RepairTicketMode this is inert, so
    /// normal COC1 campaign play in the same scene is untouched. A ticket whose
    /// TicketData has Requires Diagnosis off is diagnosed from the start, so it
    /// is unaffected too.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class RepairWorkGate : MonoBehaviour
    {
        public static RepairWorkGate Instance { get; private set; }

        [Tooltip("Shown when the learner tries to fix something before diagnosing. Leave empty and one is written for you.")]
        [SerializeField] private DialogSequence diagnoseFirstDialog;

        [Tooltip("Seconds before the dialog can pop again. Without this, a learner tapping at a stuck part gets the same popup five times in two seconds.")]
        [Min(0f)]
        [SerializeField] private float dialogCooldownSeconds = 8f;

        [Tooltip("An AssessmentRecorder stage id. Leave blank to record nothing - the refusal is still logged.")]
        [SerializeField] private string stageId = "";

        [Header("Diagnose shortcut")]
        [Tooltip("The key that opens the diagnosis, and closes it again. Set it to None to turn the shortcut off and use the toolbar button only.\n\nWHY NOT E BY DEFAULT: RepairStation already uses E to enter and leave a station. Bind both to E and a press inside the case could only ever open and close this panel - the learner would never step back out. Nothing double-fires, but one of the two actions becomes unreachable.\n\nIf you want E for diagnosis, set this to E AND change the system unit station's Interact Key to something else, so each key keeps one meaning.")]
        [SerializeField] private KeyCode openDiagnosisKey = KeyCode.F;

        [Tooltip("OFF (the default) means the diagnosis opens anywhere on the site during a ticket. That is the honest model: a technician can think about a machine from across the room, and the panel is a notebook, not a tool bolted to the bench.\n\nON restricts it to standing at or beside a station. \"Beside\" matters if you turn it on: a system unit with Require Diagnosis To Enter ticked cannot be entered until the diagnosis is done, so a learner standing at it is never INSIDE anything - counting only the inside would make the key dead at the exact machine it is meant to be pressed at.")]
        [SerializeField] private bool onlyAtAStation = false;

        [Tooltip("The panel to open. Found automatically - including while it is switched off - if left empty.")]
        [SerializeField] private TicketDiagnosisPanel diagnosisPanel;

        private DialogSequence generatedDialog;
        private float nextDialogTime;
        private bool recordedMistake;

        /// <summary>The key that opens the diagnosis, or None. Read by RepairStation
        /// so a locked case can name it in the prompt and the refusal dialog - the
        /// key is configured in one place and quoted from that one place, so the two
        /// can never drift apart.</summary>
        public static KeyCode DiagnosisKey =>
            Instance != null ? Instance.openDiagnosisKey : KeyCode.None;

        /// <summary>True while the learner must diagnose before touching anything.</summary>
        public static bool IsBlocked =>
            RepairTicketMode.IsActive && !RepairTicketMode.IsDiagnosed;

        // ==================================================================

        private void Awake()
        {
            // Destroy(this), not Destroy(gameObject) - the Managers object this
            // sits on carries half the scene's systems, and taking the whole
            // object out to resolve a duplicate component would silently remove
            // all of them.
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[RepairWorkGate] Duplicate on '{name}' - one already exists on '{Instance.name}'. Removing this component only.", this);
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            // Include inactive: the panel hides itself in Start, so by the time
            // anyone looks for it, it is switched off. A plain search would find
            // nothing and the key would silently do nothing forever.
            if (diagnosisPanel == null)
                diagnosisPanel = FindAnyObjectByType<TicketDiagnosisPanel>(FindObjectsInactive.Include);

            if (diagnosisPanel == null && openDiagnosisKey != KeyCode.None)
                Debug.LogWarning($"[RepairWorkGate] No TicketDiagnosisPanel in this scene, so '{openDiagnosisKey}' will do nothing. Assign Diagnosis Panel, or set the key to None.", this);
        }

        /// <summary>
        /// The key is read FIRST and every refusal after it says why in the Console.
        ///
        /// The old order tested the conditions first and returned before ever
        /// looking at the key, which meant a press that went nowhere left no trace
        /// at all - and "F does nothing" with a silent Console is the single most
        /// expensive kind of bug to chase in a scene you did not write today.
        /// </summary>
        private void Update()
        {
            if (openDiagnosisKey == KeyCode.None) return;
            if (!Input.GetKeyDown(openDiagnosisKey)) return;

            if (diagnosisPanel == null)
            {
                Debug.LogWarning($"[RepairWorkGate] '{openDiagnosisKey}' pressed, but no TicketDiagnosisPanel is assigned or could be found. Assign Diagnosis Panel in the Inspector.", this);
                return;
            }

            if (!RepairTicketMode.IsActive)
            {
                Debug.Log($"[RepairWorkGate] '{openDiagnosisKey}' ignored - there is no active ticket. Take one from the hub rather than launching this scene directly.", this);
                return;
            }

            // A dialog owns the keyboard while it is up - the same check every
            // other input path in this project makes.
            if (DialogManager.Instance != null && DialogManager.Instance.IsDialogOpen) return;

            bool isOpen = diagnosisPanel.IsOpen;

            // The "at a station" rule applies to OPENING only. Once the panel is
            // up it must always be closable, or a learner who wandered out of range
            // with it open would be stuck looking at it.
            if (!isOpen && onlyAtAStation && !RepairStation.PlayerCanReachAStation)
            {
                Debug.Log($"[RepairWorkGate] '{openDiagnosisKey}' ignored - the learner is not at or beside a station. Walk up to the monitor or the system unit, or turn Only At A Station off to allow it anywhere.", this);
                return;
            }

            TicketDiagnosisPanel.NoteKeyConsumed();

            if (isOpen) diagnosisPanel.Close();
            else diagnosisPanel.Open();
        }

        // ==================================================================

        /// <summary>
        /// The one call site pattern: <c>if (RepairWorkGate.Refuse("the RAM")) return;</c>
        ///
        /// Returns TRUE when the action must not happen - which reads the right way
        /// round at a call site, where the interesting case is the refusal.
        /// </summary>
        /// <summary>
        /// The part-aware overload. A CLEANING TOOL is exempt: picking up a brush
        /// is not a repair, and refusing it would leave the learner unable to even
        /// hold the thing the ticket is about. Using it on the dirt IS a repair,
        /// and DustScrubber calls the plain Refuse for that.
        /// </summary>
        public static bool Refuse(PCPartItem item)
        {
            if (CleaningTool.IsTool(item)) return false;
            return Refuse(item != null ? $"'{item.name}'" : "that part");
        }

        /// <summary>
        /// Opens the diagnosis from somewhere else - a locked station, a button, a
        /// tutorial step. Returns false if there is no panel to open.
        ///
        /// It lives here because this component already holds the only reference
        /// that survives the panel switching its own GameObject off, and because a
        /// second FindAnyObjectByType somewhere else would be a second thing to get
        /// wrong the day that panel is renamed.
        /// </summary>
        public static bool TryOpenDiagnosis()
        {
            if (Instance == null || Instance.diagnosisPanel == null) return false;
            if (Instance.diagnosisPanel.IsOpen) return true;

            // Tells RepairStation's input check that the panel owns the keyboard this
            // frame. Without it, a press that opened the panel could also be read as
            // a press at the station behind it.
            TicketDiagnosisPanel.NoteKeyConsumed();

            Instance.diagnosisPanel.Open();
            return true;
        }

        public static bool Refuse(string what)
        {
            if (!IsBlocked) return false;

            if (Instance != null) Instance.Explain(what);
            else Debug.Log($"[RepairWorkGate] Blocked: {what} - the fault has not been diagnosed yet. (No RepairWorkGate in the scene, so the learner saw nothing.)");

            return true;
        }

        private void Explain(string what)
        {
            Debug.Log($"[RepairWorkGate] Blocked an attempt to move {what} - run the diagnosis first.", this);

            // Recorded once per visit, not once per attempt. Five taps at a locked
            // part is one decision made five times, not five separate errors, and
            // counting them all would swamp everything else in the report.
            // In the COC4 assessment it is recorded whether or not a Stage Id is set: the run has
            // no stage ids of its own, and this was being dropped. Same wording as
            // TicketFaultCoordinator, so the one habit is charged once per ticket, not twice.
            if (!recordedMistake && (!string.IsNullOrEmpty(stageId) || COC4AssessmentRun.IsActive))
            {
                recordedMistake = true;
                AssessmentRecorder.Mistake("procedure", COC4AssessmentRun.RepairBeforeDiagnosisText,
                                           COC4AssessmentRun.RepairBeforeDiagnosisCorrection);
            }

            if (Time.time < nextDialogTime) return;
            nextDialogTime = Time.time + dialogCooldownSeconds;

            if (DialogManager.Instance == null) return;

            if (diagnoseFirstDialog != null)
            {
                DialogManager.Instance.ShowDialog(diagnoseFirstDialog);
                return;
            }

            // Built once and reused - a DialogSequence created per refusal would
            // leak a ScriptableObject every time the learner tapped a part.
            if (generatedDialog == null)
                generatedDialog = ScriptableObject.CreateInstance<DialogSequence>();

            generatedDialog.lines.Clear();
            generatedDialog.lines.Add(new DialogLine
            {
                speakerName = "You",
                text = "Not yet. I have not worked out what is actually wrong with this machine - " +
                       "if I start swapping parts now I am guessing, not repairing. " +
                       "Open the diagnosis and name the cause first."
            });

            DialogManager.Instance.ShowDialog(generatedDialog);
        }
    }
}