using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// Temporary diagnostic for a TextInput step that refuses a correct-looking answer.
///
/// ATTACH THIS TO: any always-active object in the COC 3 scene. It announces itself in Start(),
/// so if you do not see that line it is not running and nothing else it says matters.
///
/// HOW TO USE IT
///   1. Play, reach step 19, type the answer exactly as the hint shows it.
///   2. BEFORE pressing submit, press K.
///   3. Paste the Console block back.
///
/// WHY CHARACTER CODES RATHER THAN THE STRINGS
/// Both sides of this comparison LOOK identical on screen and are not. TextMeshPro un-escapes
/// "\\" to "\" when it renders, so a field holding two backslashes displays one - and a field
/// holding one displays one as well. No amount of staring at the screen separates those two
/// cases. Printing every character as a hex code does, immediately.
///
/// It also reports the input field's own settings, because Content Type and Character Limit can
/// each silently mangle a value on the way in: Alphanumeric drops backslashes entirely, and a
/// limit of 20 truncates the 21-character UNC paths in steps 19 and 25 while leaving every
/// shorter answer in the module working.
/// </summary>
public class COC3InputProbe : MonoBehaviour
{
    [Tooltip("Leave empty to find the COC3Manager in the scene.")]
    [SerializeField] private COC3Manager manager;

    [Tooltip("Press this to dump a report for whichever step is on screen.")]
    [SerializeField] private KeyCode reportKey = KeyCode.K;

    private void Start()
    {
        if (manager == null) manager = FindAnyObjectByType<COC3Manager>();

        Debug.LogWarning($"[COC3InputProbe] ACTIVE. Type your answer, then press {reportKey}. " +
                         (manager == null ? "NO COC3Manager FOUND." : "Manager found."));
    }

    private void Update()
    {
        if (Input.GetKeyDown(reportKey)) Report();
    }

    private void Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== COC3InputProbe ===");

        if (manager == null || manager.steps == null)
        {
            sb.AppendLine("No COC3Manager / no steps.");
            Debug.LogWarning(sb.ToString());
            return;
        }

        ScreenshotStep step = ActiveStep();

        if (step == null)
        {
            sb.AppendLine("No step is currently active. Reach the step first, then press the key.");
            Debug.LogWarning(sb.ToString());
            return;
        }

        var data = step.stepData;

        sb.AppendLine($"Step object : {step.name}");
        sb.AppendLine($"Step data   : {(data != null ? data.name : "NULL")}");
        sb.AppendLine($"stepId      : {(data != null ? data.stepId : "-")}");
        sb.AppendLine($"stepType    : {(data != null ? data.stepType.ToString() : "-")}");

        if (data == null)
        {
            Debug.LogWarning(sb.ToString());
            return;
        }

        if (data.stepType != StepType.TextInput)
        {
            sb.AppendLine();
            sb.AppendLine("NOT a TextInput step, so Validate() returns without comparing anything. " +
                          "If you expected a typed answer to be graded here, the Step Type on the " +
                          "asset is the bug.");
            Debug.LogWarning(sb.ToString());
            return;
        }

        // ---- the input field itself ----
        var field = step.textInputField;

        sb.AppendLine();
        sb.AppendLine("--- input field ---");

        if (field == null)
        {
            sb.AppendLine("textInputField is NOT ASSIGNED on this ScreenshotStep. Validate() then " +
                          "compares an empty string and nothing can ever pass.");
            Debug.LogWarning(sb.ToString());
            return;
        }

        sb.AppendLine($"  object            : {field.name}");
        sb.AppendLine($"  contentType       : {field.contentType}"
                      + (field.contentType == TMP_InputField.ContentType.Standard
                          ? ""
                          : "   <-- NOT Standard. Alphanumeric and friends drop backslashes."));
        sb.AppendLine($"  characterValidation: {field.characterValidation}");
        sb.AppendLine($"  characterLimit    : {field.characterLimit}"
                      + (field.characterLimit != 0 && field.characterLimit < 25
                          ? "   <-- the UNC answers are 21 characters"
                          : ""));
        sb.AppendLine($"  lineType          : {field.lineType}");
        sb.AppendLine($"  richText (display): {(field.textComponent != null ? field.textComponent.richText.ToString() : "no text component")}");

        // ---- the two strings, character by character ----
        string typed = field.text ?? string.Empty;
        string expected = data.textFieldExpectedValue ?? string.Empty;

        sb.AppendLine();
        sb.AppendLine("--- what you typed ---");
        Describe(sb, typed);

        sb.AppendLine();
        sb.AppendLine("--- what the asset expects ---");
        Describe(sb, expected);

        // ---- the verdict, using the exact same rule ScreenshotStep uses ----
        string t = typed.Trim();
        string e = expected.Trim();

        bool pass = data.textFieldCaseSensitive
            ? t == e
            : string.Equals(t, e, System.StringComparison.OrdinalIgnoreCase);

        sb.AppendLine();
        sb.AppendLine("--- verdict ---");
        sb.AppendLine($"  caseSensitive : {data.textFieldCaseSensitive}");
        sb.AppendLine($"  would pass    : {pass}");

        if (!pass)
        {
            int i = FirstDifference(t, e);
            sb.AppendLine($"  first difference at index {i}:");
            sb.AppendLine($"     typed[{i}]    = {At(t, i)}");
            sb.AppendLine($"     expected[{i}] = {At(e, i)}");
        }

        Debug.LogWarning(sb.ToString());
    }

    /// <summary>
    /// The step the learner is looking at. currentStepIndex is private on COC3Manager, so this
    /// finds the one panel that is switched on - the same approach COC3Tutorial uses.
    /// </summary>
    private ScreenshotStep ActiveStep()
    {
        for (int i = 0; i < manager.steps.Count; i++)
        {
            var s = manager.steps[i];
            if (s != null && s.gameObject.activeInHierarchy) return s;
        }
        return null;
    }

    private static void Describe(StringBuilder sb, string value)
    {
        sb.AppendLine($"  length : {value.Length}");
        sb.AppendLine($"  shown  : {value}");

        var codes = new StringBuilder();
        for (int i = 0; i < value.Length; i++)
        {
            if (i > 0) codes.Append(' ');
            char c = value[i];
            codes.Append(c == '\\' ? "\\(5C)" : $"{c}({(int)c:X2})");
        }

        sb.AppendLine($"  codes  : {codes}");
    }

    private static int FirstDifference(string a, string b)
    {
        int n = Mathf.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++)
            if (char.ToLowerInvariant(a[i]) != char.ToLowerInvariant(b[i])) return i;

        return n;
    }

    private static string At(string s, int i)
    {
        if (i >= s.Length) return "(past the end - this string is shorter)";
        return $"'{s[i]}'  (0x{(int)s[i]:X2})";
    }
}
