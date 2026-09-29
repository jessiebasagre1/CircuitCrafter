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

    /// <summary>
    /// The panel's CanvasGroup, fetched on first use instead of cached in Awake().
    ///
    /// WHY THIS IS A PROPERTY AND NOT AN Awake() CACHE:
    /// Unity only runs Awake() on a GameObject that is active IN THE HIERARCHY.
    /// A step panel that is switched off in the Inspector - or whose PARENT is
    /// switched off, which is easy to miss because the panel itself still reads
    /// activeSelf = true - never receives an Awake(). COC3Manager.StartActivity()
    /// then calls Hide() on all 30 steps, and every such panel threw a
    /// NullReferenceException on the very first frame.
    ///
    /// Resolving it lazily removes the trap entirely: the reference is looked up
    /// the first time Show() or Hide() actually needs it, active or not.
    /// </summary>
    private CanvasGroup Group
    {
        get
        {
            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();

                // [RequireComponent] guarantees one exists on anything created
                // through the Inspector, but a panel built purely from script
                // could still be missing it - add it rather than throw.
                if (canvasGroup == null)
                    canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
            return canvasGroup;
        }
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

        // The null check on screenshotImage is deliberate, and it is what lets you
        // assign screenshots BY HAND on each panel.
        //
        // Originally this line was an unconditional assignment:
        //     screenshotDisplay.sprite = stepData.screenshotImage;
        // which meant that whenever the StepData's field was empty, the panel's own
        // sprite was overwritten with NOTHING the instant the step was shown - so a
        // screenshot dropped straight onto the panel's Image silently disappeared.
        //
        // The rule is now:
        //     StepData has a sprite  -> the asset wins (swap content without touching the scene)
        //     StepData is empty      -> whatever you placed on the panel is left alone
        // so both workflows work, and you can mix them panel by panel.
        if (screenshotDisplay != null && stepData.screenshotImage != null)
            screenshotDisplay.sprite = stepData.screenshotImage;

        // Step-by-step instructions are Story Mode help. An assessment shows the step's
        // Assessment Prompt (the requirement, not the method) or a generic line.
        if (instructionLabel != null)
            instructionLabel.text = !AssessmentMode.IsActive
                ? stepData.instructionText
                : (string.IsNullOrEmpty(stepData.assessmentPrompt)
                    ? "Continue the server configuration required by the job order."
                    : stepData.assessmentPrompt);

        PrepareField(ipInputField);
        PrepareField(subnetInputField);
        PrepareField(gatewayInputField);
        PrepareField(textInputField);

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

    /// <summary>
    /// Stops TextMeshPro eating a backslash as you type.
    ///
    /// TMP_Text processes escape sequences in the text it is given - "\\" becomes one backslash,
    /// the same as in C# source. Harmless in a label. In an INPUT FIELD it means pressing the
    /// backslash key twice leaves one character behind, and a UNC path like
    ///
    ///     \\SERVER01\SharedDocs
    ///
    /// simply cannot be typed. Steps 19 and 25 were unpassable because of it.
    ///
    /// It is NOT the Rich Text setting - that governs &lt;b&gt; markup and switching it off changes
    /// nothing here. The one that matters is Parse Escape Characters, under Extra Settings on the
    /// text component, serialized as m_parseCtrlCharacters. Setting it in code as well as in the
    /// Inspector means a rebuilt or duplicated panel cannot quietly reintroduce the bug.
    ///
    /// IF THIS LINE WILL NOT COMPILE in your TextMeshPro version, delete this whole method and
    /// its four calls, then untick Parse Escape Characters by hand on each input field's
    /// Text (TMP) child. The effect is identical - this only makes it automatic.
    /// </summary>
    private static void PrepareField(TMP_InputField field)
    {
        if (field == null || field.textComponent == null) return;

        field.textComponent.parseCtrlCharacters = false;
    }

    /// <summary>Makes this step panel visible and interactable.</summary>
    public void Show()
    {
        gameObject.SetActive(true);
        Group.alpha = 1f;
        Group.interactable = true;
        Group.blocksRaycasts = true;
    }

    /// <summary>Hides this step panel (used when moving to the next/previous step).</summary>
    public void Hide()
    {
        Group.alpha = 0f;
        Group.interactable = false;
        Group.blocksRaycasts = false;
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
        typed = Normalize(typed);
        expected = Normalize(expected);

        return caseSensitive
            ? typed == expected
            : string.Equals(typed, expected, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Trims, and collapses any run of consecutive backslashes down to one.
    ///
    /// WHY A UNC PATH CANNOT OTHERWISE BE ANSWERED
    /// TextMeshPro treats "\\" as an escape sequence for a single backslash, and does so inside
    /// TMP_InputField as well as in a label. Press the backslash key twice and only one character
    /// reaches the field - it is not a display quirk, the second keystroke never lands in .text.
    /// Turning off Rich Text on the field does not stop it.
    ///
    /// That makes steps 19 and 25 impossible to complete, because both answers begin with the
    /// "\\" of a UNC path. No amount of correct typing can produce the stored expected value.
    ///
    /// Collapsing runs on BOTH sides makes the two agree however many backslashes survived:
    ///
    ///     expected  \\SERVER01\SharedDocs  ->  \SERVER01\SharedDocs
    ///     typed      \SERVER01\SharedDocs  ->  \SERVER01\SharedDocs      match
    ///
    /// The trade is honest and worth naming: a single leading backslash is not a valid UNC path,
    /// and this now accepts one. The alternative is a step nobody can pass. The instruction and
    /// the hint still teach the correct "\\" form, so what the learner is TOLD stays right - only
    /// the grading tolerates what the engine will physically let them type.
    ///
    /// Nothing else in COC 3 changes: steps 12 and 23 use paths like C:\ServerData\Users, which
    /// contain no consecutive backslashes and so pass through untouched.
    /// </summary>
    private static string Normalize(string value)
    {
        value = (value ?? string.Empty).Trim();
        if (value.IndexOf('\\') < 0) return value;

        var sb = new System.Text.StringBuilder(value.Length);
        bool previousWasBackslash = false;

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];

            if (c == '\\')
            {
                if (previousWasBackslash) continue;   // swallow the repeat
                previousWasBackslash = true;
            }
            else
            {
                previousWasBackslash = false;
            }

            sb.Append(c);
        }

        return sb.ToString();
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