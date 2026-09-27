using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;
using CircuitCrafter.Assessment;

namespace CircuitCrafter.Networking.Unity
{
    /// <summary>
    /// A command-prompt window that runs a real ping against the simulation.
    ///
    /// ATTACH THIS TO: the ping/console panel in the COC3 scene.
    ///
    /// TWO OUTPUTS, ON PURPOSE
    ///   Output Text      - exactly what Windows would print, nothing added.
    ///   Explanation Text - why, in words the learner can act on.
    ///
    /// Keep them visually separate: the console should look like a console. Put
    /// the explanation in a feedback panel beside it, or leave it unassigned for
    /// an assessment run where working it out IS the task. Showing only the real
    /// output teaches recognition without understanding; showing only the
    /// explanation teaches something that never appears on a real screen.
    /// </summary>
    public class PingConsolePanel : MonoBehaviour
    {
        [Header("Who is pinging")]
        [Tooltip("The machine the command is typed on.")]
        [SerializeField] private NetworkedWorkstation source;

        [Header("What to ping")]
        [Tooltip("Optional. Assign to let the learner type a target themselves - which is the more realistic exercise.")]
        [SerializeField] private TMP_InputField targetInput;

        [Tooltip("Used when there is no input field, or when the field is left empty. Either an address, or leave blank and assign Fixed Target Station instead.")]
        [SerializeField] private string fixedTargetAddress = "";

        [Tooltip("Optional - ping this machine's CURRENT address, whatever it has been configured to. Better than a fixed string when the learner sets the target's address themselves earlier in the exercise.")]
        [SerializeField] private NetworkedWorkstation fixedTargetStation;

        [Header("Output")]
        [SerializeField] private TMP_Text outputText;

        [Tooltip("Optional - the plain-language reason. Leave unassigned for an assessment run.")]
        [SerializeField] private TMP_Text explanationText;

        [SerializeField] private Button runButton;

        [Range(1, 10)]
        [SerializeField] private int packetCount = 4;

        [Header("Assessment (optional)")]
        [Tooltip("An AssessmentRecorder stage id. Leave blank to record nothing.")]
        [SerializeField] private string stageId = "";

        [SerializeField] private bool completeStageOnSuccess = true;

        [Header("Events")]
        [Tooltip("Fires when the ping gets a reply. Wire COC3Manager > SkipToNextStep here to let a successful ping advance the step, with no code changes.")]
        public UnityEvent onPingSucceeded;

        [Tooltip("Fires when the ping fails. Wire a retry prompt, a hint, or nothing at all - the learner staying put and working it out IS the exercise.")]
        public UnityEvent onPingFailed;

        /// <summary>Fires with the full result of every run. For code; the
        /// UnityEvents above are the Inspector-facing pair.</summary>
        public event Action<PingOutcome> OnPingComplete;

        public PingOutcome LastOutcome { get; private set; }

        // ==================================================================

        private void Awake()
        {
            if (runButton != null) runButton.onClick.AddListener(Run);
        }

        private void OnEnable()
        {
            if (outputText != null) outputText.text = "";
            if (explanationText != null) explanationText.text = "";
        }

        /// <summary>Wired automatically from the Run Button field.</summary>
        public void Run()
        {
            if (NetworkSimulationManager.Instance == null)
            {
                Debug.LogWarning($"[PingConsolePanel] '{name}' found no NetworkSimulationManager in this scene.", this);
                if (outputText != null) outputText.text = "Unable to run the command.";
                return;
            }

            string destination = ResolveTarget();
            if (string.IsNullOrWhiteSpace(destination))
            {
                if (outputText != null) outputText.text = "Type an address to ping.";
                return;
            }

            PingOutcome outcome = NetworkSimulationManager.Instance.Ping(source, destination, packetCount);
            LastOutcome = outcome;

            if (outputText != null) outputText.text = BuildConsoleOutput(destination, outcome);
            // GUIDANCE: the console output above is what Windows prints and stays in every mode. The
            // explanation under it teaches WHY - Story only; an assessment keeps it for the review.
            if (explanationText != null)
                explanationText.text = CircuitCrafter.Guidance.GuidancePolicy.Allows(CircuitCrafter.Guidance.GuidanceKind.CorrectiveFeedback)
                    ? outcome.Explanation : "";

            if (!string.IsNullOrEmpty(stageId))
            {
                AssessmentRecorder.Attempt(stageId, outcome.Success);

                if (outcome.Success)
                {
                    if (completeStageOnSuccess) AssessmentRecorder.Stage(stageId);
                }
                else
                {
                    // The failure reason, not just "ping failed" - so an instructor
                    // reading the report can see WHICH misconfiguration the learner
                    // was stuck on, and for how long.
                    AssessmentRecorder.Mistake("connectivity", $"Ping to {destination} failed: {outcome.Failure}. {outcome.Explanation}");
                }
            }

            OnPingComplete?.Invoke(outcome);

            // Last, so anything hooked here - advancing the step, for instance -
            // happens only after the console text, the explanation and the
            // assessment record are all in place.
            if (outcome.Success) onPingSucceeded?.Invoke();
            else onPingFailed?.Invoke();
        }

        private string ResolveTarget()
        {
            if (targetInput != null && !string.IsNullOrWhiteSpace(targetInput.text))
                return targetInput.text.Trim();

            // Reading the station's live address rather than a fixed string means
            // the exercise still works when the learner chose that machine's
            // address themselves an earlier step.
            if (fixedTargetStation != null && !fixedTargetStation.Host.Address.IsUnspecified)
                return fixedTargetStation.Host.Address.ToString();

            return fixedTargetAddress;
        }

        /// <summary>
        /// Reproduces the shape of real ping output: the header, one line per
        /// packet, then the statistics block. The per-packet repetition matters -
        /// a learner who has only ever seen a one-line summary doesn't recognise
        /// the real thing when they meet it.
        /// </summary>
        private string BuildConsoleOutput(string destination, PingOutcome outcome)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Pinging {destination} with 32 bytes of data:");

            if (outcome.Failure == PingFailure.SourcePoweredOff)
                return "";   // a machine that is off shows no console at all

            for (int i = 0; i < Mathf.Max(1, outcome.PacketsSent); i++)
                sb.AppendLine(outcome.ConsoleMessage);

            sb.AppendLine();
            sb.AppendLine($"Ping statistics for {destination}:");
            sb.AppendLine($"    Packets: Sent = {outcome.PacketsSent}, Received = {outcome.PacketsReceived}, " +
                          $"Lost = {outcome.PacketsLost} ({outcome.LossPercent}% loss),");

            if (outcome.Success)
            {
                sb.AppendLine("Approximate round trip times in milli-seconds:");
                sb.AppendLine("    Minimum = 0ms, Maximum = 0ms, Average = 0ms");
            }

            return sb.ToString();
        }

        public void Clear()
        {
            if (outputText != null) outputText.text = "";
            if (explanationText != null) explanationText.text = "";
        }
    }
}