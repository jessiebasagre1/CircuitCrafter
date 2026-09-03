using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;
using CircuitCrafter.Assessment;

namespace CircuitCrafter.Repair
{
    /// <summary>
    /// Power the machine on and prove it works.
    ///
    /// ATTACH THIS TO: a verification panel in each repair-site scene.
    ///
    /// WHY THIS PHASE EXISTS
    /// Until now the job ended the moment the fault object reported itself fixed.
    /// That is the game marking its own homework: nobody ever switched the
    /// machine on. A technician who closes a ticket without testing produces a
    /// callback, and callbacks are the thing the industry actually measures - so
    /// running this, and running it successfully, is recorded separately from
    /// whether the repair happened.
    ///
    /// IT ASKS THE FAULTS, LIVE.
    /// Every check comes from TicketFaultCoordinator.ActiveFaults and calls
    /// IsRepaired() fresh at the moment of the test. Nothing is read from a
    /// stored result. That means it catches the case the observation panel
    /// cannot: a learner who fixed something, got credited, then pulled the part
    /// back out will fail verification - correctly.
    ///
    /// IT ALSO SOLVES THE STALE-OBSERVATION PROBLEM. Findings on the Examine
    /// panel are authored text and never change after a repair, which is fine for
    /// diagnosis but useless for proving a fix. This reads the real state
    /// instead, which is exactly why it had to be its own phase rather than
    /// another observation.
    /// </summary>
    public class RepairVerificationPanel : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameObject panelRoot;

        [Tooltip("The coordinator for this site. Found automatically if left empty.")]
        [SerializeField] private TicketFaultCoordinator coordinator;

        [Tooltip("Where the test output is printed. Always assign this.")]
        [SerializeField] private TMP_Text outputText;

        [SerializeField] private Button runButton;
        [SerializeField] private Button closeButton;

        [Header("Test")]
        [Tooltip("Seconds the on-screen test takes to play out. Cosmetic, like OSInstallManager's staged progress.")]
        [Min(0f)]
        [SerializeField] private float secondsPerCheck = 0.6f;

        [Tooltip("Seconds this adds to the service call. Testing properly costs time, and it should.")]
        [Min(0)]
        [SerializeField] private int timeCostSeconds = 30;

        [Header("Assessment (optional)")]
        [SerializeField] private string stageId = "";

        [Header("Events")]
        [Tooltip("Fires when the machine passes. Wire your Return To Hub button's Interactable here so a ticket cannot be closed untested.")]
        public UnityEvent onVerifyPassed;

        [Tooltip("Fires when the machine still fails.")]
        public UnityEvent onVerifyFailed;

        private bool running;

        // ==================================================================

        private void Awake()
        {
            if (runButton != null) runButton.onClick.AddListener(Run);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
        }

        private void Start()
        {
            if (coordinator == null) coordinator = FindAnyObjectByType<TicketFaultCoordinator>();

            // Hidden here rather than in Awake - deactivating Panel Root during
            // Awake stops Unity ever calling this Start, and Panel Root is
            // normally this same object. See SiteObservationPanel for the full
            // account of that trap.
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        public void Open()
        {
            if (!RepairTicketMode.IsActive)
            {
                Debug.LogWarning("[RepairVerificationPanel] No active ticket - nothing to test.", this);
                return;
            }

            ServiceCallState.EnsureTicket(RepairTicketMode.ActiveTicketId);

            if (panelRoot != null) panelRoot.SetActive(true);
            if (outputText != null) outputText.text = "Ready to test. Press the power button.";
        }

        public void Close()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        // ==================================================================

        /// <summary>Wired automatically from the Run Button field.</summary>
        public void Run()
        {
            if (running) return;

            if (coordinator == null)
            {
                Debug.LogWarning("[RepairVerificationPanel] No TicketFaultCoordinator in this scene - there is nothing to test against.", this);
                if (outputText != null) outputText.text = "Unable to run the test.";
                return;
            }

            StartCoroutine(RunSequence());
        }

        private IEnumerator RunSequence()
        {
            running = true;
            if (runButton != null) runButton.interactable = false;

            var sb = new StringBuilder();
            sb.AppendLine("Powering on...");
            sb.AppendLine();
            if (outputText != null) outputText.text = sb.ToString();

            yield return new WaitForSeconds(secondsPerCheck);

            bool allPassed = true;

            foreach (var fault in coordinator.ActiveFaults)
            {
                if (fault == null) continue;

                // Asked fresh, at this moment - not read from HasBeenCredited.
                bool ok = fault.IsRepaired();
                if (!ok) allPassed = false;

                sb.AppendLine(Pad(fault.VerificationLabel) + (ok ? "PASS" : "FAIL"));
                if (outputText != null) outputText.text = sb.ToString();

                yield return new WaitForSeconds(secondsPerCheck);
            }

            sb.AppendLine();
            sb.AppendLine(allPassed
                ? "System starts normally and passes every check.\nThe ticket can be closed."
                : "The machine still does not pass.\nGo back and look again - something is outstanding.");

            if (outputText != null) outputText.text = sb.ToString();

            ServiceCallState.RecordVerification(allPassed, timeCostSeconds);

            if (!string.IsNullOrEmpty(stageId))
            {
                AssessmentRecorder.Attempt(stageId, allPassed);
                if (allPassed) AssessmentRecorder.Stage(stageId);
                else AssessmentRecorder.Mistake("verification", "Powered on for testing and the machine still failed.");
            }

            Debug.Log($"[RepairVerificationPanel] Test {(allPassed ? "PASSED" : "FAILED")}. {ServiceCallState.Summary()}", this);

            running = false;
            if (runButton != null) runButton.interactable = true;

            if (allPassed) onVerifyPassed?.Invoke();
            else onVerifyFailed?.Invoke();
        }

        /// <summary>Dot leaders, so the output reads like a real POST screen
        /// rather than a bulleted list.</summary>
        private static string Pad(string label)
        {
            const int width = 34;
            if (string.IsNullOrEmpty(label)) label = "Check";
            if (label.Length >= width - 2) return label + " ";

            var sb = new StringBuilder(label);
            sb.Append(' ');
            while (sb.Length < width - 1) sb.Append('.');
            sb.Append(' ');
            return sb.ToString();
        }
    }
}
