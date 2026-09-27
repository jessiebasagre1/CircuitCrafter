using System.Collections.Generic;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The job order for the OS installation: a reference sheet the learner can open at any point
/// during the install, listing the value expected at each stage.
///
/// ATTACH THIS TO: the job order panel on the OS install screen's canvas. The builder menu item
/// (CircuitCrafter > Build OS Install Job Order) makes that panel and wires every field, so there
/// is normally nothing to do by hand.
///
/// ---------------------------------------------------------------------------------------------
/// IT IS GENERATED FROM THE STAGE ASSETS, NOT TYPED OUT
///
/// Every value comes from OSInstallStageData.correctChoice - the same field OSInstallManager
/// checks the learner's answer against. So the job order and the game cannot disagree: change the
/// correct edition on the asset and the job order says the new one, with nothing to remember.
///
/// A job order maintained by hand is worse than none at all. A learner who follows it and is
/// marked wrong does not conclude that the sheet is out of date - they conclude they do not
/// understand the procedure, and that is the opposite of what this is for.
///
/// STAGES WITH NO CORRECT ANSWER ARE SAID TO BE FREE CHOICES rather than left out. Language is
/// genuinely the learner's to pick; printing nothing beside it would read as an omission, and they
/// would go looking for the answer they were meant to use.
/// ---------------------------------------------------------------------------------------------
///
/// WHY IT READS THE STAGE LIST BY REFLECTION
/// OSInstallManager keeps its stages list [SerializeField] private, and it is the learner's file -
/// this must not require editing it to add a property. StoryRunRecorder already reads
/// StoryCompleteTrigger's task list the same way for the same reason. If the field is ever renamed
/// this says so in the Console rather than quietly showing an empty sheet.
/// </summary>
[DisallowMultipleComponent]
public class OSInstallJobOrder : MonoBehaviour
{
    [Header("Wiring")]
    [Tooltip("The panel shown and hidden. Leave empty to use this object.")]
    [SerializeField] private GameObject panel;

    [Tooltip("Where the job order is written. Required - without it there is nothing to show.")]
    [SerializeField] private TMP_Text bodyText;

    [Tooltip("Opens the panel. Optional if you open it some other way.")]
    [SerializeField] private Button openButton;

    [SerializeField] private Button closeButton;

    [Header("Header")]
    [SerializeField] private string title = "JOB ORDER - Operating System Installation";

    [Tooltip("The line under the title. Written as a work instruction, because that is what a job " +
             "order is in the workplace this is training for.")]
    [TextArea(2, 4)]
    [SerializeField] private string instruction =
        "Install the operating system on the unit below using the settings specified. " +
        "Follow the sequence. Do not proceed past a step until it is correct.";

    [Header("Behaviour")]
    [Tooltip("ON: the sheet is rebuilt every time the panel opens. Costs nothing and means a stage " +
             "asset edited while the game runs shows up immediately.")]
    [SerializeField] private bool rebuildOnOpen = true;

    [Tooltip("ON: start hidden, even if the panel was left visible in the scene.")]
    [SerializeField] private bool startHidden = true;

    [Header("Look")]
    [SerializeField] private string valueColour = "#46C8F5";
    [SerializeField] private string freeChoiceColour = "#A9C6F0";
    [SerializeField] private string numberColour = "#FFC93C";

    private GameObject Panel => panel != null ? panel : gameObject;

    // ==================================================================

    private void Awake()
    {
        if (openButton != null) openButton.onClick.AddListener(Open);
        if (closeButton != null) closeButton.onClick.AddListener(Close);

        // Awake does not run on an object that starts inactive, so a panel hidden in the scene
        // would never wire its own close button. Whoever opens it first is what wakes this, and by
        // then the listeners above are attached - which is why the hiding happens here rather than
        // the panel being left switched off in the scene.
        if (startHidden && Panel != gameObject) Panel.SetActive(false);
    }

    private void Start()
    {
        // Start, not Awake: OSInstallManager assigns its Instance in Awake, and building the sheet
        // before that would find nothing and report a missing manager that is right there.
        Rebuild();
    }

    // ==================================================================

    public void Open()
    {
        if (rebuildOnOpen) Rebuild();
        Panel.SetActive(true);
    }

    public void Close() => Panel.SetActive(false);

    public void Toggle()
    {
        if (Panel.activeSelf) Close();
        else Open();
    }

    /// <summary>Rewrites the sheet from the stage assets currently on the manager.</summary>
    [ContextMenu("Rebuild now")]
    public void Rebuild()
    {
        if (bodyText == null)
        {
            Debug.LogWarning("[OSInstallJobOrder] No Body Text assigned - there is nowhere to " +
                             "write the job order. Assign a TMP text, or run " +
                             "CircuitCrafter > Build OS Install Job Order.", this);
            return;
        }

        bodyText.text = Compose();
    }

    /// <summary>Prints the sheet to the Console, so it can be checked without entering Play.</summary>
    [ContextMenu("Preview in Console")]
    public void Preview()
    {
        Debug.Log("[OSInstallJobOrder] Preview:\n\n" + Compose(), this);
    }

    // ==================================================================

    private string Compose()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"<b>{title}</b>");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(instruction))
        {
            sb.AppendLine(instruction.Trim());
            sb.AppendLine();
        }

        List<OSInstallStageData> stages = ReadStages(out string problem);

        if (stages == null)
        {
            sb.AppendLine(problem);
            return sb.ToString();
        }

        int number = 0;

        foreach (OSInstallStageData stage in stages)
        {
            if (stage == null) continue;

            // Only the stages the learner actually answers. Copying, Installing and Reboot are
            // things the machine does while they wait; listing them as work items would pad the
            // sheet with lines nobody can act on and bury the nine that matter.
            if (!stage.requiresUserInput) continue;

            number++;

            string label = Humanise(stage.stage.ToString());

            string value = string.IsNullOrWhiteSpace(stage.correctChoice)
                ? $"<color={freeChoiceColour}>your choice</color>"
                : $"<color={valueColour}><b>{stage.correctChoice}</b></color>";

            sb.AppendLine($"<color={numberColour}>{number}.</color>  {label}   -   {value}");
        }

        if (number == 0)
        {
            sb.AppendLine("No stages on this machine ask the learner for a value, so there is " +
                          "nothing to specify. Tick Requires User Input on the stage assets that " +
                          "should be answered.");
        }

        return sb.ToString();
    }

    /// <summary>
    /// The manager's stage list, or null with a sentence explaining why not.
    ///
    /// Every failure returns a sentence rather than an empty sheet, because an empty job order and
    /// a broken one look identical to a learner and nearly identical to whoever is debugging it.
    /// </summary>
    private List<OSInstallStageData> ReadStages(out string problem)
    {
        problem = "";

        OSInstallManager manager = OSInstallManager.Instance;
        if (manager == null) manager = FindAnyObjectByType<OSInstallManager>(FindObjectsInactive.Include);

        if (manager == null)
        {
            problem = "No OSInstallManager in this scene, so there is no install sequence to " +
                      "describe.";
            return null;
        }

        FieldInfo field = typeof(OSInstallManager).GetField(
            "stages", BindingFlags.Instance | BindingFlags.NonPublic);

        if (field == null)
        {
            problem = "OSInstallManager no longer has a field called 'stages' - it has been " +
                      "renamed. Update ReadStages in OSInstallJobOrder to match.";
            return null;
        }

        var stages = field.GetValue(manager) as List<OSInstallStageData>;

        if (stages == null || stages.Count == 0)
        {
            problem = "The OSInstallManager's Stage Data list is empty, so the install has no " +
                      "steps to specify.";
            return null;
        }

        return stages;
    }

    /// <summary>"AccountSetup" becomes "Account Setup", "ProductKey" becomes "Product Key".</summary>
    private static string Humanise(string enumName)
    {
        if (string.IsNullOrEmpty(enumName)) return "";

        var sb = new StringBuilder();

        for (int i = 0; i < enumName.Length; i++)
        {
            char c = enumName[i];

            // A space before a capital, except the first - and not before a capital that follows
            // another capital, so an acronym like POST or BIOS stays one word rather than becoming
            // "P O S T".
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(enumName[i - 1])) sb.Append(' ');

            sb.Append(c);
        }

        return sb.ToString();
    }
}
