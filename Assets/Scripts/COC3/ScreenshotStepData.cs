using UnityEngine;

/// <summary>
/// The different kinds of interaction a step can require from the learner.
/// Add new types here if you need new behaviours later.
/// </summary>
public enum StepType
{
    Informational,      // Just shows a screenshot + instructions, learner clicks "Next"
    NetworkConfigInput, // Learner types IP / Subnet Mask / Gateway into TMP_InputFields
    PingTest,           // A simulated "ping" command that succeeds/fails based on the previous step
    ButtonInteraction,  // Learner must click a transparent button placed over the screenshot
    TextInput           // Learner types ONE free-text value (e.g. Computer Name, DNS Server, Username)
}

/// <summary>
/// Holds ALL the data for a single step in the simulation:
/// the screenshot to show, the instructions, what "correct" looks like, and scoring.
///
/// This is a ScriptableObject, which means each step lives as its own Asset
/// in your Project window (Right-click -> Create -> COC3 -> Screenshot Step Data).
/// That makes it very easy to add, remove, or reorder steps without touching code,
/// and to reuse the whole system for a different CSS NC II competency later
/// (just create new StepData assets with different screenshots/values).
/// </summary>
[CreateAssetMenu(fileName = "NewScreenshotStep", menuName = "COC3/Screenshot Step Data")]
public class ScreenshotStepData : ScriptableObject
{
    [Header("Step Identity")]
    [Tooltip("A short unique name for this step, used in logs/reports (e.g. 'Step_04_IPv4Properties').")]
    public string stepId = "Step_01";

    [Tooltip("Human-readable instructions shown above the screenshot.")]
    [TextArea(2, 5)]
    public string instructionText;

    [Tooltip("GUIDANCE: what to show INSTEAD during an assessment. State the requirement, never the " +
             "clicks - e.g. 'Assign the server its static IP address (see the job order).' Leave blank to " +
             "show the scene's generic assessment line (COC3 steps use the job order's specifications).")]
    [TextArea(1, 3)]
    public string assessmentPrompt;

    [Header("Visual")]
    [Tooltip("The screenshot image to display for this step. Swap this to reuse the system for other topics.")]
    public Sprite screenshotImage;

    [Header("Step Type")]
    [Tooltip("Determines what kind of validation/interaction this step requires.")]
    public StepType stepType = StepType.Informational;

    [Header("Network Values (only used when Step Type = NetworkConfigInput)")]
    [Tooltip("The correct IP address the learner should type.")]
    public string expectedIP = "192.168.1.10";

    [Tooltip("The correct subnet mask the learner should type.")]
    public string expectedSubnetMask = "255.255.255.0";

    [Tooltip("The correct default gateway the learner should type.")]
    public string expectedGateway = "192.168.1.1";

    [Header("Text Input (only used when Step Type = TextInput)")]
    [Tooltip("What this field represents, e.g. 'Computer Name', 'DNS Server', 'Username'. Shown as the input field's placeholder text.")]
    public string textFieldLabel = "Value";

    [Tooltip("The exact value the learner should type for this step to be marked correct.")]
    public string textFieldExpectedValue = "";

    [Tooltip("If checked, the learner's answer must match capitalization exactly. Leave unchecked for things like usernames or computer names, which are normally not case-sensitive in Windows.")]
    public bool textFieldCaseSensitive = false;

    [Header("Scoring")]
    [Tooltip("Points awarded when this step is completed correctly.")]
    public int pointsForCorrect = 10;

    [Tooltip("Points deducted from the score each time the learner gets this step wrong.")]
    public int pointsPenaltyForMistake = 2;
}