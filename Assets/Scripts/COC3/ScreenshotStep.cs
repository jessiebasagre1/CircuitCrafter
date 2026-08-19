using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Lives on each "step panel" GameObject in your Hierarchy (one per screenshot/step).
/// It displays the screenshot + instructions from a ScreenshotStepData asset,
/// holds references to whatever input fields/buttons THIS step needs,
/// and knows how to validate itself when the learner submits an answer.
///
/// You do not need a different script per step - the same ScreenshotStep script
/// is reused for every step, just pointed at a different StepData asset.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class ScreenshotStep : MonoBehaviour
{
    [Header("Step Data (drag the ScriptableObject asset here)")]
    public ScreenshotStepData stepData;

    [Header("UI References for THIS step")]
    [Tooltip("The Image component that displays the screenshot.")]
    public Image screenshotDisplay;

    [Tooltip("The text element that displays the instructions.")]
    public TMP_Text instructionLabel;

    [Header("Input Fields (only assign the ones this step actually uses)")]
    public TMP_InputField ipInputField;
    public TMP_InputField subnetInputField;
    public TMP_InputField gatewayInputField;

    [Tooltip("Generic single-value field used only for TextInput steps (e.g. Computer Name, DNS Server, Username). Its placeholder text is set automatically from stepData.textFieldLabel.")]
    public TMP_InputField textInputField;

    [Header("Transparent Buttons (only used for ButtonInteraction steps)")]
    [Tooltip("If this step just requires clicking somewhere on the screenshot, wire that button's OnClick to COC3Manager.SubmitCurrentStep().")]
    public Button[] interactionButtons;

    private CanvasGroup canvasGroup;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }

    /// <summary>
    /// Refreshes the visuals (screenshot + instructions) and clears any leftover input
    /// from a previous attempt. Call this whenever the step becomes active.
    /// </summary>
    public void Setup()
    {
        if (stepData == null)
        {
            Debug.LogWarning($"[ScreenshotStep] No StepData assigned on '{name}'.", this);
            return;
        }

        if (screenshotDisplay != null) screenshotDisplay.sprite = stepData.screenshotImage;
        if (instructionLabel != null) instructionLabel.text = stepData.instructionText;

        if (ipInputField != null) ipInputField.text = string.Empty;
        if (subnetInputField != null) subnetInputField.text = string.Empty;
        if (gatewayInputField != null) gatewayInputField.text = string.Empty;

        if (textInputField != null)
        {
            textInputField.text = string.Empty;

            // Auto-label the placeholder so you can reuse the SAME input field
            // across many different TextInput steps (Computer Name, DNS Server,
            // Username, etc.) just by changing textFieldLabel on the StepData asset.
            if (textInputField.placeholder is TMP_Text placeholderLabel)
                placeholderLabel.text = stepData.textFieldLabel;
        }
    }

    /// <summary>Makes this step panel visible and interactable.</summary>
    public void Show()
    {
        gameObject.SetActive(true);
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
    }

    /// <summary>Hides this step panel (used when moving to the next/previous step).</summary>
    public void Hide()
    {
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Checks whether the learner's input is correct for THIS step.
    /// PingTest steps always return true here because they are validated
    /// separately by PingSimulation (they depend on a previous step's result).
    /// </summary>
    public bool Validate(out string feedbackMessage)
    {
        feedbackMessage = string.Empty;

        switch (stepData.stepType)
        {
            case StepType.Informational:
            case StepType.ButtonInteraction:
                return true;

            case StepType.PingTest:
                // Handled by PingSimulation via COC3Manager - see COC3Manager.SubmitCurrentStep()
                return true;

            case StepType.NetworkConfigInput:
                bool ipOk = NetworkConfigValidator.ValidateExactMatch(ipInputField.text, stepData.expectedIP);
                bool maskOk = NetworkConfigValidator.ValidateExactMatch(subnetInputField.text, stepData.expectedSubnetMask);
                bool gwOk = NetworkConfigValidator.ValidateExactMatch(gatewayInputField.text, stepData.expectedGateway);

                if (ipOk && maskOk && gwOk)
                {
                    feedbackMessage = "Network configuration is correct!";
                    return true;
                }

                feedbackMessage = BuildErrorMessage(ipOk, maskOk, gwOk);
                return false;

            case StepType.TextInput:
                string typedValue = textInputField != null ? textInputField.text : string.Empty;
                bool textOk = ValidateTextInput(typedValue, stepData.textFieldExpectedValue, stepData.textFieldCaseSensitive);

                if (textOk)
                {
                    feedbackMessage = "Correct!";
                    return true;
                }

                feedbackMessage = $"Please check: {stepData.textFieldLabel}";
                return false;

            default:
                return false;
        }
    }

    /// <summary>
    /// Compares the learner's typed text against the expected value for a TextInput step.
    /// Trims leading/trailing whitespace on both sides, and is case-insensitive by default
    /// (matches how Windows treats usernames/computer names) unless textFieldCaseSensitive
    /// is checked on the StepData asset. Null-safe - never throws even if the field is empty.
    /// </summary>
    private bool ValidateTextInput(string typed, string expected, bool caseSensitive)
    {
        typed = (typed ?? string.Empty).Trim();
        expected = (expected ?? string.Empty).Trim();

        return caseSensitive
            ? typed == expected
            : string.Equals(typed, expected, System.StringComparison.OrdinalIgnoreCase);
    }

    private string BuildErrorMessage(bool ipOk, bool maskOk, bool gwOk)
    {
        string msg = "Please check: ";
        if (!ipOk) msg += "IP Address, ";
        if (!maskOk) msg += "Subnet Mask, ";
        if (!gwOk) msg += "Default Gateway, ";
        return msg.TrimEnd(',', ' ');
    }
}