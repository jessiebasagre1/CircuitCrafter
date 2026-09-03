using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;
using CircuitCrafter.Assessment;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// The monitor at the repair site. The learner walks up, presses POWER, and
    /// the machine tells them - by behaving - what is wrong with it.
    ///
    /// ATTACH THIS TO: the boot-screen Canvas panel at each repair site. One per
    /// site. Menu: CircuitCrafter > Build PC Boot Screen builds and wires the
    /// whole thing for you.
    ///
    /// IT DOES NOT LISTEN FOR THE LEARNER ITSELF. Walking up to the monitor and
    /// pressing E is a RepairStation's job; this panel is what that station turns
    /// on. Open() and Close() are wired to the station's On Enter and On Exit, so
    /// the two stations at a site behave identically and there is one place - not
    /// two - that decides what "walk up and use it" feels like.
    ///
    /// WHY THIS EXISTS
    /// Up to now a service call opened with a text box telling the learner what
    /// was wrong. That is a briefing, not a symptom, and it skips the first and
    /// most important thing a technician does: confirm the reported fault with
    /// their own eyes. A customer says "it won't turn on"; sometimes it turns on
    /// perfectly and the monitor is off. You find that out by pressing the button.
    ///
    /// SO IT SHOWS THE SYMPTOM, NEVER THE CAUSE.
    /// A failed boot prints what the machine DOES - fans spinning, a black
    /// screen, three beeps - taken from each fault's Boot Symptom field. It never
    /// prints the fault's name, because the whole exercise is turning a symptom
    /// into a cause. The named, per-check PASS/FAIL list only ever appears on a
    /// boot that succeeds, where there is nothing left to give away.
    ///
    /// AND IT ASKS THE FAULTS LIVE.
    /// Every check calls TicketFault.IsRepaired() fresh, at the moment POWER is
    /// pressed. Nothing is read from a stored result, so a learner who fixed
    /// something, got credited, then pulled the part back out will fail the test -
    /// correctly.
    ///
    /// THE SAME BUTTON IS BOTH THE FIRST LOOK AND THE FINAL TEST.
    /// It does not need a mode switch, because which one it is can be worked out
    /// rather than configured: a boot that passes is a successful test; a boot
    /// that fails after the learner has already repaired something is a failed
    /// test; a boot that fails before they have touched anything is the symptom
    /// they came to see, and is recorded as an observation instead. See Finish().
    /// </summary>
    public class PCPowerStation : MonoBehaviour
    {
        // ==================================================================
        // Inspector
        // ==================================================================

        [Header("Monitor UI")]
        [Tooltip("The whole boot-screen panel. Usually this same object.")]
        [SerializeField] private GameObject panelRoot;

        [Tooltip("Where the boot text is printed. Use a monospace font - the output is laid out in columns. Always assign this.")]
        [SerializeField] private TMP_Text screenText;

        [Tooltip("The on-screen POWER button.")]
        [SerializeField] private Button powerButton;

        [Tooltip("Optional. A second way to leave, for mobile. Pressing E does the same thing.")]
        [SerializeField] private Button closeButton;

        [Tooltip("The station this screen belongs to. Assigned so the Step back button leaves the station properly - hiding the panel alone would leave the learner staring at the monitor camera with nothing on it.")]
        [SerializeField] private RepairStation monitorStation;

        [Tooltip("Optional. A black Image covering the screen while the machine is off, so the monitor looks dead rather than blank. Same trick OSInstallView uses with its reboot overlay.")]
        [SerializeField] private GameObject screenOffOverlay;

        [Header("Site")]
        [Tooltip("The coordinator for this site. Found automatically if left empty.")]
        [SerializeField] private TicketFaultCoordinator coordinator;

        [Tooltip("Optional. The system unit station. The machine refuses to power on while the learner is still inside it with the case laid open - normally impossible, since leaving that station stands the tower back up, but worth keeping as a backstop.")]
        [SerializeField] private RepairStation systemUnitStation;

        [Header("Timing")]
        [Tooltip("Seconds between printed lines. Cosmetic, like OSInstallManager's staged progress - but it is what makes pressing POWER feel like starting a computer rather than opening a menu.")]
        [Min(0f)]
        [SerializeField] private float secondsPerLine = 0.45f;

        [Header("Service call cost")]
        [Tooltip("Seconds the first power-on adds to the service call. Confirming the reported fault is real work and should cost something.")]
        [Min(0)]
        [SerializeField] private int observationTimeCostSeconds = 30;

        [Tooltip("Seconds a test after a repair adds. Testing properly costs time, and it should.")]
        [Min(0)]
        [SerializeField] private int verificationTimeCostSeconds = 30;

        [Tooltip("The id this power-on is recorded under, so pressing POWER five times still only counts as one observation. Any value works as long as no TicketObservationSet uses the same one.")]
        [SerializeField] private string observationId = "power_on_test";

        [Header("Dialogs (optional)")]
        [Tooltip("Shown after a failed boot. Leave empty and one is written for you from the symptom - so the popup the learner sees always matches what the screen just said.")]
        [SerializeField] private DialogSequence failDialog;

        [Tooltip("Shown after the machine passes. Optional.")]
        [SerializeField] private DialogSequence successDialog;

        [Header("Assessment (optional)")]
        [Tooltip("An AssessmentRecorder stage id, e.g. \"power_on_test\". Leave blank to record nothing.")]
        [SerializeField] private string stageId = "";

        [Header("Events")]
        [Tooltip("Fires when the machine starts normally and every check passes. Wire your Return To Hub button's Interactable here so a ticket cannot be closed untested.")]
        public UnityEvent onBootSucceeded;

        [Tooltip("Fires when the machine still does not work.")]
        public UnityEvent onBootFailed;

        // ==================================================================
        // State
        // ==================================================================

        private bool running;
        private bool resolved;
        private DialogSequence generatedFailDialog;

        /// <summary>True once the machine has started normally at least once this
        /// visit. Read it from a Return To Hub button if you would rather gate on a
        /// property than wire the UnityEvent.</summary>
        public bool HasPassedBootTest { get; private set; }

        /// <summary>True while the boot sequence is playing out.</summary>
        public bool IsBooting => running;

        // ==================================================================
        // Lifecycle
        // ==================================================================

        private void Awake()
        {
            if (powerButton != null) powerButton.onClick.AddListener(RequestPowerOn);
            if (closeButton != null) closeButton.onClick.AddListener(Leave);
        }

        private void Start()
        {
            Resolve();

            // Hidden HERE and not in Awake. Deactivating Panel Root during Awake
            // stops Unity ever calling this Start, which means nothing below ever
            // runs and the panel is left half-initialised with no error to show for
            // it. SiteObservationPanel has the full account of that trap; this is
            // the same trap.
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        /// <summary>Late binding, called from Start AND from Open, so the panel
        /// still works if it was inactive when the scene loaded.</summary>
        private void Resolve()
        {
            if (resolved) return;
            resolved = true;

            if (coordinator == null) coordinator = FindAnyObjectByType<TicketFaultCoordinator>();

            if (screenText == null)
                Debug.LogWarning("[PCPowerStation] No Screen Text assigned - the machine will boot but the learner will see nothing.", this);
        }

        // ==================================================================
        // Opening and closing the monitor
        // ==================================================================

        /// <summary>Wired to the monitor station's On Enter, so arriving at the
        /// station is what brings the screen up.</summary>
        public void Open()
        {
            Resolve();

            if (!RepairTicketMode.IsActive)
            {
                // Normal campaign play in this scene. The OS install station owns
                // the monitor then, and two things fighting over one screen is a
                // bug waiting to happen.
                Debug.Log("[PCPowerStation] No active ticket - leaving the monitor to the normal campaign flow.", this);
                return;
            }

            ServiceCallState.EnsureTicket(RepairTicketMode.ActiveTicketId);

            if (panelRoot != null) panelRoot.SetActive(true);
            SetScreenOff(true);

            if (screenText != null)
                screenText.text = "The computer is switched off.\n\nPress POWER to start it.";

            if (powerButton != null) powerButton.interactable = !running;
        }

        /// <summary>Wired to the monitor station's On Exit.</summary>
        public void Close()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        /// <summary>The Step back button. Leaves the whole station - camera included -
        /// rather than only hiding the screen, which would strand the learner looking
        /// at a monitor camera pointed at a blank wall.</summary>
        public void Leave()
        {
            if (monitorStation != null) monitorStation.Exit();
            else Close();
        }

        // ==================================================================
        // The boot itself
        // ==================================================================

        /// <summary>Wired automatically from the Power Button field - the POWER
        /// button on the screen itself.</summary>
        public void RequestPowerOn()
        {
            if (running) return;

            Resolve();

            if (!RepairTicketMode.IsActive)
            {
                Debug.Log("[PCPowerStation] No active ticket - nothing to test.", this);
                return;
            }

            if (panelRoot != null && !panelRoot.activeSelf) Open();

            ServiceCallState.EnsureTicket(RepairTicketMode.ActiveTicketId);

            // The case has to go back together before the machine is tested. This
            // is not pedantry: closing up is a step of the job, and a learner who
            // never does it has not finished the repair.
            if (systemUnitStation != null && systemUnitStation.IsInside)
            {
                if (screenText != null)
                    screenText.text = "The system unit is still open.\n\nStand it back up before switching the computer on.";

                Debug.Log("[PCPowerStation] Power-on refused - the system unit is still laid open.", this);
                return;
            }

            if (coordinator == null)
            {
                Debug.LogWarning("[PCPowerStation] No TicketFaultCoordinator in this scene - there is nothing to test against.", this);
                if (screenText != null) screenText.text = "Unable to start the computer.";
                return;
            }

            StartCoroutine(BootSequence());
        }

        private IEnumerator BootSequence()
        {
            running = true;
            if (powerButton != null) powerButton.interactable = false;

            SetScreenOff(false);

            // Split before printing anything, so the POST text and the symptom text
            // are decided against one single snapshot of the machine's state.
            var blocking = new List<TicketFault>();
            var afterBoot = new List<TicketFault>();
            var allFaults = new List<TicketFault>();

            foreach (var fault in coordinator.ActiveFaults)
            {
                if (fault == null) continue;
                allFaults.Add(fault);

                if (fault.IsRepaired()) continue;                 // asked fresh, right now
                if (fault.PreventsBoot) blocking.Add(fault);
                else afterBoot.Add(fault);
            }

            var sb = new StringBuilder();
            sb.AppendLine("CircuitCrafter BIOS  v1.0");
            sb.AppendLine("=========================================");
            sb.AppendLine();
            Print(sb);
            yield return Wait();

            sb.AppendLine("Power ................................ on");
            Print(sb);
            yield return Wait();

            sb.AppendLine("Detecting installed hardware ...");
            Print(sb);
            yield return Wait();
            yield return Wait();

            bool started = blocking.Count == 0;
            bool allClear = started && afterBoot.Count == 0;

            if (!started)
            {
                sb.AppendLine();
                sb.AppendLine("-----------------------------------------");
                sb.AppendLine("The computer did not start.");
                sb.AppendLine();
                foreach (var fault in blocking) sb.AppendLine(Indent(fault.BootSymptom));
                sb.AppendLine("-----------------------------------------");
                sb.AppendLine();
                sb.AppendLine("Switch it off at the wall and open the");
                sb.AppendLine("system unit to look inside.");
                Print(sb);
            }
            else
            {
                // Named checks are safe here and only here: everything that would
                // stop the machine booting has already been fixed, so listing them
                // gives nothing away - it just tells the learner what was tested.
                foreach (var fault in allFaults)
                {
                    if (!fault.PreventsBoot) continue;
                    sb.AppendLine(Pad(fault.VerificationLabel) + "OK");
                    Print(sb);
                    yield return Wait();
                }

                sb.AppendLine();
                sb.AppendLine("Starting Windows ...");
                Print(sb);
                yield return Wait();
                yield return Wait();

                if (allClear)
                {
                    sb.AppendLine();
                    sb.AppendLine("The computer starts normally and passes");
                    sb.AppendLine("every check. The ticket can be closed.");
                }
                else
                {
                    sb.AppendLine();
                    sb.AppendLine("-----------------------------------------");
                    sb.AppendLine("Windows started, but the machine is still");
                    sb.AppendLine("not working properly.");
                    sb.AppendLine();
                    foreach (var fault in afterBoot) sb.AppendLine(Indent(fault.BootSymptom));
                    sb.AppendLine("-----------------------------------------");
                }
                Print(sb);
            }

            Finish(allClear);
        }

        // ==================================================================

        /// <summary>
        /// Records the run and fires the events.
        ///
        /// WHICH KIND OF RUN THIS WAS is worked out here rather than configured,
        /// because a single power button that has to be told what it is would get
        /// told wrong. A pass is always a successful test. A failure after the
        /// learner has already repaired something is a failed test - they believed
        /// they were done and they were not, which is exactly the callback the
        /// module exists to prevent. A failure before they have repaired anything
        /// is not a failed test at all; it is them confirming the reported fault,
        /// so it is recorded as an observation and feeds the evidence gate on the
        /// diagnosis panel.
        /// </summary>
        private void Finish(bool allClear)
        {
            bool hasRepairedSomething = coordinator != null && coordinator.RepairedFaultCount > 0;
            bool countsAsTest = allClear || hasRepairedSomething;

            if (countsAsTest)
            {
                ServiceCallState.RecordVerification(allClear, verificationTimeCostSeconds);
            }
            else if (ServiceCallState.RecordObservation(observationId, observationTimeCostSeconds))
            {
                Debug.Log("[PCPowerStation] First power-on recorded as an observation - the learner has confirmed the reported fault.", this);
            }

            if (allClear) HasPassedBootTest = true;

            if (!string.IsNullOrEmpty(stageId))
            {
                if (countsAsTest)
                {
                    AssessmentRecorder.Attempt(stageId, allClear);
                    if (allClear) AssessmentRecorder.Stage(stageId);
                    else AssessmentRecorder.Mistake("verification", "Powered the machine on to test the repair and it still failed.");
                }
            }

            Debug.Log($"[PCPowerStation] Boot {(allClear ? "PASSED" : "FAILED")} " +
                      $"({(countsAsTest ? "counted as a test" : "counted as an observation")}). {ServiceCallState.Summary()}", this);

            running = false;
            if (powerButton != null) powerButton.interactable = true;

            ShowDialogFor(allClear);

            if (allClear) onBootSucceeded?.Invoke();
            else onBootFailed?.Invoke();
        }

        private void ShowDialogFor(bool allClear)
        {
            if (DialogManager.Instance == null) return;

            if (allClear)
            {
                if (successDialog != null) DialogManager.Instance.ShowDialog(successDialog);
                return;
            }

            if (failDialog != null)
            {
                DialogManager.Instance.ShowDialog(failDialog);
                return;
            }

            // Nothing authored, so one is written from the symptom that is already
            // on screen. Built once and reused: a DialogSequence created per press
            // would leak a ScriptableObject on every power-on.
            if (generatedFailDialog == null)
                generatedFailDialog = ScriptableObject.CreateInstance<DialogSequence>();

            generatedFailDialog.lines.Clear();
            generatedFailDialog.lines.Add(new DialogLine
            {
                speakerName = "You",
                text = "The computer will not start properly. There is something wrong inside it - " +
                       "I need to open the system unit and look."
            });

            DialogManager.Instance.ShowDialog(generatedFailDialog);
        }

        // ==================================================================
        // Small helpers
        // ==================================================================

        private void Print(StringBuilder sb)
        {
            if (screenText != null) screenText.text = sb.ToString();
        }

        private WaitForSeconds Wait()
        {
            return new WaitForSeconds(secondsPerLine);
        }

        private void SetScreenOff(bool off)
        {
            if (screenOffOverlay != null) screenOffOverlay.SetActive(off);
        }

        /// <summary>Dot leaders, so the output reads like a real POST screen rather
        /// than a bulleted list.</summary>
        private static string Pad(string label)
        {
            const int width = 38;
            if (string.IsNullOrEmpty(label)) label = "Check";
            if (label.Length >= width - 2) return label + " ";

            var sb = new StringBuilder(label);
            sb.Append(' ');
            while (sb.Length < width - 1) sb.Append('.');
            sb.Append(' ');
            return sb.ToString();
        }

        private static string Indent(string text)
        {
            if (string.IsNullOrEmpty(text)) return "  (no symptom written for this fault)";
            return "  " + text.Replace("\n", "\n  ");
        }
    }
}