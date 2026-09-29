using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;
using CircuitCrafter.Assessment;

namespace CircuitCrafter.Networking.Unity
{
    /// <summary>
    /// A working IPv4 properties dialog: three boxes, an OK button, and rules
    /// instead of an answer key.
    ///
    /// ATTACH THIS TO: the panel holding your IP / mask / gateway input fields in
    /// the COC3 scene. It works standalone - it does not need COC3Manager, and
    /// nothing in COC3Manager needs to change for it to run.
    ///
    /// WHAT IT DOES DIFFERENTLY FROM THE EXISTING STEP
    /// The current NetworkConfigInput step compares each box against one stored
    /// string, so 192.168.1.11 fails a step whose key says .10. Here the boxes
    /// are checked against a NetworkRequirementData - is this on the right
    /// subnet, is it a usable host address, is it already taken - so any answer
    /// that would genuinely work is accepted, and a rejection names the rule that
    /// was broken.
    ///
    /// It also APPLIES what the learner typed to the machine. That is what lets a
    /// later ping step tell the truth: the ping reads the configuration rather
    /// than a memory of whether this form was filled in correctly.
    ///
    /// ASSESSMENT
    /// If a Stage Id is set, every submit is reported to the AssessmentRecorder -
    /// right first time or not - and a rejection is logged as a mistake in the
    /// "configuration" category. The calls are static and no-op wherever no
    /// recorder exists, so this is safe in a scene without one.
    /// </summary>
    public class NetworkConfigPanel : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("The machine these settings are being applied to.")]
        [SerializeField] private NetworkedWorkstation target;

        [Tooltip("The rules the entries are checked against.")]
        [SerializeField] private NetworkRequirementData requirement;

        [Header("Fields")]
        [SerializeField] private TMP_InputField ipInput;
        [SerializeField] private TMP_InputField maskInput;

        [Tooltip("Optional. Leave unassigned for a step that doesn't ask for a gateway.")]
        [SerializeField] private TMP_InputField gatewayInput;

        [SerializeField] private Button applyButton;

        [Header("Feedback")]
        [Tooltip("Where the rule that was broken is explained. The one field worth always assigning - without it the panel can only refuse silently.")]
        [SerializeField] private TMP_Text feedbackText;

        [SerializeField] private string successMessage = "Settings applied.";

        [Header("Assessment (optional)")]
        [Tooltip("An AssessmentRecorder stage id, e.g. \"coc3_static_ip\". Leave blank to record nothing. If set, it should match a Manual Id on the recorder AND appear in the COC3 competency asset's evidence list.")]
        [SerializeField] private string stageId = "";

        [Tooltip("Close this stage on the recorder once the settings are accepted.")]
        [SerializeField] private bool completeStageOnSuccess = true;

        [Header("Events")]
        [Tooltip("Fires once the settings validate and are applied. Wire COC3Manager > SkipToNextStep here to advance the step, with no code changes.")]
        public UnityEvent onAccepted;

        [Tooltip("Fires on every rejected submit.")]
        public UnityEvent onRejected;

        /// <summary>Fires when everything validated and was applied to the machine.
        /// For code; the UnityEvents above are the Inspector-facing pair.</summary>
        public event Action OnConfigurationAccepted;

        /// <summary>Fires with the explanation whenever a submit is rejected.</summary>
        public event Action<string> OnConfigurationRejected;

        public bool IsAccepted { get; private set; }

        // ==================================================================

        private void Awake()
        {
            if (applyButton != null) applyButton.onClick.AddListener(Apply);
        }

        private void OnEnable()
        {
            if (feedbackText != null) feedbackText.text = "";

            if (target == null)
                Debug.LogWarning($"[NetworkConfigPanel] '{name}' has no target workstation - there is nothing to configure.", this);

            if (requirement == null)
                Debug.LogWarning($"[NetworkConfigPanel] '{name}' has no NetworkRequirementData - every entry would be accepted, which teaches nothing.", this);
        }

        // ==================================================================

        /// <summary>Wired automatically from the Apply Button field. Also public so
        /// you can call it from a UnityEvent if you prefer.</summary>
        public void Apply()
        {
            if (target == null || requirement == null)
            {
                Show("This screen isn't finished being set up.");
                return;
            }

            AddressRequirement rules = requirement.ToRequirement();

            // Order matters. The mask is checked first because every other rule
            // depends on it: telling someone their address is on the wrong subnet
            // is misleading when the mask defining that subnet is itself nonsense.
            AddressCheck mask = rules.CheckSubnetMask(maskInput != null ? maskInput.text : "");
            if (!mask.IsAcceptable) { Reject(mask.Message); return; }

            AddressCheck address = rules.CheckHostAddress(ipInput != null ? ipInput.text : "");
            if (!address.IsAcceptable) { Reject(address.Message); return; }

            IPv4Address gateway = IPv4Address.None;
            if (gatewayInput != null)
            {
                AddressCheck gatewayCheck = rules.CheckGateway(gatewayInput.text, address.Parsed);
                if (!gatewayCheck.IsAcceptable) { Reject(gatewayCheck.Message); return; }
                gateway = gatewayCheck.Parsed;
            }

            // Everything passed - write it to the machine. From here on the ping
            // step reads these values, not a boolean about this moment.
            target.SetSubnetMask(mask.Parsed);
            target.SetAddress(address.Parsed);
            if (gatewayInput != null) target.SetGateway(gateway);
            target.SetUsingDhcp(false);

            IsAccepted = true;

            if (!string.IsNullOrEmpty(stageId))
            {
                AssessmentRecorder.Attempt(stageId, true);
                if (completeStageOnSuccess) AssessmentRecorder.Stage(stageId);
            }

            Show(successMessage);
            OnConfigurationAccepted?.Invoke();
            onAccepted?.Invoke();
        }

        private void Reject(string message)
        {
            if (!string.IsNullOrEmpty(stageId))
            {
                AssessmentRecorder.Attempt(stageId, false);
                AssessmentRecorder.Mistake("configuration", message);
            }

            // The message names the rule that was broken - corrective feedback, Story only.
            // An assessment just says it was not accepted; the reason is on the review screen.
            Show(!AssessmentMode.IsActive
                ? message : "Action not accepted.");
            OnConfigurationRejected?.Invoke(message);
            onRejected?.Invoke();
        }

        private void Show(string message)
        {
            if (feedbackText != null) feedbackText.text = message;
            else Debug.Log($"[NetworkConfigPanel] {message}", this);
        }

        /// <summary>Clears the boxes and the verdict. Wire to a Reset button.</summary>
        public void ClearFields()
        {
            if (ipInput != null) ipInput.text = "";
            if (maskInput != null) maskInput.text = "";
            if (gatewayInput != null) gatewayInput.text = "";
            if (feedbackText != null) feedbackText.text = "";
            IsAccepted = false;
        }
    }
}